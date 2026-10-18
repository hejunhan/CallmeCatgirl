using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using CallmeCatgirl.Gameplay.Narrative;
using CallmeCatgirl.Gameplay.TaskGrid;
using CallmeCatgirl.Gameplay.Training;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace CallmeCatgirl.Tests
{
    public sealed class CompletionFlowTests
    {
        [UnityTest] public IEnumerator WritingOpeningReachesFirstInstallationAndCanContinue() => Tutorial("mew", AbilityKind.Writing);
        [UnityTest] public IEnumerator ReasoningOpeningReachesFirstInstallationAndCanContinue() => Tutorial("accept-role", AbilityKind.Reasoning);

        [UnityTest] public IEnumerator PinnedDoctorReopensAndSecondEscapeOpensPauseWithoutChangingManualPause()
        {
            EditorSceneManager.OpenScene("Assets/_Game/Scenes/Prototypes/CoreGameplay.unity");
            yield return new EnterPlayMode(); yield return null;
            var owner=UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None).Single(c=>c.GetType().Name=="CoreGameplayController");
            Api(owner,"{\"kind\":\"clock\",\"paused\":true}");
            var narrative=Get<NarrativeSession>(owner,"Narrative");
            var doctor=GameObject.Find("Conversation_"+NarrativeSession.DoctorId);
            Assert.That(doctor,Is.Not.Null);
            Assert.That(doctor.transform.Find("Name").GetComponent<Text>().text,Does.Contain("博士"));
            Assert.That(doctor.transform.GetSiblingIndex(),Is.Zero);
            Assert.That(narrative.Choose("mew"),Is.True);yield return null;CloseNarrative(narrative);yield return null;
            var workspace=UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None).Single(c=>c.GetType().Name=="ConversationWorkspaceView");
            Call(workspace,"SelectConversation",NarrativeSession.DoctorId,false);
            Assert.That(narrative.CurrentFrame,Is.Not.Null);CloseNarrative(narrative);
            var story=Get<object>(owner,"Story");Call(story,"OpenHistory","ui-history-check");yield return null;
            var canvas=UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None).Single(c=>c.GetType().Name=="CoreGameplayCanvasView");
            Assert.That(Get<bool>(canvas,"IsConversationVisible"),Is.True);
            var menu=owner.GetType().GetField("systemMenu",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(owner);
            bool paused=Get<TaskSimulation>(owner,"Simulation").Paused;
            Call(menu,"HandleEscape");yield return null;
            Assert.That(Get<bool>(canvas,"IsConversationVisible"),Is.False);
            Assert.That(Get<bool>(owner,"HasSystemPauseClaim"),Is.False);
            Call(menu,"HandleEscape");yield return null;
            Assert.That(Get<bool>(owner,"HasSystemPauseClaim"),Is.True);
            Assert.That(Get<TaskSimulation>(owner,"Simulation").Paused,Is.EqualTo(paused));
            Call(menu,"CloseAll");
            yield return new ExitPlayMode();
        }

        private IEnumerator Tutorial(string openingChoice, AbilityKind expectedAbility)
        {
            EditorSceneManager.OpenScene("Assets/_Game/Scenes/Prototypes/CoreGameplay.unity");
            yield return new EnterPlayMode(); yield return null;
            var owner = UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None)
                .Single(c => c.GetType().Name == "CoreGameplayController");
            Call(owner,"ConfigureSaveSlot","completion-qa-"+Guid.NewGuid().ToString("N"));
            Api(owner,"{\"kind\":\"clock\",\"paused\":true}");
            var narrative = Get<NarrativeSession>(owner,"Narrative");
            var simulation = Get<TaskSimulation>(owner,"Simulation");
            Assert.That(simulation.OpenCellCount,Is.Zero);
            Assert.That(simulation.Tasks,Is.Empty);
            Assert.That(Get<int>(owner,"MemoryLevel"),Is.Zero);
            // The real displayed choice ID is used; no reward or task state is fabricated by the test.
            if(openingChoice!="mew") openingChoice=narrative.CurrentFrame.Choices.Single(c=>c.Id!="mew").Id;
            Assert.That(narrative.Choose(openingChoice),Is.True);
            yield return null;
            Assert.That(narrative.CenterCellRevealed,Is.True);
            Assert.That(narrative.MemoryCount,Is.Zero);
            Assert.That(simulation.OpenCellCount,Is.EqualTo(1));
            var session=Get<TrainingSession>(Get<object>(owner,"Training"),"Session");
            Assert.That(session.Corpus.Count,Is.EqualTo(1));
            Assert.That(session.Corpus[0].Scores[expectedAbility],Is.EqualTo(1));
            CloseNarrative(narrative);
            Api(owner,"{\"kind\":\"train\",\"recipe\":\"tutorial-first-model\",\"architecture\":\"baseline\",\"displayName\":\"第一台模型\",\"investments\":[{\"ConversationId\":\"narrative-doctor\",\"Points\":1}]}");
            var training=session.Jobs.Single();
            Assert.That(simulation.Tasks[training.RequestId].Definition.Shape.Count,Is.EqualTo(1));
            Assert.That(training.PaidCost,Is.Zero);
            Finish(owner,simulation,session,training.RequestId);
            yield return null;
            Assert.That(training.Completed,Is.True);
            Assert.That(session.ActiveModelId,Is.EqualTo(training.Output.Id));
            CloseNarrative(narrative);
            yield return null;
            Assert.That(simulation.Tasks.ContainsKey(NarrativeSession.FirstDoctorRequestId),Is.True);
            Assert.That(simulation.Tasks[NarrativeSession.FirstDoctorRequestId].Definition.Requirements.Single().Kind,Is.EqualTo(expectedAbility));
            Finish(owner,simulation,session,NarrativeSession.FirstDoctorRequestId);
            yield return null;
            CloseNarrative(narrative); yield return null;
            Assert.That(simulation.Tasks.ContainsKey(NarrativeSession.FirstOrdinaryRequestId),Is.True);
            Assert.That(simulation.Tasks[NarrativeSession.FirstOrdinaryRequestId].Definition.Requirements.Single().Kind,Is.EqualTo(expectedAbility));
            Finish(owner,simulation,session,NarrativeSession.FirstOrdinaryRequestId);
            yield return null;
            CloseNarrative(narrative);
            var story=Get<object>(owner,"Story");
            if(Get<bool>(story,"IsReply")) Call(owner,"ChooseReplyAtRevision",0,Get<int>(story,"Revision"));
            yield return null; CloseNarrative(narrative); yield return null;
            Assert.That(Get<int>(owner,"CompletedTasks"),Is.EqualTo(2));
            Assert.That(Get<int>(owner,"Funds"),Is.EqualTo(120));
            Call(owner,"UpgradeMemory");
            Assert.That(Get<bool>(owner,"IsMemoryInstallPending"),Is.True);
            var install=owner.GetType().GetField("memoryInstallGame",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(owner);
            Call(install,"SkipInstall"); yield return null;
            Assert.That(Get<int>(owner,"MemoryLevel"),Is.EqualTo(1));
            Assert.That(narrative.MemoryCount,Is.EqualTo(1));
            int funds=Get<int>(owner,"Funds");
            Assert.That(funds,Is.EqualTo(20));
            Assert.That((bool)Call(owner,"SaveRun"),Is.True,Get<string>(owner,"Message"));
            Assert.That((bool)Call(owner,"ContinueRun"),Is.True,Get<string>(owner,"Message"));
            yield return null;
            Assert.That(Get<int>(owner,"Funds"),Is.EqualTo(funds));
            Assert.That(Get<NarrativeSession>(owner,"Narrative").MemoryCount,Is.EqualTo(1));
            Assert.That(Get<TrainingSession>(Get<object>(owner,"Training"),"Session").Corpus.Count,Is.GreaterThanOrEqualTo(1));
            yield return new ExitPlayMode();
        }

        private static void CloseNarrative(NarrativeSession narrative)
        {
            for(int i=0;i<8 && narrative.CurrentFrame!=null;i++)
            {
                if(narrative.CurrentFrame.CanClose && narrative.CloseConversation()) break;
                if(narrative.CurrentFrame.Choices.Count==0) break;
                Assert.That(narrative.Choose(narrative.CurrentFrame.Choices[0].Id),Is.True);
            }
        }
        private static void Finish(object owner,TaskSimulation simulation,TrainingSession session,string id)
        {
            int x=(simulation.Width-1)/2,y=(simulation.Height-1)/2;
            Api(owner,"{\"kind\":\"place\",\"id\":\""+id+"\",\"x\":"+x+",\"y\":"+y+"}");
            Api(owner,"{\"kind\":\"pause\",\"paused\":false}");
            // Billing-token requests and calculation-budget jobs intentionally use different effective rates.
            // Advance the real host in bounded steps and observe its terminal state instead of guessing a duration.
            double start=simulation.GameTime;
            for(int step=0;step<240 && simulation.Tasks[id].State==RequestState.Running;step++)
                Api(owner,"{\"kind\":\"advance\",\"seconds\":0.25}");
            var task=simulation.Tasks[id];
            Assert.That(task.State,Is.EqualTo(RequestState.Ready),
                id+" did not finish within 60 game seconds; progress="+task.Progress+"/"+task.Definition.Calculation+", elapsed="+(simulation.GameTime-start));
            Api(owner,"{\"kind\":\"submit\",\"id\":\""+id+"\"}");
        }
        private static object Api(object owner,string json)
        {
            var type=owner.GetType().Assembly.GetType("CallmeCatgirl.Gameplay.GameCommand",true);
            var result=Call(owner,"ExecuteCommand",JsonUtility.FromJson(json,type));
            Assert.That(Get<bool>(result,"ok"),Is.True,json+" => "+Get<string>(result,"code")); return result;
        }
        private static T Get<T>(object instance,string name)
        {
            var type=instance.GetType();var property=type.GetProperty(name);return (T)(property!=null?property.GetValue(instance):type.GetField(name).GetValue(instance));
        }
        private static object Call(object instance,string name,params object[] args)
            => instance.GetType().GetMethods(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)
                .Single(m=>m.Name==name && m.GetParameters().Length==args.Length).Invoke(instance,args);
        [UnityTearDown] public IEnumerator Cleanup(){if(EditorApplication.isPlaying)yield return new ExitPlayMode();}
    }
}
