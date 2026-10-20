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
using Object=UnityEngine.Object;
namespace CallmeCatgirl.Gameplay.Training.Tests
{
    // Authored controls with a disposable session; scene callbacks, not a mouse journey or narrative acceptance.
    public sealed class TrainingShapeUiRegressionTests
    {
        private static object Field(object o,string name)=>o.GetType().GetField(name,BindingFlags.NonPublic|BindingFlags.Instance)?.GetValue(o);
        private static void Private(object o,string name)=>o.GetType().GetMethod(name,BindingFlags.NonPublic|BindingFlags.Instance).Invoke(o,null);
        private static object Call(object o,string name,params object[] args)=>o.GetType().GetMethod(name).Invoke(o,args);
        private static TrainingSession Make(TaskSimulation sim,Cell[] cells)=>new TrainingSession(sim,null,
            new[]{new TrainingRecipe("normal","Normal",20,1,120,1,1,new[]{new Cell(0,0),new Cell(1,0),new Cell(0,1)},2,100,5)},
            new[]{new ArchitectureDefinition("baseline","Base",1,OutlineTransform.Keep)},new[]{new ComputeTier("one",10,0)},
            cost=>true,cost=>{},profile=>sim.TryChangeExecutionProfile(profile.Model,profile.TokensPerSecond,profile.OpenCells,profile.Architecture),rate=>sim.TrySetTokenThroughput(rate),()=>cells);
        private static MonoBehaviour FindView()=>Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include,FindObjectsSortMode.None).Single(c=>c.GetType().Name=="TrainingWorkspaceView");
        private static object Bind(MonoBehaviour view,TaskSimulation sim,TrainingSession session)
        {
            var go=new GameObject("TrainingRegressionController");
            var controller=go.AddComponent(Type.GetType("CallmeCatgirl.Gameplay.Training.TrainingController, Assembly-CSharp",true));
            Call(controller,"BindRestoredSession",sim,session,(Func<bool>)(()=>true));Call(view,"Bind",controller);return controller;
        }
        [UnityTest] public IEnumerator TutorialNineReopenPrefersCompactRecipeAndCompletesOnSingleCell()
        {
            EditorSceneManager.OpenScene("Assets/_Game/Scenes/Prototypes/CoreGameplay.unity");yield return new EnterPlayMode();yield return null;
            var cells=new[]{new Cell(1,1)};var sim=new TaskSimulation(4,4,cells,new ModelDefinition("empty",1,new Dictionary<AbilityKind,double>()));sim.Paused=true;sim.TrySetTokenThroughput(10);
            var session=Make(sim,cells);var view=FindView();Bind(view,sim,session);
            ((Dropdown)Field(view,"recipes")).value=0; // Player previously inspected the ordinary, larger recipe.
            Assert.That((string)Field(view,"recipe"),Is.EqualTo("normal"));
            Private(view,"OnDisable");
            session.GrantScriptedCorpus("opening","doctor",new Dictionary<AbilityKind,double>{{AbilityKind.Writing,1}});
            Assert.That(session.StartConversationTraining(TrainingSession.StarterRecipeId,"baseline","One","doctor",1,out var first),Is.EqualTo(TrainingResult.Success));
            Assert.That(sim.TryPlace(first.RequestId,cells[0],0,out _),Is.True);sim.Paused=false;sim.Advance(1.01);sim.Paused=true;
            Assert.That(session.CompleteTraining(first.RequestId),Is.EqualTo(TrainingResult.Success));
            session.GrantScriptedCorpus("nine","doctor",new Dictionary<AbilityKind,double>{{AbilityKind.Writing,9}});
            Private(view,"OnEnable");Call(view,"Bind",Field(view,"controller"));
            Assert.That((string)Field(view,"recipe"),Is.EqualTo(TrainingSession.TutorialCorpusRecipeId),"Reopening the tutorial cannot leave the oversized normal recipe selected.");
            Call(view,"SelectConversation","doctor");Assert.That((double)view.GetType().GetProperty("InvestedPoints").GetValue(view),Is.EqualTo(9));
            var start=(Button)Field(view,"start");Assert.That(start.interactable,Is.True);start.onClick.Invoke();
            var job=session.Jobs.Last();Assert.That(job.RecipeId,Is.EqualTo(TrainingSession.TutorialCorpusRecipeId));Assert.That(sim.Tasks[job.RequestId].Definition.Shape.Count,Is.EqualTo(1));
            Assert.That(sim.TryPlace(job.RequestId,cells[0],0,out _),Is.True);sim.Paused=false;sim.Advance(1.01);sim.Paused=true;
            Assert.That(session.CompleteTraining(job.RequestId),Is.EqualTo(TrainingResult.Success));Assert.That(job.Output.Definition.Points[AbilityKind.Writing],Is.EqualTo(9));
            Assert.That(session.GetConversationCorpus("doctor").AvailablePoints,Is.Zero);
            yield return new ExitPlayMode();
        }
        [UnityTest] public IEnumerator ActualShapePreviewGrowsWhenInvestmentSliderChanges()
        {
            EditorSceneManager.OpenScene("Assets/_Game/Scenes/Prototypes/CoreGameplay.unity");yield return new EnterPlayMode();yield return null;
            var cells=Enumerable.Range(0,64).Select(ToCell).ToArray();var sim=new TaskSimulation(8,8,cells,new ModelDefinition("empty",1,new Dictionary<AbilityKind,double>()));sim.Paused=true;sim.TrySetTokenThroughput(10);
            var session=Make(sim,cells);session.GrantScriptedCorpus("material","doctor",new Dictionary<AbilityKind,double>{{AbilityKind.Writing,20}});
            var view=FindView();Bind(view,sim,session);((Dropdown)Field(view,"recipes")).value=0;Call(view,"SelectConversation","doctor");
            var preview=Field(view,"actualShapePreview");Assert.That(preview,Is.Not.Null,"The scene must contain the actual training shape, not only source-card slots.");
            ((Slider)Field(view,"amount")).value=2;Assert.That(((Cell[])Field(preview,"cells")).Length,Is.EqualTo(4));
            ((Slider)Field(view,"amount")).value=10;Assert.That(((Cell[])Field(preview,"cells")).Length,Is.EqualTo(8));
            Assert.That(((Text)Field(view,"summary")).text,Does.Contain("8"));
            ((Button)Field(view,"start")).onClick.Invoke();Assert.That(sim.Tasks[session.Jobs.Single().RequestId].Definition.Shape.Count,Is.EqualTo(8));
            yield return new ExitPlayMode();
        }
        [UnityTest] public IEnumerator OrdinaryRecipeSelectionSurvivesClosingAndReopeningTraining()
        {
            EditorSceneManager.OpenScene("Assets/_Game/Scenes/Prototypes/CoreGameplay.unity");yield return new EnterPlayMode();yield return null;
            var cells=Enumerable.Range(0,64).Select(ToCell).ToArray();var sim=new TaskSimulation(8,8,cells,new ModelDefinition("empty",1,new Dictionary<AbilityKind,double>()));sim.Paused=true;sim.TrySetTokenThroughput(10);
            var session=MakeOrdinarySession(sim,cells);
            session.GrantScriptedCorpus("material","doctor",new Dictionary<AbilityKind,double>{{AbilityKind.Writing,9}});
            Assert.That(session.StartConversationTraining("normal-a","baseline","Established","doctor",9,out var job),Is.EqualTo(TrainingResult.Success));
            Assert.That(sim.TryPlace(job.RequestId,new Cell(0,0),0,out _),Is.True);sim.Paused=false;sim.Advance(1.01);sim.Paused=true;
            Assert.That(session.CompleteTraining(job.RequestId),Is.EqualTo(TrainingResult.Success));
            Assert.That(session.StarterTrainingAvailable,Is.False);Assert.That(session.CompactTrainingAvailable,Is.False);
            var view=FindView();Bind(view,sim,session);((Dropdown)Field(view,"recipes")).value=1;
            Assert.That((string)Field(view,"recipe"),Is.EqualTo("normal-b"));
            Private(view,"OnDisable");Private(view,"OnEnable");Call(view,"Bind",Field(view,"controller"));
            Assert.That((string)Field(view,"recipe"),Is.EqualTo("normal-b"),"Tutorial defaults must not reset the player's ordinary recipe selection.");
            yield return new ExitPlayMode();
        }
        private static TrainingSession MakeOrdinarySession(TaskSimulation sim,Cell[] cells)=>new TrainingSession(sim,null,
            new[]{new TrainingRecipe("normal-a","A",20,1,10,1,1,new[]{new Cell(0,0)},2,100),new TrainingRecipe("normal-b","B",20,1,10,1,1,new[]{new Cell(0,0)},2,100)},
            new[]{new ArchitectureDefinition("baseline","Base",1,OutlineTransform.Keep)},new[]{new ComputeTier("one",10,0)},
            cost=>true,cost=>{},profile=>sim.TryChangeExecutionProfile(profile.Model,profile.TokensPerSecond,profile.OpenCells,profile.Architecture),rate=>sim.TrySetTokenThroughput(rate),()=>cells);
        [UnityTest] public IEnumerator LongTrainingWarningUsesStrictGameSecondBoundaryAndDoesNotBlockStart()
        {
            EditorSceneManager.OpenScene("Assets/_Game/Scenes/Prototypes/CoreGameplay.unity");yield return new EnterPlayMode();yield return null;
            var cells=Enumerable.Range(0,64).Select(ToCell).ToArray();var sim=new TaskSimulation(8,8,cells,new ModelDefinition("empty",1,new Dictionary<AbilityKind,double>()));sim.Paused=true;sim.TrySetTokenThroughput(10);
            var session=MakeTimedSession(sim,cells);session.GrantScriptedCorpus("material","doctor",new Dictionary<AbilityKind,double>{{AbilityKind.Writing,20}});
            var view=FindView();Bind(view,sim,session);((Dropdown)Field(view,"recipes")).value=0;Call(view,"SelectConversation","doctor");
            var summary=(Text)Field(view,"summary");var start=(Button)Field(view,"start");
            Assert.That(summary.text,Does.Contain("预计训练时间 300 游戏秒（按当前模型与算力）"));Assert.That(summary.text,Does.Not.Contain("<color="));Assert.That(start.interactable,Is.True);
            // The selected architecture describes the output. It must not accelerate the current executor.
            ((Dropdown)Field(view,"architectures")).value=1;
            Assert.That(summary.text,Does.Contain("预计训练时间 300 游戏秒"));Assert.That(summary.text,Does.Not.Contain("<color="));
            Assert.That(sim.TrySetTokenThroughput(3000d/301),Is.True);Call(view,"Bind",Field(view,"controller"));
            Assert.That(summary.text,Does.Contain("<color=#C0392B>预计训练时间 301 游戏秒"));Assert.That(start.interactable,Is.True,"A warning must not disable a valid training draft.");
            sim.Paused=false;Call(view,"Bind",Field(view,"controller"));Assert.That(summary.text,Does.Contain("预计训练时间 301 游戏秒"));sim.Paused=true;
            Assert.That(sim.TrySetTokenThroughput(3000d/299),Is.True);Call(view,"Bind",Field(view,"controller"));
            Assert.That(summary.text,Does.Contain("预计训练时间 299 游戏秒"));Assert.That(summary.text,Does.Not.Contain("<color="));
            Assert.That(sim.TrySetTokenThroughput(3000d/301),Is.True);Call(view,"Bind",Field(view,"controller"));Assert.That(summary.text,Does.Contain("<color="));
            Call(view,"RemoveSelectedConversation");Assert.That(summary.text,Does.Not.Contain("<color="));Assert.That(start.interactable,Is.False);
            yield return new ExitPlayMode();
        }
        private static TrainingSession MakeTimedSession(TaskSimulation sim,Cell[] cells)=>new TrainingSession(sim,null,
            new[]{new TrainingRecipe("timed","Timed",20,1,3000,1,1,new[]{new Cell(0,0)},100,100)},
            new[]{new ArchitectureDefinition("baseline","Current",1,OutlineTransform.Keep),new ArchitectureDefinition("future-fast","Future output",2,OutlineTransform.Keep)},
            new[]{new ComputeTier("one",10,0)},cost=>true,cost=>{},
            profile=>sim.TryChangeExecutionProfile(profile.Model,profile.TokensPerSecond,profile.OpenCells,profile.Architecture),rate=>sim.TrySetTokenThroughput(rate),()=>cells);
        [UnityTest] public IEnumerator ComputeUpgradeUpdatesCurrentAndCatalogueAbilityPeriodWithoutChangingPoints()
        {
            EditorSceneManager.OpenScene("Assets/_Game/Scenes/Prototypes/CoreGameplay.unity");yield return new EnterPlayMode();yield return null;
            var owner=Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None).Single(IsGameplayOwner);
            var sim=(TaskSimulation)owner.GetType().GetProperty("Simulation").GetValue(owner);sim.Paused=true;sim.TrySetTokenThroughput(10);
            var cells=OpenCells(sim);
            var session=MakeHardwareUiSession(sim,cells);var model=session.ActiveModel;var points=sim.Model.Points.ToDictionary(p=>p.Key,p=>p.Value);
            var training=owner.GetType().GetProperty("Training").GetValue(owner);Call(training,"BindRestoredSession",sim,session,(Func<bool>)(()=>true));
            var canvas=Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None).Single(IsGameplayCanvas);
            Call(canvas,"Refresh");Assert.That(((Text)Field(canvas,"modelLabel")).text,Does.Contain("算力×1"));
            Assert.That(session.UpgradeCompute(),Is.EqualTo(TrainingResult.Success));Call(canvas,"Refresh");
            Assert.That(((Text)Field(canvas,"modelLabel")).text,Does.Contain("能力周期 "+(sim.Model.Interval/1.5).ToString("0.##")+"s · 算力×1.5"));
            Assert.That(((Text)Field(canvas,"clockLabel")).text,Does.Contain("周期 "+(sim.Model.Interval/1.5).ToString("0.##")+"s"));
            Assert.That(sim.Model.Points,Is.EquivalentTo(points));
            var gallery=Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include,FindObjectsSortMode.None).Single(IsModelGallery);
            gallery.GetType().GetField("controller",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(gallery,training);
            var card=Object.Instantiate((RectTransform)Field(gallery,"modelTemplate"));
            gallery.GetType().GetMethod("BindCard",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(gallery,new object[]{card,model});
            Assert.That(card.Find("Interval").GetComponent<Text>().text,Does.Contain((model.Definition.Interval/1.5).ToString("0.###")+"s · 算力×1.5"));
            Object.Destroy(card.gameObject);yield return new ExitPlayMode();
        }
        private static Cell[] OpenCells(TaskSimulation sim)=>Enumerable.Range(0,sim.Width*sim.Height).Select(i=>new Cell(i%sim.Width,i/sim.Width)).Where(sim.IsOpen).ToArray();
        private static bool IsGameplayOwner(MonoBehaviour value)=>value.GetType().Name=="CoreGameplayController";
        private static bool IsGameplayCanvas(MonoBehaviour value)=>value.GetType().Name=="CoreGameplayCanvasView";
        private static bool IsModelGallery(MonoBehaviour value)=>value.GetType().Name=="ModelGalleryView";
        private static TrainingSession MakeHardwareUiSession(TaskSimulation sim,Cell[] cells)=>new TrainingSession(sim,
            new CatalogModel("Hardware UI model","baseline",sim.Model,null),
            new[]{new TrainingRecipe("normal","Normal",20,1,100,1,1,new[]{new Cell(0,0)},100,100)},
            new[]{new ArchitectureDefinition("baseline","Base",1,OutlineTransform.Keep)},new[]{new ComputeTier("one",10,0),new ComputeTier("two",15,0)},
            cost=>true,cost=>{},profile=>sim.TryChangeExecutionProfile(profile.Model,profile.TokensPerSecond,profile.OpenCells,profile.Architecture),rate=>sim.TrySetTokenThroughput(rate),()=>cells);
        private static Cell ToCell(int i)=>new Cell(i%8,i/8);
        [UnityTearDown] public IEnumerator Cleanup(){if(EditorApplication.isPlaying)yield return new ExitPlayMode();}
    }
}
