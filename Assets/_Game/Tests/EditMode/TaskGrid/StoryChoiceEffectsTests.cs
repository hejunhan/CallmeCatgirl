using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CallmeCatgirl.Gameplay.TaskGrid;
using NUnit.Framework;
using UnityEngine;

namespace CallmeCatgirl.Tests
{
    public sealed class StoryChoiceEffectsTests
    {
        private static Type Find(string name, string assembly) => Type.GetType(name + ", " + assembly)
            ?? typeof(StoryChoiceEffectsTests).Assembly.GetType(name, true);
        private static object Call(object o, string method, params object[] args)
            => o.GetType().GetMethod(method, args.Select(a => a.GetType()).ToArray()).Invoke(o, args);
        private static T Get<T>(object o, string name) => (T)o.GetType().GetProperty(name).GetValue(o);
        private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
        private sealed class Fixture : IDisposable
        {
            public readonly object Story, Wallet;
            public readonly ScriptableObject Config;
            public readonly TaskSimulation Sim;
            public readonly TaskDefinition Definition = new TaskDefinition("A", "test", new[] { new Cell(0, 0) }, 1, 1, 100, 1,
                new[] { new AbilityRequirement(AbilityKind.Writing, 0, 1) });
            public Fixture(int funds = 10)
            {
                var importer = Find("CallmeCatgirl.EditorTools.StoryJsonImporter", "Assembly-CSharp-Editor");
                string json = File.ReadAllText(Path.Combine(Application.dataPath, "../Examples/StoryImport/placeholder-effects.example.json"));
                Config = (ScriptableObject)importer.GetMethod("Parse").Invoke(null, new object[] { json, new[] { "A", "B" } });
                // This fixture verifies deterministic scripted rewards/branching, not ordinary lifecycle.
                foreach (var profile in (Array)Config.GetType().GetField("Users").GetValue(Config))
                {
                    var kind = profile.GetType().GetField("Kind");
                    kind.SetValue(profile, Enum.Parse(kind.FieldType, "Scripted"));
                }
                Story = Activator.CreateInstance(Find("CallmeCatgirl.Gameplay.ConversationStory", "Assembly-CSharp"), new object[] { Config });
                Wallet = Activator.CreateInstance(Find("CallmeCatgirl.Gameplay.EconomyManager", "Assembly-CSharp"), funds, 1);
                Story.GetType().GetProperty("Economy").SetValue(Story, Wallet);
                Sim = new TaskSimulation(2, 1, new[] { new Cell(0, 0), new Cell(1, 0) },
                    new ModelDefinition("test", 1, new Dictionary<AbilityKind, double> { { AbilityKind.Writing, 1 } }));
                Sim.Paused = true;
            }
            public TaskInstance Submit(string user, string id)
            {
                var templates = new Dictionary<string, TaskDefinition> { { "A", Definition }, { "B", Definition } };
                var definition = (TaskDefinition)Story.GetType().GetMethod("BuildNextTask").Invoke(Story, new object[] { user, templates, Definition });
                Check(definition.Title.Contains("占位验证"), "configured session task selected");
                Check(Sim.TryCreate(id, user, id, definition, out var task), "create"); Call(Story, "Arrived", task);
                Check(Sim.TryPlace(id, new Cell(0, 0), 0, out _), "place");
                Sim.Paused = false; Sim.Advance(1); Sim.Paused = true;
                Check(Sim.TrySubmit(id), "submit while paused"); Call(Story, "Submitted", task); return task;
            }
            public void Dispose() => UnityEngine.Object.DestroyImmediate(Config);
        }
        [Test] public void TwoContactsCompleteTwoTasksAndRewardSharedWalletWhilePaused()
        {
            using (var f = new Fixture())
            {
                foreach (string user in new[] { "A", "B" })
                    for (int round = 1; round <= 2; round++)
                    {
                        var task = f.Submit(user, user + round); int revision = Get<int>(f.Story, "Revision"); double time = f.Sim.GameTime;
                        Check((bool)Call(f.Story, "ChooseAtRevision", 1, revision), "reward reply");
                        Check(!(bool)Call(f.Story, "ChooseAtRevision", 1, revision), "duplicate callback rejected");
                        Check(f.Sim.Paused && f.Sim.GameTime == time, "reply preserves pause and time");
                        Check(f.Sim.TryCloseFeedback(task.Id), "close feedback in existing simulation");
                        Check((bool)Call(f.Story, "Finished", user) == (round == 2), "next session requires next task");
                    }
                Check(Get<int>(f.Wallet, "CurrentFunds") == 30, "four rewards exactly once");
                Check(((string)Call(f.Story, "History", "A")).Contains("折页"), "contact A text");
                Check(!((string)Call(f.Story, "History", "B")).Contains("折页"), "isolated contact history");
            }
        }
        [Test] public void DuplicateEventsAndStaleCallbacksCannotRewardQueuedContact()
        {
            using (var f = new Fixture())
            {
                var a = f.Submit("A", "A1"); string history = (string)Call(f.Story, "History", "A");
                Call(f.Story, "Arrived", a); Call(f.Story, "Submitted", a);
                Check((string)Call(f.Story, "History", "A") == history, "duplicate events ignored");
                f.Submit("B", "B1"); int revision = Get<int>(f.Story, "Revision");
                Check((bool)Call(f.Story, "ChooseAtRevision", 1, revision), "first reward");
                Check(Get<string>(f.Story, "UserId") == "B", "queued contact opens");
                Check(!(bool)Call(f.Story, "ChooseAtRevision", 1, revision), "old callback rejected");
                Check(Get<int>(f.Wallet, "CurrentFunds") == 15, "no queued reward");
            }
        }
        [Test] public void MutableBalanceConditionAndOverflowAreCheckedBeforeAdvancing()
        {
            using (var f = new Fixture())
            {
                f.Submit("A", "A1"); var choice = ((IList)Get<object>(f.Story, "Choices"))[1];
                choice.GetType().GetField("MinimumFunds").SetValue(choice, 10); Call(f.Wallet, "TrySpendFunds", 1);
                int revision = Get<int>(f.Story, "Revision");
                Check(!(bool)Call(f.Story, "ChooseAtRevision", 1, revision), "balance condition rechecked");
                Check(Get<int>(f.Story, "Revision") == revision && Get<bool>(f.Story, "IsReply"), "rejection leaves story unchanged");
                Check((bool)Call(f.Story, "ChooseAtRevision", 0, revision), "ordinary alternative usable");
            }
            using (var f = new Fixture(int.MaxValue))
            {
                f.Submit("A", "A1"); Check(((IList)Get<object>(f.Story, "Choices")).Count == 1, "overflow reward hidden");
                Check((bool)Call(f.Story, "Choose", 0), "ordinary reply usable");
            }
        }
        [Test] public void PrematureSubmissionDoesNotConsumeValidSubmission()
        {
            using (var f = new Fixture())
            {
                Check(f.Sim.TryCreate("A1", "A", "1", f.Definition, out var task), "create");
                Call(f.Story, "Submitted", task); Check(!Get<bool>(f.Story, "IsReply"), "unsubmitted task rejected");
                Check(f.Sim.TryPlace(task.Id, new Cell(0, 0), 0, out _), "place");
                f.Sim.Paused = false; f.Sim.Advance(1); f.Sim.Paused = true;
                Check(f.Sim.TrySubmit(task.Id), "actual submission"); Call(f.Story, "Submitted", task);
                Check(Get<bool>(f.Story, "IsReply"), "valid submission accepted later");
            }
        }
        [Test] public void WalletNotificationCannotReenterChoiceAndPayTwice()
        {
            using (var f = new Fixture())
            {
                f.Submit("A", "A1"); int revision = Get<int>(f.Story, "Revision"); int callbacks = 0;
                Action<int> callback = _ => { callbacks++; Check(!(bool)Call(f.Story, "ChooseAtRevision", 1, revision), "reentrant choice rejected"); };
                var changed = f.Wallet.GetType().GetEvent("OnFundsChanged"); changed.AddEventHandler(f.Wallet, callback);
                try
                {
                    Check((bool)Call(f.Story, "ChooseAtRevision", 1, revision), "outer choice completes");
                    Check(callbacks == 1 && Get<int>(f.Wallet, "CurrentFunds") == 15, "single payment and notification");
                }
                finally { changed.RemoveEventHandler(f.Wallet, callback); }
            }
        }
        [Test] public void MissingWalletFiltersEffectsButAllowsOrdinaryReply()
        {
            using (var f = new Fixture())
            {
                f.Story.GetType().GetProperty("Economy").SetValue(f.Story, null);
                f.Submit("A", "A1"); Check(((IList)Get<object>(f.Story, "Choices")).Count == 1, "wallet effects need an attached wallet");
                Check((bool)Call(f.Story, "Choose", 0), "legacy zero effect reply works");
            }
        }
    }
}
