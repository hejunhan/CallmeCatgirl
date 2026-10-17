using UnityEngine;
using UnityEngine.UI;

namespace CallmeCatgirl.Debugging
{
    public sealed class InlineStageDebugControls : InlineDebugControls
    {
        private Text current;
        private Button next;
        private int displayedStage;
        public static InlineStageDebugControls Attach(RectTransform slot, DebugCommandService service, string stageId = "progression", Font font = null)
        {
            if (!DebugBuild.Enabled) return null;
            var view = Child(slot, "StageDebug").gameObject.AddComponent<InlineStageDebugControls>(); view.Bind(service, stageId, font); return view;
        }
        protected override void Build()
        {
            var row = Row(); current = Label(row, "[调试] 阶段");
            next = Button(row, "下一阶段", () => Run(DebugCommand.AdvanceStage(TargetId, displayedStage)), 110);
        }
        protected override void Refresh(DebugSnapshot snapshot, bool enabled)
        {
            displayedStage = snapshot.Stage;
            if ((snapshot.Capabilities & DebugCapabilities.Stage) == 0)
            {
                current.text = $"[调试] 阶段 API 未接入 · 时钟 {snapshot.GameTime:0.0}s";
                next.GetComponentInChildren<Text>().text = "未接入"; next.interactable = false; return;
            }
            current.text = $"[调试] {TargetId} · 阶段 {snapshot.Stage}/{snapshot.MaxStage} · 时钟 {snapshot.GameTime:0.0}s · {(snapshot.Paused ? "暂停" : "运行")}";
            next.GetComponentInChildren<Text>().text = snapshot.Stage < snapshot.MaxStage ? $"阶段 {snapshot.Stage} → {snapshot.Stage + 1}" : "已到最高阶段";
            next.interactable = enabled && TargetId == snapshot.StageId && snapshot.Stage < snapshot.MaxStage && (snapshot.Capabilities & DebugCapabilities.Stage) != 0;
        }
    }
}
