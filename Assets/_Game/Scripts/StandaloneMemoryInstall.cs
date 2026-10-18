namespace CallmeCatgirl.UI
{
    /// <summary>The same interaction can also run directly in MemoryInstallScene.</summary>
    public sealed class StandaloneMemoryInstall : MemoryInstallGame
    {
        [UnityEngine.SerializeField] private bool autoStart = true;
        private void Start() { if (autoStart) OpenGame(); }
    }
}
