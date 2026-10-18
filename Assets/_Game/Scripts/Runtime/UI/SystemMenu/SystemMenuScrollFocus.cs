using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CallmeCatgirl.UI.SystemMenu
{
    /// <summary>Keyboard/gamepad navigation keeps selected settings controls in the visible scroll area.</summary>
    public sealed class SystemMenuScrollFocus : MonoBehaviour, ISelectHandler
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
