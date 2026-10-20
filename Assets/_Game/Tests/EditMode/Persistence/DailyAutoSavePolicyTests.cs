using CallmeCatgirl.Persistence;
using NUnit.Framework;

namespace CallmeCatgirl.Tests.Persistence
{
    public sealed class DailyAutoSavePolicyTests
    {
        [Test]
        public void SavesOncePerAbsoluteDay()
        {
            Assert.That(DailyAutoSavePolicy.ShouldSave(3, 2, true, false, false, false, false, true), Is.True);
            Assert.That(DailyAutoSavePolicy.ShouldSave(3, 3, true, false, false, false, false, true), Is.False,
                "The same absolute day must not save twice.");
            Assert.That(DailyAutoSavePolicy.ShouldSave(31, 30, true, false, false, false, false, true), Is.True,
                "The last day of a month still saves.");
            Assert.That(DailyAutoSavePolicy.ShouldSave(32, 31, true, false, false, false, false, true), Is.True,
                "The first day of the next month is a new absolute day.");
        }

        [TestCase(false, false, false, false, false, true, false)]
        [TestCase(true, true, false, false, false, true, false)]
        [TestCase(true, false, true, false, false, true, false)]
        [TestCase(true, false, false, true, false, true, false)]
        [TestCase(true, false, false, false, true, true, false)]
        [TestCase(true, false, false, false, false, false, false)]
        [TestCase(true, false, false, false, false, true, true)]
        public void SkipsWhenTheStateMustNotBecomeASave(bool playing, bool ended, bool modal, bool pending, bool open, bool training, bool expected)
            => Assert.That(DailyAutoSavePolicy.ShouldSave(5, 4, playing, ended, modal, pending, open, training), Is.EqualTo(expected));
    }
}
