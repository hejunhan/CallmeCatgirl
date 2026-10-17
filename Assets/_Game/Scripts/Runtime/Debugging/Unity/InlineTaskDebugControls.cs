using CallmeCatgirl.Gameplay.TaskGrid;
using UnityEngine;
using UnityEngine.UI;

namespace CallmeCatgirl.Debugging
{
    /// <summary>Place in a dedicated slot beside the current task details. SetTarget on selection changes.</summary>
    public sealed class InlineTaskDebugControls : InlineDebugControls
    {
        private Text current;
        private Button finish;
        private readonly Button[] quality = new Button[4];
        public static InlineTaskDebugControls Attach(RectTransform slot, DebugCommandService service, string requestId, Font font = null)
        {
            if (!DebugBuild.Enabled) return null;
            var view = Child(slot, "TaskDebug").gameObject.AddComponent<InlineTaskDebugControls>(); view.Bind(service, requestId, font); return view;
        }
        protected override void Build()
        {
            current = Label(transform, "[调试] 任务", 38);
            var row = Row();
            quality[0] = Button(row, "恢复自然质量", () => Run(DebugCommand.OverrideQuality(TargetId, null)), 96);
            for (int i = 0; i < 3; i++)
            {
                var next = (Quality)i;
                quality[i + 1] = Button(row, "→" + new[] { "未达标", "完成", "优秀" }[i], () => Run(DebugCommand.OverrideQuality(TargetId, next)), 70);
            }
            finish = Button(Row(), "完成运算 → 待确认", () => Run(DebugCommand.FinishCalculation(TargetId)), 160);
        }
        protected override void Refresh(DebugSnapshot snapshot, bool enabled)
        {
            bool exists = TargetId != null && snapshot.Tasks.TryGetValue(TargetId, out _);
            var task = exists ? snapshot.Tasks[TargetId] : null;
            current.text = "[调试] " + (TargetId ?? "未选择任务") + " · " + DebugCommandService.Describe(snapshot, DebugCommand.FinishCalculation(TargetId))
                + $"\n时钟 {snapshot.GameTime:0.0}s · {(snapshot.Paused ? "暂停（可调试）" : "运行")}";
            finish.interactable = enabled && exists && !task.IsTraining && task.CanCalculate && (snapshot.Capabilities & DebugCapabilities.Calculation) != 0;
            foreach (var button in quality) button.interactable = enabled && exists && task.CanOverride && (snapshot.Capabilities & DebugCapabilities.Quality) != 0;
        }
    }
}
