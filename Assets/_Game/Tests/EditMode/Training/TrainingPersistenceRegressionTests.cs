using System;
using System.Collections.Generic;
using System.Linq;
using CallmeCatgirl.Gameplay.TaskGrid;
using NUnit.Framework;
using UnityEngine;
namespace CallmeCatgirl.Gameplay.Training.Tests
{
    public sealed class TrainingPersistenceRegressionTests
    {
        private sealed class Fixture
        {
            internal readonly Cell[] Cells={new Cell(0,0),new Cell(1,0),new Cell(0,1),new Cell(1,1)};
            internal readonly TaskSimulation Sim;
            internal readonly TrainingSession Session;
            internal Fixture()
            {
                var model=new ModelDefinition(Guid.NewGuid().ToString("D"),1,new Dictionary<AbilityKind,double>{{AbilityKind.Writing,2}});
                Sim=new TaskSimulation(2,2,Cells,model){Paused=true};Sim.TrySetTokenThroughput(10);
                Session=new TrainingSession(Sim,new CatalogModel("Base","base",model,null),
                    new[]{new TrainingRecipe("normal","Normal",0,1,10,1,1,new[]{new Cell(0,0)},100,100)},
                    new[]{new ArchitectureDefinition("base","Base",1,OutlineTransform.Keep)},new[]{new ComputeTier("one",10,0)},
                    cost=>true,cost=>{},profile=>Sim.TryChangeExecutionProfile(profile.Model,profile.TokensPerSecond,profile.OpenCells,profile.Architecture),
                    rate=>Sim.TrySetTokenThroughput(rate),()=>Cells);
            }
            internal TaskInstance ReadyOrdinary()
            {
                var def=new TaskDefinition("ordinary","Ordinary",new[]{new Cell(0,0)},10,1,100,10,new[]{new AbilityRequirement(AbilityKind.Writing,1,2)});
                Assert.That(Sim.TryCreate("ordinary","ordinary","one",def,out var task),Is.True);
                Assert.That(Sim.TryPlace(task.Id,Cells[0],0,out _),Is.True);Run();ToCandidate(task);return task;
            }
            internal void Run(){Sim.Paused=false;Sim.Advance(1);Sim.Paused=true;}
            internal void ToCandidate(TaskInstance task)
            {
                Assert.That(task.State,Is.EqualTo(RequestState.Ready));
                Assert.That(Sim.TryChangeExecutionProfile(Sim.Model,10,Cells,Sim.Architecture),Is.True);
                Assert.That(task.ReadyInCandidate,Is.True);
            }
            internal TaskSimulation Clone()
            {Sim.DrainEvents();return TaskSimulation.RestoreState(JsonUtility.FromJson<TaskSimulationState>(JsonUtility.ToJson(Sim.CaptureState())));}
            internal TrainingSession Restore(TaskSimulation sim)=>TrainingSession.RestoreState(
                JsonUtility.FromJson<TrainingSessionState>(JsonUtility.ToJson(Session.CaptureState())),sim,Session.Recipes,Session.Architectures,Session.ComputeTiers,
                cost=>true,cost=>{},profile=>true,rate=>true,()=>Cells,restoredTokensPerSecond:sim.TokenThroughput);
        }
        [TestCase(false)] [TestCase(true)]
        public void ReadyCandidateSubmissionAndFeedbackCloseClearTransientFlagBeforeSave(bool close)
        {
            var f=new Fixture();var task=f.ReadyOrdinary();Assert.That(f.Sim.TrySubmit(task.Id),Is.True);
            if(close)Assert.That(f.Sim.TryCloseFeedback(task.Id),Is.True);
            Assert.That(task.ReadyInCandidate,Is.False,"The ready-only flag must not survive a terminal transition.");
            var restored=f.Clone();Assert.That(restored.Tasks[task.Id].State,Is.EqualTo(close?RequestState.Closed:RequestState.Submitted));
            Assert.DoesNotThrow(()=>f.Restore(restored));
        }
        [Test] public void TrainingCompletedFromCandidateCanBeSavedAndRestored()
        {
            var f=new Fixture();f.Session.GrantScriptedCorpus("reward","doctor",new Dictionary<AbilityKind,double>{{AbilityKind.Writing,4}});
            Assert.That(f.Session.StartConversationTraining("normal","base","Trained","doctor",4,out var job),Is.EqualTo(TrainingResult.Success));
            Assert.That(f.Sim.TryPlace(job.RequestId,f.Cells[0],0,out _),Is.True);f.Run();var task=f.Sim.Tasks[job.RequestId];f.ToCandidate(task);
            Assert.That(f.Session.CompleteTraining(job.RequestId),Is.EqualTo(TrainingResult.Success));
            Assert.That(task.State,Is.EqualTo(RequestState.Closed));Assert.That(task.ReadyInCandidate,Is.False);
            var restored=f.Restore(f.Clone());Assert.That(restored.Jobs.Single().Completed,Is.True);
            Assert.That(restored.CompleteTraining(job.RequestId),Is.EqualTo(TrainingResult.AlreadyCompleted));
        }
        [Test] public void CaptureCanonicalizesOldTerminalFlagsButRestoreStillRejectsMalformedInput()
        {
            var f=new Fixture();var task=f.ReadyOrdinary();f.Sim.TrySubmit(task.Id);f.Sim.TryCloseFeedback(task.Id);
            // Simulate the already-running state produced by the pre-fix terminal transition.
            typeof(TaskInstance).GetProperty("ReadyInCandidate").GetSetMethod(true).Invoke(task,new object[]{true});
            f.Sim.DrainEvents();var state=f.Sim.CaptureState();Assert.That(state.Requests.Single().ReadyInCandidate,Is.False);
            Assert.DoesNotThrow(()=>TaskSimulation.RestoreState(state));
            state.Requests.Single().ReadyInCandidate=true;
            Assert.Throws<ArgumentException>(()=>TaskSimulation.RestoreState(state));
        }
        [Test] public void EffectiveModelRestoreTreatsMissingAbilityAsZeroButRejectsRealPointDifference()
        {
            var f=new Fixture();
            Assert.That(f.Sim.TryChangeModel(new ModelDefinition(f.Sim.Model.Id,1,new Dictionary<AbilityKind,double>{{AbilityKind.Writing,2},{AbilityKind.Code,0},{AbilityKind.Reasoning,0},{AbilityKind.Retrieval,0}})),Is.True);
            Assert.DoesNotThrow(()=>f.Restore(f.Clone()),"Extra explicit zero keys are semantically unchanged.");
            Assert.That(f.Sim.TryChangeModel(new ModelDefinition(f.Sim.Model.Id,1,new Dictionary<AbilityKind,double>{{AbilityKind.Writing,3}})),Is.True);
            var changed=f.Clone();Assert.Throws<ArgumentException>(()=>f.Restore(changed),"A real ability difference must still be rejected.");
        }
    }
}
