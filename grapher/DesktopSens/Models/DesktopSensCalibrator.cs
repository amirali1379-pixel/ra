using System;
using System.Collections.Generic;
using System.Linq;

namespace grapher.DesktopSens.Models
{
    public class ZoneData
    {
        public int AngleZoneIndex;
        public int DistanceZoneIndex;
        public List<double> Errors = new List<double>();
        public List<double> Distances = new List<double>();
        
        public double MeanError => Errors.Count > 0 ? Errors.Average() : 0;
        public double Variance => Errors.Count > 1 ? Errors.Select(e => Math.Pow(e - MeanError, 2)).Average() : 0;
        
        public double GetSensitivityFactor()
        {
            if (Errors.Count == 0) return 1.0;
            double factor = 1.0 - (MeanError * 0.1);
            if (Distances.Average() > 3.0 && factor > 1.0) factor = 1.0;
            return Math.Max(0.5, Math.Min(2.0, factor));
        }

        public double GetAccelerationFactor()
        {
            if (Errors.Count == 0) return 1.0;
            double avgDist = Distances.Average();
            if (avgDist < 3.0) return 1.0;
            double factor = 1.0 + (MeanError * 0.15);
            return Math.Max(0.5, Math.Min(2.0, factor));
        }
    }

    public static class DesktopSensCalibrator
    {
        public static void Calibrate(DesktopSensProfile profile)
        {
            var zones = new Dictionary<Tuple<int, int>, ZoneData>();
            
            foreach (var trial in profile.Trials)
            {
                var key = new Tuple<int, int>(trial.AngleZoneIndex, trial.DistanceZoneIndex);
                if (!zones.ContainsKey(key))
                    zones[key] = new ZoneData { AngleZoneIndex = key.Item1, DistanceZoneIndex = key.Item2 };
                
                double error = trial.IsUnderFlick ? -trial.FinalErrorCm : trial.FinalErrorCm;
                zones[key].Errors.Add(error);
                zones[key].Distances.Add(trial.TargetDistanceCm);
            }

            foreach (var kvp in zones)
            {
                var zone = kvp.Value;
                profile.SensitivityFactor[zone.AngleZoneIndex] = zone.GetSensitivityFactor();
                profile.AccelerationFactor[zone.DistanceZoneIndex] = zone.GetAccelerationFactor();
                profile.SensitivityConfidence[zone.AngleZoneIndex] = Math.Min(1.0, zone.Errors.Count / 10.0);
                profile.AccelerationConfidence[zone.DistanceZoneIndex] = Math.Min(1.0, zone.Errors.Count / 10.0);
            }

            var rotationData = new List<Tuple<double, double>>();
            foreach (var trial in profile.Trials)
            {
                double angleError = trial.FlickAngleDegrees - trial.TargetAngleDegrees;
                while (angleError > 180) angleError -= 360;
                while (angleError < -180) angleError += 360;
                rotationData.Add(new Tuple<double, double>(trial.TargetAngleDegrees, angleError));
            }

            if (rotationData.Count >= 5)
            {
                profile.Rotation = FitFourier(rotationData, 3);
            }
        }

        private static RotationModel FitFourier(List<Tuple<double, double>> data, int order)
        {
            int n = data.Count;
            int paramCount = 1 + 2 * order;
            var matrix = new double[n, paramCount];
            var vector = new double[n];

            for (int i = 0; i < n; i++)
            {
                double theta = data[i].Item1 * Math.PI / 180.0;
                matrix[i, 0] = 1;
                for (int k = 1; k <= order; k++)
                {
                    matrix[i, 2 * k - 1] = Math.Cos(k * theta);
                    matrix[i, 2 * k] = Math.Sin(k * theta);
                }
                vector[i] = data[i].Item2;
            }

            var coefficients = LeastSquares(matrix, vector);
            return new RotationModel
            {
                Type = order == 2 ? RotationModelType.FourierOrder2 : RotationModelType.FourierOrder3,
                Coefficients = coefficients,
                SampleCount = n
            };
        }

        private static double[] LeastSquares(double[,] A, double[] b)
        {
            int rows = A.GetLength(0);
            int cols = A.GetLength(1);
            var AtA = new double[cols, cols];
            var Atb = new double[cols];

            for (int i = 0; i < cols; i++)
            {
                for (int j = 0; j < cols; j++)
                {
                    double sum = 0;
                    for (int k = 0; k < rows; k++) sum += A[k, i] * A[k, j];
                    AtA[i, j] = sum;
                }
                double sumB = 0;
                for (int k = 0; k < rows; k++) sumB += A[k, i] * b[k];
                Atb[i] = sumB;
            }

            return SolveLinear(AtA, Atb);
        }

        private static double[] SolveLinear(double[,] A, double[] b)
        {
            int n = b.Length;
            var M = new double[n, n + 1];
            for (int i = 0; i < n; i++)
            {
                for (int j = 0; j < n; j++) M[i, j] = A[i, j];
                M[i, n] = b[i];
            }

            for (int i = 0; i < n; i++)
            {
                int pivot = i;
                for (int j = i + 1; j < n; j++)
                    if (Math.Abs(M[j, i]) > Math.Abs(M[pivot, i])) pivot = j;

                for (int j = 0; j <= n; j++)
                {
                    double tmp = M[i, j];
                    M[i, j] = M[pivot, j];
                    M[pivot, j] = tmp;
                }

                for (int j = i + 1; j < n; j++)
                {
                    double factor = M[j, i] / M[i, i];
                    for (int k = i; k <= n; k++)
                        M[j, k] -= factor * M[i, k];
                }
            }

            var result = new double[n];
            for (int i = n - 1; i >= 0; i--)
            {
                double sum = M[i, n];
                for (int j = i + 1; j < n; j++) sum -= M[i, j] * result[j];
                result[i] = sum / M[i, i];
            }
            return result;
        }
    }
}