using System;
using System.Collections.Generic;
using System.Linq;
using CallmeCatgirl.Gameplay.TaskGrid;
namespace CallmeCatgirl.Gameplay.Training
{
    public static class CorpusRewards
    {
        // Use the required completion threshold, never produced values or the excellent threshold.
        // Ties use the stable AbilityKind order and still award only one half-sized pool.
        public static IDictionary<AbilityKind,double> FromRequirements(IEnumerable<AbilityRequirement> requirements,double ratio)
        {
            Check.Number(ratio);
            var highest=requirements?.OrderByDescending(r=>r.Minimum).ThenBy(r=>r.Kind).FirstOrDefault();
            if(highest==null||highest.Minimum==0||ratio==0)return new Dictionary<AbilityKind,double>();
            double amount=highest.Minimum*ratio;Check.Number(amount,true);
            return new Dictionary<AbilityKind,double>{{highest.Kind,amount}};
        }
    }
}
