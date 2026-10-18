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
    public sealed class TaskGenerationTests
    {
        private static void SetMonthDuration(object config, float seconds)
        {
            config.GetType().GetField("DaysPerGameMonth").SetValue(config, 1);
            config.GetType().GetField("SecondsPerGameDay").SetValue(config, seconds);
        }

        private static Type GenerationType => Type.GetType("CallmeCatgirl.Gameplay.TaskGenerationConfiguration, Assembly-CSharp", true);
        private static Type StoryType => Type.GetType("CallmeCatgirl.Gameplay.ConversationStory, Assembly-CSharp", true);
        private static object Call(object o, string name, params object[] args) => o.GetType().GetMethod(name).Invoke(o,args);
        private static T Value<T>(object o, string name) => (T)o.GetType().GetProperty(name).GetValue(o);
        private static void Set(object o, string name, object value) => o.GetType().GetField(name).SetValue(o,value);
        private static void SingleShape(ScriptableObject config, string[] rows)
        {
            var type = GenerationType.GetNestedType("ShapePreset"); var preset = Activator.CreateInstance(type);
            Set(preset,"Name","fixture"); Set(preset,"Pattern",rows);
            var list = Array.CreateInstance(type,1); list.SetValue(preset,0); Set(config,"Shapes",list);
        }

        [Test] public void DefaultsHaveTenConnectedUniqueShapesAndEditableRefreshNumbers()
        {
            var config = ScriptableObject.CreateInstance(GenerationType);
            try
            {
                Assert.That(config.GetType().GetField("RequestsPerRefresh").GetValue(config),Is.EqualTo(5));
                Assert.That(config.GetType().GetField("MonthsPerRefresh").GetValue(config),Is.EqualTo(1));
                var shapes = (Array)config.GetType().GetField("Shapes").GetValue(config);
                Assert.That(shapes.Length,Is.EqualTo(10)); var distinct = new HashSet<string>();
                foreach (object preset in shapes)
                {
                    var cells = (Cell[])Call(preset,"Build");
                    Assert.That(cells.Length,Is.InRange(1,5)); distinct.Add(string.Join(";",cells.Select(c=>c.ToString())));
                }
                Assert.That(distinct.Count,Is.EqualTo(10)); Call(config,"Validate",8,8);
                var editor = Editor.CreateEditor(config);
                try { Assert.That(editor.GetType().Name,Is.EqualTo("TaskGenerationConfigurationEditor")); }
                finally { Object.DestroyImmediate(editor); }
                Set(config,"RandomSeed",42);
                var a = (System.Random)Call(config,"CreateRandom"); var b = (System.Random)Call(config,"CreateRandom");
                for (int i=0;i<40;i++) Assert.That((Cell[])Call(config,"PickShape",a),Is.EqualTo((Cell[])Call(config,"PickShape",b)));
            }
            finally { Object.DestroyImmediate(config); }
        }
        [Test] public void InvalidPoolAndRefreshNumbersFailBeforeGameplayStarts()
        {
            var config = ScriptableObject.CreateInstance(GenerationType);
            try
            {
                Set(config,"RequestsPerRefresh",-1); Invalid(config); Set(config,"RequestsPerRefresh",5);
                Set(config,"MonthsPerRefresh",0); Invalid(config); Set(config,"MonthsPerRefresh",1);
                SingleShape(config,new[]{"#.",".#"}); Invalid(config);
                SingleShape(config,new[]{new string('#',9)}); Invalid(config);
                var element = GenerationType.GetNestedType("ShapePreset"); Set(config,"Shapes",Array.CreateInstance(element,0)); Invalid(config);
            }
            finally { Object.DestroyImmediate(config); }
        }
        private static void Invalid(object config) => Assert.Throws<TargetInvocationException>(()=>Call(config,"Validate",8,8));

        [TestCase(true)] [TestCase(false)]
        public void ImportedPatternTakesPriorityAndOmittedPatternUsesIndependentPool(bool explicitShape)
        {
            var generation = ScriptableObject.CreateInstance(GenerationType); ScriptableObject config = null;
            try
            {
                SingleShape(generation,new[]{"#.","##"});
                string text = "角色：林小满\n角色ID：A\n计算量：20\n忍耐时间：80\n计算速度：2\nToken收入：7\n"
                    + (explicitShape ? "方块：\n■■\n■■\n" : "")
                    + "【会话：测试】\n【第1轮】\n用户：你好\n选项1：好\n下一步：结束本次会话\n";
                var importer = Type.GetType("CallmeCatgirl.EditorTools.StoryTextImporter, Assembly-CSharp-Editor",true);
                config = (ScriptableObject)importer.GetMethod("Parse").Invoke(null,new object[]{text,new[]{"A"},null});
                var story = Activator.CreateInstance(StoryType,new object[]{config,generation,new System.Random(1)});
                var basis = new TaskDefinition("A","basis",new[]{new Cell(0,0)},100,10,90,100,Array.Empty<AbilityRequirement>());
                var templates = new Dictionary<string,TaskDefinition>{{"A",basis}};
                var actual = (TaskDefinition)Call(story,"BuildNextTask","A",templates,basis);
                Assert.That(actual.Shape.Count,Is.EqualTo(explicitShape?4:3));
                Assert.That(actual.Calculation,Is.EqualTo(20)); Assert.That(actual.Speed,Is.EqualTo(2));
                Assert.That(actual.Patience,Is.EqualTo(80)); Assert.That(actual.Tokens,Is.EqualTo(7));
                var generic = (TaskDefinition)Call(story,"BuildNextTask","new-user",templates,basis);
                Assert.That(generic.Shape.Count,Is.EqualTo(3)); Assert.That(generic.Calculation,Is.EqualTo(100));
                var legacy = Activator.CreateInstance(StoryType,new object[]{config});
                var old = (TaskDefinition)Call(legacy,"BuildNextTask","new-user",templates,basis);
                Assert.That(old.Shape,Is.EqualTo(basis.Shape));
            }
            finally { if(config!=null)Object.DestroyImmediate(config); Object.DestroyImmediate(generation); }
        }

        private sealed class Fixture : IDisposable
        {
            public readonly MonoBehaviour Owner;
            public readonly ScriptableObject Config, Generation;
            private readonly ScriptableObject original;
            private readonly FieldInfo field;
            public TaskSimulation Sim => Value<TaskSimulation>(Owner,"Simulation");
            public Fixture(MonoBehaviour owner)
            {
                Owner=owner; field=owner.GetType().GetField("configuration",BindingFlags.Instance|BindingFlags.NonPublic);
                original=(ScriptableObject)field.GetValue(owner); Config=Object.Instantiate(original);
                Generation=ScriptableObject.CreateInstance(GenerationType); SingleShape(Generation,new[]{"#"});
                Set(Generation,"RandomSeed",5); Set(Config,"TaskGeneration",Generation); Set(Config,"OrdinaryDialoguePool",null);Set(Config,"Story",null);
                SetMonthDuration(Config, 30f); Set(Config,"InitialFunds",5000); Set(Config,"MonthlyMaintenanceFee",0);
                var taskType=Config.GetType().GetNestedType("DemoTask"); var task=Activator.CreateInstance(taskType);
                Set(task,"Id","A"); Set(task,"Title","fixture"); Set(task,"Shape",new[]{Vector2Int.zero});
                Set(task,"Calculation",1000f); Set(task,"Speed",1f); Set(task,"Patience",500f); Set(task,"Tokens",0f);
                Set(task,"Minimum",Vector3.zero); var tasks=Array.CreateInstance(taskType,1);tasks.SetValue(task,0);Set(Config,"Requests",tasks);
                field.SetValue(owner,Config); Reset();
            }
            public void Reset() => Call(Owner,"ResetSimulation");
            public void Advance(double amount) => Call(Owner,"AdvanceGame",amount);
            public void Patience(float seconds) => Set(((Array)Config.GetType().GetField("Requests").GetValue(Config)).GetValue(0),"Patience",seconds);
            public void Dispose()
            {
                field.SetValue(Owner,original); Call(Owner,"ResetSimulation"); Object.DestroyImmediate(Config); Object.DestroyImmediate(Generation);
            }
        }
        private static MonoBehaviour Controller() => Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None)
            .Single(o=>o.GetType().FullName=="CallmeCatgirl.Gameplay.CoreGameplayController");
        [UnityTest] public IEnumerator MonthlyBatchesPreserveExistingRequestsAndTimeoutRemovesCandidates()
        {
            EditorSceneManager.OpenScene("Assets/_Game/Scenes/Prototypes/CoreGameplay.unity"); yield return new EnterPlayMode(); yield return null;
            CheckBatches(Controller()); yield return new ExitPlayMode();
        }
        private static void CheckBatches(MonoBehaviour owner)
        {
            using(var fixture=new Fixture(owner))
            {
                fixture.Patience(45); fixture.Reset(); var sim=fixture.Sim; var old=sim.Tasks["A-1"];
                var position=Enumerable.Range(0,sim.Width*sim.Height).Select(i=>new Cell(i%sim.Width,i/sim.Width)).First(sim.IsOpen);
                Assert.That(sim.TryPlace(old.Id,position,0,out _),Is.True);sim.Paused=false;fixture.Advance(29);
                Assert.That(sim.Tasks.Count,Is.EqualTo(1));fixture.Advance(1);
                Assert.That(sim.Tasks.Count,Is.EqualTo(6));Assert.That(sim.Tasks[old.Id],Is.SameAs(old));
                Assert.That(old.Progress,Is.EqualTo(30 * sim.TokenThroughput));Assert.That(old.Origin,Is.EqualTo(position));
                var first=sim.Tasks.Values.Where(t=>t.ArrivedAt==30).ToArray();Assert.That(first.Length,Is.EqualTo(5));
                foreach(var task in first){Assert.That(task.Definition.Shape.Count,Is.EqualTo(1));Assert.That(task.State,Is.EqualTo(RequestState.Waiting));Assert.That(task.Deadline,Is.EqualTo(75));}
                fixture.Advance(15);Assert.That(old.State,Is.EqualTo(RequestState.TimedOut));Assert.That(sim.Occupancy,Is.Empty);
                fixture.Advance(30);Assert.That(sim.Tasks.Count,Is.EqualTo(11));Assert.That(Value<int>(owner,"CurrentMonth"),Is.EqualTo(3));
                foreach(var task in first)Assert.That(task.State,Is.EqualTo(RequestState.TimedOut));
                Assert.That(sim.Tasks.Values.Count(t=>t.State==RequestState.Waiting),Is.EqualTo(5));
                var activeCards=Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None).Count(o=>o.GetType().FullName=="CallmeCatgirl.UI.TaskCardView");
                Assert.That(activeCards,Is.EqualTo(5),"Expired candidates disappear from the visible queue.");
            }
        }
        [UnityTest] public IEnumerator PausesAndModalsFreezeRefreshAndResetRestoresTheInitialClock()
        {
            EditorSceneManager.OpenScene("Assets/_Game/Scenes/Prototypes/CoreGameplay.unity"); yield return new EnterPlayMode(); yield return null;
            CheckPauses(Controller()); yield return new ExitPlayMode();
        }
        private static void CheckPauses(MonoBehaviour owner)
        {
            using(var fixture=new Fixture(owner))
            {
                var sim=fixture.Sim;fixture.Advance(100);Assert.That(sim.GameTime,Is.Zero);Assert.That(sim.Tasks.Count,Is.EqualTo(1));
                sim.Paused=false;fixture.Advance(29);Call(owner,"OpenMemoryUpgrade");fixture.Advance(100);
                Assert.That(sim.GameTime,Is.EqualTo(29));Call(owner,"CloseMemoryUpgrade");sim.Paused=true;fixture.Advance(100);
                Assert.That(sim.GameTime,Is.EqualTo(29));sim.Paused=false;Call(owner,"SetSpeed",4f);Call(owner,"SetSpeed",.5f);
                Assert.That(sim.Tasks.Count,Is.EqualTo(1));fixture.Advance(1);Assert.That(sim.Tasks.Count,Is.EqualTo(6));
                fixture.Reset();Assert.That(fixture.Sim.GameTime,Is.Zero);Assert.That(fixture.Sim.Tasks.Count,Is.EqualTo(1));Assert.That(fixture.Sim.Paused,Is.True);
            }
        }
        [UnityTest] public IEnumerator ConfigurableIntervalAndBatchSizeHandleLargeFramesAndBankruptcy()
        {
            EditorSceneManager.OpenScene("Assets/_Game/Scenes/Prototypes/CoreGameplay.unity"); yield return new EnterPlayMode(); yield return null;
            CheckIntervals(Controller()); yield return new ExitPlayMode();
        }
        private static void CheckIntervals(MonoBehaviour owner)
        {
            using(var fixture=new Fixture(owner))
            {
                Set(fixture.Generation,"MonthsPerRefresh",2);Set(fixture.Generation,"RequestsPerRefresh",2);fixture.Reset();fixture.Sim.Paused=false;fixture.Advance(90);
                Assert.That(fixture.Sim.Tasks.Count,Is.EqualTo(3));Assert.That(fixture.Sim.Tasks.Values.Count(t=>t.ArrivedAt==60),Is.EqualTo(2));
                Set(fixture.Generation,"MonthsPerRefresh",1);SetMonthDuration(fixture.Config, 10f);fixture.Reset();fixture.Sim.Paused=false;fixture.Advance(35);
                Assert.That(fixture.Sim.Tasks.Count,Is.EqualTo(7));Assert.That(fixture.Sim.Tasks.Values.Count(t=>t.ArrivedAt==10),Is.EqualTo(2));
                Assert.That(fixture.Sim.Tasks.Values.Count(t=>t.ArrivedAt==20),Is.EqualTo(2));Assert.That(fixture.Sim.Tasks.Values.Count(t=>t.ArrivedAt==30),Is.EqualTo(2));
                Set(fixture.Config,"InitialFunds",0);Set(fixture.Config,"MonthlyMaintenanceFee",1);fixture.Reset();fixture.Sim.Paused=false;fixture.Advance(100);
                Assert.That(Value<bool>(owner,"GameEnded"),Is.True);Assert.That(fixture.Sim.GameTime,Is.EqualTo(20));Assert.That(fixture.Sim.Tasks.Count,Is.EqualTo(3));
                Set(fixture.Config,"InitialFunds",5000);Set(fixture.Config,"MonthlyMaintenanceFee",0);Set(fixture.Generation,"RequestsPerRefresh",0);
                fixture.Reset();fixture.Sim.Paused=false;fixture.Advance(100);Assert.That(fixture.Sim.Tasks.Count,Is.EqualTo(1));
            }
        }
        [UnityTearDown] public IEnumerator ExitIfNeeded(){if(EditorApplication.isPlaying)yield return new ExitPlayMode();}
    }
}
