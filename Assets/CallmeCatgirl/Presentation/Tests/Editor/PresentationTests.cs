using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;

namespace CallmeCatgirl.Presentation.Tests
{
    public sealed class PresentationTests
    {
        [Test]
        public void WipeReversesAtCurrentProgress()
        {
            var clock = new ReversibleProgress();
            clock.Request(true); clock.Tick(.24f, .6f);
            Assert.That(clock.Value, Is.EqualTo(.4f).Within(.00001f));
            clock.Request(false);
            Assert.That(clock.Value, Is.EqualTo(.4f).Within(.00001f));
            clock.Tick(.12f, .6f);
            Assert.That(clock.Value, Is.EqualTo(.2f).Within(.00001f));
        }

        [Test]
        public void UpperLeftCornerMustBeCoveredBeforePanelDrifts()
        {
            float last = MotionMath.SweepDistance(100, 900, 1600, 1000);
            var drift = new PaperDrift(5);
            drift.Request(true);
            drift.MarkCovered(MotionMath.IsCovered(.9f, last, 1600, 1000));
            drift.Tick(5, false, false, new MotionPose(10, 12, .9f));
            Assert.That(drift.Swept, Is.False);
            Assert.That(drift.Pose.Angle, Is.Zero);
            drift.MarkCovered(MotionMath.IsCovered(.93f, last, 1600, 1000));
            drift.Tick(.016f, false, false, new MotionPose(10, 12, .9f));
            Assert.That(drift.SegmentsStarted, Is.EqualTo(1));
        }

        [Test]
        public void HoverHoldsRotationInsteadOfReturningHome()
        {
            var drift = new PaperDrift(5);
            drift.Request(true); drift.MarkCovered(true);
            var limits = new MotionPose(10, 12, .9f);
            drift.Tick(0, false, false, limits); drift.Tick(3, false, false, limits);
            float angle = drift.Pose.Angle;
            Assert.That(angle, Is.Not.Zero);
            drift.Tick(20, true, false, limits);
            Assert.That(drift.Pose.Angle, Is.EqualTo(angle));
        }

        [Test]
        public void ModelCloseReversalDoesNotTeleportFrame()
        {
            var motion = new ModelFrameMotion();
            var home = new MotionRect(100, 100, 180, 120);
            var screen = new MotionRect(0, 0, 1920, 1080);
            motion.Request(true, home, screen); motion.Tick(.31f, home, screen);
            var painted = motion.Current;
            motion.Request(false, home, screen);
            Assert.That(motion.Current.Same(painted), Is.True);
            motion.Tick(0, home, screen);
            Assert.That(motion.Current.Same(painted), Is.True);
        }

        [Test]
        public void GraphicNeverInterceptsRaycasts()
        {
            var go = new GameObject("Difference test", typeof(RectTransform), typeof(CanvasRenderer), typeof(DifferenceWipeGraphic));
            try
            {
                var graphic = go.GetComponent<DifferenceWipeGraphic>();
                graphic.raycastTarget = true; // Even an accidental inspector change cannot intercept input.
                Assert.That(graphic.Raycast(Vector2.zero, null), Is.False);
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void InputSystemPositivePointerIdsAreAccepted()
        {
            var go = new GameObject("Paper", typeof(RectTransform), typeof(PlanningPaper));
            var eventGo = new GameObject("Events", typeof(EventSystem));
            try
            {
                var data = new PointerEventData(eventGo.GetComponent<EventSystem>()) { pointerId = 17 };
                var paper = go.GetComponent<PlanningPaper>();
                paper.OnPointerEnter(data);
                Assert.That(paper.IsPointerInside, Is.True);
                paper.OnPointerExit(data);
                Assert.That(paper.IsPointerInside, Is.False);
            }
            finally { Object.DestroyImmediate(go); Object.DestroyImmediate(eventGo); }
        }

        // 组件夹具依赖可靠的 MonoBehaviour 生命周期，必须在 PlayMode 下创建；
        // EditMode 不会可靠触发 Awake/OnEnable，Graphic.material 默认值也不能证明初始化已运行。
        private sealed class Fixture
        {
            public GameObject Root;
            public PlanningPresentation Presentation;
            public DifferenceWipeGraphic Overlay;
            public PlanningPaper Paper;
            public RectTransform MotionRoot;
            public readonly Vector2 Home = new Vector2(120, -45);
            public readonly Quaternion HomeRotation = Quaternion.Euler(0, 0, 3);
            private Material template;
            public static Fixture Create()
            {
                var shader = Shader.Find("CallmeCatgirl/UI/Difference Wipe");
                Assume.That(shader, Is.Not.Null, "Difference wipe shader asset is required for the component fixture.");
                var fixture = new Fixture();
                fixture.Root = new GameObject("Presentation", typeof(RectTransform));
                fixture.Root.SetActive(false);
                fixture.Root.AddComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
                fixture.Presentation = fixture.Root.AddComponent<PlanningPresentation>();
                var overlayGo = new GameObject("Overlay", typeof(RectTransform), typeof(CanvasRenderer), typeof(DifferenceWipeGraphic));
                overlayGo.transform.SetParent(fixture.Root.transform, false);
                fixture.Overlay = overlayGo.GetComponent<DifferenceWipeGraphic>();
                var paperGo = new GameObject("Paper", typeof(RectTransform));
                paperGo.SetActive(false); // CaptureHome(Awake) 必须看到预期的基准位姿，而不是之后的位移。
                fixture.MotionRoot = (RectTransform)paperGo.transform;
                fixture.MotionRoot.anchoredPosition = fixture.Home;
                fixture.MotionRoot.localRotation = fixture.HomeRotation;
                fixture.Paper = paperGo.AddComponent<PlanningPaper>();
                paperGo.SetActive(true); // PlayMode 下 Awake 可靠执行，捕获上面的基准位姿。
                var serialized = new SerializedObject(fixture.Presentation);
                serialized.FindProperty("overlay").objectReferenceValue = fixture.Overlay;
                fixture.template = new Material(shader);
                serialized.FindProperty("materialTemplate").objectReferenceValue = fixture.template;
                var papers = serialized.FindProperty("papers");
                papers.arraySize = 1;
                papers.GetArrayElementAtIndex(0).objectReferenceValue = fixture.Paper;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                fixture.Root.SetActive(true); // PlayMode 下 OnEnable 可靠执行。
                var instance = typeof(PlanningPresentation).GetField("instance", BindingFlags.Instance | BindingFlags.NonPublic)
                    .GetValue(fixture.Presentation);
                Assert.That(instance, Is.Not.Null, "OnEnable must have created the private runtime material instance.");
                return fixture;
            }
            public void Tick(string method) => typeof(PlanningPresentation)
                .GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(Presentation, null);
            public void Dispose()
            {
                if (Root != null) Object.DestroyImmediate(Root);
                if (Paper != null) Object.DestroyImmediate(Paper.gameObject);
                if (template != null) Object.DestroyImmediate(template);
            }
        }

        [UnityTest]
        public IEnumerator SuppressedByDefaultWipeStaysHiddenAndPaperDoesNotDrift()
        {
            yield return new EnterPlayMode();
            var fixture = Fixture.Create();
            try
            {
                Assert.That(fixture.Presentation.PauseVisualsEnabled, Is.False, "Visuals default off without any serialized scene value.");
                fixture.Presentation.SetPlanning(true);
                Assert.That(fixture.Presentation.PlanningRequested, Is.True, "Logical planning request is still tracked.");
                fixture.Tick("LateUpdate"); fixture.Tick("LateUpdate");
                Assert.That(fixture.Presentation.WipeProgress, Is.Zero);
                Assert.That(fixture.MotionRoot.anchoredPosition, Is.EqualTo(fixture.Home));
                Assert.That(fixture.MotionRoot.localRotation, Is.EqualTo(fixture.HomeRotation));
                fixture.Overlay.enabled = true; // Even an accidental enable is forced back off.
                fixture.Tick("BeforeRender");
                Assert.That(fixture.Overlay.enabled, Is.False);
                // A legitimate layout change is not overwritten while suppressed.
                var moved = fixture.Home + new Vector2(33, -18);
                fixture.MotionRoot.anchoredPosition = moved;
                fixture.Tick("LateUpdate");
                Assert.That(fixture.MotionRoot.anchoredPosition, Is.EqualTo(moved));
                fixture.Presentation.SetPlanning(false);
                Assert.That(fixture.Presentation.PlanningRequested, Is.False);
                Assert.That(fixture.Presentation.WipeProgress, Is.Zero);
            }
            finally { fixture.Dispose(); }
            yield return new ExitPlayMode();
        }

        [UnityTest]
        public IEnumerator DisablingVisualsRestoresCapturedHomeWithoutCumulativeOffset()
        {
            yield return new EnterPlayMode();
            var fixture = Fixture.Create();
            try
            {
                for (int round = 0; round < 2; round++)
                {
                    fixture.Presentation.PauseVisualsEnabled = true;
                    // Simulate a previously drifted presentation pose (no frames run inside this block).
                    fixture.MotionRoot.anchoredPosition = fixture.Home + new Vector2(9 + round, -14);
                    fixture.MotionRoot.localRotation = fixture.HomeRotation * Quaternion.Euler(0, 0, 12);
                    fixture.Presentation.PauseVisualsEnabled = false;
                    Assert.That(fixture.MotionRoot.anchoredPosition, Is.EqualTo(fixture.Home), "round " + round);
                    Assert.That(fixture.MotionRoot.localRotation, Is.EqualTo(fixture.HomeRotation), "round " + round);
                    Assert.That(fixture.Presentation.WipeProgress, Is.Zero);
                    Assert.That(fixture.Overlay.enabled, Is.False);
                }
                Assert.That(fixture.Paper.CurrentPose.X, Is.Zero);
                Assert.That(fixture.Paper.CurrentPose.Angle, Is.Zero);
            }
            finally { fixture.Dispose(); }
            yield return new ExitPlayMode();
        }

        [Test]
        public void SnapKeepsRequestedStateAndSnapHomeClearsDrift()
        {
            var progress = new ReversibleProgress();
            progress.Request(true); progress.Tick(.3f, .6f);
            progress.Snap(0);
            Assert.That(progress.Value, Is.Zero);
            Assert.That(progress.Requested, Is.True, "Snap must not clear the logical request.");
            var drift = new PaperDrift(5);
            drift.Request(true); drift.MarkCovered(true);
            var limits = new MotionPose(10, 12, .9f);
            // 第一段 Tick 只开始新片段；位姿由后续受钳制的时间步推进（与 HoverHoldsRotation 测试同一模式）。
            drift.Tick(0, false, false, limits);
            drift.Tick(20, false, false, limits);
            Assert.That(drift.Pose.Angle, Is.Not.Zero);
            drift.SnapHome();
            Assert.That(drift.Pose.Angle, Is.Zero);
            Assert.That(drift.Pose.X, Is.Zero);
            Assert.That(drift.Planning, Is.False);
            Assert.That(drift.Swept, Is.False);
        }
    }
}
