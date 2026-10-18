namespace CallmeCatgirl.Gameplay.TaskGrid
{
    /// <summary>最小二维向量类型：供无 UnityEngine 依赖的纯物理模型使用。</summary>
    public readonly struct Vec2
    {
        public readonly float X, Y;
        public Vec2(float x, float y) { X = x; Y = y; }
        public static Vec2 Zero => new Vec2(0, 0);
        public static Vec2 operator +(Vec2 a, Vec2 b) => new Vec2(a.X + b.X, a.Y + b.Y);
        public static Vec2 operator -(Vec2 a, Vec2 b) => new Vec2(a.X - b.X, a.Y - b.Y);
        public static Vec2 operator -(Vec2 a) => new Vec2(-a.X, -a.Y);
        public static Vec2 operator *(Vec2 a, float s) => new Vec2(a.X * s, a.Y * s);
        public static Vec2 operator *(float s, Vec2 a) => new Vec2(a.X * s, a.Y * s);
        public static Vec2 operator /(Vec2 a, float s) => new Vec2(a.X / s, a.Y / s);
        public float LengthSquared => X * X + Y * Y;
        public float Length => (float)System.Math.Sqrt(LengthSquared);
        public static float Dot(Vec2 a, Vec2 b) => a.X * b.X + a.Y * b.Y;
        /// <summary>二维叉积的 z 分量：r × F 即力矩。</summary>
        public static float Cross(Vec2 a, Vec2 b) => a.X * b.Y - a.Y * b.X;
        /// <summary>逆时针 90 度：用于由角速度求点速度。</summary>
        public Vec2 Perpendicular() => new Vec2(-Y, X);
        public bool IsFinite => float.IsFinite(X) && float.IsFinite(Y);
        public Vec2 ClampMagnitude(float maximum)
        {
            float squared = LengthSquared;
            if (squared <= maximum * maximum || squared <= 0) return this;
            return this * (maximum / (float)System.Math.Sqrt(squared));
        }
    }
}
