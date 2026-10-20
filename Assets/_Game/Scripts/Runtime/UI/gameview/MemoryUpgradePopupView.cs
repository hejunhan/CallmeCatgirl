using System.Collections.Generic;
using CallmeCatgirl.Gameplay;
using CallmeCatgirl.Gameplay.TaskGrid;
using UnityEngine;
using UnityEngine.UI;

namespace CallmeCatgirl.UI
{
    public sealed class MemoryUpgradePopupView : MonoBehaviour
    {
        [SerializeField] private GameObject panel;
        [SerializeField] private Text summaryLabel;
        [SerializeField] private Text conditionsLabel;
        [SerializeField] private Text previewLabel;
        [SerializeField] private Text feedbackLabel;
        [SerializeField] private Button confirmButton;
        [SerializeField] private Text confirmLabel;
        [SerializeField] private Image[] previewCells;
        [SerializeField] private MemoryPartitionView partitions;
        private CoreGameplayController bound;
        private int shownLevel, shownFunds;
        private float upgradedAt = -100;
        private Color summaryIdle;
        private bool colorCaptured;
        [SerializeField] private Color openColor = new Color(.22f, .3f, .4f);
        [SerializeField] private Color newColor = new Color(.44f, .26f, .85f);
        [SerializeField] private Color lockedColor = new Color(.07f, .085f, .12f);
        public void Refresh(CoreGameplayController owner, UIFeedbackConfiguration feedback = null)
        {
            panel.SetActive(owner.IsMemoryUpgradeOpen && !owner.GameEnded);
            if (!panel.activeSelf) return;
            if (!colorCaptured) { summaryIdle = summaryLabel.color; colorCaptured = true; }
            bool levelChanged = bound == owner && shownLevel > 0 && owner.MemoryLevel > shownLevel;
            bool fundsChanged = bound == owner && shownLevel > 0 && owner.Funds != shownFunds;
            if (levelChanged || fundsChanged) upgradedAt = Time.unscaledTime;
            shownLevel = owner.MemoryLevel; shownFunds = owner.Funds;
            float flash = feedback != null && feedback.Animate
                ? Mathf.Clamp01(1 - (Time.unscaledTime - upgradedAt) / Mathf.Max(.05f,feedback.PulseSeconds)) : 0;
            summaryLabel.color = Color.Lerp(summaryIdle,feedback == null ? summaryIdle : feedback.Signal,flash);
            var sim = owner.Simulation; var next = owner.NextMemoryUpgrade;
            summaryLabel.text = $"内存 Lv{owner.MemoryLevel} · 已开放 {sim.OpenCellCount} / {sim.Width * sim.Height} 格\n资金 {owner.Funds} · 累计达标提交 {owner.CompletedTasks} 个";
            confirmButton.interactable = owner.CanUpgradeMemory;
            confirmLabel.text = next == null ? "已满级" : $"确认升级 · {next.Cost}";
            conditionsLabel.text = next == null ? "全部升级已完成。" :
                $"下一等级 Lv{next.TargetLevel} · 新增 {owner.NextMemoryCellCount} 格\n"
                + $"资金：{owner.Funds} / {next.Cost}（{(owner.DebugIgnoreCosts ? "调试忽略" : owner.Funds >= next.Cost ? "满足" : "不足")}）\n"
                + $"达标提交：{owner.CompletedTasks} / {next.RequiredCompletedTasks}（{(owner.CompletedTasks >= next.RequiredCompletedTasks ? "满足" : "不足")}）\n"
                + "新请求：" + owner.NextTaskMultiplierText;
            bound = owner;
            feedbackLabel.text = owner.Message;
            previewLabel.text = (next == null ? "当前布局 · 内存已满级" : $"升级到 Lv{next.TargetLevel} 后的布局")
                + "\n紫色：本次新增 · 蓝灰：已开放 · 深色：未开放\n粗线为隔板，任务不能跨背包";
            var cells = new HashSet<Cell>(owner.NextMemoryCells);
            if (partitions != null) partitions.Show(sim, cells);
            for (int i = 0; i < previewCells.Length; i++)
            {
                var cell = new Cell(i % sim.Width, i / sim.Width);
                previewCells[i].color = sim.IsOpen(cell) ? openColor : cells.Contains(cell) ? (feedback != null ? feedback.UpgradePreview : newColor) : lockedColor;
            }
        }
    }
}
