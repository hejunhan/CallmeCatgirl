using System;
using System.Collections.Generic;
using System.Linq;
using CallmeCatgirl.Gameplay.Narrative;
using UnityEngine;

namespace CallmeCatgirl.Gameplay
{
    /// <summary>Author JSON is authoritative. ScriptableObject text fields are generated caches.</summary>
    public static class StoryTextLibrary
    {
        public const int SchemaVersion = 1;
        public const string ResourcePath = "NarrativeText";
        private static Dictionary<string, string> entries;
        public static int Count { get { EnsureLoaded(); return entries.Count; } }

        [Serializable] public sealed class Entry { public string id; public string text; }
        [Serializable] public sealed class Document { public int schemaVersion; public Entry[] entries; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForPlay()
        {
            entries = null;
            NarrativeTextCatalog.Bind(Get);
            StoryTextBindings.ResetCache();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        public static void Initialize()
        {
            NarrativeTextCatalog.Bind(Get);
            EnsureLoaded();
        }

        public static string Get(string id)
        {
            EnsureLoaded();
            if (string.IsNullOrWhiteSpace(id) || !entries.TryGetValue(id, out var text))
                throw new InvalidOperationException("Missing narrative JSON text ID: " + (id ?? "<null>"));
            return text;
        }

        public static void Reload()
        {
            // Publish only a complete, validated catalog; a failed reload never installs partial data.
            entries = null;
            var loaded = Load(Resources.LoadAll<TextAsset>(ResourcePath));
            entries = loaded;
            NarrativeTextCatalog.Bind(Get);
            StoryTextBindings.ResetCache();
        }

        public static Dictionary<string, string> ValidateDocuments(IEnumerable<KeyValuePair<string, string>> documents)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var source in documents.OrderBy(d => d.Key, StringComparer.Ordinal))
            {
                Document document;
                try { document = JsonUtility.FromJson<Document>(source.Value.TrimStart('\uFEFF')); }
                catch (Exception error) { throw new InvalidOperationException("Invalid narrative JSON in " + source.Key + ": " + error.Message, error); }
                if (document == null || document.schemaVersion != SchemaVersion || document.entries == null || document.entries.Length == 0)
                    throw new InvalidOperationException("Invalid narrative JSON schema/entries in " + source.Key + "; expected schemaVersion 1 and nonempty entries.");
                foreach (var entry in document.entries)
                {
                    if (entry == null || string.IsNullOrWhiteSpace(entry.id) || entry.text == null)
                        throw new InvalidOperationException("Invalid narrative JSON entry in " + source.Key + ": id and text are required (empty text is allowed).");
                    if (!result.TryAdd(entry.id, entry.text))
                        throw new InvalidOperationException("Duplicate narrative JSON text ID " + entry.id + " in " + source.Key);
                }
            }
            if (result.Count == 0) throw new InvalidOperationException("No narrative JSON text assets found at Resources/" + ResourcePath);
            return result;
        }

        private static Dictionary<string, string> Load(IEnumerable<TextAsset> documents)
            => ValidateDocuments(documents.Select(d => new KeyValuePair<string, string>(d.name + ".json", d.text)));

        private static void EnsureLoaded()
        {
            if (entries == null) entries = Load(Resources.LoadAll<TextAsset>(ResourcePath));
            NarrativeTextCatalog.Bind(Get);
        }
    }
}
