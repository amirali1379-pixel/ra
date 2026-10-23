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

        private enum TrialState { SetStartPoint, Cooldown, Flicking, ASDWAdjusting, TrialComplete }
        private TrialState _state = TrialState.SetStartPoint;

        private PointF _startPos;
        private PointF _targetPos;
        private PointF _currentCursorPos;

        private int[] _angleZoneCounts = new int[32];
        private int[] _distanceZoneCounts = new int[23];

        [DllImport("user32.dll")] static extern bool SetCursorPos(int X, int Y);

        public DesktopSensTrainerForm(DesktopSensProfile profile)
        {
            _profile = profile;
            FormBorderStyle = FormBorderStyle.None;
            WindowState = FormWindowState.Maximized;
            BackColor = Color.FromArgb(30, 30, 30);
            DoubleBuffered = true;
            KeyPreview = true;
            Cursor.Hide();

            _currentCursorPos = new PointF(Screen.PrimaryScreen.Bounds.Width / 2f, Screen.PrimaryScreen.Bounds.Height / 2f);
            SetCursorPos((int)_currentCursorPos.X, (int)_currentCursorPos.Y);

            _cooldownTimer = new Timer { Interval = 3000 };
            _cooldownTimer.Tick += OnCooldownFinished;

            FormClosed += (s, e) => Cursor.Show();
        }

        public void OnMouseSample(MouseSample sample)
        {
            // همیشه موقعیت را آپدیت کن، حتی در کول‌داون
            _currentCursorPos.X += sample.DX;
            _currentCursorPos.Y += sample.DY;

            if (_currentCursorPos.X < 0) _currentCursorPos.X = 0;
            if (_currentCursorPos.Y < 0) _currentCursorPos.Y = 0;
            if (_currentCursorPos.X > Width) _currentCursorPos.X = Width;
            if (_currentCursorPos.Y > Height) _currentCursorPos.Y = Height;

            if (_state == TrialState.Flicking || _state == TrialState.ASDWAdjusting)
                _rawSamples.Add(sample);

            SetCursorPos((int)_currentCursorPos.X, (int)_currentCursorPos.Y);
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);

            if (_state == TrialState.SetStartPoint)
            {
                _startPos = _currentCursorPos;
                
                float targetDistancePx = 18f * _profile.MouseDPI / 2.54f;
                _targetPos = new PointF(_startPos.X + targetDistancePx, _startPos.Y);

                _state = TrialState.Cooldown;
                _currentCursorPos = _startPos; // فقط موقعیت داخلی را صفر کن
                SetCursorPos((int)_startPos.X, (int)_startPos.Y); // موس ویندوز را هم ببر روی نقطه شروع
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
            if (e.KeyCode == Keys.Escape) { Close(); return; }

            if (_state == TrialState.ASDWAdjusting)
            {
                float speed = 2.0f;
                if (e.KeyCode == Keys.W) _currentCursorPos.Y -= speed;
                if (e.KeyCode == Keys.S) _currentCursorPos.Y += speed;
                if (e.KeyCode == Keys.A) _currentCursorPos.X -= speed;
                if (e.KeyCode == Keys.D) _currentCursorPos.X += speed;

                if (_currentCursorPos.X < 0) _currentCursorPos.X = 0;
                if (_currentCursorPos.Y < 0) _currentCursorPos.Y = 0;
                if (_currentCursorPos.X > Width) _currentCursorPos.X = Width;
                if (_currentCursorPos.Y > Height) _currentCursorPos.Y = Height;

                SetCursorPos((int)_currentCursorPos.X, (int)_currentCursorPos.Y);

                if (e.KeyCode == Keys.Space) FinishTrial();
                Invalidate();
            }
        }

        private void FinishTrial()
        {
            _trialStopwatch.Stop();
            _state = TrialState.TrialComplete;

            double dx = _targetPos.X - _startPos.X;
            double dy = _targetPos.Y - _startPos.Y;
            double targetAngle = Math.Atan2(dy, dx) * 180.0 / Math.PI;
            if (targetAngle < 0) targetAngle += 360;

            double targetDistancePx = Math.Sqrt(dx * dx + dy * dy);
            double targetDistanceCm = targetDistancePx / _profile.MouseDPI * 2.54;

            double errX = _currentCursorPos.X - _targetPos.X;
            double errY = _currentCursorPos.Y - _targetPos.Y;
            double finalErrorCm = Math.Sqrt(errX * errX + errY * errY) / _profile.MouseDPI * 2.54;

            bool isUnder = Math.Sqrt(Math.Pow(_currentCursorPos.X - _startPos.X, 2) + Math.Pow(_currentCursorPos.Y - _startPos.Y, 2)) < targetDistancePx;

            var trial = new DesktopSensTrial
            {
                StartX = _startPos.X, StartY = _startPos.Y,
                TargetX = _targetPos.X, TargetY = _targetPos.Y,
                TargetAngleDegrees = targetAngle, TargetDistanceCm = targetDistanceCm,
                RawSamples = new List<MouseSample>(_rawSamples),
                FinalErrorCm = finalErrorCm,
                IsUnderFlick = isUnder,
                DurationMs = _trialStopwatch.Elapsed.TotalMilliseconds,
                AngleZoneIndex = ZoneCalculations.AngleToZoneIndex(targetAngle),
                DistanceZoneIndex = ZoneCalculations.DistanceToZoneIndex(targetDistanceCm),
                MouseDPI = _profile.MouseDPI
            };

            _profile.Trials.Add(trial);
            _angleZoneCounts[trial.AngleZoneIndex]++;
            _distanceZoneCounts[trial.DistanceZoneIndex]++;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;

            if (_startPos != PointF.Empty)
            {
                // ۱. دایره‌های فاصله
                float[] distances = { 3f, 10f, 20f, 40f };
                foreach (var cm in distances)
                {
                    float px = cm * _profile.MouseDPI / 2.54f;
                    g.DrawEllipse(Pens.DarkCyan, _startPos.X - px, _startPos.Y - px, px * 2, px * 2);
                }

                // ۲. خطوط زاویه
                for (int i = 0; i < 32; i++)
                {
                    double angleRad = (i * 11.25) * Math.PI / 180.0;
                    float px = 40f * _profile.MouseDPI / 2.54f; 
                    float xEnd = _startPos.X + (float)(Math.Cos(angleRad) * px);
                    float yEnd = _startPos.Y + (float)(Math.Sin(angleRad) * px);
                    
                    Pen pen = _angleZoneCounts[i] > 0 ? Pens.Lime : Pens.DimGray;
                    g.DrawLine(pen, _startPos.X, _startPos.Y, xEnd, yEnd);
                    
                    string count = _angleZoneCounts[i].ToString();
                    g.DrawString(count, new Font("Arial", 8), Brushes.White, xEnd, yEnd);
                }
            }

            if (_state != TrialState.SetStartPoint)
                g.FillEllipse(Brushes.Red, _startPos.X - 8, _startPos.Y - 8, 16, 16);

            if (_state != TrialState.SetStartPoint)
                g.FillEllipse(Brushes.Lime, _targetPos.X - 8, _targetPos.Y - 8, 16, 16);

            g.DrawLine(Pens.White, _currentCursorPos.X - 10, _currentCursorPos.Y, _currentCursorPos.X + 10, _currentCursorPos.Y);
            g.DrawLine(Pens.White, _currentCursorPos.X, _currentCursorPos.Y - 10, _currentCursorPos.X, _currentCursorPos.Y + 10);

            string status = "";
            if (_state == TrialState.SetStartPoint) status = "Click to set Start Point";
            else if (_state == TrialState.Cooldown) status = "Cooldown (3s)...";
            else if (_state == TrialState.Flicking) status = "Flick to Target & Click";
            else if (_state == TrialState.ASDWAdjusting) status = "Adjust with ASDW, then SPACE";
            else if (_state == TrialState.TrialComplete) status = "Trial Saved! Click to start next.";

            g.DrawString(status, new Font("Arial", 16, FontStyle.Bold), Brushes.Yellow, 20, 20);
        }
    }
}