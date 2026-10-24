using System;
using grapher.DesktopSens.Models;

namespace grapher.DesktopSens.Input
{
    /// <summary>
    /// Converts a calibrated DesktopSens profile into the native Wrapper runtime's flat arrays.
    /// This is an explicit processing adapter, not a global input hook and not a kernel injector.
    /// The owner of the single supported live input/output path must call ProcessSample.
    /// </summary>
    public sealed class DesktopSensRuntimeController : IDisposable
    {
        private readonly DesktopSensRuntimeBridge runtime;
        private readonly double[] sensitivity;
        private readonly double[] acceleration;
        private readonly double[] rotation;
        private bool disposed;

        public DesktopSensRuntimeController(DesktopSensProfile profile)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));

            sensitivity = new double[DesktopSensConstants.AngularZoneCount];
            acceleration = new double[DesktopSensConstants.AngularZoneCount * DesktopSensConstants.DistanceZoneCount];
            rotation = new double[acceleration.Length];

            for (int axis = 0; axis < DesktopSensConstants.AngularZoneCount; axis++)
            {
                sensitivity[axis] = ReadPositive(profile.SensitivityFactor, axis, 1.0);
                double angle = ZoneCalculations.AngleZoneCenterDegrees(axis);

                for (int zone = 0; zone < DesktopSensConstants.DistanceZoneCount; zone++)
                {
                    int cell = DesktopSensProfile.GetCellIndex(axis, zone);
                    double distance = ZoneCalculations.ZoneIndexToDistanceCenterCm(zone);

                    acceleration[cell] = ReadPositive(profile.CellAccelerationFactor, cell, 1.0);
                    rotation[cell] = DesktopSensTranslator.GetRotationCorrectionDegrees(profile, angle, distance);
                }
            }

            runtime = new DesktopSensRuntimeBridge();
        }

        /// <summary>
        /// Processes one relative mouse sample and returns the transformed delta.
        /// It deliberately does not send the result to SendInput or the driver, avoiding a
        /// second competing input path. DPI and elapsed time must come from the live source.
        /// </summary>
        public DesktopSensRuntimeSample ProcessSample(int dx, int dy, int dpi, double elapsedMs)
        {
            ThrowIfDisposed();
            var result = runtime.Process(sensitivity, acceleration, rotation, dx, dy, dpi, elapsedMs);
            return new DesktopSensRuntimeSample
            {
                DX = result.Item1,
                DY = result.Item2,
                ActiveAxis = result.Item3,
                DistanceZone = result.Item4,
                StrokeDistanceCm = result.Item5
            };
        }

        public void Reset()
        {
            ThrowIfDisposed();
            runtime.Reset();
        }

        public void Dispose()
        {
            if (disposed) return;
            runtime.Dispose();
            disposed = true;
        }

        private void ThrowIfDisposed()
        {
            if (disposed) throw new ObjectDisposedException(nameof(DesktopSensRuntimeController));
        }

        private static double ReadPositive(double[] values, int index, double fallback)
        {
            if (values == null || index < 0 || index >= values.Length) return fallback;
            double value = values[index];
            return double.IsNaN(value) || double.IsInfinity(value) || value <= 0.0 ? fallback : value;
        }
    }

    public struct DesktopSensRuntimeSample
    {
        public double DX;
        public double DY;
        public int ActiveAxis;
        public int DistanceZone;
        public double StrokeDistanceCm;
    }
}
