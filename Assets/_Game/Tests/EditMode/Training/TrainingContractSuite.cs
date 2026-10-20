using System;
using System.Collections.Generic;
using System.Linq;
using CallmeCatgirl.Gameplay.TaskGrid;

namespace CallmeCatgirl.Gameplay.Training.Tests
{
    // Runs both as offline .NET contract checks and, after reviewed hooks land, as Unity EditMode tests.
    public static class TrainingContractSuite
    {
        public static Func<TrainingSessionState,TrainingSessionState> SerializationRoundTrip;
        public static IReadOnlyDictionary<string, Action> Cases() => new Dictionary<string, Action> {
            { "explicit_scores_and_fragment_dedup", ExplicitScores },
            { "reject_unfinished_and_low_quality", RejectIneligible },
            { "frozen_input_fee_and_no_inheritance", FrozenInput },
            { "insufficient_funds_atomic", InsufficientFunds },
            { "duplicate_selection_and_consumed_input", DuplicateInput },
            { "training_shares_occupancy_and_clock", OccupancyAndClock },
            { "training_move_withdraw_preserves_progress", TrainingWithdraw },
            { "training_completion_once_no_request_reward", CompleteOnce },
            { "paused_switch_and_host_rejection", PauseAndRejection },
            { "switch_returns_running_and_ready_without_settlement", SwapAll },
            { "ready_candidate_keeps_frozen_quality", ReadyFrozen },
            { "terminal_requests_stay_terminal_and_settle_once", TerminalOnce },
            { "compute_upgrade_changes_speed_not_attributes", ComputeOnly },
            { "architectures_change_speed_and_outline_not_scores", Architectures },
            { "parallel_requests_never_share_rate", IndependentRate },
            { "training_policy_does_not_accelerate_inference", TrainingPolicy },
            { "inference_policy_does_not_change_training_scores", InferencePolicy },
            { "invalid_profile_is_atomic", InvalidProfile },
            { "source_zero_code_can_train_code", ZeroCode },
            { "throughput_uses_token_calculation_ratio", TokenRatio },
            { "failed_host_compute_refunds_wallet", RefundOnReject },
            { "same_model_reselection_does_not_farm_rewards", Reselect },
            { "transformed_layout_uses_canonical_capacity", CanonicalLayout },
            { "model_definitions_are_immutable", ImmutableDefinitions },
            { "five_stage_architecture_unlock_schedule", FiveStageSchedule },
            { "unconfigured_architecture_stays_unavailable", PendingThird },
            { "locked_architecture_blocks_training_and_model_selection", LockedArchitecture },
            { "rename_starter_while_running_preserves_gameplay", RenameStarter },
            { "rename_active_trained_instance_preserves_stable_references", RenameTrained },
            { "rename_rejects_invalid_names_and_unknown_ids", RenameValidation },
            { "duplicate_display_names_allowed_with_distinct_ids", DuplicateNames },
            { "model_instances_use_full_uuids", UuidInstances },
            { "uuid_model_snapshot_reloads_without_regeneration", UuidReload },
            { "legacy_identity_migration_is_persisted_and_repeatable", LegacyMigration },
            { "invalid_identity_restore_is_atomic", InvalidIdentityRestore },
            { "short_id_collisions_do_not_merge_model_instances", ShortIdCollision },
            { "starter_earns_first_corpus_from_easy_request_without_training", StarterFirstCorpus },
            { "architecture_metadata_changes_do_not_rekey_model_uuid", ArchitectureIdentity },
            { "session_state_roundtrip_preserves_inventory_jobs_and_ledgers", SessionRoundtrip },
            { "session_repeated_restore_has_no_gameplay_effects", SessionRepeatRestore },
            { "session_restore_rejects_invalid_consumption_and_task_links", SessionInvalidState },
            { "session_restore_ready_candidate_finishes_once", SessionReadyCandidate },
            { "session_restore_preserves_active_renamed_uuid", SessionRenamedModel },
            { "session_restore_composes_permanent_bonus_once", SessionBonusOnce },
            { "session_capture_rejects_busy_transaction", SessionBusyCapture },
            { "session_native_dto_json_roundtrip", SessionJsonRoundtrip }
        };

        private sealed class Fixture
        {
            public TaskSimulation Sim;
            public ActualWallet Wallet;
            public TrainingSession Session;
            public PolicyModifiers Policy = PolicyModifiers.Neutral;
            public int Stage = 1;
            public bool RejectProfile, RejectRate;
            public int OrdinaryCompleted;
            public string InitialId => Session.Identities.ResolveKnown("initial");
            public readonly Cell[] Capacity = { new Cell(0,0), new Cell(1,0), new Cell(2,0), new Cell(3,0),
                new Cell(0,1), new Cell(1,1), new Cell(2,1), new Cell(3,1) };
            public Fixture(int money = 200, bool thirdReady = false)
            {
                Sim = new TaskSimulation(4, 4, Capacity,
                    new ModelDefinition("initial", 1, new Dictionary<AbilityKind,double> { { AbilityKind.Writing, 2 } }));
                Wallet = new ActualWallet(money);
                Sim.Paused = true; Sim.TrySetTokenThroughput(10);
                Session = new TrainingSession(Sim, new CatalogModel("初始", "base", Sim.Model, null),
                    new[] { new TrainingRecipe("train", "训练", 20, 1, 40, 1, 1,
                        new[] { new Cell(0,0), new Cell(1,0), new Cell(0,1), new Cell(1,1) }) },
                    new[] { new ArchitectureDefinition("base", "基准", 1, OutlineTransform.Keep),
                        new ArchitectureDefinition("alt", "转置", .5, OutlineTransform.Transpose, 2),
                        new ArchitectureDefinition("third", "协调状态架构", 1, OutlineTransform.Keep, 4, thirdReady, "注意力/状态空间混合") },
                    new[] { new ComputeTier("one", 10, 0), new ComputeTier("two", 20, 30) },
                    Wallet.TrySpendFunds, Wallet.AddFunds,
                    p => !RejectProfile && Sim.TryChangeExecutionProfile(p.Model, p.TokensPerSecond, p.OpenCells, p.Architecture),
                    rate => !RejectRate && Sim.TrySetTokenThroughput(rate), () => Capacity, null, () => Policy, () => Stage);
            }
            public TaskInstance Request(string id, double calc = 10, double tokens = 10, bool impossible = false)
            {
                var definition = new TaskDefinition("template", id, new[] { new Cell(0,0) }, calc, 1, 100, tokens,
                    new[] { new AbilityRequirement(AbilityKind.Writing, impossible ? 100 : 1, impossible ? 200 : 2) });
                True(Sim.TryCreate(id, id, "round", definition, out var task)); return task;
            }
            public void Run(double seconds) { Sim.Paused = false; Sim.Advance(seconds); Sim.Paused = true; }
            public void Place(TaskInstance task, int x = 0, int y = 0) => True(Sim.TryPlace(task.Id, new Cell(x,y), 0, out _));
            public string Record(string id = "request", AbilityKind ability = AbilityKind.Code, double score = 3)
            {
                var task = Request(id); Place(task); Run(1); True(Sim.TrySubmit(id));
                Equal(TrainingResult.Success, Session.RecordDialogue(id, "node", "reply",
                    new Dictionary<AbilityKind,double> { { ability, score } }, "story-explicit"));
                Drain(); return Session.Corpus.Last().Id;
            }
            public TrainingJob Start(string corpusId, string architecture = "base")
            {
                Equal(TrainingResult.Success, Session.StartTraining("train", architecture, "新模型", new[] { corpusId }, out var job));
                return job;
            }
            public CatalogModel Train(string id = "request", string architecture = "base")
            {
                var job = Start(Record(id), architecture); Place(Sim.Tasks[job.RequestId]); Run(4);
                Equal(TrainingResult.Success, Session.CompleteTraining(job.RequestId)); Drain(); return job.Output;
            }
            public void Drain()
            {
                // Test host is the ONLY event consumer, same routing contract as production owner.
                foreach (var item in Sim.DrainEvents())
                {
                    if (Session.IsTrainingRequest(item.RequestId)) continue;
                    if (item.Kind == TaskEventKind.Submitted && item.Result.Overall >= Quality.Complete) OrdinaryCompleted++;
                    Wallet.Settle(item);
                }
            }
        }
        // EconomyManager lives in Assembly-CSharp. Reflect only in tests to avoid an invalid asmdef -> Assembly-CSharp dependency.
        private sealed class ActualWallet
        {
            private readonly object instance;
            private readonly Type type;
            public ActualWallet(int funds)
            {
                type = Type.GetType("CallmeCatgirl.Gameplay.EconomyManager, Assembly-CSharp")
                    ?? typeof(TrainingContractSuite).Assembly.GetType("CallmeCatgirl.Gameplay.EconomyManager");
                if (type == null) throw new Exception("Actual EconomyManager is unavailable.");
                instance = Activator.CreateInstance(type, funds, 1);
            }
            public int CurrentFunds => (int)type.GetProperty("CurrentFunds").GetValue(instance);
            public bool TrySpendFunds(int amount) => (bool)type.GetMethod("TrySpendFunds").Invoke(instance, new object[] { amount });
            public void AddFunds(int amount) => type.GetMethod("AddFunds").Invoke(instance, new object[] { amount });
            public void Settle(TaskEvent item) => type.GetMethod("Settle").Invoke(instance, new object[] { item });
        }
        private static void ExplicitScores()
        {
            var f = new Fixture(); f.Record(); Equal(3d, f.Session.Corpus[0].Scores[AbilityKind.Code]);
            Equal(TrainingResult.AlreadyRecorded, f.Session.RecordDialogue("request", "node", "different-reply",
                new Dictionary<AbilityKind,double> {{ AbilityKind.Code, 99 }}, "explicit"));
            Equal(TrainingResult.Success, f.Session.RecordDialogue("request", "next-node", "reply",
                new Dictionary<AbilityKind,double> {{ AbilityKind.Writing, 2 }}, "explicit"));
            Equal(2, f.Session.Corpus.Count);
        }
        private static void RejectIneligible()
        {
            var f = new Fixture(); var t = f.Request("bad", impossible:true);
            Equal(TrainingResult.NotSubmitted, f.Session.RecordDialogue(t.Id,"n","c",new Dictionary<AbilityKind,double>{{AbilityKind.Code,1}},"explicit"));
            f.Place(t); f.Run(1); f.Sim.TrySubmit(t.Id);
            Equal(TrainingResult.LowQuality, f.Session.RecordDialogue(t.Id,"n","c",new Dictionary<AbilityKind,double>{{AbilityKind.Code,1}},"explicit"));
        }
        private static void FrozenInput()
        {
            var f = new Fixture(); var id = f.Record(); int before = f.Wallet.CurrentFunds;
            var job = f.Start(id); Equal(before-20, f.Wallet.CurrentFunds);
            Equal(3d, job.Output.Definition.Points[AbilityKind.Code]); True(!job.Output.Definition.Points.ContainsKey(AbilityKind.Writing));
            f.Record("later", AbilityKind.Code, 200); Equal(3d, job.Output.Definition.Points[AbilityKind.Code]);
        }
        private static void InsufficientFunds()
        {
            var f = new Fixture(0); var id = f.Record(); int tasks = f.Sim.Tasks.Count;
            Equal(TrainingResult.InsufficientFunds, f.Session.StartTraining("train","base","m",new[]{id},out _));
            Equal(tasks, f.Sim.Tasks.Count); True(!f.Session.IsConsumed(id));
        }
        private static void DuplicateInput()
        {
            var f = new Fixture(); var id = f.Record();
            Equal(TrainingResult.InvalidInput,f.Session.StartTraining("train","base","m",new[]{id,id},out _));
            f.Start(id); Equal(TrainingResult.CorpusConsumed,f.Session.StartTraining("train","base","m",new[]{id},out _));
        }
        private static void OccupancyAndClock()
        {
            var f = new Fixture(); var job = f.Start(f.Record()); var train = f.Sim.Tasks[job.RequestId];
            f.Run(3); Equal(0d,train.Progress); f.Place(train);
            var request = f.Request("overlap"); Equal(PlacementFailure.Overlap,f.Sim.Preview(request.Id,new Cell(0,0),0));
            f.Sim.Advance(5); Equal(0d,train.Progress); f.Run(1); Equal(10d,train.Progress);
        }
        private static void TrainingWithdraw()
        {
            var f = new Fixture(); var job = f.Start(f.Record()); var t=f.Sim.Tasks[job.RequestId]; f.Place(t);f.Run(1);
            True(f.Sim.TryPlace(t.Id,new Cell(1,0),0,out _)); Equal(10d,t.Progress); True(f.Sim.TryWithdraw(t.Id));
            f.Run(1);Equal(10d,t.Progress);f.Place(t);f.Run(3);Equal(RequestState.Ready,t.State);
        }
        private static void CompleteOnce()
        {
            var f = new Fixture(); var model=f.Train(); Equal(1,f.OrdinaryCompleted); Equal(190,f.Wallet.CurrentFunds);
            Equal(TrainingResult.AlreadyCompleted,f.Session.CompleteTraining(f.Session.Jobs[0].RequestId));Equal(2,f.Session.Models.Count);
        }
        private static void PauseAndRejection()
        {
            var f=new Fixture();var m=f.Train();f.Sim.Paused=false;Equal(TrainingResult.MustPause,f.Session.SelectModel(m.Id));
            f.Sim.Paused=true;f.RejectProfile=true;Equal(TrainingResult.HostRejected,f.Session.SelectModel(m.Id));Equal(f.InitialId,f.Session.ActiveModelId);
        }
        private static void SwapAll()
        {
            var f=new Fixture();var model=f.Train();var run=f.Request("running",100,100);f.Place(run);f.Run(.5);
            var ready=f.Request("ready");f.Place(ready,1);f.Run(1);var result=ready.Result;double progress=run.Progress,ability=run.GetAbility(AbilityKind.Writing),deadline=run.Deadline;
            int funds=f.Wallet.CurrentFunds;Equal(TrainingResult.Success,f.Session.SelectModel(model.Id));
            Equal(RequestState.Withdrawn,run.State);Equal(RequestState.Ready,ready.State);True(ready.ReadyInCandidate);
            Equal(progress,run.Progress);Equal(ability,run.GetAbility(AbilityKind.Writing));Equal(deadline,run.Deadline);
            True(ReferenceEquals(result,ready.Result));Equal(0,f.Sim.Occupancy.Count);f.Drain();Equal(funds,f.Wallet.CurrentFunds);
        }
        private static void ReadyFrozen()
        {
            var f=new Fixture();var ready=f.Request("ready");f.Place(ready);f.Run(1);var result=ready.Result;
            f.Session.SelectModel("initial");f.Run(200);Equal(RequestState.Ready,ready.State);True(ReferenceEquals(result,ready.Result));
            True(f.Sim.TrySubmit(ready.Id));True(!f.Sim.TrySubmit(ready.Id));
        }
        private static void TerminalOnce()
        {
            var f=new Fixture();f.Record();int funds=f.Wallet.CurrentFunds;f.Session.SelectModel("initial");f.Drain();
            Equal(RequestState.Submitted,f.Sim.Tasks["request"].State);Equal(funds,f.Wallet.CurrentFunds);
        }
        private static void ComputeOnly()
        {
            var f=new Fixture();var points=f.Sim.Model;int before=f.Wallet.CurrentFunds;
            Equal(TrainingResult.Success,f.Session.UpgradeCompute());Equal(20d,f.Sim.TokenThroughput);
            True(ReferenceEquals(points,f.Sim.Model));Equal(before-30,f.Wallet.CurrentFunds);
            Equal(TrainingResult.NoUpgrade,f.Session.UpgradeCompute());
        }
        private static void Architectures()
        {
            var f=new Fixture();f.Stage=2;var m=f.Train("a","alt");Equal(3d,m.Definition.Points[AbilityKind.Code]);f.Session.SelectModel(m.Id);
            Equal(5d,f.Sim.TokenThroughput);True(f.Sim.IsOpen(new Cell(1,3)));True(!f.Sim.IsOpen(new Cell(3,1)));
        }
        private static void IndependentRate()
        {
            var f=new Fixture();var a=f.Request("a",100,100);var b=f.Request("b",100,100);f.Place(a);f.Place(b,1);f.Run(2);
            Equal(20d,a.Progress);Equal(20d,b.Progress);
        }
        private static void TrainingPolicy()
        {
            var f=new Fixture();f.Policy=new PolicyModifiers(trainingScoreMultiplier:2,trainingCostMultiplier:.5,trainingWorkMultiplier:.5);
            var id=f.Record();int funds=f.Wallet.CurrentFunds;var job=f.Start(id);
            Equal(6d,job.Output.Definition.Points[AbilityKind.Code]);Equal(funds-10,f.Wallet.CurrentFunds);
            Equal(20d,f.Sim.Tasks[job.RequestId].Definition.Calculation);Equal(10d,f.Session.TokensPerSecond);
        }
        private static void InferencePolicy()
        {
            var f=new Fixture();var id=f.Record();f.Policy=new PolicyModifiers(inferenceSpeedMultiplier:2);
            Equal(TrainingResult.Success,f.Session.RefreshPolicyThroughput());Equal(20d,f.Sim.TokenThroughput);
            var job=f.Start(id);Equal(3d,job.Output.Definition.Points[AbilityKind.Code]);
        }
        private static void InvalidProfile()
        {
            var f=new Fixture();var t=f.Request("a",100,100);f.Place(t);var m=f.Sim.Model;
            True(!f.Sim.TryChangeExecutionProfile(m,double.NaN,f.Capacity));Equal(RequestState.Running,t.State);Equal(1,f.Sim.Occupancy.Count);
            True(!f.Sim.TryChangeExecutionProfile(m,20,new[]{new Cell(0,0)}));Equal(10d,f.Sim.TokenThroughput);
        }
        private static void ZeroCode()
        {
            var f=new Fixture();True(!f.Sim.Model.Points.ContainsKey(AbilityKind.Code));var m=f.Train();Equal(3d,m.Definition.Points[AbilityKind.Code]);
        }
        private static void TokenRatio()
        {
            var f=new Fixture();var t=f.Request("ratio",100,50);f.Place(t);f.Run(1);Equal(20d,t.Progress);
        }
        private static void RefundOnReject()
        {
            var f=new Fixture();f.RejectRate=true;Equal(TrainingResult.HostRejected,f.Session.UpgradeCompute());Equal(200,f.Wallet.CurrentFunds);Equal(0,f.Session.ComputeIndex);
        }
        private static void Reselect()
        {
            var f=new Fixture();f.Record();int before=f.Wallet.CurrentFunds;f.Session.SelectModel("initial");f.Session.SelectModel("initial");f.Drain();Equal(before,f.Wallet.CurrentFunds);Equal(1,f.OrdinaryCompleted);
        }
        private static void CanonicalLayout()
        {
            var f=new Fixture();f.Stage=2;var m=f.Train("a","alt");f.Session.SelectModel(m.Id);f.Session.SelectModel(m.Id);
            True(f.Sim.IsOpen(new Cell(1,3)));f.Session.SelectModel("initial");True(f.Sim.IsOpen(new Cell(3,1)));True(!f.Sim.IsOpen(new Cell(1,3)));
        }
        private static void ImmutableDefinitions()
        {
            var points=new Dictionary<AbilityKind,double>{{AbilityKind.Code,3}};var model=new ModelDefinition("m",1,points);points[AbilityKind.Code]=999;
            Equal(3d,model.Points[AbilityKind.Code]);
        }
        private static void FiveStageSchedule()
        {
            var f=new Fixture(thirdReady:true);
            Equal(TrainingResult.Success,f.Session.ArchitectureAvailability("base"));
            Equal(TrainingResult.ArchitectureLocked,f.Session.ArchitectureAvailability("alt"));
            f.Stage=2;Equal(TrainingResult.Success,f.Session.ArchitectureAvailability("alt"));
            f.Stage=3;Equal(TrainingResult.ArchitectureLocked,f.Session.ArchitectureAvailability("third"));
            f.Stage=4;Equal(TrainingResult.Success,f.Session.ArchitectureAvailability("third"));
            f.Stage=5;Equal(TrainingResult.Success,f.Session.ArchitectureAvailability("third"));
            bool invalid=false;
            try { new ArchitectureDefinition("invalid","invalid",1,OutlineTransform.Keep,6); }
            catch(ArgumentOutOfRangeException) { invalid=true; }
            True(invalid);
        }
        private static void PendingThird()
        {
            var f=new Fixture();f.Stage=4;var id=f.Record();int funds=f.Wallet.CurrentFunds;
            Equal(TrainingResult.ArchitecturePending,f.Session.ArchitectureAvailability("third"));
            Equal(TrainingResult.ArchitecturePending,f.Session.StartTraining("train","third","m",new[]{id},out _));
            Equal(funds,f.Wallet.CurrentFunds);True(!f.Session.IsConsumed(id));Equal(0,f.Session.Jobs.Count);
        }
        private static void LockedArchitecture()
        {
            var f=new Fixture();var id=f.Record();int funds=f.Wallet.CurrentFunds;
            Equal(TrainingResult.ArchitectureLocked,f.Session.StartTraining("train","alt","m",new[]{id},out _));
            Equal(funds,f.Wallet.CurrentFunds);True(!f.Session.IsConsumed(id));
            f.Stage=2;var job=f.Start(id,"alt");f.Place(f.Sim.Tasks[job.RequestId]);f.Run(4);f.Session.CompleteTraining(job.RequestId);
            f.Stage=1;Equal(TrainingResult.ArchitectureLocked,f.Session.SelectModel(job.Output.Id));Equal(f.InitialId,f.Session.ActiveModelId);
        }
        private static void RenameStarter()
        {
            var f=new Fixture();var request=f.Request("running",100,100);f.Place(request);f.Run(1);f.Sim.Paused=false;
            var model=f.Sim.Model;double progress=request.Progress,rate=f.Sim.TokenThroughput;int funds=f.Wallet.CurrentFunds;
            Equal(TrainingResult.Success,f.Session.RenameModel("initial","  小猫一号  "));
            Equal("小猫一号",f.Session.Models.Single().DisplayName);Equal(f.InitialId,f.Session.ActiveModelId);
            True(ReferenceEquals(model,f.Sim.Model));Equal(progress,request.Progress);Equal(rate,f.Sim.TokenThroughput);
            Equal(RequestState.Running,request.State);Equal(funds,f.Wallet.CurrentFunds);True(!f.Sim.Paused);
        }
        private static void RenameTrained()
        {
            var f=new Fixture();var model=f.Train();f.Session.SelectModel(model.Id);var definition=model.Definition;
            string id=model.Id,architecture=model.ArchitectureId;double rate=f.Sim.TokenThroughput;
            Equal(TrainingResult.Success,f.Session.RenameModel(id,"猫娘β"));
            Equal(id,model.Id);Equal(architecture,model.ArchitectureId);True(ReferenceEquals(definition,model.Definition));
            Equal(rate,f.Sim.TokenThroughput);Equal(id,f.Session.ActiveModelId);
            True(ReferenceEquals(model,f.Session.Jobs.Single().Output));Equal("猫娘β",f.Session.Jobs.Single().Output.DisplayName);
            Equal(TrainingResult.Success,f.Session.SelectModel(id));Equal(2,f.Session.Models.Count);
        }
        private static void RenameValidation()
        {
            var f=new Fixture();string before=f.Session.Models.Single().DisplayName;int notifications=0;f.Session.Changed+=()=>notifications++;
            foreach(var invalid in new[]{null,"","  \t ",new string('名',41),"名\n称"})
                Equal(TrainingResult.InvalidInput,f.Session.RenameModel("initial",invalid));
            Equal(TrainingResult.UnknownModel,f.Session.RenameModel("missing","valid"));Equal(before,f.Session.Models.Single().DisplayName);Equal(0,notifications);
            Equal(TrainingResult.Success,f.Session.RenameModel("initial",new string('名',40)));Equal(1,notifications);
            Equal(TrainingResult.Success,f.Session.RenameModel("initial",new string('名',40)));Equal(1,notifications);
        }
        private static void DuplicateNames()
        {
            var f=new Fixture();var trained=f.Train();var ids=f.Session.Models.Select(m=>m.Id).ToArray();
            Equal(TrainingResult.Success,f.Session.RenameModel("initial","相同名称"));
            Equal(TrainingResult.Success,f.Session.RenameModel(trained.Id,"相同名称"));
            Equal(2,f.Session.Models.Count(m=>m.DisplayName=="相同名称"));True(ids.SequenceEqual(f.Session.Models.Select(m=>m.Id)));
            Equal(TrainingResult.Success,f.Session.SelectModel(trained.Id));Equal(trained.Id,f.Session.ActiveModelId);
        }
        private static void UuidInstances()
        {
            var f=new Fixture();var trained=f.Train();var starter=f.Session.Models.First(m=>m.Id==f.InitialId);
            True(Guid.TryParseExact(starter.Id,"D",out _));True(Guid.TryParseExact(trained.Id,"D",out _));True(starter.Id!=trained.Id);
            Equal(starter.Id,starter.Definition.Id);Equal(starter.Id,starter.ModelId);Equal(9,starter.ShortModelId.Length);
            Equal("initial",starter.LegacyId);Equal(TrainingResult.Success,f.Session.SelectModel("initial"));Equal(starter.Id,f.Sim.Model.Id);
        }
        private static void UuidReload()
        {
            var f=new Fixture();var trained=f.Train();f.Session.RenameModel(trained.Id,"重载后的名字");
            var state=ModelPersistence.Capture(trained);var registry=new ModelIdentityRegistry();
            True(ModelPersistence.TryRestore(state,registry,out var first));True(ModelPersistence.TryRestore(state,registry,out var second));
            Equal(trained.Id,first.Id);Equal(first.Id,second.Id);Equal("重载后的名字",second.DisplayName);
            Equal(trained.ArchitectureId,second.ArchitectureId);Equal(3d,second.Definition.Points[AbilityKind.Code]);
            var loaded=new Dictionary<string,CatalogModel>();loaded[first.Id]=first;loaded[second.Id]=second;Equal(1,loaded.Count);
        }
        private static void LegacyMigration()
        {
            var state=new CatalogModelState {ModelId="trained-legacy",DisplayName="旧模型",ArchitectureId="base",AbilityInterval=1,
                Abilities=new[]{new ModelAbilityState{Kind=AbilityKind.Code,Points=7}}};
            var registry=new ModelIdentityRegistry();True(ModelPersistence.TryRestore(state,registry,out var migrated));
            var savedMap=registry.Capture();var loadedRegistry=new ModelIdentityRegistry();
            True(loadedRegistry.TryRestore(savedMap));True(loadedRegistry.TryRestore(savedMap));
            True(ModelPersistence.TryRestore(state,loadedRegistry,out var reloaded));Equal(migrated.Id,reloaded.Id);
            Equal("trained-legacy",reloaded.LegacyId);Equal(7d,reloaded.Definition.Points[AbilityKind.Code]);
            True(ModelPersistence.TryRestore(ModelPersistence.Capture(migrated),loadedRegistry,out var upgraded));Equal(migrated.Id,upgraded.Id);
        }
        private static void InvalidIdentityRestore()
        {
            var registry=new ModelIdentityRegistry();string id=registry.ResolveOrCreate("old");
            var invalid=new ModelIdentityState{Entries=new[]{new ModelIdentityEntry{LegacyId="a",ModelId=id},new ModelIdentityEntry{LegacyId="b",ModelId=id}}};
            True(!registry.TryRestore(invalid));Equal(id,registry.ResolveKnown("old"));Equal(1,registry.Capture().Entries.Length);
        }
        private static void ShortIdCollision()
        {
            string a="7f3a92c1-0000-4000-8000-000000000001",b="7f3a92c1-ffff-4000-8000-000000000002";
            var first=new CatalogModel("同名","base",new ModelDefinition(a,1,new Dictionary<AbilityKind,double>()),null);
            var second=new CatalogModel("同名","base",new ModelDefinition(b,1,new Dictionary<AbilityKind,double>()),null);
            Equal(first.ShortModelId,second.ShortModelId);True(first.Id!=second.Id);
            Equal(2,new Dictionary<string,CatalogModel>{{first.Id,first},{second.Id,second}}.Count);
        }
        private static void StarterFirstCorpus()
        {
            var f=new Fixture();Equal(0,f.Session.Jobs.Count);Equal(0,f.Session.Corpus.Count);
            f.Record("easy-first",AbilityKind.Writing,2);
            Equal(1,f.Session.Models.Count);Equal(0,f.Session.Jobs.Count);Equal(1,f.Session.Corpus.Count);
            Equal(Quality.Excellent,f.Sim.Tasks["easy-first"].Result.Overall);
            Equal(2d,f.Session.Corpus.Single().Scores[AbilityKind.Writing]);
        }
        private static void ArchitectureIdentity()
        {
            var f=new Fixture();var model=f.Train();var restored=new CatalogModel(model.DisplayName,"alt",model.Definition,model.CorpusIds);
            Equal(model.Id,restored.Id);Equal("alt",restored.ArchitectureId);Equal("base",model.ArchitectureId);
            Equal(model.Definition.Points[AbilityKind.Code],restored.Definition.Points[AbilityKind.Code]);
        }
        private static TaskSimulation CloneSimulation(Fixture f)
        {
            f.Drain();
            var capture=typeof(TaskSimulation).GetMethod("CaptureState");
            if(capture==null)throw new Exception("Session persistence tests require task-13 TaskSimulation.Persistence companion.");
            var state=capture.Invoke(f.Sim,null);
            return (TaskSimulation)typeof(TaskSimulation).GetMethod("RestoreState").Invoke(null,new[]{state});
        }
        private static TrainingSession RestoreSession(Fixture f,TrainingSessionState state,TaskSimulation sim,
            Func<ModelDefinition,ModelDefinition> compose=null,Action onGameplayCall=null)
        {
            return TrainingSession.RestoreState(state,sim,f.Session.Recipes,f.Session.Architectures,f.Session.ComputeTiers,
                amount=>{onGameplayCall?.Invoke();return f.Wallet.TrySpendFunds(amount);},
                amount=>{onGameplayCall?.Invoke();f.Wallet.AddFunds(amount);},
                profile=>{onGameplayCall?.Invoke();return sim.TryChangeExecutionProfile(profile.Model,profile.TokensPerSecond,profile.OpenCells,profile.Architecture);},
                rate=>{onGameplayCall?.Invoke();return sim.TrySetTokenThroughput(rate);},
                ()=>f.Capacity,compose,()=>f.Policy,()=>f.Stage,sim.TokenThroughput);
        }
        private static void SessionRoundtrip()
        {
            var f=new Fixture();var complete=f.Train("first");f.Session.RenameModel(complete.Id,"保存后的名字");
            string second=f.Record("second"),free=f.Record("third");var pending=f.Start(second);f.Place(f.Sim.Tasks[pending.RequestId]);f.Run(1);
            var state=f.Session.CaptureState();var sim=CloneSimulation(f);
            int funds=f.Wallet.CurrentFunds,calls=0;var restored=RestoreSession(f,state,sim,onGameplayCall:()=>calls++);
            Equal(0,calls);Equal(funds,f.Wallet.CurrentFunds);Equal(3,restored.Corpus.Count);Equal(2,restored.Jobs.Count);
            Equal(10d,sim.Tasks[pending.RequestId].Progress);True(restored.IsConsumed(pending.CorpusIds[0]));
            Equal(TrainingResult.AlreadyRecorded,restored.RecordDialogue("third","node","reply",new Dictionary<AbilityKind,double>{{AbilityKind.Code,3}},"story-explicit"));
            Equal(TrainingResult.CorpusConsumed,restored.StartTraining("train","base","again",pending.CorpusIds,out _));
            Equal(TrainingResult.Success,restored.StartTraining("train","base","new",new[]{free},out var next));Equal("training-3",next.RequestId);
            var savedJob=restored.Jobs.First(j=>j.Completed);True(ReferenceEquals(savedJob.Output,restored.Models.Single(m=>m.Id==complete.Id)));
            Equal("保存后的名字",savedJob.Output.DisplayName);
        }
        private static void SessionRepeatRestore()
        {
            var f=new Fixture();f.Train();var state=f.Session.CaptureState();var sim=CloneSimulation(f);int calls=0,funds=f.Wallet.CurrentFunds;
            var a=RestoreSession(f,state,sim,onGameplayCall:()=>calls++);var b=RestoreSession(f,state,sim,onGameplayCall:()=>calls++);
            Equal(0,calls);Equal(funds,f.Wallet.CurrentFunds);True(a.Models.Select(m=>m.Id).SequenceEqual(b.Models.Select(m=>m.Id)));
            Equal(TrainingResult.AlreadyCompleted,b.CompleteTraining(b.Jobs.Single().RequestId));
            // Capture succeeds only with an empty event queue: restore did not enqueue/replay anything.
            typeof(TaskSimulation).GetMethod("CaptureState").Invoke(sim,null);
        }
        private static void SessionInvalidState()
        {
            var f=new Fixture();f.Train();var state=f.Session.CaptureState();state.ConsumedCorpusIds=Array.Empty<string>();bool rejected=false;
            try{TrainingSession.ValidateState(state,f.Session.Recipes,f.Session.Architectures,f.Session.ComputeTiers);}catch(ArgumentException){rejected=true;}True(rejected);
            state=f.Session.CaptureState();var sim=CloneSimulation(f);state.Corpus[0].RequestId="missing";state.RecordedFragments[0].RequestId="missing";
            rejected=false;int calls=0;
            try{RestoreSession(f,state,sim,onGameplayCall:()=>calls++);}catch(ArgumentException){rejected=true;}True(rejected);Equal(0,calls);
            Equal(1,f.Session.Corpus.Count);Equal(2,f.Session.Models.Count);
        }
        private static void SessionReadyCandidate()
        {
            var f=new Fixture();var job=f.Start(f.Record());f.Place(f.Sim.Tasks[job.RequestId]);f.Run(4);f.Session.SelectModel(f.InitialId);
            var state=f.Session.CaptureState();var sim=CloneSimulation(f);var restored=RestoreSession(f,state,sim);
            True(sim.Tasks[job.RequestId].ReadyInCandidate);Equal(RequestState.Ready,sim.Tasks[job.RequestId].State);
            Equal(TrainingResult.Success,restored.CompleteTraining(job.RequestId));Equal(TrainingResult.AlreadyCompleted,restored.CompleteTraining(job.RequestId));
            Equal(job.Output.Id,restored.Models.Single(m=>m.Id==job.Output.Id).Id);
        }
        private static void SessionRenamedModel()
        {
            var f=new Fixture();var model=f.Train();f.Session.SelectModel(model.Id);f.Session.RenameModel(model.Id,"当前UUID模型");
            var restored=RestoreSession(f,f.Session.CaptureState(),CloneSimulation(f));
            Equal(model.Id,restored.ActiveModelId);Equal("当前UUID模型",restored.Models.Single(m=>m.Id==model.Id).DisplayName);
            var job=restored.Jobs.Single();True(ReferenceEquals(job.Output,restored.Models.Single(m=>m.Id==model.Id)));
            restored.RenameModel(model.Id,"二次改名");Equal("二次改名",job.Output.DisplayName);
        }
        private static void SessionBonusOnce()
        {
            var f=new Fixture();var model=f.Train();f.Session.SelectModel(model.Id);
            Func<ModelDefinition,ModelDefinition> compose=baseModel=>{
                var points=baseModel.Points.ToDictionary(p=>p.Key,p=>p.Value);points[AbilityKind.Writing]=1;
                return new ModelDefinition(baseModel.Id,baseModel.Interval,points);
            };
            f.Sim.TryChangeModel(compose(model.Definition));var state=f.Session.CaptureState();var sim=CloneSimulation(f);
            var restored=RestoreSession(f,state,sim,compose);Equal(TrainingResult.Success,restored.SelectModel(model.Id));
            Equal(1d,sim.Model.Points[AbilityKind.Writing]);True(!restored.Models.Single(m=>m.Id==model.Id).Definition.Points.ContainsKey(AbilityKind.Writing));
        }
        private static void SessionBusyCapture()
        {
            var f=new Fixture();f.Record();TrainingSession session=null;bool busyRejected=false;
            session=new TrainingSession(f.Sim,f.Session.Models.Single(),f.Session.Recipes,f.Session.Architectures,f.Session.ComputeTiers,
                amount=>{try{session.CaptureState();}catch(InvalidOperationException){busyRejected=true;}return f.Wallet.TrySpendFunds(amount);},
                f.Wallet.AddFunds,p=>f.Sim.TryChangeExecutionProfile(p.Model,p.TokensPerSecond,p.OpenCells,p.Architecture),
                rate=>f.Sim.TrySetTokenThroughput(rate),()=>f.Capacity);
            session.RecordDialogue("request","node","reply",new Dictionary<AbilityKind,double>{{AbilityKind.Code,3}},"story-explicit");
            Equal(TrainingResult.Success,session.StartTraining("train","base","busy",new[]{session.Corpus.Single().Id},out _));True(busyRejected);
        }
        private static void SessionJsonRoundtrip()
        {
            if(SerializationRoundTrip==null)throw new Exception("Configure the actual JSON codec in the test host.");
            var f=new Fixture();var model=f.Train("saved-first");f.Session.RenameModel(model.Id,"可重载的模型");
            var pending=f.Start(f.Record("saved-second"));f.Place(f.Sim.Tasks[pending.RequestId]);f.Run(1);
            var dto=SerializationRoundTrip(f.Session.CaptureState());var restored=RestoreSession(f,dto,CloneSimulation(f));
            Equal(model.Id,restored.Models.Single(m=>m.Id==model.Id).Id);Equal("可重载的模型",restored.Models.Single(m=>m.Id==model.Id).DisplayName);
            Equal(2,restored.Jobs.Count);True(restored.IsConsumed(pending.CorpusIds[0]));
            Equal(10d,restored.Simulation.Tasks[pending.RequestId].Progress);Equal(f.InitialId,restored.ActiveModelId);
        }
        private static void True(bool value) { if(!value)throw new Exception("Expected true."); }
        private static void Equal<T>(T expected,T actual) { if(!EqualityComparer<T>.Default.Equals(expected,actual))throw new Exception("Expected "+expected+", got "+actual); }
    }
}
