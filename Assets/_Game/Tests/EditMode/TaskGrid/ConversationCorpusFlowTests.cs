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
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object=UnityEngine.Object;
namespace CallmeCatgirl.Tests
{
    public sealed class ConversationCorpusFlowTests
    {
        private ScriptableObject configuration,story;
        private string slotDirectory;
        private static object Call(object o,string method,params object[] args)=>o.GetType().GetMethod(method).Invoke(o,args);
        private static T Get<T>(object o,string name)=>(T)o.GetType().GetProperty(name).GetValue(o);
        private static object Field(object o,string name)=>o.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(o);
        [UnityTest] public IEnumerator FinalDialogueAwardsHalfHighestThenDragSelectSpendAndRestore()
        {
            EditorSceneManager.OpenScene("Assets/_Game/Scenes/Prototypes/CoreGameplay.unity");yield return new EnterPlayMode();yield return null;
            var owner=Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None).Single(c=>c.GetType().Name=="CoreGameplayController");
            var field=owner.GetType().GetField("configuration",BindingFlags.Instance|BindingFlags.NonPublic);
            configuration=Object.Instantiate((ScriptableObject)field.GetValue(owner));
            var importer=Type.GetType("CallmeCatgirl.EditorTools.StoryTextImporter, Assembly-CSharp-Editor",true);
            const string text="角色：林小满\n角色ID：A\n计算量：30\n计算速度：30\n忍耐时间：100\nToken收入：0\n达标能力：写作10，代码2，推理2\n优秀能力：写作20，代码3，推理3\n方块：\n■\n【会话：语料测试】\n【第1轮】\n用户：你好。\n选项1：你好。\n【第2轮】\n用户：今天好吗？\n选项1：很好。\n【第3轮】\n用户：再见。\n选项1：再见。";
            story=(ScriptableObject)importer.GetMethod("Parse").Invoke(null,new object[]{text,new[]{"A","B","C"},null});
            ShareSessionsWithSecondCharacter(story);
            foreach(var user in (Array)story.GetType().GetField("Users").GetValue(story))
            {var kind=user.GetType().GetField("Kind");kind.SetValue(user,Enum.Parse(kind.FieldType,"Scripted"));}
            foreach(string name in new[]{"TaskGeneration","OrdinaryDialoguePool","OpeningBalance"})configuration.GetType().GetField(name).SetValue(configuration,null);
            configuration.GetType().GetField("Story").SetValue(configuration,story);
            configuration.GetType().GetField("ModelPoints").SetValue(configuration,new Vector4(100,100,100,100));
            configuration.GetType().GetField("AbilityInterval").SetValue(configuration,1f);
            field.SetValue(owner,configuration);Call(owner,"ResetSimulation");
            owner.GetType().GetProperty("ManualCommandClock").SetValue(owner,true);
            var sim=Get<TaskSimulation>(owner,"Simulation");var task=sim.Tasks["A-1"];
            Assert.That(task.Definition.Requirements.Max(r=>r.Minimum),Is.EqualTo(10));
            var cell=FindSpot(sim,task.Id);
            Assert.That(sim.TryPlace(task.Id,cell,0,out _),Is.True);
            sim.Paused=false;sim.Advance(30);sim.Paused=true;Call(owner,"SelectRequest",task.Id);Call(owner,"SubmitSelected");
            var training=Get<object>(owner,"Training");var session=Get<object>(training,"Session");
            Assert.That(((IEnumerable)Get<object>(session,"Corpus")).Cast<object>().Count(),Is.Zero);
            Call(owner,"ChooseReply",0);Assert.That(((IEnumerable)Get<object>(session,"Corpus")).Cast<object>().Count(),Is.Zero);
            Call(owner,"ChooseReply",0);Assert.That(((IEnumerable)Get<object>(session,"Corpus")).Cast<object>().Count(),Is.Zero);
            Call(owner,"ChooseReply",0);Call(owner,"ChooseReply",0);
            var records=((IEnumerable)Get<object>(session,"Corpus")).Cast<object>().ToArray();Assert.That(records.Length,Is.EqualTo(1));
            var scores=Get<IReadOnlyDictionary<AbilityKind,double>>(records[0],"Scores");Assert.That(scores.Count,Is.EqualTo(1));Assert.That(scores[AbilityKind.Writing],Is.EqualTo(8));
            AwardSecondSource(owner,sim);yield return null;
            Call(owner,"OpenTraining");yield return null;
            var gallery=Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include,FindObjectsSortMode.None).Single(c=>c.GetType().Name=="ModelGalleryView");
            var reveal=Field(gallery,"reveal");reveal.GetType().GetProperty("ReducedMotion").SetValue(reveal,true);
            reveal.GetType().GetMethod("OnApplicationFocus",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(reveal,new object[]{true});
            yield return null;
            reveal.GetType().GetMethod("LateUpdate",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(reveal,null);
            yield return null;
            typeof(SystemFeedbackTests).GetMethod("Capture",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{"ArchitectureCatalogue.png"});
            typeof(SystemFeedbackTests).GetMethod("Capture",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{"ArchitectureCatalogue.png"});
            ((Button)Field(gallery,"pageButton")).onClick.Invoke();yield return null;
            var view=Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None).Single(c=>c.GetType().Name=="TrainingWorkspaceView");
            var options=((RectTransform)Field(view,"architectures")).GetComponentsInChildren<Button>();
            Assert.That(options.Length,Is.EqualTo(3));
            Assert.That(options.Single(b=>b.name=="baseline").GetComponentInChildren<Text>().text,Does.Contain("语料×1.6"));
            Assert.That(options.Single(b=>b.name=="alternate").GetComponentInChildren<Text>().text,Does.Contain("内存2级"));
            Assert.That(options.Single(b=>b.name=="architecture-slot-3").GetComponentInChildren<Text>().text,Does.Contain("内存4级"));
            Assert.That(options.Single(b=>b.name=="alternate").interactable,Is.False);
            Assert.That(options.Single(b=>b.name=="architecture-slot-3").interactable,Is.False);
            var drag=Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None).First(c=>c.GetType().Name=="ConversationCorpusDrag");
            var drop=Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None).Single(c=>c.GetType().Name=="ConversationCorpusDrop");
            var pointer=new PointerEventData(EventSystem.current){pointerDrag=drag.gameObject,position=RectTransformUtility.WorldToScreenPoint(null,drop.transform.TransformPoint(((RectTransform)drop.transform).rect.center))};
            ExecuteEvents.Execute(drag.gameObject,pointer,ExecuteEvents.beginDragHandler);
            Canvas.ForceUpdateCanvases();var hits=new List<RaycastResult>();EventSystem.current.RaycastAll(pointer,hits);
            Assert.That(hits.Count,Is.GreaterThan(0));Assert.That(ExecuteEvents.GetEventHandler<IDropHandler>(hits[0].gameObject),Is.EqualTo(drop.gameObject));
            ExecuteEvents.Execute(drop.gameObject,pointer,ExecuteEvents.dropHandler);
            ExecuteEvents.Execute(drag.gameObject,pointer,ExecuteEvents.endDragHandler);
            Assert.That(Get<string>(view,"SelectedConversation"),Is.EqualTo("A"));
            ((Slider)Field(view,"corpusAmount")).value=2;
            Assert.That(Get<double>(view,"InvestedPoints"),Is.EqualTo(2));
            typeof(SystemFeedbackTests).GetMethod("Capture",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{"ConversationCorpusInvestment.png"});
            typeof(SystemFeedbackTests).GetMethod("Capture",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{"ConversationCorpusInvestment.png"});
            // A second source is added without replacing A. Its own slider amount and removal are independent.
            Call(view,"SelectConversation","B");((Slider)Field(view,"corpusAmount")).value=3;
            Assert.That(((IEnumerable)Get<object>(view,"Investments")).Cast<object>().Count(),Is.EqualTo(2));
            Assert.That(Get<double>(view,"InvestedPoints"),Is.EqualTo(3));
            Call(view,"SelectConversation","A");Assert.That(Get<double>(view,"InvestedPoints"),Is.EqualTo(2));
            Call(view,"SelectConversation","B");Call(view,"RemoveSelectedConversation");
            Assert.That(Get<string>(view,"SelectedConversation"),Is.EqualTo("A"));Assert.That(Get<double>(view,"InvestedPoints"),Is.EqualTo(2));
            Call(view,"SelectConversation","B");((Slider)Field(view,"corpusAmount")).value=2;
            Assert.That(((Text)Field(view,"investmentScores")).text,Does.Contain("2 段 · 共 4 点"));
            Assert.That(((Text)Field(view,"investmentScores")).text,Does.Contain("计算量 160"));
            var invalid=(string)Call(owner,"ExecuteCommandJson","{\"kind\":\"train\",\"recipe\":\"dialogue-training\",\"architecture\":\"baseline\",\"displayName\":\"invalid\",\"conversationId\":\"A\",\"investments\":[{\"ConversationId\":\"B\",\"Points\":2}]}");
            Assert.That(invalid,Does.Contain("InvalidInput"));Assert.That(((IEnumerable)Get<object>(session,"Jobs")).Cast<object>().Count(),Is.Zero);
            typeof(SystemFeedbackTests).GetMethod("Capture",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{"MultiCorpusTraining.png"});
            typeof(SystemFeedbackTests).GetMethod("Capture",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{"MultiCorpusTraining.png"});
            ((Button)Field(view,"trainButton")).onClick.Invoke();
            var summary=Call(session,"GetConversationCorpus","A");Assert.That(Get<double>(summary,"AvailablePoints"),Is.EqualTo(6));
            var jobs=((IEnumerable)Get<object>(session,"Jobs")).Cast<object>().ToArray();Assert.That(jobs.Length,Is.EqualTo(1));
            string request=Get<string>(jobs[0],"RequestId");Assert.That(sim.Tasks[request].Definition.Shape.Count,Is.EqualTo(8));
            Assert.That(sim.Tasks[request].Definition.Calculation,Is.EqualTo(160).Within(1e-8));
            Assert.That(Get<double>(Call(session,"GetConversationCorpus","B"),"AvailablePoints"),Is.EqualTo(6));
            Call(owner,"ConfigureSaveSlot","corpus-flow-"+Guid.NewGuid().ToString("N"));
            slotDirectory=System.IO.Path.GetDirectoryName(Get<string>(owner,"SavePath"));
            Assert.That(Call(owner,"SaveRun"),Is.EqualTo(true));Assert.That(Call(owner,"ContinueRun"),Is.EqualTo(true));
            session=Get<object>(Get<object>(owner,"Training"),"Session");
            Assert.That(Get<double>(Call(session,"GetConversationCorpus","A"),"AvailablePoints"),Is.EqualTo(6));
            yield return new ExitPlayMode();
        }
        private static void ShareSessionsWithSecondCharacter(ScriptableObject story)
        {
            var field=story.GetType().GetField("Users");var users=(Array)field.GetValue(story);var first=users.GetValue(0);
            var second=JsonUtility.FromJson(JsonUtility.ToJson(first),first.GetType());
            second.GetType().GetField("UserId").SetValue(second,"B");second.GetType().GetField("DisplayName").SetValue(second,"周序");
            var combined=Array.CreateInstance(first.GetType(),2);combined.SetValue(first,0);combined.SetValue(second,1);field.SetValue(story,combined);
        }
        private static void AwardSecondSource(object owner,TaskSimulation sim)
        {
            Assert.That(sim.TryPlace("B-1",FindSpot(sim,"B-1"),0,out _),Is.True);
            sim.Paused=false;sim.Advance(30);sim.Paused=true;
            Call(owner,"SelectRequest","B-1");Call(owner,"SubmitSelected");
            Call(owner,"ChooseReply",0);Call(owner,"ChooseReply",0);Call(owner,"ChooseReply",0);
        }
        private static Cell FindSpot(TaskSimulation sim,string id) => Enumerable.Range(0,sim.Width*sim.Height).Select(i=>new Cell(i%sim.Width,i/sim.Width)).First(c=>sim.Preview(id,c,0)==PlacementFailure.None);
        [UnityTearDown] public IEnumerator Cleanup()
        {
            if(EditorApplication.isPlaying)yield return new ExitPlayMode();
            if(configuration!=null)Object.DestroyImmediate(configuration);if(story!=null)Object.DestroyImmediate(story);
            if(slotDirectory!=null&&System.IO.Directory.Exists(slotDirectory))System.IO.Directory.Delete(slotDirectory,true);
        }
    }
}
