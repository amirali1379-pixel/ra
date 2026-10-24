using System;
using grapher.DesktopSens.Models;

namespace grapher.DesktopSens.Models
{
    /// <summary>
    /// Calibrates the four cardinal directions used by RawAccel's native X/Y sign ratios.
    /// The trainer records target-axis error; this class converts those observations into
    /// bounded sensitivity multipliers and persists confidence alongside the profile.
    /// </summary>
    public static class DirectionalCalibrator
    {
        private const double MinimumFactor = 0.5;
        private const double MaximumFactor = 2.0;
        private const double MinimumObservedDistanceCm = 0.05;
        private const double BaseSensitivityDistanceLimitCm = 3.0;

        public static void Calibrate(DesktopSensProfile profile)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            if (profile.Trials == null) profile.Trials = new System.Collections.Generic.List<DesktopSensTrial>();

            var targetTotals = new double[4];
            var observedTotals = new double[4];
            var counts = new int[4];

            foreach (DesktopSensTrial trial in profile.Trials)
            {
                // Directional sensitivity is learned only in the first 3 cm. Longer flicks
                // belong to the existing acceleration calibration and must not bias base gains.
                if (trial == null || !trial.HasMovementMeasurement ||
                    !IsFinite(trial.TargetDistanceCm) || trial.TargetDistanceCm <= 0.0 ||
                    trial.TargetDistanceCm > BaseSensitivityDistanceLimitCm)
                    continue;

                double error = trial.SignedErrorCm;
                // Older trials may only contain the legacy final error. Keep their sign if
                // the projected measurement is absent, matching the legacy calibrator.
                if (Math.Abs(error) < 0.000001 &&
                    Math.Abs(trial.ProjectedDistanceCm) < 0.000001 &&
                    trial.FinalErrorCm > 0.0)
                    error = trial.IsUnderFlick ? -trial.FinalErrorCm : trial.FinalErrorCm;

                double observed = trial.TargetDistanceCm + error;
                if (!IsFinite(error) || !IsFinite(observed) || observed <= MinimumObservedDistanceCm)
                    continue;

                int direction = DirectionFromAngle(trial.TargetAngleDegrees);
                targetTotals[direction] += trial.TargetDistanceCm;
                observedTotals[direction] += observed;
                counts[direction]++;
            }

            double[] factors =
            {
                profile.RightSensitivityFactor,
                profile.DownSensitivityFactor,
                profile.LeftSensitivityFactor,
                profile.UpSensitivityFactor
            };

            double[] confidence =
            {
                profile.RightSensitivityConfidence,
                profile.DownSensitivityConfidence,
                profile.LeftSensitivityConfidence,
                profile.UpSensitivityConfidence
            };

            for (int direction = 0; direction < 4; direction++)
            {
                if (counts[direction] == 0 || observedTotals[direction] <= MinimumObservedDistanceCm)
                {
                    confidence[direction] = 0.0;
                    continue;
                }

                factors[direction] = Clamp(targetTotals[direction] / observedTotals[direction],
                    MinimumFactor, MaximumFactor);
                confidence[direction] = Math.Min(1.0, counts[direction] / 10.0);
            }

            profile.RightSensitivityFactor = factors[0];
            profile.DownSensitivityFactor = factors[1];
            profile.LeftSensitivityFactor = factors[2];
            profile.UpSensitivityFactor = factors[3];
            profile.RightSensitivityConfidence = confidence[0];
            profile.DownSensitivityConfidence = confidence[1];
            profile.LeftSensitivityConfidence = confidence[2];
            profile.UpSensitivityConfidence = confidence[3];
            profile.ModifiedTimestamp = DateTime.UtcNow;
        }

        // Canvas coordinates use +X right and +Y down.
        private static int DirectionFromAngle(double angleDegrees)
        {
            if (!IsFinite(angleDegrees)) return 0;
            angleDegrees %= 360.0;
            if (angleDegrees < 0.0) angleDegrees += 360.0;
            return ((int)Math.Floor((angleDegrees + 45.0) / 90.0)) % 4;
        }

        private static double Clamp(double value, double min, double max)
        {
            if (!IsFinite(value)) return 1.0;
            return Math.Max(min, Math.Min(max, value));
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
