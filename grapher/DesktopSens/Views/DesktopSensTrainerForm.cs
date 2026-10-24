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

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            using (var background = new LinearGradientBrush(
                ClientRectangle,
                Color.FromArgb(19, 21, 24),
                Color.FromArgb(35, 38, 42),
                LinearGradientMode.ForwardDiagonal))
            using (var statusFont = new Font("Arial", 16, FontStyle.Bold))
            using (var smallFont = new Font("Arial", 9))
            using (var compassFont = new Font("Arial", 10, FontStyle.Bold))
            using (var faintGridPen = new Pen(Color.FromArgb(22, 145, 153, 160), 1f))
            using (var zonePen = new Pen(Color.FromArgb(58, 142, 151, 158), 1f))
            using (var usedZonePen = new Pen(Color.FromArgb(150, 86, 166, 151), 1.5f))
            using (var ringPen = new Pen(Color.FromArgb(80, 151, 160, 166), 1f))
            using (var basePen = new Pen(Color.FromArgb(130, 111, 151, 157), 1.2f))
            using (var outerPen = new Pen(Color.FromArgb(175, 183, 190, 195), 1.8f))
            using (var targetPen = new Pen(Color.FromArgb(190, 194, 155, 105), 1.5f))
            using (var cursorPen = new Pen(Color.FromArgb(220, 220, 224, 226), 1.5f))
            using (var labelBrush = new SolidBrush(Color.FromArgb(175, 183, 190, 195)))
            using (var mutedBrush = new SolidBrush(Color.FromArgb(150, 148, 157, 164)))
            using (var targetBrush = new SolidBrush(Color.FromArgb(230, 201, 166, 119)))
            using (var centerBrush = new SolidBrush(Color.FromArgb(220, 122, 157, 159)))
            using (var statusBrush = new SolidBrush(Color.FromArgb(235, 219, 222, 224)))
            using (var usedZoneBrush = new SolidBrush(Color.FromArgb(220, 119, 180, 164)))
            {
                g.FillRectangle(background, ClientRectangle);

                // Subtle crosshair/grid anchors help orientation without making the screen glow.
                int gridStep = 80;
                for (int x = ClientSize.Width / 2 % gridStep; x < ClientSize.Width; x += gridStep)
                    g.DrawLine(faintGridPen, x, 0, x, ClientSize.Height);
                for (int y = ClientSize.Height / 2 % gridStep; y < ClientSize.Height; y += gridStep)
                    g.DrawLine(faintGridPen, 0, y, ClientSize.Width, y);

                float outerRadius = (float)(_maxDistanceCm * _pixelsPerCm);
                // Full 360-degree field; the soft rings are visual guides for the in-game turn.
                float baseRadius = (float)(Math.Min(3.0, _maxDistanceCm) * _pixelsPerCm);
                g.DrawEllipse(basePen, _startPos.X - baseRadius, _startPos.Y - baseRadius,
                    baseRadius * 2, baseRadius * 2);
                g.DrawString("BASE", smallFont, mutedBrush,
                    _startPos.X + baseRadius + 4, _startPos.Y + 5);

                double[] turnRings = { 45.0, 90.0, 135.0 };
                foreach (double turnDegrees in turnRings)
                {
                    float radius = (float)(turnDegrees / DesktopSensConstants.MaxGameTurnDegrees * outerRadius);
                    g.DrawEllipse(ringPen, _startPos.X - radius, _startPos.Y - radius,
                        radius * 2, radius * 2);
                    g.DrawString(turnDegrees.ToString("0") + "°", smallFont, mutedBrush,
                        _startPos.X + radius + 4, _startPos.Y + 3);
                }
                g.DrawEllipse(outerPen, _startPos.X - outerRadius, _startPos.Y - outerRadius,
                    outerRadius * 2, outerRadius * 2);
                g.DrawString("180° END", smallFont, labelBrush,
                    _startPos.X + outerRadius - 62, _startPos.Y - 18);

                for (int i = 0; i < DesktopSensConstants.AngularZoneCount; i++)
                {
                    double angleRad = i * DesktopSensConstants.AngularZoneSizeDegrees * Math.PI / 180.0;
                    float xEnd = _startPos.X + (float)(Math.Cos(angleRad) * outerRadius);
                    float yEnd = _startPos.Y + (float)(Math.Sin(angleRad) * outerRadius);
                    g.DrawLine(_angleZoneCounts[i] > 0 ? usedZonePen : zonePen,
                        _startPos.X, _startPos.Y, xEnd, yEnd);
                    if (_angleZoneCounts[i] > 0)
                        g.DrawString(_angleZoneCounts[i].ToString(), smallFont, usedZoneBrush,
                            xEnd - 5, yEnd - 14);
                }

                // Cardinal labels make it easier to feel the full circular space while turning.
                g.DrawString("0°", compassFont, labelBrush, _startPos.X + outerRadius + 8, _startPos.Y - 8);
                g.DrawString("90°", compassFont, labelBrush, _startPos.X - 12, _startPos.Y + outerRadius + 8);
                g.DrawString("180°", compassFont, labelBrush, _startPos.X - outerRadius - 43, _startPos.Y - 8);
                g.DrawString("270°", compassFont, labelBrush, _startPos.X - 18, _startPos.Y - outerRadius - 24);

                if (_state != TrialState.SetStartPoint)
                    g.FillEllipse(centerBrush, _startPos.X - 5, _startPos.Y - 5, 10, 10);
                if (_state == TrialState.Cooldown || _state == TrialState.Flicking ||
                    _state == TrialState.ASDWAdjusting || _state == TrialState.TrialComplete)
                {
                    g.FillEllipse(targetBrush, _targetPos.X - 5, _targetPos.Y - 5, 10, 10);
                    g.DrawLine(targetPen, _startPos, _targetPos);
                }

                g.DrawLine(cursorPen, _currentCursorPos.X - 9, _currentCursorPos.Y,
                    _currentCursorPos.X + 9, _currentCursorPos.Y);
                g.DrawLine(cursorPen, _currentCursorPos.X, _currentCursorPos.Y - 9,
                    _currentCursorPos.X, _currentCursorPos.Y + 9);
                g.DrawEllipse(cursorPen, _currentCursorPos.X - 4, _currentCursorPos.Y - 4, 8, 8);

                string status;
                switch (_state)
                {
                    case TrialState.SetStartPoint: status = "1/2: Click to center the start point"; break;
                    case TrialState.SetTargetPoint: status = "2/2: Choose any direction in the 360° field"; break;
                    case TrialState.Cooldown: status = "Get ready (3 seconds)..."; break;
                    case TrialState.Flicking: status = "Flick to the target, then click to record landing"; break;
                    case TrialState.ASDWAdjusting: status = "Adjust with W/A/S/D, then press SPACE to save trial"; break;
                    default: status = "Trial saved. Click to start a new trial (ESC to exit)."; break;
                }
                g.DrawString(status, statusFont, statusBrush, 20, 20);
                g.DrawString("180° turn boundary | 360° directional field | Trials: " +
                    _profile.Trials.Count, smallFont, labelBrush, 20, 48);
            }
        }
    }
}
