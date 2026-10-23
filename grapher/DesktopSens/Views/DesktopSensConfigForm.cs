using System;
using System.Globalization;
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
        private readonly TextBox _gameSensBox;
        private readonly TextBox _dpiBox;
        private readonly TextBox _pollingRateBox;
        private readonly Label _profileStatus;

        private string ProfilePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RawAccel", "DesktopSensProfile.json");

        public DesktopSensConfigForm(AccelGUI accelGUI)
        {
            _accelGUI = accelGUI ?? throw new ArgumentNullException(nameof(accelGUI));
            _profile = LoadProfile();

            Text = "Desktop Sens Calibration";
            Width = 470;
            Height = 310;
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;

            _gameSensBox = new TextBox { Left = 155, Top = 20, Width = 180, Text = _profile.UserInputGameSensitivity.ToString(CultureInfo.InvariantCulture) };
            _dpiBox = new TextBox { Left = 155, Top = 55, Width = 180, Text = _profile.MouseDPI.ToString(CultureInfo.InvariantCulture) };
            _pollingRateBox = new TextBox { Left = 155, Top = 90, Width = 180, Text = _profile.PollingRateHz.ToString(CultureInfo.InvariantCulture) };

            var startBtn = new Button { Text = "Start Training", Left = 20, Top = 130, Width = 410, Height = 40 };
            var saveBtn = new Button { Text = "Calibrate && Save Profile", Left = 20, Top = 180, Width = 410, Height = 40 };
            _profileStatus = new Label
            {
                Left = 20, Top = 230, Width = 410, Height = 38,
                Text = "Trials: " + _profile.Trials.Count + " | Profile loads from LocalAppData"
            };

            startBtn.Click += (s, e) =>
            {
                if (!TryReadSettings()) return;
                using (var trainer = new DesktopSensTrainerForm(_profile))
                {
                    EventHandler<MouseSample> handler = (sender, sample) => trainer.OnMouseSample(sample);
                    _accelGUI.MouseWatcher.RawMouseSampleReceived += handler;
                    try
                    {
                        Hide();
                        trainer.ShowDialog();
                    }
                    finally
                    {
                        Show();
                        _accelGUI.MouseWatcher.RawMouseSampleReceived -= handler;
                    }
                }
                _profileStatus.Text = "Trials: " + _profile.Trials.Count + " | Select Calibrate && Save to persist";
            };

            saveBtn.Click += (s, e) =>
            {
                if (!TryReadSettings()) return;
                if (_profile.Trials.Count == 0)
                {
                    MessageBox.Show(this, "No trials to calibrate yet.", "Desktop Sens",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                try
                {
                    DesktopSensCalibrator.Calibrate(_profile);
                    int rotationFits = CountUsableRotationModels(_profile);
                    Directory.CreateDirectory(Path.GetDirectoryName(ProfilePath));
                    File.WriteAllText(ProfilePath, JsonConvert.SerializeObject(_profile, Formatting.Indented));
                    _profileStatus.Text = "Saved. Coverage: " + (_profile.OverallCoverage * 100.0).ToString("0.0") +
                        "% | Rotation fits: " + rotationFits + "/" + DesktopSensConstants.DistanceZoneCount +
                        " | Trials: " + _profile.Trials.Count;
                    MessageBox.Show(this,
                        "Calibration profile saved to:\n" + ProfilePath +
                        "\n\nRotation models passing quality limits: " + rotationFits + "/" +
                        DesktopSensConstants.DistanceZoneCount +
                        "\n\nNote: saving a profile does not activate a runtime mouse transform. The calibrated factors are stored for offline inspection and preview; no Driver changes or live input transformation are performed.",
                        "Desktop Sens", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Could not save profile:\n" + ex.Message, "Desktop Sens",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            };

            Controls.Add(new Label { Text = "CS2 Sens:", Left = 20, Top = 23, Width = 120 });
            Controls.Add(new Label { Text = "Mouse DPI:", Left = 20, Top = 58, Width = 120 });
            Controls.Add(new Label { Text = "Polling rate (Hz):", Left = 20, Top = 93, Width = 130 });
            Controls.Add(_gameSensBox);
            Controls.Add(_dpiBox);
            Controls.Add(_pollingRateBox);
            Controls.Add(startBtn);
            Controls.Add(saveBtn);
            Controls.Add(_profileStatus);
        }

        private bool TryReadSettings()
        {
            if (!double.TryParse(_gameSensBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double gameSens) ||
                double.IsNaN(gameSens) || double.IsInfinity(gameSens) || gameSens <= 0.0)
            {
                MessageBox.Show(this, "CS2 sensitivity must be a positive number.", "Invalid settings",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            if (!int.TryParse(_dpiBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int dpi) ||
                dpi < DesktopSensConstants.MinMouseDPI || dpi > DesktopSensConstants.MaxMouseDPI)
            {
                MessageBox.Show(this, "DPI must be between " + DesktopSensConstants.MinMouseDPI + " and " +
                    DesktopSensConstants.MaxMouseDPI + ".", "Invalid settings",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            if (!double.TryParse(_pollingRateBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double pollingRate) ||
                double.IsNaN(pollingRate) || double.IsInfinity(pollingRate) ||
                pollingRate < DesktopSensConstants.MinPollingRateHz || pollingRate > 8000.0)
            {
                MessageBox.Show(this, "Polling rate must be between 125 and 8000 Hz.", "Invalid settings",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            _profile.UserInputGameSensitivity = gameSens;
            _profile.BaseSensitivity = gameSens;
            _profile.MouseDPI = dpi;
            _profile.PollingRateHz = pollingRate;
            return true;
        }

        private static int CountUsableRotationModels(DesktopSensProfile profile)
        {
            if (profile == null || profile.RotationModelsByDistanceZone == null) return 0;
            int count = 0;
            foreach (RotationModel model in profile.RotationModelsByDistanceZone)
                if (DesktopSensRotationModelFitter.IsUsable(model)) count++;
            return count;
        }

        private DesktopSensProfile LoadProfile()
        {
            try
            {
                if (File.Exists(ProfilePath))
                {
                    var profile = JsonConvert.DeserializeObject<DesktopSensProfile>(File.ReadAllText(ProfilePath));
                    if (profile != null)
                    {
                        if (profile.Trials == null) profile.Trials = new System.Collections.Generic.List<DesktopSensTrial>();
                        return profile;
                    }
                }
            }
            catch
            {
                // A corrupt/old profile should not prevent the trainer from opening.
            }
            return new DesktopSensProfile();
        }
    }
}
