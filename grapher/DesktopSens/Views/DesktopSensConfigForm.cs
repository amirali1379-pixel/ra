using System;
using System.IO;
using System.Windows.Forms;
using grapher.Models.Mouse;
using grapher.DesktopSens.Models;
using Newtonsoft.Json;

namespace grapher.DesktopSens.Views
{
    public class DesktopSensConfigForm : Form
    {
        private readonly AccelGUI _accelGUI;
        private DesktopSensProfile _profile;

        public DesktopSensConfigForm(AccelGUI accelGUI)
        {
            _accelGUI = accelGUI;
            _profile = new DesktopSensProfile();

            Text = "Desktop Sens Calibration";
            Width = 400;
            Height = 250;
            StartPosition = FormStartPosition.CenterScreen;

            var gameSensBox = new TextBox { Left = 120, Top = 20, Width = 150, Text = "2.5" };
            var dpiBox = new TextBox { Left = 120, Top = 50, Width = 150, Text = "800" };

            var startBtn = new Button { Text = "Start Training", Left = 20, Top = 90, Width = 340, Height = 40 };
            var saveBtn = new Button { Text = "Save to JSON && Calibrate", Left = 20, Top = 140, Width = 340, Height = 40 };

            startBtn.Click += (s, e) =>
            {
                if (double.TryParse(gameSensBox.Text, out double gs)) _profile.BaseSensitivity = gs;
                if (int.TryParse(dpiBox.Text, out int dpi)) _profile.MouseDPI = dpi;

                using (var trainer = new DesktopSensTrainerForm(_profile))
                {
                    EventHandler<MouseSample> handler = (sender, sample) => trainer.OnMouseSample(sample);
                    _accelGUI.MouseWatcher.MouseSampleReceived += handler;

                    this.Hide();
                    trainer.ShowDialog();
                    this.Show();

                    _accelGUI.MouseWatcher.MouseSampleReceived -= handler;
                }
            };

            saveBtn.Click += (s, e) =>
            {
                if (_profile.Trials.Count == 0) { MessageBox.Show("No trials."); return; }
                
                DesktopSensCalibrator.Calibrate(_profile);
                
                // ذخیره‌ی فایل JSON مستقل
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "DesktopSensProfile.json");
                File.WriteAllText(path, JsonConvert.SerializeObject(_profile, Formatting.Indented));
                
                // کپی خودکار به مسیر Raw Accel (برای اعمال تنظیمات)
                string rawAccelPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RawAccel");
                if (Directory.Exists(rawAccelPath))
                {
                    File.WriteAllText(Path.Combine(rawAccelPath, "DesktopSensProfile.json"), JsonConvert.SerializeObject(_profile, Formatting.Indented));
                }

                MessageBox.Show($"Saved to:\n{path}\n\nRaw Accel does NOT need to be running.\nProfile saved independently.");
            };

            Controls.Add(new Label { Text = "Game Sens:", Left = 20, Top = 20, Width = 100 });
            Controls.Add(gameSensBox);
            Controls.Add(new Label { Text = "Mouse DPI:", Left = 20, Top = 50, Width = 100 });
            Controls.Add(dpiBox);
            Controls.Add(startBtn);
            Controls.Add(saveBtn);
        }
    }
}