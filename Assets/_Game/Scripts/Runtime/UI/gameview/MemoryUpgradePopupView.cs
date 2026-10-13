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
        [SerializeField] private Color openColor = new Color(.22f, .3f, .4f);
        [SerializeField] private Color newColor = new Color(.44f, .26f, .85f);
        [SerializeField] private Color lockedColor = new Color(.07f, .085f, .12f);
        public void Refresh(CoreGameplayController owner)
        {
            panel.SetActive(owner.IsMemoryUpgradeOpen && !owner.GameEnded);
            if (!panel.activeSelf) return;
            var sim = owner.Simulation; var next = owner.NextMemoryUpgrade;
            summaryLabel.text = $"内存 Lv{owner.MemoryLevel} · 已开放 {sim.OpenCellCount} / {sim.Width * sim.Height} 格\n资金 {owner.Funds} · 累计达标提交 {owner.CompletedTasks} 个";
            confirmButton.interactable = owner.CanUpgradeMemory;
            confirmLabel.text = next == null ? "已满级" : $"确认升级 · {next.Cost}";
            conditionsLabel.text = next == null ? "全部升级已完成。" :
                $"下一等级 Lv{next.TargetLevel} · 新增 {owner.NextMemoryCellCount} 格\n"
                + $"资金：{owner.Funds} / {next.Cost}（{(owner.Funds >= next.Cost ? "满足" : "不足")}）\n"
                + $"达标提交：{owner.CompletedTasks} / {next.RequiredCompletedTasks}（{(owner.CompletedTasks >= next.RequiredCompletedTasks ? "满足" : "不足")}）";
            feedbackLabel.text = owner.Message;
            previewLabel.text = next == null ? "当前布局 · 内存已满级" : $"升级到 Lv{next.TargetLevel} 后的布局\n紫色：本次新增 · 蓝灰：已开放 · 深色：未开放";
            var cells = new HashSet<Cell>(owner.NextMemoryCells);
            for (int i = 0; i < previewCells.Length; i++)
            {
                var cell = new Cell(i % sim.Width, i / sim.Width);
                previewCells[i].color = sim.IsOpen(cell) ? openColor : cells.Contains(cell) ? newColor : lockedColor;
            }
        }
    }
}
