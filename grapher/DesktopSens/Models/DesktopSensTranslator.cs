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

            if (distanceCm <= DesktopSensConstants.MicroZoneMaxCm)
                return ReadFactor(profile.CellSensitivityFactor, cellIndex, 1.0);

            double baseFactor = ReadFactor(profile.CellSensitivityFactor, cellIndex, 1.0);
            double accelerationFactor = ReadFactor(profile.CellAccelerationFactor, cellIndex, 1.0);
            return baseFactor * accelerationFactor;
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
