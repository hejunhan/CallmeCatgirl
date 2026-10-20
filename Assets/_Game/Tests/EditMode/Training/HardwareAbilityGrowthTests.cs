using System;
using System.Collections.Generic;
using System.Linq;
using CallmeCatgirl.Gameplay.TaskGrid;
using NUnit.Framework;
using UnityEngine;

namespace CallmeCatgirl.Gameplay.Training.Tests
{
    public sealed class HardwareAbilityGrowthTests
    {
        private sealed class Fixture
        {
            public TaskSimulation Sim;
            public TrainingSession Session;
            public int Money = 1000;
            public bool RejectRate;
            public readonly Cell[] Cells = { new Cell(0,0), new Cell(1,0) };
            public Fixture(string architecture = "baseline")
            {
                var model = new ModelDefinition(Guid.NewGuid().ToString("D"), 1,
                    new Dictionary<AbilityKind,double> { { AbilityKind.Writing, 2 } });
                Sim = new TaskSimulation(2,1,Cells,model) { Paused = true };
                Session = new TrainingSession(Sim, new CatalogModel("Initial",architecture,model,null),
                    new[] { new TrainingRecipe("train","Train",0,1,400,1,1,new[] {new Cell(0,0)}) },
                    new[] {
                        new ArchitectureDefinition("baseline","Baseline",1,OutlineTransform.Keep,1,true,"",1.6,1,true),
                        new ArchitectureDefinition("alternate","MoE",1.6,OutlineTransform.Keep,2,true,"",1,1.6,true),
                        new ArchitectureDefinition("architecture-slot-3","Balanced",1.25,OutlineTransform.Keep,4,true,"",1.25,1.25,true)
                    },
                    new[] {new ComputeTier("one",10,0),new ComputeTier("two",15,30),new ComputeTier("three",22.5,40)},
                    cost => { if(Money<cost)return false; Money-=cost; return true; }, cost => Money+=cost,
                    profile => Sim.TryChangeExecutionProfile(profile.Model,profile.TokensPerSecond,profile.OpenCells,profile.Architecture),
                    rate => !RejectRate && Sim.TrySetTokenThroughput(rate), () => Cells,currentStage:()=>5);
                Assert.That(Sim.TrySetTokenThroughput(Session.TokensPerSecond),Is.True);
            }
            public TaskInstance Request(string id)
            {
                var definition = new TaskDefinition(id,id,new[]{new Cell(0,0)},600,10,2000,600,
                    new[]{new AbilityRequirement(AbilityKind.Writing,120,121)},usesCalculationBudget:true);
                Assert.That(Sim.TryCreate(id,"customer-"+id,"round",definition,out var task),Is.True);
                Assert.That(Sim.TryPlace(id,new Cell(0,0),0,out _),Is.True);
                return task;
            }
            public void Run(double seconds) { Sim.Paused=false; Sim.Advance(seconds); Sim.Paused=true; }
        }

        [TestCase("baseline")] [TestCase("alternate")] [TestCase("architecture-slot-3")]
        public void ComputeTiersAccelerateWithoutReducingCompletedAbility(string architecture)
        {
            for(int upgrades=0;upgrades<=2;upgrades++)
            {
                var f = new Fixture(architecture);
                var definition = f.Sim.Model;
                for(int i=0;i<upgrades;i++) Assert.That(f.Session.UpgradeCompute(),Is.EqualTo(TrainingResult.Success));
                double hardware = Math.Pow(1.5,upgrades);
                Assert.That(f.Sim.HardwareAbilityMultiplier,Is.EqualTo(hardware));
                Assert.That(f.Sim.EffectiveAbilityRateMultiplier,Is.EqualTo(hardware*f.Sim.Architecture.AbilityRateMultiplier));
                var task = f.Request("request");
                double duration = task.Definition.Calculation/f.Session.TokensPerSecond;
                f.Run(duration);
                Assert.That(task.State,Is.EqualTo(RequestState.Ready));
                Assert.That(task.FinishedAt.Value,Is.EqualTo(duration).Within(1e-9));
                Assert.That(task.GetAbility(AbilityKind.Writing),Is.EqualTo(120));
                Assert.That(task.Result.Overall,Is.EqualTo(Quality.Complete));
                Assert.That(f.Sim.Model,Is.SameAs(definition),"Hardware must not mutate static model attributes.");
                Assert.That(f.Sim.Model.Points[AbilityKind.Writing],Is.EqualTo(2));
                Assert.That(f.Sim.Model.Interval,Is.EqualTo(1));
            }
        }

        [Test] public void MidTaskUpgradePreservesFractionAndRejectedUpgradePreservesRateAndMoney()
        {
            var f = new Fixture();
            var task = f.Request("running");
            f.Run(.4);
            Assert.That(f.Session.UpgradeCompute(),Is.EqualTo(TrainingResult.Success));
            f.Run(.4);
            Assert.That(task.Progress,Is.EqualTo(10).Within(1e-9));
            Assert.That(task.GetAbility(AbilityKind.Writing),Is.EqualTo(2),"0.4 old + 0.6 upgraded must form one tick.");
            int money = f.Money;
            f.RejectRate = true;
            Assert.That(f.Session.UpgradeCompute(),Is.EqualTo(TrainingResult.HostRejected));
            Assert.That(f.Money,Is.EqualTo(money));
            Assert.That(f.Session.ComputeIndex,Is.EqualTo(1));
            Assert.That(f.Sim.HardwareAbilityMultiplier,Is.EqualTo(1.5));
            Assert.That(f.Sim.TokenThroughput,Is.EqualTo(15));
            f.RejectRate = false;
            Assert.That(f.Session.UpgradeCompute(),Is.EqualTo(TrainingResult.Success));
            f.Run(590/22.5);
            Assert.That(task.State,Is.EqualTo(RequestState.Ready));
            Assert.That(task.GetAbility(AbilityKind.Writing),Is.EqualTo(120));
        }

        [Test] public void NativeJsonRestoreRebuildsHardwareRateAndTrainingDurationKeepsCurrentThroughput()
        {
            var f = new Fixture("alternate");
            Assert.That(f.Session.UpgradeCompute(),Is.EqualTo(TrainingResult.Success));
            Assert.That(f.Session.UpgradeCompute(),Is.EqualTo(TrainingResult.Success));
            var task = f.Request("running");
            f.Run(.3);
            Assert.That(f.Session.GrantScriptedCorpus("test-reward","doctor",
                new Dictionary<AbilityKind,double>{{AbilityKind.Writing,1}}),Is.EqualTo(TrainingResult.Success));
            Assert.That(f.Session.StartTraining("train","baseline","Output",new[]{"corpus-1"},out var job),Is.EqualTo(TrainingResult.Success));
            Assert.That(f.Sim.TryPlace(job.RequestId,new Cell(1,0),0,out _),Is.True);
            f.Sim.DrainEvents();
            var grid = JsonUtility.FromJson<TaskSimulationState>(JsonUtility.ToJson(f.Sim.CaptureState()));
            var state = JsonUtility.FromJson<TrainingSessionState>(JsonUtility.ToJson(f.Session.CaptureState()));
            var sim = TaskSimulation.RestoreState(grid);
            var session = TrainingSession.RestoreState(state,sim,f.Session.Recipes,f.Session.Architectures,f.Session.ComputeTiers,
                _=>true,_=>{},p=>sim.TryChangeExecutionProfile(p.Model,p.TokensPerSecond,p.OpenCells,p.Architecture),
                rate=>sim.TrySetTokenThroughput(rate),()=>f.Cells,currentStage:()=>5,restoredTokensPerSecond:sim.TokenThroughput);
            Assert.That(sim.HardwareAbilityMultiplier,Is.EqualTo(2.25));
            Assert.That(session.ComputeAbilityMultiplier,Is.EqualTo(2.25));
            Assert.That(sim.Tasks[task.Id].GetAbility(AbilityKind.Writing),Is.EqualTo(task.GetAbility(AbilityKind.Writing)));
            double duration = job.WorkTokens/session.TokensPerSecond;
            sim.Paused=false; sim.Advance(duration);
            Assert.That(sim.Tasks[job.RequestId].FinishedAt.Value,Is.EqualTo(.3+duration).Within(1e-9),
                "Hardware ability rate must not change the existing calculation budget or training duration.");
            sim.Advance(100);
            Assert.That(sim.Tasks[task.Id].GetAbility(AbilityKind.Writing),Is.EqualTo(120));
        }

        [Test] public void DefinitionSpeedModeWithoutTrainingBindingKeepsOriginalRate()
        {
            var model = new ModelDefinition("legacy",1,new Dictionary<AbilityKind,double>{{AbilityKind.Writing,2}});
            var sim = new TaskSimulation(1,1,new[]{new Cell(0,0)},model);
            var def = new TaskDefinition("legacy-task","Legacy",new[]{new Cell(0,0)},100,10,100,1,
                new[]{new AbilityRequirement(AbilityKind.Writing,20,21)});
            Assert.That(sim.TryCreate("legacy","customer","round",def,out var task),Is.True);
            Assert.That(sim.TryPlace(task.Id,new Cell(0,0),0,out _),Is.True);
            sim.Advance(10);
            Assert.That(sim.HardwareAbilityMultiplier,Is.EqualTo(1));
            Assert.That(task.GetAbility(AbilityKind.Writing),Is.EqualTo(20));
            Assert.That(task.FinishedAt.Value,Is.EqualTo(10));
        }
    }
}
