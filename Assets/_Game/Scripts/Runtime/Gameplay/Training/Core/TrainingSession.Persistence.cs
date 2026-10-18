using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CallmeCatgirl.Gameplay.TaskGrid;

namespace CallmeCatgirl.Gameplay.Training
{
    [Serializable] public sealed class CorpusRecordState
    {
        public string Id, RequestId, ConversationId, RoundId, NodeId, ChoiceId, ScoreSource;
        public Quality Quality;
        public ModelAbilityState[] Scores = Array.Empty<ModelAbilityState>();
    }
    [Serializable] public sealed class RecordedFragmentState { public string RequestId, NodeId; }
    [Serializable] public sealed class TrainingJobState
    {
        public string RequestId, RecipeId;
        public CatalogModelState Output;
        public bool Completed;
        public int PaidCost;
        public double WorkTokens;
        public CorpusUse[] Uses;
    }
    [Serializable] public sealed class TrainingSessionState
    {
        public int Version = 1;
        public int Serial, ComputeIndex, Stage;
        public string ActiveModelId, ComputeTierId;
        public double TokensPerSecond;
        public ModelIdentityState Identities;
        public CorpusRecordState[] Corpus = Array.Empty<CorpusRecordState>();
        public string[] ConsumedCorpusIds = Array.Empty<string>();
        public RecordedFragmentState[] RecordedFragments = Array.Empty<RecordedFragmentState>();
        public CatalogModelState[] Models = Array.Empty<CatalogModelState>();
        public TrainingJobState[] Jobs = Array.Empty<TrainingJobState>();
    }

    public sealed partial class TrainingSession
    {
        public const int StateVersion = 1;
        public const string ModuleRevision = "2.0-session-state-v1";

        public TrainingSessionState CaptureState()
        {
            if (busy) throw new InvalidOperationException("Capture after the command boundary.");
            return new TrainingSessionState {
                Version=StateVersion,Serial=serial,ComputeIndex=ComputeIndex,ComputeTierId=Compute.Id,Stage=CurrentStage,
                ActiveModelId=ActiveModelId,TokensPerSecond=TokensPerSecond,Identities=Identities.Capture(),
                Corpus=corpus.Values.Select(r=>new CorpusRecordState {
                    Id=r.Id,RequestId=r.RequestId,ConversationId=r.ConversationId,RoundId=r.RoundId,NodeId=r.NodeId,
                    ChoiceId=r.ChoiceId,ScoreSource=r.ScoreSource,Quality=r.Quality,
                    Scores=r.Scores.Select(p=>new ModelAbilityState{Kind=p.Key,Points=p.Value}).ToArray()
                }).ToArray(),
                ConsumedCorpusIds=consumed.OrderBy(id=>id,StringComparer.Ordinal).ToArray(),
                RecordedFragments=recordedFragments.Select(k=>new RecordedFragmentState{RequestId=k.Item1,NodeId=k.Item2}).ToArray(),
                Models=models.Values.Select(ModelPersistence.Capture).ToArray(),
                Jobs=jobs.Values.Select(j=>new TrainingJobState {
                    RequestId=j.RequestId,RecipeId=j.RecipeId,Output=ModelPersistence.Capture(j.Output),Completed=j.Completed,
                    PaidCost=j.PaidCost,WorkTokens=j.WorkTokens,Uses=j.Uses?.Select(u=>new CorpusUse{CorpusId=u.CorpusId,Points=u.Points}).ToArray()
                }).ToArray()
            };
        }

        /// <summary>Pure native-DTO/config validation for StateSaveExtension. No simulation, wallet, UUID generation or reflection.</summary>
        public static void ValidateState(TrainingSessionState state, IEnumerable<TrainingRecipe> recipes,
            IEnumerable<ArchitectureDefinition> architectures, IEnumerable<ComputeTier> compute)
        {
            BuildState(state,recipes,architectures,compute);
        }

        private sealed class DecodedState
        {
            internal readonly Dictionary<string,CorpusRecord> Corpus=new Dictionary<string,CorpusRecord>(StringComparer.Ordinal);
            internal readonly Dictionary<string,CatalogModel> Models=new Dictionary<string,CatalogModel>(StringComparer.Ordinal);
            internal readonly Dictionary<string,TrainingJob> Jobs=new Dictionary<string,TrainingJob>(StringComparer.Ordinal);
            internal readonly HashSet<string> Consumed=new HashSet<string>(StringComparer.Ordinal);
            internal readonly HashSet<Tuple<string,string>> Fragments=new HashSet<Tuple<string,string>>();
            internal readonly ModelIdentityRegistry Identities=new ModelIdentityRegistry();
        }
        private static void RequireState(bool condition,string reason)
        { if(!condition)throw new ArgumentException("Invalid training state: "+reason); }
        private static bool SameModel(CatalogModel a,CatalogModel b)
            => a.Id==b.Id && a.LegacyId==b.LegacyId && a.DisplayName==b.DisplayName && a.ArchitectureId==b.ArchitectureId
                && SameDefinition(a.Definition,b.Definition) && a.CorpusIds.SequenceEqual(b.CorpusIds);
        private static bool SameDefinition(ModelDefinition a,ModelDefinition b)
            => a!=null && b!=null && a.Id==b.Id && a.Interval==b.Interval
                && Enum.GetValues(typeof(AbilityKind)).Cast<AbilityKind>().All(k=>
                    (a.Points.TryGetValue(k,out var x)?x:0)==(b.Points.TryGetValue(k,out var y)?y:0));

        private static DecodedState BuildState(TrainingSessionState s,IEnumerable<TrainingRecipe> recipeSource,
            IEnumerable<ArchitectureDefinition> architectureSource,IEnumerable<ComputeTier> computeSource)
        {
            RequireState(s!=null && s.Version==StateVersion && s.Serial>=0 && s.Stage>=1 && s.Stage<=5,"schema/header");
            var recipeMap=recipeSource?.ToDictionary(r=>r.Id,StringComparer.Ordinal) ?? throw new ArgumentNullException(nameof(recipeSource));
            var archMap=architectureSource?.ToDictionary(a=>a.Id,StringComparer.Ordinal) ?? throw new ArgumentNullException(nameof(architectureSource));
            var tiers=computeSource?.ToArray() ?? throw new ArgumentNullException(nameof(computeSource));
            RequireState(recipeMap.Count>0 && archMap.Count>0 && s.ComputeIndex>=0 && s.ComputeIndex<tiers.Length
                && tiers[s.ComputeIndex]!=null && tiers[s.ComputeIndex].Id==s.ComputeTierId,"installed compute tier");
            Check.Number(s.TokensPerSecond,true);
            RequireState(s.Corpus!=null && s.Corpus.Length<=100000 && s.Models!=null && s.Models.Length>0 && s.Models.Length<=10000
                && s.Jobs!=null && s.Jobs.Length<=100000 && s.ConsumedCorpusIds!=null && s.RecordedFragments!=null,"collections");
            var decoded=new DecodedState();RequireState(decoded.Identities.TryRestore(s.Identities),"identity map");
            foreach(var item in s.Corpus)
            {
                RequireState(item!=null && item.Scores!=null && item.Scores.All(p=>p!=null),"corpus fields");
                var r=new CorpusRecord(item.Id,item.RequestId,item.ConversationId,item.RoundId,item.NodeId,item.ChoiceId,
                    item.ScoreSource,item.Quality,item.Scores.ToDictionary(p=>p.Kind,p=>p.Points));
                RequireState(!decoded.Corpus.ContainsKey(r.Id) && decoded.Fragments.Add(Tuple.Create(r.RequestId,r.NodeId)),"corpus/fragment duplicate");
                decoded.Corpus.Add(r.Id,r);
            }
            RequireState(Enumerable.Range(1,decoded.Corpus.Count).All(i=>decoded.Corpus.ContainsKey("corpus-"+i)),"corpus ID sequence");
            var savedFragments=new HashSet<Tuple<string,string>>();
            foreach(var f in s.RecordedFragments)
                RequireState(f!=null && !string.IsNullOrWhiteSpace(f.RequestId) && !string.IsNullOrWhiteSpace(f.NodeId)
                    && savedFragments.Add(Tuple.Create(f.RequestId,f.NodeId)),"recorded fragments");
            RequireState(savedFragments.SetEquals(decoded.Fragments),"recorded fragment ledger differs from corpus");
            Func<CatalogModelState,CatalogModel> readModel=item=>{
                RequireState(item!=null && item.ModelId==ModelIdentityRegistry.Canonical(item.ModelId),"canonical UUID");
                RequireState(ModelPersistence.TryRestore(item,decoded.Identities,out var model),"model snapshot");
                RequireState(archMap.TryGetValue(model.ArchitectureId,out var architecture) && architecture.ContentReady
                    && architecture.UnlockStage<=s.Stage,"model architecture/phase");
                RequireState(model.CorpusIds.Distinct(StringComparer.Ordinal).Count()==model.CorpusIds.Count
                    && model.CorpusIds.All(decoded.Corpus.ContainsKey),"model corpus references");
                return model;
            };
            foreach(var item in s.Models)
            {
                var model=readModel(item);RequireState(!decoded.Models.ContainsKey(model.Id),"duplicate model UUID");decoded.Models.Add(model.Id,model);
            }
            RequireState(s.ActiveModelId!=null && decoded.Models.ContainsKey(s.ActiveModelId),"active model reference");
            var outputIds=new HashSet<string>(StringComparer.Ordinal);
            foreach(var j in s.Jobs)
            {
                RequireState(j!=null && !string.IsNullOrWhiteSpace(j.RequestId) && recipeMap.ContainsKey(j.RecipeId ?? "")
                    && j.PaidCost>=0,"job header");Check.Number(j.WorkTokens,true);
                RequireState(j.RequestId.StartsWith("training-",StringComparison.Ordinal)
                    && int.TryParse(j.RequestId.Substring(9),NumberStyles.None,CultureInfo.InvariantCulture,out var number)
                    && number>0 && number<=s.Serial,"job serial");
                var output=readModel(j.Output);
                RequireState(output.CorpusIds.Count>0 && outputIds.Add(output.Id),"job output identity");
                if(j.Uses != null)
                {
                    RequireState(j.Uses.Length==output.CorpusIds.Count && j.Uses.All(u=>u!=null)
                        && j.Uses.Select(u=>u.CorpusId).SequenceEqual(output.CorpusIds),"job allocation references");
                    foreach(var u in j.Uses)Check.Number(u.Points,true);
                }
                if(j.Completed)
                {
                    RequireState(decoded.Models.TryGetValue(output.Id,out var catalog) && SameModel(output,catalog),"completed job/catalog mismatch");
                    output=catalog; // Share one instance so renaming updates job + catalog consistently after restore.
                }
                else RequireState(!decoded.Models.ContainsKey(output.Id),"unfinished output already in catalog");
                RequireState(!decoded.Jobs.ContainsKey(j.RequestId),"duplicate training request");
                decoded.Jobs.Add(j.RequestId,new TrainingJob(j.RequestId,output,j.RecipeId,j.PaidCost,j.WorkTokens){Completed=j.Completed,Uses=j.Uses==null?null:Array.AsReadOnly(j.Uses.Select(u=>new CorpusUse{CorpusId=u.CorpusId,Points=u.Points}).ToArray())});
            }
            foreach(var record in decoded.Corpus.Values)
            {
                double spent=decoded.Jobs.Values.Sum(j=>j.Uses==null
                    ? (j.CorpusIds.Contains(record.Id)?record.Scores.Values.Sum():0)
                    : j.Uses.Where(u=>u.CorpusId==record.Id).Sum(u=>u.Points));
                RequireState(spent<=record.Scores.Values.Sum()+1e-9,"corpus overspent");
                if(record.Scores.Values.Sum()-spent<=1e-9)decoded.Consumed.Add(record.Id);
            }
            RequireState(decoded.Models.Values.All(m=>m.CorpusIds.Count==0 || outputIds.Contains(m.Id)),"orphan trained model");
            RequireState(s.ConsumedCorpusIds.All(id=>id!=null) && s.ConsumedCorpusIds.Distinct(StringComparer.Ordinal).Count()==s.ConsumedCorpusIds.Length
                && decoded.Consumed.SetEquals(s.ConsumedCorpusIds),"consumption ledger mismatch");
            var allIds=new HashSet<string>(decoded.Models.Keys.Concat(outputIds),StringComparer.Ordinal);
            RequireState(decoded.Identities.Capture().Entries.All(e=>allIds.Contains(e.ModelId)),"dangling UUID alias");
            return decoded;
        }

        /// <summary>Prepare a detached training session beside already restored Simulation/Economy.
        /// Never calls StartTraining/CompleteTraining, spends/refunds, advances/drains, switches model, or emits gameplay events.</summary>
        public static TrainingSession RestoreState(TrainingSessionState state,TaskSimulation restoredSimulation,
            IEnumerable<TrainingRecipe> recipes,IEnumerable<ArchitectureDefinition> architectures,IEnumerable<ComputeTier> compute,
            Func<int,bool> spend,Action<int> refund,Func<ExecutionProfile,bool> applyProfile,Func<double,bool> applyThroughput,
            Func<IEnumerable<Cell>> capacityCells,Func<ModelDefinition,ModelDefinition> composeBonuses=null,
            Func<PolicyModifiers> policyModifiers=null,Func<int> currentStage=null,double? restoredTokensPerSecond=null)
        {
            var recipeArray=recipes?.ToArray();var architectureArray=architectures?.ToArray();var computeArray=compute?.ToArray();
            var decoded=BuildState(state,recipeArray,architectureArray,computeArray);
            var restored=new TrainingSession(restoredSimulation,null,recipeArray,architectureArray,computeArray,spend,refund,
                applyProfile,applyThroughput,capacityCells,composeBonuses,policyModifiers,currentStage,decoded.Identities,true);
            RequireState(restored.CurrentStage==state.Stage,"host stage differs from training stage");
            restored.serial=state.Serial;restored.ComputeIndex=state.ComputeIndex;restored.ActiveModelId=state.ActiveModelId;
            foreach(var p in decoded.Models)restored.models.Add(p.Key,p.Value);
            foreach(var p in decoded.Corpus)restored.corpus.Add(p.Key,p.Value);
            foreach(var p in decoded.Jobs)restored.jobs.Add(p.Key,p.Value);
            restored.consumed.UnionWith(decoded.Consumed);restored.recordedFragments.UnionWith(decoded.Fragments);
            foreach(var r in restored.corpus.Values)
                RequireState(restoredSimulation.Tasks.TryGetValue(r.RequestId,out var task) && task.Result!=null
                    && (task.State==RequestState.Submitted || task.State==RequestState.Closed) && task.Result.Overall==r.Quality
                    && task.ConversationId==r.ConversationId && task.RoundId==r.RoundId && !restored.jobs.ContainsKey(r.RequestId),"corpus/task source mismatch");
            foreach(var j in restored.jobs.Values)
            {
                RequireState(restoredSimulation.Tasks.TryGetValue(j.RequestId,out var task)
                    && task.Definition.Id=="training:"+j.RecipeId && task.Definition.Tokens==0 && task.Definition.Requirements.Count==0
                    && task.Definition.Calculation==j.WorkTokens && task.ConversationId==j.RequestId && task.RoundId=="training","training/task mismatch");
                RequireState(j.Completed ? task.State==RequestState.Closed && task.Result!=null
                    : task.State==RequestState.Waiting || task.State==RequestState.Running || task.State==RequestState.Withdrawn || task.State==RequestState.Ready,
                    "training completion state mismatch");
            }
            var active=restored.models[state.ActiveModelId];
            RequireState(SameDefinition(restored.composeBonuses(active.Definition),restoredSimulation.Model),"effective model differs (permanent bonuses must compose once)");
            double expected=restored.TokensPerSecond;
            RequireState(Math.Abs(expected-state.TokensPerSecond)<=1e-9*Math.Max(1,expected),"saved execution rate differs from policy/compute");
            if(restoredTokensPerSecond.HasValue)
                RequireState(!double.IsNaN(restoredTokensPerSecond.Value) && !double.IsInfinity(restoredTokensPerSecond.Value)
                    && Math.Abs(expected-restoredTokensPerSecond.Value)<=1e-9*Math.Max(1,expected),"TaskGrid execution rate mismatch");
            var expectedCells=restored.architectures[active.ArchitectureId].Transform(capacityCells(),restoredSimulation.Width,restoredSimulation.Height);
            RequireState(expectedCells.Length==restoredSimulation.OpenCellCount && expectedCells.All(restoredSimulation.IsOpen),"architecture outline differs from TaskGrid snapshot");
            return restored;
        }
    }
}
