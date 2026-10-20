using System;
using System.Collections.Generic;
using CallmeCatgirl.Gameplay.TaskGrid;

namespace CallmeCatgirl.Gameplay
{
    /// <summary>Single wallet for task income, upgrades and monthly maintenance.</summary>
    public sealed partial class EconomyManager
    {
        public int CurrentFunds { get; private set; }
        public int TokenPricePerUnit { get; }
        public int DebtAmount { get; private set; }
        public bool IsInGracePeriod { get; private set; }
        public bool IsBankrupt { get; private set; }
        public bool DebugIgnoreSpending { get; set; }
        public event Action<int> OnFundsChanged;
        public event Action<string> OnEconomyLog;
        private readonly HashSet<string> settledRequests = new HashSet<string>();

        public EconomyManager(int initialFunds, int tokenPricePerUnit)
        {
            if (initialFunds < 0 || tokenPricePerUnit < 0) throw new ArgumentOutOfRangeException(nameof(initialFunds));
            CurrentFunds = initialFunds; TokenPricePerUnit = tokenPricePerUnit;
        }
        public int Settle(TaskEvent item)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));
            if (IsBankrupt || (item.Kind != TaskEventKind.Submitted && item.Kind != TaskEventKind.TimedOut)
                || !settledRequests.Add(item.RequestId)) return 0;
            if (item.Kind == TaskEventKind.Submitted && (item.Result == null || item.Result.Overall == Quality.BelowStandard))
            { OnEconomyLog?.Invoke($"{item.RequestId}未达标提交，不计收入。"); return 0; }
            int income = checked((int)Math.Round(item.BillableTokens * TokenPricePerUnit, MidpointRounding.ToEven));
            AddFunds(income);
            OnEconomyLog?.Invoke($"{item.RequestId}结算收入 {income}，余额 {CurrentFunds}");
            return income;
        }
        public void AddFunds(int amount)
        {
            if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
            CurrentFunds = checked(CurrentFunds + amount);
            OnFundsChanged?.Invoke(CurrentFunds);
        }
        public bool TrySpendFunds(int amount)
        {
            if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
            if (DebugIgnoreSpending) return true;
            if (IsBankrupt || CurrentFunds < amount) return false;
            CurrentFunds -= amount; OnFundsChanged?.Invoke(CurrentFunds); return true;
        }
        public bool SettleMonth(int fee)
        {
            if (fee < 0) throw new ArgumentOutOfRangeException(nameof(fee));
            if (DebugIgnoreSpending)
            {
                DebtAmount = 0; IsInGracePeriod = false; IsBankrupt = false;
                OnEconomyLog?.Invoke("调试忽略月底扣费。"); return true;
            }
            if (IsBankrupt) return false;
            int required = checked(DebtAmount + fee);
            if (CurrentFunds >= required)
            {
                TrySpendFunds(required); DebtAmount = 0; IsInGracePeriod = false;
                OnEconomyLog?.Invoke($"月底扣费 {required}，余额 {CurrentFunds}"); return true;
            }
            if (IsInGracePeriod)
            { IsBankrupt = true; OnEconomyLog?.Invoke($"宽限期结束，仍需支付 {required}"); return false; }
            DebtAmount = fee - CurrentFunds;
            CurrentFunds = 0; IsInGracePeriod = true; OnFundsChanged?.Invoke(CurrentFunds);
            OnEconomyLog?.Invoke($"欠费 {DebtAmount}，进入一个月宽限期"); return true;
        }
    }
}
