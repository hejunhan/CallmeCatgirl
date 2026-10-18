using System;
using NUnit.Framework;
using CallmeCatgirl.Persistence;

namespace CallmeCatgirl.Tests
{
    public sealed class RunEndingStateTests
    {
        [TestCase(1)]
        [TestCase(2)]
        public void LegacyBankruptcyKeepsItsMeaningWithoutNewFields(int version)
        {
            var host = new HostState { Version = version, GameEnded = true, EndingText = "服务器停机" };
            var wallet = new EconomyState { Bankrupt = true };
            Assert.DoesNotThrow(() => RunSaveValidation.ValidateEndState(host, wallet, true));
            Assert.That(RunSaveValidation.ResolveEndReason(host), Is.EqualTo(RunEndReasons.Bankruptcy));
        }

        [TestCase(RunEndReasons.PolicyBad)]
        [TestCase(RunEndReasons.PolicyNormal)]
        [TestCase(RunEndReasons.PolicyGood)]
        [TestCase(RunEndReasons.Emotion)]
        public void AuthoredEndingDoesNotRequireBankruptcy(string reason)
        {
            var host = new HostState { Version = 3, GameEnded = true, EndReason = reason, EndingText = "落幕" };
            var wallet = new EconomyState { Bankrupt = false, Funds = 300 };
            Assert.DoesNotThrow(() => RunSaveValidation.ValidateEndState(host, wallet, true));
            Assert.That(wallet.Funds, Is.EqualTo(300));
            Assert.That(wallet.Bankrupt, Is.False);
        }

        [Test]
        public void UnknownOrContradictoryEndStateIsRejected()
        {
            var host = new HostState { Version = 3, GameEnded = true, EndReason = "unknown", EndingText = "落幕" };
            var wallet = new EconomyState();
            Assert.Throws<ArgumentException>(() => RunSaveValidation.ValidateEndState(host, wallet, true));
            host.EndReason = RunEndReasons.PolicyGood; wallet.Bankrupt = true;
            Assert.Throws<ArgumentException>(() => RunSaveValidation.ValidateEndState(host, wallet, true));
            host.EndReason = RunEndReasons.Bankruptcy;
            Assert.Throws<ArgumentException>(() => RunSaveValidation.ValidateEndState(host, wallet, false));
            host.GameEnded = false;
            Assert.Throws<ArgumentException>(() => RunSaveValidation.ValidateEndState(host, wallet, true));
        }
    }
}
