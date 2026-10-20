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
    public sealed class TaskMultiplierTests
    {
        private static void SetMonthDuration(object config, float seconds)
        {
            config.GetType().GetField("DaysPerGameMonth").SetValue(config, 1);
            config.GetType().GetField("SecondsPerGameDay").SetValue(config, seconds);
        }

        private static Type MemoryType => Type.GetType("CallmeCatgirl.Gameplay.MemoryUpgradeConfiguration, Assembly-CSharp",true);
        private static object Call(object o,string method,params object[] args) => o.GetType().GetMethod(method).Invoke(o,args);
        private static T Value<T>(object o,string name) => (T)o.GetType().GetProperty(name).GetValue(o);
        private static object Field(object o,string name) => o.GetType().GetField(name).GetValue(o);
        private static void Set(object o,string name,object value) => o.GetType().GetField(name).SetValue(o,value);
        private static void Range(object settings,double a,double b,double c,double d)
        {Set(settings,"CalculationMin",a);Set(settings,"CalculationMax",b);Set(settings,"RewardMin",c);Set(settings,"RewardMax",d);}
        private static void Capture(string name) => typeof(SystemFeedbackTests).GetMethod("Capture",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,new object[]{"Multipliers"+name+".png"});
        private static TaskDefinition Basis(double tokens=100) => new TaskDefinition("A","basis",new[]{new Cell(0,0),new Cell(1,0)},100,10,90,tokens,
            new[]{new AbilityRequirement(AbilityKind.Writing,1,2)});
        [Test] public void LegacyDefaultsAreIdentityAndDoNotConsumeRandomSequence()
        {
            var memory=ScriptableObject.CreateInstance(MemoryType);
            try
            {
                var source=Basis();var random=new System.Random(47);double next=new System.Random(47).NextDouble();
                Assert.That(Call(memory,"ScaleTask",1,source,random),Is.SameAs(source));Assert.That(random.NextDouble(),Is.EqualTo(next));
                Set(memory,"InitialTaskMultipliers",null);foreach(object step in (Array)Field(memory,"Levels"))Set(step,"TaskMultipliers",null);
                Assert.That(Call(memory,"ScaleTask",5,source,random),Is.SameAs(source));
            }
            finally{Object.DestroyImmediate(memory);}
        }
        [Test] public void SeededRandomMultipliersStayInBoundsAndPairMoreWorkWithMoreReward()
        {
            var memory=ScriptableObject.CreateInstance(MemoryType);
            try
            {
                var settings=Field(memory,"InitialTaskMultipliers");Range(settings,1.1,1.3,1.4,1.8);
                var a=new System.Random(73);var b=new System.Random(73);var basis=Basis();var different=new HashSet<double>();
                for(int i=0;i<100;i++)
                {
                    var task=(TaskDefinition)Call(memory,"ScaleTask",1,basis,a);var same=(TaskDefinition)Call(memory,"ScaleTask",1,basis,b);
                    Assert.That(task.Calculation,Is.EqualTo(same.Calculation));Assert.That(task.Tokens,Is.EqualTo(same.Tokens));
                    Assert.That(task.Calculation,Is.InRange(109.999,130.001));Assert.That(task.Tokens,Is.InRange(139.999,180.001));
                    double x=(task.Calculation/100-(double)1.1)/((double)1.3-1.1);
                    double y=(task.Tokens/100-(double)1.4)/((double)1.8-1.4);
                    Assert.That(x,Is.EqualTo(y).Within(1e-9));different.Add(task.Calculation);
                    Assert.That(task.Shape,Is.EqualTo(basis.Shape));Assert.That(task.Speed,Is.EqualTo(10));Assert.That(task.Patience,Is.EqualTo(90));
                    Assert.That(task.Requirements[0].Minimum,Is.EqualTo(1));
                }
                Assert.That(different.Count,Is.GreaterThan(1));Assert.That(basis.Calculation,Is.EqualTo(100));Assert.That(basis.Tokens,Is.EqualTo(100));
                Assert.That(((TaskDefinition)Call(memory,"ScaleTask",1,Basis(0),a)).Tokens,Is.Zero);
            }
            finally{Object.DestroyImmediate(memory);}
        }
        [Test] public void FixedDecimalMultiplierKeepsTheFinalAbilityCycle()
        {
            var memory=ScriptableObject.CreateInstance(MemoryType);
            try
            {
                Range(Field(memory,"InitialTaskMultipliers"),1.8,1.8,2.5,2.5);
                var task=(TaskDefinition)Call(memory,"ScaleTask",1,Basis(),new System.Random(1));
                Assert.That(task.Calculation,Is.EqualTo(180));Assert.That(task.Tokens,Is.EqualTo(250));
                var sim=new TaskSimulation(2,1,new[]{new Cell(0,0),new Cell(1,0)},
                    new ModelDefinition("fixture",3,new Dictionary<AbilityKind,double>{{AbilityKind.Writing,1}}));
                sim.TryCreate("A","user","1",task,out var instance);sim.TryPlace("A",new Cell(0,0),0,out _);sim.Advance(18);
                Assert.That(instance.State,Is.EqualTo(RequestState.Ready));Assert.That(instance.GetAbility(AbilityKind.Writing),Is.EqualTo(6));
            }
            finally{Object.DestroyImmediate(memory);}
        }
        [TestCase(0f,1f,1f,1f)] [TestCase(-1f,1f,1f,1f)] [TestCase(2f,1f,1f,1f)]
        [TestCase(1f,1f,-1f,1f)] [TestCase(1f,1f,2f,1f)] [TestCase(float.NaN,1f,1f,1f)] [TestCase(1f,float.PositiveInfinity,1f,1f)]
        public void InvalidRangesAreRejected(float a,float b,float c,float d)
        {
            var memory=ScriptableObject.CreateInstance(MemoryType);
            try{Range(Field(memory,"InitialTaskMultipliers"),a,b,c,d);Assert.Throws<TargetInvocationException>(()=>Call(memory,"ValidateTaskMultipliers"));}
            finally{Object.DestroyImmediate(memory);}
        }
        private sealed class Fixture:IDisposable
        {
            public readonly MonoBehaviour Owner;
            public readonly ScriptableObject Config,Memory,Generation;
            private readonly ScriptableObject original;
            private readonly FieldInfo field;
            public TaskSimulation Sim => Value<TaskSimulation>(Owner,"Simulation");
            public Fixture()
            {
                Owner=Object.FindObjectsByType<MonoBehaviour>().Single(o=>o.GetType().Name=="CoreGameplayController");
                field=Owner.GetType().GetField("configuration",BindingFlags.NonPublic|BindingFlags.Instance);original=(ScriptableObject)field.GetValue(Owner);
                Config=Object.Instantiate(original);Memory=Object.Instantiate((ScriptableObject)Field(Config,"MemoryUpgrades"));
                var originalGeneration=(ScriptableObject)Field(Config,"TaskGeneration"); Generation=originalGeneration != null ? Object.Instantiate(originalGeneration) : ScriptableObject.CreateInstance(Type.GetType("CallmeCatgirl.Gameplay.TaskGenerationConfiguration, Assembly-CSharp",true));
                var poolType=Generation.GetType().GetField("OperatingTasks").FieldType.GetElementType();
                Set(Generation,"OperatingTasks",Array.CreateInstance(poolType,0)); // 本组隔离经营池，仅测试原任务倍率。
                Set(Config,"MemoryUpgrades",Memory);Set(Config,"TaskGeneration",Generation);Set(Config,"Story",null);
                Set(Config,"InitialFunds",10000);Set(Config,"MonthlyMaintenanceFee",0);SetMonthDuration(Config, 30f);Set(Config,"TokenPrice",2);
                Set(Memory,"InitialLevel",1);Range(Field(Memory,"InitialTaskMultipliers"),1,1,1,1);
                foreach(object step in (Array)Field(Memory,"Levels"))
                {
                    int level=(int)Field(step,"TargetLevel");Set(step,"Cost",0);Set(step,"RequiredCompletedTasks",0);
                    Range(Field(step,"TaskMultipliers"),level==2?2:4,level==2?2:4,level==2?3:5,level==2?3:5);
                }
                Set(Generation,"RequestsPerRefresh",2);Set(Generation,"MonthsPerRefresh",1);Set(Generation,"RandomSeed",20);
                var shapeType=Generation.GetType().GetNestedType("ShapePreset");var shape=Activator.CreateInstance(shapeType);Set(shape,"Name","fixture");Set(shape,"Pattern",new[]{"##"});
                var shapes=Array.CreateInstance(shapeType,1);shapes.SetValue(shape,0);Set(Generation,"Shapes",shapes);
                var demoType=Config.GetType().GetNestedType("DemoTask");var demo=Activator.CreateInstance(demoType);
                Set(demo,"Id","A");Set(demo,"Title","fixture");Set(demo,"Shape",new[]{Vector2Int.zero,new Vector2Int(1,0)});
                Set(demo,"Calculation",100f);Set(demo,"Speed",10f);Set(demo,"Patience",90f);Set(demo,"Tokens",100f);Set(demo,"Minimum",Vector3.zero);
                var tasks=Array.CreateInstance(demoType,1);tasks.SetValue(demo,0);Set(Config,"Requests",tasks);Owner.GetType().GetField("training", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(Owner, null);
                Config.GetType().GetField("OrdinaryDialoguePool").SetValue(Config, null);
                Config.GetType().GetField("OpeningBalance").SetValue(Config, null);
                field.SetValue(Owner,Config);Reset();
            }
            public void Reset()=>Call(Owner,"ResetSimulation");
            public void Advance(double seconds){Sim.Paused=false;Sim.Advance(seconds);Sim.Paused=true;Refresh();}
            public void Refresh()=>Call(Object.FindObjectsByType<MonoBehaviour>().Single(o=>o.GetType().Name=="CoreGameplayCanvasView"),"Refresh");
            public void Place(TaskInstance task)
            {
                var cell=Enumerable.Range(0,Sim.Width*Sim.Height).Select(i=>new Cell(i%Sim.Width,i/Sim.Width)).First(c=>Sim.Preview(task.Id,c,0)==PlacementFailure.None);
                Assert.That(Sim.TryPlace(task.Id,cell,0,out _),Is.True);
            }
            public void Upgrade()
            {
                Call(Owner,"OpenMemoryUpgrade");Call(Owner,"UpgradeMemory");
                if(Value<bool>(Owner,"IsMemoryInstallOpen"))
                    Call(Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None).Single(o=>o.GetType().Name=="StandaloneMemoryInstall"),"SkipInstall");
                Call(Owner,"CloseMemoryUpgrade");
            }
            public TaskInstance Selected=>Value<TaskInstance>(Owner,"SelectedTask");
            public void Submit(TaskInstance task){Call(Owner,"SelectRequest",task.Id);Call(Owner,"SubmitSelected");Call(Owner,"ChooseReply",0);}
            public void RestoreImportedStoryAtLevelTwo()
            {Set(Config,"Requests",Field(original,"Requests"));Set(Config,"Story",Field(original,"Story"));Set(Memory,"InitialLevel",2);Reset();}
            public void Dispose(){field.SetValue(Owner,original);Call(Owner,"ResetSimulation");Object.DestroyImmediate(Config);Object.DestroyImmediate(Memory);Object.DestroyImmediate(Generation);}
        }
        [UnityTest] public IEnumerator UpgradesScaleOnlyNewRequestsAndNeverCompoundAcrossStoryRounds()
        {
            EditorSceneManager.OpenScene("Assets/_Game/Scenes/Prototypes/CoreGameplay.unity");yield return new EnterPlayMode();yield return null;
            CheckFlow();yield return new ExitPlayMode();
        }
        private static void CheckFlow()
        {
            using(var f=new Fixture())
            {
                var old=f.Sim.Tasks["A-1"];var original=old.Definition;f.Place(old);f.Advance(1);double progress=old.Progress;
                f.Upgrade();Assert.That(Value<int>(f.Owner,"MemoryLevel"),Is.EqualTo(2));Assert.That(old.Definition,Is.SameAs(original));Assert.That(old.Progress,Is.EqualTo(progress));
                Call(f.Owner,"AddRequest");var next=f.Selected;Assert.That(next.Definition.Calculation,Is.EqualTo(200));Assert.That(next.Definition.Tokens,Is.EqualTo(300));
                var sampled=next.Definition;f.Refresh();f.Refresh();Assert.That(next.Definition,Is.SameAs(sampled));Capture("Cards");
                f.Place(next);f.Advance(20);int funds=Value<int>(f.Owner,"Funds");f.Submit(next);Assert.That(Value<int>(f.Owner,"Funds")-funds,Is.EqualTo(600));
                Call(f.Owner,"NextRound");next=f.Selected;Assert.That(next.Definition.Calculation,Is.EqualTo(200));Assert.That(next.Definition.Tokens,Is.EqualTo(300));
                sampled=next.Definition;f.Upgrade();Assert.That(next.Definition,Is.SameAs(sampled));f.Place(next);f.Advance(20);f.Submit(next);
                Call(f.Owner,"NextRound");next=f.Selected;Assert.That(next.Definition.Calculation,Is.EqualTo(400));Assert.That(next.Definition.Tokens,Is.EqualTo(500));
                double at=f.Sim.GameTime+30;f.Sim.Paused=false;Call(f.Owner,"AdvanceGame",30d);f.Sim.Paused=true;
                var batch=f.Sim.Tasks.Values.Where(t=>t.ArrivedAt==at).ToArray();Assert.That(batch.Length,Is.EqualTo(2));
                foreach(var task in batch){Assert.That(task.Definition.Calculation,Is.EqualTo(400));Assert.That(task.Definition.Tokens,Is.EqualTo(500));}
                f.Reset();Assert.That(f.Sim.Tasks["A-1"].Definition.Calculation,Is.EqualTo(100));Assert.That(f.Sim.Tasks["A-1"].Definition.Tokens,Is.EqualTo(100));
                Set(f.Memory,"InitialLevel",3);f.Reset();Assert.That(f.Sim.Tasks["A-1"].Definition.Calculation,Is.EqualTo(400));Assert.That(f.Sim.Tasks["A-1"].Definition.Tokens,Is.EqualTo(500));
            }
        }
        [UnityTest] public IEnumerator ImportedDifficultyIsResolvedBeforeScalingAndInvalidUpgradeIsAtomic()
        {
            EditorSceneManager.OpenScene("Assets/_Game/Scenes/Prototypes/CoreGameplay.unity");yield return new EnterPlayMode();yield return null;
            CheckOverrides();yield return new ExitPlayMode();
        }
        private static void CheckOverrides()
        {
            var owner=Object.FindObjectsByType<MonoBehaviour>().Single(o=>o.GetType().Name=="CoreGameplayController");
            Call(owner,"OpenMemoryUpgrade");Capture("Upgrade");Call(owner,"CloseMemoryUpgrade");
            using(var f=new Fixture())
            {
                var first=((Array)Field(f.Memory,"Levels")).GetValue(0);Range(Field(first,"TaskMultipliers"),3,2,3,3);
                int funds=Value<int>(f.Owner,"Funds"),cells=f.Sim.OpenCellCount;Assert.That(Value<bool>(f.Owner,"CanUpgradeMemory"),Is.False);
                Call(f.Owner,"UpgradeMemory");Assert.That(Value<int>(f.Owner,"MemoryLevel"),Is.EqualTo(1));Assert.That(Value<int>(f.Owner,"Funds"),Is.EqualTo(funds));Assert.That(f.Sim.OpenCellCount,Is.EqualTo(cells));
                Range(Field(first,"TaskMultipliers"),2,2,3,3);f.RestoreImportedStoryAtLevelTwo();
                var templates=(Dictionary<string,TaskDefinition>)f.Owner.GetType().GetField("definitions",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(f.Owner);
                var story=Value<object>(f.Owner,"Story");var basis=(TaskDefinition)Call(story,"BuildNextTask","A",templates,templates["A"]);
                var task=f.Sim.Tasks["A-1"];Assert.That(task.Definition.Calculation,Is.EqualTo(basis.Calculation*2));Assert.That(task.Definition.Tokens,Is.EqualTo(basis.Tokens*3));
                Assert.That(task.Definition.Speed,Is.EqualTo(basis.Speed));Assert.That(task.Definition.Patience,Is.EqualTo(basis.Patience));Assert.That(task.Definition.Shape,Is.EqualTo(basis.Shape));
                Call(f.Owner,"OpenMemoryUpgrade");Assert.That(Object.FindObjectsByType<Text>().Any(t=>t.text.Contains("新请求：计算量")),Is.True);
            }
        }
        [UnityTearDown]public IEnumerator StopPlayMode(){if(EditorApplication.isPlaying)yield return new ExitPlayMode();}
    }
}
