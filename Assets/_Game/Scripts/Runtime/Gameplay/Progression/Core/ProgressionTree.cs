using System;
using System.Collections.Generic;
using System.Linq;
using CallmeCatgirl.Gameplay.TaskGrid;

namespace CallmeCatgirl.Gameplay.Progression
{
    [Serializable]
    public sealed class ProgressionNode
    {
        public string Id;
        public string Title;
        public int Cost;
        public string[] Prerequisites = Array.Empty<string>();
        public AbilityKind Ability;
        public double PointsPerTick = 1;
        public PolicyNodeKind Kind;
        public PolicyRoute Route;
        public string FlavorText = string.Empty;
        public string MechanicalText = string.Empty;
        public string ExclusiveGroup = string.Empty;
        public string[] AnyPrerequisites = Array.Empty<string>();
        public int Page;
        public int Column;
        public int Row;
        public int ReputationDelta;
        public PolicyEffects Effects = new PolicyEffects();
    }

    [Serializable]
    public sealed class ProgressionState
    {
        public int Version = 2;
        public string[] UnlockedIds = Array.Empty<string>();
        public int Reputation;
        public PolicyReputationEvent[] ReputationEvents = Array.Empty<PolicyReputationEvent>();
        public PolicyCorpusReward[] CorpusRewards = Array.Empty<PolicyCorpusReward>();
        public bool TerminalAcknowledged;
    }

    public enum PurchaseResult { Success, UnknownNode, AlreadyUnlocked, MissingPrerequisite, InsufficientFunds, Busy, ExcludedByChoice }

    /// <summary>Uses the host wallet and simulation; never advances time or settles task events.</summary>
    public sealed partial class ProgressionTree
    {
        private readonly Dictionary<string, ProgressionNode> nodes;
        private readonly HashSet<string> unlocked = new HashSet<string>(StringComparer.Ordinal);
        private readonly TaskSimulation simulation;
        private readonly Func<int, bool> spend;
        private bool busy;
        public event Action Changed;
        // Return copies: configuration/state cannot be mutated behind the transaction boundary.
        public IReadOnlyList<ProgressionNode> Nodes => nodes.Values.Select(Clone).ToArray();
        public bool IsUnlocked(string id) => id != null && unlocked.Contains(id);

        public ProgressionTree(IEnumerable<ProgressionNode> configuration, TaskSimulation simulation, Func<int, bool> spend)
        {
            this.simulation = simulation ?? throw new ArgumentNullException(nameof(simulation));
            this.spend = spend ?? throw new ArgumentNullException(nameof(spend));
            nodes = new Dictionary<string, ProgressionNode>(StringComparer.Ordinal);
            foreach (var source in configuration ?? throw new ArgumentNullException(nameof(configuration)))
            {
                if (source == null || string.IsNullOrWhiteSpace(source.Id) || source.Cost < 0
                    || !Enum.IsDefined(typeof(AbilityKind), source.Ability) || double.IsNaN(source.PointsPerTick)
                    || double.IsInfinity(source.PointsPerTick) || source.PointsPerTick < 0
                    || !Enum.IsDefined(typeof(PolicyNodeKind), source.Kind) || !Enum.IsDefined(typeof(PolicyRoute), source.Route)
                    || source.Prerequisites == null || source.Prerequisites.Any(string.IsNullOrWhiteSpace)
                    || source.AnyPrerequisites == null || source.AnyPrerequisites.Any(string.IsNullOrWhiteSpace)
                    || source.Effects == null || !source.Effects.Valid || source.Page < 0 || source.Column < 0 || source.Row < 0)
                    throw new ArgumentException("Invalid progression node.");
                nodes.Add(source.Id, Clone(source));
            }
            var visited = new HashSet<string>(); var visiting = new HashSet<string>();
            foreach (var id in nodes.Keys) Validate(id, visiting, visited);
        }

        private void Validate(string id, HashSet<string> visiting, HashSet<string> visited)
        {
            if (visited.Contains(id)) return;
            if (!nodes.ContainsKey(id) || !visiting.Add(id)) throw new ArgumentException("Missing prerequisite or cycle: " + id);
            foreach (var prerequisite in nodes[id].Prerequisites.Concat(nodes[id].AnyPrerequisites)) Validate(prerequisite, visiting, visited);
            visiting.Remove(id); visited.Add(id);
        }

        public PurchaseResult Availability(string id)
        {
            if (busy) return PurchaseResult.Busy;
            if (id == null || !nodes.TryGetValue(id, out var node)) return PurchaseResult.UnknownNode;
            if (unlocked.Contains(id)) return PurchaseResult.AlreadyUnlocked;
            if (unlocked.Any(n => nodes[n].Kind == PolicyNodeKind.Terminal)) return PurchaseResult.Busy;
            if (!string.IsNullOrEmpty(node.ExclusiveGroup) && unlocked.Any(n => nodes[n].ExclusiveGroup == node.ExclusiveGroup))
                return PurchaseResult.ExcludedByChoice;
            if (node.Prerequisites.Any(p => !unlocked.Contains(p))) return PurchaseResult.MissingPrerequisite;
            if (node.AnyPrerequisites.Length > 0 && !node.AnyPrerequisites.Any(unlocked.Contains)) return PurchaseResult.MissingPrerequisite;
            return PurchaseResult.Success;
        }

        public PurchaseResult TryPurchase(string id)
        {
            var result = Availability(id);
            if (result != PurchaseResult.Success) return result;
            var node = nodes[id];
            busy = true;
            try
            {
                // Inventory and wallet callbacks share the same reentrancy guard. Validate before charging.
                var target = new HashSet<string>(unlocked) { id };
                var model = BuildModel(target);
                BuildEffects(target);
                var reward = node.Kind == PolicyNodeKind.CorpusReward ? CreateReward(node) : null;
                checked { var validatedReputation = target.Sum(n => nodes[n].ReputationDelta) + reputationEvents.Sum(e => e.Delta); }
                if (!spend(node.Cost)) return PurchaseResult.InsufficientFunds;
                Apply(model);
                unlocked.Add(id);
                if (reward != null) corpusRewards.Add(reward);
            }
            finally { busy = false; }
            Changed?.Invoke();
            return PurchaseResult.Success;
        }

        public ProgressionState Capture() => new ProgressionState {
            UnlockedIds = unlocked.OrderBy(id => id, StringComparer.Ordinal).ToArray(), Reputation = Reputation,
            ReputationEvents = reputationEvents.Select(e => e.Copy()).ToArray(),
            CorpusRewards = corpusRewards.Select(e => e.Copy()).ToArray(), TerminalAcknowledged = terminalAcknowledged };

        /// <summary>Trusted save-load hook. Does not charge/refund; load wallet separately. Invalid saves leave everything unchanged.</summary>
        public bool TryRestore(ProgressionState state)
        {
            if (busy || !ValidateState(state, out _)) return false;
            var target = new HashSet<string>(state.UnlockedIds, StringComparer.Ordinal);
            ModelDefinition model;
            try { model = BuildModel(target); } catch (ArgumentException) { return false; }
            Apply(model);
            unlocked.Clear(); unlocked.UnionWith(target);
            reputationEvents.Clear(); corpusRewards.Clear();
            if (state.Version == 2) {
                reputationEvents.AddRange(state.ReputationEvents.Select(e => e.Copy()));
                corpusRewards.AddRange(state.CorpusRewards.Select(e => e.Copy()));
            }
            terminalAcknowledged = state.Version == 2 && state.TerminalAcknowledged;
            Changed?.Invoke(); return true;
        }

        private ModelDefinition BuildModel(HashSet<string> target)
        {
            var current = simulation.Model;
            var points = current.Points.ToDictionary(p => p.Key, p => p.Value);
            foreach (AbilityKind ability in Enum.GetValues(typeof(AbilityKind)))
            {
                double previous = unlocked.Where(id => nodes[id].Ability == ability).Sum(id => nodes[id].PointsPerTick);
                double next = target.Where(id => nodes[id].Ability == ability).Sum(id => nodes[id].PointsPerTick);
                points.TryGetValue(ability, out var value);
                // Add only the change; preserves independent AllocateAbility increments.
                points[ability] = value + (next - previous);
            }
            return new ModelDefinition(current.Id, current.Interval, points);
        }

        private void Apply(ModelDefinition model)
        {
            bool paused = simulation.Paused;
            try { simulation.Paused = true; simulation.TryChangeModel(model); }
            finally { simulation.Paused = paused; }
        }

        private static ProgressionNode Clone(ProgressionNode n) => new ProgressionNode
        { Id = n.Id, Title = n.Title, Cost = n.Cost, Ability = n.Ability, PointsPerTick = n.PointsPerTick,
          Kind = n.Kind, Route = n.Route, FlavorText = n.FlavorText, MechanicalText = n.MechanicalText,
          ExclusiveGroup = n.ExclusiveGroup, AnyPrerequisites = (string[])n.AnyPrerequisites.Clone(),
          Page = n.Page, Column = n.Column, Row = n.Row, ReputationDelta = n.ReputationDelta, Effects = n.Effects.Copy(),
          Prerequisites = (string[])n.Prerequisites.Clone() };

        public static ProgressionNode[] CreateDefaultNodes()
        {
            var result = new List<ProgressionNode>();
            var abilities = new[] { AbilityKind.Writing, AbilityKind.Code, AbilityKind.Reasoning };
            var labels = new[] { "写作", "代码", "推理" };
            for (int branch = 0; branch < abilities.Length; branch++)
                for (int tier = 1; tier <= 3; tier++)
                    result.Add(new ProgressionNode { Id = abilities[branch] + "-" + tier,
                        Title = labels[branch] + "专精 " + tier, Ability = abilities[branch],
                        Cost = tier * 100, PointsPerTick = 1, Column = tier, Row = branch + 3,
                        MechanicalText = labels[branch] + "能力每轮 +1", FlavorText = "把一个问题多想一次，也是一种进步。",
                        Prerequisites = tier == 1 ? Array.Empty<string>() : new[] { abilities[branch] + "-" + (tier - 1) } });
            result.AddRange(CreatePolicyNodes());
            return result.ToArray();
        }
    }
}
