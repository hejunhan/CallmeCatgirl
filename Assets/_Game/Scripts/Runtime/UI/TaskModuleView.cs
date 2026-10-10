using System.Collections.Generic;
using CallmeCatgirl.Gameplay.TaskGrid;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CallmeCatgirl.UI
{
    public sealed class TaskModuleView : MonoBehaviour, IPointerClickHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
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
        public RectTransform Rect => (RectTransform)transform;
        public void Bind(CoreGameplayCanvasView view, string id)
        { owner = view; requestId = id; if (cells.Count == 0) cells.AddRange(shapeRoot.GetComponentsInChildren<TaskCellView>(true)); }
        public void Render(TaskInstance task, int rotation, float width, float height, bool selected, bool preview = false, bool legal = true)
        {
            var offsets = task.Definition.RotatedShape(rotation);
            while (cells.Count < offsets.Length) cells.Add(Instantiate(cellPrefab, shapeRoot));
            Color color = preview ? (legal ? legalPreviewColor : illegalPreviewColor)
                : task.State == RequestState.Ready ? readyColor : selected ? selectedColor : normalColor;
            for (int i = 0; i < cells.Count; i++)
            {
                cells[i].gameObject.SetActive(i < offsets.Length); if (i >= offsets.Length) continue;
                var rect = (RectTransform)cells[i].transform;
                rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
                rect.anchoredPosition = new Vector2(offsets[i].X * width + 2, -offsets[i].Y * height - 2);
                rect.sizeDelta = new Vector2(width - 4, height - 4); cells[i].Show(color);
            }
            titleLabel.text = preview ? "预览" : task.Id + (task.State == RequestState.Ready ? "\n待提交" : "");
            titleLabel.rectTransform.sizeDelta = new Vector2(width - 4, height - 8);
            progressFill.rectTransform.sizeDelta = new Vector2(Mathf.Max(0, width - 8), 5);
            progressFill.fillAmount = (float)(task.Progress / task.Definition.Calculation);
            if (progressFill.sprite == null)
                progressFill.rectTransform.sizeDelta = new Vector2(Mathf.Max(0, width - 8) * progressFill.fillAmount, 5);
        }
        public void OnPointerClick(PointerEventData data) => owner?.SelectRequest(requestId);
        public void OnBeginDrag(PointerEventData data) { if (data.button == PointerEventData.InputButton.Left) owner?.BeginDrag(requestId, data, true); }
        public void OnDrag(PointerEventData data) => owner?.UpdateDrag(data);
        public void OnEndDrag(PointerEventData data) => owner?.EndDrag(data);
    }
}
