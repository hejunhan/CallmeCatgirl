using System;
using System.Linq;
using CallmeCatgirl.Persistence;
using CallmeCatgirl.Gameplay.TaskGrid;

namespace CallmeCatgirl.Gameplay
{
    public sealed partial class EconomyManager
    {
        public EconomyState CaptureState() => new EconomyState
        {
            Version = 1, Funds = CurrentFunds, TokenPricePerUnit = TokenPricePerUnit, Debt = DebtAmount,
            GracePeriod = IsInGracePeriod, Bankrupt = IsBankrupt,
            SettledRequestIds = settledRequests.OrderBy(id => id, StringComparer.Ordinal).ToArray()
        };
        public static EconomyManager RestoreState(EconomyState s)
        {
            StateRules.Require(s != null && s.Version == 1 && s.Funds >= 0 && s.TokenPricePerUnit >= 0 && s.Debt >= 0
                && s.SettledRequestIds != null && s.SettledRequestIds.All(id => !string.IsNullOrWhiteSpace(id))
                && s.SettledRequestIds.Distinct(StringComparer.Ordinal).Count() == s.SettledRequestIds.Length
                && (s.GracePeriod || (s.Debt == 0 && !s.Bankrupt)), "Invalid wallet state.");
            var economy = new EconomyManager(s.Funds, s.TokenPricePerUnit)
                { DebtAmount = s.Debt, IsInGracePeriod = s.GracePeriod, IsBankrupt = s.Bankrupt };
            economy.settledRequests.UnionWith(s.SettledRequestIds);
            return economy;
        }
    }
}
