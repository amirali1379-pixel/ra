using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using grapher.Models.Mouse;
using grapher.DesktopSens.Models;
using OpenTK;

namespace grapher.DesktopSens.Views
{
    public class DesktopSensTrainerForm : Form
    {
        private readonly DesktopSensProfile _profile;
        private readonly List<MouseSample> _rawSamples = new List<MouseSample>();
        private readonly Stopwatch _trialStopwatch = new Stopwatch();
        private readonly Timer _cooldownTimer;
        private readonly DesktopSensArena3DControl _arena3D;
        private readonly Label _hud;
        private readonly Label[] _degreeLabels = new Label[36];
        private readonly Label _selectedAngleLabel;

        private enum TrialState { SetStartPoint, SetTargetPoint, Cooldown, Flicking, ASDWAdjusting, TrialComplete }
        private TrialState _state = TrialState.SetStartPoint;

        private PointF _startPos;
        private PointF _targetPos;
        private PointF _currentCursorPos;
        private PointF _rawLandedPos;
        private double _targetYawDegrees;
        private double _targetDistanceCm;
        private double _targetPitchDegrees;
        private double _landedYawDegrees;
        private double _landedPitchDegrees;
        // The circular trainer radius represents exactly a 180-degree CS2 turn.
        // The conversion depends on the current mouse DPI and CS2 sensitivity.
        private readonly double _maxDistanceCm;
        private readonly float _pixelsPerCm;
        private readonly int[] _angleZoneCounts = new int[DesktopSensConstants.AngularZoneCount];
        private readonly int[] _distanceZoneCounts = new int[DesktopSensConstants.DistanceZoneCount];
        private float _cameraYawDegrees;
        private float _calibrationYawDegrees;
        private float _calibrationPitchDegrees;
        private Vector3 _calibrationOrigin;
        private Vector3 _calibrationForward = new Vector3(0f, 0f, -1f);
        private Vector3 _calibrationRight = Vector3.UnitX;
        private Vector3 _calibrationUp = Vector3.UnitY;
        private const float CalibrationPlaneDistance = 18f;
        private float _cameraPitchDegrees = 18f;
        private bool _cameraDragging;
        private Point _cameraDragOrigin;
        private float _cameraDragYaw;
        private float _cameraDragPitch;

        [DllImport("user32.dll")]
        private static extern bool SetCursorPos(int X, int Y);

        public DesktopSensTrainerForm(DesktopSensProfile profile)
        {
            _profile = profile ?? throw new ArgumentNullException(nameof(profile));
            if (_profile.MouseDPI < DesktopSensConstants.MinMouseDPI ||
                _profile.MouseDPI > DesktopSensConstants.MaxMouseDPI)
                throw new ArgumentOutOfRangeException(nameof(profile), "Mouse DPI is outside the supported range.");

            _maxDistanceCm = ZoneCalculations.MaxDistanceCmForTurnDegrees(
                _profile.MouseDPI, _profile.UserInputGameSensitivity);
            _targetDistanceCm = Math.Max(0.5, _maxDistanceCm * 0.5);
            _pixelsPerCm = (float)(Math.Min(Screen.PrimaryScreen.Bounds.Width, Screen.PrimaryScreen.Bounds.Height) /
                (2.0 * _maxDistanceCm));

            FormBorderStyle = FormBorderStyle.None;
            WindowState = FormWindowState.Maximized;
            BackColor = Color.FromArgb(24, 26, 29);
            DoubleBuffered = true;
            KeyPreview = true;
            _arena3D = new DesktopSensArena3DControl { Dock = DockStyle.Fill, Radius = (float)_maxDistanceCm };
            Controls.Add(_arena3D);
            _arena3D.SendToBack();
            _hud = new Label { AutoSize = true, Location = new Point(20, 20), Padding = new Padding(10),
                BackColor = Color.FromArgb(220, 12, 16, 22), ForeColor = Color.Gainsboro,
                Font = new Font("Segoe UI", 11f, FontStyle.Bold) };
            Controls.Add(_hud);
            // Screen-space compass labels make every 10-degree azimuth explicit.
            for (int i = 0; i < _degreeLabels.Length; i++)
            {
                int degree = i * 10;
                var label = new Label
                {
                    AutoSize = false,
                    Size = new Size(42, 20),
                    TextAlign = ContentAlignment.MiddleCenter,
                    Text = degree + "°",
                    Font = new Font("Segoe UI", degree % 30 == 0 ? 9f : 8f,
                        degree % 30 == 0 ? FontStyle.Bold : FontStyle.Regular),
                    ForeColor = degree == 0 ? Color.LightGreen :
                        degree % 30 == 0 ? Color.Khaki : Color.Gainsboro,
                    BackColor = Color.FromArgb(190, 12, 16, 22),
                    TabStop = false
                };
                _degreeLabels[i] = label;
                Controls.Add(label);
            }
            _selectedAngleLabel = new Label
            {
                AutoSize = true,
                Padding = new Padding(6, 3, 6, 3),
                BackColor = Color.FromArgb(225, 90, 42, 12),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 10f, FontStyle.Bold),
                Text = "TARGET POINT: 0.0°"
            };
            Controls.Add(_selectedAngleLabel);
            _hud.BringToFront();
            _arena3D.MouseDown += (s, e) => OnMouseDown(e);
            _arena3D.MouseMove += (s, e) => OnMouseMove(e);
            _arena3D.MouseUp += (s, e) => OnMouseUp(e);
            _arena3D.MouseWheel += (s, e) =>
            {
                if (_state == TrialState.SetTargetPoint)
                {
                    double step = Math.Max(0.25, _maxDistanceCm * 0.025);
                    _targetDistanceCm = Math.Max(0.5, Math.Min(_maxDistanceCm,
                        _targetDistanceCm + (e.Delta > 0 ? step : -step)));
                }
                else
                {
                    _cameraPitchDegrees = Math.Max(5f, Math.Min(65f,
                        _cameraPitchDegrees + (e.Delta > 0 ? 2f : -2f)));
                }
                UpdateArena3D();
            };
            Cursor.Hide();

            _startPos = new PointF(ClientSize.Width / 2f, ClientSize.Height / 2f);
            _currentCursorPos = _startPos;
            MoveSystemCursorToCanvas();
            UpdateArena3D();

            _cooldownTimer = new Timer { Interval = 3000 };
            _cooldownTimer.Tick += OnCooldownFinished;

            FormClosed += (s, e) =>
            {
                _cooldownTimer.Stop();
                _cooldownTimer.Dispose();
                Cursor.Show();
            };
        }

        public void OnMouseSample(MouseSample sample)
        {
            // Before the start point is selected, let the real OS cursor move normally.
            // Do not warp it to the virtual cursor: that pins the pointer at the center
            // and makes it impossible to choose the start point with the mouse.
            // Freeze samples during cooldown/correction/completion so they cannot contaminate
            // the measured flick.
            if (_state == TrialState.SetStartPoint ||
                _state == TrialState.Cooldown ||
                _state == TrialState.ASDWAdjusting ||
                _state == TrialState.TrialComplete)
                return;

            // Raw input is in device counts. Convert counts -> physical cm -> canvas pixels.
            double scale = _pixelsPerCm * DesktopSensConstants.InchesToCm / _profile.MouseDPI;
            _currentCursorPos.X += (float)(sample.DX * scale);
            _currentCursorPos.Y += (float)(sample.DY * scale);

            // Raw input rotates the FPS view. _maxDistanceCm is calibrated as a 180-degree
            // turn, so this conversion preserves the profile's DPI/sensitivity scale.
            double cmPerCount = DesktopSensConstants.InchesToCm / _profile.MouseDPI;
            float degreesPerCount = (float)(180.0 * cmPerCount / Math.Max(0.001, _maxDistanceCm));
            _cameraYawDegrees += sample.DX * degreesPerCount;
            _cameraPitchDegrees = Math.Max(-20f, Math.Min(35f,
                _cameraPitchDegrees - sample.DY * degreesPerCount));
            if (_state == TrialState.Flicking)
                SyncCursorPositionToCamera();
            ClampCursorToCanvas();

            if (_state == TrialState.Flicking)
                _rawSamples.Add(sample);

            // FPS-style relative mouse look: keep the hidden OS pointer at the monitor center
            // during target selection and flicking, so physical cursor bounds never become
            // the origin of rotation. Raw Input deltas, not the OS cursor position, drive yaw.
            if (_state == TrialState.SetTargetPoint || _state == TrialState.Flicking)
                RecenterSystemCursor();
            else
                MoveSystemCursorToCanvas();
            UpdateArena3D();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                _cameraDragging = true;
                _cameraDragOrigin = e.Location;
                _cameraDragYaw = _cameraYawDegrees;
                _cameraDragPitch = _cameraPitchDegrees;
                _arena3D.Capture = true;
                return;
            }
            if (e.Button != MouseButtons.Left) return;
            base.OnMouseDown(e);
            var clickedPoint = new PointF(
                Math.Max(0, Math.Min(ClientSize.Width - 1, e.X)),
                Math.Max(0, Math.Min(ClientSize.Height - 1, e.Y)));

            if (_state == TrialState.SetStartPoint)
            {
                // The start circle is fixed in world space on the calibration plane.
                _startPos = new PointF(ClientSize.Width / 2f, ClientSize.Height / 2f);
                CaptureCalibrationPlane();
                _currentCursorPos = _startPos;
                // Keep the OS pointer hidden. The centered crosshair selects the target;
                // raw mouse input rotates the view until the desired direction is under it.
                Cursor.Hide();
                _calibrationYawDegrees = _cameraYawDegrees;
                _calibrationPitchDegrees = _cameraPitchDegrees;
                _state = TrialState.SetTargetPoint;
            }
            else if (_state == TrialState.SetTargetPoint)
            {
                // The crosshair stays at screen center. Its direction relative to the view
                // captured at Start defines the target angle; ignore the OS click coordinates.
                // Store the target as angles, not only as a 2D point. A screen coordinate
                // repeats every 360 degrees, so it cannot uniquely represent multiple turns.
                _targetYawDegrees = NormalizeAngle(_cameraYawDegrees - _calibrationYawDegrees);
                _targetPitchDegrees = _cameraPitchDegrees - _calibrationPitchDegrees;
                double maxArenaRadiusPx = Math.Max(1.0, Math.Min(ClientSize.Width, ClientSize.Height) * 0.42);
                double arenaRadiusPx = maxArenaRadiusPx * (_targetDistanceCm / Math.Max(0.001, _maxDistanceCm));
                double targetYawRadians = _targetYawDegrees * Math.PI / 180.0;
                _targetPos = new PointF(
                    _startPos.X + (float)(Math.Sin(targetYawRadians) * arenaRadiusPx),
                    _startPos.Y - (float)(Math.Cos(targetYawRadians) * arenaRadiusPx));
                _arena3D.TargetPoint = WorldPointFromAngles(_targetYawDegrees, _targetPitchDegrees, _targetDistanceCm);
                _arena3D.ShowTarget = true;

                // Accept every selected direction, including targets very close to 0°.
                // This is an angular trainer; target selection must not be blocked by a
                // distance threshold or the old 180-degree turn-distance model.
                _rawSamples.Clear();
                _currentCursorPos = _startPos;
                // Restore the exact start view so the fixed centre crosshair sits on the start marker.
                _cameraYawDegrees = _calibrationYawDegrees;
                _cameraPitchDegrees = _calibrationPitchDegrees;
                Cursor.Hide();
                MoveSystemCursorToCanvas();
                _state = TrialState.Cooldown;
                _cooldownTimer.Stop();
                _cooldownTimer.Start();
            }
            else if (_state == TrialState.Flicking)
            {
                // Preserve the actual mouse-flick landing before keyboard correction. Sens/accel
                // learn from this raw landing; rotation must not be hidden by the ASDW adjustment.
                _rawLandedPos = _currentCursorPos;
                _landedYawDegrees = NormalizeAngle(_cameraYawDegrees - _calibrationYawDegrees);
                _landedPitchDegrees = _cameraPitchDegrees - _calibrationPitchDegrees;
                _state = TrialState.ASDWAdjusting;
            }
            else if (_state == TrialState.TrialComplete)
            {
                _state = TrialState.SetStartPoint;
                Cursor.Hide();
                _rawSamples.Clear();
                _startPos = PointF.Empty;
                _targetPos = PointF.Empty;
                _arena3D.ShowStart = false;
                _arena3D.ShowTarget = false;
            }

            UpdateArena3D();
        }

        private void OnCooldownFinished(object sender, EventArgs e)
        {
            _cooldownTimer.Stop();
            // Preserve the start view as the exact origin of the flick after the 3-second lock.
            _cameraYawDegrees = _calibrationYawDegrees;
            _cameraPitchDegrees = _calibrationPitchDegrees;
            _currentCursorPos = _startPos;
            _state = TrialState.Flicking;
            _rawSamples.Clear();
            _trialStopwatch.Restart();
            UpdateArena3D();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape)
            {
                _cooldownTimer.Stop();
                Close();
                return;
            }

            if (_state == TrialState.ASDWAdjusting)
            {
                // WASD adjusts the view, so the centre crosshair actually moves over the 3D target.
                const float adjustmentSpeedDegrees = 0.35f;
                if (e.KeyCode == Keys.W) _cameraPitchDegrees = Math.Min(35f, _cameraPitchDegrees + adjustmentSpeedDegrees);
                if (e.KeyCode == Keys.S) _cameraPitchDegrees = Math.Max(-20f, _cameraPitchDegrees - adjustmentSpeedDegrees);
                if (e.KeyCode == Keys.A) _cameraYawDegrees -= adjustmentSpeedDegrees;
                if (e.KeyCode == Keys.D) _cameraYawDegrees += adjustmentSpeedDegrees;

                SyncCursorPositionToCamera();
                ClampCursorToCanvas();

                if (e.KeyCode == Keys.Space)
                    FinishTrial();

                e.Handled = true;
                e.SuppressKeyPress = true;
                UpdateArena3D();
            }
        }

        private void SyncCursorPositionToCamera()
        {
            // Keep the landing cursor aligned with the actual view angles. The previous
            // yaw-only circle projection changed both X and Y, so a purely horizontal
            // mouse movement made the cursor drift down/up around the circle.
            // Yaw controls horizontal position; relative pitch controls vertical position.
            double yawRadians = NormalizeSignedAngle(_cameraYawDegrees - _calibrationYawDegrees) * Math.PI / 180.0;
            double pitchRadians = (_cameraPitchDegrees - _calibrationPitchDegrees) * Math.PI / 180.0;
            double arenaRadiusPx = Math.Max(1.0, Math.Min(ClientSize.Width, ClientSize.Height) * 0.42);
            _currentCursorPos = new PointF(
                _startPos.X + (float)(Math.Sin(yawRadians) * arenaRadiusPx),
                _startPos.Y - (float)(Math.Sin(pitchRadians) * arenaRadiusPx));
        }

        private void FinishTrial()
        {
            _trialStopwatch.Stop();
            _state = TrialState.TrialComplete;

            // Compare angular measurements directly. Do not derive them from the projected
            // screen points: those points alias after a full turn and corrupt 360-degree trials.
            double targetAngle = NormalizeAngle(_targetYawDegrees);
            double flickAngle = NormalizeAngle(_landedYawDegrees);
            double signedRotationError = NormalizeSignedAngle(targetAngle - flickAngle);
            double targetDistanceCm = _targetDistanceCm;
            double flickDistanceCm = CalculateFlickTravelCm();
            double signedErrorCm = flickDistanceCm - targetDistanceCm;
            double angularErrorCm = Math.Abs(signedRotationError) * _maxDistanceCm / 180.0;
            double finalErrorCm = Math.Sqrt(angularErrorCm * angularErrorCm + signedErrorCm * signedErrorCm);
            double projectedDistancePx = (targetDistanceCm + signedErrorCm) * _pixelsPerCm;
            double actualDistancePx = flickDistanceCm * _pixelsPerCm;
            double peakVelocity = CalculatePeakVelocity();
            var trial = new DesktopSensTrial
            {
                StartX = _startPos.X,
                StartY = _startPos.Y,
                TargetX = _targetPos.X,
                TargetY = _targetPos.Y,
                TargetAngleDegrees = targetAngle,
                TargetDistanceCm = targetDistanceCm,
                RawSamples = new List<MouseSample>(_rawSamples),
                LandedX = _currentCursorPos.X,
                LandedY = _currentCursorPos.Y,
                FinalErrorCm = finalErrorCm,
                SignedErrorCm = signedErrorCm,
                ProjectedDistanceCm = projectedDistancePx / _pixelsPerCm,
                IsUnderFlick = signedErrorCm < 0.0,
                IsOverFlick = signedErrorCm > 0.0,
                FlickAngleDegrees = flickAngle,
                SignedRotationErrorDegrees = signedRotationError,
                HasMovementMeasurement = actualDistancePx >= 0.25 * _pixelsPerCm,
                HasRotationMeasurement = actualDistancePx >= 0.25 * _pixelsPerCm,
                FlickDistanceCm = flickDistanceCm,
                DurationMs = _trialStopwatch.Elapsed.TotalMilliseconds,
                PeakVelocityPxPerSec = peakVelocity,
                AngleZoneIndex = ZoneCalculations.AngleToZoneIndex(targetAngle),
                DistanceZoneIndex = ZoneCalculations.DistanceToZoneIndex(targetDistanceCm, _maxDistanceCm),
                CompletionMode = FlickCompletionMode.Click,
                CompletionTimestamp = _rawSamples.Count > 0 ? _rawSamples[_rawSamples.Count - 1].Timestamp : 0,
                MouseDPI = _profile.MouseDPI,
                GameSensitivity = _profile.UserInputGameSensitivity,
                EffectivePollingRateHz = _profile.PollingRateHz,
                CanvasPixelsPerCm = _pixelsPerCm
            };

            _profile.Trials.Add(trial);
            _angleZoneCounts[trial.AngleZoneIndex]++;
            _distanceZoneCounts[trial.DistanceZoneIndex]++;
            _profile.ModifiedTimestamp = DateTime.UtcNow;
            UpdateArena3D();
        }

        private double CalculateFlickTravelCm()
        {
            double totalCounts = 0.0;
            for (int i = 0; i < _rawSamples.Count; i++)
            {
                double dx = _rawSamples[i].DX;
                double dy = _rawSamples[i].DY;
                totalCounts += Math.Sqrt(dx * dx + dy * dy);
            }
            return totalCounts * DesktopSensConstants.InchesToCm / _profile.MouseDPI;
        }

        private double CalculatePeakVelocity()
        {
            if (_rawSamples.Count < 2) return 0.0;
            double peak = 0.0;
            double scale = _pixelsPerCm * DesktopSensConstants.InchesToCm / _profile.MouseDPI;
            for (int i = 1; i < _rawSamples.Count; i++)
            {
                long ticks = _rawSamples[i].Timestamp - _rawSamples[i - 1].Timestamp;
                if (ticks <= 0) continue;
                double seconds = ticks / (double)Stopwatch.Frequency;
                double dx = (_rawSamples[i].DX) * scale;
                double dy = (_rawSamples[i].DY) * scale;
                double velocity = Math.Sqrt(dx * dx + dy * dy) / seconds;
                if (velocity > peak) peak = velocity;
            }
            return peak;
        }

        private PointF ClampToTurnCircle(PointF point)
        {
            double dx = point.X - _startPos.X;
            double dy = point.Y - _startPos.Y;
            double distance = Math.Sqrt(dx * dx + dy * dy);
            double maxRadius = _maxDistanceCm * _pixelsPerCm;
            if (distance > maxRadius && distance > 0.0)
            {
                double scale = maxRadius / distance;
                point = new PointF(
                    _startPos.X + (float)(dx * scale),
                    _startPos.Y + (float)(dy * scale));
            }

            return point;
        }

        private void ClampCursorToCanvas()
        {
            _currentCursorPos.X = Math.Max(0, Math.Min(ClientSize.Width - 1, _currentCursorPos.X));
            _currentCursorPos.Y = Math.Max(0, Math.Min(ClientSize.Height - 1, _currentCursorPos.Y));
            if (_state != TrialState.SetStartPoint)
                _currentCursorPos = ClampToTurnCircle(_currentCursorPos);
        }

        private void MoveSystemCursorToCanvas()
        {
            Point screenPoint = PointToScreen(Point.Round(_currentCursorPos));
            SetCursorPos(screenPoint.X, screenPoint.Y);
        }

        private void RecenterSystemCursor()
        {
            // Recenter in client coordinates, accounting for the form's actual screen origin.
            Point center = PointToScreen(new Point(ClientSize.Width / 2, ClientSize.Height / 2));
            SetCursorPos(center.X, center.Y);
        }

        private static double Distance(PointF a, PointF b)
        {
            double dx = a.X - b.X;
            double dy = a.Y - b.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        private static double NormalizeAngle(double angle)
        {
            if (double.IsNaN(angle) || double.IsInfinity(angle)) return 0.0;
            angle %= 360.0;
            return angle < 0.0 ? angle + 360.0 : angle;
        }

        private static double NormalizeSignedAngle(double angle)
        {
            angle = NormalizeAngle(angle);
            return angle > 180.0 ? angle - 360.0 : angle;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (_cameraDragging)
            {
                _cameraYawDegrees = _cameraDragYaw + (e.X - _cameraDragOrigin.X) * 0.45f;
                _cameraPitchDegrees = Math.Max(5f, Math.Min(65f,
                    _cameraDragPitch + (e.Y - _cameraDragOrigin.Y) * 0.25f));
                UpdateArena3D();
                return;
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right && _cameraDragging)
            {
                _cameraDragging = false;
                _arena3D.Capture = false;
                return;
            }
            base.OnMouseUp(e);
        }

        private void UpdateArena3D()
        {
            if (_arena3D == null || _arena3D.IsDisposed) return;
            _arena3D.YawDegrees = _cameraYawDegrees;
            _arena3D.PitchDegrees = _cameraPitchDegrees;
            // The camera and markers must use the same calibration-relative basis.
            // Passing absolute yaw to the renderer made the crosshair and arena disagree
            // whenever calibration began at a non-zero view angle.
            _arena3D.ViewDirection = CameraDirectionFromCalibration();
            _arena3D.Radius = (float)_maxDistanceCm;
            _arena3D.StartPoint = _calibrationOrigin;
            _arena3D.TargetPoint = WorldPointFromAngles(_targetYawDegrees, _targetPitchDegrees, _targetDistanceCm);
            _arena3D.CalibrationRight = _calibrationRight;
            _arena3D.CalibrationForward = _calibrationForward;
            _arena3D.CalibrationUp = _calibrationUp;
            _arena3D.ShowStart = _state != TrialState.SetStartPoint;
            _arena3D.ShowTarget = _state == TrialState.Cooldown || _state == TrialState.Flicking ||
                _state == TrialState.ASDWAdjusting || _state == TrialState.TrialComplete;
            _arena3D.StatusText = _state.ToString();
            _arena3D.TrialCount = _profile.Trials.Count;
            string instruction;
            switch (_state)
            {
                case TrialState.SetStartPoint: instruction = "Click to begin  |  Right-drag: look around  |  ESC: exit"; break;
                case TrialState.SetTargetPoint: instruction = "Aim angle with mouse  |  Wheel: target distance " + _targetDistanceCm.ToString("0.0") + " cm  |  Left-click to place"; break;
                case TrialState.Cooldown: instruction = "Crosshair locked on start point · 3 seconds…"; break;
                case TrialState.Flicking: instruction = "Flick to target 2, then left-click to record landing"; break;
                case TrialState.ASDWAdjusting: instruction = "W/A/S/D to aim crosshair at target 2  |  SPACE to calibrate"; break;
                default: instruction = "Trial saved  |  Click to start the next trial"; break;
            }
            double relativeYaw = NormalizeAngle(_cameraYawDegrees - _calibrationYawDegrees);
            string angleReadout = _state == TrialState.SetTargetPoint
                ? "CROSSHAIR / TARGET ANGLE: " + relativeYaw.ToString("0.0") + "°"
                : (_state == TrialState.Cooldown || _state == TrialState.Flicking ||
                   _state == TrialState.ASDWAdjusting || _state == TrialState.TrialComplete
                    ? "TARGET: " + _targetYawDegrees.ToString("0.0") + "°  |  LANDING: " +
                      _landedYawDegrees.ToString("0.0") + "°"
                    : "0° = calibration");
            _hud.Text = "DESKTOPSENS 3D  |  Relative yaw " + relativeYaw.ToString("0.0") + "°  Pitch " +
                _cameraPitchDegrees.ToString("0.0") + "°  |  Trials " + _profile.Trials.Count + Environment.NewLine +
                angleReadout + Environment.NewLine + instruction +
                Environment.NewLine + "360° arena · labels every 10° from fixed 0° start";

            float cx = ClientSize.Width * 0.5f;
            float cy = ClientSize.Height * 0.5f;
            float compassRadius = Math.Max(100f, Math.Min(ClientSize.Width, ClientSize.Height) * 0.34f);
            for (int i = 0; i < _degreeLabels.Length; i++)
            {
                double radians = (i * 10.0 - relativeYaw) * Math.PI / 180.0;
                Label label = _degreeLabels[i];
                label.Location = new Point(
                    (int)(cx + Math.Sin(radians) * compassRadius - label.Width / 2f),
                    (int)(cy - Math.Cos(radians) * compassRadius - label.Height / 2f));
            }

            double shownAngle = _state == TrialState.SetTargetPoint ? relativeYaw : _targetYawDegrees;
            _selectedAngleLabel.Text = "TARGET POINT: " + shownAngle.ToString("0.0") + "°";
            PointF angleAnchor = _state == TrialState.SetTargetPoint
                ? new PointF(cx + (float)(Math.Sin((shownAngle - relativeYaw) * Math.PI / 180.0) * compassRadius),
                    cy - (float)(Math.Cos((shownAngle - relativeYaw) * Math.PI / 180.0) * compassRadius))
                : _targetPos;
            _selectedAngleLabel.Location = new Point(
                (int)(angleAnchor.X - _selectedAngleLabel.Width / 2f),
                (int)(angleAnchor.Y - _selectedAngleLabel.Height - 10f));
            _selectedAngleLabel.BringToFront();
            _hud.BringToFront();
            _arena3D.Invalidate();
        }

        private Vector3 CameraDirectionFromCalibration()
        {
            double yaw = NormalizeSignedAngle(_cameraYawDegrees - _calibrationYawDegrees) * Math.PI / 180.0;
            double pitch = (_cameraPitchDegrees - _calibrationPitchDegrees) * Math.PI / 180.0;

            Vector3 horizontal = Vector3.Normalize(
                _calibrationForward * (float)Math.Cos(yaw) +
                _calibrationRight * (float)Math.Sin(yaw));
            return Vector3.Normalize(
                horizontal * (float)Math.Cos(pitch) +
                _calibrationUp * (float)Math.Sin(pitch));
        }

        private void CaptureCalibrationPlane()
        {
            float yaw = MathHelper.DegreesToRadians(_cameraYawDegrees);
            float pitch = MathHelper.DegreesToRadians(_cameraPitchDegrees);
            _calibrationYawDegrees = _cameraYawDegrees;
            _calibrationPitchDegrees = _cameraPitchDegrees;
            _calibrationForward = Vector3.Normalize(new Vector3(
                (float)(Math.Sin(yaw) * Math.Cos(pitch)),
                (float)Math.Sin(pitch),
                (float)(-Math.Cos(yaw) * Math.Cos(pitch))));
            _calibrationRight = Vector3.Normalize(Vector3.Cross(_calibrationForward, Vector3.UnitY));
            _calibrationUp = Vector3.Normalize(Vector3.Cross(_calibrationRight, _calibrationForward));
            Vector3 eye = new Vector3(0f, 1.7f, 0f);
            _calibrationOrigin = eye + _calibrationForward * CalibrationPlaneDistance;
            _arena3D.StartPoint = _calibrationOrigin;
            _arena3D.CalibrationRight = _calibrationRight;
            _arena3D.CalibrationUp = _calibrationUp;
            _arena3D.ShowStart = true;
        }

        private Vector3 WorldPointFromAngles(double yawDegrees, double pitchDegrees, double distanceCm)
        {
            // Construct the marker directly from its measured angular coordinates.
            // Do not infer its world angle from a projected screen pixel: that mapping
            // compressed and distorted the 360-degree circle.
            double yaw = yawDegrees * Math.PI / 180.0;
            double pitch = pitchDegrees * Math.PI / 180.0;
            Vector3 horizontal = Vector3.Normalize(
                _calibrationForward * (float)Math.Cos(yaw) +
                _calibrationRight * (float)Math.Sin(yaw));
            Vector3 direction = Vector3.Normalize(
                horizontal * (float)Math.Cos(pitch) +
                _calibrationUp * (float)Math.Sin(pitch));
            Vector3 eye = new Vector3(0f, 1.7f, 0f);
            float worldDistance = (float)(1.0 + 19.0 * Math.Max(0.0,
                Math.Min(1.0, distanceCm / Math.Max(0.001, _maxDistanceCm))));
            return eye + direction * worldDistance;
        }

        private Vector3 ScreenPointToWorld(PointF point)
        {
            if (ClientSize.Width < 1 || ClientSize.Height < 1)
                return _calibrationOrigin;

            // Treat the trainer as an angular map around the player, not a flat tangent
            // plane. Tan(angle) explodes at 90 degrees and flips sign beyond it, which made
            // targets at/near a quarter-turn disappear. A fixed-radius cylindrical mapping
            // remains finite through a full 360-degree yaw.
            float dxPixels = point.X - _startPos.X;
            float dyPixels = _startPos.Y - point.Y;
            double degreesPerPixel = 180.0 /
                (Math.Max(0.001, _maxDistanceCm) * Math.Max(1f, _pixelsPerCm));
            double yawOffset = dxPixels * degreesPerPixel * Math.PI / 180.0;
            double pitchOffset = dyPixels * degreesPerPixel * Math.PI / 180.0;

            Vector3 forward = _calibrationForward;
            Vector3 right = _calibrationRight;
            Vector3 up = _calibrationUp;
            Vector3 horizontal = Vector3.Normalize(forward * (float)Math.Cos(yawOffset)
                + right * (float)Math.Sin(yawOffset));
            Vector3 direction = Vector3.Normalize(horizontal * (float)Math.Cos(pitchOffset)
                + up * (float)Math.Sin(pitchOffset));
            Vector3 eye = new Vector3(0f, 1.7f, 0f);
            return eye + direction * CalibrationPlaneDistance;
        }

        private void InvalidateArena()
        {
            UpdateArena3D();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            // OpenGL renders the arena; no GDI+ pseudo-3D scene is drawn here.
            base.OnPaint(e);
        }
    }
}
