namespace grapher.DesktopSens.Models
{
    public static class DesktopSensConstants
    {
        // Angular: 32 bins cover the full 360-degree mouse-movement direction.
        public const int AngularZoneCount = 32;
        public const double AngularZoneSizeDegrees = 11.25;

        // Distance zones are mapped dynamically from 0-3 cm base sensitivity to the
        // physical mouse travel that corresponds to a 180-degree CS2 turn.
        public const int DistanceZoneCount = 46;
        public const int MicroZoneCount = 6;
        public const int ShortZoneCount = 7;
        public const int MediumZoneCount = 5;
        public const int LongZoneCount = 28;

        public const double MicroZoneSizeCm = 0.5;
        public const double ShortZoneSizeCm = 1.0;
        public const double MediumZoneSizeCm = 2.0;
        public const double LongZoneSizeCm = 80.0 / LongZoneCount;

        public const double MicroZoneMaxCm = 3.0;
        public const double ShortZoneMaxCm = 10.0;
        public const double MediumZoneMaxCm = 20.0;
        public const double LongZoneMaxCm = 100.0;

        public const int ShortZoneStartIndex = 6;
        public const int MediumZoneStartIndex = 13;
        public const int LongZoneStartIndex = 18;

        // Rotation
        public const int RotationFourierOrder = 3;
        public const double RotationMaxResidualDegrees = 3.0;
        public const double RotationMinFitQuality = 0.7;

        // Flick completion
        public const double MovementStartVelocityThresholdPxPerSec = 50.0;
        public const double MovementEndVelocityThresholdPxPerSec = 5.0;
        public const double StabilizationPeriodMs = 150.0;

        // CS2 default m_yaw: one raw mouse count rotates the view by sensitivity * 0.022 degrees.
        public const double Cs2YawDegreesPerCount = 0.022;
        public const double MaxGameTurnDegrees = 180.0;

        // LUT
        public const int LutMaxPoints = 257;
        public const int LutMinPoints = 2;

        // Hardware
        public const int MinMouseDPI = 100;
        public const int MaxMouseDPI = 30000;
        public const double MinPollingRateHz = 125;
        public const double TargetPollingRateHz = 1000;

        // Schema
        public const int CurrentSchemaVersion = 4;
        public const int CurrentModelVersion = 4;

        // Conversion
        public const double InchesToCm = 2.54;
        public const double AspectRatioWidthFactor = 0.8716;
        public const double AspectRatioHeightFactor = 0.4903;
    }
}