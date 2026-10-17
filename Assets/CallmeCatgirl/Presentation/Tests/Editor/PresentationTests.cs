using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;

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
    }
}
