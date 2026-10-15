using System.Collections.Generic;
using CallmeCatgirl.Gameplay;
using CallmeCatgirl.Gameplay.TaskGrid;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CallmeCatgirl.UI
{
    public sealed class TaskCardView : MonoBehaviour, IPointerClickHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        [SerializeField] private string requestId;
        [SerializeField] private Text titleLabel;
        [SerializeField] private Text statusLabel;
        [SerializeField] private Text progressLabel;
        [SerializeField] private RectTransform shapeRoot;
        [SerializeField] private TaskCellView cellPrefab;
        [SerializeField] private Image selectionBorder;
        [SerializeField] private Image avatar;
        [SerializeField] private Color shapeColor = new Color(0.7f, 0.8f, 0.95f);
        private readonly List<TaskCellView> shapeCells = new List<TaskCellView>();
        private CoreGameplayCanvasView owner;
        private float arrivedAt;
        private float rejectedAt = -100;
        private Vector2 shapeHome;
        private bool homeCaptured;
        public void PlayRejected() => rejectedAt = Time.unscaledTime;
        private int rotation = -1;
        private TaskDefinition lastDefinition;
        public string RequestId => requestId;
        public void Bind(CoreGameplayCanvasView view, TaskInstance task)
        {
            if (!homeCaptured) { shapeHome = shapeRoot.anchoredPosition; homeCaptured = true; }
            owner = view; requestId = task.Id; arrivedAt = Time.unscaledTime;
            if (avatar != null) { avatar.sprite = view.GetAvatar(task.ConversationId); avatar.enabled = avatar.sprite != null; }
            if (shapeCells.Count == 0) shapeCells.AddRange(shapeRoot.GetComponentsInChildren<TaskCellView>(true));
        }
        public void Refresh(TaskInstance task, double now, bool selected)
        {
            titleLabel.text = task.Id + " · " + task.Definition.Title;
            statusLabel.text = CoreGameplayController.StateText(task.State) + $"  忍耐 {task.Remaining(now):0.0}s";
            progressLabel.text = $"计算 {task.Progress:0.#}/{task.Definition.Calculation:0.#}  {task.Definition.Shape.Count}格\n达标奖 {task.Definition.Tokens:0.#} Token";
            progressLabel.resizeTextForBestFit = true; progressLabel.resizeTextMinSize = 10; progressLabel.resizeTextMaxSize = progressLabel.fontSize;
            var feedback = owner != null ? owner.Feedback : null;
            float shake = 0;
            if (feedback != null && feedback.Animate)
            {
                float t = Mathf.Clamp01((Time.unscaledTime-rejectedAt)/Mathf.Max(.05f,feedback.RejectSeconds));
                shake = Mathf.Sin(t*Mathf.PI*6)*(1-t)*feedback.RejectShakePixels;
            }
            shapeRoot.anchoredPosition = shapeHome + Vector2.right*shake;
            if (selectionBorder != null)
            {
                float pulse = feedback != null && feedback.Animate
                    ? Mathf.Clamp01(1 - (Time.unscaledTime - arrivedAt) / Mathf.Max(.05f,feedback.PulseSeconds)) : 0;
                selectionBorder.enabled = selected || pulse > 0;
                if (feedback != null)
                {
                    Color border = selected ? feedback.CardSelected : feedback.Signal;
                    if (!selected) border.a *= pulse * feedback.PulseStrength * .5f;
                    selectionBorder.color = border;
                }
            }
            if (rotation == task.Rotation && lastDefinition == task.Definition) return;
            rotation = task.Rotation; lastDefinition = task.Definition;
            var offsets = task.Definition.RotatedShape(rotation);
            while (shapeCells.Count < offsets.Length) shapeCells.Add(Instantiate(cellPrefab, shapeRoot));
            for (int i = 0; i < shapeCells.Count; i++)
            {
                shapeCells[i].gameObject.SetActive(i < offsets.Length); if (i >= offsets.Length) continue;
                var rect = (RectTransform)shapeCells[i].transform;
                rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
                rect.anchoredPosition = new Vector2(offsets[i].X * 13, -offsets[i].Y * 13); rect.sizeDelta = new Vector2(11, 11);
                shapeCells[i].Show(shapeColor);
            }
        }
        public void OnPointerClick(PointerEventData data) { if (owner != null) owner.SelectRequest(requestId); }
        public void OnBeginDrag(PointerEventData data) { if (data.button == PointerEventData.InputButton.Left) owner?.BeginDrag(requestId, data, false); }
        public void OnDrag(PointerEventData data) => owner?.UpdateDrag(data);
        public void OnEndDrag(PointerEventData data) => owner?.EndDrag(data);
    }
}
