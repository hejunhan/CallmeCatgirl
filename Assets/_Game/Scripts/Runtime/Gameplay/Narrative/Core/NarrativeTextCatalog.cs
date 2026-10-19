using System;

namespace CallmeCatgirl.Gameplay.Narrative
{
    /// <summary>The engine host supplies the same imported JSON catalog in Editor and Player.</summary>
    public static class NarrativeTextCatalog
    {
        private static Func<string, string> resolve;

        public static void Bind(Func<string, string> textResolver)
            => resolve = textResolver ?? throw new ArgumentNullException(nameof(textResolver));

        public static string Get(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Narrative text ID is empty.", nameof(id));
            if (resolve == null) throw new InvalidOperationException("Narrative JSON text resolver is not installed. Missing text ID: " + id);
            return resolve(id) ?? throw new InvalidOperationException("Narrative JSON text is null: " + id);
        }
    }
}
