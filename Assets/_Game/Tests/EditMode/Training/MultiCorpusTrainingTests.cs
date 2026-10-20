using System;
using System.Collections.Generic;
using System.Linq;
using CallmeCatgirl.Gameplay.TaskGrid;
using NUnit.Framework;
using UnityEngine;
namespace CallmeCatgirl.Gameplay.Training.Tests
{
    public sealed class MultiCorpusTrainingTests
    {
        private sealed class Fixture
        {
            public TaskSimulation Sim;
            public TrainingSession Session;
            public int Money=200;
            public Fixture(double maximum=100,PolicyModifiers modifiers=null)
            {
                var cells=Enumerable.Range(0,64).Select(i=>new Cell(i%8,i/8)).ToArray();
                Sim=new TaskSimulation(8,8,cells,new ModelDefinition("starter",1,new Dictionary<AbilityKind,double>{{AbilityKind.Writing,100},{AbilityKind.Code,100},{AbilityKind.Reasoning,100}}));
                Sim.Paused=true;Sim.TrySetTokenThroughput(10);
                Session=new TrainingSession(Sim,new CatalogModel("初始","base",Sim.Model,null),
                    new[]{new TrainingRecipe("train","训练",20,1,120,1,1,new[]{new Cell(0,0),new Cell(1,0),new Cell(2,0),new Cell(0,1),new Cell(1,1),new Cell(2,1)},2,maximum,5,1.5)},
                    new[]{new ArchitectureDefinition("base","基础",1,OutlineTransform.Keep)},new[]{new ComputeTier("one",10,0)},
                    amount=>{if(Money<amount)return false;Money-=amount;return true;},amount=>Money+=amount,
                    profile=>Sim.TryChangeExecutionProfile(profile.Model,profile.TokensPerSecond,profile.OpenCells,profile.Architecture),rate=>Sim.TrySetTokenThroughput(rate),()=>cells,null,()=>modifiers??PolicyModifiers.Neutral);
            }
            public void Award(string user,params (AbilityKind kind,double points)[] scores)
            {
                string id="request-"+Sim.Tasks.Count;
                var def=new TaskDefinition(id,"测试",new[]{new Cell(0,0)},10,1,100,0,new[]{new AbilityRequirement(AbilityKind.Writing,1,2)});
                Assert.That(Sim.TryCreate(id,user,id,def,out _),Is.True);
                Assert.That(Sim.TryPlace(id,new Cell(0,0),0,out _),Is.True);
                Sim.Paused=false;Sim.Advance(1);Sim.Paused=true;Assert.That(Sim.TrySubmit(id),Is.True);
                Assert.That(Session.RecordDialogue(id,"final","choice",scores.ToDictionary(p=>p.kind,p=>p.points),"test"),Is.EqualTo(TrainingResult.Success));
            }
            public void AwardBoth(){Award("A",(AbilityKind.Writing,12),(AbilityKind.Code,4));Award("B",(AbilityKind.Reasoning,10),(AbilityKind.Code,6));}
        }
        private static ConversationInvestment I(string id,double points)=>new ConversationInvestment{ConversationId=id,Points=points};
        [Test] public void BatchPreviewAndJobAgreeAndEachAbilityAddsSeparately()
        {
            var f=new Fixture();f.AwardBoth();var inputs=new[]{I("A",8),I("B",8)};
            var preview=f.Session.PreviewTraining("train",inputs);
            Assert.That(f.Session.GetConversationCorpus("A").AvailablePoints,Is.EqualTo(16));
            Assert.That(preview.TotalPoints,Is.EqualTo(16));Assert.That(preview.WorkTokens,Is.EqualTo(440).Within(1e-8));
            Assert.That(preview.EstimatedSeconds,Is.EqualTo(44).Within(1e-8));Assert.That(preview.Shape.Count,Is.EqualTo(14));
            Assert.That(preview.Abilities[AbilityKind.Writing],Is.EqualTo(6));Assert.That(preview.Abilities[AbilityKind.Code],Is.EqualTo(5));Assert.That(preview.Abilities[AbilityKind.Reasoning],Is.EqualTo(5));
            Assert.That(f.Session.StartConversationsTraining("train","base","合并",inputs,out var job),Is.EqualTo(TrainingResult.Success));
            Assert.That(job.WorkTokens,Is.EqualTo(preview.WorkTokens));Assert.That(f.Sim.Tasks[job.RequestId].Definition.Shape,Is.EquivalentTo(preview.Shape));
            Assert.That(job.Output.Definition.Points,Is.EquivalentTo(preview.Abilities));Assert.That(job.Uses.Sum(u=>u.Points),Is.EqualTo(16));
            Assert.That(f.Money,Is.EqualTo(180));Assert.That(f.Session.GetConversationCorpus("A").AvailablePoints,Is.EqualTo(8));Assert.That(f.Session.GetConversationCorpus("B").AvailablePoints,Is.EqualTo(8));
            Assert.That(f.Sim.TryPlace(job.RequestId,new Cell(0,0),0,out _),Is.True);
            f.Sim.Paused=false;f.Sim.Advance(43);Assert.That(f.Sim.Tasks[job.RequestId].State,Is.EqualTo(RequestState.Running));
            f.Sim.Advance(1.000001);Assert.That(f.Sim.Tasks[job.RequestId].State,Is.EqualTo(RequestState.Ready));
        }
        [Test] public void InvalidBatchRejectsAtomicallyIncludingAggregateCapAndDuplicateSources()
        {
            var f=new Fixture(20);f.AwardBoth();
            var invalid=new[]{new[]{I("A",12),I("B",12)},new[]{I("A",2),I("A",2)},new[]{I("A",2),I("B",17)},new[]{I("A",2),I("B",0)},new[]{I("A",double.NaN)},new[]{I("missing",1)},new ConversationInvestment[]{null},Array.Empty<ConversationInvestment>()};
            foreach(var inputs in invalid)Assert.That(f.Session.StartConversationsTraining("train","base","无效",inputs,out _),Is.EqualTo(TrainingResult.InvalidInput));
            Assert.That(f.Session.StartConversationsTraining("train","base","无效",null,out _),Is.EqualTo(TrainingResult.InvalidInput));
            f.Money=0;Assert.That(f.Session.StartConversationsTraining("train","base","没钱",new[]{I("A",2),I("B",2)},out _),Is.EqualTo(TrainingResult.InsufficientFunds));
            Assert.That(f.Session.Jobs,Is.Empty);Assert.That(f.Session.GetConversationCorpus("A").AvailablePoints,Is.EqualTo(16));Assert.That(f.Session.GetConversationCorpus("B").AvailablePoints,Is.EqualTo(16));
        }
        [Test] public void HundredPointTrainingExpandsPastOriginalShapeWidthAndRetainsFractionalPoints()
        {
            var f=new Fixture();f.Award("A",(AbilityKind.Writing,60.625));f.Award("B",(AbilityKind.Code,60.625));
            var preview=f.Session.PreviewTraining("train",new[]{I("A",50),I("B",50)});
            Assert.That(preview.Shape.Count,Is.EqualTo(56));Assert.That(preview.Shape.Max(c=>c.X),Is.GreaterThan(2));
            Assert.That(preview.WorkTokens,Is.EqualTo(5120));
            Assert.That(f.Session.StartConversationsTraining("train","base","大模型",new[]{I("A",50),I("B",50)},out _),Is.EqualTo(TrainingResult.Success));
            Assert.That(f.Session.GetConversationCorpus("A").AvailablePoints,Is.EqualTo(10.625));
        }
        [Test] public void SpaceGrowthDoesNotDependOnWorkGrowthCoefficientOrEntryApi()
        {
            var f=new Fixture();f.AwardBoth();
            // Mutually exclusive work/space controls must remain independent.
            var rules=new[]{new TrainingRecipe("fixed-work","Fixed work",20,1,120,1,1,new[]{new Cell(0,0)},2,100,0)};
            var session=new TrainingSession(f.Sim,new CatalogModel("Base","base",f.Sim.Model,null),rules,
                f.Session.Architectures,f.Session.ComputeTiers,cost=>true,cost=>{},profile=>true,rate=>true,
                ()=>Enumerable.Range(0,64).Select(i=>new Cell(i%8,i/8)));
            session.GrantScriptedCorpus("material","A",new Dictionary<AbilityKind,double>{{AbilityKind.Writing,8}});
            var preview=session.PreviewTraining("fixed-work",new[]{I("A",8)});
            Assert.That(preview.Shape.Count,Is.EqualTo(5));Assert.That(preview.WorkTokens,Is.EqualTo(120));
            Assert.That(session.StartTraining("fixed-work","base","Legacy entry",new[]{session.Corpus.Single().Id},out var job),Is.EqualTo(TrainingResult.Success));
            Assert.That(f.Sim.Tasks[job.RequestId].Definition.Shape,Is.EquivalentTo(preview.Shape),"Point-based preview and record-based submission must agree.");
        }
        [Test] public void TrainingPreviewChecksCompleteCompartmentNotJustOpenCellCount()
        {
            var cells=Enumerable.Range(0,16).Select(i=>new Cell(i%4,i/4)).ToArray();
            var sim=new TaskSimulation(4,4,cells,new ModelDefinition("empty",1,new Dictionary<AbilityKind,double>()),Enumerable.Range(0,16).ToArray());
            sim.Paused=true;
            var session=new TrainingSession(sim,new CatalogModel("Base","base",sim.Model,null),
                new[]{new TrainingRecipe("train","Train",20,1,120,1,1,new[]{new Cell(0,0)},2,100,5)},
                new[]{new ArchitectureDefinition("base","Base",1,OutlineTransform.Keep)},new[]{new ComputeTier("one",10,0)},
                cost=>true,cost=>{},profile=>true,rate=>true,()=>cells);
            session.GrantScriptedCorpus("material","A",new Dictionary<AbilityKind,double>{{AbilityKind.Writing,4}});
            var preview=session.PreviewTraining("train",new[]{I("A",4)});
            Assert.That(preview.Shape.Count,Is.LessThan(sim.OpenCellCount));
            var fit=preview.GetType().GetProperty("FitsCurrentMemory");
            Assert.That(fit,Is.Not.Null,"A valid total cell count cannot prove a training shape fits between partitions.");
            Assert.That((bool)fit.GetValue(preview),Is.False);
        }
        [Test] public void FitPreviewAllowsRotationAndPlanningAfterCurrentTaskIsWithdrawn()
        {
            var cells=new[]{new Cell(1,0),new Cell(1,1),new Cell(1,2)};
            var sim=new TaskSimulation(3,3,cells,new ModelDefinition("empty",1,new Dictionary<AbilityKind,double>()));sim.Paused=true;
            var session=new TrainingSession(sim,new CatalogModel("Base","base",sim.Model,null),
                new[]{new TrainingRecipe("train","Train",20,1,120,1,1,new[]{new Cell(0,0),new Cell(1,0),new Cell(2,0)},100,100)},
                new[]{new ArchitectureDefinition("base","Base",1,OutlineTransform.Keep)},new[]{new ComputeTier("one",10,0)},
                cost=>true,cost=>{},profile=>true,rate=>true,()=>cells);
            sim.TryCreate("busy","busy","busy",new TaskDefinition("busy","busy",new[]{new Cell(0,0)},10,1,100,0,Array.Empty<AbilityRequirement>()),out _);
            Assert.That(sim.TryPlace("busy",cells[0],0,out _),Is.True);
            session.GrantScriptedCorpus("material","A",new Dictionary<AbilityKind,double>{{AbilityKind.Writing,4}});
            var preview=session.PreviewTraining("train",new[]{I("A",4)});
            var fit=preview.GetType().GetProperty("FitsCurrentMemory");Assert.That(fit,Is.Not.Null);
            Assert.That((bool)fit.GetValue(preview),Is.True,"Rotation must fit; temporary occupancy is a rearrangement issue, not permanently insufficient memory.");
        }
        [Test] public void TrainingEtaTracksCurrentThroughputWorkAndPoliciesAndMatchesCompletion()
        {
            var cells=Enumerable.Range(0,64).Select(i=>new Cell(i%8,i/8)).ToArray();
            var sim=new TaskSimulation(8,8,cells,new ModelDefinition("empty",1,new Dictionary<AbilityKind,double>()));sim.Paused=true;sim.TrySetTokenThroughput(10);
            var session=new TrainingSession(sim,null,
                new[]{new TrainingRecipe("train","Train",20,1,100,1,1,new[]{new Cell(0,0)},100,100,5,2)},
                new[]{new ArchitectureDefinition("base","Executor",1,OutlineTransform.Keep),new ArchitectureDefinition("future-fast","Output",2,OutlineTransform.Keep)},
                new[]{new ComputeTier("one",10,0)},cost=>true,cost=>{},
                profile=>sim.TryChangeExecutionProfile(profile.Model,profile.TokensPerSecond,profile.OpenCells,profile.Architecture),rate=>sim.TrySetTokenThroughput(rate),()=>cells,
                policyModifiers:()=>new PolicyModifiers(trainingWorkMultiplier:2));
            session.GrantScriptedCorpus("material","A",new Dictionary<AbilityKind,double>{{AbilityKind.Writing,20}});
            var small=session.PreviewTraining("train",new[]{I("A",2)});var large=session.PreviewTraining("train",new[]{I("A",4)});
            Assert.That(small.WorkTokens,Is.EqualTo(240));Assert.That(small.EstimatedSeconds,Is.EqualTo(24));
            Assert.That(large.WorkTokens,Is.EqualTo(360));Assert.That(large.EstimatedSeconds,Is.EqualTo(36));
            Assert.That(sim.TrySetTokenThroughput(20),Is.True);var faster=session.PreviewTraining("train",new[]{I("A",4)});
            Assert.That(faster.EstimatedSeconds,Is.EqualTo(18));
            sim.Paused=false;Assert.That(session.PreviewTraining("train",new[]{I("A",4)}).EstimatedSeconds,Is.EqualTo(18));sim.Paused=true;
            Assert.That(session.StartConversationTraining("train","future-fast","Future model","A",4,out var job),Is.EqualTo(TrainingResult.Success));
            Assert.That(job.Output.ArchitectureId,Is.EqualTo("future-fast"));Assert.That(sim.TryPlace(job.RequestId,new Cell(0,0),0,out _),Is.True);
            sim.Paused=false;sim.Advance(faster.EstimatedSeconds-.01);Assert.That(sim.Tasks[job.RequestId].State,Is.EqualTo(RequestState.Running));
            sim.Advance(.011);Assert.That(sim.Tasks[job.RequestId].State,Is.EqualTo(RequestState.Ready));
        }
        [Test] public void PowerGrowthIsSuperlinearAndPoliciesApplyOnce()
        {
            var f=new Fixture(modifiers:new PolicyModifiers(trainingScoreMultiplier:1.5,trainingCostMultiplier:2,trainingWorkMultiplier:2));f.AwardBoth();
            var small=f.Session.PreviewTraining("train",new[]{I("A",4)});var big=f.Session.PreviewTraining("train",new[]{I("A",8)});
            Assert.That(big.WorkTokens-240,Is.GreaterThan((small.WorkTokens-240)*2));
            Assert.That(big.Cost,Is.EqualTo(40));Assert.That(big.Abilities.Values.Sum(),Is.EqualTo(12));
            Assert.That(f.Session.StartConversationsTraining("train","base","政策",new[]{I("A",8)},out var job),Is.EqualTo(TrainingResult.Success));
            Assert.That(job.WorkTokens,Is.EqualTo(big.WorkTokens));Assert.That(job.Output.Definition.Points,Is.EquivalentTo(big.Abilities));
            Assert.That(f.Money,Is.EqualTo(160));
        }
        [Test] public void BatchLedgerAndFrozenWorkSurviveNativeJsonAndCannotBeSpentTwice()
        {
            var f=new Fixture();f.AwardBoth();f.Session.StartConversationsTraining("train","base","保存",new[]{I("A",8),I("B",8)},out var job);
            var state=JsonUtility.FromJson<TrainingSessionState>(JsonUtility.ToJson(f.Session.CaptureState()));
            f.Sim.DrainEvents().ToArray();var sim=TaskSimulation.RestoreState(JsonUtility.FromJson<TaskSimulationState>(JsonUtility.ToJson(f.Sim.CaptureState())));
            var restored=TrainingSession.RestoreState(state,sim,f.Session.Recipes,f.Session.Architectures,f.Session.ComputeTiers,
                amount=>true,amount=>{},profile=>true,rate=>true,()=>Enumerable.Range(0,64).Select(i=>new Cell(i%8,i/8)));
            Assert.That(restored.Jobs.Single().WorkTokens,Is.EqualTo(job.WorkTokens));
            Assert.That(restored.GetConversationCorpus("A").AvailablePoints,Is.EqualTo(8));Assert.That(restored.GetConversationCorpus("B").AvailablePoints,Is.EqualTo(8));
            Assert.That(restored.StartConversationsTraining("train","base","超支",new[]{I("A",9),I("B",1)},out _),Is.EqualTo(TrainingResult.InvalidInput));
        }
        [Test] public void FractionalPowerWorkSurvivesJsonAgainstLiveTaskButRejectsDifferentWork()
        {
            var f=new Fixture();f.AwardBoth();
            Assert.That(f.Session.StartConversationsTraining("train","base","小数计算量",new[]{I("A",.8)},out var job),Is.EqualTo(TrainingResult.Success));
            var state=JsonUtility.FromJson<TrainingSessionState>(JsonUtility.ToJson(f.Session.CaptureState()));
            Assert.DoesNotThrow(()=>TrainingSession.RestoreState(state,f.Sim,f.Session.Recipes,f.Session.Architectures,f.Session.ComputeTiers,
                amount=>true,amount=>{},profile=>true,rate=>true,()=>Enumerable.Range(0,64).Select(i=>new Cell(i%8,i/8))));
            state.Jobs[0].WorkTokens+=.01;
            Assert.Throws<ArgumentException>(()=>TrainingSession.RestoreState(state,f.Sim,f.Session.Recipes,f.Session.Architectures,f.Session.ComputeTiers,
                amount=>true,amount=>{},profile=>true,rate=>true,()=>Enumerable.Range(0,64).Select(i=>new Cell(i%8,i/8))));
        }
        [Test] public void OversizedShapeAndUnrepresentableWorkAreRejectedBeforePayment()
        {
            var f=new Fixture(1000);f.Award("A",(AbilityKind.Writing,500));
            Assert.That(f.Session.StartConversationsTraining("train","base","太大",new[]{I("A",120)},out _),Is.EqualTo(TrainingResult.InvalidInput));
            Assert.That(f.Money,Is.EqualTo(200));Assert.That(f.Session.Jobs,Is.Empty);
            var r=new TrainingRecipe("x","x",0,1,120,1,1,new[]{new Cell(0,0)},2,100,double.MaxValue,2);
            Assert.Throws<ArgumentOutOfRangeException>(()=>r.WorkForPoints(100));
            Assert.Throws<ArgumentOutOfRangeException>(()=>new TrainingRecipe("x","x",0,1,120,1,1,new[]{new Cell(0,0)},2,100,5,.5));
        }
    }
}
