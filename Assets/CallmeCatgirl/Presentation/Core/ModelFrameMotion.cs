namespace CallmeCatgirl.Presentation
{
    public readonly struct MotionRect
    {
        public readonly float X, Y, Width, Height;
        public MotionRect(float x, float y, float width, float height) { X = x; Y = y; Width = width; Height = height; }
        public bool Same(MotionRect other) => X == other.X && Y == other.Y && Width == other.Width && Height == other.Height;
        public static MotionRect Lerp(MotionRect a, MotionRect b, float t) => new MotionRect(
            MotionMath.Lerp(a.X, b.X, t), MotionMath.Lerp(a.Y, b.Y, t),
            MotionMath.Lerp(a.Width, b.Width, t), MotionMath.Lerp(a.Height, b.Height, t));
    }

    public sealed class ModelFrameMotion
    {
        private MotionRect from, target;
        private float fromExpansion, elapsed, duration;
        private bool initialized;
        public bool RequestedOpen { get; private set; }
        public bool Active { get; private set; }
        public float Expansion { get; private set; }
        public MotionRect Current { get; private set; }

        public bool Request(bool open, MotionRect home, MotionRect viewport)
        {
            if (RequestedOpen == open) return false;
            if (!initialized || !Active)
            { Current = RequestedOpen ? viewport : home; initialized = true; }
            RequestedOpen = open;
            from = Current;
            target = open ? viewport : home;
            fromExpansion = Expansion;
            elapsed = 0;
            duration = .62f * System.Math.Abs((open ? 1 : 0) - Expansion);
            Active = duration > 0;
            return true;
        }

        public void Tick(float delta, MotionRect home, MotionRect viewport, bool reduced = false)
        {
            var goal = RequestedOpen ? viewport : home;
            if (!Active || reduced)
            {
                Current = goal; Expansion = RequestedOpen ? 1 : 0; Active = false; initialized = true;
                return;
            }
            if (!target.Same(goal))
            {
                // Resizing retargets the remaining time from the rectangle last painted.
                from = Current; target = goal; fromExpansion = Expansion;
                duration = System.Math.Max(0, duration - elapsed); elapsed = 0;
            }
            elapsed += MotionMath.Delta(delta);
            float t = duration <= 0 ? 1 : MotionMath.Clamp01(elapsed / duration);
            float eased = MotionMath.Smooth(t);
            Current = MotionRect.Lerp(from, target, eased);
            Expansion = MotionMath.Lerp(fromExpansion, RequestedOpen ? 1 : 0, eased);
            if (t >= 1) Active = false;
        }
    }
}
