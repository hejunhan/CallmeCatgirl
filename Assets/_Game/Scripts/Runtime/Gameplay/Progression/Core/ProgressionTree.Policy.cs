using System;
using System.Collections.Generic;
using System.Linq;

namespace CallmeCatgirl.Gameplay.Progression
{
    public sealed partial class ProgressionTree
    {
        public const int BadReputationThreshold = -15;
        public const int GoodReputationThreshold = 15;
        private readonly List<PolicyReputationEvent> reputationEvents = new List<PolicyReputationEvent>();
        private readonly List<PolicyCorpusReward> corpusRewards = new List<PolicyCorpusReward>();
        private bool terminalAcknowledged;

        /// <summary>Host inventory query, called once before paying for the destructive scan. Zero is a valid empty inventory.</summary>
        public Func<double> BestAvailableCorpusPoints { get; set; }
        public PolicyEffects Effects => BuildEffects(unlocked);
        public int Reputation => checked(unlocked.Sum(id => nodes[id].ReputationDelta) + reputationEvents.Sum(e => e.Delta));
        public PolicyEndingBand ReputationBand => ClassifyReputation(Reputation);
        public IReadOnlyList<PolicyCorpusReward> PendingCorpusRewards => Array.AsReadOnly(corpusRewards.Where(r => !r.Acknowledged).Select(r => r.Copy()).ToArray());
        public PolicyTerminalSignal PendingTerminal
        {
            get {
                var terminal = unlocked.OrderBy(id => id, StringComparer.Ordinal).FirstOrDefault(id => nodes[id].Kind == PolicyNodeKind.Terminal);
                return terminal == null || terminalAcknowledged ? null : new PolicyTerminalSignal("policy:terminal:" + terminal, Reputation, ReputationBand);
            }
        }

        public static PolicyEndingBand ClassifyReputation(int reputation) => reputation <= BadReputationThreshold
            ? PolicyEndingBand.Bad : reputation >= GoodReputationThreshold ? PolicyEndingBand.Good : PolicyEndingBand.Normal;

        public bool RecordReputationEvent(string eventId, int delta)
        {
            if (busy || string.IsNullOrWhiteSpace(eventId) || reputationEvents.Any(e => e.EventId == eventId)
                || unlocked.Any(n => nodes[n].Kind == PolicyNodeKind.Terminal)) return false;
            try { checked { var next = Reputation + delta; } } catch (OverflowException) { return false; }
            reputationEvents.Add(new PolicyReputationEvent { EventId = eventId, Delta = delta });
            Changed?.Invoke(); return true;
        }

        /// <summary>Acknowledge only after the training ledger reports Success or AlreadyRecorded.</summary>
        public bool AcknowledgeCorpusReward(string eventId)
        {
            if (busy) return false;
            var reward = corpusRewards.FirstOrDefault(r => r.EventId == eventId && !r.Acknowledged);
            if (reward == null) return false;
            reward.Acknowledged = true; Changed?.Invoke(); return true;
        }

        /// <summary>Host first hands the signal to narrative arbitration, then acknowledges the accepted signal.</summary>
        public bool AcknowledgeTerminal()
        {
            if (busy || PendingTerminal == null) return false;
            terminalAcknowledged = true; Changed?.Invoke(); return true;
        }

        private PolicyCorpusReward CreateReward(ProgressionNode node)
        {
            double best = BestAvailableCorpusPoints?.Invoke() ?? 0;
            if (double.IsNaN(best) || double.IsInfinity(best) || best < 0) throw new ArgumentException("Invalid inventory quality snapshot.");
            double points = Math.Max(4, best * 1.25);
            if (double.IsInfinity(points)) throw new ArgumentException("Corpus reward overflow.");
            return new PolicyCorpusReward { EventId = "policy:corpus:" + node.Id, NodeId = node.Id,
                ConversationId = "policy-book-scan", Stage = 3, FrozenPoints = points };
        }

        private PolicyEffects BuildEffects(IEnumerable<string> target)
        {
            var effects = new PolicyEffects();
            foreach (var id in target.OrderBy(n => n, StringComparer.Ordinal)) effects.Combine(nodes[id].Effects);
            return effects;
        }

        /// <summary>Side effect free; permits host preflight before committing the shared run save.</summary>
        public bool ValidateState(ProgressionState state, out string error)
        {
            error = "Invalid policy state.";
            if (state == null || (state.Version != 1 && state.Version != 2) || state.UnlockedIds == null) return false;
            var target = new HashSet<string>(StringComparer.Ordinal);
            foreach (var id in state.UnlockedIds)
                if (string.IsNullOrWhiteSpace(id) || !nodes.ContainsKey(id) || !target.Add(id)) return false;
            foreach (var id in target) {
                var n = nodes[id];
                if (n.Prerequisites.Any(p => !target.Contains(p))
                    || (n.AnyPrerequisites.Length > 0 && !n.AnyPrerequisites.Any(target.Contains))) return false;
            }
            if (target.Where(id => !string.IsNullOrEmpty(nodes[id].ExclusiveGroup))
                .GroupBy(id => nodes[id].ExclusiveGroup).Any(g => g.Count() > 1)) return false;
            if (target.Count(id => nodes[id].Kind == PolicyNodeKind.Terminal) > 1) return false;
            try { BuildEffects(target); } catch (ArgumentException) { return false; }
            // Original v1 has only the stable ability IDs, and no story reward ledger to replay.
            if (state.Version == 1) {
                if (target.Any(id => nodes[id].Kind != PolicyNodeKind.Ability)) return false;
                error = null; return true;
            }
            if (state.ReputationEvents == null || state.CorpusRewards == null) return false;
            var events = new HashSet<string>(StringComparer.Ordinal);
            foreach (var e in state.ReputationEvents)
                if (e == null || string.IsNullOrWhiteSpace(e.EventId) || !events.Add(e.EventId)) return false;
            try {
                int expected = checked(target.Sum(id => nodes[id].ReputationDelta) + state.ReputationEvents.Sum(e => e.Delta));
                if (expected != state.Reputation) return false;
            } catch (OverflowException) { return false; }
            var rewards = new HashSet<string>(StringComparer.Ordinal);
            foreach (var r in state.CorpusRewards) {
                if (r == null || r.NodeId == null || !target.Contains(r.NodeId) || nodes[r.NodeId].Kind != PolicyNodeKind.CorpusReward
                    || r.EventId != "policy:corpus:" + r.NodeId || !rewards.Add(r.NodeId) || r.ConversationId != "policy-book-scan"
                    || r.Stage != 3 || double.IsNaN(r.FrozenPoints) || double.IsInfinity(r.FrozenPoints) || r.FrozenPoints < 4) return false;
            }
            if (target.Any(id => nodes[id].Kind == PolicyNodeKind.CorpusReward && !rewards.Contains(id))) return false;
            if (state.TerminalAcknowledged && !target.Any(id => nodes[id].Kind == PolicyNodeKind.Terminal)) return false;
            error = null; return true;
        }

        private static ProgressionNode[] CreatePolicyNodes()
        {
            return new[] {
                Policy("policy-foundations", "一份可以更改的约定", 100, 0, 0, 1,
                    "先把怎样学习、怎样回答写清楚。", "训练得分 ×1.10", new PolicyEffects { TrainingScoreMultiplier = 1.10 }),
                Policy("route-open", "把门留着", 200, 0, 1, 0,
                    "把工具摆到阳光下，也接受别人会带着它离开。", "开放路线 · 与封闭互斥\n客流 ×0.85；训练得分 ×1.15；声望 +10",
                    new PolicyEffects { CustomerArrivalMultiplier = .85, TrainingScoreMultiplier = 1.15 }, new[] { "policy-foundations" },
                    route: PolicyRoute.Open, exclusive: "distribution-route", reputation: 10),
                Policy("route-closed", "门后的机房", 200, 0, 1, 2,
                    "每扇门都有钥匙，每把钥匙都可以按月收费。", "封闭路线 · 与开放互斥\n客流 ×1.15；推理速度 ×1.10；声望 -10",
                    new PolicyEffects { CustomerArrivalMultiplier = 1.15, InferenceSpeedMultiplier = 1.10 }, new[] { "policy-foundations" },
                    route: PolicyRoute.Closed, exclusive: "distribution-route", reputation: -10),
                Policy("branch-book-scan", "再见，纸页", 180, 0, 2, 1,
                    "扫描头记住了字句，装订线再也不能把它们收回去。", "一次奖励：购买当时最佳剩余语料点数 ×1.25，最低 4 点\n原书在故事中被破坏；不自动购买更多书籍",
                    new PolicyEffects(), new[] { "policy-foundations" }, kind: PolicyNodeKind.CorpusReward),
                Policy("branch-distillation-claim", "一份过于响亮的声明", 240, 0, 2, 2,
                    "虚构寓言：一座高塔指控另一座高塔偷走影子。围观者先排起了队。", "客流 ×1.30；对话结束概率 ×0.70；声望 -10\n只影响后续对话留存，不妨碍当前任务完成",
                    new PolicyEffects { CustomerArrivalMultiplier = 1.30, EndProbabilityMultiplier = .70 }, new[] { "route-closed" },
                    route: PolicyRoute.Closed, reputation: -10),
                Policy("branch-technology-share", "给后来者的梯子", 240, 0, 2, 0,
                    "梯子没有收回来。有人越过围墙，也有人留下来一起修台阶。", "对话结束概率 ×0.80；声望 +10",
                    new PolicyEffects { EndProbabilityMultiplier = .80 }, new[] { "route-open" },
                    route: PolicyRoute.Open, reputation: 10),
                Policy("main-emergence", "第二页：回答以外", 300, 1, 0, 1,
                    "在问题之间，有人谈起今天窗外的天气。", "解锁与一名真实个体的对话机会\n不是购买感情；推理速度 ×1.10",
                    new PolicyEffects { EmotionOpportunityUnlocked = true, InferenceSpeedMultiplier = 1.10 },
                    any: new[] { "route-open", "route-closed" }),
                Policy("branch-efficient-training", "少绕一圈", 250, 1, 1, 0,
                    "绕路可以有风景；重复烧坏同一根保险丝没有。", "训练工作量 ×0.85；训练金币成本 ×0.90",
                    new PolicyEffects { TrainingWorkMultiplier = .85, TrainingCostMultiplier = .90 }, new[] { "main-emergence" }),
                Policy("branch-auto-placement", "把手留给新的问题", 250, 1, 1, 2,
                    "熟悉的方块自己归位，你终于可以抬头看一眼。", "解锁合法空位自动摆放\n不会挤走现有任务；满盘时保留候选任务",
                    new PolicyEffects { AutoPlacement = true }, new[] { "main-emergence" }),
                Policy("branch-open-stewardship", "共同维护", 300, 1, 2, 0,
                    "署名册越来越长，决定不再只属于第一行。", "训练得分 ×1.10；声望 +8",
                    new PolicyEffects { TrainingScoreMultiplier = 1.10 }, new[] { "main-emergence", "route-open" }, reputation: 8, route: PolicyRoute.Open),
                Policy("branch-closed-consolidation", "唯一的入口", 300, 1, 2, 2,
                    "指示牌把所有道路画到同一扇收费站前。", "推理速度 ×1.15；客流 ×1.10；声望 -8",
                    new PolicyEffects { InferenceSpeedMultiplier = 1.15, CustomerArrivalMultiplier = 1.10 },
                    new[] { "main-emergence", "route-closed" }, reputation: -8, route: PolicyRoute.Closed),
                Policy("main-public-future", "把回答交给未来", 500, 1, 3, 1,
                    "最后一个按钮不承诺替你选择怎样的世界。", "触发一次政策终局\n声望 ≤ -15：坏结局；-14～14：普通；≥15：好结局\n感情结局由明确选择单独进入",
                    new PolicyEffects(), new[] { "main-emergence" }, kind: PolicyNodeKind.Terminal)
            };
        }

        private static ProgressionNode Policy(string id, string title, int cost, int page, int column, int row,
            string flavor, string mechanics, PolicyEffects effects, string[] required = null, string[] any = null,
            PolicyRoute route = PolicyRoute.None, string exclusive = "", int reputation = 0, PolicyNodeKind kind = PolicyNodeKind.Policy)
            => new ProgressionNode { Id = id, Title = title, Cost = cost, Page = page, Column = column, Row = row,
                FlavorText = flavor, MechanicalText = mechanics, Effects = effects, Prerequisites = required ?? Array.Empty<string>(),
                AnyPrerequisites = any ?? Array.Empty<string>(), Route = route, ExclusiveGroup = exclusive,
                ReputationDelta = reputation, Kind = kind, PointsPerTick = 0 };
    }
}
