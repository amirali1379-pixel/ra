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
            Height = 370;
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;

            _gameSensBox = new TextBox { Left = 155, Top = 20, Width = 180, Text = _profile.UserInputGameSensitivity.ToString(CultureInfo.InvariantCulture) };
            _dpiBox = new TextBox { Left = 155, Top = 55, Width = 180, Text = _profile.MouseDPI.ToString(CultureInfo.InvariantCulture) };
            _pollingRateBox = new TextBox { Left = 155, Top = 90, Width = 180, Text = _profile.PollingRateHz.ToString(CultureInfo.InvariantCulture) };

            var startBtn = new Button { Text = "Start Training", Left = 20, Top = 130, Width = 410, Height = 40 };
            var saveBtn = new Button { Text = "Calibrate 4 Directions && Save Profile", Left = 20, Top = 180, Width = 410, Height = 40 };
            var applyBtn = new Button { Text = "Apply Saved 4-Way Profile to RawAccel", Left = 20, Top = 230, Width = 410, Height = 40 };
            _profileStatus = new Label
            {
                Left = 20, Top = 280, Width = 410, Height = 48,
                Text = "Trials: " + _profile.Trials.Count + " | Profile loads from LocalAppData"
            };

            applyBtn.Click += (s, e) => ApplyDirectionalProfile();

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
                try
                {
                    SaveProfileToDisk();
                    _profileStatus.Text = "Trials saved: " + _profile.Trials.Count + " | Calibrate to update the four direction factors";
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Could not save training trials:\n" + ex.Message, "Desktop Sens",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
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
                    // Preserve the existing post-3 cm acceleration calibration model.
                    DesktopSensCalibrator.Calibrate(_profile);
                    // Directional sensitivity is independently reduced to the four cardinal axes.
                    DirectionalCalibrator.Calibrate(_profile);
                    SaveProfileToDisk();
                    _profileStatus.Text = "4-way profile saved | R " + _profile.RightSensitivityFactor.ToString("0.###", CultureInfo.InvariantCulture) +
                        " L " + _profile.LeftSensitivityFactor.ToString("0.###", CultureInfo.InvariantCulture) +
                        " U " + _profile.UpSensitivityFactor.ToString("0.###", CultureInfo.InvariantCulture) +
                        " D " + _profile.DownSensitivityFactor.ToString("0.###", CultureInfo.InvariantCulture);
                    MessageBox.Show(this,
                        "Four-way calibration profile saved to:\n" + ProfilePath +
                        "\n\nUse 'Apply Saved 4-Way Profile to RawAccel' to activate these factors.",
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
            Controls.Add(applyBtn);
            Controls.Add(_profileStatus);
        }

        private void SaveProfileToDisk()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ProfilePath));
            File.WriteAllText(ProfilePath, JsonConvert.SerializeObject(_profile, Formatting.Indented));
        }

        private void ApplyDirectionalProfile()
        {
            double right = PositiveOrOne(_profile.RightSensitivityFactor);
            double left = PositiveOrOne(_profile.LeftSensitivityFactor);
            double up = PositiveOrOne(_profile.UpSensitivityFactor);
            double down = PositiveOrOne(_profile.DownSensitivityFactor);

            try
            {
                var settings = _accelGUI.MakeSettingsFromFields();
                if (settings.outputDPI <= 0.0 || double.IsNaN(settings.outputDPI) || double.IsInfinity(settings.outputDPI))
                {
                    MessageBox.Show(this, "The current base sensitivity is invalid.", "Cannot apply",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                // Keep a stable base DPI in the saved profile so pressing Apply repeatedly
                // does not multiply the right-direction factor over and over.
                double baseOutputDPI = _profile.RawAccelBaseOutputDPI;
                if (double.IsNaN(baseOutputDPI) || double.IsInfinity(baseOutputDPI) || baseOutputDPI <= 0.0)
                    baseOutputDPI = settings.outputDPI;

                // RawAccel natively applies X direction by sign and Y direction by sign.
                // These ratios map the four independent calibrated gains without another input path.
                settings.outputDPI = baseOutputDPI * right;
                settings.lrOutputDPIRatio = left / right;
                settings.yxOutputDPIRatio = down / right;
                settings.udOutputDPIRatio = up / down;

                if (!_accelGUI.Settings.TryActivate(settings, out string errors))
                {
                    MessageBox.Show(this, errors ?? "RawAccel rejected the calibrated profile.", "Cannot apply",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                _profile.RawAccelBaseOutputDPI = baseOutputDPI;
                SaveProfileToDisk();
                _accelGUI.RefreshActive();
                _accelGUI.RefreshUser();
                _profileStatus.Text = "Applied to RawAccel | R " + right.ToString("0.###", CultureInfo.InvariantCulture) +
                    " L " + left.ToString("0.###", CultureInfo.InvariantCulture) +
                    " U " + up.ToString("0.###", CultureInfo.InvariantCulture) +
                    " D " + down.ToString("0.###", CultureInfo.InvariantCulture);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not apply directional profile:\n" + ex.Message, "Desktop Sens",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static double PositiveOrOne(double value)
        {
            return double.IsNaN(value) || double.IsInfinity(value) || value <= 0.0 ? 1.0 : value;
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
