using System;
using System.Collections.Generic;

namespace grapher.DesktopSens.Models
{
    public class DesktopSensProfile
    {
        public int SchemaVersion = DesktopSensConstants.CurrentSchemaVersion;
        public int ModelVersion = DesktopSensConstants.CurrentModelVersion;
        public string ProfileId = Guid.NewGuid().ToString();
        public DateTime CreatedTimestamp = DateTime.UtcNow;
        public DateTime ModifiedTimestamp = DateTime.UtcNow;

        public double UserInputGameSensitivity = 1.0;
        public double BaseSensitivity = 1.0;
        public double BaseSensitivityConfidence = 0.0;

        public int MouseDPI = 800;
        public double PollingRateHz = 1000.0;

        // Legacy one-dimensional fields are retained so existing profile files can still be read.
        public double[] SensitivityFactor = new double[DesktopSensConstants.AngularZoneCount];
        public double[] SensitivityConfidence = new double[DesktopSensConstants.AngularZoneCount];
        public double[] AccelerationFactor = new double[DesktopSensConstants.DistanceZoneCount];
        public double[] AccelerationConfidence = new double[DesktopSensConstants.DistanceZoneCount];

        // New per-cell values: one independent value for each angle (32) x distance zone (23).
        // Cell index = angleZone * DistanceZoneCount + distanceZone.
        public double[] CellSensitivityFactor = new double[DesktopSensConstants.AngularZoneCount * DesktopSensConstants.DistanceZoneCount];
        public double[] CellSensitivityConfidence = new double[DesktopSensConstants.AngularZoneCount * DesktopSensConstants.DistanceZoneCount];
        public double[] CellAccelerationFactor = new double[DesktopSensConstants.AngularZoneCount * DesktopSensConstants.DistanceZoneCount];
        public double[] CellAccelerationConfidence = new double[DesktopSensConstants.AngularZoneCount * DesktopSensConstants.DistanceZoneCount];

        // Signed rotation correction in degrees for each angle/distance cell.
        public double[] CellRotationOffsetDegrees = new double[DesktopSensConstants.AngularZoneCount * DesktopSensConstants.DistanceZoneCount];
        public double[] CellRotationConfidence = new double[DesktopSensConstants.AngularZoneCount * DesktopSensConstants.DistanceZoneCount];

        // Legacy global rotation summary retained for older profile consumers.
        public RotationModel Rotation = new RotationModel();

        // Rotation is fitted independently for each distance zone from observations across
        // the 32 angular axes. Raw per-cell offsets remain available as the primary lookup.
        public RotationModel[] RotationModelsByDistanceZone =
            new RotationModel[DesktopSensConstants.DistanceZoneCount];

        public int CellsStable = 0;
        public int CellsLowConfidence = 0;
        public int CellsInsufficientData = DesktopSensConstants.AngularZoneCount * DesktopSensConstants.DistanceZoneCount;
        public double OverallCoverage = 0.0;
        public double OverallConfidence = 0.0;

        public List<DesktopSensTrial> Trials = new List<DesktopSensTrial>();

        public DesktopSensProfile()
        {
            InitializeFactors(SensitivityFactor, 1.0);
            InitializeFactors(AccelerationFactor, 1.0);
            InitializeFactors(CellSensitivityFactor, 1.0);
            InitializeFactors(CellAccelerationFactor, 1.0);
            InitializeFactors(CellRotationOffsetDegrees, 0.0);
            InitializeFactors(SensitivityConfidence, 0.0);
            InitializeFactors(AccelerationConfidence, 0.0);
            InitializeFactors(CellSensitivityConfidence, 0.0);
            InitializeFactors(CellAccelerationConfidence, 0.0);
            InitializeFactors(CellRotationConfidence, 0.0);
            for (int i = 0; i < RotationModelsByDistanceZone.Length; i++)
            {
                RotationModelsByDistanceZone[i] = new RotationModel
                {
                    Type = RotationModelType.FourierOrder3,
                    Coefficients = new double[0]
                };
            }
        }

        public static int GetCellIndex(int angleZoneIndex, int distanceZoneIndex)
        {
            if (angleZoneIndex < 0 || angleZoneIndex >= DesktopSensConstants.AngularZoneCount)
                throw new ArgumentOutOfRangeException(nameof(angleZoneIndex));
            if (distanceZoneIndex < 0 || distanceZoneIndex >= DesktopSensConstants.DistanceZoneCount)
                throw new ArgumentOutOfRangeException(nameof(distanceZoneIndex));
            return angleZoneIndex * DesktopSensConstants.DistanceZoneCount + distanceZoneIndex;
        }

        private static void InitializeFactors(double[] values, double initialValue)
        {
            if (values == null) return;
            for (int i = 0; i < values.Length; i++) values[i] = initialValue;
        }
    }
}
