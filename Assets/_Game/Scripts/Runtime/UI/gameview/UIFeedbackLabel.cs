using UnityEngine;
using UnityEngine.UI;

namespace CallmeCatgirl.UI
{
    [RequireComponent(typeof(CanvasGroup))]
    public sealed class UIFeedbackLabel : MonoBehaviour
    {
        [SerializeField] private Text label;
        private CanvasGroup group;
        private RectTransform rect;
        private Vector2 origin;
        private float started, duration, rise, pop;
        private void Awake()
        {
            rect = (RectTransform)transform; origin = rect.anchoredPosition;
            group = GetComponent<CanvasGroup>(); group.blocksRaycasts = false; group.interactable = false;
            label.raycastTarget = false; Clear();
        }
        public void SetOrigin(Vector2 position) { origin = position; }
        public void Play(string text, Color color, float seconds, float distance, float scale)
        {
            if (group == null) Awake();
            label.text = text; label.color = color; duration = Mathf.Max(.05f, seconds);
            rise = distance; pop = scale; started = Time.unscaledTime;
            group.alpha = 1; rect.anchoredPosition = origin; rect.localScale = Vector3.one * (1 + pop);
        }
        public void Clear()
        {
            duration = 0;
            if (group != null) group.alpha = 0;
            if (rect != null) { rect.anchoredPosition = origin; rect.localScale = Vector3.one; }
        }
        private void LateUpdate()
        {
            if (duration <= 0) return;
            float t = Mathf.Clamp01((Time.unscaledTime - started) / duration);
            rect.anchoredPosition = origin + Vector2.up * rise * (1 - (1-t)*(1-t));
            rect.localScale = Vector3.one * (1 + pop * Mathf.Clamp01(1-t*4));
            group.alpha = 1 - Mathf.InverseLerp(.65f, 1, t);
            if (t >= 1) Clear();
        }
        private void OnDisable() => Clear();
    }
}
