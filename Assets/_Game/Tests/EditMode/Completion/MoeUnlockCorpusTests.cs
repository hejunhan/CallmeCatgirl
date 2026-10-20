using System;
using System.Collections;
using System.Collections.Generic;
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
using Object=UnityEngine.Object;

namespace CallmeCatgirl.Tests
{
    public sealed class MoeUnlockCorpusTests
    {
        private const string RewardEvent="narrative-architecture-2:doctor-corpus";
        private static T Get<T>(object target,string name)
        {
            var p=target.GetType().GetProperty(name);
            return (T)(p!=null?p.GetValue(target):target.GetType().GetField(name).GetValue(target));
        }
        private static object Field(object target,string name)=>target.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(target);
        private static void SetPrivate(object target,string name,object value)=>target.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(target,value);
        private static object Call(object target,string name,params object[] args)=>target.GetType().GetMethods(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)
            .Single(m=>m.Name==name&&m.GetParameters().Length==args.Length).Invoke(target,args);
        private static TrainingSession Training(object owner)=>Get<TrainingSession>(Get<object>(owner,"Training"),"Session");
        private static NarrativeSession Narrative(object owner)=>Get<NarrativeSession>(owner,"Narrative");
        private static MonoBehaviour Owner()=>Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None).Single(c=>c.GetType().Name=="CoreGameplayController");
        private static void PrepareLegacyFixture(object owner,bool acknowledged)
        {
            // Explicit subsystem fixture: valid legacy stage/outline, not a claim of playing the full tutorial.
            Call(owner,"ResetSimulation");owner.GetType().GetProperty("ManualCommandClock").SetValue(owner,true);
            Call(owner,"ConfigureSaveSlot","moe-reward-"+Guid.NewGuid().ToString("N"));
            var original=Narrative(owner);
            var narrative=NarrativeSession.AdoptLegacy(acknowledged?2:1);
            if(acknowledged)
            {
                var state=narrative.Capture();state.AcknowledgedEventIds=new[]{"narrative-architecture-2"};state.PendingStageNodes=new[]{"stage.2"};
                narrative=NarrativeSession.Restore(state);
            }
            else narrative.ObserveMemoryCount(2);
            narrative.MoeCorpusPointsProvider=original.MoeCorpusPointsProvider;narrative.MoeCorpusGrantedProvider=original.MoeCorpusGrantedProvider;
            owner.GetType().GetProperty("Narrative").SetValue(owner,narrative);owner.GetType().GetProperty("MemoryLevel").SetValue(owner,2);
            var config=Field(owner,"configuration");var memory=config.GetType().GetField("MemoryUpgrades").GetValue(config);
            Assert.That(Get<TaskSimulation>(owner,"Simulation").TryUnlockCells((IEnumerable<Cell>)Call(memory,"GetOpenCells",2)),Is.True);
            Call(Field(owner,"narrativePanel"),"Bind",narrative,Call(owner,"CompletionFont"));
        }
        private static void SaveAndRestore(object owner)
        {
            Assert.That(Call(owner,"SaveRun"),Is.EqualTo(true),Get<string>(owner,"Message"));
            Assert.That(Call(owner,"ContinueRun"),Is.EqualTo(true),Get<string>(owner,"Message"));
        }
        private static void AssertReward(object owner)
        {
            var reward=Training(owner).Corpus.Single(r=>r.EventId==RewardEvent);
            Assert.That(reward.ConversationId,Is.EqualTo(NarrativeSession.DoctorId));Assert.That(reward.Scores.Count,Is.EqualTo(4));
            foreach(AbilityKind kind in Enum.GetValues(typeof(AbilityKind)))Assert.That(reward.Scores[kind],Is.EqualTo(5));
            Assert.That(reward.Scores.Values.Sum(),Is.EqualTo(20));
        }
        private static void CheckDefaultRevision(object owner)
        {
            var training=Get<object>(owner,"Training");var original=(ScriptableObject)Get<object>(training,"Configuration");
            var clone=Object.Instantiate(original);clone.name=original.name;
            try
            {
                SetPrivate(training,"configuration",clone);
                clone.GetType().GetField("MoeUnlockCorpusPointsPerAbility").SetValue(clone,5d);
                clone.GetType().GetField("LongTrainingWarningSeconds").SetValue(clone,300d);
                string baseline=(string)Call(owner,"CalculateContentRevision",false,false);
                clone.GetType().GetField("MoeUnlockCorpusPointsPerAbility").SetValue(clone,6d);
                Assert.That(Call(owner,"CalculateContentRevision",false,false),Is.Not.EqualTo(baseline));
                clone.GetType().GetField("MoeUnlockCorpusPointsPerAbility").SetValue(clone,5d);
                clone.GetType().GetField("LongTrainingWarningSeconds").SetValue(clone,301d);
                Assert.That(Call(owner,"CalculateContentRevision",false,false),Is.Not.EqualTo(baseline));
                clone.GetType().GetField("LongTrainingWarningSeconds").SetValue(clone,300d);
                Assert.That(Call(owner,"CalculateContentRevision",false,false),Is.EqualTo(baseline));
            }
            finally{SetPrivate(training,"configuration",original);Object.DestroyImmediate(clone);}
        }
        [UnityTest] public IEnumerator MoeGrantRetriesOnceAcrossNativeSavesWithoutChangingModelAndDoesNotBackfillOldAcknowledgedRuns()
        {
            EditorSceneManager.OpenScene("Assets/_Game/Scenes/Prototypes/CoreGameplay.unity");yield return new EnterPlayMode();yield return null;
            CheckRewardLifecycle(Owner());yield return new ExitPlayMode();
        }
        private static void CheckRewardLifecycle(object owner)
        {
            CheckDefaultRevision(owner);PrepareLegacyFixture(owner,false);
            var before=Get<TaskSimulation>(owner,"Simulation").Model.Points.ToDictionary(p=>p.Key,p=>p.Value);
            SaveAndRestore(owner);
            Assert.That(Training(owner).Corpus,Is.Empty);Assert.That(Narrative(owner).PendingEffects.Any(e=>e.EventId=="narrative-architecture-2"),Is.True);
            var session=Training(owner);SetPrivate(session,"busy",true);
            try{Call(owner,"ProcessCompletion");Assert.That(Narrative(owner).PendingEffects.Any(e=>e.EventId=="narrative-architecture-2"),Is.True);Assert.That(session.Corpus,Is.Empty);}
            finally{SetPrivate(session,"busy",false);}
            var effect=Narrative(owner).PendingEffects.Single(e=>e.EventId=="narrative-architecture-2");
            Assert.That(Call(owner,"ApplyNarrativeEffect",effect),Is.EqualTo(true));AssertReward(owner);
            Assert.That(Call(owner,"ApplyNarrativeEffect",effect),Is.EqualTo(true));Assert.That(session.Corpus.Count,Is.EqualTo(1));
            SaveAndRestore(owner);AssertReward(owner);Call(owner,"ProcessCompletion");
            Assert.That(Narrative(owner).PendingEffects.Any(e=>e.EventId=="narrative-architecture-2"),Is.False);
            Assert.That(Training(owner).Corpus.Count,Is.EqualTo(1));
            Assert.That(Get<TaskSimulation>(owner,"Simulation").Model.Points,Is.EquivalentTo(before));
            // Spending the entire reward keeps its original issue record, so historic text must not switch to 'never granted'.
            Call(Get<object>(owner,"Economy"),"AddFunds",20);session=Training(owner);
            Assert.That(session.StartConversationTraining("dialogue-training","baseline","Spent grant",NarrativeSession.DoctorId,20,out _),Is.EqualTo(TrainingResult.Success));
            Assert.That(session.GetConversationCorpus(NarrativeSession.DoctorId).AvailablePoints,Is.Zero);
            Narrative(owner).OpenContact(NarrativeSession.DoctorId);
            Assert.That(Narrative(owner).CurrentFrame.Body,Does.Contain("各5点"));
            Narrative(owner).Choose("moe-experts");Assert.That(Narrative(owner).CurrentFrame.Body,Does.Contain("剩余点数以训练页显示为准"));
            SaveAndRestore(owner);Call(owner,"ProcessCompletion");AssertReward(owner);Assert.That(Training(owner).Corpus.Count,Is.EqualTo(1));
            Assert.That(Training(owner).GetConversationCorpus(NarrativeSession.DoctorId).AvailablePoints,Is.Zero);
            Assert.That(Get<TaskSimulation>(owner,"Simulation").Model.Points,Is.EquivalentTo(before));
            foreach(var stage in new[]{1,4})
                Assert.That(Call(owner,"ApplyNarrativeEffect",new NarrativeEffect{EventId="narrative-architecture-"+stage,Kind=NarrativeEffectKind.IntroduceArchitecture,ArchitectureId=stage==1?"baseline":"architecture-slot-3"}),Is.EqualTo(true));
            Assert.That(Training(owner).Corpus.Count,Is.EqualTo(1));
            PrepareLegacyFixture(owner,true);Call(owner,"ProcessCompletion");Assert.That(Training(owner).Corpus,Is.Empty);
            Narrative(owner).OpenContact(NarrativeSession.DoctorId);
            Assert.That(Narrative(owner).CurrentFrame.Body,Does.Not.Contain("各5点"));Assert.That(Narrative(owner).CurrentFrame.Body,Does.Not.Contain("共20点"));
            Assert.That(Narrative(owner).CurrentFrame.Body,Does.Contain("不会重复赠送"));
            SaveAndRestore(owner);Call(owner,"ProcessCompletion");Assert.That(Training(owner).Corpus,Is.Empty);
        }
        [UnityTearDown] public IEnumerator Cleanup(){if(EditorApplication.isPlaying)yield return new ExitPlayMode();}
    }
}
