using System;
using UnityEngine;

namespace CallmeCatgirl.Gameplay
{
    [CreateAssetMenu(menuName = "CallmeCatgirl/Memory Upgrade Configuration")]
    public sealed class MemoryUpgradeConfiguration : ScriptableObject
    {
        [InspectorName("升级档位")]
        public UpgradeStep[] Levels =
        {
            new UpgradeStep { TargetLevel = 2, Cost = 100, AddedCells = 4, RequiredCompletedTasks = 0 },
            new UpgradeStep { TargetLevel = 3, Cost = 200, AddedCells = 4, RequiredCompletedTasks = 3 },
            new UpgradeStep { TargetLevel = 4, Cost = 400, AddedCells = 6, RequiredCompletedTasks = 6 },
            new UpgradeStep { TargetLevel = 5, Cost = 800, AddedCells = 10, RequiredCompletedTasks = 10 }
        };

        // 当前等级属于玩家进度；这里只查下一档配置，不扣费或开放格子。
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
            [Min(1), InspectorName("本次新增格子数")]
            [Tooltip("本次增加的开放格子数量，不是升级后的总格子数。")]
            public int AddedCells = 4;
            [Min(0), InspectorName("需要累计完成任务数")]
            [Tooltip("填 0 表示没有任务数量门槛。")]
            public int RequiredCompletedTasks;
        }
    }
}