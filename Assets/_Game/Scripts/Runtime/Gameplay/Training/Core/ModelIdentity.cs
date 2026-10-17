using System;
using System.Collections.Generic;
using System.Linq;
using CallmeCatgirl.Gameplay.TaskGrid;

namespace CallmeCatgirl.Gameplay.Training
{
    [Serializable] public sealed class ModelIdentityEntry
    {
        public string LegacyId;
        public string ModelId;
    }
    [Serializable] public sealed class ModelIdentityState
    {
        public int Version = 1;
        public ModelIdentityEntry[] Entries = Array.Empty<ModelIdentityEntry>();
    }

    /// <summary>Persist this migration map with the global save before discarding old IDs. Not a second wallet or save service.</summary>
    public sealed class ModelIdentityRegistry
    {
        private readonly Dictionary<string, string> aliases = new Dictionary<string, string>(StringComparer.Ordinal);
        public static string Canonical(string value)
        {
            if (!Guid.TryParse(value, out var uuid) || uuid == Guid.Empty) throw new ArgumentException("Nonempty model UUID required.");
            return uuid.ToString("D");
        }
        public string ResolveOrCreate(string oldOrUuid)
        {
            Check.Id(oldOrUuid);
            if (Guid.TryParse(oldOrUuid, out var uuid) && uuid != Guid.Empty) return uuid.ToString("D");
            if (aliases.TryGetValue(oldOrUuid, out var known)) return known;
            string created;
            do { created = Guid.NewGuid().ToString("D"); } while (aliases.ContainsValue(created));
            aliases.Add(oldOrUuid, created); return created;
        }
        public bool Register(string legacyId, string modelId)
        {
            string canonical;
            try { canonical = Canonical(modelId); } catch (ArgumentException) { return false; }
            if (string.IsNullOrWhiteSpace(legacyId)) return true;
            if (aliases.TryGetValue(legacyId, out var prior)) return prior == canonical;
            if (aliases.Values.Contains(canonical)) return false; // Never merge two legacy instances accidentally.
            aliases.Add(legacyId, canonical); return true;
        }
        public string ResolveKnown(string id)
        {
            if (id == null) return null;
            if (Guid.TryParse(id, out var uuid) && uuid != Guid.Empty) return uuid.ToString("D");
            return aliases.TryGetValue(id, out var known) ? known : null;
        }
        public ModelIdentityState Capture() => new ModelIdentityState {
            Entries = aliases.OrderBy(p => p.Key, StringComparer.Ordinal)
                .Select(p => new ModelIdentityEntry { LegacyId = p.Key, ModelId = p.Value }).ToArray()
        };
        public bool TryRestore(ModelIdentityState state)
        {
            if (state == null || state.Version != 1 || state.Entries == null) return false;
            var next = new Dictionary<string,string>(StringComparer.Ordinal);
            var used = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in state.Entries)
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.LegacyId)) return false;
                string id;
                try { id = Canonical(entry.ModelId); } catch (ArgumentException) { return false; }
                if (next.ContainsKey(entry.LegacyId) || !used.Add(id)) return false;
                next.Add(entry.LegacyId, id);
            }
            aliases.Clear(); foreach (var pair in next) aliases.Add(pair.Key, pair.Value);
            return true;
        }
    }

    [Serializable] public sealed class ModelAbilityState
    {
        public AbilityKind Kind;
        public double Points;
    }
    [Serializable] public sealed class CatalogModelState
    {
        public int Version = 1;
        public string ModelId;
        public string LegacyId;
        public string DisplayName;
        public string ArchitectureId;
        public double AbilityInterval;
        public ModelAbilityState[] Abilities = Array.Empty<ModelAbilityState>();
        public string[] CorpusIds = Array.Empty<string>();
    }

    public static class ModelPersistence
    {
        public static CatalogModelState Capture(CatalogModel model) => new CatalogModelState {
            ModelId = model.Id, LegacyId = model.LegacyId, DisplayName = model.DisplayName,
            ArchitectureId = model.ArchitectureId, AbilityInterval = model.Definition.Interval,
            Abilities = model.Definition.Points.Select(p => new ModelAbilityState { Kind = p.Key, Points = p.Value }).ToArray(),
            CorpusIds = model.CorpusIds.ToArray()
        };
        /// <summary>Loads a model DTO without gameplay effects or new UUID generation for already migrated saves.
        /// For old non-UUID ModelId, provide the persisted identity registry; persist both the upgraded DTO and registry.</summary>
        public static bool TryRestore(CatalogModelState state, ModelIdentityRegistry identities, out CatalogModel model)
        {
            model = null;
            if (state == null || state.Version != 1 || identities == null || state.Abilities == null || state.CorpusIds == null
                || state.Abilities.Any(p => p == null))
                return false;
            try
            {
                // Validate all ordinary fields before registering a migration.
                var points = state.Abilities.ToDictionary(p => p.Kind, p => p.Points);
                var definition = new ModelDefinition(Check.Id(state.ModelId), state.AbilityInterval, points);
                if (!ModelNames.TryNormalize(state.DisplayName, out _) || string.IsNullOrWhiteSpace(state.ArchitectureId)
                    || state.CorpusIds.Any(string.IsNullOrWhiteSpace) || points.Keys.Any(k => !Enum.IsDefined(typeof(AbilityKind), k))) return false;
                string uuid = identities.ResolveOrCreate(state.ModelId);
                string legacy = Guid.TryParse(state.ModelId, out _) ? state.LegacyId : state.ModelId;
                if (!identities.Register(legacy, uuid)) return false;
                model = new CatalogModel(state.DisplayName, state.ArchitectureId, definition, state.CorpusIds, uuid, legacy);
                return true;
            }
            catch (ArgumentException) { return false; }
        }
    }
}
