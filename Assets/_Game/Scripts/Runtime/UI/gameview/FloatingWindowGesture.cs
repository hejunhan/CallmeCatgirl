using UnityEngine;
using UnityEngine.EventSystems;

namespace CallmeCatgirl.UI
{
    /// <summary>Title drag or bottom-right resize, expressed in the window parent's canvas units.</summary>
    public sealed class FloatingWindowGesture : MonoBehaviour, IBeginDragHandler, IDragHandler
    {
        private RectTransform target;
        private bool resize;
        private Vector2 previous;
        private readonly Vector3[] corners = new Vector3[4];
        public void Bind(RectTransform window, bool resizeCorner) { target = window; resize = resizeCorner; }
        public void OnBeginDrag(PointerEventData e)
        {
            if (target == null || e.button != PointerEventData.InputButton.Left) return;
            RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)target.parent, e.position, e.pressEventCamera, out previous);
        }
        public void OnDrag(PointerEventData e)
        {
            if (target == null || e.button != PointerEventData.InputButton.Left) return;
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
            ClampToParent(target);
        }
        public static void ClampToParent(RectTransform window)
        {
            if (!(window.parent is RectTransform parent)) return;
            var corners = new Vector3[4]; window.GetWorldCorners(corners);
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
