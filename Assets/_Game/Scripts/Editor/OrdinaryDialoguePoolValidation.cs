using System;
using System.IO;
using System.Linq;
using CallmeCatgirl.Gameplay;
using UnityEditor;
using UnityEngine;

namespace CallmeCatgirl.EditorTools
{
    public static class OrdinaryDialoguePoolValidation
    {
        // Optional real-Unity import check after merge. Does not write assets or enter Play mode.
        [MenuItem("Tools/CallmeCatgirl/Validate Ordinary Dialogue Pool")]
        public static void Validate()
        {
            var core = AssetDatabase.LoadAssetAtPath<GameplayConfiguration>("Assets/_Game/Data/TaskGrid/CoreGameplayConfiguration.asset");
            var pool = AssetDatabase.LoadAssetAtPath<StoryConfiguration>("Assets/_Game/Data/TaskGrid/OrdinaryDialoguePool.asset");
            Require(core != null && pool != null && core.OrdinaryDialoguePool == pool, "Ordinary pool reference is not connected.");
            var templates = core.Requests.Select(r => r.Id).ToArray(); pool.Validate(templates);
            var parsed = StoryJsonImporter.Parse(File.ReadAllText("Examples/OrdinaryDialogue/argon-four-scenes.json"), templates);
            try
            {
                Require(pool.Users.Length == 4 && parsed.Users.Length == 4, "Expected four pool entries.");
                int requests = 0, choices = 0;
                foreach (var actual in pool.Users)
                {
                    var expected = parsed.Users.Single(p => p.UserId == actual.UserId);
                    Require(actual.Kind == StoryConfiguration.ConversationKind.Ordinary
                        && actual.DisplayName == expected.DisplayName && Text(actual.InitialHistory) == Text(expected.InitialHistory)
                        && actual.Sessions.Length == expected.Sessions.Length, "Pool identity/type differs from reviewed JSON.");
                    for (int s = 0; s < actual.Sessions.Length; s++)
                    {
                        var a = actual.Sessions[s]; var e = expected.Sessions[s]; requests++;
                        Require(a.Id == e.Id && a.Title == e.Title && a.RequestId == e.RequestId && a.Rounds.Length == 1, "Request differs.");
                        Require(a.Task == null || (a.Task.Calculation == 0 && a.Task.Speed == 0 && a.Task.Patience == 0 && a.Task.Tokens == -1
                            && (a.Task.Abilities == null || a.Task.Abilities.Length == 0) && (a.Task.Shape == null || a.Task.Shape.Length == 0)), "Pool overrides task mechanics.");
                        var n = a.Rounds[0]; var en = e.Rounds[0];
                        Require(n.Id == en.Id && n.UserMessage == en.UserMessage && n.Choices.Length == 3, "Prompt differs.");
                        for (int c = 0; c < n.Choices.Length; c++)
                        {
                            var x = n.Choices[c]; var y = en.Choices[c]; choices++;
                            Require(x.Label == y.Label && x.PlayerReply == y.PlayerReply && x.UserReply == y.UserReply
                                && Text(x.NextNodeId) == Text(y.NextNodeId) && x.Requirement == y.Requirement
                                && x.MinimumFunds == 0 && x.FundsReward == 0, "Reply/content link differs.");
                        }
                    }
                }
                Require(requests == 10 && choices == 30, "Content counts differ.");
                Debug.Log("Ordinary dialogue pool import validation passed: 4 entries / 10 requests / 30 choices; task values inherited; no assets written.");
            }
            finally { UnityEngine.Object.DestroyImmediate(parsed); }
        }
        private static string Text(string value) => value ?? string.Empty;
        private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    }
}
