using System;

namespace CallmeCatgirl.Presentation
{
    // Engine-free clocks: a request changes the destination, never the current pose.
    public static class MotionMath
    {
        public static float Clamp01(float value) => Math.Max(0, Math.Min(1, value));
        public static float Smooth(float t) => t * t * (3 - 2 * t);
        public static float Smoother(float t) => t * t * t * (10 + t * (-15 + 6 * t));
        public static float Lerp(float a, float b, float t) => a + (b - a) * t;
        public static float Delta(float delta) => float.IsNaN(delta) || float.IsInfinity(delta) ? 0 : Math.Max(0, delta);
        public static float Approach(float current, float target, float delta, float seconds)
        {
            float step = seconds <= 0 ? 1 : Delta(delta) / seconds;
            return current < target ? Math.Min(target, current + step) : Math.Max(target, current - step);
        }

        // Screen coordinates are bottom-left based. Equal physical x/y distances give
        // a 45-degree boundary on ultrawide and portrait screens as well as 16:9.
        public static float SweepDistance(float x, float y, float width, float height)
            => width - Math.Max(0, Math.Min(width, x)) + Math.Max(0, Math.Min(height, y));

        public static float PanelSweepDistance(float minX, float minY, float maxX, float maxY, float width, float height)
        {
            if (maxX <= 0 || maxY <= 0 || minX >= width || minY >= height)
                return float.PositiveInfinity;
            // A clipped AABB is conservative. Clamping corners separately is unsafe:
            // a rotated edge crossing a viewport corner may outlast both endpoints.
            return SweepDistance(minX, maxY, width, height);
        }

        public static bool IsCovered(float progress, float lastCornerDistance, float width, float height)
            => width > 0 && height > 0 && (progress >= 1 ||
                (progress > 0 && lastCornerDistance <= progress * (width + height)));

        // White difference layer, implemented by fixed-function destination blending.
        public static float Difference(float destination, float amount)
            => destination * (1 - amount) + (1 - destination) * amount;
    }

    public struct ReversibleProgress
    {
        public float Value { get; private set; }
        public bool Requested { get; private set; }
        public bool Request(bool value)
        {
            if (Requested == value) return false;
            Requested = value;
            return true;
        }
        public void Tick(float delta, float seconds, bool reduced = false)
            => Value = reduced ? (Requested ? 1 : 0) : MotionMath.Approach(Value, Requested ? 1 : 0, delta, seconds);
        /// <summary>只改变可见进度，不改变请求的目标状态。</summary>
        public void Snap(float value) => Value = MotionMath.Clamp01(value);
    }

    public readonly struct MotionPose
    {
        public readonly float X, Y, Angle;
        public MotionPose(float x, float y, float angle) { X = x; Y = y; Angle = angle; }
        public static MotionPose Lerp(MotionPose a, MotionPose b, float t) => new MotionPose(
            MotionMath.Lerp(a.X, b.X, t), MotionMath.Lerp(a.Y, b.Y, t), MotionMath.Lerp(a.Angle, b.Angle, t));
    }

    public sealed class PaperDrift
    {
        private MotionPose from, target;
        private float elapsed, duration;
        private uint random;
        private bool segment;
        public MotionPose Pose { get; private set; }
        public bool Planning { get; private set; }
        public bool Swept { get; private set; }
        public int SegmentsStarted { get; private set; }

        public PaperDrift(uint seed) { random = seed == 0 ? 1u : seed; }
        public void Request(bool planning)
        {
            if (Planning == planning) return;
            Planning = planning;
            Swept = false;
            segment = false;
            if (!planning) Begin(default, .6f);
        }
        public void MarkCovered(bool covered) { if (Planning && covered) Swept = true; }
        /// <summary>立即回到基准姿态并清除规划/扫过状态；用于关闭视觉时一次性复位。</summary>
        public void SnapHome() { Planning = false; Swept = false; segment = false; Pose = default; }
        public void Tick(float delta, bool engaged, bool reduced, MotionPose limits)
        {
            if (reduced) { Pose = default; segment = false; Swept = false; return; }
            if (Planning && !Swept) return;
            if (engaged) return; // Hover/keyboard focus holds the actual rotated pose.
            if (!segment)
            {
                if (Planning)
                    Begin(new MotionPose(Signed() * limits.X, Signed() * limits.Y, Signed() * limits.Angle), 6 + Next() * 7);
                return; // A newly covered panel first renders its unchanged pose.
            }
            elapsed = Math.Min(duration, elapsed + MotionMath.Delta(delta));
            Pose = MotionPose.Lerp(from, target, MotionMath.Smoother(elapsed / duration));
            if (elapsed >= duration) segment = false;
        }
        private void Begin(MotionPose next, float seconds)
        {
            from = Pose; target = next; duration = seconds; elapsed = 0; segment = true; SegmentsStarted++;
        }
        private float Signed() => Next() * 2 - 1;
        private float Next()
        {
            random ^= random << 13; random ^= random >> 17; random ^= random << 5;
            return (random & 0x00ffffff) / 16777216f;
        }
    }
}
