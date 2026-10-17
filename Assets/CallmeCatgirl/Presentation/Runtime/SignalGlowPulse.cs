using UnityEngine;
using UnityEngine.UI;

namespace CallmeCatgirl.Presentation
{
    [RequireComponent(typeof(Graphic))]
    [AddComponentMenu("CallmeCatgirl/Presentation/Signal Glow Pulse")]
    public sealed class SignalGlowPulse : MonoBehaviour
    {
        [SerializeField, Min(.1f)] private float period = 3.6f;
        [SerializeField, Range(0, 1)] private float depth = .12f;
        [SerializeField] private bool reducedMotion = false;
        private Graphic graphic;
        private float phase;
        private bool focused = true, appPaused, skipDelta = true;
        private void Awake() { graphic = GetComponent<Graphic>(); graphic.raycastTarget = false; }
        private void OnEnable() { focused = Application.isFocused; skipDelta = true; }
        private void OnApplicationFocus(bool value) { focused = value; skipDelta = true; }
        private void OnApplicationPause(bool value) { appPaused = value; skipDelta = true; }
        private void Update()
        {
            float delta = focused && !appPaused && !skipDelta ? Time.unscaledDeltaTime : 0;
            skipDelta = false;
            phase = Mathf.Repeat(phase + delta / Mathf.Max(.1f, period), 1);
            float alpha = reducedMotion ? 1 : 1-depth*(.5f+.5f*Mathf.Cos(phase*Mathf.PI*2));
            graphic.canvasRenderer.SetAlpha(alpha); // Per-renderer multiplier; material stays shared.
        }
        private void OnDisable() { if (graphic != null) graphic.canvasRenderer.SetAlpha(1); }
    }
}
