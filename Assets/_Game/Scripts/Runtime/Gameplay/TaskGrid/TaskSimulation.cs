using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
//创建、摆放、旋转、撤回、计算、能力增长、判质、提交和超时
namespace CallmeCatgirl.Gameplay.TaskGrid
{
    /// <summary>Single owner of task state, time, occupancy and terminal events. No Unity dependency.</summary>
    public sealed class TaskSimulation
    {//整个游玩区域
        private const double Epsilon = 1e-9;
        private readonly HashSet<Cell> openCells;
        private readonly Dictionary<Cell, string> occupancy = new Dictionary<Cell, string>();
        private readonly Dictionary<string, TaskInstance> tasks = new Dictionary<string, TaskInstance>();
        private readonly Dictionary<string, string> activeConversations = new Dictionary<string, string>();
        private readonly Dictionary<Tuple<string, string>, string> roundRequests = new Dictionary<Tuple<string, string>, string>();
        private readonly List<TaskEvent> pendingEvents = new List<TaskEvent>();
        public int Width { get; }
        public int Height { get; }
        public double GameTime { get; private set; }
        public bool Paused { get; set; }
        public ModelDefinition Model { get; private set; }
        public IReadOnlyDictionary<string, TaskInstance> Tasks { get; }
        public IReadOnlyDictionary<Cell, string> Occupancy { get; }

        public TaskSimulation(int width, int height, IEnumerable<Cell> availableCells, ModelDefinition model)
        {
            if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
            Width = width; 
            Height = height; 
            Model = model ?? throw new ArgumentNullException(nameof(model));
            openCells = new HashSet<Cell>(availableCells ?? throw new ArgumentNullException(nameof(availableCells)));
            if (openCells.Count == 0 || openCells.Any(c => !InBounds(c)))
                throw new ArgumentException("Open cells must be inside the board.");
            Tasks = new ReadOnlyDictionary<string, TaskInstance>(tasks);
            Occupancy = new ReadOnlyDictionary<Cell, string>(occupancy);
        }

        public bool IsOpen(Cell cell) => openCells.Contains(cell);
        public string TaskAt(Cell cell) => occupancy.TryGetValue(cell, out var id) ? id : null;
        private bool InBounds(Cell c) => c.X >= 0 && c.Y >= 0 && c.X < Width && c.Y < Height;

        public bool TryCreate(string requestId, string conversationId, string roundId, TaskDefinition definition,
            out TaskInstance task)
        {
            task = null;
            if (string.IsNullOrWhiteSpace(requestId) || string.IsNullOrWhiteSpace(conversationId)
                || string.IsNullOrWhiteSpace(roundId) || definition == null) return false;
            var key = Tuple.Create(conversationId, roundId);
            if (tasks.ContainsKey(requestId) || roundRequests.ContainsKey(key)
                || activeConversations.ContainsKey(conversationId)) return false;
            task = new TaskInstance(requestId, conversationId, roundId, definition, GameTime);
            tasks.Add(requestId, task); roundRequests.Add(key, requestId);
            activeConversations.Add(conversationId, requestId);
            Emit(TaskEventKind.Arrived, task, GameTime);
            return true;
        }

        public PlacementFailure Preview(string requestId, Cell origin, int rotation)
        {
            if (!tasks.TryGetValue(requestId, out var task)) return PlacementFailure.UnknownTask;
            if (task.State != RequestState.Waiting && task.State != RequestState.Withdrawn
                && task.State != RequestState.Running) return PlacementFailure.InvalidState;
            foreach (var offset in task.Definition.RotatedShape(rotation))
            {
                Cell cell = origin + offset;
                if (!InBounds(cell)) return PlacementFailure.OutOfBounds;
                if (!IsOpen(cell)) return PlacementFailure.LockedCell;
                if (occupancy.TryGetValue(cell, out var occupant) && occupant != requestId)
                    return PlacementFailure.Overlap;
            }
            return PlacementFailure.None;
        }

        public bool TryPlace(string requestId, Cell origin, int rotation, out PlacementFailure failure)
        {//尝试放置
            failure = Preview(requestId, origin, rotation);
            if (failure != PlacementFailure.None) return false;
            var task = tasks[requestId];
            // Validate everything before releasing old cells: invalid moves are atomic no-ops.
            Release(task);
            task.Origin = origin; task.Rotation = ((rotation % 4) + 4) % 4;
            foreach (var offset in task.Definition.RotatedShape(task.Rotation)) occupancy.Add(origin + offset, task.Id);
            task.State = RequestState.Running;
            Emit(TaskEventKind.Placed, task, GameTime);
            return true;
        }

        /// <summary>Rotate a candidate task by 90 degrees clockwise.</summary>
        public bool TryRotateCandidate(string requestId)
        {
            if (!tasks.TryGetValue(requestId, out var task)
                || (task.State != RequestState.Waiting && task.State != RequestState.Withdrawn)) return false;
            task.Rotation = (task.Rotation + 1) % 4;
            return true;
        }

        public bool TryWithdraw(string requestId)
        {//尝试撤回
            if (!tasks.TryGetValue(requestId, out var task) || task.State != RequestState.Running) return false;
            Release(task); task.State = RequestState.Withdrawn;
            Emit(TaskEventKind.Withdrawn, task, GameTime);
            return true;
        }

        public bool TrySubmit(string requestId)
        {//尝试提交
            if (!tasks.TryGetValue(requestId, out var task) || task.State != RequestState.Ready) return false;
            Release(task); task.State = RequestState.Submitted;
            Emit(TaskEventKind.Submitted, task, GameTime, task.Definition.Tokens);
            return true;
        }

        /// <summary>Integration hook for IM: terminal feedback must close before the next round.</summary>
        public bool TryCloseFeedback(string requestId)
        {
            if (!tasks.TryGetValue(requestId, out var task)
                || (task.State != RequestState.Submitted && task.State != RequestState.TimedOut)) return false;
            task.State = RequestState.Closed; activeConversations.Remove(task.ConversationId);
            Emit(TaskEventKind.FeedbackClosed, task, GameTime);
            return true;
        }

        /// <summary>Interim rule: preserve old contributions and elapsed fraction; only later ticks use the new model.</summary>
        public bool TryChangeModel(ModelDefinition model)
        {
            if (!Paused || model == null) return false;
            foreach (var task in tasks.Values.Where(t => t.State == RequestState.Running || t.State == RequestState.Withdrawn))
                task.AbilityRemainder = task.AbilityRemainder / Model.Interval * model.Interval;
            Model = model;
            return true;
        }

        public void Advance(double delta)
        {//是让整个任务系统向前运行一段游戏时间的方法。

            Rules.NonNegative(delta, nameof(delta));
            //时间参数
            if (Paused || delta == 0) return;
            double end = GameTime + delta;
            if (double.IsInfinity(end)) throw new ArgumentOutOfRangeException(nameof(delta));
            foreach (var task in tasks.Values)
            {
                if (task.State != RequestState.Waiting && task.State != RequestState.Running
                    && task.State != RequestState.Withdrawn) continue;
                double available = Math.Max(0, Math.Min(end, task.Deadline) - GameTime);
                //计算任务在期限内还有多少可用时间，本次推进可能跨过任务期限，因此不能把整个 delta 都算给任务。
                double finishAfter = double.PositiveInfinity;
                //从本次开始时间起，还需要多少秒才能算完。先设为正无穷
                if (task.State == RequestState.Running)//只有正在运行的任务，才增加进度和能力。

                {
                    //距离完成的时间 = 剩余计算量 ÷ 每秒计算速度
                    finishAfter = (task.Definition.Calculation - task.Progress) / task.Definition.Speed;
                    double runTime = Math.Min(available, finishAfter);
                    task.Progress = Math.Min(task.Definition.Calculation, task.Progress + task.Definition.Speed * runTime);
                    AddAbilities(task, runTime);
                    // Completion wins when it coincides with timeout, even across a large frame.
                    if (finishAfter <= available + Epsilon)
                    //Epsilon 是一个很小的误差容许值，用来避免浮点计算导致的边界误判。
                    {
                        task.Progress = task.Definition.Calculation;
                        //把进度明确设置为总计算量，避免浮点误差导致完成时间略晚于期限
                        task.FinishedAt = Math.Min(GameTime + finishAfter, task.Deadline);
                        //记录真正的完成时间
                        task.Result = QualityResult.Evaluate(task.Definition, task.Abilities);
                        //根据任务要求和已经获得的能力，确定最终质量
                        task.State = RequestState.Ready;
                        Emit(TaskEventKind.Ready, task, task.FinishedAt.Value);
                        //生成“任务算完”的事件，并使用准确的完成时间。
                        continue;
                    }
                }
                if (end >= task.Deadline)
                {//如果本次推进结束时已经到达或越过期限，且任务没有在上面被判定完成，就执行超时处理。
                    Release(task); task.State = RequestState.TimedOut; task.FinishedAt = task.Deadline;
                    double tokens = task.Definition.Tokens * task.Progress / task.Definition.Calculation;
                    //完成比例计算计费 Token
                    Emit(TaskEventKind.TimedOut, task, task.Deadline, tokens);
                    
                }
            }
            GameTime = end;
            //
GameTime = end;
        }

        private void AddAbilities(TaskInstance task, double runTime)
        {//给任务赋予能力值
            double elapsed = task.AbilityRemainder + runTime;
            double count = Math.Floor((elapsed + Epsilon) / Model.Interval);
            task.AbilityRemainder = Math.Max(0, elapsed - count * Model.Interval);
            if (count == 0) return;
            foreach (var point in Model.Points)
                task.MutableAbilities[point.Key] = task.GetAbility(point.Key) + count * point.Value;
        }

        private void Release(TaskInstance task)
        {
            // State-owned pose supplies exactly the old cells; never release somebody else's occupancy.
            if (task.State != RequestState.Running && task.State != RequestState.Ready) return;
            foreach (var offset in task.Definition.RotatedShape(task.Rotation))
            {
                var cell = task.Origin + offset;
                if (occupancy.TryGetValue(cell, out var id) && id == task.Id) occupancy.Remove(cell);
            }
        }

        private void Emit(TaskEventKind kind, TaskInstance task, double now, double tokens = 0)
            => pendingEvents.Add(new TaskEvent(kind, task, now, tokens));

        public TaskEvent[] DrainEvents()
        {
            var events = pendingEvents.OrderBy(e => e.GameTime).ToArray();
            pendingEvents.Clear(); return events;
        }
    }
}
