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

        private enum TrialState { SetStartPoint, SetTargetPoint, Cooldown, Flicking, ASDWAdjusting, TrialComplete }
        private TrialState _state = TrialState.SetStartPoint;

        private PointF _startPos;
        private PointF _targetPos;
        private PointF _currentCursorPos;
        private readonly float _pixelsPerCm = 20.0f;
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

            FormBorderStyle = FormBorderStyle.None;
            WindowState = FormWindowState.Maximized;
            BackColor = Color.FromArgb(30, 30, 30);
            DoubleBuffered = true;
            KeyPreview = true;
            Cursor.Hide();

            _currentCursorPos = new PointF(ClientSize.Width / 2f, ClientSize.Height / 2f);
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
                _startPos = clickedPoint;
                _currentCursorPos = _startPos;
                MoveSystemCursorToCanvas();
                _state = TrialState.SetTargetPoint;
            }
            else if (_state == TrialState.SetTargetPoint)
            {
                _targetPos = clickedPoint;
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
            double actualDx = _currentCursorPos.X - _startPos.X;
            double actualDy = _currentCursorPos.Y - _startPos.Y;
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
                FlickDistanceCm = flickDistanceCm,
                DurationMs = _trialStopwatch.Elapsed.TotalMilliseconds,
                PeakVelocityPxPerSec = peakVelocity,
                AngleZoneIndex = ZoneCalculations.AngleToZoneIndex(targetAngle),
                DistanceZoneIndex = ZoneCalculations.DistanceToZoneIndex(targetDistanceCm),
                CompletionMode = FlickCompletionMode.Click,
                CompletionTimestamp = _rawSamples.Count > 0 ? _rawSamples[_rawSamples.Count - 1].Timestamp : 0,
                MouseDPI = _profile.MouseDPI,
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

        private void ClampCursorToCanvas()
        {
            _currentCursorPos.X = Math.Max(0, Math.Min(ClientSize.Width - 1, _currentCursorPos.X));
            _currentCursorPos.Y = Math.Max(0, Math.Min(ClientSize.Height - 1, _currentCursorPos.Y));
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

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;

            using (var statusFont = new Font("Arial", 16, FontStyle.Bold))
            using (var smallFont = new Font("Arial", 9))
            {
                float[] distancesCm = { 3f, 10f, 20f, 40f };
                foreach (float cm in distancesCm)
                {
                    float radius = cm * _pixelsPerCm;
                    g.DrawEllipse(Pens.DarkCyan, _startPos.X - radius, _startPos.Y - radius, radius * 2, radius * 2);
                    g.DrawString(cm.ToString("0") + " cm", smallFont, Brushes.DarkCyan,
                        _startPos.X + radius + 3, _startPos.Y);
                }

                for (int i = 0; i < DesktopSensConstants.AngularZoneCount; i++)
                {
                    double angleRad = i * DesktopSensConstants.AngularZoneSizeDegrees * Math.PI / 180.0;
                    float radius = 40f * _pixelsPerCm;
                    float xEnd = _startPos.X + (float)(Math.Cos(angleRad) * radius);
                    float yEnd = _startPos.Y + (float)(Math.Sin(angleRad) * radius);
                    g.DrawLine(_angleZoneCounts[i] > 0 ? Pens.Lime : Pens.DimGray,
                        _startPos.X, _startPos.Y, xEnd, yEnd);
                    g.DrawString(_angleZoneCounts[i].ToString(), smallFont, Brushes.White, xEnd, yEnd);
                }

                if (_state != TrialState.SetStartPoint)
                    g.FillEllipse(Brushes.Red, _startPos.X - 6, _startPos.Y - 6, 12, 12);
                if (_state == TrialState.Cooldown || _state == TrialState.Flicking ||
                    _state == TrialState.ASDWAdjusting || _state == TrialState.TrialComplete)
                {
                    g.FillEllipse(Brushes.Lime, _targetPos.X - 6, _targetPos.Y - 6, 12, 12);
                    g.DrawLine(Pens.Orange, _startPos, _targetPos);
                }

                g.DrawLine(Pens.White, _currentCursorPos.X - 10, _currentCursorPos.Y,
                    _currentCursorPos.X + 10, _currentCursorPos.Y);
                g.DrawLine(Pens.White, _currentCursorPos.X, _currentCursorPos.Y - 10,
                    _currentCursorPos.X, _currentCursorPos.Y + 10);

                string status;
                switch (_state)
                {
                    case TrialState.SetStartPoint: status = "1/2: Click the START point"; break;
                    case TrialState.SetTargetPoint: status = "2/2: Click the TARGET point"; break;
                    case TrialState.Cooldown: status = "Get ready (3 seconds)..."; break;
                    case TrialState.Flicking: status = "Flick to the target, then click to record landing"; break;
                    case TrialState.ASDWAdjusting: status = "Adjust with W/A/S/D, then press SPACE to save trial"; break;
                    default: status = "Trial saved. Click to start a new trial (ESC to exit)."; break;
                }
                g.DrawString(status, statusFont, Brushes.Yellow, 20, 20);
                g.DrawString("Distance scale: " + _pixelsPerCm.ToString("0") + " canvas px/cm | Trials: " +
                    _profile.Trials.Count, smallFont, Brushes.White, 20, 48);
            }
        }
    }
}
