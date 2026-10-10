using System.Collections.Generic;
using System.Linq;
using CallmeCatgirl.Gameplay;
using CallmeCatgirl.Gameplay.TaskGrid;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CallmeCatgirl.UI
{
    public sealed class CoreGameplayCanvasView : MonoBehaviour
    {
        [Header("Scene layout — edit these objects directly")]
        [SerializeField] private RectTransform board;
        [SerializeField] private RectTransform boardModules;
        [SerializeField] private RectTransform candidateContent;
        [SerializeField] private RectTransform dragLayer;
        [SerializeField] private GridCellView[] gridCells;
        [Header("Replaceable prefabs")]
        [SerializeField] private TaskCardView candidatePrefab;
        [SerializeField] private TaskModuleView modulePrefab;
        [Header("Texts and actions")]
        [SerializeField] private Text clockLabel;
        [SerializeField] private Text messageLabel;
        [SerializeField] private Text logLabel;
        [SerializeField] private Text pauseLabel;
        [SerializeField] private Text speedLabel;
        [SerializeField] private Button[] speedButtons; // 1x, 2x, 4x
        [SerializeField] private Color speedActiveColor = new Color(0.32f, 0.12f, 0.9f, 1f);
        private Color[] speedIdleColors;
        [SerializeField] private GameObject infoPanel;
        [SerializeField] private Text infoTitle;
        [SerializeField] private Text infoStatus;
        [SerializeField] private Text abilitiesLabel;
        [SerializeField] private Text qualityLabel;
        [SerializeField] private Button rotateButton;
        [SerializeField] private Button withdrawButton;
        [SerializeField] private Button submitButton;
        [SerializeField] private Button nextRoundButton;
        [SerializeField] private Text occupancyLabel;
        [SerializeField] private Text modelLabel;
        [SerializeField] private Text conversationLabel;
        private readonly Dictionary<string, TaskCardView> cards = new Dictionary<string, TaskCardView>();
        private readonly Dictionary<string, TaskModuleView> modules = new Dictionary<string, TaskModuleView>();
        private CoreGameplayController controller;
        private string draggedId;
        private int dragRotation;
        private Cell grabOffset;
        private Vector2 lastPointer;
        private Camera pointerCamera;
        private TaskModuleView ghost;
        public bool IsInitialized => controller != null;
        public bool IsDragging => draggedId != null;
        public RectTransform Board => board;

        public void Initialize(CoreGameplayController owner)
        {
            CancelDrag(); controller = owner;
            if (speedIdleColors == null && speedButtons != null)
                speedIdleColors = speedButtons.Select(b => b.targetGraphic.color).ToArray();
            foreach (var module in modules.Values) if (module != null) { module.gameObject.SetActive(false); Destroy(module.gameObject); }
            modules.Clear(); cards.Clear();
            foreach (var card in candidateContent.GetComponentsInChildren<TaskCardView>(true))
            {
                if (controller.Simulation.Tasks.TryGetValue(card.RequestId, out var task) && !cards.ContainsKey(task.Id))
                { cards.Add(task.Id, card); card.Bind(this, task); }
                else { card.gameObject.SetActive(false); Destroy(card.gameObject); }
            }
            Canvas.ForceUpdateCanvases(); Refresh();
        }

        public void Refresh()
        {
            if (controller == null || controller.Simulation == null) return;
            var sim = controller.Simulation;
            if (draggedId != null && (!sim.Tasks.TryGetValue(draggedId, out var dragging)
                || (dragging.State != RequestState.Running && dragging.State != RequestState.Waiting && dragging.State != RequestState.Withdrawn))) CancelDrag();
            clockLabel.text = $"游戏时间 {sim.GameTime:0.0}s   模型周期 {sim.Model.Interval:0.#}s";
            messageLabel.text = controller.Message; logLabel.text = string.Join("\n", controller.Logs);
            pauseLabel.text = sim.Paused ? "继续 [空格]" : "暂停 [空格]";
            speedLabel.text = $"速度 {controller.PlaybackSpeed:0.##}x · {(sim.Paused ? "已暂停" : "运行中")}";
            if (speedButtons != null && speedIdleColors != null)
                for (int i = 0; i < speedButtons.Length; i++)
                    speedButtons[i].targetGraphic.color = Mathf.Approximately(controller.PlaybackSpeed, 1 << i)
                        ? speedActiveColor : speedIdleColors[i];
            float width = board.rect.width / sim.Width, height = board.rect.height / sim.Height;
            int open = 0;
            for (int i = 0; i < gridCells.Length; i++)
            {
                bool available = sim.IsOpen(new Cell(i % sim.Width, i / sim.Width));
                gridCells[i].Show(available); if (available) open++;
            }
            if (occupancyLabel != null) occupancyLabel.text = $"已占用 {sim.Occupancy.Count} / {open} 格";
            if (modelLabel != null) modelLabel.text = $"固定模型 · {sim.Model.Id}\n\n每个请求独立，全速运算\n每 {sim.Model.Interval:0.#} 秒增加能力\n\n"
                + string.Join("\n", sim.Model.Points.Select(p => $"{CoreGameplayController.AbilityText(p.Key)}  +{p.Value:0.#}"));
            foreach (var task in sim.Tasks.Values)
            {
                bool candidate = task.State == RequestState.Waiting || task.State == RequestState.Withdrawn;
                if (candidate && !cards.ContainsKey(task.Id))
                {
                    var card = Instantiate(candidatePrefab, candidateContent); card.name = "RequestCard_" + task.Id;
                    card.Bind(this, task); cards.Add(task.Id, card);
                }
                if (cards.TryGetValue(task.Id, out var existingCard))
                {
                    existingCard.gameObject.SetActive(candidate);
                    if (candidate) existingCard.Refresh(task, sim.GameTime, task.Id == controller.SelectedId);
                }
                bool onBoard = task.State == RequestState.Running || task.State == RequestState.Ready;
                if (onBoard && !modules.ContainsKey(task.Id))
                {
                    var module = Instantiate(modulePrefab, boardModules); module.name = "RunningModule_" + task.Id;
                    module.Bind(this, task.Id); modules.Add(task.Id, module);
                }
                if (modules.TryGetValue(task.Id, out var existingModule))
                {
                    existingModule.gameObject.SetActive(onBoard);
                    if (onBoard)
                    {
                        existingModule.Rect.anchoredPosition = new Vector2(task.Origin.X * width, -task.Origin.Y * height);
                        existingModule.Render(task, task.Rotation, width, height, task.Id == controller.SelectedId);
                    }
                }
            }
            RefreshInfo();
            if (IsDragging) RenderGhost();
        }

        private void RefreshInfo()
        {
            var task = controller.SelectedTask; infoPanel.SetActive(task != null);
            if (conversationLabel != null) conversationLabel.text = task == null ? "选择用户或请求，查看关联通讯。"
                : $"用户 {task.ConversationId}  /  第 {task.RoundId} 轮\n\n{task.Definition.Title}\n\n当前请求 {task.Id}\n{CoreGameplayController.StateText(task.State)}\n\n"
                + (task.Result == null ? "安排请求后开始计算。\n算完后在任务详情中确认提交。" : "本轮结果：" + CoreGameplayController.QualityText(task.Result.Overall));
            if (task == null) return;
            infoTitle.text = "当前请求 / " + task.Id + " · " + task.Definition.Title;
            infoStatus.text = $"{CoreGameplayController.StateText(task.State)}  ·  计算 {task.Progress:0.#}/{task.Definition.Calculation:0.#}  ·  忍耐 {task.Remaining(controller.Simulation.GameTime):0.0}s";
            abilitiesLabel.text = string.Join("\n", task.Definition.Requirements.Select(req =>
                $"{CoreGameplayController.AbilityText(req.Kind)}  {task.GetAbility(req.Kind):0.#} / 完成 {req.Minimum:0.#} / 优秀 {req.Excellent:0.#}"
                + (task.Result == null ? "" : "  " + CoreGameplayController.QualityText(task.Result.ByAbility[req.Kind]))));
            qualityLabel.text = task.Result == null ? "各项能力分别达标，超额不能补其他能力的短板。"
                : "最终质量：" + CoreGameplayController.QualityText(task.Result.Overall) + "；结果已冻结，等待不会再增加能力。";
            rotateButton.interactable = task.State == RequestState.Waiting || task.State == RequestState.Withdrawn || task.State == RequestState.Running;
            withdrawButton.interactable = task.State == RequestState.Running;
            submitButton.interactable = task.State == RequestState.Ready;
            nextRoundButton.interactable = task.State == RequestState.Submitted || task.State == RequestState.TimedOut;
        }

        public void SelectRequest(string id) { controller.SelectRequest(id); RefreshInfo(); }
        public void BeginDrag(string id, PointerEventData data, bool fromBoard)
        {
            CancelDrag();
            if (!controller.Simulation.Tasks.TryGetValue(id, out var task)
                || (task.State != RequestState.Waiting && task.State != RequestState.Withdrawn && task.State != RequestState.Running)) return;
            controller.SelectRequest(id); draggedId = id; dragRotation = task.Rotation;
            lastPointer = data.position; pointerCamera = data.pressEventCamera;
            var cell = ScreenToCell(data.position, pointerCamera);
            grabOffset = fromBoard ? new Cell(cell.X - task.Origin.X, cell.Y - task.Origin.Y) : new Cell(0, 0);
            ghost = Instantiate(modulePrefab, dragLayer); ghost.name = "DragPreview"; ghost.Bind(this, id);
            var group = ghost.GetComponent<CanvasGroup>(); if (group == null) group = ghost.gameObject.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false; group.interactable = false;
            RenderGhost();
        }
        public void UpdateDrag(PointerEventData data)
        { if (!IsDragging) return; lastPointer = data.position; pointerCamera = data.pressEventCamera; RenderGhost(); }
        public void EndDrag(PointerEventData data)
        {
            if (!IsDragging) return;
            lastPointer = data.position; pointerCamera = data.pressEventCamera;
            var cell = ScreenToCell(lastPointer, pointerCamera);
            var origin = new Cell(cell.X - grabOffset.X, cell.Y - grabOffset.Y);
            bool success = controller.Simulation.TryPlace(draggedId, origin, dragRotation, out var failure);
            controller.SetMessage(success ? "合法落位，任务独立运行。" : "落位失败：" + CoreGameplayController.FailureText(failure) + "，原位置与进度保留。");
            CancelDrag(); Refresh();
        }
        public void RotateDrag()
        { if (!IsDragging) return; dragRotation = (dragRotation + 1) % 4; grabOffset = new Cell(0, 0); RenderGhost(); }
        public void CancelDrag()
        {
            draggedId = null;
            if (ghost != null) { ghost.gameObject.SetActive(false); Destroy(ghost.gameObject); }
            ghost = null;
        }
        private Cell ScreenToCell(Vector2 screen, Camera camera)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(board, screen, camera, out var local);
            var sim = controller.Simulation;
            return new Cell(Mathf.FloorToInt((local.x - board.rect.xMin) / (board.rect.width / sim.Width)),
                Mathf.FloorToInt((board.rect.yMax - local.y) / (board.rect.height / sim.Height)));
        }
        private void RenderGhost()
        {
            if (!IsDragging || ghost == null) return;
            var sim = controller.Simulation; var task = sim.Tasks[draggedId];
            var cell = ScreenToCell(lastPointer, pointerCamera); var origin = new Cell(cell.X - grabOffset.X, cell.Y - grabOffset.Y);
            bool legal = sim.Preview(draggedId, origin, dragRotation) == PlacementFailure.None;
            float width = board.rect.width / sim.Width, height = board.rect.height / sim.Height;
            if (RectTransformUtility.RectangleContainsScreenPoint(board, lastPointer, pointerCamera))
                ghost.Rect.position = board.TransformPoint(new Vector3(board.rect.xMin + origin.X * width, board.rect.yMax - origin.Y * height, 0));
            else
            {
                RectTransformUtility.ScreenPointToWorldPointInRectangle(dragLayer, lastPointer, pointerCamera, out var world);
                ghost.Rect.position = world;
            }
            ghost.Render(task, dragRotation, width, height, false, true, legal);
        }
    }
}
