using System;
using System.Linq;
using CallmeCatgirl.Gameplay.TaskGrid;
using UnityEngine;

namespace CallmeCatgirl.Gameplay
{
    [CreateAssetMenu(menuName = "CallmeCatgirl/Task Generation Configuration")]
    public sealed class TaskGenerationConfiguration : ScriptableObject
    {
        [Min(0), InspectorName("每次新增请求数"), Tooltip("按日模式为每次到达总数，不受来客数量天赋倍率影响；0关闭自动刷新。刷新日移除上一天未放置/已撤回的普通候选，运行中、待结算、训练及剧情关键请求保留；按月模式仍保留已有请求。")]
        public int RequestsPerRefresh = 1;
        [InspectorName("按天刷新"), Tooltip("开启按游戏日刷新；关闭时兼容原来的按月刷新。")]
        public bool RefreshByDay = true;
        [Min(1), InspectorName("每隔多少天刷新")] public int DaysPerRefresh = 1;
        [Min(1), InspectorName("每隔多少个月刷新"), Tooltip("仅关闭按天刷新时生效；月份时长由主配置的每天秒数与每月天数决定。")]
        public int MonthsPerRefresh = 1;
        [InspectorName("随机种子"), Tooltip("负数每次开局随机；非负数便于复现测试。")]
        public int RandomSeed = -1;
        [Header("经营任务池")]
        [InspectorName("经营任务模板"), Tooltip("按刷新周期补齐请求，主动新增时优先抽取。低能力门槛、大计算量、低奖励；空列表兼容原随机任务。形状共用下方形状库。")]
        public GameplayConfiguration.DemoTask[] OperatingTasks = Array.Empty<GameplayConfiguration.DemoTask>();
        public bool HasOperatingTasks => OperatingTasks != null && OperatingTasks.Length > 0;
        public TaskDefinition PickOperatingTask(System.Random random)
        {
            if (random == null) throw new ArgumentNullException(nameof(random));
            if (!HasOperatingTasks) return null;
            var source = OperatingTasks[random.Next(OperatingTasks.Length)].Build();
            return new TaskDefinition(source.Id, "经营 · " + source.Title, PickShape(random), source.Calculation,
                source.Speed, source.Patience, source.Tokens, source.Requirements, usesCalculationBudget: true);
        }
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
        public bool ShouldRefreshDay(long currentDay) => RequestsPerRefresh > 0 && currentDay > 1 && (currentDay - 1) % DaysPerRefresh == 0;
        public void Validate(int boardWidth, int boardHeight)
        {
            if (RequestsPerRefresh < 0 || MonthsPerRefresh < 1 || DaysPerRefresh < 1 || Shapes == null || Shapes.Length == 0)
                throw new InvalidOperationException("每次请求数不能为负，刷新天数/月数间隔至少为1，形状库不能为空。");
            var ids = new System.Collections.Generic.HashSet<string>();
            foreach (var task in OperatingTasks ?? Array.Empty<GameplayConfiguration.DemoTask>())
            {
                if (task == null || string.IsNullOrWhiteSpace(task.Id) || !ids.Add(task.Id) || string.IsNullOrWhiteSpace(task.Title))
                    throw new InvalidOperationException("经营任务模板ID/标题为空，或ID重复。");
                task.Build(); // 复用任务数值与能力门槛校验。
                if (new[] { task.Minimum.x, task.Minimum.y, task.Minimum.z }.Any(n => float.IsNaN(n) || float.IsInfinity(n) || n < 0))
                    throw new InvalidOperationException("经营任务能力门槛必须是非负有限数值。");
            }
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
