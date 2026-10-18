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

    /// <summary>安装物理的调参集合；构造即默认值，Sanitize 保证有限且非负。</summary>
    [Serializable]
    public sealed class MemoryInstallTuning
    {
        // 内存条刚体（单位约为像素、秒、质量 1）。
        public float RamLength = 240, RamThickness = 24, RamMass = 1;
        public float HoverHeight = 70;              // 初始悬停在插槽上方的距离。
        // 鼠标弹簧：力 = 刚度*(光标-当前抓取点) - 阻尼*抓取点速度，并限额。
        public float MouseStiffness = 90, MouseDamping = 20, MouseForceCap = 600;
        // 抓取期间的手持角阻尼（模拟手握姿态，不锁定角度）。
        public float GripAngularDamping = 20000;
        // 插槽/主板：左中右三个顺从节点，锚定弹簧 + 相邻弯曲弹簧；中端更软，按压中间会凹陷。
        public float NodeMass = .2f, AnchorStiffnessEnd = 300, AnchorStiffnessCenter = 60;
        public float AnchorDamping = 8, BendStiffness = 80, BendDamping = 4, MaxDeflection = 40;
        // 单边接触（只推不拉）：槽底在开口下方 FloorDepth，卡扣喉深 LatchDepth。
        public float FloorDepth = 4, LatchDepth = 7, ContactStiffness = 120, ContactDamping = 20;
        // 端部卡扣：深度/倾角/板弯条件持续 LatchHoldTime 才捕获；过度倾斜或拔出会脱扣。
        public float LatchHoldTime = .08f, LatchTiltLimit = .45f, ReleaseTiltLimit = .55f;
        public float ReleaseDepthFactor = .35f, ReleaseHoldTime = .12f;
        // 卡扣捕获后是强定位销：另一端按压时内存条绕该销杠杆转动。
        public float LatchStiffness = 800, LatchDamping = 24, LatchBendLimit = 4, LatchAlignTolerance = 24;
        public float ContactForceCap = 1200;
        // 被动阻力与行程边界：松手后漂移会衰减，状态始终有界。
        public float PassiveLinearDrag = 1.5f, PassiveAngularDrag = 4000;
        public float BoundX = 200, BoundUp = -160, BoundDown = 60;
        // 成功判定：两端捕获、松开鼠标、低倾角、低板弯、低速，稳定 StableTimeRequired。
        public float SolveTiltLimit = .09f, SolveBendLimit = 3, SolveSpeedLimit = 15;
        public float SolveAngularLimit = .6f, StableTimeRequired = .3f;
        // 积分与数值安全。
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
            if (!float.IsFinite(BoundUp) || BoundUp > 0) BoundUp = -160; // 上界必须为负（悬停区）。
            RamLength = Math.Max(1, RamLength); RamThickness = Math.Max(1, RamThickness); RamMass = Math.Max(.0001f, RamMass);
            NodeMass = Math.Max(.0001f, NodeMass); FixedStep = Math.Clamp(FixedStep, 1f / 480, 1f / 30);
            LatchDepth = Math.Clamp(LatchDepth, 0, FloorDepth + 20);
            MaxStepsPerAdvance = Math.Clamp(MaxStepsPerAdvance, 1, 32);
        }
    }

    /// <summary>
    /// 纯数值内存安装物理：整根内存条可在任意点抓取，鼠标弹簧作用于随刚体变换的固定局部抓取点，
    /// 插槽/主板为三个顺从节点加弯曲弹簧，单边接触，端部卡扣支持单只鼠标换边。
    /// 不包含任何 UnityEngine 依赖；Advance 使用非缩放时间累加器与 1/120 半隐式定步。
    /// </summary>
    public sealed class MemoryInstallSimulation
    {
        public const int NodeCount = 3; // 0 左 1 中 2 右。
        private readonly MemoryInstallTuning tuning;
        private Vec2 position, linearVelocity, target, gripLocal, appliedForce;
        private float theta, angularVelocity, accumulator;
        private readonly float[] nodeY = new float[NodeCount], nodeVelocity = new float[NodeCount], holdTimer = new float[NodeCount];
        private readonly float[] releaseTimer = new float[NodeCount];
        private readonly bool[] captured = new bool[NodeCount]; // 只用 0 与 2（两端）。
        private bool grabbed;
        public MemoryInstallTuning Tuning => tuning;
        public RamPose Ram => new RamPose(position, theta);
        public Vec2 LinearVelocity => linearVelocity;
        public float AngularVelocity => angularVelocity;
        public bool IsGrabbed => grabbed;
        public Vec2 GripLocal => gripLocal;
        public Vec2 GripWorld => Ram.LocalToWorld(gripLocal);
        public Vec2 AppliedForce => appliedForce;
        public bool LeftCaptured => captured[0];
        public bool RightCaptured => captured[2];
        public bool IsCaptured(int end) => captured[end == 0 ? 0 : 2];
        public float NodeRestX(int index) => (index - 1) * tuning.RamLength * .5f;
        public float NodeDeflection(int index) => nodeY[index];
        public Vec2 NodePosition(int index) => new Vec2(NodeRestX(index), nodeY[index]);
        public float BoardBend { get; private set; }
        public float StableTime { get; private set; }
        public bool Solved { get; private set; }

        public MemoryInstallSimulation(MemoryInstallTuning settings = null)
        {
            tuning = settings ?? new MemoryInstallTuning();
            tuning.Sanitize(); ResetSimulation();
        }
        public void ResetSimulation()
        {
            position = new Vec2(0, -tuning.HoverHeight); theta = 0;
            linearVelocity = Vec2.Zero; angularVelocity = 0; appliedForce = Vec2.Zero;
            gripLocal = Vec2.Zero; target = Vec2.Zero; grabbed = false; accumulator = 0;
            Array.Clear(nodeY, 0, NodeCount); Array.Clear(nodeVelocity, 0, NodeCount);
            Array.Clear(holdTimer, 0, NodeCount); Array.Clear(releaseTimer, 0, NodeCount);
            Array.Clear(captured, 0, NodeCount);
            BoardBend = 0; StableTime = 0; Solved = false;
        }
        /// <summary>整根内存条可点：指针落在条内则记录 RAM 局部抓取点；该局部点在拖拽期间不随旋转改变。</summary>
        public bool Grab(Vec2 worldPoint)
        {
            if (!worldPoint.IsFinite) { Release(); return false; }
            var local = Ram.WorldToLocal(worldPoint);
            // 端角往返变换有浮点误差，保留半像素容差；整根条可点但不接受远离条体的点。
            if (Math.Abs(local.X) > tuning.RamLength * .5f + .5f || Math.Abs(local.Y) > tuning.RamThickness * .5f + .5f) return false;
            gripLocal = local; target = worldPoint; grabbed = true;
            return true;
        }
        public void UpdateTarget(Vec2 worldCursor)
        {
            if (!worldCursor.IsFinite) { Release(); return; } // 非有限输入等价于失去焦点：解除鼠标约束。
            target = worldCursor;
        }
        public void Release() { grabbed = false; appliedForce = Vec2.Zero; }
        /// <summary>非缩放时间推进：定步 1/120，长帧有界追帧，状态保持有限。</summary>
        public void Advance(float unscaledDelta)
        {
            tuning.Sanitize();
            if (!float.IsFinite(unscaledDelta) || unscaledDelta < 0) unscaledDelta = 0;
            accumulator = Math.Min(accumulator + unscaledDelta, tuning.MaxAdvance);
            int steps = 0;
            while (accumulator >= tuning.FixedStep && steps < tuning.MaxStepsPerAdvance)
            { Step(tuning.FixedStep); accumulator -= tuning.FixedStep; steps++; }
            if (steps == tuning.MaxStepsPerAdvance) accumulator = Math.Min(accumulator, tuning.FixedStep);
        }
        private void Step(float dt)
        {
            var pose = Ram;
            float inertia = tuning.RamMass * (tuning.RamLength * tuning.RamLength + tuning.RamThickness * tuning.RamThickness) / 12;
            var force = Vec2.Zero; float torque = 0;
            var nodeForce = new float[NodeCount];
            // 鼠标弹簧：作用于当前世界抓取点，力矩为 r × F。
            appliedForce = Vec2.Zero;
            if (grabbed)
            {
                var offset = GripWorld - position;
                var gripVelocity = linearVelocity + offset.Perpendicular() * angularVelocity;
                appliedForce = ((target - GripWorld) * tuning.MouseStiffness - gripVelocity * tuning.MouseDamping)
                    .ClampMagnitude(tuning.MouseForceCap);
                force += appliedForce; torque += Vec2.Cross(offset, appliedForce);
                torque -= tuning.GripAngularDamping * angularVelocity; // 手持角阻尼：不锁角度，只耗散旋转。
            }
            // 单边接触：槽底只推不拉；采样点钳制到条底最近点（旋转时端角仍能接触），
            // 阻尼只抵抗压入、不在分离时加力，避免向系统泵能。
            for (int i = 0; i < NodeCount; i++)
            {
                float cos = (float)Math.Cos(theta), sin = (float)Math.Sin(theta);
                if (Math.Abs(cos) < .2f) continue;
                float localX = (NodeRestX(i) - position.X + tuning.RamThickness * .5f * sin) / cos;
                if (Math.Abs(localX) > tuning.RamLength * .5f + 30) continue;
                localX = Math.Clamp(localX, -tuning.RamLength * .5f, tuning.RamLength * .5f);
                var bottomWorld = pose.LocalToWorld(new Vec2(localX, tuning.RamThickness * .5f));
                float floorY = nodeY[i] + tuning.FloorDepth;
                float penetration = bottomWorld.Y - floorY;
                if (penetration <= 0) continue;
                var pointVelocity = linearVelocity + (bottomWorld - position).Perpendicular() * angularVelocity;
                float approach = pointVelocity.Y - nodeVelocity[i];
                float push = tuning.ContactStiffness * penetration + (approach > 0 ? tuning.ContactDamping * approach : 0);
                push = Math.Clamp(push, 0, tuning.ContactForceCap);
                force += new Vec2(0, -push); torque += Vec2.Cross(bottomWorld - position, new Vec2(0, -push));
                nodeForce[i] += push;
            }
            // 端部卡扣：捕获端以强弹簧锚定到静止框架的喉深处（不随节点位移漂移，避免正反馈拖垮整板），同时承担水平对齐。
            for (int end = 0; end < 3; end += 2)
            {
                if (!captured[end]) continue;
                var attach = pose.LocalToWorld(new Vec2(NodeRestX(end), tuning.RamThickness * .5f));
                var capture = new Vec2(NodeRestX(end), tuning.LatchDepth);
                var pointVelocity = linearVelocity + (attach - position).Perpendicular() * angularVelocity;
                var pull = (capture - attach) * tuning.LatchStiffness - pointVelocity * tuning.LatchDamping;
                pull = pull.ClampMagnitude(tuning.ContactForceCap * 3);
                force += pull; torque += Vec2.Cross(attach - position, pull);
            }
            // 主板节点：锚定弹簧 + 相邻弯曲弹簧 + 接触下压。
            for (int i = 0; i < NodeCount; i++)
            {
                float anchor = i == 1 ? tuning.AnchorStiffnessCenter : tuning.AnchorStiffnessEnd;
                nodeForce[i] += -anchor * nodeY[i] - tuning.AnchorDamping * nodeVelocity[i];
            }
            for (int i = 0; i + 1 < NodeCount; i++)
            {
                float relative = nodeY[i] - nodeY[i + 1], relativeVelocity = nodeVelocity[i] - nodeVelocity[i + 1];
                float bend = tuning.BendStiffness * relative + tuning.BendDamping * relativeVelocity;
                nodeForce[i] -= bend; nodeForce[i + 1] += bend;
            }
            // 半隐式积分。
            linearVelocity += force * (dt / tuning.RamMass); angularVelocity += torque * (dt / inertia);
            linearVelocity = linearVelocity.ClampMagnitude(tuning.MaxLinearSpeed);
            angularVelocity = Math.Clamp(angularVelocity, -tuning.MaxAngularSpeed, tuning.MaxAngularSpeed);
            // 被动阻力：松手后的漂移逐渐衰减而不是永动。
            linearVelocity -= linearVelocity * Math.Min(1, tuning.PassiveLinearDrag * dt);
            angularVelocity -= angularVelocity * Math.Min(1, tuning.PassiveAngularDrag * dt / inertia);
            position += linearVelocity * dt; theta = WrapAngle(theta + angularVelocity * dt);
            // 行程边界：钳制位置并消除指向界外的速度分量。
            if (position.X < -tuning.BoundX) { position = new Vec2(-tuning.BoundX, position.Y); linearVelocity = new Vec2(Math.Max(0, linearVelocity.X), linearVelocity.Y); }
            if (position.X > tuning.BoundX) { position = new Vec2(tuning.BoundX, position.Y); linearVelocity = new Vec2(Math.Min(0, linearVelocity.X), linearVelocity.Y); }
            if (position.Y < tuning.BoundUp) { position = new Vec2(position.X, tuning.BoundUp); linearVelocity = new Vec2(linearVelocity.X, Math.Max(0, linearVelocity.Y)); }
            if (position.Y > tuning.BoundDown) { position = new Vec2(position.X, tuning.BoundDown); linearVelocity = new Vec2(linearVelocity.X, Math.Min(0, linearVelocity.Y)); }
            for (int i = 0; i < NodeCount; i++)
            {
                nodeVelocity[i] = Math.Clamp(nodeVelocity[i] + nodeForce[i] * (dt / tuning.NodeMass), -tuning.MaxLinearSpeed, tuning.MaxLinearSpeed);
                nodeY[i] = Math.Clamp(nodeY[i] + nodeVelocity[i] * dt, 0, tuning.MaxDeflection);
            }
            UpdateLatches(dt);
            UpdateSolved(dt);
            // 有限性护栏：任何非有限状态直接复位动力学，不留残余约束。
            if (!position.IsFinite || !linearVelocity.IsFinite || !float.IsFinite(theta) || !float.IsFinite(angularVelocity)
                || !float.IsFinite(nodeY[0]) || !float.IsFinite(nodeY[1]) || !float.IsFinite(nodeY[2])) ResetSimulation();
        }
        private void UpdateLatches(float dt)
        {
            var pose = Ram;
            float bend = ComputeBend();
            for (int end = 0; end < 3; end += 2)
            {
                var attach = pose.LocalToWorld(new Vec2(NodeRestX(end), tuning.RamThickness * .5f));
                float depth = attach.Y - nodeY[end];
                if (captured[end])
                {
                    // 过度倾斜立即脱扣；端部浮出喉深须持续一段时间（换边按压的瞬时回弹不脱扣）。
                    if (Math.Abs(theta) > tuning.ReleaseTiltLimit) { captured[end] = false; holdTimer[end] = 0; releaseTimer[end] = 0; continue; }
                    releaseTimer[end] = depth < tuning.LatchDepth * tuning.ReleaseDepthFactor ? releaseTimer[end] + dt : 0;
                    if (releaseTimer[end] >= tuning.ReleaseHoldTime) { captured[end] = false; holdTimer[end] = 0; releaseTimer[end] = 0; }
                    continue;
                }
                var pointVelocity = linearVelocity + (attach - position).Perpendicular() * angularVelocity;
                // 捕获要求：端部与对应卡扣水平对齐、相对变形后的局部插槽达到喉深、低倾角、低板弯、非向上回弹。
                bool seating = Math.Abs(attach.X - NodeRestX(end)) <= tuning.LatchAlignTolerance
                    && depth > tuning.LatchDepth && Math.Abs(theta) <= tuning.LatchTiltLimit
                    && bend <= tuning.LatchBendLimit && pointVelocity.Y >= -5;
                holdTimer[end] = seating ? holdTimer[end] + dt : 0;
                if (holdTimer[end] >= tuning.LatchHoldTime) captured[end] = true;
            }
        }
        private void UpdateSolved(float dt)
        {
            BoardBend = ComputeBend();
            if (Solved) return;
            bool stable = captured[0] && captured[2] && !grabbed
                && Math.Abs(theta) <= tuning.SolveTiltLimit && BoardBend <= tuning.SolveBendLimit
                && linearVelocity.Length <= tuning.SolveSpeedLimit && Math.Abs(angularVelocity) <= tuning.SolveAngularLimit
                && Math.Abs(nodeVelocity[0]) <= tuning.SolveSpeedLimit && Math.Abs(nodeVelocity[1]) <= tuning.SolveSpeedLimit
                && Math.Abs(nodeVelocity[2]) <= tuning.SolveSpeedLimit;
            StableTime = stable ? StableTime + dt : 0;
            if (StableTime >= tuning.StableTimeRequired) Solved = true;
        }
        private float ComputeBend()
        {
            float bend = 0;
            for (int i = 0; i < NodeCount; i++) bend = Math.Max(bend, Math.Abs(nodeY[i]));
            return bend;
        }
        private static float WrapAngle(float angle)
        {
            const float twoPi = (float)(Math.PI * 2);
            angle %= twoPi;
            if (angle > Math.PI) angle -= twoPi;
            if (angle < -Math.PI) angle += twoPi;
            return angle;
        }
    }
}
