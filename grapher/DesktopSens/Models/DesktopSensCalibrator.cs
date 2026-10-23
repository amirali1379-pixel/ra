using System;
using System.Collections.Generic;
using System.Linq;

namespace grapher.DesktopSens.Models
{
    public class ZoneData
    {
        public int AngleZoneIndex;
        public int DistanceZoneIndex;
        public List<double> SignedErrors = new List<double>();
        public List<double> TargetDistances = new List<double>();
        public List<double> RotationErrors = new List<double>();

        public double MeanError => SignedErrors.Count > 0 ? SignedErrors.Average() : 0.0;
        public double MeanRotationError
        {
            get
            {
                if (RotationErrors.Count == 0) return 0.0;
                double sin = 0.0;
                double cos = 0.0;
                foreach (double angle in RotationErrors)
                {
                    double radians = angle * Math.PI / 180.0;
                    sin += Math.Sin(radians);
                    cos += Math.Cos(radians);
                }
                return Math.Atan2(sin, cos) * 180.0 / Math.PI;
            }
        }
        public double Variance => SignedErrors.Count > 1
            ? SignedErrors.Select(e => Math.Pow(e - MeanError, 2)).Average()
            : 0.0;
    }

    public static class DesktopSensCalibrator
    {
        private const double MinimumDenominatorCm = 0.05;
        private const double MinimumFactor = 0.5;
        private const double MaximumFactor = 2.0;
        private const double BaseZoneEndCm = 3.0;

        public static void Calibrate(DesktopSensProfile profile)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            if (profile.Trials == null) profile.Trials = new List<DesktopSensTrial>();

            EnsureArray(ref profile.CellSensitivityFactor, DesktopSensConstants.AngularZoneCount * DesktopSensConstants.DistanceZoneCount, 1.0);
            EnsureArray(ref profile.CellSensitivityConfidence, DesktopSensConstants.AngularZoneCount * DesktopSensConstants.DistanceZoneCount, 0.0);
            EnsureArray(ref profile.CellAccelerationFactor, DesktopSensConstants.AngularZoneCount * DesktopSensConstants.DistanceZoneCount, 1.0);
            EnsureArray(ref profile.CellAccelerationConfidence, DesktopSensConstants.AngularZoneCount * DesktopSensConstants.DistanceZoneCount, 0.0);
            EnsureArray(ref profile.CellRotationOffsetDegrees, DesktopSensConstants.AngularZoneCount * DesktopSensConstants.DistanceZoneCount, 0.0);
            EnsureArray(ref profile.CellRotationConfidence, DesktopSensConstants.AngularZoneCount * DesktopSensConstants.DistanceZoneCount, 0.0);
            EnsureArray(ref profile.SensitivityFactor, DesktopSensConstants.AngularZoneCount, 1.0);
            EnsureArray(ref profile.SensitivityConfidence, DesktopSensConstants.AngularZoneCount, 0.0);
            EnsureArray(ref profile.AccelerationFactor, DesktopSensConstants.DistanceZoneCount, 1.0);
            EnsureArray(ref profile.AccelerationConfidence, DesktopSensConstants.DistanceZoneCount, 0.0);

            // Recompute from the current trial set. Never leave stale factors in cells whose
            // observations were removed or are no longer valid.
            ResetArray(profile.CellSensitivityFactor, 1.0);
            ResetArray(profile.CellSensitivityConfidence, 0.0);
            ResetArray(profile.CellAccelerationFactor, 1.0);
            ResetArray(profile.CellAccelerationConfidence, 0.0);
            ResetArray(profile.CellRotationOffsetDegrees, 0.0);
            ResetArray(profile.CellRotationConfidence, 0.0);
            ResetArray(profile.SensitivityFactor, 1.0);
            ResetArray(profile.SensitivityConfidence, 0.0);
            ResetArray(profile.AccelerationFactor, 1.0);
            ResetArray(profile.AccelerationConfidence, 0.0);

            var cells = new Dictionary<int, ZoneData>();
            foreach (var trial in profile.Trials)
            {
                if (trial == null || !trial.HasMovementMeasurement ||
                    trial.AngleZoneIndex < 0 || trial.AngleZoneIndex >= DesktopSensConstants.AngularZoneCount ||
                    trial.DistanceZoneIndex < 0 || trial.DistanceZoneIndex >= DesktopSensConstants.DistanceZoneCount ||
                    !IsFinite(trial.TargetDistanceCm) || trial.TargetDistanceCm <= 0.0)
                    continue;

                int cellIndex = DesktopSensProfile.GetCellIndex(trial.AngleZoneIndex, trial.DistanceZoneIndex);
                if (!cells.TryGetValue(cellIndex, out ZoneData zone))
                {
                    zone = new ZoneData
                    {
                        AngleZoneIndex = trial.AngleZoneIndex,
                        DistanceZoneIndex = trial.DistanceZoneIndex
                    };
                    cells.Add(cellIndex, zone);
                }

                // Legacy trials have no projection measurement. Only use the legacy fallback
                // when ProjectedDistanceCm is also absent; a new trial can have zero along-axis
                // error but a non-zero lateral landing error.
                double signedError = trial.SignedErrorCm;
                if (Math.Abs(signedError) < 0.000001 &&
                    Math.Abs(trial.ProjectedDistanceCm) < 0.000001 &&
                    trial.FinalErrorCm > 0.0)
                    signedError = trial.IsUnderFlick ? -trial.FinalErrorCm : trial.FinalErrorCm;

                if (!IsFinite(signedError)) continue;
                zone.SignedErrors.Add(signedError);
                zone.TargetDistances.Add(trial.TargetDistanceCm);
                if (trial.HasRotationMeasurement && IsFinite(trial.SignedRotationErrorDegrees))
                    zone.RotationErrors.Add(trial.SignedRotationErrorDegrees);
            }

            var angleErrorTotals = new double[DesktopSensConstants.AngularZoneCount];
            var angleCounts = new int[DesktopSensConstants.AngularZoneCount];
            var distanceFactorTotals = new double[DesktopSensConstants.DistanceZoneCount];
            var distanceCounts = new int[DesktopSensConstants.DistanceZoneCount];

            foreach (var pair in cells)
            {
                int cellIndex = pair.Key;
                ZoneData zone = pair.Value;
                double targetCm = zone.TargetDistances.Average();
                double signedErrorCm = zone.MeanError;
                if (zone.RotationErrors.Count > 0)
                {
                    profile.CellRotationOffsetDegrees[cellIndex] = zone.MeanRotationError;
                    profile.CellRotationConfidence[cellIndex] = Confidence(zone.RotationErrors.Count);
                }
                double observedCm = targetCm + signedErrorCm;
                double factor;

                if (targetCm <= BaseZoneEndCm)
                {
                    factor = SafeRatio(targetCm, observedCm);
                    profile.CellSensitivityFactor[cellIndex] = Clamp(factor, MinimumFactor, MaximumFactor);
                    profile.CellSensitivityConfidence[cellIndex] = Confidence(zone.SignedErrors.Count);
                }
                else
                {
                    // Preserve the first 3 cm as the base-sensitivity region. Only the excess
                    // distance contributes to acceleration. If the raw flick never reaches 3 cm,
                    // acceleration cannot explain/correct that result, so leave this cell untrained.
                    double desiredExcess = targetCm - BaseZoneEndCm;
                    double observedExcess = observedCm - BaseZoneEndCm;
                    if (observedExcess > MinimumDenominatorCm)
                    {
                        factor = SafeRatio(desiredExcess, observedExcess);
                        profile.CellAccelerationFactor[cellIndex] = Clamp(factor, MinimumFactor, MaximumFactor);
                        profile.CellAccelerationConfidence[cellIndex] = Confidence(zone.SignedErrors.Count);
                    }
                }

                // Only observations for the relevant correction dimension contribute to legacy summaries.
                if (targetCm <= BaseZoneEndCm)
                {
                    angleErrorTotals[zone.AngleZoneIndex] += profile.CellSensitivityFactor[cellIndex] * zone.SignedErrors.Count;
                    angleCounts[zone.AngleZoneIndex] += zone.SignedErrors.Count;
                }
                else if (observedCm - BaseZoneEndCm > MinimumDenominatorCm &&
                    profile.CellAccelerationConfidence[cellIndex] > 0.0)
                {
                    distanceFactorTotals[zone.DistanceZoneIndex] += profile.CellAccelerationFactor[cellIndex] * zone.SignedErrors.Count;
                    distanceCounts[zone.DistanceZoneIndex] += zone.SignedErrors.Count;
                }
            }

            for (int angle = 0; angle < DesktopSensConstants.AngularZoneCount; angle++)
            {
                if (angleCounts[angle] > 0)
                {
                    profile.SensitivityFactor[angle] = angleErrorTotals[angle] / angleCounts[angle];
                    profile.SensitivityConfidence[angle] = Confidence(angleCounts[angle]);
                }
            }

            for (int distance = 0; distance < DesktopSensConstants.DistanceZoneCount; distance++)
            {
                if (distanceCounts[distance] > 0)
                {
                    profile.AccelerationFactor[distance] = distanceFactorTotals[distance] / distanceCounts[distance];
                    profile.AccelerationConfidence[distance] = Confidence(distanceCounts[distance]);
                }
            }

            int populatedCells = cells.Count;
            profile.CellsInsufficientData = DesktopSensConstants.AngularZoneCount * DesktopSensConstants.DistanceZoneCount - populatedCells;
            profile.CellsStable = cells.Count(p => p.Value.SignedErrors.Count >= 10);
            profile.CellsLowConfidence = populatedCells - profile.CellsStable;
            profile.OverallCoverage = (double)populatedCells /
                (DesktopSensConstants.AngularZoneCount * DesktopSensConstants.DistanceZoneCount);
            profile.OverallConfidence = cells.Count == 0 ? 0.0 :
                cells.Values.Average(c => Confidence(c.SignedErrors.Count));
            profile.ModifiedTimestamp = DateTime.UtcNow;
        }

        private static void ResetArray(double[] values, double defaultValue)
        {
            if (values == null) return;
            for (int i = 0; i < values.Length; i++) values[i] = defaultValue;
        }

        private static void EnsureArray(ref double[] values, int length, double defaultValue)
        {
            if (values == null || values.Length != length)
            {
                var replacement = new double[length];
                for (int i = 0; i < replacement.Length; i++) replacement[i] = defaultValue;
                if (values != null)
                    Array.Copy(values, replacement, Math.Min(values.Length, replacement.Length));
                values = replacement;
            }
        }

        private static double SafeRatio(double numerator, double denominator)
        {
            if (Math.Abs(denominator) < MinimumDenominatorCm)
                denominator = denominator < 0 ? -MinimumDenominatorCm : MinimumDenominatorCm;
            return numerator / denominator;
        }

        private static double Confidence(int sampleCount)
        {
            return Math.Min(1.0, sampleCount / 10.0);
        }

        private static double Clamp(double value, double minimum, double maximum)
        {
            if (!IsFinite(value)) return 1.0;
            return Math.Max(minimum, Math.Min(maximum, value));
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
