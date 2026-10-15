using UnityEngine;
using UnityEngine.UI;

namespace CallmeCatgirl.UI
{
    public sealed class GridCellView : MonoBehaviour
    {
        [SerializeField] private Image background;
        [SerializeField] private GameObject lockedMarker;
        [SerializeField] private Color openColor = new Color(0.17f, 0.21f, 0.27f);
        [SerializeField] private Color lockedColor = new Color(0.08f, 0.10f, 0.13f);
        private bool shown, wasOpen;
        private float unlockedAt = -100;
        public void Show(bool open, UIFeedbackConfiguration feedback = null)
        {
            if (shown && open && !wasOpen) unlockedAt = Time.unscaledTime;
            shown = true; wasOpen = open;
            float pulse = feedback != null && feedback.Animate && open
                ? Mathf.Clamp01(1 - (Time.unscaledTime - unlockedAt) / Mathf.Max(.05f,feedback.PulseSeconds)) * feedback.PulseStrength : 0;
            background.color = Color.Lerp(open ? openColor : lockedColor, feedback == null ? openColor : feedback.Signal,pulse);
            if (lockedMarker != null) lockedMarker.SetActive(!open);
        }
    }
}
