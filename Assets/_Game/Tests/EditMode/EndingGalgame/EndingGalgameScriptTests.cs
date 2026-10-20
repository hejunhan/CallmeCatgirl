using System;
using System.Collections.Generic;
using System.Linq;
using CallmeCatgirl.Persistence;
using CallmeCatgirl.UI.EndingGalgame;
using NUnit.Framework;
using UnityEngine;

namespace CallmeCatgirl.Tests.EndingGalgame
{
    public sealed class EndingGalgameScriptTests
    {
        private static EndingScriptDocument LoadAuthored()
        {
            var asset = Resources.Load<TextAsset>(EndingScriptLibrary.ResourcePath);
            Assert.That(asset, Is.Not.Null, "Authored ending script must exist under Resources/" + EndingScriptLibrary.ResourcePath);
            return EndingScriptLibrary.Parse(asset.text);
        }

        [Test]
        public void AuthoredScriptKeepsFourEndingsSixteenScenesAndValidLinks()
        {
            var document = LoadAuthored();

            Assert.That(document.endings.Length, Is.EqualTo(4));
            Assert.That(document.endings.Sum(e => e.scenes.Length), Is.EqualTo(16));
            Assert.That(document.endings.Sum(e => e.scenes.Sum(s => s.lines.Length)), Is.EqualTo(64));
            Assert.That(document.endings.Sum(e => e.scenes.Sum(s => s.choices.Length)), Is.EqualTo(21));

            foreach (var ending in document.endings)
            {
                var sceneIds = ending.scenes.Select(s => s.id).ToHashSet(StringComparer.Ordinal);
                Assert.That(sceneIds, Does.Contain(ending.startSceneId), ending.id + " start scene");
                foreach (var scene in ending.scenes)
                {
                    Assert.That(scene.lines, Is.Not.Empty, ending.id + "/" + scene.id + " lines");
                    Assert.That(scene.choices, Is.Not.Empty, ending.id + "/" + scene.id + " choices");
                    foreach (var choice in scene.choices)
                        if (!string.IsNullOrEmpty(choice.nextSceneId))
                            Assert.That(sceneIds, Does.Contain(choice.nextSceneId), ending.id + "/" + scene.id + "/" + choice.id);
                }
                Assert.That(ending.scenes.Any(s => s.choices.Any(c => string.IsNullOrEmpty(c.nextSceneId))), Is.True,
                    ending.id + " must expose a terminal choice");
            }
        }

        [TestCase("ending.low")]
        [TestCase("ending.normal")]
        [TestCase("ending.good")]
        [TestCase("ending.companion")]
        public void TriggerIdsResolveWithOptionalSpecialPrefix(string triggerNodeId)
        {
            var document = LoadAuthored();

            Assert.That(EndingScriptLibrary.TryFindEnding(document, triggerNodeId, out var plain), Is.True);
            Assert.That(EndingScriptLibrary.TryFindEnding(document, "special." + triggerNodeId, out var prefixed), Is.True);
            Assert.That(prefixed, Is.SameAs(plain));
            Assert.That(EndingScriptLibrary.TryFindEnding(document, triggerNodeId.Substring("ending.".Length), out _), Is.False);
            Assert.That(EndingRunReasons.Resolve(triggerNodeId), Is.EqualTo(EndingRunReasons.Resolve("special." + triggerNodeId)));
            Assert.That(RunEndReasons.IsNarrative(EndingRunReasons.Resolve(triggerNodeId)), Is.True);
        }

        [Test]
        public void ChoicesAdvanceThroughScenesAndTerminalChoiceCompletes()
        {
            var runner = new EndingStoryRunner(LoadAuthored(), "special.ending.normal");
            Assert.That(runner.Scene.id, Is.EqualTo("normal.1"));
            Assert.That(runner.IsAtChoices, Is.False);

            AdvanceToChoices(runner);
            Assert.That(runner.Scene.id, Is.EqualTo("normal.1"));
            Assert.That(runner.Choose(IndexOfChoice(runner.Scene, "enter")), Is.True);
            Assert.That(runner.Scene.id, Is.EqualTo("normal.2"));
            Assert.That(runner.LineIndex, Is.Zero);
            Assert.That(runner.IsAtChoices, Is.False);

            AdvanceToChoices(runner);
            Assert.That(runner.Choose(IndexOfChoice(runner.Scene, "decline")), Is.True);
            Assert.That(runner.Scene.id, Is.EqualTo("normal.3b"));

            AdvanceToChoices(runner);
            int terminal = IndexOfTerminal(runner.Scene);
            Assert.That(terminal, Is.GreaterThanOrEqualTo(0), "normal.3b must end the ending");
            Assert.That(runner.Choose(terminal), Is.True);
            Assert.That(runner.IsComplete, Is.True);
            Assert.That(runner.CurrentLine, Is.Null);

            Assert.That(runner.Advance(), Is.False, "completed endings ignore further advances");
            Assert.That(runner.Choose(terminal), Is.False, "completed endings ignore further choices");
        }

        [Test]
        public void RunnerRejectsUnknownOrMismatchedChoices()
        {
            var runner = new EndingStoryRunner(LoadAuthored(), "ending.low");
            Assert.That(runner.Choose(0), Is.False, "choices are unavailable before the last line");
            AdvanceToChoices(runner);
            Assert.That(runner.Choose(-1), Is.False);
            Assert.That(runner.Choose(runner.Scene.choices.Length), Is.False);
            Assert.That(runner.IsComplete, Is.False);
        }

        [Test]
        public void TerminalChoiceRaisesRunEndExactlyOnce()
        {
            var runner = new EndingStoryRunner(LoadAuthored(), "ending.companion");
            int guard = 0;
            while (!runner.IsComplete)
            {
                Assert.That(++guard, Is.LessThan(128), "companion ending must terminate");
                if (!runner.IsAtChoices) { Assert.That(runner.Advance(), Is.True); continue; }
                int terminal = IndexOfTerminal(runner.Scene);
                Assert.That(runner.Choose(terminal >= 0 ? terminal : 0), Is.True);
            }
            Assert.That(runner.Scene.id, Is.EqualTo("companion.3"));

            var request = new EndingRunRequest
            {
                EndingId = runner.Ending.id,
                TriggerNodeId = runner.Ending.triggerNodeId,
                RunEndReason = EndingRunReasons.Resolve(runner.Ending.triggerNodeId)
            };
            var gate = new EndingCompletionGate();
            var raised = new List<EndingRunRequest>();
            Assert.That(gate.TryRaise(request, raised.Add), Is.True);
            Assert.That(gate.TryRaise(request, raised.Add), Is.False);

            Assert.That(raised, Has.Count.EqualTo(1));
            Assert.That(raised[0].RunEndReason, Is.EqualTo(RunEndReasons.Emotion));
            Assert.That(RunEndReasons.IsNarrative(raised[0].RunEndReason), Is.True);
            Assert.That(gate.IsRaised, Is.True);
        }

        [Test]
        public void NonNarrativeRunEndReasonIsRejected()
        {
            var gate = new EndingCompletionGate();
            var request = new EndingRunRequest { EndingId = "ending.low", TriggerNodeId = "ending.low", RunEndReason = RunEndReasons.Bankruptcy };
            Assert.That(() => gate.TryRaise(request, _ => { }), Throws.ArgumentException);
            Assert.That(gate.IsRaised, Is.False);
        }

        [Test]
        public void MissingTextFallsBackToItsIdAndIsReported()
        {
            var reported = new List<string>();
            string text = EndingScriptLibrary.ResolveText(_ => throw new InvalidOperationException("missing"), "special.ending.low.title", reported.Add);
            Assert.That(text, Is.EqualTo("special.ending.low.title"));
            Assert.That(reported, Is.EqualTo(new[] { "special.ending.low.title" }));

            Assert.That(EndingScriptLibrary.ResolveText(_ => "正文", "any", reported.Add), Is.EqualTo("正文"));
            Assert.That(EndingScriptLibrary.ResolveText(_ => throw new InvalidOperationException(), string.Empty, reported.Add), Is.Empty);
        }

        [Test]
        public void ValidationRejectsBrokenLinksAndNonTerminatingScenes()
        {
            var broken = MinimalDocument(new EndingChoice { id = "go", labelTextId = "label", nextSceneId = "missing" });
            var brokenError = Assert.Throws<InvalidOperationException>(() => EndingScriptLibrary.Validate(broken));
            StringAssert.Contains("unknown scene missing", brokenError.Message);

            var cyclic = MinimalDocument(new EndingChoice { id = "loop", labelTextId = "label", nextSceneId = "a" });
            var cyclicError = Assert.Throws<InvalidOperationException>(() => EndingScriptLibrary.Validate(cyclic));
            StringAssert.Contains("cannot reach an empty nextSceneId", cyclicError.Message);

            EndingScriptLibrary.Validate(MinimalDocument(new EndingChoice { id = "end", labelTextId = "label", nextSceneId = "" }));
        }

        [Test]
        public void EveryAuthoredBackgroundAndCharacterLoadsAsSprite()
        {
            var document = LoadAuthored();
            var paths = document.endings.SelectMany(e => e.scenes)
                .SelectMany(s => new[] { s.background, s.characters?.left?.sprite, s.characters?.mid?.sprite, s.characters?.right?.sprite })
                .Where(p => !string.IsNullOrEmpty(p)).Distinct(StringComparer.Ordinal).ToArray();

            // lin-qi.png is authored for headProfile, which the panel intentionally ignores.
            Assert.That(paths, Has.Length.EqualTo(14));
            foreach (string path in paths)
                Assert.That(Resources.Load<Sprite>(path), Is.Not.Null, path + " must import as a Sprite");

            Assert.That(Resources.LoadAll<Sprite>("EndingGalgame/backgrounds"), Has.Length.EqualTo(9));
            Assert.That(Resources.LoadAll<Sprite>("EndingGalgame/characters"), Has.Length.EqualTo(6));
        }

        private static void AdvanceToChoices(EndingStoryRunner runner)
        {
            int guard = 0;
            while (!runner.IsAtChoices)
            {
                Assert.That(runner.Advance(), Is.True, "advance must reach the choices");
                Assert.That(++guard, Is.LessThan(64), "line walk must terminate");
            }
        }

        private static int IndexOfChoice(EndingScene scene, string choiceId)
        {
            int index = Array.FindIndex(scene.choices, c => c.id == choiceId);
            Assert.That(index, Is.GreaterThanOrEqualTo(0), "missing choice " + choiceId + " in " + scene.id);
            return index;
        }

        private static int IndexOfTerminal(EndingScene scene)
            => Array.FindIndex(scene.choices, c => string.IsNullOrEmpty(c.nextSceneId));

        private static EndingScriptDocument MinimalDocument(EndingChoice choice)
            => new EndingScriptDocument
            {
                schemaVersion = EndingScriptLibrary.SchemaVersion,
                endings = new[]
                {
                    new EndingScript
                    {
                        id = "ending.low", triggerNodeId = "ending.low", titleTextId = "title", startSceneId = "a",
                        scenes = new[]
                        {
                            new EndingScene
                            {
                                id = "a", background = "background", bgm = "",
                                lines = new[] { new EndingLine { id = "l1", textTextId = "text" } },
                                choices = new[] { choice }
                            }
                        }
                    }
                }
            };
    }
}
