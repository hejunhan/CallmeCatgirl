using System;
using System.Collections.Generic;
using CallmeCatgirl.Gameplay.TaskGrid;
using UnityEngine;
//主玩法数值；修改配置后重开游戏生效。
namespace CallmeCatgirl.Gameplay
{
    [CreateAssetMenu(menuName = "CallmeCatgirl/Core Gameplay Configuration")]
    public sealed class GameplayConfiguration : ScriptableObject
    {
        [Min(0.1f)] public float AbilityInterval = 3;
        public Vector4 ModelPoints = new Vector4(4, 1, 1, 0);
        [Range(0.1f, 2)] public float PlaybackSpeed = 0.5f;
        public DemoTask[] Requests;
        [Min(0), InspectorName("初始资金"), Tooltip("新开局或重开时读取，可在主配置资源中调整。")] public int InitialFunds = 1500;
        [InspectorName("内存配置"), Tooltip("棋盘布局、初始开放格子及升级规则都在这份配置中。")]
        public MemoryUpgradeConfiguration MemoryUpgrades;
        [InspectorName("原有会话配置（由角色类型区分普通与剧情）")] public StoryConfiguration Story;
        [InspectorName("普通随机对话池（替换普通试玩内容，剧情不受影响）")] public StoryConfiguration OrdinaryDialoguePool;
        [Min(0), InspectorName("每Token收入单价")] public int TokenPrice = 1;
        [Min(0), InspectorName("每月维护费")] public int MonthlyMaintenanceFee = 1500;
        [Min(0.1f), InspectorName("每天游戏秒数"), Tooltip("游戏时间，受暂停和倍速影响。120秒为一天。")]
        public float SecondsPerGameDay = 120;
        [Min(1), InspectorName("每月天数")] public int DaysPerGameMonth = 30;
        public float SecondsPerGameMonth => SecondsPerGameDay * DaysPerGameMonth;
        [InspectorName("任务生成与方块配置")] public TaskGenerationConfiguration TaskGeneration;
        [Header("角色亲密度（重开生效，允许负值）")]
        [InspectorName("初始亲密度")] public float InitialAffinity = 0;
        [InspectorName("达标提交亲密度变化")] public float SuccessAffinity = 1;
        [InspectorName("失败亲密度变化")] public float FailureAffinity = -0.5f;
        [InspectorName("开局难度曲线（独立覆盖）")] public OpeningBalanceConfiguration OpeningBalance;
        public EconomyManager BuildEconomyManager() => new EconomyManager(InitialFunds, TokenPrice);

        public ModelDefinition BuildModel() => new ModelDefinition("test-model", AbilityInterval,
            new Dictionary<AbilityKind, double> {
                {AbilityKind.Writing, ModelPoints.x}, {AbilityKind.Code, ModelPoints.y},
                {AbilityKind.Reasoning, ModelPoints.z}, {AbilityKind.Retrieval, ModelPoints.w}
            });

        public TaskSimulation BuildSimulation()
        {
            if (MemoryUpgrades == null) throw new InvalidOperationException("请在主配置中指定内存配置。");
            return new TaskSimulation(MemoryUpgrades.BoardWidth, MemoryUpgrades.BoardHeight,
                MemoryUpgrades.GetOpenCells(MemoryUpgrades.InitialLevel), BuildModel(), MemoryUpgrades.GetCompartments());
        }
        [Serializable]
        public sealed class DemoTask
        {
            //代码块配置
            public string Id;
            public string Title;
            [HideInInspector] public Vector2Int[] Shape; // 旧模板兼容；新任务形状优先来自剧情或独立形状库。
            [Min(0.1f)] public float Calculation = 100;
            [Min(0.1f)] public float Speed = 10;
            [Min(0.1f)] public float Patience = 60;
            [Min(0)] public float Tokens = 100;
            public Vector3 Minimum = new Vector3(10, 2, 2);
            public Vector3 Excellent = new Vector3(16, 4, 4);

            public TaskDefinition Build()
            {
                var cells = new List<Cell>();
                foreach (var cell in Shape == null || Shape.Length == 0 ? new[] { Vector2Int.zero } : Shape) cells.Add(new Cell(cell.x, cell.y));
                var req = new List<AbilityRequirement>();
                // A zero minimum means this ability is not required by the inspector configuration.
                if (Minimum.x > 0) req.Add(new AbilityRequirement(AbilityKind.Writing, Minimum.x, Excellent.x));
                if (Minimum.y > 0) req.Add(new AbilityRequirement(AbilityKind.Code, Minimum.y, Excellent.y));
                if (Minimum.z > 0) req.Add(new AbilityRequirement(AbilityKind.Reasoning, Minimum.z, Excellent.z));
                return new TaskDefinition(Id, Title, cells, Calculation, Speed, Patience, Tokens, req);
            }
        }
    }
}
