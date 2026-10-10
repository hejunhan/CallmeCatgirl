using System;
using System.Collections.Generic;
using System.Linq;
using CallmeCatgirl.Gameplay.TaskGrid;
using CallmeCatgirl.UI;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CallmeCatgirl.Gameplay
{
    /// <summary>Owns the simulation and commands; visual layout is serialized in the Canvas scene.</summary>
    public sealed class CoreGameplayController : MonoBehaviour
    {
        [SerializeField] private GameplayConfiguration configuration;
        [SerializeField] private CoreGameplayCanvasView canvasView;
        public TaskSimulation Simulation { get; private set; }
        public string SelectedId { get; private set; }
        public TaskInstance SelectedTask => SelectedId != null && Simulation.Tasks.TryGetValue(SelectedId, out var task) ? task : null;
        public float PlaybackSpeed { get; private set; }
        public string Message { get; private set; }
        public IReadOnlyList<string> Logs => logs;
        public event Action<TaskEvent> TaskEventRaised;
        private readonly List<string> logs = new List<string>();
        private readonly Dictionary<string, TaskDefinition> definitions = new Dictionary<string, TaskDefinition>();
        private int serial;

        private void Awake() => ResetSimulation();
        private void Start() { if (canvasView != null && Simulation != null) canvasView.Initialize(this); }
        private void Update()
        {
            if (Simulation == null) return;
            Simulation.Advance(Time.unscaledDeltaTime * PlaybackSpeed);
            if (Keyboard.current != null)
            {
                if (Keyboard.current.rKey.wasPressedThisFrame) RotateSelected();
                if (Keyboard.current.spaceKey.wasPressedThisFrame) TogglePause();
                if (Keyboard.current.escapeKey.wasPressedThisFrame && canvasView != null) canvasView.CancelDrag();
            }
            ReadEvents();
            if (canvasView != null) canvasView.Refresh();
        }

        public void ResetSimulation()
        {
            if (configuration == null) { Debug.LogError("CoreGameplayController requires a GameplayConfiguration.", this); enabled = false; return; }
            try
            {
                Simulation = configuration.BuildSimulation(); definitions.Clear(); logs.Clear(); serial = 0;
                foreach (var source in configuration.Requests)
                {
                    var definition = source.Build(); definitions.Add(definition.Id, definition);
                    Simulation.TryCreate(definition.Id + "-1", definition.Id, "1", definition, out _);
                }
                SelectedId = Simulation.Tasks.Keys.FirstOrDefault(); PlaybackSpeed = configuration.PlaybackSpeed;
                Message = "拖动请求到格子。R旋转，空格暂停，Esc取消拖拽；撤回不重置忍耐。";
                ReadEvents();
                if (canvasView != null && canvasView.IsInitialized) canvasView.Initialize(this);
            }
            catch (Exception exception) { Debug.LogException(exception, this); enabled = false; }
        }

        public void SelectRequest(string id) { if (Simulation.Tasks.ContainsKey(id)) SelectedId = id; }
        public void SelectConversation(string id)
        {
            var task = Simulation.Tasks.Values.LastOrDefault(t => t.ConversationId == id);
            if (task != null) SelectedId = task.Id;
        }
        public void CloseInfo() => SelectedId = null;
        public void SetMessage(string value) => Message = value;
        public void TogglePause() => Simulation.Paused = !Simulation.Paused;
        public void CycleSpeed() => PlaybackSpeed = PlaybackSpeed == 0.5f ? 1f : PlaybackSpeed == 1 ? 0.25f : 0.5f;
        public void SetSpeed(float value) => PlaybackSpeed = value;
        public void RotateSelected()
        {
            if (canvasView != null && canvasView.IsDragging) { canvasView.RotateDrag(); return; }
            var task = SelectedTask; if (task == null) return;
            if (task.State == RequestState.Running)
            {
                Simulation.TryPlace(task.Id, task.Origin, task.Rotation + 1, out var failure);
                Message = failure == PlacementFailure.None ? "旋转完成，进度保留。" : "旋转失败：" + FailureText(failure);
            }
            else Simulation.TryRotateCandidate(task.Id);
        }
        public void WithdrawSelected()
        {
            if (SelectedTask == null || !Simulation.TryWithdraw(SelectedId)) return;
            canvasView?.CancelDrag(); Message = "已撤回：进度和能力保留，忍耐继续。";
        }
        public void SubmitSelected()
        {
            if (SelectedTask != null && Simulation.TrySubmit(SelectedId)) Message = "已提交：释放格子并发出一次结算事件。";
        }
        public void AddRequest()
        {
            if (definitions.Count == 0) return;
            string suffix = (++serial).ToString();
            Simulation.TryCreate("new-" + suffix, "new-user-" + suffix, "extra-" + suffix, definitions.Values.First(), out var task);
            SelectedId = task?.Id;
        }
        public void NextRound()
        {
            var previous = SelectedTask;
            if (previous == null || !Simulation.TryCloseFeedback(previous.Id)) return;
            string round = "round-" + (++serial);
            Simulation.TryCreate(previous.ConversationId + "-" + round, previous.ConversationId, round, previous.Definition, out var next);
            SelectedId = next?.Id;
        }
        private void ReadEvents()
        {
            foreach (var item in Simulation.DrainEvents())
            {
                logs.Insert(0, $"{item.GameTime:0.0}s  {item.RequestId}  {EventText(item.Kind)}"
                    + (item.Kind == TaskEventKind.Submitted || item.Kind == TaskEventKind.TimedOut ? $"  token={item.BillableTokens:0.##}" : ""));
                if (logs.Count > 7) logs.RemoveAt(logs.Count - 1);
                TaskEventRaised?.Invoke(item);
            }
        }
        public static string AbilityText(AbilityKind kind) => kind == AbilityKind.Writing ? "写作" : kind == AbilityKind.Code ? "代码" : kind == AbilityKind.Reasoning ? "推理" : "检索";
        public static string QualityText(Quality quality) => quality == Quality.Excellent ? "优秀" : quality == Quality.Complete ? "完成" : "未达标";
        public static string StateText(RequestState state) => new[] { "等待摆放", "运行中", "已撤回", "算完待确认", "已提交待回复", "超时失败", "反馈结束" }[(int)state];
        public static string FailureText(PlacementFailure failure) => new[] { "无", "任务不存在", "当前状态不可摆放", "越界", "未开放格", "与其他任务重叠" }[(int)failure];
        private static string EventText(TaskEventKind kind) => new[] { "请求到达", "合法摆放", "撤回", "算完判质", "提交结算", "超时结算", "反馈结束" }[(int)kind];
    }
}
