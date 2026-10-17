using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace CallmeCatgirl.Presentation
{
    [DisallowMultipleComponent]
    [AddComponentMenu("CallmeCatgirl/Presentation/Planning Paper")]
    public sealed class PlanningPaper : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        private static uint nextSeed;
        [SerializeField] private RectTransform motionRoot;
        [SerializeField, Tooltip("Optional second physical sheet. Must move with motionRoot.")]
        private RectTransform rearSheet = null;
        [SerializeField] private Vector2 driftPixels = new Vector2(10, 12);
        [SerializeField, Range(0, 3)] private float driftDegrees = .9f;
        [SerializeField, Tooltip("Zero derives a separate local seed per panel; gameplay Random is never touched.")] private uint randomSeed = 0;
        private readonly Vector3[] corners = new Vector3[4];
        private readonly HashSet<int> pointers = new HashSet<int>();
        private PaperDrift drift;
        private Vector2 home;
        private Quaternion homeRotation;
        private Canvas rootCanvas;
        private bool engaged;
        private float restoration;
        internal float Restoration => MotionMath.Smooth(restoration);
        internal RectTransform RearSheet => rearSheet;
        internal bool HasGeometry => isActiveAndEnabled && motionRoot != null && motionRoot.gameObject.activeInHierarchy;
        public bool IsSwept => drift != null && drift.Swept;
        public bool IsPointerInside => pointers.Count > 0;
        public MotionPose CurrentPose => drift == null ? default : drift.Pose;

        private void Awake() => CaptureHome();
        public void CaptureHome()
        {
            if (motionRoot == null) motionRoot = transform as RectTransform;
            if (motionRoot == null) return;
            home = motionRoot.anchoredPosition; homeRotation = motionRoot.localRotation;
            var owner = motionRoot.GetComponentInParent<Canvas>();
            rootCanvas = owner == null ? null : owner.rootCanvas;
            drift = new PaperDrift(randomSeed == 0 ? unchecked(++nextSeed * 2654435761u) : randomSeed);
        }
        public void OnPointerEnter(PointerEventData eventData) => pointers.Add(eventData.pointerId);
        public void OnPointerExit(PointerEventData eventData) => pointers.Remove(eventData.pointerId);
        private void OnDisable() { pointers.Clear(); restoration = 0; }
        internal void Request(bool planning)
        {
            if (drift == null) CaptureHome();
            drift?.Request(planning);
        }
        internal void Advance(float delta, bool reduced, Transform selection)
        {
            if (!HasGeometry || drift == null) return;
            engaged = pointers.Count > 0 || (selection != null && (selection == transform || selection.IsChildOf(transform)));
            drift.Tick(delta, engaged, reduced, new MotionPose(driftPixels.x, driftPixels.y, driftDegrees));
            var pose = drift.Pose;
            float scale = rootCanvas == null ? 1 : Mathf.Max(.0001f, rootCanvas.scaleFactor);
            motionRoot.anchoredPosition = home + new Vector2(Mathf.Round(pose.X), Mathf.Round(pose.Y)) / scale;
            motionRoot.localRotation = homeRotation * Quaternion.Euler(0, 0, pose.Angle);
        }
        internal float Sample(RectTransform sheet, Vector2 viewport, out Vector4 pair01, out Vector4 pair23)
        {
            sheet.GetWorldCorners(corners);
            Camera camera = rootCanvas == null || rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : rootCanvas.worldCamera;
            Vector2 a = RectTransformUtility.WorldToScreenPoint(camera, corners[0]);
            Vector2 b = RectTransformUtility.WorldToScreenPoint(camera, corners[1]);
            Vector2 c = RectTransformUtility.WorldToScreenPoint(camera, corners[2]);
            Vector2 d = RectTransformUtility.WorldToScreenPoint(camera, corners[3]);
            pair01 = new Vector4(a.x, a.y, b.x, b.y); pair23 = new Vector4(c.x, c.y, d.x, d.y);
            Vector2 min = Vector2.Min(Vector2.Min(a, b), Vector2.Min(c, d));
            Vector2 max = Vector2.Max(Vector2.Max(a, b), Vector2.Max(c, d));
            return MotionMath.PanelSweepDistance(min.x, min.y, max.x, max.y, viewport.x, viewport.y);
        }
        internal float SampleFront(Vector2 viewport, out Vector4 pair01, out Vector4 pair23)
            => Sample(motionRoot, viewport, out pair01, out pair23);
        internal void ObserveCoverage(float delta, bool planning, bool covered, bool reduced)
        {
            drift?.MarkCovered(covered);
            float target = planning && covered && engaged ? 1 : 0;
            restoration = reduced ? target : MotionMath.Approach(restoration, target, delta, .7f);
        }
    }
}
