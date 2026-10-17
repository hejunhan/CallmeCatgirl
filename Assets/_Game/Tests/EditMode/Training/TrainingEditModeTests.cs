using NUnit.Framework;
namespace CallmeCatgirl.Gameplay.Training.Tests
{
    public sealed class TrainingEditModeTests
    {
        [OneTimeSetUp]
        public void ConfigureNativeCodec() => TrainingContractSuite.SerializationRoundTrip = state =>
            UnityEngine.JsonUtility.FromJson<TrainingSessionState>(UnityEngine.JsonUtility.ToJson(state));
        [TestCaseSource(nameof(CaseNames))]
        public void Contract(string name) => TrainingContractSuite.Cases()[name]();
        public static System.Collections.Generic.IEnumerable<string> CaseNames => TrainingContractSuite.Cases().Keys;
    }
}
