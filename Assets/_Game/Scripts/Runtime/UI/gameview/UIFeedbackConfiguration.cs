using UnityEngine;
using CallmeCatgirl.Gameplay.TaskGrid;

namespace CallmeCatgirl.UI
{
    [CreateAssetMenu(menuName = "CallmeCatgirl/UI Feedback", fileName = "UIFeedbackConfiguration")]
    public sealed class UIFeedbackConfiguration : ScriptableObject
    {
        [Header("任务填色 / 高亮")]
        public Color TaskNormal = new Color(.36f, .47f, .63f);
        public Color TaskSelected = new Color(.31f, .12f, .9f);
        public Color CardSelected = new Color(.55f, .4f, .95f, .16f);
        public Color SpeedSelected = new Color(.32f, .12f, .9f);
        public Color PauseSelected = new Color(.32f, .12f, .9f);
        public Color LegalPreview = new Color(.25f, .9f, .45f, .65f);
        public Color IllegalPreview = new Color(1, .3f, .3f, .65f);
        [Header("结果 / 操作反馈")]
        public Color BelowStandard = new Color(.85f, .48f, .36f);
        public Color Complete = new Color(.28f, .68f, .45f);
        public Color Excellent = new Color(.73f, .63f, .34f);
        public Color Error = new Color(.95f, .3f, .3f);
        public Color UpgradePreview = new Color(.44f,.26f,.85f);
        public Color Signal = new Color(.55f, .85f, .9f);
        [Header("任务外缘描边 — 按任务ID稳定分配")]
        public Color[] TaskBorders = {
            new Color(.56f,.78f,.82f), new Color(.79f,.65f,.86f),
            new Color(.83f,.74f,.5f), new Color(.58f,.79f,.65f),
            new Color(.83f,.62f,.57f), new Color(.57f,.67f,.9f) };
        public Color SelectedBorder = new Color(.86f,.78f,1f);
        [Range(.5f, 6)] public float BorderWidth = 1.5f;
        [Header("克制的系统动效 — 关闭后仍保留描边和状态颜色")]
        public bool Animate = true;
        [Range(0, 1)] public float ScanOpacity = .12f;
        [Min(.1f), Tooltip("扫描一次所需游戏秒数，暂停时冻结，随倍速变化。")] public float ScanSeconds = 3f;
        [Min(.05f), Tooltip("操作脉冲的真实秒数，暂停时操作仍有反馈。")] public float PulseSeconds = .55f;
        [Range(0, 1)] public float PulseStrength = .65f;
        [Header("落位 / 拖动 / 非法落位")]
        [Range(0,.15f)] public float DragScale = .04f;
        [Range(0,.15f)] public float PlacementScale = .07f;
        [Min(.05f)] public float PlacementSeconds = .28f;
        [Range(0,10)] public float RejectShakePixels = 4;
        [Min(.05f)] public float RejectSeconds = .2f;
        [Header("能力增长 / 完成 / 结算")]
        [Min(.1f)] public float AbilityTextSeconds = .9f;
        public bool ShowUnselectedAbilityText = false;
        [Min(.1f)] public float CompletionTextSeconds = 1.1f;
        [Range(0,.15f)] public float CompletionScale = .05f;
        [Range(0,.35f)] public float ReadyPulseStrength = .16f;
        [Min(.1f)] public float ReadyPulseSeconds = 1.6f;
        [Min(.1f)] public float SettlementSeconds = 1.4f;
        [Min(0)] public float FloatingTextRise = 22;
        public Color ResultColor(Quality quality) => quality == Quality.Excellent ? Excellent
            : quality == Quality.Complete ? Complete : BelowStandard;
        public Color BorderFor(string id)
        {
            if (TaskBorders == null || TaskBorders.Length == 0) return Signal;
            uint hash = 2166136261;
            foreach (char c in id ?? "") hash = unchecked((hash ^ c) * 16777619);
            return TaskBorders[hash % (uint)TaskBorders.Length];
        }
    }
}
