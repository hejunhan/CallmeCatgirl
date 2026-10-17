using UnityEngine;
using UnityEngine.UI;

namespace CallmeCatgirl.Debugging
{
    public sealed class InlineTrainingDebugControls : InlineDebugControls
    {
        private Text current;
        private Button finish;
        public static InlineTrainingDebugControls Attach(RectTransform slot, DebugCommandService service, string trainingRequestId, Font font = null)
        {
            if (!DebugBuild.Enabled) return null;
            var view = Child(slot, "TrainingDebug").gameObject.AddComponent<InlineTrainingDebugControls>(); view.Bind(service, trainingRequestId, font); return view;
        }
        protected override void Build()
        {
            current = Label(transform, "[调试] 训练", 38);
            finish = Button(Row(), "完成训练 → 确认产物", () => Run(DebugCommand.FinishTraining(TargetId)), 170);
        }
        protected override void Refresh(DebugSnapshot snapshot, bool enabled)
        {
            bool exists = TargetId != null && snapshot.Tasks.TryGetValue(TargetId, out _);
            var task = exists ? snapshot.Tasks[TargetId] : null;
            current.text = "[调试] " + (TargetId ?? "未选择训练") + " · " + DebugCommandService.Describe(snapshot, DebugCommand.FinishTraining(TargetId))
                + $"\n时钟 {snapshot.GameTime:0.0}s · {(snapshot.Paused ? "暂停（可调试）" : "运行")}";
            finish.interactable = enabled && exists && task.CanFinishTraining && (snapshot.Capabilities & DebugCapabilities.Training) != 0;
        }
    }
}
