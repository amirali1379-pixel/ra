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