using System;
using System.Linq;
using System.Collections.Generic;
using CallmeCatgirl.Gameplay.TaskGrid;
using NUnit.Framework;
using UnityEngine;
namespace CallmeCatgirl.Gameplay.Training.Tests
{
    public sealed class ConversationCorpusPointTests
    {
        private sealed class Fixture
        {
            public TaskSimulation Sim;
            public TrainingSession Session;
            public int Money=100;
            public Fixture()
            {
                var cells=Enumerable.Range(0,64).Select(i=>new Cell(i%8,i/8)).ToArray();
                Sim=new TaskSimulation(8,8,cells,new ModelDefinition("starter",1,new Dictionary<AbilityKind,double>{{AbilityKind.Writing,30},{AbilityKind.Code,30},{AbilityKind.Reasoning,30}}));
                Sim.Paused=true;
                Session=new TrainingSession(Sim,new CatalogModel("初始","base",Sim.Model,null),
                    new[]{new TrainingRecipe("train","训练",10,1,100,1,1,new[]{new Cell(0,0),new Cell(1,0)},2,10)},
                    new[]{new ArchitectureDefinition("base","基础",1,OutlineTransform.Keep)},new[]{new ComputeTier("one",1,0)},
                    amount=>{if(Money<amount)return false;Money-=amount;return true;},amount=>Money+=amount,
                    profile=>Sim.TryChangeExecutionProfile(profile.Model,profile.TokensPerSecond,profile.OpenCells,profile.Architecture),rate=>Sim.TrySetTokenThroughput(rate),()=>cells);
            }
            public void Award(string id="request")
            {
                var def=new TaskDefinition("request","测试",new[]{new Cell(0,0)},1,1,100,0,
                    new[]{new AbilityRequirement(AbilityKind.Writing,6,8),new AbilityRequirement(AbilityKind.Reasoning,2,4)});
                Assert.That(Sim.TryCreate(id,"user-A",id,def,out _),Is.True);
                Assert.That(Sim.TryPlace(id,new Cell(0,0),0,out _),Is.True);
                Sim.Paused=false;Sim.Advance(1);Sim.Paused=true;Assert.That(Sim.TrySubmit(id),Is.True);
                Assert.That(Session.RecordDialogue(id,"final","choice",new Dictionary<AbilityKind,double>{{AbilityKind.Writing,3},{AbilityKind.Reasoning,1}},"test"),Is.EqualTo(TrainingResult.Success));
            }
        }
        [Test] public void HighestRequirementUsesCompletionThresholdAndOnlyItsAbility()
        {
            var scores=CorpusRewards.FromRequirements(new[]{new AbilityRequirement(AbilityKind.Writing,10,25),new AbilityRequirement(AbilityKind.Code,2,100),new AbilityRequirement(AbilityKind.Reasoning,2,3)},.5);
            Assert.That(scores.Count,Is.EqualTo(1));Assert.That(scores[AbilityKind.Writing],Is.EqualTo(5));
            Assert.That(CorpusRewards.FromRequirements(Array.Empty<AbilityRequirement>(),.5),Is.Empty);
            Assert.That(CorpusRewards.FromRequirements(new[]{new AbilityRequirement(AbilityKind.Writing,3,4)},.5)[AbilityKind.Writing],Is.EqualTo(1.5));
        }
        [Test] public void TiedHighestDoesNotMultiplyRewardAndRatioIsConfigurable()
        {
            var scores=CorpusRewards.FromRequirements(new[]{new AbilityRequirement(AbilityKind.Code,10,11),new AbilityRequirement(AbilityKind.Writing,10,12)},.25);
            Assert.That(scores.Count,Is.EqualTo(1));Assert.That(scores[AbilityKind.Writing],Is.EqualTo(2.5));
            Assert.Throws<ArgumentOutOfRangeException>(()=>CorpusRewards.FromRequirements(Array.Empty<AbilityRequirement>(),double.NaN));
        }
        [Test] public void AllRequirementsUseEveryCompletionThresholdAtFixedRatio()
        {
            var requirements=new[]{new AbilityRequirement(AbilityKind.Writing,10,25),new AbilityRequirement(AbilityKind.Code,2,100),
                new AbilityRequirement(AbilityKind.Reasoning,4,8),new AbilityRequirement(AbilityKind.Retrieval,6,12)};
            var scores=CorpusRewards.FromAllRequirements(requirements,.25);
            Assert.That(scores.Count,Is.EqualTo(4));Assert.That(scores[AbilityKind.Writing],Is.EqualTo(2.5));
            Assert.That(scores[AbilityKind.Code],Is.EqualTo(.5));Assert.That(scores[AbilityKind.Reasoning],Is.EqualTo(1));
            Assert.That(scores[AbilityKind.Retrieval],Is.EqualTo(1.5));
        }
        [Test] public void AllRequirementsUseIndependentRatios()
        {
            var requirements=new[]{new AbilityRequirement(AbilityKind.Writing,20,100),new AbilityRequirement(AbilityKind.Code,40,100),
                new AbilityRequirement(AbilityKind.Reasoning,60,100),new AbilityRequirement(AbilityKind.Retrieval,80,100)};
            var ratios=new[]{.05,.1,.075,.025};var selected=new List<AbilityRequirement>();
            var scores=CorpusRewards.FromAllRequirements(requirements,requirement=>{selected.Add(requirement);return ratios[selected.Count-1];});
            CollectionAssert.AreEqual(requirements,selected);Assert.That(scores.Count,Is.EqualTo(4));
            Assert.That(scores[AbilityKind.Writing],Is.EqualTo(1).Within(1e-12));Assert.That(scores[AbilityKind.Code],Is.EqualTo(4).Within(1e-12));
            Assert.That(scores[AbilityKind.Reasoning],Is.EqualTo(4.5).Within(1e-12));Assert.That(scores[AbilityKind.Retrieval],Is.EqualTo(2).Within(1e-12));
        }
        [Test] public void AllRequirementsSkipZeroRatios()
        {
            var requirements=new[]{new AbilityRequirement(AbilityKind.Writing,10,100),new AbilityRequirement(AbilityKind.Code,20,100),
                new AbilityRequirement(AbilityKind.Reasoning,30,100)};
            Assert.That(CorpusRewards.FromAllRequirements(requirements,0),Is.Empty);
            Assert.That(CorpusRewards.FromAllRequirements(requirements,requirement=>0),Is.Empty);
            var scores=CorpusRewards.FromAllRequirements(requirements,requirement=>requirement.Kind==AbilityKind.Code ? .1 : 0);
            Assert.That(scores.Count,Is.EqualTo(1));Assert.That(scores[AbilityKind.Code],Is.EqualTo(2));
        }
        [Test] public void AllRequirementsSkipZeroMinimumWithoutSelectingRatio()
        {
            var requirements=new[]{new AbilityRequirement(AbilityKind.Writing,0,100),new AbilityRequirement(AbilityKind.Code,20,100),
                new AbilityRequirement(AbilityKind.Reasoning,0,100),new AbilityRequirement(AbilityKind.Retrieval,40,100)};
            var selected=new List<AbilityRequirement>();
            var scores=CorpusRewards.FromAllRequirements(requirements,requirement=>{selected.Add(requirement);return .1;});
            CollectionAssert.AreEqual(new[]{requirements[1],requirements[3]},selected);
            Assert.That(scores.Count,Is.EqualTo(2));Assert.That(scores[AbilityKind.Code],Is.EqualTo(2));
            Assert.That(scores[AbilityKind.Retrieval],Is.EqualTo(4));
        }
        [Test] public void NullEmptyAndAllZeroRequirementsReturnNoScores()
        {
            var cases=new IEnumerable<AbilityRequirement>[]{null,Array.Empty<AbilityRequirement>(),
                new[]{new AbilityRequirement(AbilityKind.Writing,0,10),new AbilityRequirement(AbilityKind.Code,0,20)}};
            int selections=0;
            foreach(var requirements in cases)
            {
                Assert.That(CorpusRewards.FromAllRequirements(requirements,.1),Is.Empty);
                Assert.That(CorpusRewards.FromAllRequirements(requirements,requirement=>{selections++;return .1;}),Is.Empty);
            }
            Assert.That(selections,Is.Zero);
        }
        [TestCase(-.1)] [TestCase(double.NaN)] [TestCase(double.PositiveInfinity)] [TestCase(double.NegativeInfinity)]
        public void AllRequirementsRejectInvalidRatios(double ratio)
        {
            var requirements=new[]{new AbilityRequirement(AbilityKind.Writing,10,20)};
            Assert.Throws<ArgumentOutOfRangeException>(()=>CorpusRewards.FromAllRequirements(requirements,ratio));
            Assert.Throws<ArgumentOutOfRangeException>(()=>CorpusRewards.FromAllRequirements(requirements,requirement=>ratio));
            Assert.Throws<ArgumentOutOfRangeException>(()=>CorpusRewards.FromAllRequirements(null,ratio));
            Assert.Throws<ArgumentOutOfRangeException>(()=>CorpusRewards.FromAllRequirements(Array.Empty<AbilityRequirement>(),ratio));
        }
        [Test] public void AllRequirementsRejectNullRatioSelector()
        {
            var cases=new IEnumerable<AbilityRequirement>[]{null,Array.Empty<AbilityRequirement>(),new[]{new AbilityRequirement(AbilityKind.Writing,10,20)}};
            foreach(var requirements in cases)
                Assert.Throws<ArgumentNullException>(()=>CorpusRewards.FromAllRequirements(requirements,(Func<AbilityRequirement,double>)null));
        }
        [Test] public void PartialSpendingPreservesAbilityProportionsAndRemainingPoints()
        {
            var f=new Fixture();f.Award();
            Assert.That(f.Session.StartConversationTraining("train","base","小模型","user-A",2,out var job),Is.EqualTo(TrainingResult.Success));
            Assert.That(job.Output.Definition.Points[AbilityKind.Writing],Is.EqualTo(1.5));
            Assert.That(job.Output.Definition.Points[AbilityKind.Reasoning],Is.EqualTo(.5));
            Assert.That(f.Session.GetConversationCorpus("user-A").AvailablePoints,Is.EqualTo(2));
            Assert.That(f.Sim.Tasks[job.RequestId].Definition.Shape.Count,Is.EqualTo(3));
            Assert.That(f.Session.StartConversationTraining("train","base","第二次","user-A",2,out _),Is.EqualTo(TrainingResult.Success));
            Assert.That(f.Session.GetConversationCorpus("user-A").AvailablePoints,Is.Zero);
            Assert.That(f.Session.IsConsumed("corpus-1"),Is.True);
            int before=f.Money;
            Assert.That(f.Session.StartConversationTraining("train","base","重复","user-A",1,out _),Is.EqualTo(TrainingResult.InvalidInput));
            Assert.That(f.Money,Is.EqualTo(before));
        }
        [Test] public void PartialSpendingSurvivesJsonAndRejectsOverspending()
        {
            var f=new Fixture();f.Award();
            f.Session.StartConversationTraining("train","base","部分","user-A",1,out _);
            var state=JsonUtility.FromJson<TrainingSessionState>(JsonUtility.ToJson(f.Session.CaptureState()));
            TrainingSession.ValidateState(state,f.Session.Recipes,f.Session.Architectures,f.Session.ComputeTiers);
            f.Sim.DrainEvents().ToArray();
            var sim=TaskSimulation.RestoreState(f.Sim.CaptureState());
            var restored=TrainingSession.RestoreState(state,sim,f.Session.Recipes,f.Session.Architectures,f.Session.ComputeTiers,
                amount=>true,amount=>{},profile=>true,rate=>true,()=>Enumerable.Range(0,64).Select(i=>new Cell(i%8,i/8)));
            Assert.That(restored.GetConversationCorpus("user-A").AvailablePoints,Is.EqualTo(3));
            Assert.That(restored.PreviewCorpus("user-A",2)[AbilityKind.Reasoning],Is.EqualTo(.5));
            state.Jobs[0].Uses[0].Points=5;
            Assert.Throws<ArgumentException>(()=>TrainingSession.ValidateState(state,f.Session.Recipes,f.Session.Architectures,f.Session.ComputeTiers));
        }
        [Test] public void OldWholeRecordJobsStillValidateAndRemainConsumed()
        {
            var f=new Fixture();f.Award();f.Session.StartTraining("train","base","旧格式",new[]{"corpus-1"},out _);
            var state=f.Session.CaptureState();state.Jobs[0].Uses=null;
            Assert.DoesNotThrow(()=>TrainingSession.ValidateState(state,f.Session.Recipes,f.Session.Architectures,f.Session.ComputeTiers));
        }
        [Test] public void InvalidAmountAndInsufficientFundsDoNotSpendCorpus()
        {
            var f=new Fixture();f.Award();
            foreach(double points in new[]{0,-1,double.NaN,double.PositiveInfinity,11,5})
                Assert.That(f.Session.StartConversationTraining("train","base","无效","user-A",points,out _),Is.EqualTo(TrainingResult.InvalidInput));
            f.Money=0;
            Assert.That(f.Session.StartConversationTraining("train","base","没钱","user-A",2,out _),Is.EqualTo(TrainingResult.InsufficientFunds));
            Assert.That(f.Session.GetConversationCorpus("user-A").AvailablePoints,Is.EqualTo(4));
            Assert.That(f.Session.Jobs,Is.Empty);
        }
        [Test] public void GrowthPreservesBaseAndRejectsShapesExceedingBoard()
        {
            var f=new Fixture();var r=f.Session.Recipes[0];
            var cells=TrainingSession.GrowShape(r,5,8,8);
            Assert.That(cells.Length,Is.EqualTo(4));Assert.That(cells.Intersect(r.Shape).Count(),Is.EqualTo(2));
            Assert.Throws<ArgumentException>(()=>TrainingSession.GrowShape(r,10,2,2));
        }
    }
}
