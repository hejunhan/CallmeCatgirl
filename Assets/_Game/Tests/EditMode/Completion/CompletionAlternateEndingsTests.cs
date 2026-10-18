using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using CallmeCatgirl.Gameplay.Narrative;
using CallmeCatgirl.Gameplay.TaskGrid;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace CallmeCatgirl.Tests
{
    /// <summary>Three bounded real-income routes. No injected wallet, reputation, settlement event or ending fixture.</summary>
    public sealed class CompletionAlternateEndingsTests
    {
        private const string Menu = "Assets/_Game/Scenes/Gameplay/MenuScene.unity";
        private CompletionCampaignTests campaign;

        [UnityTest]
        public IEnumerator GoodPolicyEndingEarnsItsRouteAndSurvivesMidpointContinue()
        {
            EditorSceneManager.OpenScene(Menu);
            yield return new EnterPlayMode();
            // Construct all execution state and captured predicates after the play-mode domain reload.
            yield return RunPolicyRoute(true);
        }

        [UnityTest]
        public IEnumerator BadPolicyEndingEarnsItsRouteAndErasedMemorySurvivesContinue()
        {
            EditorSceneManager.OpenScene(Menu);
            yield return new EnterPlayMode();
            yield return RunPolicyRoute(false);
        }

        [UnityTest]
        public IEnumerator CompanionSevenMeetingsRequireRealOrdersAndExplicitDeparture()
        {
            EditorSceneManager.OpenScene(Menu);
            yield return new EnterPlayMode();
            yield return RunCompanionRoute();
        }

        private IEnumerator Bootstrap(string route)
        {
            campaign = new CompletionCampaignTests
            {
                RouteTag = route,
                GameSecondsLimit = 3600,
                SpawnedOrderLimit = 360,
                AdvanceCommandLimit = 1800
            };
            yield return campaign.BeginRealCampaign();
            yield return EarnAndBuyMemoryTwo();
            Assert.That(campaign.Memory, Is.EqualTo(2));
            Assert.That(campaign.Sim.OpenCellCount, Is.EqualTo(30));
            Assert.That(campaign.FixedSpend, Is.EqualTo(400));
            Debug.Log("[CompletionAlternateEndings] provenance=real-new-run; route=" + route
                + "; no injected cash/reputation/events; bounded to <3600 game seconds, <360 active generations, <1800 advances."
                + " Submit-local earned is not total income; normal partial timeout settlements remain active.");
        }

        private IEnumerator EarnAndBuyMemoryTwo()
        {
            yield return campaign.WorkUntil(() => campaign.Funds >= 100, "earn-second-install");
            yield return campaign.InstallNext(2);
        }

        private IEnumerator EarnAndPurchase(string id, int cost)
        {
            yield return campaign.WorkUntil(() => campaign.Funds >= cost, "earn-" + id);
            campaign.Purchase(id, cost);
            yield return null;
            yield return null;
            campaign.Checkpoint("bought-" + id);
        }

        private IEnumerator RunPolicyRoute(bool highReputation)
        {
            string route = highReputation ? "policy.good" : "policy.bad";
            yield return Bootstrap(route);
            yield return EarnAndPurchase("policy-foundations", 100);
            yield return EarnAndPurchase(highReputation ? "route-open" : "route-closed", 200);
            Assert.That(Reputation(), Is.EqualTo(highReputation ? 10 : -10));
            yield return EarnAndPurchase(highReputation ? "branch-technology-share" : "branch-distillation-claim", 240);
            Assert.That(Reputation(), Is.EqualTo(highReputation ? 20 : -20));
            Assert.That(campaign.Narrative.EndingRequested, Is.False);
            yield return SaveAndContinue("policy-midpoint");
            yield return EarnAndPurchase("main-emergence", 300);
            Assert.That(campaign.Narrative.PinnedContacts.Any(c => c.Id == NarrativeSession.CompanionId), Is.True);
            Assert.That(campaign.Narrative.RomanceReady, Is.False);
            yield return EarnAndPurchase("main-public-future", 500);
            Assert.That(Reputation(), Is.EqualTo(highReputation ? 20 : -20));
            if (!highReputation)
            {
                Assert.That(campaign.Narrative.CurrentFrame.Id, Is.EqualTo("ending.low"));
                var remember = ActiveButton("Choice_remember");
                Assert.That(EventSystem.current, Is.Not.Null);
                EventSystem.current.SetSelectedGameObject(remember.gameObject);
                remember.onClick.Invoke();
                Assert.That(campaign.Narrative.Notice, Is.EqualTo("你没有"));
                Assert.That(campaign.Narrative.CurrentFrame.Choices.Any(c => c.Id == "remember"), Is.False);
                Assert.That(EventSystem.current.currentSelectedGameObject.name, Is.EqualTo("DismissNotice"));
                Assert.That(campaign.Narrative.Choose("destroy"), Is.False, "The notice must block the destructive ending action.");
                yield return SaveAndContinue("bad-erased-option-notice");
                Assert.That(campaign.Narrative.Notice, Is.EqualTo("你没有"));
                ActiveButton("DismissNotice").onClick.Invoke();
                Assert.That(EventSystem.current.currentSelectedGameObject, Is.Null, "Dismissing the notice must clear the removed option's focus.");
                Assert.That(campaign.Narrative.Choose("remember"), Is.False);
                Assert.That(UnityEngine.Object.FindObjectsByType<Button>().Any(b => b.name == "Choice_remember" && b.isActiveAndEnabled), Is.False);
            }
            yield return FinishEnding(highReputation ? NarrativeEndingReason.HighReputation : NarrativeEndingReason.LowReputation,
                highReputation ? "ending.good" : "ending.low", highReputation ? "joke" : "destroy", route, 1740);
        }

        private IEnumerator RunCompanionRoute()
        {
            yield return Bootstrap("emotion");
            yield return EarnAndPurchase("policy-foundations", 100);
            yield return EarnAndPurchase("route-closed", 200);
            yield return EarnAndPurchase("main-emergence", 300);
            Assert.That(Reputation(), Is.EqualTo(-10));
            Assert.That(campaign.Narrative.CompanionBeat, Is.EqualTo(1));
            Assert.That(campaign.Narrative.CompanionMeetingReady, Is.True);
            var orderEvidence = new List<string>();
            for (int beat = 1; beat <= 7; beat++)
            {
                if (beat > 1)
                {
                    Assert.That(campaign.Narrative.CompanionMeetingReady, Is.False);
                    int completedBefore = campaign.Completed;
                    var observedBefore = new HashSet<string>(campaign.Narrative.Capture().ObservedCompletedRequestIds, StringComparer.Ordinal);
                    yield return campaign.WorkUntil(() => campaign.Narrative.CompanionMeetingReady
                        && campaign.Completed > completedBefore, "real-order-before-meeting-" + beat);
                    var freshOrders = campaign.Narrative.Capture().ObservedCompletedRequestIds.Where(id => !observedBefore.Contains(id)
                        && campaign.Sim.Tasks.TryGetValue(id, out var task) && task.Result?.Overall >= Quality.Complete
                        && !campaign.Training.IsTrainingRequest(id)
                        && (bool)CompletionCampaignTests.Call(campaign.Owner, "IsOperatingTask", id)).ToArray();
                    Assert.That(freshOrders, Is.Not.Empty, "Each meeting needs a new real, accepted operating-order submission.");
                    orderEvidence.Add(freshOrders[0]);
                }
                campaign.CloseDialogs();
                Assert.That((bool)CompletionCampaignTests.Call(campaign.Owner, "OpenNarrativeContact", NarrativeSession.CompanionId), Is.True);
                Assert.That(campaign.Narrative.CurrentFrame.Id, Is.EqualTo("companion." + beat));
                Assert.That(campaign.Narrative.Choose(beat == 7 ? "accept-invitation" : "first"), Is.True);
                Assert.That(campaign.Narrative.CurrentFrame.Id, Is.EqualTo("companion." + beat + ".after"));
                if (beat == 3) yield return SaveAndContinue("companion-mid-conversation-after-beat-3");
                Assert.That(campaign.Narrative.Choose("continue"), Is.True);
                Assert.That(campaign.Narrative.CompanionBeat, Is.EqualTo(beat + 1));
                Assert.That(campaign.Narrative.CompanionMeetingReady, Is.False);
                campaign.Checkpoint("companion-beat-" + beat + "-finished");
            }
            Assert.That(orderEvidence.Distinct(StringComparer.Ordinal).Count(), Is.EqualTo(6));
            Assert.That(campaign.Narrative.Capture().Memories.Count(m => m.Key.StartsWith("companion.", StringComparison.Ordinal)), Is.EqualTo(7));
            Assert.That(campaign.Narrative.Remembered("lin.habit"), Is.EqualTo("screwdriver"));
            Assert.That(campaign.Narrative.Remembered("lin.promise"), Is.EqualTo("sunset"));
            Assert.That(campaign.Narrative.Remembered("lin.boundary"), Is.EqualTo("ask-first"));
            Assert.That(campaign.Narrative.RomanceReady, Is.True);
            Debug.Log("[CompletionAlternateEndings] companion real order evidence=" + string.Join(",", orderEvidence));
            yield return EarnAndPurchase("main-public-future", 500);
            Assert.That(campaign.Narrative.CurrentFrame.Id, Is.EqualTo("ending.choose"));
            Assert.That(campaign.Narrative.Capture().PolicyEnding, Is.EqualTo(NarrativeEndingReason.OrdinaryReputation));
            Assert.That(campaign.Narrative.EndingReason, Is.EqualTo(NarrativeEndingReason.None));
            Assert.That(CompletionCampaignTests.Get<bool>(campaign.Owner, "GameEnded"), Is.False);
            Assert.That(campaign.Narrative.CurrentFrame.Choices.Select(c => c.Id), Is.EquivalentTo(new[] { "leave", "stay" }));
            Assert.That(campaign.Narrative.Choose("leave"), Is.True, "Only the explicit departure choice selects the independent fourth ending.");
            yield return FinishEnding(NarrativeEndingReason.Companion, "ending.companion", "together", "emotion", 1500);
        }

        private IEnumerator FinishEnding(NarrativeEndingReason reason, string frame, string choice, string endReason, int expectedSpend)
        {
            Assert.That(campaign.Narrative.EndingRequested, Is.True);
            Assert.That(campaign.Narrative.EndingReason, Is.EqualTo(reason));
            Assert.That(campaign.Narrative.CurrentFrame.Id, Is.EqualTo(frame));
            Assert.That(CompletionCampaignTests.Get<bool>(campaign.Owner, "GameEnded"), Is.False);
            Assert.That(campaign.Narrative.Choose(choice), Is.True);
            Assert.That(campaign.Narrative.CurrentFrame.Id, Is.EqualTo(frame + ".closed." + choice));
            Assert.That(campaign.Narrative.Choose("curtain"), Is.True);
            yield return null;
            yield return null;
            Assert.That(CompletionCampaignTests.Get<bool>(campaign.Owner, "GameEnded"), Is.True);
            Assert.That(CompletionCampaignTests.Get<string>(campaign.Owner, "EndReason"), Is.EqualTo(endReason));
            Assert.That(campaign.Narrative.EndingClosed, Is.True);
            Assert.That(campaign.Narrative.Choose("curtain"), Is.False);
            Assert.That(campaign.Narrative.Capture().AcknowledgedEventIds.Count(id => id == "narrative-end-run"), Is.EqualTo(1));
            Assert.That(campaign.Narrative.PendingEffects, Is.Empty);
            Assert.That(campaign.FixedSpend, Is.EqualTo(expectedSpend));
            Assert.That(campaign.Sim.GameTime, Is.LessThan(3600));
            Assert.That(CompletionCampaignTests.Get<int>(campaign.Owner, "CurrentMonth"), Is.EqualTo(1));
            yield return SaveAndContinue("closed-ending-once-ledgers");
            campaign.Checkpoint("real-alternate-ending-passed");
            Debug.Log("[CompletionAlternateEndings] PASS candidate: provenance=real-new-run; ending=" + endReason
                + "; fixedSpend=" + expectedSpend + "; simulatedSeconds=" + campaign.Sim.GameTime.ToString("0.###")
                + "; no month crossed; maintenance affordability untested; " + campaign.Summary("terminal"));
        }

        private IEnumerator SaveAndContinue(string phase)
        {
            // The public save command owns ReadEvents -> saveService.Save. Capture only after
            // that successful boundary; feedback-close events can still be queued after submit.
            campaign.Api("{\"kind\":\"save\"}");
            var snapshot = new Snapshot(campaign);
            campaign.DetachEconomyObserver();
            // This is the production menu continuation, whose new Core calls ContinueRun in Start.
            // It exercises scene unload/recreation rather than restoring only the current controller.
            yield return campaign.ReturnToMenuAndContinueQaSlot();
            snapshot.AssertUnchanged(campaign, phase);
            campaign.AttachEconomyObserver();
            campaign.Checkpoint("saved-continued-" + phase);
        }

        private int Reputation() => CompletionCampaignTests.Get<int>(CompletionCampaignTests.Get<object>(campaign.Policy(), "Tree"), "Reputation");
        private static Button ActiveButton(string name)
        {
            var result = UnityEngine.Object.FindObjectsByType<Button>().SingleOrDefault(b => b.name == name && b.isActiveAndEnabled);
            Assert.That(result, Is.Not.Null, "Production narrative control missing: " + name);
            Assert.That(result.interactable, Is.True);
            return result;
        }

        private sealed class Snapshot
        {
            private readonly int funds, completed, memory;
            private readonly double gameTime;
            private readonly bool ended;
            private readonly string runId, endReason, narrative, policy, training, economy;
            public Snapshot(CompletionCampaignTests c)
            {
                funds = c.Funds; completed = c.Completed; memory = c.Memory; gameTime = c.Sim.GameTime;
                ended = CompletionCampaignTests.Get<bool>(c.Owner, "GameEnded");
                runId = CompletionCampaignTests.Get<string>(CompletionCampaignTests.Call(c.Owner, "CaptureRun"), "RunId");
                endReason = CompletionCampaignTests.Get<string>(c.Owner, "EndReason");
                narrative = JsonUtility.ToJson(c.Narrative.Capture());
                policy = JsonUtility.ToJson(CompletionCampaignTests.Call(CompletionCampaignTests.Get<object>(c.Policy(), "Tree"), "Capture"));
                training = JsonUtility.ToJson(c.Training.CaptureState());
                economy = JsonUtility.ToJson(CompletionCampaignTests.Call(CompletionCampaignTests.Get<object>(c.Owner, "Economy"), "CaptureState"));
            }
            public void AssertUnchanged(CompletionCampaignTests c, string phase)
            {
                var actual = new Snapshot(c);
                Assert.That(actual.funds, Is.EqualTo(funds), phase + ": funds");
                Assert.That(actual.completed, Is.EqualTo(completed), phase + ": successful submissions");
                Assert.That(actual.memory, Is.EqualTo(memory), phase + ": committed memory");
                Assert.That(actual.gameTime, Is.EqualTo(gameTime), phase + ": simulation clock");
                Assert.That(actual.ended, Is.EqualTo(ended), phase + ": terminal state");
                Assert.That(actual.runId, Is.EqualTo(runId), phase + ": named QA run identity");
                Assert.That(actual.endReason, Is.EqualTo(endReason), phase + ": exclusive terminal reason");
                Assert.That(actual.narrative, Is.EqualTo(narrative), phase + ": narrative choices and acknowledged effects");
                Assert.That(actual.policy, Is.EqualTo(policy), phase + ": purchased policy and reputation ledger");
                Assert.That(actual.training, Is.EqualTo(training), phase + ": inventory and scripted corpus ledger");
                Assert.That(actual.economy, Is.EqualTo(economy), phase + ": wallet and settled-order deduplication ledger");
            }
        }

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            campaign?.CleanupSessionObservers();
            // Domain-reload instructions must be yielded by the top-level UnityTearDown.
            if (EditorApplication.isPlaying) yield return new ExitPlayMode();
        }
    }
}
