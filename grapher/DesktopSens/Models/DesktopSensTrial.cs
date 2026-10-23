using System;
using System.Collections.Generic;
using grapher.Models.Mouse;

namespace grapher.DesktopSens.Models
{
    public enum FlickCompletionMode
    {
        VelocityStop,
        Click
    }

    public class DesktopSensTrial
    {
        public string TrialId = Guid.NewGuid().ToString();
        public string SessionId;

        public double StartX;
        public double StartY;
        public double TargetX;
        public double TargetY;
        public double TargetAngleDegrees;
        public double TargetDistanceCm;

        public List<MouseSample> RawSamples = new List<MouseSample>();

        public double LandedX;
        public double LandedY;
        public double FinalErrorCm;
        // Signed error along the target direction: negative = short, positive = past target.
        public double SignedErrorCm;
        public double ProjectedDistanceCm;
        public bool IsUnderFlick;
        public bool IsOverFlick;

        public double FlickAngleDegrees;
        // Signed shortest angular correction: target angle minus measured flick angle.
        public double SignedRotationErrorDegrees;
        public double FlickDistanceCm;
        public double DurationMs;
        public double PeakVelocityPxPerSec;

        public int AngleZoneIndex;
        public int DistanceZoneIndex;

        public FlickCompletionMode CompletionMode;
        public long CompletionTimestamp;

        public int MouseDPI;
        public double EffectivePollingRateHz;
        public double CanvasPixelsPerCm;
    }
}
