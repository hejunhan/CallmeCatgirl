using System;
using System.Collections.Generic;
using CallmeCatgirl.Gameplay.TaskGrid;
using UnityEngine;
//Inspector 中的棋盘、模型和任务数值配置，设定后无法更改
namespace CallmeCatgirl.Gameplay
{
    [CreateAssetMenu(menuName = "CallmeCatgirl/Core Gameplay Configuration")]
    public sealed class GameplayConfiguration : ScriptableObject
    {
        [Min(0.1f)] public float AbilityInterval = 3;
        public Vector4 ModelPoints = new Vector4(4, 1, 1, 0);
        [Range(0.1f, 2)] public float PlaybackSpeed = 0.5f;
        public DemoTask[] Requests;
        [InspectorName("内存配置"), Tooltip("棋盘布局、初始开放格子及升级规则都在这份配置中。")]
        public MemoryUpgradeConfiguration MemoryUpgrades;

        public ModelDefinition BuildModel() => new ModelDefinition("test-model", AbilityInterval,
            new Dictionary<AbilityKind, double> {
                {AbilityKind.Writing, ModelPoints.x}, {AbilityKind.Code, ModelPoints.y},
                {AbilityKind.Reasoning, ModelPoints.z}, {AbilityKind.Retrieval, ModelPoints.w}
            });

        public TaskSimulation BuildSimulation()
        {
            if (MemoryUpgrades == null) throw new InvalidOperationException("请在主配置中指定内存配置。");
            return new TaskSimulation(MemoryUpgrades.BoardWidth, MemoryUpgrades.BoardHeight,
                MemoryUpgrades.GetOpenCells(MemoryUpgrades.InitialLevel), BuildModel());
        }
        [Serializable]
        public sealed class DemoTask
        {
            //代码块配置
            public string Id;
            public string Title;
            public Vector2Int[] Shape;
            [Min(0.1f)] public float Calculation = 100;
            [Min(0.1f)] public float Speed = 10;
            [Min(0.1f)] public float Patience = 60;
            [Min(0)] public float Tokens = 100;
            public Vector3 Minimum = new Vector3(10, 2, 2);
            public Vector3 Excellent = new Vector3(16, 4, 4);

            public TaskDefinition Build()
            {
                var cells = new List<Cell>();
                foreach (var cell in Shape) cells.Add(new Cell(cell.x, cell.y));
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
