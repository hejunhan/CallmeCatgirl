using System;
using CallmeCatgirl.Gameplay.TaskGrid;
using NUnit.Framework;

namespace CallmeCatgirl.Tests
{
    /// <summary>
    /// 几何安装模型的确定性测试：左右插槽阻力、单端倾斜、中央底座弯曲、两端落座。
    /// 不依赖弹簧力；鼠标拖拽直接映射插槽深度。
    /// </summary>
    [TestFixture]
    public sealed class MemoryInstallPhysicsTests
    {
        private const float StepFps = 60f;

        private static void Advance(MemoryInstallSimulation sim, float seconds, float fps = StepFps)
        {
            int frames = Math.Max(1, (int)(seconds * fps));
            for (int i = 0; i < frames; i++) sim.Advance(1f / fps);
        }

        private static void PressEnd(MemoryInstallSimulation sim, int end, float pull = 120, float maxSeconds = 3)
        {
            Assert.That(sim.Grab(sim.Ram.LocalToWorld(new Vec2(sim.NodeRestX(end), 0))), Is.True, "抓取点应在内存条端部");
            float anchorY = sim.GripWorld.Y;
            for (int i = 0; i < (int)(maxSeconds * StepFps) && !sim.IsCaptured(end); i++)
            {
                sim.UpdateTarget(new Vec2(sim.GripWorld.X, anchorY + pull));
                sim.Advance(1f / StepFps);
            }
            sim.Release();
        }

        [Test]
        public void LeftEndInsertsHoldsAndRaisesRightWithoutSolving()
        {
            var sim = new MemoryInstallSimulation();
            PressEnd(sim, 0);
            Assert.That(sim.LeftCaptured, Is.True);
            Assert.That(sim.RightCaptured, Is.False);
            Assert.That(sim.Ram.Theta, Is.LessThan(0), "左端进入时左端应低于右端");
            Advance(sim, .4f);
            Assert.That(sim.LeftCaptured, Is.True, "松开后插槽阻力应保持左端");
            Assert.That(sim.Solved, Is.False, "只进入一端不得完成");
        }

        [Test]
        public void SecondEndSeatsAndCentersTheModule()
        {
            var sim = new MemoryInstallSimulation();
            PressEnd(sim, 0);
            Advance(sim, .2f);
            PressEnd(sim, 2);
            Advance(sim, .5f);
            Assert.That(sim.LeftCaptured && sim.RightCaptured, Is.True);
            Assert.That(sim.Solved, Is.True, "两端进入并保持水平后应落座");
            Assert.That(sim.Ram.Theta, Is.EqualTo(0).Within(1e-3));
            Assert.That(sim.Ram.Position.X, Is.EqualTo(0).Within(1e-3));
        }

        [Test]
        public void CenterPressBendsBaseButCannotInsertOrSolve()
        {
            var sim = new MemoryInstallSimulation();
            Assert.That(sim.Grab(sim.Ram.LocalToWorld(Vec2.Zero)), Is.True);
            float anchorY = sim.GripWorld.Y;
            for (int i = 0; i < 90; i++)
            {
                sim.UpdateTarget(new Vec2(sim.GripWorld.X, anchorY + 160));
                sim.Advance(1f / StepFps);
            }
            sim.Release();
            Assert.That(sim.BoardBend, Is.GreaterThan(.5f), "按压中间必须让底座可见下弯");
            Assert.That(sim.LeftCaptured || sim.RightCaptured, Is.False, "中间按压不能把端部带入插槽");
            Assert.That(sim.Solved, Is.False);
            Advance(sim, .8f);
            Assert.That(sim.BoardBend, Is.LessThan(.1f), "松开后底座应回弹");
            Assert.That(sim.Solved, Is.False);
        }

        [Test]
        public void CenterPressAfterBothEndsBlocksSeatUntilReleased()
        {
            var tuning = new MemoryInstallTuning { SeatHoldTime = .5f };
            var sim = new MemoryInstallSimulation(tuning);
            PressEnd(sim, 0); PressEnd(sim, 2);
            Advance(sim, .2f);
            Assert.That(sim.Solved, Is.False, "SeatHoldTime 未到不得完成");
            Assert.That(sim.Grab(sim.Ram.LocalToWorld(Vec2.Zero)), Is.True);
            float anchorY = sim.GripWorld.Y;
            for (int i = 0; i < 60; i++)
            {
                sim.UpdateTarget(new Vec2(sim.GripWorld.X, anchorY + 160));
                sim.Advance(1f / StepFps);
            }
            Assert.That(sim.Solved, Is.False, "中央凸起被压下时不能卡入凹槽");
            sim.Release();
            Advance(sim, 1.2f);
            Assert.That(sim.Solved, Is.True, "底座回弹后两端仍在插槽内应可落座");
        }

        [Test]
        public void SeatRequiresBothEnds()
        {
            var sim = new MemoryInstallSimulation();
            sim.Seat();
            Assert.That(sim.Solved, Is.False);
            PressEnd(sim, 0);
            sim.Seat();
            Assert.That(sim.Solved, Is.False, "仅一端进入时 Seat 必须无效");
            PressEnd(sim, 2);
            sim.Seat();
            Assert.That(sim.Solved, Is.True);
        }

        [Test]
        public void DragKeepsLocalGripAndHasNoSpringForce()
        {
            var sim = new MemoryInstallSimulation();
            var grip = new Vec2(-80, 4);
            Assert.That(sim.Grab(sim.Ram.LocalToWorld(grip)), Is.True);
            float anchorY = sim.GripWorld.Y;
            for (int i = 0; i < 30; i++)
            {
                sim.UpdateTarget(new Vec2(sim.GripWorld.X, anchorY + 70));
                sim.Advance(1f / StepFps);
            }
            Assert.That(sim.GripLocal.X, Is.EqualTo(grip.X).Within(.02));
            Assert.That(sim.GripLocal.Y, Is.EqualTo(grip.Y).Within(.02));
            Assert.That(sim.Ram.Theta, Is.LessThan(-.02), "左端下拉应让左端下沉");
            Assert.That(sim.AppliedForce, Is.EqualTo(Vec2.Zero), "不再保留鼠标弹簧力");
        }

        [TestCase(30f)] [TestCase(60f)] [TestCase(144f)]
        public void FrameRatesSolveTheSameGeometry(float fps)
        {
            var sim = new MemoryInstallSimulation();
            Assert.That(sim.Grab(sim.Ram.LocalToWorld(new Vec2(-120, 0))), Is.True);
            float y = sim.GripWorld.Y;
            for (int i = 0; i < (int)(2f * fps) && !sim.LeftCaptured; i++)
            { sim.UpdateTarget(new Vec2(sim.GripWorld.X, y + 120)); sim.Advance(1f / fps); }
            sim.Release(); Advance(sim, .2f, fps);
            Assert.That(sim.Grab(sim.Ram.LocalToWorld(new Vec2(120, 0))), Is.True);
            y = sim.GripWorld.Y;
            for (int i = 0; i < (int)(2f * fps) && !sim.RightCaptured; i++)
            { sim.UpdateTarget(new Vec2(sim.GripWorld.X, y + 120)); sim.Advance(1f / fps); }
            sim.Release(); Advance(sim, .6f, fps);
            Assert.That(sim.Solved, Is.True, "各帧率下两端进入后都应完成");
            Assert.That(sim.Ram.Position.IsFinite && float.IsFinite(sim.Ram.Theta), Is.True);
        }

        [TestCase(30f)] [TestCase(60f)] [TestCase(144f)]
        public void ThickAuthoredRamStillSeats(float fps)
        {
            var tuning = new MemoryInstallTuning
            {
                RamThickness = 240f * 236 / 700, HoverHeight = 50,
                BoundX = (1080 - 700) * .5f / (700f / 240), BoundUp = -50, BoundDown = 20
            };
            var sim = new MemoryInstallSimulation(tuning);
            foreach (int end in new[] { 0, 2 })
            {
                Assert.That(sim.Grab(sim.Ram.LocalToWorld(new Vec2(sim.NodeRestX(end), 0))), Is.True);
                float y = sim.GripWorld.Y;
                for (int i = 0; i < (int)(3f * fps) && !sim.IsCaptured(end); i++)
                { sim.UpdateTarget(new Vec2(sim.GripWorld.X, y + 120)); sim.Advance(1f / fps); }
                Assert.That(sim.IsCaptured(end), Is.True);
                sim.Release(); Advance(sim, .2f, fps);
            }
            Advance(sim, .8f, fps);
            Assert.That(sim.Solved, Is.True);
        }

        [Test]
        public void SeatHoldTimeIsHonored()
        {
            var tuning = new MemoryInstallTuning { SeatHoldTime = .5f };
            var sim = new MemoryInstallSimulation(tuning);
            PressEnd(sim, 0); PressEnd(sim, 2);
            Advance(sim, .3f);
            Assert.That(sim.Solved, Is.False);
            Advance(sim, .3f);
            Assert.That(sim.Solved, Is.True);
            Assert.That(sim.StableTime, Is.GreaterThanOrEqualTo(tuning.SeatHoldTime));
        }
    }
}
