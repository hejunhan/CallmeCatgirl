using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using CallmeCatgirl.Gameplay.TaskGrid;
using CallmeCatgirl.Debugging;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace CallmeCatgirl.Tests
{
    public sealed class GameJamIntegrationTests
    {
        private static MonoBehaviour Owner() => Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None)
            .Single(o => o.GetType().FullName == "CallmeCatgirl.Gameplay.CoreGameplayController");
        private static T Get<T>(object o, string property)
        {
            Assert.That(o, Is.Not.Null, "Missing instance while reading " + property);
            var member = o.GetType().GetProperty(property);
            Assert.That(member, Is.Not.Null, o.GetType().FullName + " has no property " + property);
            return (T)member.GetValue(o);
        }
        private static object Call(object o, string method, params object[] args)
        {
            Assert.That(o, Is.Not.Null, "Missing instance while calling " + method);
            var member = o.GetType().GetMethod(method);
            Assert.That(member, Is.Not.Null, o.GetType().FullName + " has no method " + method);
            return member.Invoke(o, args);
        }
        private static IEnumerator WaitForScene(string name)
        {
            float until=Time.realtimeSinceStartup+20;
            while(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name!=name && Time.realtimeSinceStartup<until)
                yield return null;
            Assert.That(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name,Is.EqualTo(name),"Scene transition timed out.");
            yield return null;
        }
        private static void MakeScripted(object story)
        {
            foreach(var user in (Array)story.GetType().GetField("Users").GetValue(story))
            {var field=user.GetType().GetField("Kind");field.SetValue(user,Enum.Parse(field.FieldType,"Scripted"));}
        }
        private static Button Button(string name) => Object.FindObjectsByType<Button>(FindObjectsInactive.Include, FindObjectsSortMode.None).Single(b => b.name == name);
        private static object[] Items(object owner, string property) => ((System.Collections.IEnumerable)Get<object>(owner, property)).Cast<object>().ToArray();
        private static double CompletionSeconds(TaskSimulation simulation, string id)
        {
            var definition = simulation.Tasks[id].Definition;
            return simulation.TokenThroughput > 0
                ? (definition.Tokens > 0 ? definition.Tokens : definition.Calculation) / simulation.TokenThroughput
                : definition.Calculation / definition.Speed;
        }
        private static Cell FirstLegalOrigin(TaskSimulation simulation, string id)
        {
            for (int y = 0; y < simulation.Height; y++)
                for (int x = 0; x < simulation.Width; x++)
                {
                    var cell = new Cell(x, y);
                    if (simulation.Preview(id, cell, 0) == PlacementFailure.None) return cell;
                }
            throw new InvalidOperationException("No legal placement for " + id);
        }

        [UnityTest]
        public IEnumerator SavedProgressionPanelPurchasesThroughSharedWalletAndPreservesPause()
        {
            EditorSceneManager.OpenScene("Assets/_Game/Scenes/Prototypes/CoreGameplay.unity");
            yield return new EnterPlayMode();
            Application.runInBackground = true;
            yield return null;
            var owner = Owner(); var simulation = Get<TaskSimulation>(owner, "Simulation");
            var controller = owner.GetComponents<MonoBehaviour>().Single(c => c.GetType().Name == "ProgressionController");
            Call(controller, "EnsureBound");
            int funds = Get<int>(owner, "Funds"); double points = simulation.Model.Points[AbilityKind.Writing];
            if (Get<bool>(owner, "TrainingEnabled"))
            {
                Assert.That(Call(controller, "TryPurchase", "Writing-1").ToString(), Is.EqualTo("Busy"));
                Assert.That(Get<int>(owner, "Funds"), Is.EqualTo(funds));
                Assert.That(simulation.Model.Points[AbilityKind.Writing], Is.EqualTo(points));
                yield return new ExitPlayMode(); yield break;
            }
            Assert.That(Get<object>(Get<object>(owner, "Story"), "Economy"), Is.SameAs(Get<object>(owner, "Economy")));
            Button("OpenProgression").onClick.Invoke(); yield return null;
            var overlay = GameObject.Find("ProgressionOverlay"); Assert.That(overlay, Is.Not.Null);
            for (int frame = 0; frame < 60 && !overlay.GetComponentsInChildren<Button>().Any(b => b.name == "Writing-1"); frame++)
                yield return null;
            var panelButtons = overlay.GetComponentsInChildren<Button>(true);
            Assert.That(panelButtons.Any(b => b.name == "Writing-1"), Is.True,
                "Policy buttons: " + string.Join(",", panelButtons.Select(b => b.name)) + "; panels: " + string.Join(",", overlay.GetComponentsInChildren<MonoBehaviour>(true).Select(p => p.GetType().Name + ":" + p.enabled + ":" + p.gameObject.activeInHierarchy)));
            var purchase = panelButtons.Single(b => b.name == "Writing-1");
            Assert.That(purchase.interactable, Is.True); purchase.onClick.Invoke();
            Assert.That(Get<int>(owner, "Funds"), Is.EqualTo(funds - 100));
            Assert.That(simulation.Model.Points[AbilityKind.Writing], Is.EqualTo(points + 1));
            Assert.That(simulation.Paused, Is.True); Assert.That(simulation.GameTime, Is.Zero);
            Assert.That(Call(controller, "TryPurchase", "Writing-1").ToString(), Is.EqualTo("AlreadyUnlocked"));
            Assert.That(Get<int>(owner, "Funds"), Is.EqualTo(funds - 100));
            string saved = (string)Call(controller, "SaveJson");
            Assert.That(Call(controller, "LoadJson", saved), Is.EqualTo(true));
            Assert.That(Call(controller, "LoadJson", saved), Is.EqualTo(true));
            Assert.That(simulation.Model.Points[AbilityKind.Writing], Is.EqualTo(points + 1));
            Button("CloseProgression").onClick.Invoke(); Assert.That(overlay.activeSelf, Is.False);
            yield return new ExitPlayMode();
        }

        [UnityTest]
        public IEnumerator ConfiguredStoryRewardsSharedWalletAndRejectsOldControllerCallback()
        {
            EditorSceneManager.OpenScene("Assets/_Game/Scenes/Prototypes/CoreGameplay.unity");
            yield return new EnterPlayMode(); yield return null;
            var owner = Owner();
            var field = owner.GetType().GetField("configuration", BindingFlags.Instance | BindingFlags.NonPublic);
            var original = (ScriptableObject)field.GetValue(owner);
            var fixture = Object.Instantiate(original);
            fixture.GetType().GetField("OpeningBalance").SetValue(fixture, null);
            var importer = Type.GetType("CallmeCatgirl.EditorTools.StoryJsonImporter, Assembly-CSharp-Editor", true);
            var storyConfig = (ScriptableObject)importer.GetMethod("Parse").Invoke(null, new object[] {
                File.ReadAllText("Examples/StoryImport/placeholder-effects.example.json"), new[] { "A", "B", "C" } });
            try
            {
                MakeScripted(storyConfig);
                fixture.GetType().GetField("Story").SetValue(fixture, storyConfig);
                field.SetValue(owner, fixture); Call(owner, "ResetSimulation");
                var simulation = Get<TaskSimulation>(owner, "Simulation");
                Call(owner, "SelectRequest", "A-1");
                var origin = Enumerable.Range(0, simulation.Width * simulation.Height).Select(i => new Cell(i % simulation.Width, i / simulation.Width))
                    .First(c => simulation.Preview("A-1", c, 0) == PlacementFailure.None);
                Assert.That(simulation.TryPlace("A-1", origin, 0, out _), Is.True);
                simulation.Paused = false; Call(owner, "AdvanceGame", CompletionSeconds(simulation, "A-1") + .01d); simulation.Paused = true;
                Assert.That(simulation.Tasks["A-1"].State, Is.EqualTo(RequestState.Ready));
                Call(owner, "SubmitSelected");
                var story = Get<object>(owner, "Story"); int revision = Get<int>(story, "Revision");
                Assert.That(Get<bool>(story, "IsReply"), Is.True);
                int funds = Get<int>(owner, "Funds"); double time = simulation.GameTime;
                Call(owner, "ChooseReplyAtRevision", 1, revision);
                Assert.That(Get<int>(owner, "Funds"), Is.EqualTo(funds + 5));
                Call(owner, "ChooseReplyAtRevision", 1, revision);
                Assert.That(Get<int>(owner, "Funds"), Is.EqualTo(funds + 5));
                Assert.That(simulation.GameTime, Is.EqualTo(time)); Assert.That(simulation.Paused, Is.True);
                Call(owner, "NextRound"); Assert.That(Get<string>(owner, "SelectedId"), Is.Not.EqualTo("A-1"));
            }
            finally
            {
                field.SetValue(owner, original); Call(owner, "ResetSimulation");
                Object.DestroyImmediate(fixture); Object.DestroyImmediate(storyConfig);
            }
            yield return new ExitPlayMode();
        }

        [UnityTest]
        public IEnumerator SavedSceneRaycastDragAndRotationRemainAvailableWhileManuallyPaused()
        {
            EditorSceneManager.OpenScene("Assets/_Game/Scenes/Prototypes/CoreGameplay.unity");
            yield return new EnterPlayMode(); Application.runInBackground = true; yield return null;
            var owner = Owner(); var simulation = Get<TaskSimulation>(owner, "Simulation");
            var presentation = Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None).Single(p => p.GetType().Name == "PlanningPresentation");
            Assert.That(Get<bool>(presentation, "PlanningRequested"), Is.False);
            var papers = Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None).Where(p => p.GetType().Name == "PlanningPaper").ToArray();
            Assert.That(papers.Select(p => p.name), Is.EquivalentTo(new[] { "MemoryPanel", "CandidatePanel", "ConversationPanel", "LogArea" }));
            foreach (var paper in papers)
                Assert.That(paper.GetComponent<Image>().raycastTarget, Is.True, "Whole paper must receive hover events.");
            var card = Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None).Single(p => p.GetType().Name == "TaskCardView" && Get<string>(p, "RequestId") == "A-1");
            var rect = (RectTransform)card.transform;
            var point = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center));
            var events = UnityEngine.EventSystems.EventSystem.current;
            var pointer = new UnityEngine.EventSystems.PointerEventData(events) { position = point, button = UnityEngine.EventSystems.PointerEventData.InputButton.Left };
            var hits = new System.Collections.Generic.List<UnityEngine.EventSystems.RaycastResult>();
            events.RaycastAll(pointer, hits);
            Assert.That(hits[0].gameObject.GetComponentInParent(card.GetType()), Is.SameAs(card), "Card center must reach the real request rather than a visual overlay.");
            UnityEngine.EventSystems.ExecuteEvents.Execute(card.gameObject, pointer, UnityEngine.EventSystems.ExecuteEvents.beginDragHandler);
            var view = Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None).Single(p => p.GetType().Name == "CoreGameplayCanvasView");
            var board = (RectTransform)view.GetType().GetField("board", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(view);
            pointer.position = RectTransformUtility.WorldToScreenPoint(null, board.TransformPoint(new Vector3(board.rect.xMin + board.rect.width * 1.5f / simulation.Width, board.rect.yMax - board.rect.height * 2.5f / simulation.Height)));
            UnityEngine.EventSystems.ExecuteEvents.Execute(card.gameObject, pointer, UnityEngine.EventSystems.ExecuteEvents.dragHandler);
            UnityEngine.EventSystems.ExecuteEvents.Execute(card.gameObject, pointer, UnityEngine.EventSystems.ExecuteEvents.endDragHandler);
            Assert.That(simulation.Tasks["A-1"].State, Is.EqualTo(RequestState.Running));
            Assert.That(simulation.Paused, Is.True); Assert.That(simulation.GameTime, Is.Zero);
            Call(owner, "TogglePause"); Call(owner, "TogglePause");
            Assert.That(Get<bool>(presentation, "PlanningRequested"), Is.True);
            Call(owner, "RotateSelected");
            Assert.That(simulation.Tasks["A-1"].Rotation, Is.EqualTo(1));
            Assert.That(simulation.Paused, Is.True); Assert.That(simulation.GameTime, Is.Zero);
            yield return new ExitPlayMode();
        }

        [UnityTest]
        public IEnumerator MainMenuStartsFreshSessionAndReturnDoesNotDuplicateControllersOrWallets()
        {
            EditorSceneManager.OpenScene("Assets/_Game/Scenes/Gameplay/MenuScene.unity");
            yield return new EnterPlayMode(); Application.runInBackground = true; yield return null;
            Assert.That(Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None).Count(p => p.GetType().Name == "CoreGameplayController"), Is.Zero);
            Button("StartNewGame").onClick.Invoke(); Button("StartNewGame").onClick.Invoke();
            yield return WaitForScene("CoreGameplay");
            yield return null;
            var owner = Owner(); var first = Get<TaskSimulation>(owner, "Simulation");
            var initialFunds = Get<int>(owner, "Funds");
            var wallet = Get<object>(owner, "Economy");
            Call(wallet, "TrySpendFunds", 100);
            Assert.That(Get<int>(owner, "Funds"), Is.EqualTo(initialFunds - 100));
            Button("ReturnToMenu").onClick.Invoke(); Button("ReturnToMenu").onClick.Invoke();
            yield return WaitForScene("MenuScene");
            yield return null;
            Assert.That(Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None).Count(p => p.GetType().Name == "CoreGameplayController"), Is.Zero);
            Button("StartNewGame").onClick.Invoke();
            yield return WaitForScene("CoreGameplay");
            yield return null;
            owner = Owner();
            Assert.That(Get<TaskSimulation>(owner, "Simulation"), Is.Not.SameAs(first));
            Assert.That(Get<object>(owner, "Economy"), Is.Not.SameAs(wallet));
            Assert.That(Get<int>(owner, "Funds"), Is.EqualTo(initialFunds));
            Assert.That(Object.FindObjectsByType<UnityEngine.EventSystems.EventSystem>(FindObjectsSortMode.None).Length, Is.EqualTo(1));
            Call(owner, "ResetSimulation"); Call(owner, "ResetSimulation");
            var simulation = Get<TaskSimulation>(owner, "Simulation");
            Assert.That(simulation.GameTime, Is.Zero); Assert.That(simulation.Paused, Is.True);
            Assert.That(simulation.Tasks.Count, Is.EqualTo(3));
            Assert.That(Get<int>(owner, "Funds"), Is.EqualTo(initialFunds));
            var progression = owner.GetComponents<MonoBehaviour>().Single(p => p.GetType().Name == "ProgressionController");
            Call(progression, "EnsureBound");
            Call(progression, "TryPurchase", "Writing-1"); Call(progression, "TryPurchase", "Writing-1");
            Assert.That(Get<int>(owner, "Funds"), Is.EqualTo(Get<bool>(owner, "TrainingEnabled") ? initialFunds : initialFunds - 100));
            yield return new ExitPlayMode();
        }

        [UnityTest]
        public IEnumerator ImportedCStoryRunsRealSubmissionAndNextSessionWithoutExtraRewards()
        {
            EditorSceneManager.OpenScene("Assets/_Game/Scenes/Prototypes/CoreGameplay.unity");
            yield return new EnterPlayMode(); Application.runInBackground = true; yield return null;
            var owner = Owner(); var simulation = Get<TaskSimulation>(owner, "Simulation");
            var story = Get<object>(owner, "Story");
            var field=owner.GetType().GetField("configuration",BindingFlags.NonPublic|BindingFlags.Instance);
            var fixture=Object.Instantiate((ScriptableObject)field.GetValue(owner));
            fixture.GetType().GetField("OrdinaryDialoguePool").SetValue(fixture,null);
            var sourceStory=Object.Instantiate((ScriptableObject)fixture.GetType().GetField("Story").GetValue(fixture));
            MakeScripted(sourceStory);fixture.GetType().GetField("Story").SetValue(fixture,sourceStory);
            field.SetValue(owner,fixture);Call(owner,"ResetSimulation");
            simulation=Get<TaskSimulation>(owner,"Simulation");story=Get<object>(owner,"Story");
            Assert.That(Call(story, "Name", "C"), Is.EqualTo("苏禾"));
            Call(owner, "SelectRequest", "C-1");
            var task = simulation.Tasks["C-1"];
            Assert.That(task.Definition.Calculation, Is.EqualTo(30));
            Assert.That(task.Definition.Speed, Is.EqualTo(10));
            Assert.That(task.Definition.Shape.Count, Is.EqualTo(4));
            // Avoid a captured-local display class spanning EnterPlayMode's domain reload.
            var origin = FirstLegalOrigin(simulation, "C-1");
            Assert.That(simulation.TryPlace("C-1", origin, 0, out _), Is.True);
            simulation.Paused = false; Call(owner, "AdvanceGame", CompletionSeconds(simulation, "C-1") + .01d); simulation.Paused = true;
            int funds = Get<int>(owner, "Funds"); double time = simulation.GameTime;
            Call(owner, "SubmitSelected");
            funds = Get<int>(owner, "Funds"); // Reply itself must not add any C-source reward.
            Assert.That(Get<bool>(story, "IsReply"), Is.True);
            Assert.That(Get<string>(story, "UserId"), Is.EqualTo("C"));
            Assert.That(Call(story, "History", "C").ToString(), Does.Contain("初次问候"));
            int revision = Get<int>(story, "Revision");
            Call(owner, "ChooseReplyAtRevision", 0, revision);
            Call(owner, "ChooseReplyAtRevision", 0, revision);
            Assert.That(Get<int>(owner, "Funds"), Is.EqualTo(funds));
            Assert.That(simulation.GameTime, Is.EqualTo(time)); Assert.That(simulation.Paused, Is.True);
            Call(owner, "NextRound");
            Assert.That(Get<string>(owner, "SelectedId"), Is.Not.EqualTo("C-1"));
            Assert.That(simulation.Tasks[Get<string>(owner, "SelectedId")].ConversationId, Is.EqualTo("C"));
            yield return new ExitPlayMode();
        }

        [Serializable] private sealed class ApiInput
        {
            public string kind,id,recipe,architecture,displayName;
            public string[] corpusIds;
            public int x,y,rotation,choice,storyRevision=-1;
            public double seconds;
            public bool paused;
            public long expectedVersion=-1;
        }
        private static void Api(object owner, ApiInput input)
        {
            string result=(string)Call(owner,"ExecuteCommandJson",JsonUtility.ToJson(input));
            Assert.That(result, Does.Contain("\"ok\":true"), input.kind+": "+result);
            TestContext.WriteLine(input.kind+" "+result);
        }
        private static void ApiPlace(object owner, TaskSimulation simulation, string id)
        {
            var snapshot=JsonUtility.FromJson<SnapshotProbe>((string)Call(owner,"QueryGameJson"));
            Assert.That(snapshot.openCells.Length,Is.GreaterThan(0));
            foreach(var cell in snapshot.openCells)
                if ((string)Call(owner,"PreviewPlacement",id,cell.x,cell.y,0)=="None")
                { Api(owner,new ApiInput {kind="place",id=id,x=cell.x,y=cell.y}); return; }
            Assert.Fail("Query and PreviewPlacement found no legal cell for "+id);
        }
        [Serializable] private sealed class CellProbe { public int x,y; }
        [Serializable] private sealed class SnapshotProbe { public long version; public CellProbe[] openCells; }
        private static int observedEvents;
        private static void ThrowingObserver(TaskEvent _) { throw new InvalidOperationException("Deliberate observer regression fixture"); }
        private static void CountingObserver(TaskEvent _) { observedEvents++; }
        private static void ApiAdvance(object owner,double seconds)
        {
            Api(owner,new ApiInput {kind="pause",paused=false});
            Api(owner,new ApiInput {kind="advance",seconds=seconds});
            Api(owner,new ApiInput {kind="pause",paused=true});
        }
        [UnityTest]
        public IEnumerator DefaultOpeningDialogueTrainsModelAndSwitchPreservesTasksWithoutUserRewards()
        {
            EditorSceneManager.OpenScene("Assets/_Game/Scenes/Prototypes/CoreGameplay.unity");
            yield return new EnterPlayMode(); Application.runInBackground = true; yield return null;
            var owner = Owner();
            try
            {
                Call(owner, "ResetSimulation");
                Api(owner,new ApiInput {kind="clock",paused=true});
                observedEvents=0;
                owner.GetType().GetEvent("TaskEventRaised").AddEventHandler(owner,(Action<TaskEvent>)ThrowingObserver);
                owner.GetType().GetEvent("TaskEventRaised").AddEventHandler(owner,(Action<TaskEvent>)CountingObserver);
                Assert.That(Get<bool>(owner, "TrainingEnabled"), Is.True);
                var simulation = Get<TaskSimulation>(owner, "Simulation");
                Assert.That(simulation.Model.Points[AbilityKind.Writing], Is.EqualTo(1));
                var training = Get<object>(owner, "Training"); var session = Get<object>(training, "Session");
                double firstSeconds = CompletionSeconds(simulation, "C-1");
                Assert.That(simulation.Tasks["C-1"].Definition.Patience - firstSeconds, Is.GreaterThanOrEqualTo(80), "Opening leaves time for placement and reading.");
                ApiPlace(owner,simulation,"C-1");
                Call(owner, "SelectRequest", "C-1");
                ApiAdvance(owner,CompletionSeconds(simulation,"C-1")+.01d);
                Assert.That(simulation.Tasks["C-1"].Result.Overall, Is.EqualTo(Quality.Excellent));
                Api(owner,new ApiInput {kind="submit",id="C-1"});
                var story = Get<object>(owner, "Story"); int revision = Get<int>(story, "Revision");
                Api(owner,new ApiInput {kind="reply",choice=0,storyRevision=revision});
                string stale=(string)Call(owner,"ExecuteCommandJson",JsonUtility.ToJson(new ApiInput {kind="reply",choice=0,storyRevision=revision}));
                Assert.That(stale,Does.Contain("StaleStoryRevision"));
                Assert.That(Items(session, "Corpus").Length, Is.EqualTo(1));
                string duplicate=(string)Call(owner,"ExecuteCommandJson",JsonUtility.ToJson(new ApiInput {kind="submit",id="C-1"}));
                Assert.That(duplicate,Does.Contain("InvalidState"));
                int funds = Get<int>(owner, "Funds"), completed = Get<int>(owner, "CompletedTasks");
                var recipe = Items(session, "Recipes")[0]; var architecture = Items(session, "Architectures")[0];
                var corpus = Items(session, "Corpus")[0];
                Api(owner,new ApiInput {kind="train",recipe=Get<string>(recipe,"Id"),architecture=Get<string>(architecture,"Id"),displayName="语料模型验证",corpusIds=new[]{Get<string>(corpus,"Id")}});
                int cost = Get<int>(recipe, "Cost");
                Assert.That(funds - cost, Is.GreaterThanOrEqualTo(1000), "First training does not consume the operating reserve.");
                Assert.That(Get<int>(owner, "Funds"), Is.EqualTo(funds - cost));
                var job = Items(session, "Jobs")[0]; string jobId = Get<string>(job, "RequestId");
                TestContext.WriteLine("Default opening: first-order seconds=" + firstSeconds + "; patience=" + simulation.Tasks["C-1"].Definition.Patience
                    + "; training seconds=" + CompletionSeconds(simulation, jobId) + "; training cost=" + cost + "; funds remaining=" + Get<int>(owner,"Funds"));
                ApiPlace(owner,simulation,jobId); ApiPlace(owner,simulation,"A-1");
                ApiAdvance(owner,CompletionSeconds(simulation,jobId)+.01d);
                var ready = simulation.Tasks["A-1"]; Assert.That(ready.State, Is.EqualTo(RequestState.Ready));
                var frozenResult = ready.Result; var frozenFinished = ready.FinishedAt;
                Api(owner,new ApiInput {kind="submit",id=jobId}); yield return null;
                Assert.That(Items(session, "Models").Length, Is.EqualTo(2));
                Assert.That(Get<int>(owner, "CompletedTasks"), Is.EqualTo(completed));
                Assert.That(Get<int>(owner, "Funds"), Is.EqualTo(funds - cost));
                Assert.That(Get<bool>(owner, "IsStoryOpen"), Is.False);
                Assert.That(Call(training, "CompleteTraining", jobId).ToString(), Is.EqualTo("AlreadyCompleted"));
                var definition = new TaskDefinition("hold", "切换保留验证", new[] { new Cell(0, 0) }, 1000, 10, 1000, 1000, Array.Empty<AbilityRequirement>());
                Assert.That(simulation.TryCreate("switch-running", "switch-user", "1", definition, out _), Is.True);
                ApiPlace(owner,simulation,"switch-running");
                Api(owner,new ApiInput {kind="pause",paused=false});
                var continuousBefore=JsonUtility.FromJson<SnapshotProbe>((string)Call(owner,"QueryGameJson"));
                Api(owner,new ApiInput {kind="advance",seconds=1d});
                var continuousAfter=JsonUtility.FromJson<SnapshotProbe>((string)Call(owner,"QueryGameJson"));
                Assert.That(continuousAfter.version,Is.EqualTo(continuousBefore.version),"Clock/progress alone must not invalidate discrete versions.");
                Api(owner,new ApiInput {kind="pause",paused=true});
                string staleVersion=(string)Call(owner,"ExecuteCommandJson",JsonUtility.ToJson(new ApiInput {kind="query",expectedVersion=continuousBefore.version}));
                Assert.That(staleVersion,Does.Contain("StaleVersion"));
                double progress = simulation.Tasks["switch-running"].Progress, time = simulation.GameTime;
                var output = Get<object>(job, "Output");
                Api(owner,new ApiInput {kind="selectModel",id=Get<string>(output,"Id")});
                Api(owner,new ApiInput {kind="renameModel",id=Get<string>(output,"Id"),displayName="首轮模型"});
                Assert.That(simulation.Occupancy.Count, Is.Zero);
                Assert.That(ready.State, Is.EqualTo(RequestState.Ready)); Assert.That(ready.ReadyInCandidate, Is.True);
                Assert.That(ready.Result, Is.SameAs(frozenResult)); Assert.That(ready.FinishedAt, Is.EqualTo(frozenFinished));
                Assert.That(simulation.Tasks["switch-running"].State, Is.EqualTo(RequestState.Withdrawn));
                Assert.That(simulation.Tasks["switch-running"].Progress, Is.EqualTo(progress));
                Assert.That(simulation.GameTime, Is.EqualTo(time)); Assert.That(simulation.Paused, Is.True);
                var beforePoints = simulation.Model.Points[AbilityKind.Writing];
                double beforeRate = simulation.TokenThroughput;
                Api(owner,new ApiInput {kind="upgradeCompute"});
                Assert.That(simulation.TokenThroughput, Is.GreaterThan(beforeRate));
                Assert.That(simulation.Model.Points[AbilityKind.Writing], Is.EqualTo(beforePoints));
                Button("OpenTraining").onClick.Invoke();
                Assert.That(Get<bool>(owner, "IsModalOpen"), Is.True);
                string longName=new string('猫',40);
                Api(owner,new ApiInput {kind="renameModel",id=Get<string>(output,"Id"),displayName=longName});
                yield return null;Canvas.ForceUpdateCanvases();
                var modelLabels=Object.FindObjectsByType<Text>(FindObjectsInactive.Include,FindObjectsSortMode.None).Where(t=>t.name=="ModelName").ToArray();
                Assert.That(modelLabels.Length,Is.EqualTo(2));var longLabel=modelLabels.Single(t=>t.text==longName);
                Assert.That(longLabel.preferredHeight,Is.LessThanOrEqualTo(longLabel.rectTransform.rect.height+1),"Maximum-length model names must not be cut off.");
                var search=Object.FindObjectsByType<InputField>(FindObjectsInactive.Include,FindObjectsSortMode.None).Single(f=>f.name=="ModelSearch");
                search.text="no-model-matches-this-search";yield return null;
                Assert.That(GameObject.Find("Cards").transform.Cast<Transform>().Count(t=>t.name=="ModelCard"),Is.Zero);
                search.text=longName;yield return null;
                Assert.That(GameObject.Find("Cards").transform.Cast<Transform>().Count(t=>t.name=="ModelCard"),Is.EqualTo(1));
                Assert.That(Object.FindObjectsByType<Text>(FindObjectsInactive.Include,FindObjectsSortMode.None).Any(t=>t.name=="ModelUuid" && t.text==Get<string>(output,"Id") && t.alignment==TextAnchor.LowerRight),Is.True,"Full UUID remains quietly visible on the card.");
                Assert.That(observedEvents,Is.GreaterThan(3),"Throwing observer must not prevent later notifications or settlement.");
                Call(owner, "AdvanceGame", 20d); Assert.That(simulation.GameTime, Is.EqualTo(time));
                var panel = owner.GetComponents<MonoBehaviour>().Single(p => p.GetType().Name == "TrainingPanel"); Call(panel, "Close");
            }
            finally
            {
                owner.GetType().GetEvent("TaskEventRaised").RemoveEventHandler(owner,(Action<TaskEvent>)ThrowingObserver);
                owner.GetType().GetEvent("TaskEventRaised").RemoveEventHandler(owner,(Action<TaskEvent>)CountingObserver);
                Call(owner, "ResetSimulation");
            }
            yield return new ExitPlayMode();
        }

        [UnityTest]
        public IEnumerator NormalApiDiskSaveRestoresRenamedUuidAndMenuContinueWithoutReplayingRewards()
        {
            EditorSceneManager.OpenScene("Assets/_Game/Scenes/Prototypes/CoreGameplay.unity");
            yield return new EnterPlayMode(); Application.runInBackground=true; yield return null;
            var owner=Owner(); Call(owner,"ResetSimulation");
            string slot="api-qa-"+Guid.NewGuid().ToString("N"); Call(owner,"ConfigureSaveSlot",slot);
            Api(owner,new ApiInput{kind="clock",paused=true});
            var simulation=Get<TaskSimulation>(owner,"Simulation");
            ApiPlace(owner,simulation,"C-1");ApiAdvance(owner,CompletionSeconds(simulation,"C-1")+.01d);
            Api(owner,new ApiInput{kind="submit",id="C-1"});
            Api(owner,new ApiInput{kind="reply",choice=0,storyRevision=Get<int>(Get<object>(owner,"Story"),"Revision")});
            var session=Get<object>(Get<object>(owner,"Training"),"Session");
            var recipe=Items(session,"Recipes")[0];var arch=Items(session,"Architectures")[0];var corpus=Items(session,"Corpus")[0];
            Api(owner,new ApiInput{kind="train",recipe=Get<string>(recipe,"Id"),architecture=Get<string>(arch,"Id"),displayName="存档训练模型",corpusIds=new[]{Get<string>(corpus,"Id")}});
            string jobId=Get<string>(Items(session,"Jobs")[0],"RequestId");
            ApiPlace(owner,simulation,jobId);ApiAdvance(owner,3d);
            double savedProgress=simulation.Tasks[jobId].Progress;
            Api(owner,new ApiInput{kind="save"});Api(owner,new ApiInput{kind="restore"});
            simulation=Get<TaskSimulation>(owner,"Simulation");session=Get<object>(Get<object>(owner,"Training"),"Session");
            Assert.That(simulation.Tasks[jobId].Progress,Is.EqualTo(savedProgress));Assert.That(simulation.Tasks[jobId].State,Is.EqualTo(RequestState.Running));
            ApiAdvance(owner,CompletionSeconds(simulation,jobId)-3d+.01d);Api(owner,new ApiInput{kind="submit",id=jobId});
            string modelId=Get<string>(Get<object>(Items(session,"Jobs")[0],"Output"),"Id");
            Api(owner,new ApiInput{kind="selectModel",id=modelId});Api(owner,new ApiInput{kind="renameModel",id=modelId,displayName="恢复后的猫"});
            int funds=Get<int>(owner,"Funds"),completed=Get<int>(owner,"CompletedTasks");double time=simulation.GameTime;
            int revision=Get<int>(Get<object>(owner,"Story"),"Revision");
            Api(owner,new ApiInput{kind="save"});Api(owner,new ApiInput{kind="save"});
            string path=Get<string>(owner,"SavePath");Assert.That(path,Does.Contain(slot));Assert.That(File.Exists(path),Is.True);
            Assert.That(Directory.GetFiles(Path.GetDirectoryName(path),"run.json.backup-*").Length,Is.GreaterThan(0));
            Api(owner,new ApiInput{kind="renameModel",id=modelId,displayName="不应保留的改名"});Api(owner,new ApiInput{kind="restore"});
            session=Get<object>(Get<object>(owner,"Training"),"Session");
            Assert.That(Get<string>(session,"ActiveModelId"),Is.EqualTo(modelId));Assert.That(Get<string>(owner,"ModelName"),Is.EqualTo("恢复后的猫"));
            Assert.That(Get<int>(owner,"Funds"),Is.EqualTo(funds));Assert.That(Get<int>(owner,"CompletedTasks"),Is.EqualTo(completed));
            Assert.That(Get<TaskSimulation>(owner,"Simulation").GameTime,Is.EqualTo(time));Assert.That(Get<int>(Get<object>(owner,"Story"),"Revision"),Is.GreaterThan(revision));
            Api(owner,new ApiInput{kind="restore"});Assert.That(Get<int>(owner,"Funds"),Is.EqualTo(funds));
            // Corrupt only this unique QA slot. The original committed bytes remain in its atomic backup.
            File.WriteAllText(path,"{incomplete QA fixture");Api(owner,new ApiInput{kind="restore"});Assert.That(Get<int>(owner,"Funds"),Is.EqualTo(funds));
            Button("ReturnToMenu").onClick.Invoke();yield return WaitForScene("MenuScene");
            yield return null;
            var navigation=Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None).Single(m=>m.GetType().Name=="SceneNavigation");Call(navigation,"ContinueSlot",slot);
            yield return WaitForScene("CoreGameplay");
            yield return null;owner=Owner();
            Assert.That(Get<string>(owner,"ModelName"),Is.EqualTo("恢复后的猫"));Assert.That(Get<int>(owner,"Funds"),Is.EqualTo(funds));
            Assert.That(Get<string>(Get<object>(Get<object>(owner,"Training"),"Session"),"ActiveModelId"),Is.EqualTo(modelId));
            TestContext.WriteLine("QA slot="+slot+"; UUID="+modelId+"; funds="+funds+"; completed="+completed+"; time="+time+"; backup recovery and menu continue passed.");
            yield return new ExitPlayMode();
        }
        [UnityTest]
        public IEnumerator LocalDebugUsesSharedLifecycleWithoutClockOrDuplicateRewards()
        {
            EditorSceneManager.OpenScene("Assets/_Game/Scenes/Prototypes/CoreGameplay.unity");
            yield return new EnterPlayMode();Application.runInBackground=true;yield return null;
            var owner=Owner();Call(owner,"ResetSimulation");Api(owner,new ApiInput{kind="clock",paused=true});
            var simulation=Get<TaskSimulation>(owner,"Simulation");ApiPlace(owner,simulation,"C-1");ApiAdvance(owner,CompletionSeconds(simulation,"C-1")+.01d);
            Api(owner,new ApiInput{kind="submit",id="C-1"});Api(owner,new ApiInput{kind="reply",choice=0,storyRevision=Get<int>(Get<object>(owner,"Story"),"Revision")});
            var service=Get<DebugCommandService>(owner,"DebugCommands");var session=Get<object>(Get<object>(owner,"Training"),"Session");
            string corpusId=Get<string>(Items(session,"Corpus")[0],"Id");double time=simulation.GameTime;bool paused=simulation.Paused;
            Assert.That(service.Execute(DebugCommand.SetCorpus(corpusId,AbilityKind.Writing,4)).Status,Is.EqualTo(DebugStatus.Applied));
            var recipe=Items(session,"Recipes")[0];var arch=Items(session,"Architectures")[0];
            Api(owner,new ApiInput{kind="train",recipe=Get<string>(recipe,"Id"),architecture=Get<string>(arch,"Id"),displayName="仅调试验证",corpusIds=new[]{corpusId}});
            int funds=Get<int>(owner,"Funds"),completed=Get<int>(owner,"CompletedTasks");string jobId=Get<string>(Items(session,"Jobs")[0],"RequestId");
            Call(owner,"OpenTraining");yield return null;
            var trainingTab=Object.FindObjectsByType<Button>(FindObjectsInactive.Include,FindObjectsSortMode.None).Single(b=>b.name=="语料与训练");trainingTab.onClick.Invoke();yield return null;
            Assert.That(Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include,FindObjectsSortMode.None).Count(b=>b.GetType().Name=="LocalDebugFoldout"),Is.GreaterThanOrEqualTo(3));
            Assert.That(Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include,FindObjectsSortMode.None).Count(b=>b.GetType().BaseType?.Name=="InlineDebugControls"),Is.Zero,"Debug controls start folded.");
            Assert.That(service.Execute(DebugCommand.FinishTraining(jobId)).Status,Is.EqualTo(DebugStatus.Applied));
            Assert.That(service.Execute(DebugCommand.FinishTraining(jobId)).Status,Is.EqualTo(DebugStatus.InvalidState));
            Assert.That(Items(session,"Models").Length,Is.EqualTo(2));Assert.That(Get<int>(owner,"Funds"),Is.EqualTo(funds));Assert.That(Get<int>(owner,"CompletedTasks"),Is.EqualTo(completed));
            Assert.That(service.Execute(DebugCommand.SetCorpus(corpusId,AbilityKind.Writing,99)).Status,Is.EqualTo(DebugStatus.InvalidState));
            Assert.That(service.Execute(DebugCommand.OverrideQuality("B-1",Quality.Excellent)).Status,Is.EqualTo(DebugStatus.Applied));
            Assert.That(service.Execute(DebugCommand.FinishCalculation("B-1")).Status,Is.EqualTo(DebugStatus.Applied));
            Assert.That(simulation.Tasks["B-1"].State,Is.EqualTo(RequestState.Ready));Assert.That(simulation.Tasks["B-1"].ReadyInCandidate,Is.True);
            Assert.That(simulation.Tasks["B-1"].Result.Overall,Is.EqualTo(Quality.Excellent));Assert.That(simulation.GameTime,Is.EqualTo(time));Assert.That(simulation.Paused,Is.EqualTo(paused));
            Assert.That(Get<int>(owner,"Funds"),Is.EqualTo(funds));Assert.That(service.Execute(DebugCommand.AdvanceStage("progression",1)).Status,Is.EqualTo(DebugStatus.Unsupported));
            TestContext.WriteLine("Local debug: corpus edits freeze on training; instant finish uses shared Ready/CompleteTraining; no clock, customer reward, or stage substitution.");
            yield return new ExitPlayMode();
        }
        [UnityTearDown]
        public IEnumerator StopPlay() { if (EditorApplication.isPlaying) yield return new ExitPlayMode(); }
    }
}
