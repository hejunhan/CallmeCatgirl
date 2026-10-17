using System;
using UnityEngine;
using UnityEngine.UI;

namespace CallmeCatgirl.Presentation
{
    [DisallowMultipleComponent, DefaultExecutionOrder(10000)]
    [AddComponentMenu("CallmeCatgirl/Presentation/Model Page Reveal")]
    public sealed class ModelPageReveal : MonoBehaviour
    {
        [Serializable]
        public sealed class RevealItem
        {
            public CanvasGroup Group;
            [Tooltip("Use a wrapper not driven by a LayoutGroup. Optional: only fade without it.")]
            public RectTransform MotionRoot;
            [Min(0)] public float Delay = .06f;
            [Min(0)] public float Rise = 12;
            [NonSerialized] internal Vector2 Home;
            [NonSerialized] internal float Amount;
        }
        [SerializeField] private RectTransform trigger = null;
        [SerializeField] private RectTransform viewport = null;
        [SerializeField, Tooltip("One opaque white Image + RectMask2D; its parent must be stationary.")]
        private RectTransform frame = null;
        [SerializeField, Tooltip("Direct child of frame, with no background Image. Items are nested inside.")]
        private CanvasGroup content = null;
        [SerializeField] private RevealItem[] items = new RevealItem[0];
        [SerializeField] private bool reducedMotion;
        private readonly ModelFrameMotion motion = new ModelFrameMotion();
        private readonly Vector3[] corners = new Vector3[4];
        private RectTransform frameParent;
        private MotionRect homeRect, viewportRect;
        private bool focused = true, appPaused, blocked, skipDelta = true, homesCaptured;
        private float revealAge;
        public bool IsOpenRequested => motion.RequestedOpen;
        public bool IsAnimating => motion.Active;
        public float Expansion => motion.Expansion;
        public bool ReducedMotion { get => reducedMotion; set => reducedMotion = value; }
        public event Action<bool> OpenRequestedChanged;
        public void Configure(RectTransform source, RectTransform area, RectTransform whiteFrame, CanvasGroup body)
        {
            trigger = source; viewport = area; frame = whiteFrame; content = body;
        }

        private void Awake()
        {
            frameParent = frame == null ? null : frame.parent as RectTransform;
            if (trigger == null || viewport == null || frame == null || frameParent == null || content == null ||
                content.transform.parent != frame || frame.GetComponent<RectMask2D>() == null)
            { Debug.LogError("ModelPageReveal: assign trigger, viewport, frame with RectMask2D, and its direct content child.", this); enabled = false; return; }
            if (transform == frame || transform.IsChildOf(frame))
            { Debug.LogError("ModelPageReveal: the controller must live outside the frame it hides.", this); enabled = false; return; }
            frame.anchorMin = frame.anchorMax = frame.pivot = new Vector2(.5f, .5f);
            frame.localRotation = Quaternion.identity; frame.localScale = Vector3.one;
            var image = frame.GetComponent<Image>();
            if (image != null) { image.color = Color.white; image.raycastTarget = false; }
            content.interactable = content.blocksRaycasts = false;
            frame.gameObject.SetActive(false);
        }
        private void OnEnable() { focused = Application.isFocused; skipDelta = true; }
        public void CaptureItemHomes()
        {
            foreach (var item in items)
            {
                if (item == null) continue;
                if (item.MotionRoot != null) item.Home = item.MotionRoot.anchoredPosition;
                if (item.Group != null) item.Group.alpha = 0;
                item.Amount = 0;
            }
            homesCaptured = true;
        }
        public void Open() => SetOpen(true);
        public void Close() => SetOpen(false);
        public void SetOpen(bool value)
        {
            if (!enabled || frameParent == null) return;
            Measure();
            if (!motion.Request(value, homeRect, viewportRect)) return;
            if (!homesCaptured) CaptureItemHomes();
            revealAge = 0;
            content.interactable = content.blocksRaycasts = value;
            frame.gameObject.SetActive(true);
            skipDelta = true;
            OpenRequestedChanged?.Invoke(value);
            Paint(); // Request reversal paints the exact existing rectangle.
        }
        public void SetPresentationBlocked(bool value)
        {
            if (blocked == value) return;
            blocked = value; skipDelta = true;
        }
        private void OnApplicationFocus(bool value) { focused = value; skipDelta = true; }
        private void OnApplicationPause(bool value) { appPaused = value; skipDelta = true; }
        private void LateUpdate()
        {
            if (frameParent == null) return;
            if (!homesCaptured) CaptureItemHomes();
            if (!motion.RequestedOpen && !motion.Active) return;
            bool available = focused && !appPaused && !blocked;
            float delta = available && !skipDelta ? Time.unscaledDeltaTime : 0;
            skipDelta = false;
            Measure();
            motion.Tick(delta, homeRect, viewportRect, reducedMotion && available);
            if (motion.RequestedOpen && motion.Expansion >= .78f) revealAge += delta;
            else revealAge = 0;
            foreach (var item in items)
            {
                if (item == null || item.Group == null) continue;
                bool show = motion.RequestedOpen && motion.Expansion >= .78f && (reducedMotion || revealAge >= item.Delay);
                item.Amount = reducedMotion && available ? (show ? 1 : 0) : MotionMath.Approach(item.Amount, show ? 1 : 0, delta, .32f);
                float t = MotionMath.Smooth(item.Amount);
                item.Group.alpha = t;
                item.Group.interactable = item.Group.blocksRaycasts = motion.RequestedOpen && item.Amount >= 1;
                if (item.MotionRoot != null) item.MotionRoot.anchoredPosition = item.Home + Vector2.down * (item.Rise * (1 - t));
            }
            Paint();
        }
        private void Paint()
        {
            var r = motion.Current;
            frame.localPosition = new Vector3(r.X + r.Width * .5f, r.Y + r.Height * .5f, frame.localPosition.z);
            frame.sizeDelta = new Vector2(r.Width, r.Height);
            var full = viewportRect;
            var contentRect = (RectTransform)content.transform;
            contentRect.anchorMin = contentRect.anchorMax = contentRect.pivot = new Vector2(.5f, .5f);
            contentRect.localPosition = new Vector3(full.X + full.Width * .5f - frame.localPosition.x,
                full.Y + full.Height * .5f - frame.localPosition.y, contentRect.localPosition.z);
            contentRect.sizeDelta = new Vector2(full.Width, full.Height);
            frame.gameObject.SetActive(motion.RequestedOpen || motion.Active);
        }
        private void Measure() { homeRect = Bounds(trigger); viewportRect = Bounds(viewport); }
        private MotionRect Bounds(RectTransform target)
        {
            target.GetWorldCorners(corners);
            Vector2 min = new Vector2(float.PositiveInfinity, float.PositiveInfinity), max = -min;
            for (int i = 0; i < 4; i++)
            {
                Vector2 p = frameParent.InverseTransformPoint(corners[i]);
                min = Vector2.Min(min, p); max = Vector2.Max(max, p);
            }
            return new MotionRect(min.x, min.y, max.x - min.x, max.y - min.y);
        }
    }
}
