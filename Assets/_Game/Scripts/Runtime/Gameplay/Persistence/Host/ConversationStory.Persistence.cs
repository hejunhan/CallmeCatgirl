using System;
using System.Collections.Generic;
using System.Linq;
using CallmeCatgirl.Gameplay.TaskGrid;
using CallmeCatgirl.Persistence;

namespace CallmeCatgirl.Gameplay
{
    public sealed partial class ConversationStory
    {
        public StoryState CaptureState()
        {
            if (choosing) throw new InvalidOperationException("Cannot save inside a reply reward transaction.");
            return new StoryState
            {
                Version = 4, Users = users.Select(p => new StoryUserState
                { UserId = p.Key, NodeId = p.Value.NodeId, PoolEntryId = p.Value.PoolEntryId,
                    Affinity = p.Value.Affinity, CompletedSessions = p.Value.CompletedSessions.OrderBy(id => id, StringComparer.Ordinal).ToArray(),
                    ConversationKind = (int)Kind(p.Key), History = p.Value.History.ToArray() }).ToArray(),
                ArrivedRequestIds = arrived.OrderBy(id => id, StringComparer.Ordinal).ToArray(),
                SettledRequestIds = settled.OrderBy(id => id, StringComparer.Ordinal).ToArray(),
                SubmittedRequestIds = submitted.OrderBy(id => id, StringComparer.Ordinal).ToArray(),
                PendingRequestIds = pending.Select(t => t.Id).ToArray(), IsOpen = IsOpen, IsReply = IsReply,
                UserId = UserId, RequestId = RequestId, Result = Result, Revision = Revision,
                VisibleChoices = Choices.Select(CaptureChoice).ToArray(),
                VisibleCorpusChoiceIds = Choices.Select(CorpusChoiceId).ToArray(),
                OrdinaryConversations = OrdinaryLifecycle.CaptureState()
            };
        }
        private static ReplyChoiceState CaptureChoice(StoryConfiguration.ReplyChoice c) => new ReplyChoiceState
        {
            MinimumFunds = c.MinimumFunds, FundsReward = c.FundsReward, Label = c.Label, PlayerReply = c.PlayerReply,
            UserReply = c.UserReply, Requirement = (int)c.Requirement, NextNodeId = c.NextNodeId
        };
        private static StoryConfiguration.ReplyChoice BuildChoice(ReplyChoiceState c) => new StoryConfiguration.ReplyChoice
        {
            MinimumFunds = c.MinimumFunds, FundsReward = c.FundsReward, Label = c.Label, PlayerReply = c.PlayerReply,
            UserReply = c.UserReply, Requirement = (StoryConfiguration.ReplyRequirement)c.Requirement, NextNodeId = c.NextNodeId
        };
        // JsonUtility normalizes null strings to empty strings; both represent the same authored optional text/link.
        private static bool SameChoice(ReplyChoiceState a, ReplyChoiceState b) => a.MinimumFunds == b.MinimumFunds && a.FundsReward == b.FundsReward
            && (a.Label ?? "") == (b.Label ?? "") && (a.PlayerReply ?? "") == (b.PlayerReply ?? "") && (a.UserReply ?? "") == (b.UserReply ?? "")
            && a.Requirement == b.Requirement && (a.NextNodeId ?? "") == (b.NextNodeId ?? "");

        public static ConversationStory RestoreState(StoryState s, TaskSimulation simulation, StoryConfiguration config,
            TaskGenerationConfiguration generation, System.Random random, EconomyManager economy, int previousLiveRevision,
            StoryConfiguration ordinaryDialoguePool = null, double initialAffinity = 0, double successAffinity = 1, double failureAffinity = -0.5)
        {
            StateRules.Require(s != null && (s.Version == 1 || s.Version == 2 || s.Version == 3 || s.Version == 4) && s.Users != null && s.ArrivedRequestIds != null
                && s.SubmittedRequestIds != null && s.PendingRequestIds != null && s.VisibleChoices != null
                && (s.Version == 1 || (s.VisibleCorpusChoiceIds != null
                    && s.VisibleCorpusChoiceIds.Length == s.VisibleChoices.Length && s.OrdinaryConversations != null))
                && s.Revision >= 0 && s.Revision < int.MaxValue && previousLiveRevision < int.MaxValue
                && Enum.IsDefined(typeof(Quality), s.Result) && (!s.IsReply || s.IsOpen), "Invalid story state.");
            if (simulation == null || economy == null) throw new ArgumentNullException("simulation/economy");
            var story = new ConversationStory(config, generation, random, ordinaryDialoguePool, initialAffinity, successAffinity, failureAffinity) { Economy = economy };
            foreach (var u in s.Users)
            {
                StateRules.Require(u != null && !string.IsNullOrWhiteSpace(u.UserId) && u.History != null
                    && u.History.All(h => h != null) && !story.users.ContainsKey(u.UserId), "Invalid/duplicate story user.");
                var user = story.BindUser(u.UserId, s.Version >= 3 ? u.PoolEntryId : null, true);
                if (s.Version >= 3)
                    StateRules.Require(Enum.IsDefined(typeof(StoryConfiguration.ConversationKind), u.ConversationKind)
                        && (int)story.Kind(u.UserId) == u.ConversationKind, "Saved conversation kind differs from content.");
                StateRules.Require(string.IsNullOrEmpty(u.NodeId) || user.Profile?.FindNode(u.NodeId) != null, "Unknown story node; content revision mismatch.");
                user.NodeId = u.NodeId; user.History.Clear(); user.History.AddRange(u.History);
                if (s.Version >= 4)
                {
                    StateRules.Require(StateRules.Finite(u.Affinity) && u.CompletedSessions != null, "Invalid affinity/progression state.");
                    foreach (string id in u.CompletedSessions)
                        StateRules.Require(id != null && user.Profile?.Sessions?.Any(session => session.Id == id) == true
                            && user.CompletedSessions.Add(id), "Unknown/duplicate completed session.");
                    user.Affinity = u.Affinity;
                }
            }
            foreach (string id in s.ArrivedRequestIds)
            {
                StateRules.Require(id != null && simulation.Tasks.ContainsKey(id) && story.arrived.Add(id), "Unknown/duplicate arrived request.");
            }
            foreach (string id in s.SubmittedRequestIds)
            {
                StateRules.Require(id != null && simulation.Tasks.TryGetValue(id, out var task)
                    && (task.State == RequestState.Submitted || task.State == RequestState.Closed) && task.Result != null && story.submitted.Add(id), "Invalid submitted story request.");
            }
            if (s.Version >= 4)
            {
                StateRules.Require(s.SettledRequestIds != null, "Missing affinity settlement ledger.");
                foreach (string id in s.SettledRequestIds)
                    StateRules.Require(id != null && story.arrived.Contains(id) && simulation.Tasks.TryGetValue(id, out var task)
                        && (task.State == RequestState.Submitted || task.State == RequestState.TimedOut || task.State == RequestState.Closed)
                        && story.settled.Add(id), "Invalid/duplicate affinity settlement.");
                StateRules.Require(story.settled.SetEquals(story.arrived.Where(id => {
                    var state = simulation.Tasks[id].State;
                    return state == RequestState.Submitted || state == RequestState.TimedOut || state == RequestState.Closed;
                })), "Affinity settlement ledger differs from role tasks.");
            }
            var queued = new HashSet<string>(StringComparer.Ordinal);
            foreach (string id in s.PendingRequestIds)
            {
                StateRules.Require(id != null && story.submitted.Contains(id) && simulation.Tasks[id].State == RequestState.Submitted
                    && queued.Add(id) && id != s.RequestId, "Invalid queued reply.");
                story.pending.Enqueue(simulation.Tasks[id]);
            }
            StateRules.Require(s.PendingRequestIds.Length == 0 || s.IsReply, "Queued replies lack active reply.");
            StateRules.Require(!s.IsOpen || (s.UserId != null && story.users.ContainsKey(s.UserId)), "Unknown active story user.");
            if (s.IsReply)
            {
                StateRules.Require(s.RequestId != null && story.submitted.Contains(s.RequestId)
                    && simulation.Tasks[s.RequestId].State == RequestState.Submitted
                    && simulation.Tasks[s.RequestId].ConversationId == s.UserId
                    && simulation.Tasks[s.RequestId].Result.Overall == s.Result && s.VisibleChoices.Length > 0, "Invalid active reply.");
                var user = story.users[s.UserId];
                var configured = user.Profile?.FindNode(user.NodeId)?.Choices;
                if (configured == null || configured.Length == 0) configured = config?.DefaultChoices;
                var allowed = (configured ?? Array.Empty<StoryConfiguration.ReplyChoice>()).Where(c => c != null).Select(CaptureChoice).ToList();
                allowed.Add(CaptureChoice(new StoryConfiguration.ReplyChoice { NextNodeId = user.Profile?.SequentialNext(user.NodeId) }));
                foreach (var choice in s.VisibleChoices)
                    StateRules.Require(choice != null && choice.MinimumFunds >= 0 && choice.FundsReward >= 0
                        && Enum.IsDefined(typeof(StoryConfiguration.ReplyRequirement), choice.Requirement)
                        && allowed.Any(a => SameChoice(a, choice)) && BuildChoice(choice).IsAvailable(s.Result), "Choice differs from story content.");
            }
            else StateRules.Require(s.VisibleChoices.Length == 0, "Non-reply has choices.");
            story.IsOpen = s.IsOpen; story.IsReply = s.IsReply; story.UserId = s.UserId; story.RequestId = s.RequestId; story.Result = s.Result;
            // No LoadChoices/OpenReply/Choose: preserve the exact prompt/history without replaying rewards.
            if (s.Version >= 2)
                story.Choices = s.VisibleChoices.Select((choice, index) => story.RestoreCorpusChoice(s.VisibleCorpusChoiceIds[index], choice)).ToArray();
            else
            {
            // Reuse authored choice objects so CorpusChoiceId keeps its stable choice-N/default-N identity after load.
            var activeUser=s.IsReply?story.users[s.UserId]:null;
            var originals=activeUser?.Profile?.FindNode(activeUser.NodeId)?.Choices;
            if(originals==null||originals.Length==0)originals=config?.DefaultChoices;
            var remaining=(originals??Array.Empty<StoryConfiguration.ReplyChoice>()).Where(c=>c!=null).ToList();
            story.Choices=s.VisibleChoices.Select(saved=>
            {
                var original=remaining.FirstOrDefault(c=>SameChoice(CaptureChoice(c),saved));
                if(original!=null){remaining.Remove(original);return original;}
                return BuildChoice(saved);
            }).ToArray();
            }
            var submittedTasks = s.SubmittedRequestIds.Select(id => simulation.Tasks[id]);
            story.ordinaryLifecycle = s.Version == 1
                ? OrdinaryConversationLifecycle.RestoreLegacyV1(config?.OrdinaryLifecycle ?? new OrdinaryConversationPolicy(), submittedTasks, story.IsScriptedConversation)
                : OrdinaryConversationLifecycle.RestoreState(s.OrdinaryConversations,
                config?.OrdinaryLifecycle ?? new OrdinaryConversationPolicy(),
                submittedTasks, story.IsScriptedConversation);
            story.Revision = Math.Max(s.Revision, previousLiveRevision) + 1;
            return story;
        }
    }
}
