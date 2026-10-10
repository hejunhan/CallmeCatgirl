using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

//格子坐标、任务定义、运行状态、能力门槛、质量结果和事件

namespace CallmeCatgirl.Gameplay.TaskGrid
{//代码块定义
    public readonly struct Cell : IEquatable<Cell>
    {
        public readonly int X;
        public readonly int Y;
        public Cell(int x, int y) { X = x; Y = y; }
        public bool Equals(Cell other) => X == other.X && Y == other.Y;
        public override bool Equals(object obj) => obj is Cell other && Equals(other);
        public override int GetHashCode() => unchecked(X * 397 ^ Y);
        public static Cell operator +(Cell a, Cell b) => new Cell(a.X + b.X, a.Y + b.Y);
        public override string ToString() => $"({X},{Y})";
    }

    public enum AbilityKind { Writing, Code, Reasoning, Retrieval }
    public enum RequestState { Waiting, Running, Withdrawn, Ready, Submitted, TimedOut, Closed }
    public enum Quality { BelowStandard, Complete, Excellent }
    public enum PlacementFailure { None, UnknownTask, InvalidState, OutOfBounds, LockedCell, Overlap }
    public enum TaskEventKind { Arrived, Placed, Withdrawn, Ready, Submitted, TimedOut, FeedbackClosed }
    //能力要求

    public sealed class AbilityRequirement
    {
        public AbilityKind Kind { get; }
        public double Minimum { get; }
        public double Excellent { get; }
        public AbilityRequirement(AbilityKind kind, double minimum, double excellent)
        {
            Rules.NonNegative(minimum, nameof(minimum));
            Rules.Positive(excellent, nameof(excellent));
            if (excellent <= minimum) throw new ArgumentException("Excellent must exceed minimum.");
            Kind = kind; Minimum = minimum; Excellent = excellent;
        }
    }

    public sealed class TaskDefinition
    {
        public string Id { get; }
        public string Title { get; }
        public double Calculation { get; }
        public double Speed { get; }
        public double Patience { get; }
        public double Tokens { get; }
        public IReadOnlyList<Cell> Shape { get; }
        public IReadOnlyList<AbilityRequirement> Requirements { get; }
        public TaskDefinition(string id, string title, IEnumerable<Cell> shape, double calculation,
            double speed, double patience, double tokens, IEnumerable<AbilityRequirement> requirements)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Task definition needs an ID.");
            Rules.Positive(calculation, nameof(calculation)); Rules.Positive(speed, nameof(speed));
            Rules.Positive(patience, nameof(patience)); Rules.NonNegative(tokens, nameof(tokens));
            var cells = shape?.ToArray() ?? throw new ArgumentNullException(nameof(shape));
            if (cells.Length == 0 || cells.Distinct().Count() != cells.Length)
                throw new ArgumentException("Shape must contain distinct cells.");
            var req = requirements?.ToArray() ?? throw new ArgumentNullException(nameof(requirements));
            if (req.Any(r => r == null) || req.Select(r => r.Kind).Distinct().Count() != req.Length)
                throw new ArgumentException("Requirements must contain distinct abilities.");
            Id = id; Title = title ?? id; Calculation = calculation; Speed = speed;
            Patience = patience; Tokens = tokens;
            Shape = Array.AsReadOnly(Normalize(cells)); Requirements = Array.AsReadOnly(req);
        }

        public Cell[] RotatedShape(int quarterTurns)
        {//旋转
            int turns = ((quarterTurns % 4) + 4) % 4;
            var cells = Shape.ToArray();
            for (int i = 0; i < turns; i++)
                for (int j = 0; j < cells.Length; j++) cells[j] = new Cell(-cells[j].Y, cells[j].X);
            return Normalize(cells);
        }

        private static Cell[] Normalize(Cell[] cells)
        {
            int minX = cells.Min(c => c.X), minY = cells.Min(c => c.Y);
            return cells.Select(c => new Cell(c.X - minX, c.Y - minY)).OrderBy(c => c.Y).ThenBy(c => c.X).ToArray();
        }
    }

    public sealed class ModelDefinition
    {
        public string Id { get; }
        public double Interval { get; }
        public IReadOnlyDictionary<AbilityKind, double> Points { get; }
        public ModelDefinition(string id, double interval, IDictionary<AbilityKind, double> points)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Model needs an ID.");
            Rules.Positive(interval, nameof(interval));
            if (points == null) throw new ArgumentNullException(nameof(points));
            foreach (var point in points.Values) Rules.NonNegative(point, nameof(points));
            Id = id; Interval = interval;
            Points = new ReadOnlyDictionary<AbilityKind, double>(new Dictionary<AbilityKind, double>(points));
        }
    }

    public sealed class QualityResult
    {
        public Quality Overall { get; }
        public IReadOnlyDictionary<AbilityKind, Quality> ByAbility { get; }
        public IReadOnlyDictionary<AbilityKind, double> Produced { get; }
        private QualityResult(Quality overall, Dictionary<AbilityKind, Quality> grades,
            Dictionary<AbilityKind, double> produced)
        {
            Overall = overall;
            ByAbility = new ReadOnlyDictionary<AbilityKind, Quality>(grades);
            Produced = new ReadOnlyDictionary<AbilityKind, double>(produced);
        }
        public static QualityResult Evaluate(TaskDefinition definition, IReadOnlyDictionary<AbilityKind, double> points)
        {
            var grades = new Dictionary<AbilityKind, Quality>();
            var produced = new Dictionary<AbilityKind, double>();
            Quality overall = Quality.Excellent;
            foreach (var requirement in definition.Requirements)
            {
                double amount = points.TryGetValue(requirement.Kind, out var value) ? value : 0;
                Rules.NonNegative(amount, nameof(points));
                Quality grade = amount >= requirement.Excellent ? Quality.Excellent
                    : amount >= requirement.Minimum ? Quality.Complete : Quality.BelowStandard;
                grades.Add(requirement.Kind, grade); produced.Add(requirement.Kind, amount);
                if (grade < overall) overall = grade;
            }
            // A task without ability requirements only needs to finish calculation.
            if (definition.Requirements.Count == 0) overall = Quality.Complete;
            return new QualityResult(overall, grades, produced);
        }
    }

    public sealed class TaskInstance
    {
        public string Id { get; }
        public string ConversationId { get; }
        public string RoundId { get; }
        public TaskDefinition Definition { get; }
        public RequestState State { get; internal set; }
        public double ArrivedAt { get; }
        public double Deadline { get; }
        public double Progress { get; internal set; }
        public double AbilityRemainder { get; internal set; }
        public double? FinishedAt { get; internal set; }
        public Cell Origin { get; internal set; }
        public int Rotation { get; internal set; }
        public QualityResult Result { get; internal set; }
        public IReadOnlyDictionary<AbilityKind, double> Abilities { get; }
        internal readonly Dictionary<AbilityKind, double> MutableAbilities = new Dictionary<AbilityKind, double>();
        internal TaskInstance(string id, string conversation, string round, TaskDefinition definition, double now)
        {
            Id = id; ConversationId = conversation; RoundId = round; Definition = definition;
            State = RequestState.Waiting; ArrivedAt = now; Deadline = now + definition.Patience;
            Abilities = new ReadOnlyDictionary<AbilityKind, double>(MutableAbilities);
        }
        public double GetAbility(AbilityKind kind) => MutableAbilities.TryGetValue(kind, out var value) ? value : 0;
        public double Remaining(double now) => State == RequestState.Ready || State == RequestState.Submitted
            || State == RequestState.TimedOut || State == RequestState.Closed ? 0 : Math.Max(0, Deadline - now);
    }

    public sealed class TaskEvent
    {
        public TaskEventKind Kind { get; }
        public string RequestId { get; }
        public double GameTime { get; }
        public double BillableTokens { get; }
        public QualityResult Result { get; }
        internal TaskEvent(TaskEventKind kind, TaskInstance task, double now, double tokens = 0)
        { Kind = kind; RequestId = task.Id; GameTime = now; BillableTokens = tokens; Result = task.Result; }
    }

    internal static class Rules
    {
        internal static void NonNegative(double value, string name)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value < 0)
                throw new ArgumentOutOfRangeException(name);
        }
        internal static void Positive(double value, string name)
        { NonNegative(value, name); if (value == 0) throw new ArgumentOutOfRangeException(name); }
    }
}
