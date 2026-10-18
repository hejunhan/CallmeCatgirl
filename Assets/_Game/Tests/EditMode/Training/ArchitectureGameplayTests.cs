using System;
using System.Linq;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using CallmeCatgirl.Gameplay.TaskGrid;
namespace CallmeCatgirl.Gameplay.Training.Tests
{
    public sealed class ArchitectureGameplayTests
    {
        private sealed class Fixture
        {
            public readonly Cell[] Cells=Enumerable.Range(0,64).Select(i=>new Cell(i%8,i/8)).ToArray();
            public readonly TaskSimulation Sim;
            public readonly TrainingSession Session;
            public int Stage=5,Money=1000;
            public Fixture(string initial="baseline")
            {
                var model=new ModelDefinition(Guid.NewGuid().ToString("D"),1,new Dictionary<AbilityKind,double>{{AbilityKind.Writing,1}});
                Sim=new TaskSimulation(8,8,Cells,model){Paused=true};
                Session=new TrainingSession(Sim,new CatalogModel("初始",initial,model,null),
                    new[]{new TrainingRecipe("train","训练",10,1,400,1,1,new[]{new Cell(0,0)})},
                    new[]{new ArchitectureDefinition("baseline","学习型",1,OutlineTransform.Keep,1,true,"",1.6,1,true),
                        new ArchitectureDefinition("alternate","高速型",1.6,OutlineTransform.Keep,2,true,"",1,1.6,true),
                        new ArchitectureDefinition("architecture-slot-3","均衡型",1.25,OutlineTransform.Keep,4,true,"",1.25,1.25,true)},
                    new[]{new ComputeTier("compute",10,0)},
                    cost=>{if(Money<cost)return false;Money-=cost;return true;},cost=>Money+=cost,
                    p=>Sim.TryChangeExecutionProfile(p.Model,p.TokensPerSecond,p.OpenCells,p.Architecture),rate=>Sim.TrySetTokenThroughput(rate),()=>Cells,currentStage:()=>Stage);
                Sim.TrySetTokenThroughput(Session.TokensPerSecond);
            }
            public TaskInstance Request(string id="source",int x=0)
            {
                var definition=new TaskDefinition("template","请求",new[]{new Cell(0,0)},400,10,2000,400,
                    new[]{new AbilityRequirement(AbilityKind.Writing,10,50)});
                Assert.That(Sim.TryCreate(id,"user-"+id,"1",definition,out var task),Is.True);
                Assert.That(Sim.TryPlace(id,new Cell(x,0),0,out _),Is.True);return task;
            }
            public void Run(double seconds){Sim.Paused=false;Sim.Advance(seconds);Sim.Paused=true;}
            public TrainingResult Award(string id="source")
            {
                if(Sim.Tasks[id].State==RequestState.Ready)Assert.That(Sim.TrySubmit(id),Is.True);
                return Session.RecordDialogue(id,"end","choice",new Dictionary<AbilityKind,double>{{AbilityKind.Writing,5}},"test");
            }
        }
        [TestCase("baseline",40d,8d)]
        [TestCase("alternate",25d,5d)]
        [TestCase("architecture-slot-3",32d,6.25d)]
        public void DifferentArchitecturesChangeDurationAndCorpusWithoutReducingAbilities(string id,double duration,double corpus)
        {
            var f=new Fixture(id);var task=f.Request();f.Run(duration);
            Assert.That(task.State,Is.EqualTo(RequestState.Ready));Assert.That(task.FinishedAt,Is.EqualTo(duration).Within(1e-8));
            Assert.That(task.GetAbility(AbilityKind.Writing),Is.EqualTo(40));Assert.That(task.Result.Overall,Is.EqualTo(Quality.Complete));
            Assert.That(f.Award(),Is.EqualTo(TrainingResult.Success));
            Assert.That(f.Session.GetConversationCorpus("user-source").AvailablePoints,Is.EqualTo(corpus));
            Assert.That(f.Award(),Is.EqualTo(TrainingResult.AlreadyRecorded));
        }
        [Test] public void ArchitectureUnlocksFollowMemoryOneTwoFour()
        {
            var f=new Fixture();f.Stage=1;
            Assert.That(f.Session.ArchitectureAvailability("baseline"),Is.EqualTo(TrainingResult.Success));
            Assert.That(f.Session.ArchitectureAvailability("alternate"),Is.EqualTo(TrainingResult.ArchitectureLocked));
            f.Stage=2;Assert.That(f.Session.ArchitectureAvailability("alternate"),Is.EqualTo(TrainingResult.Success));
            f.Stage=3;Assert.That(f.Session.ArchitectureAvailability("architecture-slot-3"),Is.EqualTo(TrainingResult.ArchitectureLocked));
            f.Stage=4;Assert.That(f.Session.ArchitectureAvailability("architecture-slot-3"),Is.EqualTo(TrainingResult.Success));
        }
        [Test] public void SwitchCannotReplaceStartedTaskBonusesButNewTasksUseNewArchitecture()
        {
            var f=new Fixture("alternate");var old=f.Request();f.Run(10);
            var learning=f.Session.Architectures.Single(a=>a.Id=="baseline");
            Assert.That(f.Sim.TryChangeExecutionProfile(f.Sim.Model,10,f.Cells,learning.Runtime),Is.True);
            Assert.That(old.State,Is.EqualTo(RequestState.Withdrawn));Assert.That(old.Architecture.Id,Is.EqualTo("alternate"));
            f.Sim.TryPlace(old.Id,new Cell(0,0),0,out _);var next=f.Request("new",1);f.Run(15);
            Assert.That(old.State,Is.EqualTo(RequestState.Ready));Assert.That(old.GetAbility(AbilityKind.Writing),Is.EqualTo(40));
            Assert.That(next.Progress,Is.EqualTo(150));Assert.That(next.Architecture.Id,Is.EqualTo("baseline"));
            f.Award();Assert.That(f.Session.Corpus.Single().Scores[AbilityKind.Writing],Is.EqualTo(5),"Switching before reply cannot earn learning-type corpus.");
        }
        [Test] public void FrozenTaskAndCurrentArchitectureBothSurviveSimulationJsonRestore()
        {
            var f=new Fixture("alternate");var task=f.Request();f.Run(10);
            f.Sim.TryChangeExecutionProfile(f.Sim.Model,10,f.Cells,f.Session.Architectures.First().Runtime);
            f.Sim.TryPlace(task.Id,new Cell(0,0),0,out _);f.Run(5);
            Assert.That(f.Sim.TryCreate("queued","idle","1",task.Definition,out _),Is.True);f.Sim.DrainEvents();
            var state=JsonUtility.FromJson<TaskSimulationState>(JsonUtility.ToJson(f.Sim.CaptureState()));
            Assert.That(state.ExecutionProfileVersion,Is.EqualTo(2));var restored=TaskSimulation.RestoreState(state);
            Assert.That(restored.Tasks["queued"].Architecture,Is.Null);
            Assert.That(restored.Architecture.Id,Is.EqualTo("baseline"));Assert.That(restored.Tasks[task.Id].Architecture.Id,Is.EqualTo("alternate"));
            restored.Paused=false;restored.Advance(10);
            Assert.That(restored.Tasks[task.Id].State,Is.EqualTo(RequestState.Ready));
            Assert.That(restored.Tasks[task.Id].GetAbility(AbilityKind.Writing),Is.EqualTo(40));
        }
        [Test] public void TrainingBlocksAreAcceleratedAndNativeRestoreDoesNotMultiplyCorpusAgain()
        {
            var f=new Fixture("alternate");f.Request();f.Run(25);f.Award();
            Assert.That(f.Session.StartTraining("train","alternate","高速新模型",new[]{"corpus-1"},out var job),Is.EqualTo(TrainingResult.Success));
            f.Sim.TryPlace(job.RequestId,new Cell(0,0),0,out _);f.Run(5);f.Sim.DrainEvents();
            var sim=TaskSimulation.RestoreState(f.Sim.CaptureState());
            var native=JsonUtility.FromJson<TrainingSessionState>(JsonUtility.ToJson(f.Session.CaptureState()));
            var restored=TrainingSession.RestoreState(native,sim,f.Session.Recipes,f.Session.Architectures,f.Session.ComputeTiers,
                cost=>true,cost=>{},p=>sim.TryChangeExecutionProfile(p.Model,p.TokensPerSecond,p.OpenCells,p.Architecture),rate=>sim.TrySetTokenThroughput(rate),()=>f.Cells,currentStage:()=>5);
            Assert.That(restored.Corpus.Single().Scores[AbilityKind.Writing],Is.EqualTo(5));
            sim.Paused=false;sim.Advance(20);sim.Paused=true;
            Assert.That(sim.Tasks[job.RequestId].FinishedAt,Is.EqualTo(50).Within(1e-8));
            Assert.That(restored.CompleteTraining(job.RequestId),Is.EqualTo(TrainingResult.Success));
            Assert.That(restored.RecordDialogue("source","end","choice",new Dictionary<AbilityKind,double>{{AbilityKind.Writing,5}},"test"),Is.EqualTo(TrainingResult.AlreadyRecorded));
        }
        [TestCase(0d)] [TestCase(-1d)] [TestCase(double.NaN)] [TestCase(double.PositiveInfinity)]
        public void InvalidBonusValuesAreRejected(double value)
            =>Assert.Throws<ArgumentOutOfRangeException>(()=>new ArchitectureRuntime("test",1,1,value));
    }
}
