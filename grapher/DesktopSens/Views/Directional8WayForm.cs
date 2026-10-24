using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using grapher;

namespace grapher.DesktopSens.Views
{
    /// <summary>
    /// Eight-way sensitivity using RawAccel's existing output-axis/sign ratios.
    /// The four cardinal gains are independently configurable. Diagonal gains are
    /// the resulting component-wise vector gains; no second input path is created.
    /// </summary>
    public sealed class Directional8WayForm : Form
    {
        private readonly AccelGUI _accelGUI;
        private readonly TextBox _rightBox;
        private readonly TextBox _leftBox;
        private readonly TextBox _upBox;
        private readonly TextBox _downBox;
        private readonly Label _northEastValue;
        private readonly Label _northWestValue;
        private readonly Label _southEastValue;
        private readonly Label _southWestValue;
        private readonly Label _status;

        public Directional8WayForm(AccelGUI accelGUI)
        {
            _accelGUI = accelGUI ?? throw new ArgumentNullException(nameof(accelGUI));

            Text = "Directional Sensitivity — 8 Way";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(610, 385);

            var active = _accelGUI.Settings.ActiveProfile;
            double down = PositiveOrOne(active.yxOutputDPIRatio);
            double up = PositiveOrOne(active.yxOutputDPIRatio * active.udOutputDPIRatio);
            double left = PositiveOrOne(active.lrOutputDPIRatio);

            Controls.Add(new Label {
                Text = "Cardinal multipliers (relative to the current base sensitivity)",
                Left = 18, Top = 15, Width = 560, Height = 24,
                Font = new Font(Font, FontStyle.Bold)
            });

            _rightBox = AddMultiplier("Right / East", 20, 55, 1.0);
            _leftBox = AddMultiplier("Left / West", 315, 55, left);
            _upBox = AddMultiplier("Up / North", 20, 105, up);
            _downBox = AddMultiplier("Down / South", 315, 105, down);

            Controls.Add(new Label {
                Text = "Derived diagonal gain (45° movement; not independent controls)",
                Left = 18, Top = 165, Width = 560, Height = 22,
                Font = new Font(Font, FontStyle.Bold)
            });

            _northWestValue = AddDiagonal("North-West", 20, 200);
            _northEastValue = AddDiagonal("North-East", 315, 200);
            _southWestValue = AddDiagonal("South-West", 20, 235);
            _southEastValue = AddDiagonal("South-East", 315, 235);

            _status = new Label {
                Left = 20, Top = 273, Width = 565, Height = 38,
                Text = "Changes are applied through the existing RawAccel profile/IO path."
            };
            Controls.Add(_status);

            var applyButton = new Button {
                Text = "Apply to RawAccel", Left = 315, Top = 326, Width = 165, Height = 34
            };
            applyButton.Click += ApplyClicked;
            Controls.Add(applyButton);

            var closeButton = new Button {
                Text = "Close", Left = 490, Top = 326, Width = 95, Height = 34,
                DialogResult = DialogResult.Cancel
            };
            Controls.Add(closeButton);
            CancelButton = closeButton;

            _rightBox.TextChanged += (s, e) => UpdateDiagonalPreview();
            _leftBox.TextChanged += (s, e) => UpdateDiagonalPreview();
            _upBox.TextChanged += (s, e) => UpdateDiagonalPreview();
            _downBox.TextChanged += (s, e) => UpdateDiagonalPreview();
            UpdateDiagonalPreview();
        }

        private TextBox AddMultiplier(string label, int left, int top, double value)
        {
            Controls.Add(new Label {
                Text = label, Left = left, Top = top + 5, Width = 115, Height = 22
            });
            var box = new TextBox {
                Left = left + 120, Top = top, Width = 145, Height = 24,
                Text = value.ToString("0.###", CultureInfo.InvariantCulture)
            };
            Controls.Add(box);
            return box;
        }

        private Label AddDiagonal(string label, int left, int top)
        {
            var name = new Label { Text = label + ":", Left = left, Top = top + 3, Width = 115 };
            var value = new Label { Text = "—", Left = left + 120, Top = top + 3, Width = 145 };
            Controls.Add(name);
            Controls.Add(value);
            return value;
        }

        private void UpdateDiagonalPreview()
        {
            if (!TryReadMultipliers(out double right, out double left, out double up, out double down))
            {
                _northWestValue.Text = _northEastValue.Text =
                    _southWestValue.Text = _southEastValue.Text = "Enter positive values";
                return;
            }

            _northWestValue.Text = DiagonalGain(left, up).ToString("0.###", CultureInfo.InvariantCulture) + "×";
            _northEastValue.Text = DiagonalGain(right, up).ToString("0.###", CultureInfo.InvariantCulture) + "×";
            _southWestValue.Text = DiagonalGain(left, down).ToString("0.###", CultureInfo.InvariantCulture) + "×";
            _southEastValue.Text = DiagonalGain(right, down).ToString("0.###", CultureInfo.InvariantCulture) + "×";
        }

        private static double DiagonalGain(double horizontal, double vertical)
        {
            // Resultant magnitude for equal-length X/Y components at 45 degrees.
            return Math.Sqrt((horizontal * horizontal + vertical * vertical) / 2.0);
        }

        private bool TryReadMultipliers(out double right, out double left, out double up, out double down)
        {
            return TryReadPositive(_rightBox.Text, out right) &&
                   TryReadPositive(_leftBox.Text, out left) &&
                   TryReadPositive(_upBox.Text, out up) &&
                   TryReadPositive(_downBox.Text, out down);
        }

        private static bool TryReadPositive(string text, out double value)
        {
            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) &&
                   !double.IsNaN(value) && !double.IsInfinity(value) && value > 0.0;
        }

        private static double PositiveOrOne(double value)
        {
            return double.IsNaN(value) || double.IsInfinity(value) || value <= 0.0 ? 1.0 : value;
        }

        private void ApplyClicked(object sender, EventArgs e)
        {
            if (!TryReadMultipliers(out double right, out double left, out double up, out double down))
            {
                MessageBox.Show(this, "All four multipliers must be finite numbers greater than zero.",
                    "Invalid directional sensitivity", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                // Start from the current GUI profile so acceleration curves and device
                // settings remain intact. Then map the four cardinal gains to fields
                // already supported by the existing RawAccel processing path.
                var settings = _accelGUI.MakeSettingsFromFields();
                if (settings.outputDPI <= 0.0 || double.IsNaN(settings.outputDPI) ||
                    double.IsInfinity(settings.outputDPI))
                {
                    MessageBox.Show(this, "The current base sensitivity is invalid.",
                        "Cannot apply", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                settings.outputDPI *= right;
                settings.lrOutputDPIRatio = left / right;
                settings.yxOutputDPIRatio = down / right;
                settings.udOutputDPIRatio = up / down;

                if (!_accelGUI.Settings.TryActivate(settings, out string errors))
                {
                    MessageBox.Show(this, errors ?? "RawAccel rejected the settings.",
                        "Cannot apply", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                _accelGUI.RefreshActive();
                _accelGUI.RefreshUser();
                _status.Text = "Applied. Diagonals combine the horizontal and vertical component gains.";
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not apply directional sensitivity:\n" + ex.Message,
                    "Directional Sensitivity", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
