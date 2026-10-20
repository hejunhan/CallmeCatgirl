using System;
using System.Collections.Generic;
using UnityEngine;

namespace CallmeCatgirl.UI.EndingGalgame
{
    /// <summary>Loads and structurally validates the authored ending script graph. Content stays in JSON.</summary>
    public static class EndingScriptLibrary
    {
        public const int SchemaVersion = 1;
        public const string ResourcePath = "EndingGalgame/ending-scripts";
        private const string SpecialPrefix = "special.";

        public static EndingScriptDocument LoadFromResources()
            => Load(Resources.Load<TextAsset>(ResourcePath));

        public static EndingScriptDocument Load(TextAsset asset)
        {
            if (asset == null)
                throw new InvalidOperationException("Missing ending script at Resources/" + ResourcePath + ".json");
            return Parse(asset.text);
        }

        public static EndingScriptDocument Parse(string json)
        {
            EndingScriptDocument document;
            try { document = JsonUtility.FromJson<EndingScriptDocument>((json ?? string.Empty).TrimStart('\uFEFF')); }
            catch (Exception error) { throw new InvalidOperationException("Invalid ending script JSON: " + error.Message, error); }
            Validate(document);
            return document;
        }

        /// <summary>Throws on the first document-level structural defect list; every message names the scene or choice.</summary>
        public static void Validate(EndingScriptDocument document)
        {
            var issues = new List<string>();
            if (document == null) throw new InvalidOperationException("Ending script JSON produced no document.");
            if (document.schemaVersion != SchemaVersion)
                issues.Add("schemaVersion must be " + SchemaVersion + " but was " + document.schemaVersion + ".");
            if (document.endings == null || document.endings.Length == 0)
                issues.Add("endings must be nonempty.");
            else
            {
                var endingIds = new HashSet<string>(StringComparer.Ordinal);
                foreach (var ending in document.endings)
                    ValidateEnding(ending, endingIds, issues);
            }
            if (issues.Count > 0) throw new InvalidOperationException("Invalid ending script: " + string.Join(" ", issues));
        }

        public static EndingScript FindEnding(EndingScriptDocument document, string endingId)
        {
            if (!TryFindEnding(document, endingId, out var ending))
                throw new InvalidOperationException("Unknown ending ID: " + (endingId ?? "<null>"));
            return ending;
        }

        /// <summary>Accepts the ending ID, the trigger node ID, and either form with the optional "special." prefix.</summary>
        public static bool TryFindEnding(EndingScriptDocument document, string endingId, out EndingScript ending)
        {
            ending = null;
            if (document?.endings == null || string.IsNullOrWhiteSpace(endingId)) return false;
            string normalized = NormalizeNodeId(endingId.Trim());
            foreach (var candidate in document.endings)
            {
                if (candidate == null) continue;
                if (string.Equals(candidate.id, normalized, StringComparison.Ordinal)
                    || string.Equals(NormalizeNodeId(candidate.triggerNodeId), normalized, StringComparison.Ordinal))
                {
                    ending = candidate;
                    return true;
                }
            }
            return false;
        }

        public static string NormalizeNodeId(string nodeId)
            => nodeId != null && nodeId.StartsWith(SpecialPrefix, StringComparison.Ordinal)
                ? nodeId.Substring(SpecialPrefix.Length) : nodeId;

        /// <summary>Missing text falls back to its ID and is reported instead of aborting an ending.</summary>
        public static string ResolveText(Func<string, string> resolver, string textId, Action<string> onMissing)
        {
            if (string.IsNullOrEmpty(textId)) return string.Empty;
            string text = null;
            try { text = resolver?.Invoke(textId); }
            catch (Exception) { text = null; }
            if (text != null) return text;
            onMissing?.Invoke(textId);
            return textId;
        }

        private static void ValidateEnding(EndingScript ending, HashSet<string> endingIds, List<string> issues)
        {
            if (ending == null) { issues.Add("endings contains null."); return; }
            if (string.IsNullOrWhiteSpace(ending.id)) { issues.Add("An ending is missing its id."); return; }
            string where = "Ending " + ending.id + ":";
            if (!endingIds.Add(ending.id)) issues.Add(where + " duplicate id.");
            if (string.IsNullOrWhiteSpace(ending.triggerNodeId)) issues.Add(where + " triggerNodeId is required.");
            else if (!EndingRunReasons.TryResolve(ending.triggerNodeId, out _))
                issues.Add(where + " unsupported triggerNodeId " + ending.triggerNodeId + ".");
            if (string.IsNullOrWhiteSpace(ending.startSceneId)) issues.Add(where + " startSceneId is required.");
            if (ending.scenes == null || ending.scenes.Length == 0) { issues.Add(where + " scenes must be nonempty."); return; }

            var scenes = new Dictionary<string, EndingScene>(StringComparer.Ordinal);
            foreach (var scene in ending.scenes)
            {
                if (scene == null || string.IsNullOrWhiteSpace(scene.id)) { issues.Add(where + " a scene is missing its id."); continue; }
                if (!scenes.TryAdd(scene.id, scene)) issues.Add(where + " duplicate scene " + scene.id + ".");
            }
            foreach (var scene in scenes.Values) ValidateScene(ending, scene, scenes, issues);
            ValidateReachability(ending, scenes, issues);
        }

        private static void ValidateScene(EndingScript ending, EndingScene scene, Dictionary<string, EndingScene> scenes, List<string> issues)
        {
            string where = "Ending " + ending.id + " scene " + scene.id + ":";
            if (scene.lines == null || scene.lines.Length == 0) issues.Add(where + " lines must be nonempty.");
            else foreach (var line in scene.lines)
                if (line == null || string.IsNullOrWhiteSpace(line.textTextId)) issues.Add(where + " a line is missing textTextId.");

            if (scene.choices == null || scene.choices.Length == 0) { issues.Add(where + " choices must be nonempty."); return; }
            var choiceIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var choice in scene.choices)
            {
                if (choice == null || string.IsNullOrWhiteSpace(choice.id)) { issues.Add(where + " a choice is missing its id."); continue; }
                if (!choiceIds.Add(choice.id)) issues.Add(where + " duplicate choice " + choice.id + ".");
                if (string.IsNullOrWhiteSpace(choice.labelTextId)) issues.Add(where + " choice " + choice.id + " is missing labelTextId.");
                if (!string.IsNullOrEmpty(choice.nextSceneId) && !scenes.ContainsKey(choice.nextSceneId))
                    issues.Add(where + " choice " + choice.id + " points to unknown scene " + choice.nextSceneId + ".");
            }
        }

        private static void ValidateReachability(EndingScript ending, Dictionary<string, EndingScene> scenes, List<string> issues)
        {
            if (!scenes.ContainsKey(ending.startSceneId))
            {
                if (!string.IsNullOrWhiteSpace(ending.startSceneId))
                    issues.Add("Ending " + ending.id + " startSceneId " + ending.startSceneId + " is not one of its scenes.");
                return;
            }
            var reached = new HashSet<string>(StringComparer.Ordinal) { ending.startSceneId };
            var pending = new Stack<string>();
            pending.Push(ending.startSceneId);
            while (pending.Count > 0)
            {
                var choices = scenes[pending.Pop()].choices;
                if (choices == null) continue;
                foreach (var choice in choices)
                    if (!string.IsNullOrEmpty(choice.nextSceneId) && scenes.ContainsKey(choice.nextSceneId) && reached.Add(choice.nextSceneId))
                        pending.Push(choice.nextSceneId);
            }
            foreach (var id in scenes.Keys)
            {
                if (!reached.Contains(id)) { issues.Add("Ending " + ending.id + " scene " + id + " is unreachable from " + ending.startSceneId + "."); continue; }
                if (!CanTerminate(id, scenes, new HashSet<string>(StringComparer.Ordinal), new Dictionary<string, bool>(StringComparer.Ordinal)))
                    issues.Add("Ending " + ending.id + " scene " + id + " cannot reach an empty nextSceneId.");
            }
        }

        private static bool CanTerminate(string sceneId, Dictionary<string, EndingScene> scenes, HashSet<string> visiting, Dictionary<string, bool> memo)
        {
            if (!scenes.ContainsKey(sceneId)) return false;
            if (memo.TryGetValue(sceneId, out bool known)) return known;
            if (!visiting.Add(sceneId)) return false;
            bool terminates = false;
            var choices = scenes[sceneId].choices;
            if (choices != null)
                foreach (var choice in choices)
                    if (string.IsNullOrEmpty(choice.nextSceneId) || CanTerminate(choice.nextSceneId, scenes, visiting, memo))
                    { terminates = true; break; }
            visiting.Remove(sceneId);
            memo[sceneId] = terminates;
            return terminates;
        }
    }
}
