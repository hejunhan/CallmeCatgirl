namespace CallmeCatgirl.Persistence
{
    /// <summary>Pure gate for the once-per-absolute-game-day silent save.</summary>
    public static class DailyAutoSavePolicy
    {
        public static bool ShouldSave(int absoluteDay, int lastSavedDay, bool playing, bool gameEnded,
            bool modal, bool installPending, bool installOpen, bool trainingEnabled)
            => playing && absoluteDay != lastSavedDay && !gameEnded && !modal
                && !installPending && !installOpen && trainingEnabled;
    }
}
