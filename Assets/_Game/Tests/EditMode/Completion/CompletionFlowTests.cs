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

        [UnityTest]
        public IEnumerator SecondTrainingDefaultDragRequiresNinePointsAndRecovers()
        {
            EditorSceneManager.OpenScene("Assets/_Game/Scenes/Prototypes/CoreGameplay.unity");
            yield return new EnterPlayMode(); yield return null;
            var owner=UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None)
                .Single(c=>c.GetType().Name=="CoreGameplayController");
            Call(owner,"ConfigureSaveSlot","tutorial-ui-nine-"+Guid.NewGuid().ToString("N"));
            Api(owner,"{\"kind\":\"clock\",\"paused\":true}");
            var narrative=Get<NarrativeSession>(owner,"Narrative");
            var simulation=Get<TaskSimulation>(owner,"Simulation");
            Assert.That(narrative.Choose("mew"),Is.True);
            yield return null; CloseNarrative(narrative); yield return null;

            Api(owner,"{\"kind\":\"train\",\"recipe\":\"tutorial-first-model\",\"architecture\":\"baseline\",\"displayName\":\"第一台模型\",\"investments\":[{\"ConversationId\":\"narrative-doctor\",\"Points\":1}]}");
            var session=Get<TrainingSession>(Get<object>(owner,"Training"),"Session");
            var starter=session.Jobs.Single();
            Finish(owner,simulation,session,starter.RequestId);
            yield return null; CloseNarrative(narrative); yield return null; CloseNarrative(narrative); yield return null;
            Assert.That(session.CompactTrainingAvailable,Is.True);
            Assert.That(session.GetConversationCorpus(NarrativeSession.DoctorId).AvailablePoints,Is.EqualTo(9));

            Api(owner,"{\"kind\":\"pause\",\"paused\":true}");
            Assert.That(Get<bool>(owner,"IsModalOpen"),Is.False,"A tutorial frame must not block the training page.");
            Call(owner,"OpenTraining"); yield return null;
            var gallery=UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include,FindObjectsSortMode.None)
                .Single(c=>c.GetType().Name=="ModelGalleryView");
            Field<Button>(gallery,"pageButton").onClick.Invoke(); yield return null;
            var workspace=UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include,FindObjectsSortMode.None)
                .Single(c=>c.GetType().Name=="TrainingWorkspaceView");
            Assert.That(Field<string>(workspace,"recipe"),Is.EqualTo(TrainingSession.TutorialCorpusRecipeId));
            Assert.That((bool)Call(workspace,"TryPlaceConversation",NarrativeSession.DoctorId,0),Is.True);
            Assert.That(Get<double>(workspace,"InvestedPoints"),Is.EqualTo(9d));
            Assert.That(Field<Button>(workspace,"start").interactable,Is.True);

            var amount=Field<Slider>(workspace,"amount");
            amount.value=1; yield return null;
            Assert.That(Get<double>(workspace,"InvestedPoints"),Is.EqualTo(1d));
            Assert.That(Field<Button>(workspace,"start").interactable,Is.False,
                "The tutorial compact recipe must reject a one-point draft.");
            int jobsBefore=session.Jobs.Count, corpusBefore=session.Corpus.Count;
            Field<Button>(workspace,"start").onClick.Invoke(); yield return null;
            Assert.That(session.Jobs.Count,Is.EqualTo(jobsBefore),"A rejected one-point draft must not create a job.");
            Assert.That(session.Corpus.Count,Is.EqualTo(corpusBefore),"A rejected one-point draft must not consume corpus.");
            Assert.That(session.CompactTrainingAvailable,Is.True,"The recipe must stay available after rejection.");
            Assert.That(session.GetConversationCorpus(NarrativeSession.DoctorId).AvailablePoints,Is.EqualTo(9));
            Assert.That(Field<Text>(workspace,"message").text,Does.Contain("9"));

            amount.value=9; yield return null;
            Assert.That(Field<Button>(workspace,"start").interactable,Is.True);
            Field<Button>(workspace,"start").onClick.Invoke(); yield return null;
            Assert.That(session.Jobs.Count,Is.EqualTo(jobsBefore+1));
            var compact=session.Jobs.Last();
            Assert.That(compact.RecipeId,Is.EqualTo(TrainingSession.TutorialCorpusRecipeId));
            Assert.That(compact.PaidCost,Is.EqualTo(20));
            Assert.That(simulation.Tasks[compact.RequestId].Definition.Shape.Count,Is.EqualTo(1));
            Assert.That(session.CompactTrainingAvailable,Is.True,"A pending nine-point job must not hide the recipe.");
            Finish(owner,simulation,session,compact.RequestId); yield return null;
            Assert.That(compact.Completed,Is.True);
            Assert.That(session.ActiveModelId,Is.EqualTo(compact.Output.Id));
            Assert.That(compact.Output.Definition.Points.Values.Sum(),Is.EqualTo(9d));
            Assert.That(session.CompactTrainingAvailable,Is.False,"The recipe closes only after a nine-point model exists.");
            yield return new ExitPlayMode();
        }

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
            Assert.That(simulation.Tasks.ContainsKey(NarrativeSession.FirstDoctorRequestId),Is.False,
                "The first doctor order is narrative-only and must not create a task.");
            Assert.That(Get<int>(owner,"Funds"),Is.EqualTo(100));
            Assert.That(simulation.Tasks.ContainsKey(NarrativeSession.SecondDoctorRequestId),Is.True);
            var secondRequest=simulation.Tasks[NarrativeSession.SecondDoctorRequestId].Definition;
            Assert.That(secondRequest.Requirements.Single().Kind,Is.EqualTo(expectedAbility));
            Assert.That(secondRequest.Requirements.Single().Minimum,Is.EqualTo(6));
            Assert.That(secondRequest.Calculation,Is.EqualTo(20));
            Assert.That(secondRequest.Shape.Count,Is.EqualTo(1));

            // The doctor's 9-point grant is already in inventory; the compact 1-cell recipe keeps it on the single open cell.
            Assert.That(session.Corpus.Count,Is.EqualTo(2));
            Api(owner,"{\"kind\":\"train\",\"recipe\":\"tutorial-corpus-model\",\"architecture\":\"baseline\",\"displayName\":\"第二台模型\",\"investments\":[{\"ConversationId\":\"narrative-doctor\",\"Points\":9}]}");
            var secondTraining=session.Jobs.Last();
            Assert.That(secondTraining.PaidCost,Is.EqualTo(20));
            Assert.That(simulation.Tasks[secondTraining.RequestId].Definition.Shape.Count,Is.EqualTo(1));
            Finish(owner,simulation,session,secondTraining.RequestId);
            yield return null;
            Assert.That(secondTraining.Completed,Is.True);
            Assert.That(session.ActiveModelId,Is.EqualTo(secondTraining.Output.Id));
            Assert.That(session.ActiveModel.Definition.Points[expectedAbility],Is.EqualTo(9));
            CloseNarrative(narrative); yield return null;

            Finish(owner,simulation,session,NarrativeSession.SecondDoctorRequestId);
            yield return null; CloseNarrative(narrative); yield return null;
            Assert.That(simulation.Tasks.ContainsKey(NarrativeSession.ThirdDoctorRequestId),Is.True);
            var thirdRequest=simulation.Tasks[NarrativeSession.ThirdDoctorRequestId].Definition;
            Assert.That(thirdRequest.Requirements.Single().Kind,Is.EqualTo(expectedAbility));
            Assert.That(thirdRequest.Requirements.Single().Minimum,Is.EqualTo(6));
            Finish(owner,simulation,session,NarrativeSession.ThirdDoctorRequestId);
            yield return null; CloseNarrative(narrative); yield return null;
            Assert.That(simulation.Tasks.ContainsKey(NarrativeSession.FirstOrdinaryRequestId),Is.True);
            Assert.That(simulation.Tasks[NarrativeSession.FirstOrdinaryRequestId].Definition.Requirements.Single().Kind,Is.EqualTo(expectedAbility));
            Finish(owner,simulation,session,NarrativeSession.FirstOrdinaryRequestId);
            yield return null;
            CloseNarrative(narrative);
            var story=Get<object>(owner,"Story");
            if(Get<bool>(story,"IsReply")) Call(owner,"ChooseReplyAtRevision",0,Get<int>(story,"Revision"));
            yield return null; CloseNarrative(narrative); yield return null;
            Assert.That(Get<int>(owner,"CompletedTasks"),Is.EqualTo(3));
            Assert.That(Get<int>(owner,"Funds"),Is.EqualTo(280));
            Call(owner,"UpgradeMemory");
            Assert.That(Get<bool>(owner,"IsMemoryInstallPending"),Is.True);
            var install=owner.GetType().GetField("memoryInstallGame",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(owner);
            Call(install,"SkipInstall"); yield return null;
            Assert.That(Get<int>(owner,"MemoryLevel"),Is.EqualTo(1));
            Assert.That(narrative.MemoryCount,Is.EqualTo(1));
            int funds=Get<int>(owner,"Funds");
            Assert.That(funds,Is.EqualTo(180));
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
        private static T Field<T>(object instance,string name)
            => (T)instance.GetType().GetField(name,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).GetValue(instance);
        private static object Call(object instance,string name,params object[] args)
            => instance.GetType().GetMethods(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)
                .Single(m=>m.Name==name && m.GetParameters().Length==args.Length).Invoke(instance,args);
        [UnityTearDown] public IEnumerator Cleanup(){if(EditorApplication.isPlaying)yield return new ExitPlayMode();}
    }
}
