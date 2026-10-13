using System;
using CallmeCatgirl.Gameplay.TaskGrid;

namespace CallmeCatgirl.Gameplay
{
    /// <summary>
    /// 全局唯一资金管理器，负责所有订单结算、扩容扣费、资金统计
    /// </summary>
    public sealed class EconomyManager
    {
        public int CurrentFunds { get; private set; }
        public int TokenPricePerUnit { get; }
        public int MemoryUpgradeCost { get; }

        public event Action<int> OnFundsChanged;
        public event Action<string> OnEconomyLog;

        public EconomyManager(int initialFunds, int tokenPricePerUnit, int memoryUpgradeCost)
        {
            if (initialFunds < 0 || tokenPricePerUnit < 0 || memoryUpgradeCost < 0)
                throw new ArgumentOutOfRangeException(nameof(initialFunds), "经济配置数值不能为负数");

            CurrentFunds = initialFunds;
            TokenPricePerUnit = tokenPricePerUnit;
            MemoryUpgradeCost = memoryUpgradeCost;

            OnEconomyLog?.Invoke($"初始化资金：{CurrentFunds}");
        }

        /// <summary>
        /// 结算成功完成的订单，按全额Token结算
        /// </summary>
        public void SettleCompletedTask(TaskInstance task)
        {
            if (task == null) throw new ArgumentNullException(nameof(task));
            if (task.State != RequestState.Submitted)
                throw new InvalidOperationException("仅已提交的任务可以结算");

            int income = (int)Math.Floor(task.Definition.Tokens * TokenPricePerUnit);
            AddFunds(income);
            OnEconomyLog?.Invoke($"任务{task.Id}完成，收入{income}，当前余额{CurrentFunds}");
        }

        /// <summary>
        /// 结算超时任务，按进度比例结算
        /// </summary>
        public void SettleTimedOutTask(TaskInstance task)
        {
            if (task == null) throw new ArgumentNullException(nameof(task));
            if (task.State != RequestState.TimedOut)
                throw new InvalidOperationException("仅超时任务可以按失败结算");

            double progressRatio = task.Progress / task.Definition.Calculation;
            int earnedTokens = (int)Math.Floor(task.Definition.Tokens * progressRatio);
            int income = earnedTokens * TokenPricePerUnit;

            AddFunds(income);
            OnEconomyLog?.Invoke($"任务{task.Id}超时，按进度收入{income}，当前余额{CurrentFunds}");
        }

        /// <summary>
        /// 尝试内存扩容，资金足够则扣费并返回成功
        /// </summary>
        public bool TryPurchaseMemoryUpgrade()
        {
            if (CurrentFunds < MemoryUpgradeCost)
            {
                OnEconomyLog?.Invoke($"扩容失败，需要{MemoryUpgradeCost}，当前余额{CurrentFunds}");
                return false;
            }

            SpendFunds(MemoryUpgradeCost);
            OnEconomyLog?.Invoke($"内存扩容成功，支出{MemoryUpgradeCost}，当前余额{CurrentFunds}");
            return true;
        }

        private void AddFunds(int amount)
        {
            if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
            CurrentFunds += amount;
            OnFundsChanged?.Invoke(CurrentFunds);
        }

        private void SpendFunds(int amount)
        {
            if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
            if (CurrentFunds < amount) throw new InvalidOperationException("余额不足，无法扣款");

            CurrentFunds -= amount;
            OnFundsChanged?.Invoke(CurrentFunds);
        }
    }
}
