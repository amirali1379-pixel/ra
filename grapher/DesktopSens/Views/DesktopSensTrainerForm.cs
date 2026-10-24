using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
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
            Cursor.Hide();

            _startPos = new PointF(ClientSize.Width / 2f, ClientSize.Height / 2f);
            _currentCursorPos = _startPos;
            MoveSystemCursorToCanvas();

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
            ClampCursorToCanvas();

            if (_state == TrialState.Flicking)
                _rawSamples.Add(sample);

            MoveSystemCursorToCanvas();
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                _cameraDragging = true;
                _cameraDragOrigin = e.Location;
                _cameraDragYaw = _cameraYawDegrees;
                _cameraDragPitch = _cameraPitchDegrees;
                Capture = true;
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

            Invalidate();
        }

        private void OnCooldownFinished(object sender, EventArgs e)
        {
            _cooldownTimer.Stop();
            _state = TrialState.Flicking;
            _rawSamples.Clear();
            _trialStopwatch.Restart();
            Invalidate();
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
                Invalidate();
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

        // Project the trainer's world-space ground plane through a rotatable perspective camera.
        private PointF ProjectArenaPoint(PointF point, float outerRadius, float groundCenterY)
        {
            float radius = Math.Max(1f, outerRadius);
            double worldX = (point.X - _startPos.X) / radius;
            double worldZ = -(point.Y - _startPos.Y) / radius;
            double yaw = _cameraYawDegrees * Math.PI / 180.0;
            double pitch = _cameraPitchDegrees * Math.PI / 180.0;
            const double cameraDistance = 3.2;
            const double cameraHeight = 1.15;
            double camX = Math.Sin(yaw) * cameraDistance;
            double camZ = -Math.Cos(yaw) * cameraDistance;
            double dx = worldX - camX;
            double dz = worldZ - camZ;
            double cameraX = dx * Math.Cos(yaw) - dz * Math.Sin(yaw);
            double depthBeforePitch = dx * Math.Sin(yaw) + dz * Math.Cos(yaw);
            double cameraY = -cameraHeight;
            double depth = depthBeforePitch * Math.Cos(pitch) - cameraY * Math.Sin(pitch);
            double vertical = cameraY * Math.Cos(pitch) + depthBeforePitch * Math.Sin(pitch);
            if (depth < 0.08) depth = 0.08;
            double focal = Math.Min(ClientSize.Width, ClientSize.Height) * 1.05;
            float horizonY = ClientSize.Height * 0.27f;
            return new PointF((float)(ClientSize.Width * 0.5 + cameraX * focal / depth),
                (float)(horizonY - vertical * focal / depth));
        }

        private void DrawArenaCircle(Graphics g, Pen pen, float radiusPixels, float outerRadius, float groundCenterY)
        {
            const int segments = 96;
            PointF previous = PointF.Empty;
            for (int i = 0; i <= segments; i++)
            {
                double a = i * Math.PI * 2.0 / segments;
                PointF worldPoint = new PointF(_startPos.X + (float)Math.Cos(a) * radiusPixels,
                    _startPos.Y - (float)Math.Sin(a) * radiusPixels);
                PointF projected = ProjectArenaPoint(worldPoint, outerRadius, groundCenterY);
                if (i > 0 && IsOnScreen(previous) && IsOnScreen(projected))
                    g.DrawLine(pen, previous, projected);
                previous = projected;
            }
        }

        private bool IsOnScreen(PointF p)
        {
            return !float.IsNaN(p.X) && !float.IsInfinity(p.X) &&
                !float.IsNaN(p.Y) && !float.IsInfinity(p.Y) &&
                Math.Abs(p.X) < ClientSize.Width * 8f && Math.Abs(p.Y) < ClientSize.Height * 8f;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (_cameraDragging)
            {
                _cameraYawDegrees = _cameraDragYaw + (e.X - _cameraDragOrigin.X) * 0.45f;
                _cameraPitchDegrees = Math.Max(5f, Math.Min(65f,
                    _cameraDragPitch + (e.Y - _cameraDragOrigin.Y) * 0.25f));
                Invalidate();
                return;
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right && _cameraDragging)
            {
                _cameraDragging = false;
                Capture = false;
                return;
            }
            base.OnMouseUp(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            float outerRadius = (float)(_maxDistanceCm * _pixelsPerCm);
            float horizonY = ClientSize.Height * 0.27f;
            float groundCenterY = ClientSize.Height * 0.68f;
            float sceneRadius = Math.Min(outerRadius, Math.Min(ClientSize.Width * 0.43f,
                (ClientSize.Height - horizonY) * 0.72f));
            float ellipseHeight = sceneRadius * 0.48f;

            using (var background = new LinearGradientBrush(ClientRectangle,
                Color.FromArgb(12, 16, 22), Color.FromArgb(31, 39, 46),
                LinearGradientMode.Vertical))
            using (var skyBrush = new LinearGradientBrush(
                new Rectangle(0, 0, ClientSize.Width, Math.Max(1, (int)horizonY)),
                Color.FromArgb(10, 14, 20), Color.FromArgb(30, 40, 48),
                LinearGradientMode.Vertical))
            using (var floorBrush = new LinearGradientBrush(
                new Rectangle(0, (int)horizonY, ClientSize.Width,
                    Math.Max(1, ClientSize.Height - (int)horizonY)),
                Color.FromArgb(25, 34, 41), Color.FromArgb(10, 14, 19),
                LinearGradientMode.Vertical))
            using (var horizonPen = new Pen(Color.FromArgb(95, 94, 139, 148), 1.2f))
            using (var gridPen = new Pen(Color.FromArgb(35, 123, 158, 164), 1f))
            using (var zonePen = new Pen(Color.FromArgb(65, 130, 163, 169), 1f))
            using (var usedZonePen = new Pen(Color.FromArgb(185, 83, 185, 151), 1.8f))
            using (var ringPen = new Pen(Color.FromArgb(90, 132, 168, 174), 1.1f))
            using (var outerPen = new Pen(Color.FromArgb(205, 156, 202, 207), 2f))
            using (var targetPen = new Pen(Color.FromArgb(220, 242, 173, 94), 2f))
            using (var cursorPen = new Pen(Color.FromArgb(235, 228, 239, 240), 1.6f))
            using (var textBrush = new SolidBrush(Color.FromArgb(210, 202, 216, 220)))
            using (var mutedBrush = new SolidBrush(Color.FromArgb(165, 151, 171, 178)))
            using (var targetBrush = new SolidBrush(Color.FromArgb(240, 242, 173, 94)))
            using (var cursorBrush = new SolidBrush(Color.FromArgb(235, 104, 218, 183)))
            using (var statusBrush = new SolidBrush(Color.FromArgb(245, 231, 237, 239)))
            using (var statusFont = new Font("Arial", 16, FontStyle.Bold))
            using (var smallFont = new Font("Arial", 9))
            using (var compassFont = new Font("Arial", 10, FontStyle.Bold))
            {
                g.FillRectangle(background, ClientRectangle);
                g.FillRectangle(skyBrush, 0, 0, ClientSize.Width, horizonY);
                g.FillRectangle(floorBrush, 0, horizonY, ClientSize.Width,
                    ClientSize.Height - horizonY);
                g.DrawLine(horizonPen, 0, horizonY, ClientSize.Width, horizonY);

                // World-space floor grid, projected through the rotatable 3D camera.
                for (int i = -10; i <= 10; i++)
                {
                    PointF a = ProjectArenaPoint(new PointF(_startPos.X + i * outerRadius / 10f, _startPos.Y - outerRadius), outerRadius, groundCenterY);
                    PointF b = ProjectArenaPoint(new PointF(_startPos.X + i * outerRadius / 10f, _startPos.Y + outerRadius), outerRadius, groundCenterY);
                    if (IsOnScreen(a) && IsOnScreen(b)) g.DrawLine(gridPen, a, b);
                }
                for (int i = -10; i <= 10; i++)
                {
                    PointF a = ProjectArenaPoint(new PointF(_startPos.X - outerRadius, _startPos.Y + i * outerRadius / 10f), outerRadius, groundCenterY);
                    PointF b = ProjectArenaPoint(new PointF(_startPos.X + outerRadius, _startPos.Y + i * outerRadius / 10f), outerRadius, groundCenterY);
                    if (IsOnScreen(a) && IsOnScreen(b)) g.DrawLine(gridPen, a, b);
                }

                // The playable plane is a 360-degree arena, shown as a perspective ellipse.
                // The outer boundary still means exactly the 180-degree CS2 turn limit.
                float baseRadius = (float)(Math.Min(3.0, _maxDistanceCm) / _maxDistanceCm) * outerRadius;
                DrawArenaCircle(g, ringPen, baseRadius, outerRadius, groundCenterY);
                g.DrawString("START / SPAWN", smallFont, mutedBrush,
                    ClientSize.Width / 2f + 8, ClientSize.Height * 0.70f);

                double[] turnRings = { 45.0, 90.0, 135.0 };
                foreach (double degrees in turnRings)
                {
                    float r = (float)(degrees / DesktopSensConstants.MaxGameTurnDegrees * outerRadius);
                    DrawArenaCircle(g, ringPen, r, outerRadius, groundCenterY);
                }
                DrawArenaCircle(g, outerPen, outerRadius, outerRadius, groundCenterY);
                g.DrawString("180° TURN DISTANCE LIMIT", compassFont, textBrush, 20, ClientSize.Height - 52);

                // Angular sectors are drawn onto the projected plane, not as a flat screen grid.
                for (int i = 0; i < DesktopSensConstants.AngularZoneCount; i++)
                {
                    double a = i * DesktopSensConstants.AngularZoneSizeDegrees * Math.PI / 180.0;
                    PointF edge = new PointF(
                        _startPos.X + (float)Math.Cos(a) * (float)(_maxDistanceCm * _pixelsPerCm),
                        _startPos.Y + (float)Math.Sin(a) * (float)(_maxDistanceCm * _pixelsPerCm));
                    PointF projectedEdge = ProjectArenaPoint(edge,
                        (float)(_maxDistanceCm * _pixelsPerCm), groundCenterY);
                    using (var sectorPen = new Pen(_angleZoneCounts[i] > 0
                        ? Color.FromArgb(185, 83, 185, 151)
                        : Color.FromArgb(48, 130, 163, 169), _angleZoneCounts[i] > 0 ? 1.8f : 1f))
                        g.DrawLine(sectorPen, ClientSize.Width / 2f, groundCenterY, projectedEdge.X, projectedEdge.Y);

                    if (_angleZoneCounts[i] > 0)
                    {
                        using (var countBrush = new SolidBrush(Color.FromArgb(230, 100, 225, 177)))
                            g.DrawString(_angleZoneCounts[i].ToString(), smallFont, countBrush,
                                projectedEdge.X - 5, projectedEdge.Y - 13);
                    }
                }

                g.DrawString("CAMERA: right-drag to look around 360°", smallFont, textBrush, 20, 72);
                g.DrawString("Yaw " + _cameraYawDegrees.ToString("0") + "°   Pitch " + _cameraPitchDegrees.ToString("0") + "°", smallFont, mutedBrush, 20, 90);

                if (_state != TrialState.SetStartPoint)
                {
                    using (var startBrush = new SolidBrush(Color.FromArgb(235, 101, 207, 178)))
                        g.FillEllipse(startBrush, ClientSize.Width / 2f - 6, groundCenterY - 6, 12, 12);
                }

                if (_state == TrialState.Cooldown || _state == TrialState.Flicking ||
                    _state == TrialState.ASDWAdjusting || _state == TrialState.TrialComplete)
                {
                    PointF target = ProjectArenaPoint(_targetPos, outerRadius, groundCenterY);
                    g.DrawLine(targetPen, ClientSize.Width / 2f, groundCenterY, target.X, target.Y);
                    g.DrawLine(targetPen, target.X - 9, target.Y, target.X + 9, target.Y);
                    g.DrawLine(targetPen, target.X, target.Y - 9, target.X, target.Y + 9);
                    g.FillEllipse(targetBrush, target.X - 5, target.Y - 5, 10, 10);
                }

                PointF cursor = ProjectArenaPoint(_currentCursorPos, outerRadius, groundCenterY);
                g.DrawLine(cursorPen, cursor.X - 9, cursor.Y, cursor.X + 9, cursor.Y);
                g.DrawLine(cursorPen, cursor.X, cursor.Y - 9, cursor.X, cursor.Y + 9);
                g.FillEllipse(cursorBrush, cursor.X - 4, cursor.Y - 4, 8, 8);

                string status;
                switch (_state)
                {
                    case TrialState.SetStartPoint: status = "1/2: Click to enter the 3D arena"; break;
                    case TrialState.SetTargetPoint: status = "2/2: Select any direction around you (360°)"; break;
                    case TrialState.Cooldown: status = "Get ready (3 seconds)..."; break;
                    case TrialState.Flicking: status = "Flick to the target, then click to record landing"; break;
                    case TrialState.ASDWAdjusting: status = "Adjust with W/A/S/D, then press SPACE to save"; break;
                    default: status = "Trial saved. Click to start another trial (ESC to exit)."; break;
                }
                g.DrawString(status, statusFont, statusBrush, 20, 20);
                g.DrawString("3D WORLD CAMERA  |  360° LOOK  |  180° CS2 TURN DISTANCE CAP  |  Trials: " +
                    _profile.Trials.Count, smallFont, textBrush, 20, 48);
            }
        }
    }
}
