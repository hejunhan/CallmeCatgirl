using System;
using System.Collections.Generic;
using UnityEngine;

namespace CallmeCatgirl.Gameplay
{
    // The asset fields are generated presentation caches. JSON owns the authored words.
    public static class StoryTextBindings
    {
        [Serializable] private sealed class Document
        {
            public int schemaVersion;
            public Binding[] bindings;
        }
        [Serializable] private sealed class Binding
        {
            public string assetName, userId, sessionId, nodeId, choiceKey, field, textId;
        }
        private sealed class Target
        {
            public Binding Owner;
            public Action<string> Set;
        }
        private static Dictionary<string, Dictionary<string, Binding>> cached;

        public static void ResetCache() => cached = null;

        public static bool IsFormalAsset(StoryConfiguration config)
            => config != null && (config.name == "StoryConfiguration" || config.name == "OrdinaryDialoguePool");

        public static void Refresh(StoryConfiguration config)
        {
            // Temporary import/test objects keep their existing public field API.
            if (!IsFormalAsset(config)) return;
            var targets = Targets(config);
            var mappings = Load()[config.name];
            var expected = new HashSet<string>(StringComparer.Ordinal);
            var values = new string[targets.Count];
            for (int i = 0; i < targets.Count; i++)
            {
                var owner = targets[i].Owner;
                string key = Key(owner);
                if (!expected.Add(key)) throw Error(owner, "duplicate object or field");
                if (!mappings.TryGetValue(key, out var binding)) throw Error(owner, "missing text binding");
                try { values[i] = StoryTextLibrary.Get(binding.textId); }
                catch (Exception e) { throw new InvalidOperationException(Error(binding, "text resolution failed").Message, e); }
                if (values[i] == null) throw Error(owner, "null text for " + binding.textId);
            }
            foreach (var binding in mappings.Values)
                if (!expected.Contains(Key(binding))) throw Error(binding, "binding has no matching asset field");
            // Resolve every ID before touching any field; failures cannot leave a half-updated cache.
            for (int i = 0; i < targets.Count; i++) targets[i].Set(values[i]);
        }

        private static Dictionary<string, Dictionary<string, Binding>> Load()
        {
            if (cached != null) return cached;
            var asset = Resources.Load<TextAsset>("NarrativeTextBindings");
            if (asset == null) throw new InvalidOperationException("NarrativeTextBindings.json is missing from Resources.");
            Document document;
            try { document = JsonUtility.FromJson<Document>(asset.text.TrimStart('\uFEFF')); }
            catch (Exception e) { throw new InvalidOperationException("NarrativeTextBindings.json has invalid JSON: " + e.Message, e); }
            if (document == null || document.schemaVersion != 1 || document.bindings == null)
                throw new InvalidOperationException("NarrativeTextBindings.json requires schemaVersion 1 and bindings.");
            var result = new Dictionary<string, Dictionary<string, Binding>>(StringComparer.Ordinal)
            {
                ["StoryConfiguration"] = new Dictionary<string, Binding>(StringComparer.Ordinal),
                ["OrdinaryDialoguePool"] = new Dictionary<string, Binding>(StringComparer.Ordinal)
            };
            foreach (var binding in document.bindings)
            {
                if (binding == null) throw new InvalidOperationException("NarrativeTextBindings.json contains a null binding.");
                if (string.IsNullOrWhiteSpace(binding.assetName) || !result.TryGetValue(binding.assetName, out var entries))
                    throw Error(binding, "unknown asset name");
                if (string.IsNullOrWhiteSpace(binding.field) || string.IsNullOrWhiteSpace(binding.textId))
                    throw Error(binding, "field and textId are required");
                string key = Key(binding);
                if (entries.ContainsKey(key)) throw Error(binding, "duplicate text binding");
                entries.Add(key, binding);
            }
            cached = result;
            return cached;
        }

        private static string Key(Binding binding)
        {
            var parts = new[] { binding.assetName, binding.userId, binding.sessionId, binding.nodeId, binding.choiceKey, binding.field };
            foreach (var part in parts)
                if (part != null && part.Contains("\u001f")) throw Error(binding, "invalid control character in owner ID");
            return string.Join("\u001f", parts);
        }

        private static InvalidOperationException Error(Binding b, string problem)
            => new InvalidOperationException("NarrativeTextBindings.json: " + problem + " [asset=" + b.assetName
                + ", user=" + b.userId + ", session=" + b.sessionId + ", node=" + b.nodeId
                + ", choice=" + b.choiceKey + ", field=" + b.field + ", textId=" + b.textId + "].");

        private static List<Target> Targets(StoryConfiguration config)
        {
            var result = new List<Target>();
            void Add(string user, string session, string node, string choice, string field, Action<string> set)
                => result.Add(new Target { Owner = new Binding { assetName = config.name, userId = user, sessionId = session,
                    nodeId = node, choiceKey = choice, field = field }, Set = set });
            void AddChoice(StoryConfiguration.ReplyChoice choice, string user, string session, string node, string key)
            {
                if (choice == null) throw new InvalidOperationException("Null reply choice in formal story resource " + config.name + ".");
                Add(user, session, node, key, "Label", value => choice.Label = value);
                Add(user, session, node, key, "PlayerReply", value => choice.PlayerReply = value);
                Add(user, session, node, key, "UserReply", value => choice.UserReply = value);
            }
            foreach (var user in config.Users ?? Array.Empty<StoryConfiguration.UserStory>())
            {
                if (user == null) throw new InvalidOperationException("Null user in formal story resource " + config.name + ".");
                Add(user.UserId, null, null, null, "DisplayName", value => user.DisplayName = value);
                Add(user.UserId, null, null, null, "InitialHistory", value => user.InitialHistory = value);
                foreach (var session in user.Sessions ?? Array.Empty<StoryConfiguration.ConversationSession>())
                {
                    if (session == null) throw new InvalidOperationException("Null session in formal story resource " + config.name + ".");
                    Add(user.UserId, session.Id, null, null, "Title", value => session.Title = value);
                    foreach (var node in session.Rounds ?? Array.Empty<StoryConfiguration.StoryNode>())
                    {
                        if (node == null) throw new InvalidOperationException("Null node in formal story resource " + config.name + ".");
                        Add(user.UserId, session.Id, node.Id, null, "UserMessage", value => node.UserMessage = value);
                        var choices = node.Choices ?? Array.Empty<StoryConfiguration.ReplyChoice>();
                        for (int i = 0; i < choices.Length; i++) AddChoice(choices[i], user.UserId, session.Id, node.Id, "choice-" + i);
                    }
                }
            }
            var defaults = config.DefaultChoices ?? Array.Empty<StoryConfiguration.ReplyChoice>();
            for (int i = 0; i < defaults.Length; i++) AddChoice(defaults[i], null, null, null, "default-" + i);
            return result;
        }
    }
}
