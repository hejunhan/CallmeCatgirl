using System;
using System.Collections.Generic;
using CallmeCatgirl.Gameplay.TaskGrid;
using UnityEngine;

namespace CallmeCatgirl.Gameplay
{
    [CreateAssetMenu(menuName = "CallmeCatgirl/Memory Configuration")]
    public sealed class MemoryUpgradeConfiguration : ScriptableObject
    {
        // 棋盘尺寸与格子开放规则统一保存在这里，Inspector 用可视化棋盘编辑。
        [HideInInspector] public int BoardWidth = 8;
        [HideInInspector] public int BoardHeight = 8;
        [Min(1), InspectorName("初始内存等级")] public int InitialLevel = 1;
        [HideInInspector] public int[] CellUnlockLevels = new int[64]; // 0 永久锁定，1 初始，2+ 对应等级解锁。
        [InspectorName("升级价格和条件")]
        public UpgradeStep[] Levels =
        {
            new UpgradeStep { TargetLevel = 2, Cost = 100, RequiredCompletedTasks = 0 },
            new UpgradeStep { TargetLevel = 3, Cost = 200, RequiredCompletedTasks = 3 },
            new UpgradeStep { TargetLevel = 4, Cost = 400, RequiredCompletedTasks = 6 },
            new UpgradeStep { TargetLevel = 5, Cost = 800, RequiredCompletedTasks = 10 }
        };

        public List<Cell> GetOpenCells(int level)
        {
            if (BoardWidth < 1 || BoardHeight < 1 || CellUnlockLevels == null
                || CellUnlockLevels.Length != BoardWidth * BoardHeight)
                throw new InvalidOperationException("内存棋盘尺寸与格子配置不一致。");
            var cells = new List<Cell>();
            for (int i = 0; i < CellUnlockLevels.Length; i++)
                if (CellUnlockLevels[i] > 0 && CellUnlockLevels[i] <= level)
                    cells.Add(new Cell(i % BoardWidth, i / BoardWidth));
            return cells;
        }

        public bool TryGetNextLevel(int currentLevel, out UpgradeStep step)
        {
            if (Levels != null)
                foreach (var level in Levels)
                    if (level != null && level.TargetLevel == currentLevel + 1)
                    { step = level; return true; }
            step = null;
            return false;
        }

        [Serializable]
        public sealed class UpgradeStep
        {
            [Min(2), InspectorName("升级后的等级")] public int TargetLevel = 2;
            [Min(0), InspectorName("升级花费")] public int Cost;
            [Min(0), InspectorName("需要累计完成任务数")] public int RequiredCompletedTasks;
        }
    }
}
