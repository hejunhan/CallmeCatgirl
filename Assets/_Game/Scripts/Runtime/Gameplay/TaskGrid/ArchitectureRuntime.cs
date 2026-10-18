using System;
namespace CallmeCatgirl.Gameplay.TaskGrid
{
    /// <summary>Immutable architecture bonuses, independent of model abilities and hardware tiers.</summary>
    public sealed class ArchitectureRuntime
    {
        public static readonly ArchitectureRuntime Neutral = new ArchitectureRuntime("legacy",1,1,1,false);
        public string Id { get; }
        public double SpeedMultiplier { get; }
        public double AbilityRateMultiplier { get; }
        public double CorpusMultiplier { get; }
        public bool FreezeOnPlacement { get; }
        public ArchitectureRuntime(string id,double speed,double abilityRate,double corpus,bool freeze=true)
        {
            if(string.IsNullOrWhiteSpace(id))throw new ArgumentException("Architecture ID required.");
            Rules.Positive(speed,nameof(speed));Rules.Positive(abilityRate,nameof(abilityRate));Rules.Positive(corpus,nameof(corpus));
            Id=id;SpeedMultiplier=speed;AbilityRateMultiplier=abilityRate;CorpusMultiplier=corpus;FreezeOnPlacement=freeze;
        }
        public bool SameAs(ArchitectureRuntime other)=>other!=null&&Id==other.Id&&SpeedMultiplier==other.SpeedMultiplier
            &&AbilityRateMultiplier==other.AbilityRateMultiplier&&CorpusMultiplier==other.CorpusMultiplier&&FreezeOnPlacement==other.FreezeOnPlacement;
    }
    [Serializable] public sealed class ArchitectureRuntimeState
    {
        public string Id;
        public double Speed,AbilityRate,Corpus;
        public bool Freeze;
        public ArchitectureRuntime Build()=>new ArchitectureRuntime(Id,Speed,AbilityRate,Corpus,Freeze);
        public static ArchitectureRuntimeState Capture(ArchitectureRuntime a)=>a==null?null:new ArchitectureRuntimeState
            {Id=a.Id,Speed=a.SpeedMultiplier,AbilityRate=a.AbilityRateMultiplier,Corpus=a.CorpusMultiplier,Freeze=a.FreezeOnPlacement};
    }
}
