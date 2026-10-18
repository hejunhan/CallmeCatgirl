using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CallmeCatgirl.Gameplay.Narrative;
using CallmeCatgirl.Gameplay.TaskGrid;
using CallmeCatgirl.Gameplay.Training;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace CallmeCatgirl.Tests
{
    /// <summary>Bounded real campaign. No wallet injection, direct task settlement, debug quality override or ending fixture.</summary>
    public sealed class CompletionCampaignTests
    {
        private const string Menu = "Assets/_Game/Scenes/Gameplay/MenuScene.unity";
        private const string Gameplay = "Assets/_Game/Scenes/Prototypes/CoreGameplay.unity";
        private const double MaximumSceneLoadSeconds = 30;
        private const double MaximumGameSeconds = 7200;
        private const int MaximumSpawnedOrders = 600, MaximumAdvanceCommands = 4000;
        private MonoBehaviour owner;
        private string slot;
        private int spawned, advances, paidSubmissions, income, spent;
        private readonly List<string> maintenance = new List<string>();
        private Action<string> economyObserver;
        private object observedEconomy;
        private string awaitedScenePath;
        private bool awaitedSceneLoaded;
        private int awaitedSceneLoadedFrame;
        internal TaskSimulation Sim => Get<TaskSimulation>(owner, "Simulation");
        internal NarrativeSession Narrative => Get<NarrativeSession>(owner, "Narrative");
        internal TrainingSession Training => Get<TrainingSession>(Get<object>(owner, "Training"), "Session");
        internal int Funds => Get<int>(owner, "Funds");
        internal int Memory => Get<int>(owner, "MemoryLevel");
        internal MonoBehaviour Owner => owner;
        internal int Completed => Get<int>(owner, "CompletedTasks");
        internal int FixedSpend => spent;
        internal string RouteTag { get; set; } = string.Empty;
        internal double GameSecondsLimit { get; set; } = MaximumGameSeconds;
        internal int SpawnedOrderLimit { get; set; } = MaximumSpawnedOrders;
        internal int AdvanceCommandLimit { get; set; } = MaximumAdvanceCommands;

        [UnityTest]
        public IEnumerator EarnsFiveRealInstallationsAndNormalPolicyEndingThenRestores()
        {
            EditorSceneManager.OpenScene(Menu);
            yield return new EnterPlayMode();
            // Create the execution iterator and all captured predicates AFTER the play-mode domain reload.
            yield return RunAfterEnteringPlayMode();
        }
        private IEnumerator RunAfterEnteringPlayMode()
        {
            yield return BeginRealCampaign();

            for (int target = 2; target <= 5; target++)
            {
                object step = Get<object>(owner, "NextMemoryUpgrade");
                int cost = Get<int>(step, "Cost"), required = Get<int>(step, "RequiredCompletedTasks");
                int expectedCost = target == 2 ? 100 : target == 3 ? 200 : target == 4 ? 400 : 800;
                int expectedTasks = target == 2 ? 0 : target == 3 ? 3 : target == 4 ? 6 : 10;
                Assert.That(cost, Is.EqualTo(expectedCost)); Assert.That(required, Is.EqualTo(expectedTasks));
                yield return WorkUntil(() => Funds >= cost && Get<int>(owner, "CompletedTasks") >= required, "earn-install-" + target);
                yield return InstallNext(target); Checkpoint("installed-" + target); Api("{\"kind\":\"save\"}");
                Assert.That(Training.ArchitectureAvailability("alternate"), Is.EqualTo(TrainingResult.Success));
                Assert.That(Training.ArchitectureAvailability("architecture-slot-3"),
                    Is.EqualTo(target >= 4 ? TrainingResult.Success : TrainingResult.ArchitectureLocked));
            }
            Assert.That(Narrative.Capture().Memories.Count(m => m.Key.StartsWith("stage.", StringComparison.Ordinal)), Is.EqualTo(5));

            // Four serious doctor requests are real one/two/three-cell work, not just chapter text.
            yield return WorkUntil(() => Enumerable.Range(2, 4).All(n => Sim.Tasks.TryGetValue("narrative-doctor-stage-" + n, out var t)
                && t.State == RequestState.Closed && t.Result?.Overall >= Quality.Complete), "finish-doctor-stage-work");
            foreach (var purchase in new[] { Tuple.Create("policy-foundations", 100), Tuple.Create("route-closed", 200),
                Tuple.Create("main-emergence", 300), Tuple.Create("main-public-future", 500) })
            {
                yield return WorkUntil(() => Funds >= purchase.Item2, "earn-" + purchase.Item1);
                Purchase(purchase.Item1, purchase.Item2); yield return null; yield return null;
                Checkpoint("bought-" + purchase.Item1);
            }
            Assert.That(Get<int>(Get<object>(Policy(), "Tree"), "Reputation"), Is.EqualTo(-10));
            Assert.That(Narrative.EndingReason, Is.EqualTo(NarrativeEndingReason.OrdinaryReputation));
            Assert.That(Narrative.CurrentFrame.Id, Is.EqualTo("ending.normal"));
            Assert.That(Narrative.Choose("analyze"), Is.True);
            Assert.That(Narrative.CurrentFrame.Id, Is.EqualTo("ending.normal.closed.analyze"));
            Assert.That(Narrative.Choose("curtain"), Is.True); yield return null; yield return null;
            Assert.That(Get<bool>(owner, "GameEnded"), Is.True);
            Assert.That(Get<string>(owner, "EndReason"), Is.EqualTo("policy.normal"));
            Assert.That(Narrative.EndingClosed, Is.True); Assert.That(Memory, Is.EqualTo(5));
            int savedFunds = Funds, savedCompleted = Get<int>(owner, "CompletedTasks"), savedCorpus = Training.Corpus.Count;
            string narrativeLedger = JsonUtility.ToJson(Narrative.Capture());
            string policyLedger = JsonUtility.ToJson(Call(Get<object>(Policy(), "Tree"), "Capture"));
            string trainingLedger = JsonUtility.ToJson(Training.CaptureState());
            string economyLedger = JsonUtility.ToJson(Call(Get<object>(owner, "Economy"), "CaptureState"));
            string savedRunId = Get<string>(Call(owner, "CaptureRun"), "RunId"), savedPath = Get<string>(owner, "SavePath");
            Api("{\"kind\":\"save\"}");
            DetachEconomyObserver();
            Api("{\"kind\":\"restore\"}"); yield return null;
            Api("{\"kind\":\"clock\",\"paused\":true}");
            Assert.That(Funds, Is.EqualTo(savedFunds)); Assert.That(Get<int>(owner, "CompletedTasks"), Is.EqualTo(savedCompleted));
            Assert.That(Training.Corpus.Count, Is.EqualTo(savedCorpus)); Assert.That(Memory, Is.EqualTo(5));
            Assert.That(Get<string>(owner, "EndReason"), Is.EqualTo("policy.normal"));
            Assert.That(JsonUtility.ToJson(Narrative.Capture()), Is.EqualTo(narrativeLedger));
            Assert.That(JsonUtility.ToJson(Call(Get<object>(Policy(), "Tree"), "Capture")), Is.EqualTo(policyLedger));
            Assert.That(JsonUtility.ToJson(Training.CaptureState()), Is.EqualTo(trainingLedger));
            Assert.That(JsonUtility.ToJson(Call(Get<object>(owner, "Economy"), "CaptureState")), Is.EqualTo(economyLedger));
            Assert.That(spent, Is.EqualTo(2900), "Actual five installs, two ability policies and minimum terminal path must account for every fixed purchase.");
            Checkpoint("terminal-save-restore-passed");

            // Exercise the production scene-owned continue path as well as the same-Core restore above.
            // Every check compares this run's actual terminal values; random orders need not repeat UNITY09's count.
            var previousOwner = owner;
            yield return NavigateAndWait("ReturnToMenu", Menu);
            Assert.That(previousOwner == null, Is.True, "Returning to the menu must unload the previous Core.");
            Assert.That(Find("CoreGameplayController"), Is.Null, "The menu must not retain the old game session.");
            owner = null;
            yield return NavigateAndWait("ContinueSlot", Gameplay, slot);
            owner = Find("CoreGameplayController");
            Assert.That(owner, Is.Not.Null, "ContinueSlot must create a new Core in the gameplay scene.");
            Assert.That(owner, Is.Not.SameAs(previousOwner), "Continue must create a different controller instance.");
            Api("{\"kind\":\"clock\",\"paused\":true}");
            Assert.That(Get<string>(owner, "SavePath"), Is.EqualTo(savedPath), "Continue must use only the named QA slot.");
            Assert.That(Get<string>(Call(owner, "CaptureRun"), "RunId"), Is.EqualTo(savedRunId));
            Assert.That(Funds, Is.EqualTo(savedFunds)); Assert.That(Get<int>(owner, "CompletedTasks"), Is.EqualTo(savedCompleted));
            Assert.That(Memory, Is.EqualTo(5)); Assert.That(Training.Corpus.Count, Is.EqualTo(savedCorpus));
            Assert.That(Get<bool>(owner, "GameEnded"), Is.True);
            Assert.That(Get<string>(owner, "EndReason"), Is.EqualTo("policy.normal"));
            Assert.That(Narrative.EndingClosed, Is.True);
            Assert.That(JsonUtility.ToJson(Narrative.Capture()), Is.EqualTo(narrativeLedger));
            Assert.That(JsonUtility.ToJson(Call(Get<object>(Policy(), "Tree"), "Capture")), Is.EqualTo(policyLedger));
            Assert.That(JsonUtility.ToJson(Training.CaptureState()), Is.EqualTo(trainingLedger));
            Assert.That(JsonUtility.ToJson(Call(Get<object>(owner, "Economy"), "CaptureState")), Is.EqualTo(economyLedger));
            Checkpoint("terminal-menu-continue-passed");
            yield return new ExitPlayMode();
        }

        // Shared only by the bounded alternate-ending tests; the normal campaign keeps its original sequence.
        internal IEnumerator BeginRealCampaign()
        {
            yield return null;
            yield return StartNewGameFromMenu();
            yield return null; owner = Find("CoreGameplayController"); Assert.That(owner, Is.Not.Null);
            slot = "completion-campaign-" + Guid.NewGuid().ToString("N");
            Call(owner, "ConfigureSaveSlot", slot);
            Api("{\"kind\":\"clock\",\"paused\":true}"); AttachEconomyObserver();
            Assert.That(Sim.OpenCellCount, Is.Zero); Assert.That(Sim.Tasks, Is.Empty); Assert.That(Funds, Is.Zero);
            Assert.That(Memory, Is.Zero);
            Assert.That(Narrative.Choose("mew"), Is.True); yield return null; yield return null;
            Assert.That(Narrative.CenterCellRevealed, Is.True); Assert.That(Narrative.MemoryCount, Is.Zero);
            Assert.That(Sim.OpenCellCount, Is.EqualTo(1));
            Assert.That(Training.Corpus.Single().Scores[AbilityKind.Writing], Is.EqualTo(1));
            CloseDialogs();
            Api("{\"kind\":\"train\",\"recipe\":\"tutorial-first-model\",\"architecture\":\"baseline\",\"displayName\":\"真实经营首训\",\"investments\":[{\"ConversationId\":\"narrative-doctor\",\"Points\":1}]}");
            var starter = Training.Jobs.Single();
            Assert.That(starter.PaidCost, Is.Zero); Assert.That(Sim.Tasks[starter.RequestId].Definition.Shape.Count, Is.EqualTo(1));
            yield return FinishOne(starter.RequestId, 10);
            Assert.That(starter.Completed, Is.True); Assert.That(Training.ActiveModelId, Is.EqualTo(starter.Output.Id));
            CloseDialogs(); yield return null;
            yield return FinishOne(NarrativeSession.FirstDoctorRequestId, 10); CloseDialogs(); yield return null;
            yield return FinishOne(NarrativeSession.FirstOrdinaryRequestId, 20); CloseDialogs(); yield return null;
            Assert.That(Get<int>(owner, "CompletedTasks"), Is.EqualTo(2)); Assert.That(Funds, Is.EqualTo(120));
            yield return InstallNext(1);
            Assert.That(Funds, Is.EqualTo(20));
            Checkpoint("first-install"); Api("{\"kind\":\"save\"}");

            // The pre-tutorial catalogue entry has ZERO abilities. The trained Writing model is the genuine bootstrap.
            Assert.That(Training.Models.First(m => m.CorpusIds.Count == 0).Definition.Points.Values.Sum(), Is.Zero);
            yield return WorkUntil(() => Funds >= 100, "earn-code-policy");
            Purchase("Code-1", 100); yield return null;
            Assert.That(Sim.Model.Points[AbilityKind.Code], Is.GreaterThanOrEqualTo(1));
            yield return WorkUntil(() => Funds >= 100, "earn-reasoning-policy");
            Purchase("Reasoning-1", 100); yield return null;
            Assert.That(Sim.Model.Points[AbilityKind.Reasoning], Is.GreaterThanOrEqualTo(1));
            Checkpoint("all-three-real-abilities");

        }

        private IEnumerator StartNewGameFromMenu()
        {
            Assert.That(SceneManager.GetActiveScene().path, Is.EqualTo(Menu));
            yield return NavigateAndWait("StartNewGame", Gameplay);
        }

        internal IEnumerator ReturnToMenuAndContinueQaSlot()
        {
            string savedPath = Get<string>(owner, "SavePath");
            var previousOwner = owner;
            yield return NavigateAndWait("ReturnToMenu", Menu);
            Assert.That(previousOwner == null, Is.True, "Returning to the menu must unload the previous Core.");
            Assert.That(Find("CoreGameplayController"), Is.Null);
            owner = null;
            yield return NavigateAndWait("ContinueSlot", Gameplay, slot);
            owner = Find("CoreGameplayController");
            Assert.That(owner, Is.Not.Null);
            Assert.That(owner, Is.Not.SameAs(previousOwner), "Continue must create a different controller instance.");
            Api("{\"kind\":\"clock\",\"paused\":true}");
            Assert.That(Get<string>(owner, "SavePath"), Is.EqualTo(savedPath), "Continue must use only the named QA slot.");
        }

        private IEnumerator NavigateAndWait(string command, string destination, params object[] args)
        {
            Assert.That(EditorApplication.isPlaying, Is.True, "EnterPlayMode must complete before normal menu navigation.");
            Assert.That(EditorApplication.isPaused, Is.False, "The player loop must be running to complete an asynchronous scene load.");
            Assert.That(Application.CanStreamedLevelBeLoaded(destination), Is.True, "Destination must be enabled in Build Settings: " + destination);
            var navigation = Find("SceneNavigation");
            Assert.That(navigation, Is.Not.Null, "The current scene must expose its production SceneNavigation.");
            Assert.That(Get<bool>(navigation, "IsTransitioning"), Is.False);
            awaitedScenePath = destination;
            awaitedSceneLoaded = false;
            awaitedSceneLoadedFrame = -1;
            int editorAdvances = 0, initialPlayerFrame = Time.frameCount;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            SceneManager.sceneLoaded += ObserveAwaitedSceneLoaded;
            try
            {
                // Production SceneNavigation owns LoadSceneAsync and intentionally exposes no AsyncOperation.
                // Observe its real completion event rather than issuing a second load from the test.
                Call(navigation, command, args);
                Assert.That(Get<bool>(navigation, "IsTransitioning"), Is.True, "Production navigation did not accept " + command + ".");
                while ((!awaitedSceneLoaded || !SceneManager.GetSceneByPath(destination).isLoaded
                    || SceneManager.GetActiveScene().path != destination || Time.frameCount <= awaitedSceneLoadedFrame)
                    && clock.Elapsed.TotalSeconds < MaximumSceneLoadSeconds)
                {
                    // EditMode's consumer recursively advances nested IEnumerators, but a null yield
                    // is an editor update, not a guarantee of a PlayerLoop frame or I/O completion.
                    EditorApplication.QueuePlayerLoopUpdate();
                    editorAdvances++;
                    yield return null;
                }
                string observation = command + ": realSeconds=" + clock.Elapsed.TotalSeconds.ToString("0.###")
                    + ", editorAdvances=" + editorAdvances + ", playerFrames=" + (Time.frameCount - initialPlayerFrame)
                    + ", sceneLoadedEvent=" + awaitedSceneLoaded + ", loaded=" + SceneManager.GetSceneByPath(destination).isLoaded
                    + ", active=" + SceneManager.GetActiveScene().path + ", playing=" + EditorApplication.isPlaying
                    + ", editorPaused=" + EditorApplication.isPaused + ", transitioning="
                    + (navigation == null ? "previous-scene-unloaded" : Get<bool>(navigation, "IsTransitioning").ToString());
                Debug.Log("[CompletionCampaign] " + observation);
                Assert.That(awaitedSceneLoaded && SceneManager.GetSceneByPath(destination).isLoaded
                    && SceneManager.GetActiveScene().path == destination && Time.frameCount > awaitedSceneLoadedFrame,
                    Is.True, "Production scene navigation did not finish within " + MaximumSceneLoadSeconds + " real seconds. " + observation);
            }
            finally
            {
                SceneManager.sceneLoaded -= ObserveAwaitedSceneLoaded;
                awaitedScenePath = null;
                clock.Stop();
            }
        }

        private void ObserveAwaitedSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.path != awaitedScenePath || mode != LoadSceneMode.Single) return;
            awaitedSceneLoaded = true;
            awaitedSceneLoadedFrame = Time.frameCount;
        }

        internal IEnumerator InstallNext(int target)
        {
            CloseDialogs(); Api("{\"kind\":\"pause\",\"paused\":true}");
            int funds = Funds, cost = Get<int>(Get<object>(owner, "NextMemoryUpgrade"), "Cost");
            Call(owner, "UpgradeMemory");
            Assert.That(Get<bool>(owner, "IsMemoryInstallPending"), Is.True, Get<string>(owner, "Message"));
            Assert.That(Memory, Is.EqualTo(target - 1)); Assert.That(Funds, Is.EqualTo(funds));
            var install = Find("MemoryInstallGame") ?? UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .FirstOrDefault(m => m.GetType().GetMethod("SkipInstall") != null && Get<bool>(m, "IsOpen"));
            Assert.That(install, Is.Not.Null); Call(install, "SkipInstall"); yield return null; yield return null;
            Assert.That(Memory, Is.EqualTo(target)); Assert.That(Funds, Is.EqualTo(funds - cost)); spent += cost;
            Assert.That(Narrative.MemoryCount, Is.EqualTo(target));
            CloseDialogs();
            string contact = target <= 2 ? NarrativeSession.DoctorId : NarrativeSession.StageContactId;
            Assert.That((bool)Call(owner, "OpenNarrativeContact", contact), Is.True, "The real pinned contact must open the installed stage.");
            Assert.That(Narrative.CurrentFrame.Id, Is.EqualTo("stage." + target));
            Assert.That(Narrative.Choose("first"), Is.True); Assert.That(Narrative.Choose("continue"), Is.True);
            yield return null; yield return null; CloseDialogs();
        }

        private IEnumerator FinishOne(string id, double limit)
        {
            CloseDialogs(); Assert.That(Sim.Tasks.ContainsKey(id), Is.True, id);
            Assert.That(Place(id), Is.True, "No legal placement for " + id);
            Api("{\"kind\":\"pause\",\"paused\":false}");
            double start = Sim.GameTime;
            while (Sim.Tasks[id].State == RequestState.Running && Sim.GameTime - start < limit) Advance(.25);
            Assert.That(Sim.Tasks[id].State, Is.EqualTo(RequestState.Ready), id);
            Submit(id); yield return null; yield return null;
        }

        internal IEnumerator WorkUntil(Func<bool> achieved, string phase)
        {
            Checkpoint(phase + "-start");
            int idle = 0;
            while (!achieved())
            {
                Bound(phase); CloseDialogs();
                Api("{\"kind\":\"pause\",\"paused\":true}");
                foreach (var ready in Sim.Tasks.Values.Where(t => t.State == RequestState.Ready && !Training.IsTrainingRequest(t.Id)).ToArray()) Submit(ready.Id);
                CloseDialogs();
                if (achieved()) break;
                foreach (var candidate in Sim.Tasks.Values.Where(t => (t.State == RequestState.Waiting || t.State == RequestState.Withdrawn)
                    && (bool)Call(owner, "IsOperatingTask", t.Id) && CanProduce(t) && !Training.IsTrainingRequest(t.Id))
                    .OrderByDescending(t => t.Id.StartsWith("narrative-doctor-stage-", StringComparison.Ordinal)).ToArray()) Place(candidate.Id);
                // Buffer a modest real queue, rather than spamming hundreds of orders into unusable holes.
                for (int attempt = 0; attempt < 12 && HasFreeAdjacentPair()
                    && (Sim.Tasks.Values.Count(t => t.State == RequestState.Waiting || t.State == RequestState.Withdrawn) < 24
                        || !Sim.Tasks.Values.Any(t => t.State == RequestState.Running)); attempt++)
                {
                    Bound(phase); var button = UnityEngine.Object.FindObjectsByType<Button>(FindObjectsSortMode.None)
                        .SingleOrDefault(b => b.name == "AddRequestButton" && b.isActiveAndEnabled && b.interactable);
                    Assert.That(button, Is.Not.Null, "Campaign must use the real visible AddRequestButton, not an unavailable debug command.");
                    var before = new HashSet<string>(Sim.Tasks.Keys, StringComparer.Ordinal); button.onClick.Invoke();
                    var added = Sim.Tasks.Values.Where(t => !before.Contains(t.Id)).ToArray();
                    Assert.That(added.Length, Is.EqualTo(1), "The normal request UI did not generate exactly one order.");
                    spawned++; if (CanProduce(added[0])) Place(added[0].Id);
                }
                bool running = Sim.Tasks.Values.Any(t => t.State == RequestState.Running);
                if (!running)
                {
                    Assert.That(++idle, Is.LessThanOrEqualTo(4), "No compatible order fits the actual board: " + Summary(phase));
                    yield return null; continue;
                }
                idle = 0; Api("{\"kind\":\"pause\",\"paused\":false}");
                // Time is advanced by the real owner, so arrivals, deadlines and monthly charges remain active.
                Advance(Math.Min(5, Math.Min(Get<double>(owner, "DayTimeRemaining"), Get<double>(owner, "MonthTimeRemaining"))));
                yield return null;
            }
            CloseDialogs(); Api("{\"kind\":\"pause\",\"paused\":true}"); Checkpoint(phase + "-done");
        }
        private bool CanProduce(TaskInstance task) => task.Definition.Requirements.All(r => r.Minimum <= 0
            || Sim.Model.Points.TryGetValue(r.Kind, out var value) && value > 0);
        private bool HasFreeAdjacentPair()
        {
            for (int y = 0; y < Sim.Height; y++)
                for (int x = 0; x < Sim.Width; x++)
                {
                    var cell = new Cell(x, y);
                    if (!Sim.IsOpen(cell) || Sim.TaskAt(cell) != null) continue;
                    foreach (var neighbor in new[] { new Cell(x + 1, y), new Cell(x, y + 1) })
                        if (neighbor.X < Sim.Width && neighbor.Y < Sim.Height && Sim.IsOpen(neighbor)
                            && Sim.TaskAt(neighbor) == null && Sim.CompartmentAt(cell) == Sim.CompartmentAt(neighbor)) return true;
                }
            return false;
        }
        private bool Place(string id)
        {
            for (int rotation = 0; rotation < 4; rotation++)
                for (int y = 0; y < Sim.Height; y++)
                    for (int x = 0; x < Sim.Width; x++)
                        if (Sim.Preview(id, new Cell(x, y), rotation) == PlacementFailure.None)
                        { Api("{\"kind\":\"place\",\"id\":\"" + id + "\",\"x\":" + x + ",\"y\":" + y + ",\"rotation\":" + rotation + "}"); return true; }
            return false;
        }
        private void Submit(string id)
        {
            var task = Sim.Tasks[id];
            Assert.That(QualityResult.Evaluate(task.Definition, task.Abilities).Overall, Is.GreaterThanOrEqualTo(Quality.Complete),
                "A naturally below-standard order cannot fund campaign progress: " + id);
            int before = Funds; Api("{\"kind\":\"submit\",\"id\":\"" + id + "\"}");
            if (!Training.IsTrainingRequest(id)) { paidSubmissions++; income += Funds - before; }
            CloseDialogs();
        }
        internal void Purchase(string id, int cost)
        {
            CloseDialogs(); int before = Funds;
            Assert.That(Call(Policy(), "TryPurchase", id).ToString(), Is.EqualTo("Success"), id);
            Assert.That(Funds, Is.EqualTo(before - cost), "Policy must really spend its configured price: " + id); spent += cost;
        }
        internal object Policy()
        {
            var component = owner.GetComponent(owner.GetType().Assembly.GetType("CallmeCatgirl.Gameplay.Progression.ProgressionController", true));
            Assert.That(component, Is.Not.Null); Call(component, "EnsureBound"); return component;
        }
        internal void CloseDialogs()
        {
            if (owner == null) return;
            var menu = Find("SystemMenuHost"); if (menu != null) Call(menu, "CloseAll");
            for (int n = 0; n < 16 && Narrative.CurrentFrame != null; n++)
            {
                var frame = Narrative.CurrentFrame;
                if (frame.CanClose && Narrative.CloseConversation()) break;
                Assert.Fail("Campaign cannot silently choose a required narrative frame: " + frame.Id);
            }
            var story = Get<object>(owner, "Story");
            for (int n = 0; n < 16 && Get<bool>(story, "IsReply"); n++)
                Api("{\"kind\":\"reply\",\"choice\":0,\"storyRevision\":" + Get<int>(story, "Revision") + "}");
            if (Get<bool>(story, "IsOpen") && !Get<bool>(story, "IsReply")) Api("{\"kind\":\"closeHistory\"}");
        }
        private void Advance(double seconds)
        { advances++; Bound("advance"); Assert.That(seconds, Is.GreaterThan(0)); Api("{\"kind\":\"advance\",\"seconds\":" + seconds.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "}"); }
        private void Bound(string phase)
        {
            Assert.That(Get<bool>(owner, "GameEnded"), Is.False, Summary(phase));
            Assert.That(Sim.GameTime, Is.LessThan(GameSecondsLimit), Summary(phase));
            Assert.That(spawned, Is.LessThan(SpawnedOrderLimit), Summary(phase));
            Assert.That(advances, Is.LessThan(AdvanceCommandLimit), Summary(phase));
        }
        internal string Summary(string phase) => (string.IsNullOrEmpty(RouteTag) ? phase : RouteTag + "/" + phase) + ": gameSeconds=" + Sim.GameTime.ToString("0.##") + ", spawned=" + spawned
            + ", paidSubmissions=" + paidSubmissions + ", funds=" + Funds + ", earned=" + income + ", spent=" + spent
            + ", completed=" + Get<int>(owner, "CompletedTasks") + ", RAM=" + Memory + ", month=" + Get<int>(owner, "CurrentMonth")
            + ", debt=" + Get<int>(owner, "DebtAmount") + ", grace=" + Get<bool>(owner, "IsInGracePeriod")
            + ", model=[" + string.Join(";", Sim.Model.Points.Select(p => p.Key + "=" + p.Value.ToString("0.###"))) + "]"
            + ", slot=" + slot + ", maintenance=[" + string.Join("; ", maintenance) + "]";
        internal void Checkpoint(string phase) => Debug.Log("[CompletionCampaign] " + Summary(phase));
        internal void AttachEconomyObserver()
        {
            observedEconomy = Get<object>(owner, "Economy");
            economyObserver = text => { if (text.Contains("月底") || text.Contains("欠费") || text.Contains("宽限期")) maintenance.Add(text); };
            observedEconomy.GetType().GetEvent("OnEconomyLog").AddEventHandler(observedEconomy, economyObserver);
        }
        internal void DetachEconomyObserver()
        { if (observedEconomy != null && economyObserver != null) observedEconomy.GetType().GetEvent("OnEconomyLog").RemoveEventHandler(observedEconomy, economyObserver); observedEconomy = null; economyObserver = null; }
        internal object Api(string json)
        {
            var type = owner.GetType().Assembly.GetType("CallmeCatgirl.Gameplay.GameCommand", true);
            var result = Call(owner, "ExecuteCommand", JsonUtility.FromJson(json, type));
            Assert.That(Get<bool>(result, "ok"), Is.True, json + " => " + Get<string>(result, "code") + "; " + Summary("command")); return result;
        }
        private static MonoBehaviour Find(string name) => UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None).FirstOrDefault(m => m.GetType().Name == name);
        internal static T Get<T>(object value, string name)
        { var type = value.GetType(); var property = type.GetProperty(name); return (T)(property != null ? property.GetValue(value) : type.GetField(name).GetValue(value)); }
        internal static object Call(object value, string name, params object[] args)
            => value.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Single(m => m.Name == name && m.GetParameters().Length == args.Length).Invoke(value, args);
        internal void CleanupSessionObservers()
        {
            DetachEconomyObserver();
            if (owner != null) Debug.Log("[CompletionCampaign] Final observed state: " + Summary("cleanup"));
        }
        [UnityTearDown] public IEnumerator Cleanup()
        {
            CleanupSessionObservers();
            if (EditorApplication.isPlaying) yield return new ExitPlayMode();
        }
    }
}
