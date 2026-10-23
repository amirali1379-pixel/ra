using System;

namespace grapher.DesktopSens.Models
{
    /// <summary>
    /// Pure profile lookup/conversion helpers. This class does not inject input or modify the
    /// Raw Accel driver; it provides the calibrated multiplier for a future/explicit runtime hook.
    /// </summary>
    public static class DesktopSensTranslator
    {
        public static double GetMultiplier(DesktopSensProfile profile, double angleDegrees, double distanceCm)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            if (distanceCm < 0.0 || double.IsNaN(distanceCm) || double.IsInfinity(distanceCm))
                throw new ArgumentOutOfRangeException(nameof(distanceCm));

            int angleIndex = ZoneCalculations.AngleToZoneIndex(angleDegrees);
            int distanceIndex = ZoneCalculations.DistanceToZoneIndex(distanceCm);
            int cellIndex = DesktopSensProfile.GetCellIndex(angleIndex, distanceIndex);

            // Sensitivity is per axis (32 angular zones). It remains the base multiplier at
            // every distance; acceleration is added only after the first 3 cm.
            double baseFactor = ReadFactor(profile.SensitivityFactor, angleIndex, 1.0);
            if (distanceCm <= DesktopSensConstants.MicroZoneMaxCm)
                return baseFactor;

            double accelerationFactor = ReadFactor(profile.CellAccelerationFactor, cellIndex, 1.0);
            return baseFactor * accelerationFactor;
        }

        public static double GetRotationCorrectionDegrees(DesktopSensProfile profile, double angleDegrees, double distanceCm)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            if (distanceCm < 0.0 || double.IsNaN(distanceCm) || double.IsInfinity(distanceCm))
                throw new ArgumentOutOfRangeException(nameof(distanceCm));

            int angleIndex = ZoneCalculations.AngleToZoneIndex(angleDegrees);
            int distanceIndex = ZoneCalculations.DistanceToZoneIndex(distanceCm);
            int cellIndex = DesktopSensProfile.GetCellIndex(angleIndex, distanceIndex);

            double cellCorrection = 0.0;
            bool hasCellCorrection = profile.CellRotationOffsetDegrees != null &&
                cellIndex < profile.CellRotationOffsetDegrees.Length &&
                !double.IsNaN(profile.CellRotationOffsetDegrees[cellIndex]) &&
                !double.IsInfinity(profile.CellRotationOffsetDegrees[cellIndex]);
            if (hasCellCorrection)
                cellCorrection = profile.CellRotationOffsetDegrees[cellIndex];

            // Keep the calibrated per-axis/per-distance cell as the primary source whenever it
            // has evidence. Non-zero values are also honored for older profiles without confidence.
            bool hasCellConfidence = profile.CellRotationConfidence != null &&
                cellIndex < profile.CellRotationConfidence.Length &&
                profile.CellRotationConfidence[cellIndex] > 0.0;
            if (hasCellCorrection && (hasCellConfidence || Math.Abs(cellCorrection) > 1e-9))
                return NormalizeSignedAngle(cellCorrection);

            // When a particular cell has not been measured, use only a validated Fourier model
            // fitted for that same distance zone. Never fall back to one global 1-D rotation curve.
            if (profile.RotationModelsByDistanceZone != null &&
                distanceIndex < profile.RotationModelsByDistanceZone.Length)
            {
                RotationModel model = profile.RotationModelsByDistanceZone[distanceIndex];
                if (DesktopSensRotationModelFitter.IsUsable(model))
                    return DesktopSensRotationModelFitter.EvaluateDegrees(model, angleDegrees);
            }

            return hasCellCorrection ? NormalizeSignedAngle(cellCorrection) : 0.0;
        }

        private static double NormalizeSignedAngle(double angle)
        {
            angle %= 360.0;
            if (angle > 180.0) angle -= 360.0;
            if (angle < -180.0) angle += 360.0;
            return angle;
        }

        private static double ReadFactor(double[] values, int index, double fallback)
        {
            if (values == null || index < 0 || index >= values.Length)
                return fallback;
            double value = values[index];
            return double.IsNaN(value) || double.IsInfinity(value) || value <= 0.0 ? fallback : value;
        }
    }
}
