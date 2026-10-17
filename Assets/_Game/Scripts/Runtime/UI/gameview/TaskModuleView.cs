using System.Collections.Generic;
using System.Text;
using CallmeCatgirl.Gameplay.TaskGrid;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CallmeCatgirl.UI
{
    public sealed class TaskModuleView : MonoBehaviour, IPointerClickHandler, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerEnterHandler, IPointerExitHandler
    {
        [SerializeField] private RectTransform shapeRoot;
        [SerializeField] private TaskCellView cellPrefab;
        [SerializeField] private Text titleLabel;
        [SerializeField] private Image progressFill;
        [SerializeField] private Color normalColor = new Color(0.36f, 0.47f, 0.63f);
        [SerializeField] private Color selectedColor = new Color(0.38f, 0.64f, 0.93f);
        [SerializeField] private Color readyColor = new Color(0.28f, 0.68f, 0.45f);
        [SerializeField] private Color legalPreviewColor = new Color(0.25f, 0.9f, 0.45f, 0.65f);
        [SerializeField] private Color illegalPreviewColor = new Color(1, 0.3f, 0.3f, 0.65f);
        private readonly List<TaskCellView> cells = new List<TaskCellView>();
        private CoreGameplayCanvasView owner;
        private string requestId;
        private RequestState lastState;
        private Cell lastOrigin;
        private int lastRotation;
        private bool rendered;
        [SerializeField] private UIFeedbackLabel feedbackLabel;
        private readonly double[] observedAbilities = new double[4];
        private float pulseAt = -100, placedAt = -100, completedAt = -100, rejectedAt = -100;
        private Vector2 shapeHome;
        private Vector3 shapeScale;
        private Color progressIdle;
        private bool homeCaptured;
        public void PlayPlacement() { placedAt = pulseAt = Time.unscaledTime; }
        public void PlayRejected() { rejectedAt = Time.unscaledTime; }
        private string ReadAbilityGains(TaskInstance task)
        {
            var text = new StringBuilder();
            for (int i = 0; i < observedAbilities.Length; i++)
            {
                double value = task.GetAbility((AbilityKind)i), gain = value - observedAbilities[i];
                observedAbilities[i] = value;
                if (!rendered || gain <= 0) continue;
                if (text.Length > 0) text.Append("  ");
                text.Append(CallmeCatgirl.Gameplay.CoreGameplayController.AbilityText((AbilityKind)i)).Append(" +").Append(gain.ToString("0.##"));
            }
            return text.ToString();
        }
        private void OnEnable() => rendered = false;
        public RectTransform Rect => (RectTransform)transform;
        public void Bind(CoreGameplayCanvasView view, string id)
        { owner = view; requestId = id; if (cells.Count == 0) cells.AddRange(shapeRoot.GetComponentsInChildren<TaskCellView>(true)); }
        public void Render(TaskInstance task, int rotation, float width, float height, bool selected, bool preview = false, bool legal = true)
        {
            var config = owner != null ? owner.Feedback : null;
            bool ready = task.State == RequestState.Ready;
            if (!homeCaptured) { shapeHome = shapeRoot.anchoredPosition; shapeScale = shapeRoot.localScale; progressIdle = progressFill.color; homeCaptured = true; }
            string gains = preview ? "" : ReadAbilityGains(task);
            bool completed = !preview && ready && (!rendered || lastState != RequestState.Ready);
            if (!preview && !ready && (!rendered || !lastOrigin.Equals(task.Origin) || lastRotation != rotation)) PlayPlacement();
            if (completed) completedAt = Time.unscaledTime;
            if (!preview && (!rendered || lastState != task.State || !lastOrigin.Equals(task.Origin) || lastRotation != rotation))
                pulseAt = Time.unscaledTime;
            rendered = true; lastState = task.State; lastOrigin = task.Origin; lastRotation = rotation;
            var offsets = task.Definition.RotatedShape(rotation);
            while (cells.Count < offsets.Length) cells.Add(Instantiate(cellPrefab, shapeRoot));
            int columns = 1, rows = 1;
            foreach (var offset in offsets) { columns = Mathf.Max(columns,offset.X+1); rows = Mathf.Max(rows,offset.Y+1); }
            if (feedbackLabel != null)
            {
                feedbackLabel.SetOrigin(new Vector2(columns*width*.5f,((RectTransform)feedbackLabel.transform).rect.height*.5f+4));
                if (config == null || !config.Animate || preview) feedbackLabel.Clear();
                else if (completed) feedbackLabel.Play(CallmeCatgirl.Gameplay.CoreGameplayController.QualityText(task.Result.Overall)+" · 待提交"+(gains.Length > 0 ? "\n"+gains : ""),
                    config.ResultColor(task.Result.Overall),config.CompletionTextSeconds,config.FloatingTextRise,.08f);
                else if (gains.Length > 0 && (selected || config.ShowUnselectedAbilityText))
                    feedbackLabel.Play(gains,config.Signal,config.AbilityTextSeconds,config.FloatingTextRise,.06f);
            }
            if (gains.Length > 0) pulseAt = Time.unscaledTime;
            float scale = 1, shake = 0;
            if (config != null && config.Animate)
            {
                float t = (Time.unscaledTime-placedAt)/Mathf.Max(.05f,config.PlacementSeconds);
                scale += config.PlacementScale*Mathf.Cos(Mathf.Clamp01(t)*Mathf.PI*3)*(1-Mathf.Clamp01(t));
                t = Mathf.Clamp01((Time.unscaledTime-completedAt)/Mathf.Max(.05f,config.PlacementSeconds));
                scale += config.CompletionScale*Mathf.Sin(t*Mathf.PI)*(1-t);
                t = Mathf.Clamp01((Time.unscaledTime-rejectedAt)/Mathf.Max(.05f,config.RejectSeconds));
                shake = Mathf.Sin(t*Mathf.PI*6)*(1-t)*config.RejectShakePixels;
                if (preview) scale = 1+config.DragScale;
            }
            shapeRoot.localScale = shapeScale*scale;
            shapeRoot.anchoredPosition = shapeHome + new Vector2(columns*width*(1-scale)*.5f+shake,-rows*height*(1-scale)*.5f);
            Color color = preview ? (legal ? legalPreviewColor : illegalPreviewColor)
                : task.State == RequestState.Ready ? readyColor : selected ? selectedColor : normalColor;
            if (config != null)
                color = preview ? (legal ? config.LegalPreview : config.IllegalPreview)
                    : ready ? config.ResultColor(task.Result.Overall) : selected ? config.TaskSelected : config.TaskNormal;
            Color border = config == null ? Color.white : preview ? (legal ? config.Signal : config.Error)
                : selected ? config.SelectedBorder : config.BorderFor(task.Id);
            if (config != null && config.Animate && Time.unscaledTime-rejectedAt < config.RejectSeconds) border = config.Error;
            Color data = config == null ? Color.white : ready ? config.ResultColor(task.Result.Overall) : config.Signal;
            float flash = config != null && config.Animate && !preview
                ? Mathf.Clamp01(1 - (Time.unscaledTime - pulseAt) / Mathf.Max(.05f, config.PulseSeconds)) * config.PulseStrength : 0;
            float readyBreath = config != null && config.Animate && ready && !preview
                ? (.5f+.5f*Mathf.Sin(Time.unscaledTime*Mathf.PI*2/Mathf.Max(.1f,config.ReadyPulseSeconds)))*config.ReadyPulseStrength : 0;
            flash = Mathf.Max(flash,readyBreath);
            float phase = config != null ? Mathf.Repeat((float)(owner.VisualGameTime / Mathf.Max(.1f, config.ScanSeconds)),1) : 0;
            for (int i = 0; i < cells.Count; i++)
            {
                cells[i].gameObject.SetActive(i < offsets.Length); if (i >= offsets.Length) continue;
                var rect = (RectTransform)cells[i].transform;
                rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
                rect.anchoredPosition = new Vector2(offsets[i].X * width + 2, -offsets[i].Y * height - 2);
                rect.sizeDelta = new Vector2(width - 4, height - 4); cells[i].Show(color);
                int edges = 0;
                if (System.Array.IndexOf(offsets, offsets[i] + new Cell(0,-1)) < 0) edges |= 1;
                if (System.Array.IndexOf(offsets, offsets[i] + new Cell(1,0)) < 0) edges |= 2;
                if (System.Array.IndexOf(offsets, offsets[i] + new Cell(0,1)) < 0) edges |= 4;
                if (System.Array.IndexOf(offsets, offsets[i] + new Cell(-1,0)) < 0) edges |= 8;
                cells[i].ShowSignal(border, data, edges, config == null ? 1.5f : config.BorderWidth * (1 + readyBreath * 3), phase,
                    config != null && config.Animate && !preview && !ready ? config.ScanOpacity : 0, flash);
            }
            titleLabel.text = preview ? "预览" : task.Id + (task.State == RequestState.Ready ? "\n待提交" : "");
            titleLabel.rectTransform.sizeDelta = new Vector2(width - 4, height - 8);
            progressFill.rectTransform.sizeDelta = new Vector2(Mathf.Max(0, width - 8), 5);
            progressFill.color = Color.Lerp(progressIdle,data,flash);
            progressFill.fillAmount = (float)(task.Progress / task.Definition.Calculation);
            if (progressFill.sprite == null)
                progressFill.rectTransform.sizeDelta = new Vector2(Mathf.Max(0, width - 8) * progressFill.fillAmount, 5);
        }
        public void OnPointerEnter(PointerEventData data)=>owner?.HoverRequest(requestId,this);
        public void OnPointerExit(PointerEventData data)=>owner?.ClearAssociationHover(this);
        public void OnPointerClick(PointerEventData data)
        {
            if (data.button != PointerEventData.InputButton.Left || data.dragging || !data.eligibleForClick) return;
            owner?.SelectRequest(requestId);
            if (data.clickCount >= 2) owner?.OpenRequestConversation(requestId);
        }
        public void OnBeginDrag(PointerEventData data) { if (data.button == PointerEventData.InputButton.Left) owner?.BeginDrag(requestId, data, true); }
        public void OnDrag(PointerEventData data) => owner?.UpdateDrag(data);
        public void OnEndDrag(PointerEventData data) => owner?.EndDrag(data);
    }
}
