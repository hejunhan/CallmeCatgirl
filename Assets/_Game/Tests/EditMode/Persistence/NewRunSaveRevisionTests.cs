using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using CallmeCatgirl.Gameplay.TaskGrid;
using CallmeCatgirl.Gameplay.Progression;
using CallmeCatgirl.Persistence;
using NUnit.Framework;
using UnityEngine;

namespace CallmeCatgirl.Tests.Persistence
{
    public sealed class NewRunSaveRevisionTests
    {
        private string directory;
        private readonly ISaveJsonCodec codec = new JsonCodec();
        [SetUp] public void SetUp() => directory = Path.Combine(Path.GetTempPath(), "CallmeCatgirl-NewRunSave-" + Guid.NewGuid().ToString("N"));
        [TearDown] public void TearDown()
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
        private AtomicRunSaveStore Store(string revision) => new AtomicRunSaveStore(directory, codec, revision, new[] { new VersionedExtension() });

        [Test]
        public void DifferentRunCanSaveAfterConfigurationChangeAndPreservesPreviousBytes()
        {
            var oldRun = Run("old-config");
            Store("old-config").Save(oldRun);
            var original = File.ReadAllBytes(Store("old-config").ActivePath);
            var newRun = Run("new-config");
            var result = Store("new-config").Save(newRun);
            Assert.That(File.ReadAllBytes(result.BackupPath), Is.EqualTo(original));
            Assert.That(Store("new-config").Load().Data.RunId, Is.EqualTo(newRun.RunId));
        }

        [Test]
        public void SameRunWithChangedConfigurationCannotReplaceItsSavedState()
        {
            var oldRun = Run("old-config"); Store("old-config").Save(oldRun);
            var next = Run("new-config"); next.RunId = oldRun.RunId;
            AssertProtected(next);
        }

        [Test]
        public void LoadStillRejectsDifferentConfiguration()
        {
            Store("old-config").Save(Run("old-config"));
            Assert.Throws<SaveCompatibilityException>(() => Store("new-config").Load());
        }

        [TestCase("schema")]
        [TestCase("component")]
        [TestCase("required-extension")]
        [TestCase("extension-version")]
        [TestCase("envelope")]
        public void NewRunCannotOverwriteUnsupportedSavedVersions(string kind)
        {
            var oldRun = Run("old-config");
            if (kind == "schema") oldRun.SchemaVersion++;
            if (kind == "component") oldRun.Host.Version = 4;
            if (kind == "required-extension") oldRun.Extensions = new[] { new SaveExtensionState { Id = "future-module", Version = 1, Required = true, PayloadJson = "{}" } };
            if (kind == "extension-version") oldRun.Extensions = new[] { new SaveExtensionState { Id = "known-module", Version = 2, Required = false, PayloadJson = "{}" } };
            WriteEnvelope(oldRun, kind == "envelope" ? 2 : 1);
            AssertProtected(Run("new-config"));
        }

        private void AssertProtected(RunSaveData next)
        {
            string active = Store("new-config").ActivePath;
            var original = File.ReadAllBytes(active);
            Assert.Throws<SaveCompatibilityException>(() => Store("new-config").Save(next));
            Assert.That(File.ReadAllBytes(active), Is.EqualTo(original));
            Assert.That(Directory.GetFiles(directory, "run.json.backup-*").Length, Is.Zero);
        }
        private void WriteEnvelope(RunSaveData run, int envelopeVersion)
        {
            Directory.CreateDirectory(directory);
            string payload = codec.Encode(run);
            string hash;
            using (var sha = SHA256.Create()) hash = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(payload))).Replace("-", "").ToLowerInvariant();
            File.WriteAllText(Store("new-config").ActivePath, codec.Encode(new SaveEnvelope { Format = "CallmeCatgirl.RunSave", Version = envelopeVersion, PayloadJson = payload, PayloadSha256 = hash }), new UTF8Encoding(false));
        }
        private static RunSaveData Run(string revision)
        {
            var model = new ModelDefinition("model", 1, new Dictionary<AbilityKind, double>());
            var simulation = new TaskSimulation(1, 1, new[] { new Cell(0, 0) }, model) { Paused = true };
            return new RunSaveData
            {
                SchemaVersion = 1, RunId = Guid.NewGuid().ToString("D"), ContentRevision = revision, SavedAtUtc = DateTimeOffset.UtcNow.ToString("O"),
                Simulation = simulation.CaptureState(), Policies = new ProgressionState(), Extensions = Array.Empty<SaveExtensionState>(),
                Host = new HostState { Version = 3, CurrentMonth = 1, SecondsPerMonth = 120, PlaybackSpeed = 1, MemoryLevel = 1, TaskBases = Array.Empty<TaskBasisState>(), OperatingRequestIds = Array.Empty<string>(), FillerCounts = new int[5], GenerationRandom = new RandomStreamState { Version = 1, AlgorithmId = "test", StateJson = "{}" } },
                Economy = new EconomyState { Version = 1, SettledRequestIds = Array.Empty<string>() },
                Story = new StoryState { Version = 1, Users = Array.Empty<StoryUserState>(), VisibleChoices = Array.Empty<ReplyChoiceState>(), ArrivedRequestIds = Array.Empty<string>(), SubmittedRequestIds = Array.Empty<string>(), SettledRequestIds = Array.Empty<string>(), PendingRequestIds = Array.Empty<string>() }
            };
        }
        private sealed class VersionedExtension : IRunSaveExtension
        {
            public string Id => "known-module";
            public bool Required => false;
            public SaveExtensionState Capture() => new SaveExtensionState { Id = Id, Version = 1, Required = false, PayloadJson = "{}" };
            public void Validate(SaveExtensionState state)
            {
                if (state.Version != 1) throw new SaveCompatibilityException("Unsupported known-module version.");
            }
        }
        private sealed class JsonCodec : ISaveJsonCodec
        {
            public string Encode<T>(T value) => JsonUtility.ToJson(value);
            public T Decode<T>(string json) => JsonUtility.FromJson<T>(json);
        }
    }
}