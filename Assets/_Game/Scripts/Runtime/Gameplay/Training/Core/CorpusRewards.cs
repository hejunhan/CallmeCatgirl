using System;
using System.Collections.Generic;
using System.Linq;
using CallmeCatgirl.Gameplay.TaskGrid;
namespace CallmeCatgirl.Gameplay.Training
{
    public static class CorpusRewards
    {
        // Use the required completion threshold, never produced values or the excellent threshold.
        public static IDictionary<AbilityKind,double> FromRequirements(IEnumerable<AbilityRequirement> requirements,double ratio)
        {
            Check.Number(ratio);
            var highest=requirements?.OrderByDescending(r=>r.Minimum).ThenBy(r=>r.Kind).FirstOrDefault();
            if(highest==null||highest.Minimum==0||ratio==0)return new Dictionary<AbilityKind,double>();
            double amount=highest.Minimum*ratio;Check.Number(amount,true);
            return new Dictionary<AbilityKind,double>{{highest.Kind,amount}};
        }

        public static IDictionary<AbilityKind,double> FromAllRequirements(IEnumerable<AbilityRequirement> requirements,double ratio)
        {
            Check.Number(ratio);
            return FromAllRequirements(requirements, _ => ratio);
        }

        public static IDictionary<AbilityKind,double> FromAllRequirements(IEnumerable<AbilityRequirement> requirements,
            Func<AbilityRequirement,double> ratioSelector)
        {
            if (ratioSelector == null) throw new ArgumentNullException(nameof(ratioSelector));
            var scores = new Dictionary<AbilityKind,double>();
            foreach (var requirement in requirements ?? Enumerable.Empty<AbilityRequirement>())
            {
                if (requirement.Minimum <= 0) continue;
                double ratio = ratioSelector(requirement);
                Check.Number(ratio);
                if (ratio == 0) continue;
                double amount = requirement.Minimum * ratio;
                Check.Number(amount, true);
                scores.Add(requirement.Kind, amount);
            }
            return scores;
        }
    }
}
