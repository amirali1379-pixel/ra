using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using grapher.Models.Mouse;
using grapher.DesktopSens.Models;

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

        private enum TrialState { SetStartPoint, SetTargetPoint, Cooldown, Flicking, ASDWAdjusting, TrialComplete }
        private TrialState _state = TrialState.SetStartPoint;

        private PointF _startPos;
        private PointF _targetPos;
        private PointF _currentCursorPos;
        private PointF _rawLandedPos;
        // The circular trainer radius represents exactly a 180-degree CS2 turn.
        // The conversion depends on the current mouse DPI and CS2 sensitivity.
        private readonly double _maxDistanceCm;
        private readonly float _pixelsPerCm;
        private readonly int[] _angleZoneCounts = new int[DesktopSensConstants.AngularZoneCount];
        private readonly int[] _distanceZoneCounts = new int[DesktopSensConstants.DistanceZoneCount];
        private float _cameraYawDegrees;
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
            _hud.BringToFront();
            _arena3D.MouseDown += (s, e) => OnMouseDown(e);
            _arena3D.MouseMove += (s, e) => OnMouseMove(e);
            _arena3D.MouseUp += (s, e) => OnMouseUp(e);
            _arena3D.MouseWheel += (s, e) => { _cameraPitchDegrees = Math.Max(5f, Math.Min(65f, _cameraPitchDegrees + e.Delta / 120f)); InvalidateArena(); };
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
            // Keep mouse tracking enabled while placing the setup points. Freeze the virtual
            // cursor during cooldown and keyboard correction so those phases cannot contaminate
            // the raw flick measurement.
            if (_state == TrialState.Cooldown ||
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
            ClampCursorToCanvas();

            if (_state == TrialState.Flicking)
                _rawSamples.Add(sample);

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
                // Keep the origin centered so every direction in the 360-degree field is reachable.
                _startPos = new PointF(ClientSize.Width / 2f, ClientSize.Height / 2f);
                _currentCursorPos = _startPos;
                MoveSystemCursorToCanvas();
                _state = TrialState.SetTargetPoint;
            }
            else if (_state == TrialState.SetTargetPoint)
            {
                // Preserve the clicked angle instead of snapping to four directions.
                // Clamp the target to the 180-degree turn circle so training never extrapolates
                // beyond the configured end point.
                _targetPos = ClampToTurnCircle(clickedPoint);

                double targetDistanceCm = Distance(_startPos, _targetPos) / _pixelsPerCm;
                if (targetDistanceCm < 0.25)
                {
                    MessageBox.Show(this, "Choose a target at least 0.25 cm away from the start point.",
                        "Target too close", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                _rawSamples.Clear();
                _currentCursorPos = _startPos;
                MoveSystemCursorToCanvas();
                _state = TrialState.Cooldown;
                _cooldownTimer.Start();
            }
            else if (_state == TrialState.Flicking)
            {
                // Preserve the actual mouse-flick landing before keyboard correction. Sens/accel
                // learn from this raw landing; rotation must not be hidden by the ASDW adjustment.
                _rawLandedPos = _currentCursorPos;
                _state = TrialState.ASDWAdjusting;
            }
            else if (_state == TrialState.TrialComplete)
            {
                _state = TrialState.SetStartPoint;
                _rawSamples.Clear();
                _startPos = PointF.Empty;
                _targetPos = PointF.Empty;
            }

            UpdateArena3D();
        }

        private void OnCooldownFinished(object sender, EventArgs e)
        {
            _cooldownTimer.Stop();
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
                const float adjustmentSpeed = 2.0f;
                if (e.KeyCode == Keys.W) _currentCursorPos.Y -= adjustmentSpeed;
                if (e.KeyCode == Keys.S) _currentCursorPos.Y += adjustmentSpeed;
                if (e.KeyCode == Keys.A) _currentCursorPos.X -= adjustmentSpeed;
                if (e.KeyCode == Keys.D) _currentCursorPos.X += adjustmentSpeed;

                ClampCursorToCanvas();
                MoveSystemCursorToCanvas();

                if (e.KeyCode == Keys.Space)
                    FinishTrial();

                e.Handled = true;
                e.SuppressKeyPress = true;
                UpdateArena3D();
            }
        }

        private void FinishTrial()
        {
            _trialStopwatch.Stop();
            _state = TrialState.TrialComplete;

            double targetDx = _targetPos.X - _startPos.X;
            double targetDy = _targetPos.Y - _startPos.Y;
            double targetDistancePx = Math.Sqrt(targetDx * targetDx + targetDy * targetDy);
            if (targetDistancePx <= 0.0) return;

            double targetAngle = NormalizeAngle(Math.Atan2(targetDy, targetDx) * 180.0 / Math.PI);
            double actualDx = _rawLandedPos.X - _startPos.X;
            double actualDy = _rawLandedPos.Y - _startPos.Y;
            double actualDistancePx = Math.Sqrt(actualDx * actualDx + actualDy * actualDy);
            double targetUnitX = targetDx / targetDistancePx;
            double targetUnitY = targetDy / targetDistancePx;
            double projectedDistancePx = actualDx * targetUnitX + actualDy * targetUnitY;
            double signedErrorCm = (projectedDistancePx - targetDistancePx) / _pixelsPerCm;
            double finalErrorCm = Distance(_currentCursorPos, _targetPos) / _pixelsPerCm;
            double targetDistanceCm = targetDistancePx / _pixelsPerCm;
            double flickAngle = NormalizeAngle(Math.Atan2(actualDy, actualDx) * 180.0 / Math.PI);
            double flickDistanceCm = actualDistancePx / _pixelsPerCm;

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
                SignedRotationErrorDegrees = NormalizeSignedAngle(targetAngle - flickAngle),
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
            _arena3D.Radius = (float)_maxDistanceCm;
            _arena3D.Target = new PointF(
                (float)((_targetPos.X - _startPos.X) / Math.Max(1f, _pixelsPerCm) * 180.0 / _maxDistanceCm),
                (float)((_startPos.Y - _targetPos.Y) / Math.Max(1f, _pixelsPerCm) * 180.0 / _maxDistanceCm));
            _arena3D.ShowTarget = _state == TrialState.Cooldown || _state == TrialState.Flicking ||
                _state == TrialState.ASDWAdjusting || _state == TrialState.TrialComplete;
            _arena3D.StatusText = _state.ToString();
            _arena3D.TrialCount = _profile.Trials.Count;
            string instruction;
            switch (_state)
            {
                case TrialState.SetStartPoint: instruction = "Click to begin  |  Right-drag: look around  |  ESC: exit"; break;
                case TrialState.SetTargetPoint: instruction = "Click a target position  |  Mouse movement: look around"; break;
                case TrialState.Cooldown: instruction = "Get ready…"; break;
                case TrialState.Flicking: instruction = "Turn your view to the target, then click to record landing"; break;
                case TrialState.ASDWAdjusting: instruction = "W/A/S/D to adjust  |  SPACE to save"; break;
                default: instruction = "Trial saved  |  Click to start the next trial"; break;
            }
            _hud.Text = "DESKTOPSENS 3D  |  Yaw " + _cameraYawDegrees.ToString("0") + "°  Pitch " +
                _cameraPitchDegrees.ToString("0") + "°  |  Trials " + _profile.Trials.Count + Environment.NewLine +
                instruction + Environment.NewLine + "FPS range · raw mouse look · calibrated 180° turn-distance limit";
            _arena3D.Invalidate();
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
