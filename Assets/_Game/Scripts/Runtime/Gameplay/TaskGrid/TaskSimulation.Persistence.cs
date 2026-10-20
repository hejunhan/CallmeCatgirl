#define CALLMECATGIRL_TRAINING_GRID
using System;
using System.Collections.Generic;
using System.Linq;

namespace CallmeCatgirl.Gameplay.TaskGrid
{
    [Serializable] public sealed class CellState { public int X; public int Y; public Cell Build() => new Cell(X, Y); }
    [Serializable] public sealed class AbilityState { public AbilityKind Kind; public double Value; }
    [Serializable] public sealed class RequirementState { public AbilityKind Kind; public double Minimum; public double Excellent; }
    [Serializable] public sealed class ModelState
    {
        public string Id;
        public double Interval;
        public AbilityState[] Points;
        public static ModelState Capture(ModelDefinition model) => new ModelState
        { Id = model.Id, Interval = model.Interval, Points = StateRules.CaptureAbilities(model.Points) };
        public ModelDefinition Build() => new ModelDefinition(Id, Interval, StateRules.Abilities(Points));
    }
    [Serializable] public sealed class DefinitionState
    {
        public string Id;
        public string Title;
        public CellState[] Shape;
        public double Calculation;
        public double Speed;
        public double Patience;
        public double Tokens;
        public bool UsesCalculationBudget;
        public RequirementState[] Requirements;
        public static DefinitionState Capture(TaskDefinition d) => new DefinitionState
        {
            Id = d.Id, Title = d.Title, Shape = d.Shape.Select(StateRules.Cell).ToArray(),
            Calculation = d.Calculation, Speed = d.Speed, Patience = d.Patience, Tokens = d.Tokens, UsesCalculationBudget = d.UsesCalculationBudget,
            Requirements = d.Requirements.Select(r => new RequirementState
            { Kind = r.Kind, Minimum = r.Minimum, Excellent = r.Excellent }).ToArray()
        };
        public TaskDefinition Build()
        {
            StateRules.Require(Shape != null && Shape.Length > 0 && Shape.All(c => c != null)
                && Requirements != null && Requirements.All(r => r != null && Enum.IsDefined(typeof(AbilityKind), r.Kind)), "Invalid definition arrays.");
            return new TaskDefinition(Id, Title, Shape.Select(c => c.Build()), Calculation, Speed, Patience, Tokens,
                Requirements.Select(r => new AbilityRequirement(r.Kind, r.Minimum, r.Excellent)), UsesCalculationBudget);
        }
    }
    [Serializable] public sealed class RequestSnapshot
    {
        public string Id;
        public string ConversationId;
        public string RoundId;
        public DefinitionState Definition;
        public RequestState State;
        public double ArrivedAt;
        public double Progress;
        public double AbilityRemainder;
        public bool HasArchitecture;
        public ArchitectureRuntimeState Architecture;
        public bool HasFinishedAt;
        public double FinishedAt;
        public CellState Origin;
        public int Rotation;
        public AbilityState[] Abilities;
        public bool HasResult;
        public bool ReadyInCandidate;
        public bool IsFiller;
        public string VisitorDisplayName;
        public string VisitorSourceText;
        public string VisitorRequestFamily;
        public string VisitorSourceCustomerId;
    }
    [Serializable] public sealed class TaskSimulationState
    {
        public int Version;
        public int Width;
        public int Height;
        public CellState[] OpenCells;
        public int[] Compartments;
        public ModelState Model;
        public double GameTime;
        public bool Paused;
        public RequestSnapshot[] Requests;
        public int ExecutionProfileVersion;
        public double TokenThroughput;
        public ArchitectureRuntimeState Architecture;
    }

    public static class StateRules
    {
        public static void Require(bool condition, string reason) { if (!condition) throw new ArgumentException(reason); }
        public static bool Finite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);
        public static CellState Cell(Cell c) => new CellState { X = c.X, Y = c.Y };
        public static AbilityState[] CaptureAbilities(IReadOnlyDictionary<AbilityKind, double> p)
            => p.OrderBy(x => x.Key).Select(x => new AbilityState { Kind = x.Key, Value = x.Value }).ToArray();
        public static Dictionary<AbilityKind, double> Abilities(AbilityState[] values)
        {
            Require(values != null && values.Length <= 4, "Missing/oversized abilities.");
            var result = new Dictionary<AbilityKind, double>();
            foreach (var item in values)
            {
                Require(item != null && Enum.IsDefined(typeof(AbilityKind), item.Kind)
                    && Finite(item.Value) && item.Value >= 0 && !result.ContainsKey(item.Kind), "Invalid/duplicate ability.");
                result.Add(item.Kind, item.Value);
            }
            return result;
        }
    }

    // Compile beside TaskSimulation.cs, in CallmeCatgirl.TaskGrid. Add only the `partial` keyword there.
    public sealed partial class TaskSimulation
    {
        public TaskSimulationState CaptureState()
        {
            if (pendingEvents.Count != 0) throw new InvalidOperationException("Save after the owner's ReadEvents boundary; persistence must never drain events.");
            return new TaskSimulationState
            {
                Version = 1, Width = Width, Height = Height, OpenCells = openCells.OrderBy(c => c.Y).ThenBy(c => c.X).Select(StateRules.Cell).ToArray(),
                Compartments = (int[])compartments.Clone(), Model = ModelState.Capture(Model), GameTime = GameTime, Paused = Paused,
                Architecture = ArchitectureRuntimeState.Capture(Architecture),
                ExecutionProfileVersion =
#if CALLMECATGIRL_TRAINING_GRID
                    2, TokenThroughput = TokenThroughput,
#else
                    0, TokenThroughput = 0,
#endif
                Requests = tasks.Values.Select(t => new RequestSnapshot
                {
                    Id = t.Id, ConversationId = t.ConversationId, RoundId = t.RoundId, Definition = DefinitionState.Capture(t.Definition),
                    State = t.State, ArrivedAt = t.ArrivedAt, Progress = t.Progress, AbilityRemainder = t.AbilityRemainder,
                    HasArchitecture = t.Architecture != null, Architecture = ArchitectureRuntimeState.Capture(t.Architecture),
                    HasFinishedAt = t.FinishedAt.HasValue, FinishedAt = t.FinishedAt ?? 0, Origin = StateRules.Cell(t.Origin),
                    Rotation = t.Rotation, Abilities = StateRules.CaptureAbilities(t.Abilities), HasResult = t.Result != null,
                    IsFiller = t.IsFiller,
                    VisitorDisplayName = t.Visitor?.DisplayName, VisitorSourceText = t.Visitor?.SourceText,
                    VisitorRequestFamily = t.Visitor?.RequestFamily, VisitorSourceCustomerId = t.Visitor?.SourceCustomerId,
#if CALLMECATGIRL_TRAINING_GRID
                    ReadyInCandidate = t.State == RequestState.Ready && t.ReadyInCandidate
#else
                    ReadyInCandidate = false
#endif
                }).ToArray()
            };
        }

        /// <summary>Build a detached real simulation. No replay, events, clock advance or mutation of the running session.</summary>
        public static TaskSimulation RestoreState(TaskSimulationState s)
        {
            StateRules.Require(s != null && s.Version == 1 && s.Width > 0 && s.Height > 0 && s.Width <= 256 && s.Height <= 256
                && s.OpenCells != null && s.OpenCells.All(c => c != null) && s.Compartments != null && s.Model != null
                && s.Requests != null && s.Requests.Length <= 100000 && StateRules.Finite(s.GameTime) && s.GameTime >= 0, "Invalid simulation state.");
            var cells = s.OpenCells.Select(c => c.Build()).ToArray();
            StateRules.Require(cells.Distinct().Count() == cells.Length, "Duplicate open cells.");
            var simulation = new TaskSimulation(s.Width, s.Height, cells, s.Model.Build(), s.Compartments)
                { GameTime = s.GameTime, Paused = s.Paused };
            StateRules.Require(s.ExecutionProfileVersion >= 0 && s.ExecutionProfileVersion <= 2
                && StateRules.Finite(s.TokenThroughput) && s.TokenThroughput >= 0
                && (s.ExecutionProfileVersion != 0 || s.TokenThroughput == 0), "Invalid execution profile state.");
#if CALLMECATGIRL_TRAINING_GRID
            StateRules.Require(s.ExecutionProfileVersion<2 || s.Architecture!=null,"Missing architecture profile.");
            simulation.TokenThroughput = s.TokenThroughput;
            simulation.Architecture = s.ExecutionProfileVersion<2 && string.IsNullOrWhiteSpace(s.Architecture?.Id)
                ? ArchitectureRuntime.Neutral : s.Architecture?.Build() ?? ArchitectureRuntime.Neutral;
#else
            if (s.ExecutionProfileVersion != 0) throw new NotSupportedException("Save requires the training TaskGrid patch and CALLMECATGIRL_TRAINING_GRID.");
#endif
            foreach (var item in s.Requests)
            {
                StateRules.Require(item != null && !string.IsNullOrWhiteSpace(item.Id) && !string.IsNullOrWhiteSpace(item.ConversationId)
                    && !string.IsNullOrWhiteSpace(item.RoundId) && item.Definition != null && item.Origin != null
                    && Enum.IsDefined(typeof(RequestState), item.State) && item.Rotation >= 0 && item.Rotation < 4
                    && StateRules.Finite(item.ArrivedAt) && item.ArrivedAt >= 0 && item.ArrivedAt <= s.GameTime
                    && StateRules.Finite(item.Progress) && item.Progress >= 0 && StateRules.Finite(item.AbilityRemainder)
                    && item.AbilityRemainder >= 0 && StateRules.Finite(item.FinishedAt), "Invalid request fields.");
                var definition = item.Definition.Build();
                // Filler identity is explicit; never infer a role from an arbitrary request name.
                bool fillerRequestId = VisitorQueuePolicy.TryParseRequestId(item.Id, out _, out _);
                StateRules.Require(item.IsFiller == fillerRequestId, "Filler flag differs from request id.");
                if (item.IsFiller)
                {
                    StateRules.Require(item.ConversationId == VisitorQueuePolicy.ConversationId, "Filler task uses a different conversation.");
                }
                else
                {
                    StateRules.Require(string.IsNullOrEmpty(item.VisitorDisplayName) && string.IsNullOrEmpty(item.VisitorSourceText)
                        && string.IsNullOrEmpty(item.VisitorRequestFamily) && string.IsNullOrEmpty(item.VisitorSourceCustomerId),
                        "Non-filler request carries visitor identity.");
                }
                var visitor = item.IsFiller
                    ? new VisitorIdentity(item.VisitorDisplayName, item.VisitorSourceText, item.VisitorRequestFamily, item.VisitorSourceCustomerId)
                    : null;
                var task = new TaskInstance(item.Id, item.ConversationId, item.RoundId, definition, item.ArrivedAt)
                    { Visitor = visitor };
                StateRules.Require(StateRules.Finite(task.Deadline) && item.Progress <= definition.Calculation
                    && (!item.HasFinishedAt || (item.FinishedAt >= item.ArrivedAt && item.FinishedAt <= task.Deadline && item.FinishedAt <= s.GameTime)), "Invalid request timing.");
                bool unfinished = item.State == RequestState.Waiting || item.State == RequestState.Running || item.State == RequestState.Withdrawn;
                StateRules.Require(unfinished ? !item.HasFinishedAt && !item.HasResult && item.Progress < definition.Calculation && task.Deadline > s.GameTime
                    : item.HasFinishedAt, "Inconsistent request state/time.");
                StateRules.Require(!item.HasResult || (item.Progress == definition.Calculation && item.State != RequestState.TimedOut), "Invalid result.");
                StateRules.Require((item.State != RequestState.Ready && item.State != RequestState.Submitted) || item.HasResult, "Completed request lacks result.");
                StateRules.Require(item.State != RequestState.TimedOut || item.Progress < definition.Calculation, "Timeout is already complete.");
                StateRules.Require(!item.ReadyInCandidate || (s.ExecutionProfileVersion >= 1 && item.State == RequestState.Ready), "Invalid candidate-ready state.");
                StateRules.Require(item.State != RequestState.Closed || item.HasResult == (item.Progress == definition.Calculation), "Closed request result differs from completion.");
                StateRules.Require((item.State != RequestState.TimedOut && (item.State != RequestState.Closed || item.HasResult))
                    || item.FinishedAt == task.Deadline, "Timeout finish time differs from deadline.");
                StateRules.Require(!unfinished || item.AbilityRemainder < simulation.Model.Interval + Epsilon, "Invalid ability remainder.");
                var abilities = StateRules.Abilities(item.Abilities);
                StateRules.Require(item.State != RequestState.Waiting || (item.Progress == 0 && item.AbilityRemainder == 0 && abilities.Count == 0), "Waiting request already produced work.");
                // Unity JsonUtility expands null serializable objects to empty objects. The explicit presence flag is authoritative.
                StateRules.Require(!item.HasArchitecture || (s.ExecutionProfileVersion>=2 && item.Architecture!=null),"Missing frozen architecture.");
                task.Architecture = item.HasArchitecture ? item.Architecture.Build() : null;
                StateRules.Require(task.Architecture==null || (task.Architecture.FreezeOnPlacement && item.State!=RequestState.Waiting),"Invalid frozen architecture.");
                task.State = item.State; task.Progress = item.Progress; task.AbilityRemainder = item.AbilityRemainder;
                task.FinishedAt = item.HasFinishedAt ? (double?)item.FinishedAt : null; task.Origin = item.Origin.Build(); task.Rotation = item.Rotation;
#if CALLMECATGIRL_TRAINING_GRID
                task.ReadyInCandidate = item.ReadyInCandidate;
#endif
                foreach (var ability in abilities) task.MutableAbilities.Add(ability.Key, ability.Value);
                if (item.HasResult) task.Result = QualityResult.Evaluate(definition, task.Abilities);
                simulation.tasks.Add(task.Id, task);
                simulation.roundRequests.Add(Tuple.Create(task.ConversationId, task.RoundId), task.Id);
                if (task.State != RequestState.Closed && !task.IsFiller) simulation.activeConversations.Add(task.ConversationId, task.Id);
                if (task.State == RequestState.Running || (task.State == RequestState.Ready && !item.ReadyInCandidate))
                {
                    int? compartment = null;
                    foreach (var offset in definition.RotatedShape(task.Rotation))
                    {
                        var cell = task.Origin + offset;
                        StateRules.Require(simulation.InBounds(cell) && simulation.IsOpen(cell), "Occupied cell is locked/outside board.");
                        int current = simulation.CompartmentAt(cell);
                        StateRules.Require(!compartment.HasValue || compartment == current, "Task crosses partition.");
                        compartment = current; simulation.occupancy.Add(cell, task.Id);
                    }
                }
            }
            return simulation;
        }
    }
}
