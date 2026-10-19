using UnityEngine;
using UnityEngine.EventSystems;

namespace CallmeCatgirl.UI
{
    /// <summary>Title drag or bottom-right resize, expressed in the window parent's canvas units.</summary>
    public sealed class FloatingWindowGesture : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        private RectTransform target;
        private bool resize;
        private bool dragging;
        private int pointerId;
        private Vector2 previous;
        private readonly Vector3[] corners = new Vector3[4];
        public void Bind(RectTransform window, bool resizeCorner) { target = window; resize = resizeCorner; dragging = false; }
        public void OnBeginDrag(PointerEventData e)
        {
            if (target == null || dragging || e.button != PointerEventData.InputButton.Left
                || !RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)target.parent, e.position, e.pressEventCamera, out previous)) return;
            pointerId = e.pointerId; dragging = true;
        }
        public void OnDrag(PointerEventData e)
        {
            if (target == null || !dragging || e.pointerId != pointerId || e.button != PointerEventData.InputButton.Left) return;
            var parent = (RectTransform)target.parent;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, e.position, e.pressEventCamera, out var point)) return;
            var delta = point - previous; previous = point;
            if (resize)
            {
                var size = target.rect.size;
                float maxWidth = Mathf.Max(1, parent.rect.width - 24), maxHeight = Mathf.Max(1, parent.rect.height - 24);
                float width = Mathf.Clamp(size.x + delta.x, Mathf.Min(420, maxWidth), maxWidth);
                float height = Mathf.Clamp(size.y - delta.y, Mathf.Min(340, maxHeight), maxHeight);
                // Preserve the top-left corner regardless of the authored pivot.
                target.anchoredPosition += new Vector2((width - size.x) * target.pivot.x, -(height - size.y) * (1 - target.pivot.y));
                target.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
                target.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
            }
            else target.anchoredPosition += delta;
            ClampToParent(target, corners);
        }
        public void OnEndDrag(PointerEventData e) { if (e.pointerId == pointerId) dragging = false; }
        private void OnDisable() { dragging = false; }
        private void OnApplicationFocus(bool focused) { if (!focused) dragging = false; }
        public static void ClampToParent(RectTransform window)
            => ClampToParent(window, new Vector3[4]);
        private static void ClampToParent(RectTransform window, Vector3[] corners)
        {
            if (!(window.parent is RectTransform parent)) return;
            if (parent.rect.width <= 16 || parent.rect.height <= 16) return;
            var original = window.rect.size;
            var size = Vector2.Min(original, parent.rect.size - new Vector2(16, 16));
            if (size != original)
            {
                // Fit a resized viewport while retaining the same top-left attachment point.
                window.anchoredPosition += new Vector2((size.x - original.x) * window.pivot.x, -(size.y - original.y) * (1 - window.pivot.y));
                window.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, size.x);
                window.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, size.y);
            }
            window.GetWorldCorners(corners);
            var low = (Vector2)parent.InverseTransformPoint(corners[0]);
            var high = (Vector2)parent.InverseTransformPoint(corners[2]);
            var bounds = parent.rect; var correction = Vector2.zero;
            if (low.x < bounds.xMin + 8) correction.x += bounds.xMin + 8 - low.x;
            else if (high.x > bounds.xMax - 8) correction.x += bounds.xMax - 8 - high.x;
            if (low.y < bounds.yMin + 8) correction.y += bounds.yMin + 8 - low.y;
            else if (high.y > bounds.yMax - 8) correction.y += bounds.yMax - 8 - high.y;
            window.anchoredPosition += correction;
        }
    }
}
