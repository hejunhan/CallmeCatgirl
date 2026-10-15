using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using CallmeCatgirl.Gameplay.TaskGrid;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace CallmeCatgirl.Tests
{
    public sealed class StrongFeedbackTests
    {
        private static object Call(object o,string name,params object[] args) => o.GetType().GetMethod(name).Invoke(o,args);
        private static T Value<T>(object o,string name) => (T)o.GetType().GetProperty(name).GetValue(o);
        private static object Field(object o,string name) => o.GetType().GetField(name,BindingFlags.NonPublic|BindingFlags.Instance).GetValue(o);
        private static MonoBehaviour Named(string name) => Object.FindObjectsByType<MonoBehaviour>().First(o=>o.GetType().Name==name);
        private static Text Label(object effect) => (Text)Field(effect,"label");
        private static void Capture(string name) => typeof(SystemFeedbackTests).GetMethod("Capture",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{"Strong"+name+".png"});
        private sealed class Fixture : IDisposable
        {
            public readonly MonoBehaviour Owner,View;
            public readonly ScriptableObject Config,Visual;
            private readonly ScriptableObject oldConfig,oldVisual;
            private readonly FieldInfo configField,visualField;
            public TaskSimulation Sim => Value<TaskSimulation>(Owner,"Simulation");
            public Fixture()
            {
                Owner=Named("CoreGameplayController");View=Named("CoreGameplayCanvasView");
                configField=Owner.GetType().GetField("configuration",BindingFlags.NonPublic|BindingFlags.Instance);
                visualField=View.GetType().GetField("feedback",BindingFlags.NonPublic|BindingFlags.Instance);
                oldConfig=(ScriptableObject)configField.GetValue(Owner);oldVisual=(ScriptableObject)visualField.GetValue(View);
                Config=Object.Instantiate(oldConfig);Visual=Object.Instantiate(oldVisual);
                Config.GetType().GetField("Story").SetValue(Config,null);Config.GetType().GetField("TokenPrice").SetValue(Config,3);
                configField.SetValue(Owner,Config);visualField.SetValue(View,Visual);Call(Owner,"ResetSimulation");
            }
            public TaskInstance Place(string id,double calculation=12,double patience=90,double tokens=11,bool fail=false)
            {
                var def=new TaskDefinition("fx",id,new[]{new Cell(0,0),new Cell(1,0)},calculation,1,patience,tokens,
                    new[]{new AbilityRequirement(AbilityKind.Writing,fail?999:1,fail?1000:2)});
                Assert.That(Sim.TryCreate(id,"user-"+id,"1",def,out var task),Is.True);
                PlaceExisting(task);Call(Owner,"SelectRequest",id);Refresh();return task;
            }
            public void PlaceExisting(TaskInstance task)
            {
                var origin=Enumerable.Range(0,Sim.Width*Sim.Height).Select(i=>new Cell(i%Sim.Width,i/Sim.Width)).First(c=>Sim.Preview(task.Id,c,0)==PlacementFailure.None);
                Assert.That(Sim.TryPlace(task.Id,origin,0,out _),Is.True);
            }
            public void Advance(double seconds) { Sim.Paused=false;Sim.Advance(seconds);Sim.Paused=true;Refresh(); }
            public void Refresh() => Call(View,"Refresh");
            public MonoBehaviour Module(string id) => Object.FindObjectsByType<MonoBehaviour>().First(o=>o.name=="RunningModule_"+id && o.GetType().Name=="TaskModuleView");
            public void Dispose(){configField.SetValue(Owner,oldConfig);visualField.SetValue(View,oldVisual);Call(Owner,"ResetSimulation");Object.DestroyImmediate(Config);Object.DestroyImmediate(Visual);}
        }
        [UnityTest] public IEnumerator FourFeedbackPointsUseRealValuesAndDoNotMoveLogicalCells()
        {
            EditorSceneManager.OpenScene("Assets/_Game/Scenes/Prototypes/CoreGameplay.unity");
            yield return new EnterPlayMode();yield return null;
            using(var f=new Fixture())
            {
                var task=f.Place("fx");var module=f.Module(task.Id);var shape=(RectTransform)Field(module,"shapeRoot");
                var root=(RectTransform)module.transform;var position=root.anchoredPosition;var origin=task.Origin;
                Assert.That(shape.localScale.x,Is.GreaterThan(1));
                yield return new WaitForSecondsRealtime(.4f);
                Assert.That(shape.localScale.x,Is.EqualTo(1).Within(.001));Assert.That(Vector2.Distance(root.anchoredPosition,position),Is.LessThan(.1f));
                Assert.That(task.Origin,Is.EqualTo(origin));Assert.That(task.Progress,Is.Zero);
                f.Advance(f.Sim.Model.Interval);
                var effect=(MonoBehaviour)Field(module,"feedbackLabel");double gain=task.GetAbility(AbilityKind.Writing);
                Assert.That(Label(effect).text,Does.Contain("写作 +"+gain.ToString("0.##")));
                Assert.That(effect.GetComponent<CanvasGroup>().alpha,Is.GreaterThan(0));
                Assert.That(effect.GetComponent<CanvasGroup>().blocksRaycasts,Is.False);
                Assert.That(effect.GetComponentsInChildren<Graphic>().All(g=>!g.raycastTarget),Is.True);
                Capture("Abilities");
                Assert.That(Vector2.Distance(root.anchoredPosition,position),Is.LessThan(.1f));
                double pausedProgress=task.Progress;yield return new WaitForSecondsRealtime(.1f);Assert.That(task.Progress,Is.EqualTo(pausedProgress));
                f.Advance(f.Sim.Model.Interval*2);
                Assert.That(Label(effect).text,Does.Contain("写作 +"+(gain*2).ToString("0.##")),"Large frames show actual aggregated gains.");
                Assert.That(f.Sim.TryWithdraw(task.Id),Is.True);f.Refresh();f.PlaceExisting(task);f.Refresh();
                Assert.That(effect.GetComponent<CanvasGroup>().alpha,Is.Zero,"Re-placing a task must not invent ability gains.");
                f.Advance(20);Assert.That(task.State,Is.EqualTo(RequestState.Ready));
                Assert.That(Label(effect).text,Does.Contain("优秀"));Capture("Complete");
                int before=Value<int>(f.Owner,"Funds");Call(f.Owner,"SubmitSelected");
                int after=Value<int>(f.Owner,"Funds");Assert.That(after-before,Is.EqualTo(33));
                var settlement=(MonoBehaviour)Field(f.View,"settlementFeedback");Assert.That(Label(settlement).text,Does.Contain("收入 +33"));
                Assert.That(settlement.transform.GetSiblingIndex(),Is.EqualTo(settlement.transform.parent.childCount-1));
                Assert.That(Value<bool>(f.Owner,"IsStoryOpen"),Is.True);Capture("Settlement");
                double frozen=f.Sim.GameTime;yield return new WaitForSecondsRealtime(.2f);
                Assert.That(f.Sim.GameTime,Is.EqualTo(frozen));Assert.That(Value<int>(f.Owner,"Funds"),Is.EqualTo(after));
                int displayed=(int)f.View.GetType().GetProperty("DisplayFunds",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(f.View);
                Assert.That(displayed,Is.InRange(before,after));
                yield return new WaitForSecondsRealtime(1.4f);Assert.That(settlement.GetComponent<CanvasGroup>().alpha,Is.Zero);
                Call(f.Owner,"ChooseReply",0);
                task=f.Place("failed",1,90,11,true);f.Advance(1);before=Value<int>(f.Owner,"Funds");Call(f.Owner,"SubmitSelected");
                Assert.That(Value<int>(f.Owner,"Funds"),Is.EqualTo(before));Assert.That(Label(settlement).text,Does.Contain("未达标").And.Contain("收入 +0"));
                Call(f.Owner,"ChooseReply",0);
                task=f.Place("timeout",10,5,3);before=Value<int>(f.Owner,"Funds");f.Sim.Paused=false;Call(f.Owner,"AdvanceGame",5d);f.Sim.Paused=true;
                Assert.That(task.State,Is.EqualTo(RequestState.TimedOut));Assert.That(Value<int>(f.Owner,"Funds")-before,Is.EqualTo(4));
                Assert.That(Label(settlement).text,Does.Contain("超时结算").And.Contain("收入 +4"));
                f.Visual.GetType().GetField("Animate").SetValue(f.Visual,false);f.Refresh();
                Assert.That(settlement.GetComponent<CanvasGroup>().alpha,Is.Zero);
            }
            yield return new ExitPlayMode();
        }
        [UnityTest] public IEnumerator RejectedPlacementShakesLocallyAndStillPreservesProgressAndPose()
        {
            EditorSceneManager.OpenScene("Assets/_Game/Scenes/Prototypes/CoreGameplay.unity");yield return new EnterPlayMode();yield return null;
            using(var f=new Fixture())
            {
                var task=f.Place("shake");yield return new WaitForSecondsRealtime(.35f);
                var module=f.Module(task.Id);var root=(RectTransform)module.transform;var shape=(RectTransform)Field(module,"shapeRoot");
                Vector2 position=root.anchoredPosition,home=shape.anchoredPosition;Cell origin=task.Origin;
                var board=Value<RectTransform>(f.View,"Board");
                var data=new PointerEventData(EventSystem.current){button=PointerEventData.InputButton.Left,position=RectTransformUtility.WorldToScreenPoint(null,root.TransformPoint(new Vector3(10,-10,0)))};
                ((IBeginDragHandler)module).OnBeginDrag(data);
                data.position=RectTransformUtility.WorldToScreenPoint(null,board.TransformPoint(new Vector3(board.rect.xMin-100,board.rect.yMax+100,0)));
                ((IEndDragHandler)module).OnEndDrag(data);
                // A fixed point inside the configured interval makes this independent of frame rate.
                module.GetType().GetField("rejectedAt",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(module,Time.unscaledTime-.025f);f.Refresh();
                Assert.That(shape.anchoredPosition.x,Is.Not.EqualTo(home.x));Assert.That(Vector2.Distance(root.anchoredPosition,position),Is.LessThan(.1f));
                Assert.That(task.Origin,Is.EqualTo(origin));Assert.That(task.Progress,Is.Zero);Assert.That(task.State,Is.EqualTo(RequestState.Running));
                yield return new WaitForSecondsRealtime(.3f);Assert.That(shape.anchoredPosition,Is.EqualTo(home));
            }
            yield return new ExitPlayMode();
        }
        [UnityTearDown]public IEnumerator StopPlayMode(){if(EditorApplication.isPlaying)yield return new ExitPlayMode();}
    }
}
