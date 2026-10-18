using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using CallmeCatgirl.Gameplay.TaskGrid;

namespace CallmeCatgirl.Gameplay.Training
{
    [CreateAssetMenu(menuName = "CallmeCatgirl/Training/Training Configuration")]
    public sealed class TrainingConfiguration : ScriptableObject
    {
        [Tooltip("以下为可调默认，历史架构研究结果由集成者替换，不表示确定的平衡参数。")]
        public string DefaultsNotice = "暂定：语料一次消耗、无退款取消、能力不继承旧模型。速度为每任务token/s，并发不分摊。";
        public RecipeEntry[] Recipes = { new RecipeEntry() };
        public ArchitectureEntry[] Architectures = {
            new ArchitectureEntry { Id = "baseline", Title = "学习型", UnlockStage = 1, CorpusMultiplier = 1.6, SpeedMultiplier = 1, Outline = OutlineTransform.Keep },
            new ArchitectureEntry { Id = "alternate", Title = "高速型", UnlockStage = 2, CorpusMultiplier = 1, SpeedMultiplier = 1.6, Outline = OutlineTransform.Keep },
            new ArchitectureEntry { Id = "architecture-slot-3", Title = "均衡型", TechnicalDescription = "语料与速度均衡", UnlockStage = 4, ContentReady = true, CorpusMultiplier = 1.25, SpeedMultiplier = 1.25, Outline = OutlineTransform.Keep }
        };
        public ComputeEntry[] Compute = {
            new ComputeEntry { Id = "compute-1", TokensPerSecond = 10, UpgradeCost = 0 },
            new ComputeEntry { Id = "compute-2", TokensPerSecond = 15, UpgradeCost = 50 },
            new ComputeEntry { Id = "compute-3", TokensPerSecond = 22.5, UpgradeCost = 100 }
        };
        public string InitialArchitectureId = "baseline";
        public string InitialModelName = "初始模型";

        [Serializable] public sealed class RecipeEntry
        {
            public string Id = "dialogue-training", Title = "对话语料训练（暂定）";
            public int Cost = 20, MinimumRecords = 1;
            public double WorkTokens = 120, AbilityInterval = 1, PointsPerScore = 1;
            [InspectorName("每增加一格所需语料点"), Min(0.01f)] public double PointsPerExtraCell = 2;
            [InspectorName("单次训练语料点上限"), Min(1)] public double MaximumCorpusPoints = 100;
            [InspectorName("计算量增长系数"), Min(0), Tooltip("计算量 = WorkTokens + 系数 × 总投入点数 ^ 指数；随后应用训练政策倍率。")]
            public double WorkGrowthCoefficient = 5;
            [InspectorName("计算量增长指数"), Min(1)] public double WorkGrowthExponent = 1.5;
            public Vector2Int[] Shape = { new Vector2Int(0, 0), new Vector2Int(1, 0), new Vector2Int(2, 0),
                new Vector2Int(0, 1), new Vector2Int(1, 1), new Vector2Int(2, 1) };
            public TrainingRecipe Build() => new TrainingRecipe(Id, Title, Cost, MinimumRecords, WorkTokens,
                AbilityInterval, PointsPerScore, Shape.Select(c => new Cell(c.x, c.y)), PointsPerExtraCell, MaximumCorpusPoints, WorkGrowthCoefficient, WorkGrowthExponent);
        }
        [Serializable] public sealed class ArchitectureEntry
        {
            public string Id, Title;
            public string TechnicalDescription;
            [InspectorName("运行速度倍率"), Min(.01f)] public double SpeedMultiplier = 1;
            [InspectorName("语料获取倍率"), Min(.01f)] public double CorpusMultiplier = 1;
            public OutlineTransform Outline;
            [Range(1, 5)] public int UnlockStage = 1;
            public bool ContentReady = true;
            public ArchitectureDefinition Build() => new ArchitectureDefinition(Id, Title, SpeedMultiplier, Outline, UnlockStage, ContentReady, TechnicalDescription, CorpusMultiplier, SpeedMultiplier, true);
        }
        [Serializable] public sealed class ComputeEntry
        {
            public string Id;
            public double TokensPerSecond = 10;
            public int UpgradeCost;
            public ComputeTier Build() => new ComputeTier(Id, TokensPerSecond, UpgradeCost);
        }
        public TrainingSession Build(TaskSimulation simulation, EconomyManager wallet,
            Func<ExecutionProfile, bool> apply, Func<double, bool> updateRate, Func<IEnumerable<Cell>> capacity,
            Func<ModelDefinition, ModelDefinition> compose = null, Func<PolicyModifiers> policy = null,
            ModelDefinition initialBaseDefinition = null, Func<int> currentStage = null, ModelIdentityRegistry identities = null)
        {
            if (compose != null && initialBaseDefinition == null)
                throw new InvalidOperationException("Supplying permanent bonuses requires an explicit unmodified initial base model.");
            identities = identities ?? new ModelIdentityRegistry();
            var basis = initialBaseDefinition ?? simulation.Model;
            return new TrainingSession(simulation,
                new CatalogModel(InitialModelName, InitialArchitectureId, basis, Array.Empty<string>(), identities.ResolveOrCreate(basis.Id)),
                Recipes.Select(r => r.Build()), Architectures.Select(a => a.Build()), Compute.Select(c => c.Build()),
                wallet.TrySpendFunds, wallet.AddFunds, apply, updateRate, capacity, compose, policy, currentStage, identities);
        }
    }
}
