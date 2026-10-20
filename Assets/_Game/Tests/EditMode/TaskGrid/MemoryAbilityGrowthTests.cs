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
using Object = UnityEngine.Object;

namespace CallmeCatgirl.Tests
{
    // Scene integration checks. Model abilities come from training, never memory installation.
    public sealed class MemoryAbilityGrowthTests
    {
        private const string Core = "Assets/_Game/Scenes/Prototypes/CoreGameplay.unity";
        private static MonoBehaviour Owner() => Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None)
            .Single(o => o.GetType().Name == "CoreGameplayController");
        private static object Call(object o,string name,params object[] args) => o.GetType().GetMethod(name).Invoke(o,args);
        private static T Value<T>(object o,string name)
        {
            var property=o.GetType().GetProperty(name);
            return (T)(property!=null?property.GetValue(o):o.GetType().GetField(name).GetValue(o));
        }
        private static object Field(object o,string name) => o.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(o);
        private static void Set(object o,string name,object value) => o.GetType().GetField(name).SetValue(o,value);
        private static Dictionary<AbilityKind,double> Points(ModelDefinition model) => model.Points.ToDictionary(p=>p.Key,p=>p.Value);
        private static IEnumerator EligibleRun()
        {
            Call(Owner(),"ConfigureSaveSlot","memory-no-allocation-"+Guid.NewGuid().ToString("N"));
            yield return MemoryInstallationTests.PrepareEligibleRun(Owner());
        }
        private static void AssertNoAllocationControls()
        {
            var names=Object.FindObjectsByType<RectTransform>(FindObjectsInactive.Include,FindObjectsSortMode.None)
                .Select(t=>t.name).ToArray();
            foreach(string name in new[]{"AbilityPointsLabel","AllocateWritingButton","AllocateCodeButton","AllocateReasoningButton","AllocateRetrievalButton"})
                Assert.That(names,Does.Not.Contain(name),"The authored scene must not retain obsolete allocation controls: "+name);
        }
        [Test] public void ConfigurationAndControllerExposeNoAbilityAllocation()
        {
            var step=Type.GetType("CallmeCatgirl.Gameplay.MemoryUpgradeConfiguration+UpgradeStep, Assembly-CSharp",true);
            Assert.That(step.GetField("AbilityPoints"),Is.Null,"No public reward configuration remains.");
            Assert.That(step.GetField("AbilityGains"),Is.Null);Assert.That(step.GetField("GrowthAbility"),Is.Null);
            var legacy=step.GetField("AbilityPoints",BindingFlags.Instance|BindingFlags.NonPublic);
            Assert.That(legacy,Is.Not.Null,"Retain the ignored historical JSON container for strict content revisions.");
            Assert.That(Attribute.IsDefined(legacy,typeof(SerializeField)),Is.True);
            Assert.That(Attribute.IsDefined(legacy,typeof(HideInInspector)),Is.True);
            var controller=Type.GetType("CallmeCatgirl.Gameplay.CoreGameplayController, Assembly-CSharp",true);
            Assert.That(controller.GetProperty("AvailableAbilityPoints"),Is.Null);
            Assert.That(controller.GetMethod("AllocateAbility"),Is.Null);
            Assert.That(controller.GetProperty("CanAllocateAbility"),Is.Null);
        }
        [UnityTest] public IEnumerator MemoryUpgradeSceneContainsNoAbilityAllocationControls()
        {
            EditorSceneManager.OpenScene(Core);yield return new EnterPlayMode();yield return null;
            AssertNoAllocationControls();yield return new ExitPlayMode();
        }
        [UnityTest] public IEnumerator SuccessfulInstallationExpandsMemoryWithoutChangingTrainedAbilities()
        {
            EditorSceneManager.OpenScene(Core);yield return new EnterPlayMode();yield return null;
            yield return EligibleRun();
            var owner=Owner();var sim=Value<TaskSimulation>(owner,"Simulation");
            var before=Points(sim.Model);string modelId=sim.Model.Id;
            var session=Value<object>(Value<object>(owner,"Training"),"Session");
            string active=Value<string>(session,"ActiveModelId");
            int funds=Value<int>(owner,"Funds"),level=Value<int>(owner,"MemoryLevel"),cells=sim.OpenCellCount;
            int cost=Value<int>(Value<object>(owner,"NextMemoryUpgrade"),"Cost");
            Call(owner,"OpenMemoryUpgrade");Call(owner,"UpgradeMemory");
            Assert.That(sim.Model.Points,Is.EquivalentTo(before));Assert.That(Value<int>(owner,"Funds"),Is.EqualTo(funds));
            MemoryUpgradeTestActions.Skip(owner);yield return null;
            Assert.That(Value<int>(owner,"MemoryLevel"),Is.EqualTo(level+1));Assert.That(sim.OpenCellCount,Is.GreaterThan(cells));
            Assert.That(Value<int>(owner,"Funds"),Is.EqualTo(funds-cost));
            Assert.That(sim.Model.Id,Is.EqualTo(modelId));Assert.That(sim.Model.Points,Is.EquivalentTo(before));
            Assert.That(Value<string>(session,"ActiveModelId"),Is.EqualTo(active));
            Assert.That(Value<ModelDefinition>(Value<object>(session,"ActiveModel"),"Definition").Points,Is.EquivalentTo(before));
            AssertNoAllocationControls();yield return new ExitPlayMode();
        }
        [UnityTest] public IEnumerator UnaffordableInstallationDoesNotChangeMemoryMoneyOrModelAbilities()
        {
            EditorSceneManager.OpenScene(Core);yield return new EnterPlayMode();yield return null;
            yield return EligibleRun();
            var owner=Owner();var sim=Value<TaskSimulation>(owner,"Simulation");var before=Points(sim.Model);
            var economy=Value<object>(owner,"Economy");int funds=Value<int>(owner,"Funds");
            Assert.That(Call(economy,"TrySpendFunds",funds),Is.EqualTo(true));
            int cells=sim.OpenCellCount,level=Value<int>(owner,"MemoryLevel");
            Call(owner,"OpenMemoryUpgrade");Call(owner,"UpgradeMemory");
            Assert.That(Value<bool>(owner,"IsMemoryInstallPending"),Is.False);Assert.That(Value<bool>(owner,"IsMemoryInstallOpen"),Is.False);
            Assert.That(Value<int>(owner,"Funds"),Is.Zero);Assert.That(Value<int>(owner,"MemoryLevel"),Is.EqualTo(level));
            Assert.That(sim.OpenCellCount,Is.EqualTo(cells));Assert.That(sim.Model.Points,Is.EquivalentTo(before));
            yield return new ExitPlayMode();
        }
        [UnityTest] public IEnumerator InstallationPreservesRunningTaskProgressAbilitiesAndCycleRemainder()
        {
            EditorSceneManager.OpenScene(Core);yield return new EnterPlayMode();yield return null;
            yield return EligibleRun();
            var owner=Owner();var sim=Value<TaskSimulation>(owner,"Simulation");var before=Points(sim.Model);
            var definition=new TaskDefinition("memory-upgrade-work","Memory upgrade work",new[]{new Cell(0,0)},1000,1,1000,0,Array.Empty<AbilityRequirement>());
            Assert.That(sim.TryCreate("memory-work","memory-work","one",definition,out var task),Is.True);
            Place(sim,task.Id);double cycle=sim.Model.Interval/sim.Architecture.AbilityRateMultiplier;
            sim.Paused=false;sim.Advance(cycle*.25);
            double time=sim.GameTime,progress=task.Progress,remainder=task.AbilityRemainder;var origin=task.Origin;
            var abilities=task.Abilities.ToDictionary(p=>p.Key,p=>p.Value);
            Call(owner,"OpenMemoryUpgrade");Call(owner,"UpgradeMemory");MemoryUpgradeTestActions.Skip(owner);
            Assert.That(sim.Paused,Is.False);Assert.That(sim.GameTime,Is.EqualTo(time));Assert.That(task.Progress,Is.EqualTo(progress));
            Assert.That(task.AbilityRemainder,Is.EqualTo(remainder));Assert.That(task.Abilities,Is.EquivalentTo(abilities));Assert.That(task.Origin,Is.EqualTo(origin));
            Assert.That(sim.Model.Points,Is.EquivalentTo(before));
            Call(owner,"AdvanceGame",10d);Assert.That(sim.GameTime,Is.EqualTo(time),"The upgrade modal still owns its clock pause.");
            Call(owner,"CloseMemoryUpgrade");sim.Advance(cycle*.75);sim.Paused=true;
            Assert.That(task.Progress,Is.GreaterThan(progress));
            Assert.That(task.GetAbility(AbilityKind.Writing),Is.EqualTo(before[AbilityKind.Writing]));
            Assert.That(task.AbilityRemainder,Is.EqualTo(0).Within(1e-8));
            yield return new ExitPlayMode();
        }
        private static void Place(TaskSimulation sim,string id)
        {
            for(int y=0;y<sim.Height;y++)for(int x=0;x<sim.Width;x++)
                if(sim.Preview(id,new Cell(x,y),0)==PlacementFailure.None)
                {Assert.That(sim.TryPlace(id,new Cell(x,y),0,out _),Is.True);return;}
            Assert.Fail("The qualified fixture must have a free cell for running work.");
        }
        [UnityTest] public IEnumerator HistoricalPointBalanceIsIgnoredOnRestoreAndNewCaptureWritesZero()
        {
            EditorSceneManager.OpenScene(Core);yield return new EnterPlayMode();yield return null;
            yield return EligibleRun();
            var owner=Owner();var before=Points(Value<TaskSimulation>(owner,"Simulation").Model);
            owner.GetType().GetMethod("EnsureSaveService",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(owner,null);
            var data=Call(owner,"CaptureRun");
            var extensions=(Array)Field(owner,"runExtensions");
            var states=Array.CreateInstance(data.GetType().GetField("Extensions").FieldType.GetElementType(),extensions.Length);
            for(int i=0;i<extensions.Length;i++)states.SetValue(Call(extensions.GetValue(i),"Capture"),i);
            Set(data,"Extensions",states);Set(Value<object>(data,"Host"),"AvailableAbilityPoints",37);
            data=JsonUtility.FromJson(JsonUtility.ToJson(data),data.GetType());
            Assert.That(Value<int>(Value<object>(data,"Host"),"AvailableAbilityPoints"),Is.EqualTo(37),"The legacy JSON field must actually be present in the test.");
            var prepared=Call(owner,"PrepareRestore",data);
            try{Call(prepared,"Commit");}finally{((IDisposable)prepared).Dispose();}
            Assert.That(Value<TaskSimulation>(owner,"Simulation").Model.Points,Is.EquivalentTo(before));
            Assert.That(Value<int>(Value<object>(Call(owner,"CaptureRun"),"Host"),"AvailableAbilityPoints"),Is.Zero);
            AssertNoAllocationControls();yield return new ExitPlayMode();
        }
        [UnityTearDown] public IEnumerator ExitIfNeeded(){if(EditorApplication.isPlaying)yield return new ExitPlayMode();}
    }
}
