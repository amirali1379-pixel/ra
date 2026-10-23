using System;
using System.Collections.Generic;

namespace grapher.DesktopSens.Models
{
    public class DesktopSensProfile
    {
        public int SchemaVersion = 1;
        public int ModelVersion = 1;
        public string ProfileId = Guid.NewGuid().ToString();
        public DateTime CreatedTimestamp = DateTime.UtcNow;
        public DateTime ModifiedTimestamp = DateTime.UtcNow;

        public double UserInputGameSensitivity;
        public double BaseSensitivity = 1.0;
        public double BaseSensitivityConfidence = 0.0;

        public int MouseDPI = 800;
        public double PollingRateHz = 1000.0;

        public double[] SensitivityFactor = new double[32];
        public double[] SensitivityConfidence = new double[32];
        public double[] AccelerationFactor = new double[23];
        public double[] AccelerationConfidence = new double[23];

        public RotationModel Rotation = new RotationModel();

        public int CellsStable = 0;
        public int CellsLowConfidence = 0;
        public int CellsInsufficientData = 32 * 23;
        public double OverallCoverage = 0.0;
        public double OverallConfidence = 0.0;

        public List<DesktopSensTrial> Trials = new List<DesktopSensTrial>();

        public DesktopSensProfile()
        {
            for (int i = 0; i < SensitivityFactor.Length; i++) SensitivityFactor[i] = 1.0;
            for (int i = 0; i < AccelerationFactor.Length; i++) AccelerationFactor[i] = 1.0;
        }
    }
}