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
        [Min(0), InspectorName("初始内存等级")] public int InitialLevel = 0;
        [InspectorName("初始模型名称"), Tooltip("ModelInfo显示的名称；留空沿用模型原标识。")]
        public string InitialModelName = "";
        [InspectorName("初始等级的新任务倍率")]
        public TaskMultiplierSettings InitialTaskMultipliers = new TaskMultiplierSettings();
        [HideInInspector] public int[] CellUnlockLevels = new int[64]; // 0 永久锁定，1 初始，2+ 对应等级解锁。
        [HideInInspector] public int[] CellCompartments = new int[64]; // 相同编号属于同一背包；0也表示有效分区。

        public int[] GetCompartments()
        {
            int count = BoardWidth * BoardHeight;
            if (BoardWidth < 1 || BoardHeight < 1) throw new InvalidOperationException("内存棋盘尺寸无效。");
            // 旧资源没有分区字段时，全部属于0号背包，保持原有摆放规则。
            if (CellCompartments == null || CellCompartments.Length == 0) return new int[count];
            if (CellCompartments.Length != count || Array.Exists(CellCompartments, id => id < 0))
                throw new InvalidOperationException("背包分区数量应与格子数一致，编号不能为负数。请在内存配置中修复。");
            return (int[])CellCompartments.Clone();
        }

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

        public string GetModelName(int currentLevel, string fallback)
        {
            string name = string.IsNullOrWhiteSpace(InitialModelName) ? fallback : InitialModelName.Trim();
            int namedLevel = InitialLevel;
            if (Levels != null)
                foreach (var step in Levels)
                    if (step != null && step.TargetLevel >= namedLevel && step.TargetLevel <= currentLevel
                        && !string.IsNullOrWhiteSpace(step.ModelName))
                    { name = step.ModelName.Trim(); namedLevel = step.TargetLevel; }
            return name;
        }

        public TaskMultiplierSettings GetTaskMultipliers(int currentLevel)
        {
            var settings = InitialTaskMultipliers;
            int selectedLevel = InitialLevel;
            if (Levels != null)
                foreach (var step in Levels)
                    if (step != null && step.TargetLevel >= selectedLevel && step.TargetLevel <= currentLevel && step.TaskMultipliers != null)
                    { settings = step.TaskMultipliers; selectedLevel = step.TargetLevel; }
            return settings;
        }

        public void ValidateTaskMultipliers()
        {
            if (InitialTaskMultipliers != null && !InitialTaskMultipliers.IsValid)
                throw new InvalidOperationException("初始新任务倍率配置无效：计算量倍率必须为正数，奖励倍率不能为负，上限不能小于下限。");
            if (Levels != null)
                foreach (var step in Levels)
                    if (step?.TaskMultipliers != null && !step.TaskMultipliers.IsValid)
                        throw new InvalidOperationException($"Lv{step.TargetLevel}的新任务倍率范围配置无效。");
        }

        public TaskDefinition ScaleTask(int currentLevel, TaskDefinition basis, System.Random random)
        {
            var settings = GetTaskMultipliers(currentLevel);
            return settings == null ? basis : settings.Scale(basis, random);
        }

        [Serializable]
        public sealed class TaskMultiplierSettings
        {
            [Min(.01f), InspectorName("计算量最小倍率")] public double CalculationMin = 1;
            [Min(.01f), InspectorName("计算量最大倍率")] public double CalculationMax = 1;
            [Min(0), InspectorName("奖励最小倍率")] public double RewardMin = 1;
            [Min(0), InspectorName("奖励最大倍率")] public double RewardMax = 1;
            public bool IsValid => ValidRange(CalculationMin, CalculationMax, true) && ValidRange(RewardMin, RewardMax, false);
            private static bool ValidRange(double min, double max, bool positive)
                => !double.IsNaN(min) && !double.IsNaN(max) && !double.IsInfinity(min) && !double.IsInfinity(max)
                    && (positive ? min > 0 : min >= 0) && max >= min;
            public string Description => IsValid ? $"计算量 ×{CalculationMin:0.##}～{CalculationMax:0.##} · 奖励 ×{RewardMin:0.##}～{RewardMax:0.##}" : "倍率范围配置无效";
            public TaskDefinition Scale(TaskDefinition basis, System.Random random)
            {
                if (basis == null) throw new ArgumentNullException(nameof(basis));
                if (random == null) throw new ArgumentNullException(nameof(random));
                if (!IsValid) throw new InvalidOperationException("新任务倍率范围无效。");
                // 1倍兼容旧资源，也不额外消耗随机序列。一次抽样同时映射难度和奖励。
                if (CalculationMin == 1 && CalculationMax == 1 && RewardMin == 1 && RewardMax == 1) return basis;
                double roll = CalculationMin == CalculationMax && RewardMin == RewardMax ? 0 : random.NextDouble();
                double calculation = basis.Calculation * (CalculationMin + ((double)CalculationMax-CalculationMin)*roll);
                double tokens = basis.Tokens * (RewardMin + ((double)RewardMax-RewardMin)*roll);
                return new TaskDefinition(basis.Id, basis.Title, basis.Shape, calculation, basis.Speed,
                    basis.Patience, tokens, basis.Requirements, basis.UsesCalculationBudget);
            }
        }

        [Serializable]
        public sealed class UpgradeStep
        {
            [Min(2), InspectorName("升级后的等级")] public int TargetLevel = 2;
            [InspectorName("升级后的模型名称"), Tooltip("升级成功后显示；留空沿用之前的名称，不影响能力与模型标识。")]
            public string ModelName = "";
            [InspectorName("升级后新任务倍率"), Tooltip("相对原始任务的总倍率，不与上一级累乘。每个新任务抽样一次，较高计算量对应较高奖励；已有任务不变。上下限相同则固定。")]
            public TaskMultiplierSettings TaskMultipliers = new TaskMultiplierSettings();
            [Min(0), InspectorName("升级花费")] public int Cost;
            [Min(0), InspectorName("需要累计完成任务数")] public int RequiredCompletedTasks;
            // Legacy serialized value only. Retained to preserve configuration hashes; never grants points.
            [SerializeField, HideInInspector] private int AbilityPoints = 1;
        }
    }
}
