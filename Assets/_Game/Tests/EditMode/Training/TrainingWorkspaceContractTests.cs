using System;
using System.Collections.Generic;
using System.Linq;
using CallmeCatgirl.Gameplay.TaskGrid;
using NUnit.Framework;
using UnityEngine;

namespace CallmeCatgirl.Gameplay.Training.Tests
{
    public sealed class TrainingWorkspaceContractTests
    {
        private sealed class Fixture
        {
            public readonly Cell[] Open={new Cell(1,1)};
            public TaskSimulation Simulation;
            public TrainingSession Session;
            public int Money;
            public Fixture()
            {
                Simulation=new TaskSimulation(3,3,Open,new ModelDefinition("starter",1,new Dictionary<AbilityKind,double>{{AbilityKind.Writing,1},{AbilityKind.Reasoning,1}}));
                Simulation.Paused=true;Simulation.TrySetTokenThroughput(10);
                Session=new TrainingSession(Simulation,new CatalogModel("初始","baseline",Simulation.Model,null),
                    new[]{new TrainingRecipe("normal","普通训练",20,1,120,1,1,new[]{new Cell(0,0),new Cell(1,0),new Cell(0,1)},2,100,5)},
                    new[]{new ArchitectureDefinition("baseline","基础",1,OutlineTransform.Keep)},new[]{new ComputeTier("one",10,0)},
                    cost=>{if(Money<cost)return false;Money-=cost;return true;},cost=>Money+=cost,
                    profile=>Simulation.TryChangeExecutionProfile(profile.Model,profile.TokensPerSecond,profile.OpenCells,profile.Architecture),
                    rate=>Simulation.TrySetTokenThroughput(rate),()=>Open);
            }
            public TrainingSession Restore()
            {
                var state=JsonUtility.FromJson<TrainingSessionState>(JsonUtility.ToJson(Session.CaptureState()));
                Simulation.DrainEvents().ToArray();
                var sim=TaskSimulation.RestoreState(JsonUtility.FromJson<TaskSimulationState>(JsonUtility.ToJson(Simulation.CaptureState())));
                return TrainingSession.RestoreState(state,sim,Session.Recipes,Session.Architectures,Session.ComputeTiers,cost=>true,cost=>{},profile=>true,rate=>true,()=>Open);
            }
        }
        private static ConversationInvestment Investment(string id,double points)=>new ConversationInvestment{ConversationId=id,Points=points};

        [TestCase(AbilityKind.Writing)]
        [TestCase(AbilityKind.Reasoning)]
        public void OnePointStarterTrainsOnOneRealCellWithoutMoneyAndCannotBeStartedTwice(AbilityKind ability)
        {
            var f=new Fixture();
            Assert.That(f.Session.GrantScriptedCorpus("opening","doctor",new Dictionary<AbilityKind,double>{{ability,1}}),Is.EqualTo(TrainingResult.Success));
            var inputs=new[]{Investment("doctor",1)};
            var preview=f.Session.PreviewTraining(TrainingSession.StarterRecipeId,inputs);
            Assert.That(preview.Cost,Is.Zero);Assert.That(preview.Shape.Count,Is.EqualTo(1));
            Assert.That(f.Session.StartConversationsTraining(TrainingSession.StarterRecipeId,"baseline","第一个模型",inputs,out var job),Is.EqualTo(TrainingResult.Success));
            Assert.That(f.Session.StartConversationsTraining(TrainingSession.StarterRecipeId,"baseline","重复",inputs,out _),Is.Not.EqualTo(TrainingResult.Success));
            Assert.That(f.Session.Jobs.Count,Is.EqualTo(1));Assert.That(f.Money,Is.Zero);
            Assert.That(f.Session.RemainingPoints(f.Session.Corpus.Single().Id),Is.Zero);
            Assert.That(f.Simulation.TryPlace(job.RequestId,f.Open[0],0,out _),Is.True);
            f.Simulation.Paused=false;f.Simulation.Advance(1.01);f.Simulation.Paused=true;
            Assert.That(f.Session.CompleteTraining(job.RequestId),Is.EqualTo(TrainingResult.Success));
            Assert.That(job.Output.Definition.Points[ability],Is.EqualTo(1));
            Assert.That(f.Session.StarterTrainingAvailable,Is.False);
        }

        [Test] public void DraftCancelAndInventoryFilterDoNotReserveAndPartialTrainingReservesExactlyOnce()
        {
            var f=new Fixture();f.Money=100;
            f.Session.GrantScriptedCorpus("materials","doctor",new Dictionary<AbilityKind,double>{{AbilityKind.Writing,4}});
            var draft=new CorpusArrangement();Assert.That(draft.TryPlace("doctor",4,1),Is.True);
            Assert.That(f.Session.QueryConversationCorpora(AbilityKind.Code),Is.Empty);
            Assert.That(draft.Investments.Single().Points,Is.EqualTo(1));
            Assert.That(f.Session.GetConversationCorpus("doctor").AvailablePoints,Is.EqualTo(4));
            draft.Clear();Assert.That(f.Money,Is.EqualTo(100));Assert.That(f.Session.Jobs,Is.Empty);
            Assert.That(f.Session.StartConversationsTraining(TrainingSession.StarterRecipeId,"baseline","部分",new[]{Investment("doctor",1)},out _),Is.EqualTo(TrainingResult.Success));
            Assert.That(f.Session.GetConversationCorpus("doctor").AvailablePoints,Is.EqualTo(3));
            Assert.That(f.Session.StartConversationsTraining(TrainingSession.StarterRecipeId,"baseline","重复",new[]{Investment("doctor",1)},out _),Is.EqualTo(TrainingResult.InvalidInput));
            Assert.That(f.Session.GetConversationCorpus("doctor").AvailablePoints,Is.EqualTo(3));
            Assert.That(f.Session.BestAvailableCorpusPoints,Is.EqualTo(3));
        }

        [Test] public void ScriptedRewardRestoresWithoutTaskAndDoesNotRelaxRealDialogueValidation()
        {
            var f=new Fixture();
            Assert.That(f.Session.RecordDialogue("missing","node","choice",new Dictionary<AbilityKind,double>{{AbilityKind.Writing,1}},"story"),Is.EqualTo(TrainingResult.NotSubmitted));
            Assert.That(f.Session.GrantScriptedCorpus("opening","doctor",new Dictionary<AbilityKind,double>{{AbilityKind.Writing,1}}),Is.EqualTo(TrainingResult.Success));
            var restored=f.Restore();
            Assert.That(restored.Corpus.Single().SourceKind,Is.EqualTo(CorpusSourceKind.ScriptedReward));
            Assert.That(restored.GrantScriptedCorpus("opening","doctor",new Dictionary<AbilityKind,double>{{AbilityKind.Writing,999}}),Is.EqualTo(TrainingResult.AlreadyRecorded));
            Assert.That(restored.Corpus.Single().Scores[AbilityKind.Writing],Is.EqualTo(1));
            Assert.That(restored.Simulation.Tasks,Is.Empty);
        }

        [Test] public void NativeRestoreRetainsReservedStarterAndEventLedger()
        {
            var f=new Fixture();f.Session.GrantScriptedCorpus("opening","doctor",new Dictionary<AbilityKind,double>{{AbilityKind.Reasoning,1}});
            f.Session.StartConversationsTraining(TrainingSession.StarterRecipeId,"baseline","首训",new[]{Investment("doctor",1)},out var job);
            var restored=f.Restore();
            Assert.That(restored.Jobs.Single().RequestId,Is.EqualTo(job.RequestId));
            Assert.That(restored.GetConversationCorpus("doctor").AvailablePoints,Is.Zero);
            Assert.That(restored.StarterTrainingAvailable,Is.False);
            Assert.That(restored.GrantScriptedCorpus("opening","doctor",new Dictionary<AbilityKind,double>{{AbilityKind.Writing,1}}),Is.EqualTo(TrainingResult.AlreadyRecorded));
        }

        [Test] public void BookRewardFreezesBestAndHasUsefulZeroInventoryBaseline()
        {
            var f=new Fixture();
            Assert.That(f.Session.GrantBestCorpusReward("book-empty","library"),Is.EqualTo(TrainingResult.Success));
            Assert.That(f.Session.GetConversationCorpus("library").AvailablePoints,Is.EqualTo(15));
            f.Session.GrantScriptedCorpus("better","doctor",new Dictionary<AbilityKind,double>{{AbilityKind.Reasoning,100}});
            Assert.That(f.Session.GrantBestCorpusReward("book-empty","library"),Is.EqualTo(TrainingResult.AlreadyRecorded));
            Assert.That(f.Session.GetConversationCorpus("library").AvailablePoints,Is.EqualTo(15));
            Assert.That(f.Session.GrantBestCorpusReward("book-new","archive"),Is.EqualTo(TrainingResult.Success));
            Assert.That(f.Session.GetConversationCorpus("archive").AvailableScores[AbilityKind.Reasoning],Is.EqualTo(375));
        }

        [Test] public void ArrangementRejectsOccupiedDropsWithoutChangingDraftAndCanExpand()
        {
            var draft=new CorpusArrangement();Assert.That(draft.TryPlace("A",0,1),Is.True);Assert.That(draft.TryPlace("B",1,2),Is.True);
            Assert.That(draft.TryPlace("A",1,1),Is.False);Assert.That(draft.Find("A"),Is.Zero);
            Assert.That(draft.TryPlace("A",4,1),Is.True);Assert.That(draft.Find("A"),Is.EqualTo(4));
            draft.Configure(4,4);Assert.That(draft.Investments.Sum(i=>i.Points),Is.EqualTo(3));
        }
    }
}
