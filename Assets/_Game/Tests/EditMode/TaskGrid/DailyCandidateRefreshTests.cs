using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CallmeCatgirl.Gameplay.TaskGrid;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace CallmeCatgirl.Tests
{
    public sealed class DailyCandidateRefreshTests
    {
        private const string Core = "Assets/_Game/Scenes/Prototypes/CoreGameplay.unity";
        private static TaskDefinition Definition(string id) => new TaskDefinition(id,"Daily fixture",new[]{new Cell(0,0)},100000,1,5000,10,Array.Empty<AbilityRequirement>(),usesCalculationBudget:true);
        private static object Call(object target,string name,params object[] args) => target.GetType()
            .GetMethods(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)
            .Single(m=>m.Name==name && m.GetParameters().Length==args.Length).Invoke(target,args);
        private static T Value<T>(object target,string name)
        {
            var property=target.GetType().GetProperty(name);
            return (T)(property!=null?property.GetValue(target):target.GetType().GetField(name).GetValue(target));
        }
        private static object Field(object target,string name) => target.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(target);
        private static void Set(object target,string name,object value) => target.GetType().GetField(name).SetValue(target,value);
        private static void SetPrivate(object target,string name,object value) => target.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(target,value);
        private static MonoBehaviour Owner() => Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None).Single(c=>c.GetType().Name=="CoreGameplayController");
        private static TaskInstance Create(object owner,string id,string user=null,bool operating=false)
            => (TaskInstance)Call(owner,"CreateTask",id,user??id,"round-"+id,Definition(id),operating);
        private static void Place(TaskSimulation sim,TaskInstance task)
        {
            var position=Enumerable.Range(0,sim.Width*sim.Height).Select(i=>new Cell(i%sim.Width,i/sim.Width))
                .First(c=>sim.Preview(task.Id,c,0)==PlacementFailure.None);
            Assert.That(sim.TryPlace(task.Id,position,0,out _),Is.True);
        }
        private sealed class Arrivals
        {
            public readonly List<string> Ids=new List<string>();
            public void Record(TaskEvent e) { if(e.Kind==TaskEventKind.Arrived) Ids.Add(e.RequestId); }
        }
        [TestCase(false)] [TestCase(true)]
        public void DiscardCandidateReleasesAllIndexesWithoutTerminalEvents(bool withdrawn)
        {
            var sim=new TaskSimulation(2,1,new[]{new Cell(0,0),new Cell(1,0)},new ModelDefinition("fixture",1,new Dictionary<AbilityKind,double>()));
            Assert.That(sim.TryCreate("old","user","round",Definition("old"),out var old),Is.True);
            if(withdrawn) { Place(sim,old);sim.Advance(0.1);Assert.That(sim.TryWithdraw(old.Id),Is.True); }
            Assert.That(sim.TryDebugSetQualityOverride(old.Id,Quality.Excellent),Is.True);
            Assert.That(sim.TryDiscardCandidate(old.Id),Is.True);
            Assert.That(sim.Tasks,Does.Not.ContainKey(old.Id));Assert.That(sim.Occupancy,Is.Empty);
            Assert.That(sim.GetDebugQualityOverride(old.Id),Is.Null);Assert.That(sim.DrainEvents(),Is.Empty);
            Assert.That(sim.TryDiscardCandidate(old.Id),Is.False);
            Assert.That(sim.TryCreate("old","user","round",Definition("old"),out var reused),Is.True,"Every ID index must be released.");
            Place(sim,reused);Assert.That(sim.TryDiscardCandidate(reused.Id),Is.False,"Running work is protected.");
            sim.TryDebugFinishCalculation(reused.Id);Assert.That(sim.TryDiscardCandidate(reused.Id),Is.False,"Ready work is protected.");
            Assert.That(sim.TrySubmit(reused.Id),Is.True);Assert.That(sim.TryDiscardCandidate(reused.Id),Is.False,"Completed history is protected.");
            sim.DrainEvents();
            Assert.That(TaskSimulation.RestoreState(sim.CaptureState()).Tasks.Keys,Is.EquivalentTo(sim.Tasks.Keys));
        }

        [UnityTest] public IEnumerator DailyBudgetTurnoverProtectsWorkAndRoundTripsThroughTheRealSave()
        {
            EditorSceneManager.OpenScene(Core);yield return new EnterPlayMode();yield return null;
            var owner=Owner();Call(owner,"ConfigureSaveSlot","daily-refresh-"+Guid.NewGuid().ToString("N"));
            yield return MemoryInstallationTests.PrepareEligibleRun(owner);
            using(var fixture=new SceneFixture(owner))
            {
                // Qualify stage 2 through the existing installation transaction. This is a fixture, not a timed player-journey claim.
                for(int i=0;i<2;i++)
                {
                    Call(owner,"OpenMemoryUpgrade");Call(owner,"UpgradeMemory");MemoryUpgradeTestActions.Skip(owner);
                    Call(owner,"CloseMemoryUpgrade");yield return null;CloseNarrative(owner);yield return null;
                }
                Assert.That(Value<int>(owner,"MemoryLevel"),Is.EqualTo(2));
                fixture.SetCalendar();fixture.EnableArrivalMultiplier();
                var sim=Value<TaskSimulation>(owner,"Simulation");
                var training=Value<object>(owner,"Training");var session=Value<object>(training,"Session");
                Assert.That(Call(session,"GrantScriptedCorpus","daily-test-grant","daily-training",new Dictionary<AbilityKind,double>{{AbilityKind.Writing,1}},"daily test").ToString(),Is.EqualTo("Success"));
                string recipe=((IEnumerable)Value<object>(session,"Recipes")).Cast<object>().Select(r=>Value<string>(r,"Id")).First(id=>!id.StartsWith("tutorial-"));
                Assert.That(Call(training,"StartConversationTraining",recipe,"baseline","Protected training","daily-training",1d).ToString(),Is.EqualTo("Success"));
                string trainingId=Value<string>(((IEnumerable)Value<object>(session,"Jobs")).Cast<object>().Last(),"RequestId");
                var waiting=Create(owner,"daily-waiting");var withdrawn=Create(owner,"daily-withdrawn");
                var running=Create(owner,"daily-running",operating:true);var ready=Create(owner,"daily-ready",operating:true);
                var operating=Create(owner,"daily-operating",operating:true);
                var critical=Create(owner,"narrative-daily-protected","daily-critical");
                Assert.That(waiting,Is.Not.Null);Assert.That(withdrawn,Is.Not.Null);Assert.That(running,Is.Not.Null);Assert.That(ready,Is.Not.Null);Assert.That(critical,Is.Not.Null);
                Assert.That(running.Definition.UsesCalculationBudget,Is.True);Assert.That(running.Definition.Calculation,Is.GreaterThanOrEqualTo(100000));
                Place(sim,withdrawn);Assert.That(sim.TryWithdraw(withdrawn.Id),Is.True);
                Place(sim,running);Place(sim,ready);Assert.That(sim.TryDebugFinishCalculation(ready.Id),Is.True);
                Call(owner,"ReadEvents");Call(Field(owner,"canvasView"),"Refresh");yield return null;
                Assert.That(GameObject.Find("RequestCard_"+waiting.Id),Is.Not.Null,"The old candidate must actually be visible before turnover.");
                double affinity=Convert.ToDouble(Call(Value<object>(owner,"Story"),"Affinity",waiting.ConversationId));
                int funds=Value<int>(owner,"Funds"),completed=Value<int>(owner,"CompletedTasks");
                var arrivals=new Arrivals();owner.GetType().GetEvent("TaskEventRaised").AddEventHandler(owner,new Action<TaskEvent>(arrivals.Record));
                sim.Paused=false;Call(owner,"AdvanceGame",2d);yield return null;
                Assert.That(arrivals.Ids.Count,Is.EqualTo(1),"Configured daily count is exact even with arrival multiplier 3 and stage-2 filler targets.");
                Assert.That(sim.Tasks.ContainsKey(waiting.Id),Is.False);Assert.That(sim.Tasks.ContainsKey(withdrawn.Id),Is.False);Assert.That(sim.Tasks.ContainsKey(operating.Id),Is.False);
                Assert.That(GameObject.Find("RequestCard_"+waiting.Id),Is.Null,"Removed candidates must disappear from the Canvas, not only the simulation.");
                Assert.That(GameObject.Find("Conversation_"+waiting.Id),Is.Null,"The stale conversation row must disappear too.");
                Assert.That(sim.Tasks[running.Id].State,Is.EqualTo(RequestState.Running));Assert.That(sim.Tasks[ready.Id].State,Is.EqualTo(RequestState.Ready));
                Assert.That(sim.Tasks.ContainsKey(trainingId),Is.True);Assert.That(sim.Tasks.ContainsKey(critical.Id),Is.True);
                Assert.That(Convert.ToDouble(Call(Value<object>(owner,"Story"),"Affinity",waiting.ConversationId)),Is.EqualTo(affinity));
                Assert.That(Value<int>(owner,"Funds"),Is.EqualTo(funds));Assert.That(Value<int>(owner,"CompletedTasks"),Is.EqualTo(completed));
                string dayTwo=arrivals.Ids.Single();Call(owner,"AdvanceGame",4d);yield return null;
                Assert.That(arrivals.Ids.Count,Is.EqualTo(3),"A large frame crosses two more days, including the month boundary, exactly once each.");
                Assert.That(Value<int>(owner,"CurrentMonth"),Is.EqualTo(2));Assert.That(Value<int>(owner,"CurrentDay"),Is.EqualTo(1));
                Assert.That(sim.Tasks.ContainsKey(dayTwo),Is.False);Assert.That(sim.Tasks.ContainsKey(arrivals.Ids.Last()),Is.True);
                int beforeTopUp=sim.Tasks.Count;Call(owner,"EnsureVisitorQueue");Call(owner,"ReadEvents");Assert.That(sim.Tasks.Count,Is.EqualTo(beforeTopUp),"An upgrade-era queue top-up cannot bypass the daily budget.");
                sim.Paused=true;
                Assert.That(Call(owner,"SaveRun"),Is.EqualTo(true),Value<string>(owner,"Message"));
                Assert.That(Call(owner,"ContinueRun"),Is.EqualTo(true),Value<string>(owner,"Message"));yield return null;
                var restored=Value<TaskSimulation>(owner,"Simulation");
                Assert.That(restored.Tasks.ContainsKey(waiting.Id),Is.False);Assert.That(restored.Tasks.ContainsKey(withdrawn.Id),Is.False);
                Assert.That(restored.Tasks.ContainsKey(trainingId),Is.True);Assert.That(restored.Tasks[running.Id].State,Is.EqualTo(RequestState.Running));
                Assert.That(restored.Tasks[ready.Id].State,Is.EqualTo(RequestState.Ready));Assert.That(restored.Tasks.ContainsKey(critical.Id),Is.True);
                Assert.That(Value<int>(owner,"Funds"),Is.EqualTo(funds));
                fixture.SetDailyCount(2);restored.Paused=false;Call(owner,"AdvanceGame",2d);yield return null;
                Assert.That(ArrivalsAtNow(Value<TaskSimulation>(owner,"Simulation")),Is.EqualTo(2),"The daily total remains configurable.");
            }
            yield return new ExitPlayMode();
        }
        private static int ArrivalsAtNow(TaskSimulation sim) => sim.Tasks.Values.Count(t=>Math.Abs(t.ArrivedAt-sim.GameTime)<1e-8);
        private static void CloseNarrative(object owner)
        {
            var narrative=Value<object>(owner,"Narrative");
            for(int i=0;i<20;i++)
            {
                var frame=Value<object>(narrative,"CurrentFrame");if(frame==null)return;
                if(Value<bool>(frame,"CanClose") && (bool)Call(narrative,"CloseConversation"))return;
                var choices=((IEnumerable)Value<object>(frame,"Choices")).Cast<object>().ToArray();
                Assert.That(choices,Is.Not.Empty);Assert.That(Call(narrative,"Choose",Value<string>(choices[0],"Id")),Is.EqualTo(true));
            }
            Assert.Fail("Narrative did not expose a closeable frame.");
        }
        private sealed class SceneFixture : IDisposable
        {
            private readonly object owner,policy;
            private readonly ScriptableObject original,config,generation,originalPolicy,policyConfig;
            public SceneFixture(object owner)
            {
                this.owner=owner;original=(ScriptableObject)Field(owner,"configuration");config=Object.Instantiate(original);
                generation=Object.Instantiate((ScriptableObject)config.GetType().GetField("TaskGeneration").GetValue(config));
                Set(config,"TaskGeneration",generation);Set(generation,"RefreshByDay",true);Set(generation,"DaysPerRefresh",1);Set(generation,"RequestsPerRefresh",1);
                var shapeType=generation.GetType().GetNestedType("ShapePreset");var shape=Activator.CreateInstance(shapeType);Set(shape,"Pattern",new[]{"#"});
                var shapes=Array.CreateInstance(shapeType,1);shapes.SetValue(shape,0);Set(generation,"Shapes",shapes);
                SetPrivate(owner,"configuration",config);SetPrivate(owner,"saveContentRevision",null);
                policy=((MonoBehaviour)owner).GetComponents<MonoBehaviour>().Single(c=>c.GetType().Name=="ProgressionController");
                originalPolicy=(ScriptableObject)Field(policy,"configuration");policyConfig=Object.Instantiate(originalPolicy);
                Call(Value<object>(owner,"Economy"),"AddFunds",5000);
            }
            public void SetCalendar()
            {
                Set(config,"SecondsPerGameDay",2f);Set(config,"DaysPerGameMonth",3);Set(config,"MonthlyMaintenanceFee",0);
                SetPrivate(owner,"monthTimer",0d);owner.GetType().GetProperty("CurrentMonth").SetValue(owner,1);
                SetPrivate(owner,"saveContentRevision",null);
            }
            public void SetDailyCount(int count){Set(generation,"RequestsPerRefresh",count);SetPrivate(owner,"saveContentRevision",null);}
            public void EnableArrivalMultiplier()
            {
                var nodes=(Array)policyConfig.GetType().GetField("Nodes").GetValue(policyConfig);
                var foundation=nodes.Cast<object>().Single(n=>(string)n.GetType().GetField("Id").GetValue(n)=="policy-foundations");
                Set(foundation,"Cost",0);var effects=foundation.GetType().GetField("Effects").GetValue(foundation);Set(effects,"CustomerArrivalMultiplier",3d);
                SetPrivate(policy,"configuration",policyConfig);SetPrivate(policy,"boundSimulation",null);Call(policy,"EnsureBound");
                Assert.That(Call(policy,"TryPurchase","policy-foundations").ToString(),Is.EqualTo("Success"));
                Assert.That(Value<double>(Value<object>(Value<object>(policy,"Tree"),"Effects"),"CustomerArrivalMultiplier"),Is.EqualTo(3d));
            }
            public void Dispose()
            {
                SetPrivate(owner,"configuration",original);SetPrivate(policy,"configuration",originalPolicy);
                Object.DestroyImmediate(config);Object.DestroyImmediate(generation);Object.DestroyImmediate(policyConfig);
            }
        }
        [UnityTearDown] public IEnumerator Cleanup(){if(EditorApplication.isPlaying)yield return new ExitPlayMode();}
    }
}
