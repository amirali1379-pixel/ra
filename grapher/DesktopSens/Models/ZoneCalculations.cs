using System;

namespace grapher.DesktopSens.Models
{
    public static class ZoneCalculations
    {
        public static int AngleToZoneIndex(double angleDegrees)
        {
            angleDegrees = NormalizeAngle(angleDegrees);
            int idx = (int)Math.Floor(angleDegrees / DesktopSensConstants.AngularZoneSizeDegrees);
            if (idx < 0) idx = 0;
            if (idx >= DesktopSensConstants.AngularZoneCount) idx = DesktopSensConstants.AngularZoneCount - 1;
            return idx;
        }

        public static int DistanceToZoneIndex(double distanceCm)
        {
            if (distanceCm < 0) distanceCm = 0;

            if (distanceCm < DesktopSensConstants.MicroZoneMaxCm)
            {
                int idx = (int)Math.Floor(distanceCm / DesktopSensConstants.MicroZoneSizeCm);
                if (idx < 0) idx = 0;
                if (idx >= DesktopSensConstants.MicroZoneCount) idx = DesktopSensConstants.MicroZoneCount - 1;
                return idx;
            }

            if (distanceCm < DesktopSensConstants.ShortZoneMaxCm)
            {
                int idx = (int)Math.Floor((distanceCm - DesktopSensConstants.MicroZoneMaxCm) / DesktopSensConstants.ShortZoneSizeCm);
                if (idx < 0) idx = 0;
                if (idx >= DesktopSensConstants.ShortZoneCount) idx = DesktopSensConstants.ShortZoneCount - 1;
                return DesktopSensConstants.ShortZoneStartIndex + idx;
            }

            if (distanceCm < DesktopSensConstants.MediumZoneMaxCm)
            {
                int idx = (int)Math.Floor((distanceCm - DesktopSensConstants.ShortZoneMaxCm) / DesktopSensConstants.MediumZoneSizeCm);
                if (idx < 0) idx = 0;
                if (idx >= DesktopSensConstants.MediumZoneCount) idx = DesktopSensConstants.MediumZoneCount - 1;
                return DesktopSensConstants.MediumZoneStartIndex + idx;
            }

            if (distanceCm < DesktopSensConstants.LongZoneMaxCm)
            {
                int idx = (int)Math.Floor((distanceCm - DesktopSensConstants.MediumZoneMaxCm) / DesktopSensConstants.LongZoneSizeCm);
                if (idx < 0) idx = 0;
                if (idx >= DesktopSensConstants.LongZoneCount) idx = DesktopSensConstants.LongZoneCount - 1;
                return DesktopSensConstants.LongZoneStartIndex + idx;
            }

            return DesktopSensConstants.DistanceZoneCount - 1;
        }

        // Convert CS2 turn range to physical mouse travel for the current DPI and sensitivity.
        public static double MaxDistanceCmForTurnDegrees(double mouseDpi, double gameSensitivity)
        {
            if (!IsFinite(mouseDpi) || mouseDpi <= 0.0 ||
                !IsFinite(gameSensitivity) || gameSensitivity <= 0.0)
                throw new ArgumentOutOfRangeException(nameof(gameSensitivity), "DPI and game sensitivity must be positive and finite.");

            double countsPerCm = mouseDpi / DesktopSensConstants.InchesToCm;
            double degreesPerCount = gameSensitivity * DesktopSensConstants.Cs2YawDegreesPerCount;
            return DesktopSensConstants.MaxGameTurnDegrees / (countsPerCm * degreesPerCount);
        }

        // New model: keep the 0-3 cm base-sensitivity band, then distribute all remaining
        // zones evenly across the physical travel needed for a 180-degree in-game turn.
        public static int DistanceToZoneIndex(double distanceCm, double maxDistanceCm)
        {
            if (!IsFinite(distanceCm)) distanceCm = 0.0;
            if (distanceCm < 0.0) distanceCm = 0.0;
            if (!IsFinite(maxDistanceCm) || maxDistanceCm <= 0.0)
                return DistanceToZoneIndex(distanceCm);

            double baseMax = Math.Min(DesktopSensConstants.MicroZoneMaxCm, maxDistanceCm);
            if (distanceCm < baseMax && baseMax > 0.0)
            {
                int baseIndex = (int)Math.Floor(distanceCm / baseMax * DesktopSensConstants.MicroZoneCount);
                return Math.Max(0, Math.Min(DesktopSensConstants.MicroZoneCount - 1, baseIndex));
            }

            int accelerationZoneCount = DesktopSensConstants.DistanceZoneCount - DesktopSensConstants.MicroZoneCount;
            double accelerationSpan = maxDistanceCm - baseMax;
            if (accelerationZoneCount <= 0 || accelerationSpan <= 0.0)
                return DesktopSensConstants.DistanceZoneCount - 1;

            int accelerationIndex = (int)Math.Floor((distanceCm - baseMax) / accelerationSpan * accelerationZoneCount);
            accelerationIndex = Math.Max(0, Math.Min(accelerationZoneCount - 1, accelerationIndex));
            return DesktopSensConstants.MicroZoneCount + accelerationIndex;
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        public static double ZoneIndexToDistanceCenterCm(int zoneIndex)
        {
            if (zoneIndex < DesktopSensConstants.ShortZoneStartIndex)
                return (zoneIndex + 0.5) * DesktopSensConstants.MicroZoneSizeCm;

            if (zoneIndex < DesktopSensConstants.MediumZoneStartIndex)
            {
                int i = zoneIndex - DesktopSensConstants.ShortZoneStartIndex;
                return DesktopSensConstants.MicroZoneMaxCm + (i + 0.5) * DesktopSensConstants.ShortZoneSizeCm;
            }

            if (zoneIndex < DesktopSensConstants.LongZoneStartIndex)
            {
                int i = zoneIndex - DesktopSensConstants.MediumZoneStartIndex;
                return DesktopSensConstants.ShortZoneMaxCm + (i + 0.5) * DesktopSensConstants.MediumZoneSizeCm;
            }

            int j = zoneIndex - DesktopSensConstants.LongZoneStartIndex;
            return DesktopSensConstants.MediumZoneMaxCm + (j + 0.5) * DesktopSensConstants.LongZoneSizeCm;
        }

        public static double AngleZoneCenterDegrees(int zoneIndex)
        {
            return (zoneIndex + 0.5) * DesktopSensConstants.AngularZoneSizeDegrees;
        }

        public static double NormalizeAngle(double angleDegrees)
        {
            while (angleDegrees < 0) angleDegrees += 360;
            while (angleDegrees >= 360) angleDegrees -= 360;
            return angleDegrees;
        }
    }
}