using System;
using CallmeCatgirl.Gameplay.TaskGrid;
using NUnit.Framework;

namespace CallmeCatgirl.Tests
{
    /// <summary>
    /// 内存安装纯物理模型的确定性测试：固定局部抓取点、鼠标弹簧、板中部凹陷失败、
    /// 单指针换边求解、帧率等价与有界/松手动力学。序列参数集中在 <see cref="Press"/> 供 UI 集成复用。
    /// </summary>
    [TestFixture]
    public sealed class MemoryInstallPhysicsTests
    {
        // 真实单指按压序列参数：抓取 RAM 局部点，竖直下拉，目标 x 每帧跟随当前抓取点（绕卡扣转动时端点水平移动）。
        private const float StepFps = 60f, PressPull = 90, PressTimeoutSeconds = 4;
        private static void Advance(MemoryInstallSimulation sim, float seconds, float fps = StepFps)
        {
            int frames = (int)(seconds * fps);
            for (int i = 0; i < frames; i++) sim.Advance(1f / fps);
        }
        private static void Press(MemoryInstallSimulation sim, Vec2 grabLocal, float pullDown, float maxSeconds, Func<bool> done)
        {
            Assert.IsTrue(sim.Grab(sim.Ram.LocalToWorld(grabLocal)), "抓取点必须落在内存条上");
            float anchorY = sim.GripWorld.Y;
            int frames = (int)(maxSeconds * StepFps);
            for (int i = 0; i < frames && !done(); i++)
            {
                sim.UpdateTarget(new Vec2(sim.GripWorld.X, anchorY + pullDown));
                sim.Advance(1f / StepFps);
            }
            sim.Release();
        }

        [Test]
        public void GripLocalStaysFixedWhileWorldGripFollowsPose()
        {
            var sim = new MemoryInstallSimulation();
            var grabLocal = new Vec2(-80, 4);
            Assert.IsTrue(sim.Grab(sim.Ram.LocalToWorld(grabLocal)));
            Assert.AreEqual(grabLocal.X, sim.GripLocal.X, 1e-3);
            Assert.AreEqual(grabLocal.Y, sim.GripLocal.Y, 1e-3);
            // 拉压让刚体平移+旋转：局部抓取点不变，世界抓取点跟随刚体变换。
            float anchorY = sim.GripWorld.Y;
            for (int i = 0; i < 30; i++)
            {
                sim.UpdateTarget(new Vec2(sim.GripWorld.X, anchorY + 60));
                sim.Advance(1f / StepFps);
            }
            Assert.AreEqual(grabLocal.X, sim.GripLocal.X, 1e-3, "局部抓取点在拖拽期间不得改变");
            Assert.AreEqual(grabLocal.Y, sim.GripLocal.Y, 1e-3);
            Assert.Less(sim.Ram.Theta, -0.05, "左半侧下压应产生旋转");
            var expectedWorld = sim.Ram.LocalToWorld(grabLocal);
            Assert.AreEqual(expectedWorld.X, sim.GripWorld.X, 1e-3);
            Assert.AreEqual(expectedWorld.Y, sim.GripWorld.Y, 1e-3);
            // 远离条体的点不可抓取。
            Assert.IsFalse(sim.Grab(new Vec2(500, 500)));
        }

        [Test]
        public void MouseForceFollowsDisplacementGrowsWithDistanceAndCaps()
        {
            // 方向与随距离增长：小位移 → 刚度*位移；大位移 → 上限。
            var sim = new MemoryInstallSimulation();
            Assert.IsTrue(sim.Grab(sim.Ram.LocalToWorld(Vec2.Zero)));
            var grip = sim.GripWorld;
            sim.UpdateTarget(grip + new Vec2(0, 5));
            sim.Advance(sim.Tuning.FixedStep); // 恰好一个定步。
            Assert.AreEqual(450, sim.AppliedForce.Y, 1, "5px 位移应产生 90*5 的力");
            Assert.AreEqual(0, sim.AppliedForce.X, 1);
            var far = new MemoryInstallSimulation();
            Assert.IsTrue(far.Grab(far.Ram.LocalToWorld(Vec2.Zero)));
            far.UpdateTarget(far.GripWorld + new Vec2(0, 1000));
            far.Advance(far.Tuning.FixedStep);
            Assert.AreEqual(far.Tuning.MouseForceCap, far.AppliedForce.Length, 1e-3, "远距离拉动必须被力上限钳制");
            // 力矩符号：压左端向左倾（theta<0），压右端向右倾（theta>0）。
            var left = new MemoryInstallSimulation();
            Press(left, new Vec2(-120, 0), 70, .3f, () => false);
            Assert.Less(left.Ram.Theta, 0, "压左端的 r×F 力矩应使左端下沉");
            var right = new MemoryInstallSimulation();
            Press(right, new Vec2(120, 0), 70, .3f, () => false);
            Assert.Greater(right.Ram.Theta, 0, "压右端的力矩符号应与左端相反");
        }

        [Test]
        public void CenterPressSagsMiddleNodeAndCannotCaptureOrSolve()
        {
            var sim = new MemoryInstallSimulation();
            Assert.IsTrue(sim.Grab(sim.Ram.LocalToWorld(Vec2.Zero)));
            float anchorY = sim.GripWorld.Y;
            float maxBend = 0;
            for (int i = 0; i < (int)(1.5f * StepFps); i++)
            {
                sim.UpdateTarget(new Vec2(sim.GripWorld.X, anchorY + 120));
                sim.Advance(1f / StepFps);
                maxBend = Math.Max(maxBend, sim.BoardBend);
            }
            Assert.Greater(maxBend, .5f, "按中间必须让主板中部可见凹陷");
            Assert.Greater(sim.NodeDeflection(1), sim.NodeDeflection(0), "中部节点凹陷应大于端部");
            Assert.Greater(sim.NodeDeflection(1), sim.NodeDeflection(2));
            Assert.IsFalse(sim.LeftCaptured || sim.RightCaptured, "持续用力按中间不得捕获任何一端");
            sim.Release();
            Advance(sim, 1.2f); // 松开后被动回弹。
            Assert.IsFalse(sim.Solved, "按中间+松开不得直接成功");
            Assert.IsFalse(sim.LeftCaptured || sim.RightCaptured);
            Assert.Less(sim.BoardBend, .2f, "松开后板弯应回弹消失");
        }

        [Test]
        public void OnePointerSequenceCapturesBothEndsAndSettlesToSolved()
        {
            var sim = new MemoryInstallSimulation();
            // 真实输入序列：压左端→捕获→松开→换边压右端→捕获→松开静置。不设置任何锁存/成功状态。
            Press(sim, new Vec2(-120, 0), PressPull, PressTimeoutSeconds, () => sim.LeftCaptured);
            Assert.IsTrue(sim.LeftCaptured, "单指压左端应捕获左卡扣");
            Assert.IsFalse(sim.RightCaptured);
            Advance(sim, .3f);
            Assert.IsTrue(sim.LeftCaptured, "松开后左卡扣应保持（单只鼠标可换边）");
            Press(sim, new Vec2(120, 0), PressPull, PressTimeoutSeconds, () => sim.RightCaptured);
            Assert.IsTrue(sim.LeftCaptured && sim.RightCaptured, "换边后应捕获右卡扣且左卡扣不脱扣");
            float settle = 0;
            while (!sim.Solved && settle < 3) { Advance(sim, .1f); settle += .1f; }
            Assert.IsTrue(sim.Solved, "两端捕获+松开+静置约0.3s 应判定成功");
            Assert.GreaterOrEqual(sim.StableTime, sim.Tuning.StableTimeRequired);
            Assert.IsTrue(sim.Ram.Position.IsFinite && float.IsFinite(sim.Ram.Theta));
            Assert.Less(Math.Abs(sim.Ram.Theta), sim.Tuning.SolveTiltLimit + 1e-3);
        }

        [Test]
        public void LeftPressRaisesRightEndAndReleaseReboundsWithoutResidualSpring()
        {
            var sim = new MemoryInstallSimulation();
            float initialRightY = sim.Ram.LocalToWorld(new Vec2(120, 12)).Y;
            Press(sim, new Vec2(-120, 0), 70, .35f, () => false);
            var pose = sim.Ram;
            float leftY = pose.LocalToWorld(new Vec2(-120, 12)).Y;
            float rightY = pose.LocalToWorld(new Vec2(120, 12)).Y;
            Assert.Greater(leftY, rightY, "压左端时左端应低于右端");
            Assert.Less(rightY, initialRightY, "压左端时右端应向上翘起");
            // 被动回弹：中按到板已承载后松开，接触/板弹簧把内存条向上推回（模型无重力，回弹来自接触）。
            var rebound = new MemoryInstallSimulation();
            Press(rebound, Vec2.Zero, 120, 1f, () => false);
            Assert.Greater(rebound.BoardBend, .3f, "按压期间板应处于承载凹陷状态");
            float pressedY = rebound.Ram.Position.Y;
            Assert.AreEqual(Vec2.Zero, rebound.AppliedForce, "松开后不得残留鼠标弹簧力");
            Assert.IsFalse(rebound.IsGrabbed);
            Advance(rebound, 1f);
            Assert.Less(rebound.Ram.Position.Y, pressedY - 2, "松开后内存条应被动向上回弹");
            Assert.Less(rebound.BoardBend, .2f, "松开后板弯应回弹消失");
        }

        [Test]
        public void FrameRatesStayComparableLongFramesBoundedAndNonfiniteInputReleases()
        {
            var results = new System.Collections.Generic.List<(bool captured, Vec2 pos, float theta)>();
            foreach (float fps in new[] { 30f, 60f, 144f })
            {
                var sim = new MemoryInstallSimulation();
                Assert.IsTrue(sim.Grab(sim.Ram.LocalToWorld(new Vec2(-120, 0))));
                float anchorY = sim.GripWorld.Y;
                for (int i = 0; i < (int)(1.2f * fps); i++)
                {
                    sim.UpdateTarget(new Vec2(sim.GripWorld.X, anchorY + PressPull));
                    sim.Advance(1f / fps);
                }
                sim.Release();
                Advance(sim, .5f, fps);
                sim.Advance(.3f); // 长帧：有界追帧，状态保持有限。
                results.Add((sim.LeftCaptured, sim.Ram.Position, sim.Ram.Theta));
            }
            foreach (var r in results) Assert.IsTrue(r.captured, "各帧率下左端都应可捕获");
            Assert.AreEqual(results[0].pos.X, results[2].pos.X, 1.5f, "30 与 144fps 轨迹应可比较");
            Assert.AreEqual(results[0].pos.Y, results[2].pos.Y, 1.5f);
            Assert.AreEqual(results[0].theta, results[2].theta, .02f);
            foreach (var r in results) Assert.IsTrue(r.pos.IsFinite && float.IsFinite(r.theta));
            // 非有限输入等价于释放：不得残留约束。
            var bad = new MemoryInstallSimulation();
            Assert.IsTrue(bad.Grab(bad.Ram.LocalToWorld(Vec2.Zero)));
            bad.UpdateTarget(new Vec2(float.NaN, 0));
            Assert.IsFalse(bad.IsGrabbed);
            Assert.AreEqual(Vec2.Zero, bad.AppliedForce);
            // 空闲无输入不得瞬移。
            var idle = new MemoryInstallSimulation();
            Advance(idle, 2f);
            Assert.AreEqual(0, idle.Ram.Position.X, 1e-3);
            Assert.AreEqual(-idle.Tuning.HoverHeight, idle.Ram.Position.Y, 1e-3);
            Assert.AreEqual(0, idle.Ram.Theta, 1e-3);
        }

        [TestCase(30f)] [TestCase(60f)] [TestCase(144f)]
        public void AuthoredThickRamCanBeInstalledFromEitherEndAtEachFrameRate(float fps)
        {
            // The production UI derives this 700x236 physical rectangle from the authored art.
            // Test its different inertia/hover/travel bounds, rather than only the thin defaults.
            foreach (int first in new[] { 0, 2 })
            {
                var tuning = new MemoryInstallTuning
                {
                    RamThickness = 240f * 236 / 700, HoverHeight = 50,
                    BoundX = (1080 - 700) * .5f / (700f / 240), BoundUp = -50, BoundDown = 20
                };
                var sim = new MemoryInstallSimulation(tuning);
                foreach (int end in new[] { first, 2 - first })
                {
                    Assert.That(sim.Grab(sim.Ram.LocalToWorld(new Vec2(sim.NodeRestX(end), 0))), Is.True);
                    float y = sim.GripWorld.Y;
                    for (int i = 0; i < 4 * fps && !sim.IsCaptured(end); i++)
                    {
                        sim.UpdateTarget(new Vec2(sim.GripWorld.X, y + 90)); sim.Advance(1f / fps);
                    }
                    Assert.That(sim.IsCaptured(end), Is.True); sim.Release(); Advance(sim, .3f, fps);
                }
                Advance(sim, 3, fps);
                Assert.That(sim.Solved, Is.True, "Authored geometry must solve with either end first at " + fps + " FPS.");
            }
        }
    }
}
