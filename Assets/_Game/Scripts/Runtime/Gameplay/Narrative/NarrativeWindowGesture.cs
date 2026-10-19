using UnityEngine;
using UnityEngine.EventSystems;

namespace CallmeCatgirl.Gameplay.Narrative
{
    /// <summary>Pointer coordinates are measured in the modal canvas, including CanvasScaler transforms.</summary>
    public sealed class NarrativeWindowGesture : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        [SerializeField] private RectTransform window, bounds;
        [SerializeField] private bool resize;
        private bool dragging;
        private int pointerId;
        private Vector2 originPointer, originPosition, originSize, boundsSize;
        public void Bind(RectTransform target, RectTransform screenBounds, bool resizing)
        { window = target; bounds = screenBounds; resize = resizing; boundsSize = bounds.rect.size; }
        public void OnBeginDrag(PointerEventData data)
        {
            if (window == null || bounds == null || data.button != PointerEventData.InputButton.Left
                || !RectTransformUtility.ScreenPointToLocalPointInRectangle(bounds, data.position, data.pressEventCamera, out originPointer)) return;
            var size = window.rect.size;
            var position = (Vector2)bounds.InverseTransformPoint(window.position);
            window.anchorMin = window.anchorMax = new Vector2(.5f, .5f); window.pivot = new Vector2(.5f, .5f);
            window.sizeDelta = size; window.anchoredPosition = position;
            originSize = size; originPosition = position; pointerId = data.pointerId; dragging = true;
        }
        public void OnDrag(PointerEventData data)
        {
            if (!dragging || data.pointerId != pointerId || !RectTransformUtility.ScreenPointToLocalPointInRectangle(bounds, data.position, data.pressEventCamera, out var point)) return;
            var delta = point - originPointer;
            if (resize)
            {
                var max = bounds.rect.size - new Vector2(24, 24);
                var min = new Vector2(Mathf.Min(520, max.x), Mathf.Min(360, max.y));
                var size = new Vector2(Mathf.Clamp(originSize.x + delta.x, min.x, max.x), Mathf.Clamp(originSize.y - delta.y, min.y, max.y));
                window.sizeDelta = size;
                window.anchoredPosition = originPosition + new Vector2((size.x - originSize.x) * .5f, -(size.y - originSize.y) * .5f);
            }
            else window.anchoredPosition = originPosition + delta;
            ClampToScreen();
        }
        public void OnEndDrag(PointerEventData data) { if (data.pointerId == pointerId) dragging = false; }
        private void OnDisable() { dragging = false; }
        private void LateUpdate()
        {
            if (window != null && bounds != null && window.anchorMin == window.anchorMax && boundsSize != bounds.rect.size)
            { boundsSize = bounds.rect.size; ClampToScreen(); }
        }
        private void ClampToScreen()
        {
            var max = bounds.rect.size - new Vector2(24, 24);
            var size = window.rect.size;
            size.x = Mathf.Min(size.x, max.x); size.y = Mathf.Min(size.y, max.y);
            window.sizeDelta = size;
            var halfRoom = (bounds.rect.size - size) * .5f - new Vector2(8, 8);
            var pos = window.anchoredPosition;
            pos.x = Mathf.Clamp(pos.x, -Mathf.Max(0, halfRoom.x), Mathf.Max(0, halfRoom.x));
            pos.y = Mathf.Clamp(pos.y, -Mathf.Max(0, halfRoom.y), Mathf.Max(0, halfRoom.y));
            window.anchoredPosition = pos;
        }
    }
}
