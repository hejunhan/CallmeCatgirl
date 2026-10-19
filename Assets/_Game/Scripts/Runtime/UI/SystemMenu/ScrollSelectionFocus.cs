using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CallmeCatgirl.UI.SystemMenu
{
    /// <summary>Shared UGUI behavior: keyboard/gamepad selection reveals a control in its scroll viewport.</summary>
    public class ScrollSelectionFocus : MonoBehaviour, ISelectHandler
    {
        public void OnSelect(BaseEventData eventData)
        {
            var scroll = GetComponentInParent<ScrollRect>();
            if (scroll == null || scroll.viewport == null || scroll.content == null || !scroll.vertical) return;
            Canvas.ForceUpdateCanvases();
            var bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(scroll.viewport, transform);
            var visible = scroll.viewport.rect;
            float delta = bounds.max.y > visible.yMax ? visible.yMax - bounds.max.y : bounds.min.y < visible.yMin ? visible.yMin - bounds.min.y : 0;
            if (Mathf.Abs(delta) < .1f) return;
            scroll.StopMovement();
            scroll.content.anchoredPosition += new Vector2(0, delta);
        }
    }
}
