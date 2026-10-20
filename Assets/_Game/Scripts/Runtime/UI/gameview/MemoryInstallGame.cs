using System;
using CallmeCatgirl.Gameplay.TaskGrid;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CallmeCatgirl.UI
{
    /// <summary>Physical installation shared by the upgrade popup and standalone scene.</summary>
    public class MemoryInstallGame : MonoBehaviour
    {
        // Optional external overlay; the standalone scene renders directly in this object's window.
        [SerializeField] private GameObject gamePanel;
        [SerializeField] private RectTransform memoryStick, leftClip, rightClip;
        // Keep authored references so existing scenes and prefabs need no migration.
        [SerializeField] private Button pressLeftButton, pressRightButton, skipButton, finishButton;
        [SerializeField] private TMP_Text tipText;
        [SerializeField] private float openAngle = 20;
        [SerializeField] private MemoryInstallTuning physicsTuning = new MemoryInstallTuning { HoverHeight = 50 };
        private bool installed, bound;
        private RectTransform frame;
        private Vector2 socketOrigin, leftClipRest, rightClipRest;
        private float unitsToCanvas;
        private int? activePointer;
        private Camera dragCamera;
        private MemoryInstallSimulation simulation;
        private GameObject frontMask;

        public bool IsOpen { get; private set; }
        public bool IsComplete => installed && !IsOpen;
        public MemoryInstallSimulation PhysicsState => simulation;
        public int? ActivePointerId => activePointer;
        public RectTransform RamTransform => memoryStick;
        public event Action InstallCompleted;

        public void OpenGame()
        {
            if (!enabled || memoryStick == null || leftClip == null || rightClip == null
                || skipButton == null || finishButton == null || memoryStick.GetComponent<Graphic>() == null)
                throw new InvalidOperationException("内存安装界面缺少必要控件。");
            if (IsOpen) return;
            var layers = GetComponentInParent<UIWindowLayers>(true);
            // Observe the already-open upgrade modal before this modal becomes visible, even within one frame.
            layers?.Synchronize();
            EnsureBound(); ResetGame(); IsOpen = true;
            (gamePanel != null ? gamePanel : gameObject).SetActive(true); Paint();
            layers?.Synchronize();
        }

        private void EnsureBound()
        {
            if (bound) return;
            frame = memoryStick.parent as RectTransform;
            if (frame == null || leftClip.parent != frame || rightClip.parent != frame)
                throw new InvalidOperationException("内存条与卡扣必须位于同一个安装窗口。");
            frontMask = frame.Find("前沿遮挡")?.gameObject;
            // One similarity transform serves drawing, hit testing, and cursor forces.
            unitsToCanvas = memoryStick.rect.width / Mathf.Max(1, physicsTuning.RamLength);
            physicsTuning.RamThickness = memoryStick.rect.height / unitsToCanvas;
            physicsTuning.BoundX = Mathf.Min(physicsTuning.BoundX, Mathf.Max(1, (frame.rect.width - memoryStick.rect.width) * .5f / unitsToCanvas));
            physicsTuning.BoundUp = Mathf.Max(physicsTuning.BoundUp, -physicsTuning.HoverHeight);
            physicsTuning.BoundDown = Mathf.Min(physicsTuning.BoundDown, 20);
            physicsTuning.Sanitize(); simulation = new MemoryInstallSimulation(physicsTuning);
            leftClipRest = leftClip.localPosition; rightClipRest = rightClip.localPosition;
            socketOrigin = (leftClipRest + rightClipRest) * .5f;
            memoryStick.pivot = new Vector2(.5f, .5f); memoryStick.localScale = Vector3.one;
            memoryStick.GetComponent<Graphic>().raycastTarget = true;
            var surface = memoryStick.GetComponent<MemoryInstallPointerSurface>();
            if (surface == null) surface = memoryStick.gameObject.AddComponent<MemoryInstallPointerSurface>();
            surface.Bind(this);
            DisableLegacyButton(pressLeftButton); DisableLegacyButton(pressRightButton);
            skipButton.onClick.AddListener(SkipInstall); finishButton.onClick.AddListener(CompleteInstall);
            foreach (Transform child in frame)
            {
                if (child.name != "插槽底座" && child.name != "前沿遮挡") continue;
                var source = child.GetComponent<Image>();
                if (source == null) continue;
                var graphic = child.GetComponent<MemorySocketGraphic>();
                if (graphic == null) graphic = child.gameObject.AddComponent<MemorySocketGraphic>();
                graphic.Bind(this, source.sprite, source.color); source.raycastTarget = false;
            }
            foreach (var clip in new[] { leftClip, rightClip })
                foreach (var graphic in clip.GetComponentsInChildren<Graphic>(true)) graphic.raycastTarget = false;
            memoryStick.SetAsLastSibling(); leftClip.SetAsLastSibling(); rightClip.SetAsLastSibling();
            tipText?.transform.SetAsLastSibling(); skipButton.transform.SetAsLastSibling(); finishButton.transform.SetAsLastSibling();
            bound = true;
        }

        private static void DisableLegacyButton(Button button)
        {
            if (button == null) return;
            button.onClick.RemoveAllListeners(); button.interactable = false;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            foreach (var graphic in button.GetComponentsInChildren<Graphic>(true)) graphic.raycastTarget = false;
            button.enabled = false; button.gameObject.SetActive(false);
        }

        public void ResetGame()
        {
            IsOpen = installed = false; ReleasePointer(); simulation?.ResetSimulation();
            if (finishButton != null) finishButton.gameObject.SetActive(false);
            Paint();
            if (gamePanel != null && gamePanel.activeSelf) gamePanel.SetActive(false);
        }

        private void Update() => AdvancePhysics(Time.unscaledDeltaTime);

        /// <summary>The same unscaled path serves player input and deterministic UI checks.</summary>
        public void AdvancePhysics(float unscaledDelta)
        {
            if (!IsOpen || simulation == null || installed) return;
            simulation.Advance(unscaledDelta);
            if (simulation.Solved)
            {
                installed = true; ReleasePointer();
                skipButton.gameObject.SetActive(true); skipButton.transform.SetAsLastSibling();
                finishButton.gameObject.SetActive(true); finishButton.interactable = true;
                finishButton.transform.SetAsLastSibling();
            }
            Paint();
        }

        public Vector2 SimulationToFrame(Vec2 point) => socketOrigin + new Vector2(point.X, -point.Y) * unitsToCanvas;
        public Vec2 FrameToSimulation(Vector2 point) => new Vec2((point.x - socketOrigin.x) / unitsToCanvas, -(point.y - socketOrigin.y) / unitsToCanvas);
        public Vector2 SimulationToScreen(Vec2 point, Camera eventCamera = null) =>
            RectTransformUtility.WorldToScreenPoint(eventCamera, frame.TransformPoint(SimulationToFrame(point)));

        internal void PointerDown(PointerEventData data)
        {
            if (!IsOpen || installed || activePointer.HasValue || data.button != PointerEventData.InputButton.Left) return;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(frame, data.position, data.pressEventCamera, out var local)) return;
            if (!simulation.Grab(FrameToSimulation(local))) return;
            activePointer = data.pointerId; dragCamera = data.pressEventCamera;
            Paint();
        }
        internal void PointerDrag(PointerEventData data)
        {
            if (activePointer != data.pointerId || !IsOpen) return;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(frame, data.position, dragCamera, out var local))
            { ReleasePointer(); return; }
            simulation.UpdateTarget(FrameToSimulation(local));
        }
        internal void PointerUp(PointerEventData data) { if (activePointer == data.pointerId) ReleasePointer(); }
        public void ReleasePointer()
        {
            activePointer = null; dragCamera = null; simulation?.Release();
        }
        private void OnApplicationFocus(bool focused) { if (!focused) ReleasePointer(); }
        private void OnApplicationPause(bool paused) { if (paused) ReleasePointer(); }

        public void SkipInstall()
        {
            if (!IsOpen) return;
            installed = true; ReleasePointer(); CompleteInstall();
        }
        private void CompleteInstall()
        {
            if (!IsOpen || !installed) return;
            IsOpen = false; // Observers see closed state, so duplicate callbacks cannot commit twice.
            ReleasePointer(); finishButton.gameObject.SetActive(false);
            if (gamePanel != null) gamePanel.SetActive(false);
            InstallCompleted?.Invoke();
        }
        private void OnDisable()
        {
            ReleasePointer();
            if (!IsOpen) return;
            IsOpen = installed = false; simulation?.ResetSimulation();
            // Never SetActive here: Unity may be traversing a disabling parent hierarchy.
        }

        private void Paint()
        {
            if (simulation == null) return;
            var pose = simulation.Ram; var p = SimulationToFrame(pose.Position);
            memoryStick.localPosition = new Vector3(p.x, p.y, memoryStick.localPosition.z);
            memoryStick.localRotation = Quaternion.Euler(0, 0, -pose.Theta * Mathf.Rad2Deg);
            if (frontMask != null) frontMask.SetActive(!(installed || simulation.LeftCaptured || simulation.RightCaptured));
            PaintClip(leftClip, leftClipRest, 0, openAngle); PaintClip(rightClip, rightClipRest, 2, -openAngle);
            foreach (var graphic in frame.GetComponentsInChildren<MemorySocketGraphic>(true)) graphic.SetVerticesDirty();
            if (tipText == null) return;
            tipText.text = installed ? "两端已进入并卡入中央凹槽。点击“完成”确认安装。" : simulation.LeftCaptured || simulation.RightCaptured
                ? "这一端已进入插槽。松开后换另一端；不要按中间。"
                : "抓住一端向下压入插槽，松开后阻力会保持；再压另一端。\n按住中间会让插槽底座下陷，无法卡入凹槽。";
            tipText.fontSize = 24; tipText.raycastTarget = false;
            tipText.rectTransform.sizeDelta = new Vector2(900, 80);
            tipText.rectTransform.anchoredPosition = new Vector2(0, 250);
        }
        private void PaintClip(RectTransform clip, Vector2 rest, int end, float angle)
        {
            clip.localPosition = new Vector3(rest.x, rest.y - simulation.NodeDeflection(end) * unitsToCanvas, clip.localPosition.z);
            float openness = 1f - Mathf.Clamp01(simulation.Insertion(end));
            clip.localRotation = Quaternion.Euler(0, 0, angle * openness);
        }
    }
}
