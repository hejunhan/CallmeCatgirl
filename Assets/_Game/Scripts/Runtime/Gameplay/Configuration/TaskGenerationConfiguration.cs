using System;
using System.Linq;
using CallmeCatgirl.Gameplay.TaskGrid;
using UnityEngine;

namespace CallmeCatgirl.Gameplay
{
    [CreateAssetMenu(menuName = "CallmeCatgirl/Task Generation Configuration")]
    public sealed class TaskGenerationConfiguration : ScriptableObject
    {
        [Min(0), InspectorName("每次新增请求数"), Tooltip("0关闭自动刷新。已有请求保留，超时按任务自身忍耐时间处理。")]
        public int RequestsPerRefresh = 5;
        [Min(1), InspectorName("每隔多少个月刷新"), Tooltip("月长使用主玩法配置的每月游戏秒数；1表示每个月新增一批。")]
        public int MonthsPerRefresh = 1;
        [InspectorName("随机种子"), Tooltip("负数每次开局随机；非负数便于复现测试。")]
        public int RandomSeed = -1;
        [HideInInspector] public ShapePreset[] Shapes =
        {
            new ShapePreset { Name = "单格", Pattern = new[] { "#" } },
            new ShapePreset { Name = "双格", Pattern = new[] { "##" } },
            new ShapePreset { Name = "三格直线", Pattern = new[] { "###" } },
            new ShapePreset { Name = "三格转角", Pattern = new[] { "#.", "##" } },
            new ShapePreset { Name = "四格正方形", Pattern = new[] { "##", "##" } },
            new ShapePreset { Name = "四格T形", Pattern = new[] { "###", ".#." } },
            new ShapePreset { Name = "四格L形", Pattern = new[] { "#.", "#.", "##" } },
            new ShapePreset { Name = "四格S形", Pattern = new[] { ".##", "##." } },
            new ShapePreset { Name = "四格Z形", Pattern = new[] { "##.", ".##" } },
            new ShapePreset { Name = "五格十字", Pattern = new[] { ".#.", "###", ".#." } }
        };

        public System.Random CreateRandom() => new System.Random(RandomSeed < 0 ? Guid.NewGuid().GetHashCode() : RandomSeed);
        public Cell[] PickShape(System.Random random)
        {
            if (random == null) throw new ArgumentNullException(nameof(random));
            if (Shapes == null || Shapes.Length == 0) throw new InvalidOperationException("任务形状库至少需要一个预设。");
            return Shapes[random.Next(Shapes.Length)].Build();
        }
        public bool ShouldRefresh(int currentMonth) => RequestsPerRefresh > 0 && (currentMonth - 1) % MonthsPerRefresh == 0;
        public void Validate(int boardWidth, int boardHeight)
        {
            if (RequestsPerRefresh < 0 || MonthsPerRefresh < 1 || Shapes == null || Shapes.Length == 0)
                throw new InvalidOperationException("每次请求数不能为负，刷新月份间隔至少为1，形状库不能为空。");
            foreach (var preset in Shapes)
            {
                if (preset == null) throw new InvalidOperationException("任务形状库包含空预设。");
                var cells = preset.Build();
                int width = cells.Max(c => c.X) - cells.Min(c => c.X) + 1;
                int height = cells.Max(c => c.Y) - cells.Min(c => c.Y) + 1;
                if (!(width <= boardWidth && height <= boardHeight) && !(height <= boardWidth && width <= boardHeight))
                    throw new InvalidOperationException("任务形状超过棋盘尺寸：" + preset.Name);
            }
        }
        [Serializable]
        public sealed class ShapePreset
        {
            public string Name = "新形状";
            public string[] Pattern = { "#" };
            public Cell[] Build() => ShapePattern.Parse(Pattern);
        }
    }
}
