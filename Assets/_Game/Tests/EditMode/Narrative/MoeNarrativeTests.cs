using System;
using System.Linq;
using CallmeCatgirl.Gameplay.Narrative;
using NUnit.Framework;
using UnityEngine;

namespace CallmeCatgirl.Tests
{
    public sealed class MoeNarrativeTests
    {
        private static NarrativeSession AtMoe()
        {
            var session=NarrativeSession.AdoptLegacy(1);session.ObserveMemoryCount(2);
            Assert.That(session.OpenContact(NarrativeSession.DoctorId),Is.True);
            Assert.That(session.CurrentFrame.Id,Is.EqualTo("stage.2"));return session;
        }
        [TestCase("moe-experts","stage.2.moe")] [TestCase("moe-training","stage.2.training")]
        public void ExplanationBranchesRestoreAndReturnWithoutCompletingTheStage(string choice,string expectedNode)
        {
            var session=AtMoe();var pending=session.PendingEffects.Select(e=>e.EventId).ToArray();
            Assert.That(session.CurrentFrame.Choices.Select(c=>c.Id),Does.Contain("first"));
            Assert.That(session.CurrentFrame.Choices.Select(c=>c.Id),Does.Contain("second"));
            Assert.That(session.CurrentFrame.Choices.Select(c=>c.Id),Does.Contain("third"));
            Assert.That(session.Choose(choice),Is.True);Assert.That(session.CurrentFrame.Id,Is.EqualTo(expectedNode));
            var restored=NarrativeSession.Restore(JsonUtility.FromJson<NarrativeSaveData>(JsonUtility.ToJson(session.Capture())));
            Assert.That(restored.CurrentFrame.Id,Is.EqualTo(expectedNode));
            Assert.That(restored.Remembered("stage.2"),Is.Null);Assert.That(restored.Capture().PendingStageNodes,Does.Contain("stage.2"));
            Assert.That(restored.PendingEffects.Select(e=>e.EventId),Is.EqualTo(pending));
            Assert.That(restored.Choose("return"),Is.True);Assert.That(restored.CurrentFrame.Id,Is.EqualTo("stage.2"));
            Assert.That(restored.Choose("first"),Is.True);Assert.That(restored.Remembered("stage.2"),Is.EqualTo("first"));
            Assert.That(restored.PendingEffects.Count(e=>e.Kind==NarrativeEffectKind.QueueStageDoctorRequest),Is.EqualTo(1));
            Assert.That(restored.ObserveMemoryCount(2),Is.False);
            restored.Choose("continue");restored.OpenContact(NarrativeSession.DoctorId);
            Assert.That(restored.CurrentFrame.Id,Is.EqualTo("doctor.legacy"),"The stage introduction is not queued a second time.");
        }
        [Test] public void ConfiguredAmountsAndLegacyPresentationDoNotPromiseUnawardedCorpus()
        {
            var session=AtMoe();session.MoeCorpusPointsProvider=()=>7;
            Assert.That(session.CurrentFrame.Body,Does.Contain("各7点"));Assert.That(session.CurrentFrame.Body,Does.Contain("共28点"));
            Assert.That(session.CurrentFrame.Body,Does.Contain("不会直接改变"));
            session.MoeCorpusGrantedProvider=()=>false;
            Assert.That(session.CurrentFrame.Body,Does.Not.Contain("各7点"));Assert.That(session.CurrentFrame.Body,Does.Not.Contain("共28点"));
            Assert.That(session.CurrentFrame.Body,Does.Contain("不会重复赠送"));
            Assert.That(session.CurrentFrame.Choices.Single(c=>c.Id=="moe-training").Text,Does.Not.Contain("28"));
            session.Choose("moe-experts");Assert.That(session.CurrentFrame.Body,Does.Contain("已有的语料"));
            session.Choose("return");session.MoeCorpusGrantedProvider=()=>true;session.Choose("moe-training");
            Assert.That(session.CurrentFrame.Body,Does.Contain("28点"));Assert.That(session.CurrentFrame.Body,Does.Contain("幂函数"));
            Assert.That(session.CurrentFrame.Body,Does.Contain("格子"));
        }
    }
}
