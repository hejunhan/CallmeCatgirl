using System;

namespace CallmeCatgirl.Gameplay.TaskGrid
{
    /// <summary>内存条刚体位姿：局部坐标随平移与旋转变换，抓取点在拖拽期间保持不变。</summary>
    public readonly struct RamPose
    {
        public readonly Vec2 Position;
        public readonly float Theta;
        public RamPose(Vec2 position, float theta) { Position = position; Theta = theta; }
        public Vec2 LocalToWorld(Vec2 local)
        {
            float cos = (float)Math.Cos(Theta), sin = (float)Math.Sin(Theta);
            return new Vec2(Position.X + local.X * cos - local.Y * sin, Position.Y + local.X * sin + local.Y * cos);
        }
        public Vec2 WorldToLocal(Vec2 world)
        {
            float cos = (float)Math.Cos(Theta), sin = (float)Math.Sin(Theta);
            float dx = world.X - Position.X, dy = world.Y - Position.Y;
            return new Vec2(dx * cos + dy * sin, -dx * sin + dy * cos);
        }
    }

    /// <summary>安装调参。旧弹簧字段保留用于场景序列化兼容，但不再产生捕获力。</summary>
    [Serializable]
    public sealed class MemoryInstallTuning
    {
        public float RamLength = 240, RamThickness = 24, RamMass = 1;
        public float HoverHeight = 70;
        // 旧鼠标弹簧字段：保留序列化兼容，不再参与捕获。
        public float MouseStiffness = 90, MouseDamping = 20, MouseForceCap = 600;
        public float GripAngularDamping = 20000;
        // 只有插槽座中心节点可弯；左右节点保持刚性。
        public float NodeMass = .2f, AnchorStiffnessEnd = 300, AnchorStiffnessCenter = 60;
        public float AnchorDamping = 8, BendStiffness = 80, BendDamping = 4, MaxDeflection = 40;
        public float FloorDepth = 4, LatchDepth = 10, ContactStiffness = 120, ContactDamping = 20;
        // 旧卡扣字段：保留序列化兼容，不再用于捕获。
        public float LatchHoldTime = .18f, LatchTiltLimit = .42f, ReleaseTiltLimit = .62f;
        public float ReleaseDepthFactor = .35f, ReleaseHoldTime = .12f;
        public float LatchStiffness = 800, LatchDamping = 24, LatchBendLimit = 4, LatchAlignTolerance = 12;
        public float ContactForceCap = 1200;
        public float PassiveLinearDrag = 1.5f, PassiveAngularDrag = 4000;
        public float BoundX = 200, BoundUp = -160, BoundDown = 60;
        public float SolveTiltLimit = .08f, SolveBendLimit = 3, SolveSpeedLimit = 15;
        public float SolveAngularLimit = .6f, StableTimeRequired = .12f, SeatHoldTime = .12f;
        // 新几何参数：插槽行程、左右最大倾角、深度速度、中央按压行程。
        public float SlotTravel = 100, MaxTilt = .35f, DepthSpeed = 2.4f, CenterPressTravel = 80;
        public float HeldDepth = .35f, SeatDepth = .5f, SeatLevelTolerance = .12f, CenterSeatLimit = .25f;
        public float FixedStep = 1f / 120, MaxAdvance = .25f, MaxLinearSpeed = 600, MaxAngularSpeed = 20;
        public int MaxStepsPerAdvance = 8;
        public void Sanitize()
        {
            foreach (var field in GetType().GetFields())
            {
                if (field.FieldType != typeof(float) || field.Name == nameof(BoundUp)) continue;
                float value = (float)field.GetValue(this);
                if (!float.IsFinite(value)) value = 0;
                field.SetValue(this, Math.Max(0, value));
            }
            if (!float.IsFinite(BoundUp) || BoundUp > 0) BoundUp = -160;
            RamLength = Math.Max(1, RamLength); RamThickness = Math.Max(1, RamThickness); RamMass = Math.Max(.0001f, RamMass);
            NodeMass = Math.Max(.0001f, NodeMass); FixedStep = Math.Clamp(FixedStep, 1f / 480, 1f / 30);
            MaxStepsPerAdvance = Math.Clamp(MaxStepsPerAdvance, 1, 32);
            SlotTravel = Math.Max(1, SlotTravel); MaxTilt = Math.Clamp(MaxTilt, .02f, .8f);
            CenterPressTravel = Math.Max(1, CenterPressTravel);
            SeatDepth = Math.Clamp(SeatDepth, .5f, 1f); SeatLevelTolerance = Math.Clamp(SeatLevelTolerance, .01f, .5f);
            CenterSeatLimit = Math.Clamp(CenterSeatLimit, .05f, .9f);
        }
    }

    /// <summary>
    /// 现实内存条安装的几何模型：左右端进入插槽后受阻力，倾斜时另一端无法同时进入；
    /// 中央凸起对应内存条凹槽，按压中间只会让插槽座下弯，不能完成安装。
    /// 只有中央底座是弹性的；鼠标拖拽直接映射为插槽深度，不再用弹簧捕获。
    /// </summary>
    public sealed class MemoryInstallSimulation
    {
        public const int NodeCount = 3;
        private readonly MemoryInstallTuning tuning;
        private Vec2 position, target;
        private float theta, accumulator;
        private bool grabbed;
        private Vec2 gripLocal;
        private int grabbedEnd = -1;
        private float leftDepth, rightDepth, leftTarget, rightTarget;
        private float centerBend, centerTarget;
        private float seatTimer;
        public MemoryInstallTuning Tuning => tuning;
        public RamPose Ram => new RamPose(position, theta);
        public Vec2 LinearVelocity => Vec2.Zero;
        public float AngularVelocity => 0;
        public bool IsGrabbed => grabbed;
        public Vec2 GripLocal => gripLocal;
        public Vec2 GripWorld => Ram.LocalToWorld(gripLocal);
        public Vec2 AppliedForce => Vec2.Zero;
        public bool LeftCaptured => leftDepth >= tuning.SeatDepth;
        public bool RightCaptured => rightDepth >= tuning.SeatDepth;
        public bool IsCaptured(int end) => Insertion(end) >= tuning.SeatDepth;
        public float Insertion(int end) => end == 0 ? leftDepth : rightDepth;
        public float NodeRestX(int index) => (index - 1) * tuning.RamLength * .5f;
        public float NodeDeflection(int index) => index == 1 ? centerBend * tuning.MaxDeflection : 0;
        public Vec2 NodePosition(int index) => new Vec2(NodeRestX(index), NodeDeflection(index));
        public float BoardBend => centerBend * tuning.MaxDeflection;
        public float StableTime { get; private set; }
        public bool Solved { get; private set; }

        public MemoryInstallSimulation(MemoryInstallTuning settings = null)
        {
            tuning = settings ?? new MemoryInstallTuning();
            tuning.Sanitize(); ResetSimulation();
        }

        public void ResetSimulation()
        {
            position = new Vec2(0, -tuning.HoverHeight); theta = 0; target = position; accumulator = 0;
            grabbed = false; grabbedEnd = -1; gripLocal = Vec2.Zero;
            leftDepth = rightDepth = leftTarget = rightTarget = 0;
            centerBend = centerTarget = 0; seatTimer = 0; StableTime = 0; Solved = false;
        }

        /// <summary>整根内存条可点；抓取中间进入“按压底座”模式，抓左右端进入插槽模式。</summary>
        public bool Grab(Vec2 worldPoint)
        {
            if (!worldPoint.IsFinite) { Release(); return false; }
            var local = Ram.WorldToLocal(worldPoint);
            if (Math.Abs(local.X) > tuning.RamLength * .5f + .5f || Math.Abs(local.Y) > tuning.RamThickness * .5f + .5f) return false;
            gripLocal = local; target = worldPoint; grabbed = true;
            grabbedEnd = local.X < -tuning.LatchAlignTolerance ? 0 : local.X > tuning.LatchAlignTolerance ? 2 : 1;
            if (grabbedEnd == 0) leftTarget = leftDepth;
            else if (grabbedEnd == 2) rightTarget = rightDepth;
            else centerTarget = centerBend;
            return true;
        }

        public void UpdateTarget(Vec2 worldCursor)
        {
            if (!worldCursor.IsFinite) { Release(); return; }
            target = worldCursor;
        }

        public void Release()
        {
            grabbed = false; grabbedEnd = -1;
            leftTarget = leftDepth; rightTarget = rightDepth; centerTarget = 0;
        }

        public void Advance(float unscaledDelta)
        {
            if (!float.IsFinite(unscaledDelta) || unscaledDelta < 0) unscaledDelta = 0;
            tuning.Sanitize();
            accumulator = Math.Min(accumulator + unscaledDelta, tuning.MaxAdvance);
            int steps = 0;
            while (accumulator >= tuning.FixedStep && steps < tuning.MaxStepsPerAdvance)
            { Step(tuning.FixedStep); accumulator -= tuning.FixedStep; steps++; }
            if (steps == tuning.MaxStepsPerAdvance) accumulator = Math.Min(accumulator, tuning.FixedStep);
        }

        private void Step(float dt)
        {
            if (Solved) return;
            if (grabbed)
            {
                float dragDepth = Clamp01((target.Y + tuning.HoverHeight) / tuning.SlotTravel);
                if (grabbedEnd == 0) leftTarget = dragDepth;
                else if (grabbedEnd == 2) rightTarget = dragDepth;
                else centerTarget = Clamp01((target.Y + tuning.HoverHeight) / tuning.CenterPressTravel);
            }
            // 插槽阻力：越深越难推；松手后目标保持当前深度，形成静摩擦。
            leftDepth = MoveTowards(leftDepth, leftTarget, DepthSpeed(dt, leftDepth, rightDepth, 0));
            rightDepth = MoveTowards(rightDepth, rightTarget, DepthSpeed(dt, rightDepth, leftDepth, 2));
            centerBend = MoveTowards(centerBend, centerTarget, tuning.DepthSpeed * dt);
            // 按压中间时底座下弯；左右端已经进入时保留深度，但底座未回弹前不能落座。
            float depth = (leftDepth + rightDepth) * .5f;
            position = new Vec2(0, -tuning.HoverHeight + depth * tuning.SlotTravel);
            theta = (rightDepth - leftDepth) * tuning.MaxTilt;
            float level = Math.Abs(leftDepth - rightDepth);
            bool ready = !grabbed && leftDepth >= tuning.SeatDepth && rightDepth >= tuning.SeatDepth
                && level <= tuning.SeatLevelTolerance && centerBend <= tuning.CenterSeatLimit;
            seatTimer = ready ? seatTimer + dt : 0;
            StableTime = seatTimer;
            if (seatTimer >= tuning.SeatHoldTime) Seat();
        }

        private float DepthSpeed(float dt, float depth, float other, int end)
        {
            // 已进入的一端被另一端拖动时提供更高阻力；只允许很慢的退出。
            bool held = !grabbed || grabbedEnd != end;
            float resistance = held ? .45f : 1f;
            if (depth > .6f) resistance *= .55f;
            return tuning.DepthSpeed * resistance * dt;
        }

        /// <summary>两端都进入插槽、中心对齐且底座回弹后才允许落座；未齐时为空操作。</summary>
        public void Seat()
        {
            if (Solved || grabbed || leftDepth < tuning.SeatDepth || rightDepth < tuning.SeatDepth
                || Math.Abs(leftDepth - rightDepth) > tuning.SeatLevelTolerance
                || centerBend > tuning.CenterSeatLimit) return;
            leftDepth = rightDepth = 1; centerBend = 0; theta = 0;
            position = new Vec2(0, tuning.LatchDepth - tuning.RamThickness * .5f);
            StableTime = Math.Max(StableTime, tuning.SeatHoldTime); Solved = true;
        }

        private static float Clamp01(float value) => value < 0 ? 0 : value > 1 ? 1 : value;
        private static float MoveTowards(float current, float target, float maxDelta)
            => Math.Abs(target - current) <= maxDelta ? target : current + Math.Sign(target - current) * maxDelta;
    }
}
