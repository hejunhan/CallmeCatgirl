using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace CallmeCatgirl.Persistence
{
    [Serializable] public sealed class SaveEnvelope
    {
        public string Format;
        public int Version;
        public string PayloadSha256;
        public string PayloadJson;
    }
    public sealed class SaveLoadResult
    {
        public RunSaveData Data { get; internal set; }
        public string SourcePath { get; internal set; }
        public bool RecoveredBackup { get; internal set; }
        public string[] RejectedFiles { get; internal set; }
    }
    public sealed class SaveWriteResult
    {
        public string ActivePath { get; internal set; }
        public string BackupPath { get; internal set; }
    }

    /// <summary>Single slot, synchronous IO. Never deletes files. Call on the main thread or serialize calls in the host.</summary>
    public sealed class AtomicRunSaveStore
    {
        public const int MaxFileBytes = 16 * 1024 * 1024;
        private const string Format = "CallmeCatgirl.RunSave";
        private static readonly Encoding Utf8 = new UTF8Encoding(false, true);
        private readonly string directory;
        private readonly ISaveJsonCodec codec;
        private readonly string revision;
        private readonly string legacyStoryV1Revision;
        private readonly string[] compatiblePresentationRevisions;
        private readonly IRunSaveExtension[] extensions;
        public string ActivePath => Path.Combine(directory, "run.json");
        // A test seam for power-loss before commit. An exception leaves the last active file intact.
        public Action BeforeAtomicCommit { private get; set; }
        public AtomicRunSaveStore(string slotDirectory, ISaveJsonCodec codec, string contentRevision,
            IEnumerable<IRunSaveExtension> extensions = null, string legacyStoryV1Revision = null,
            IEnumerable<string> compatiblePresentationRevisions = null)
        {
            if (string.IsNullOrWhiteSpace(slotDirectory) || string.IsNullOrWhiteSpace(contentRevision)) throw new ArgumentException("Explicit slot directory/content revision required.");
            directory = Path.GetFullPath(slotDirectory); this.codec = codec ?? throw new ArgumentNullException(nameof(codec));
            revision = contentRevision; this.legacyStoryV1Revision = legacyStoryV1Revision; this.extensions = (extensions ?? Array.Empty<IRunSaveExtension>()).ToArray();
            this.compatiblePresentationRevisions = (compatiblePresentationRevisions ?? Array.Empty<string>()).ToArray();
        }
        private FileStream LockSlot()
        {
            Directory.CreateDirectory(directory);
            // Persistent lock file: closing releases the lock, no cleanup/delete needed after a crash.
            return new FileStream(Path.Combine(directory, "run.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        public SaveWriteResult Save(RunSaveData data)
        {
            RunSaveValidation.Validate(data, revision, extensions, legacyStoryV1Revision, compatiblePresentationRevisions);
            string payload = codec.Encode(data);
            string json = codec.Encode(new SaveEnvelope { Format = Format, Version = 1, PayloadJson = payload, PayloadSha256 = Hash(payload) });
            byte[] bytes = Utf8.GetBytes(json);
            if (bytes.Length > MaxFileBytes) throw new InvalidDataException("Run save exceeds size limit.");
            using (LockSlot())
            {
                if (File.Exists(ActivePath))
                {
                    try { Read(ActivePath, data.RunId); }
                    catch (SaveCompatibilityException) { throw; } // Keep future format/module protection and same-run content compatibility.
                    catch (ArgumentException) { }
                    catch (InvalidDataException) { } // Atomic replacement preserves the corrupt bytes in a unique backup.
                }
                string suffix = DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffffff") + "-" + Guid.NewGuid().ToString("N");
                string staging = Path.Combine(directory, "run.json.pending-" + suffix);
                using (var stream = new FileStream(staging, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                { stream.Write(bytes, 0, bytes.Length); stream.Flush(true); }
                // Verify the serialized bytes before touching the last committed slot.
                Read(staging);
                BeforeAtomicCommit?.Invoke();
                string backup = null;
                if (File.Exists(ActivePath))
                {
                    backup = Path.Combine(directory, "run.json.backup-" + suffix);
                    File.Replace(staging, ActivePath, backup); // Atomic, same directory/volume; no delete+move fallback.
                }
                else File.Move(staging, ActivePath);
                return new SaveWriteResult { ActivePath = ActivePath, BackupPath = backup };
            }
        }
        public SaveLoadResult Load()
        {
            using (LockSlot())
            {
                var rejected = new List<string>();
                var candidates = new[] { ActivePath }.Concat(Directory.GetFiles(directory, "run.json.backup-*").OrderByDescending(x => x, StringComparer.Ordinal));
                foreach (var path in candidates)
                {
                    if (!File.Exists(path)) continue;
                    try
                    {
                        return new SaveLoadResult { Data = Read(path), SourcePath = path,
                            RecoveredBackup = path != ActivePath, RejectedFiles = rejected.ToArray() };
                    }
                    catch (SaveCompatibilityException) { throw; } // Never silently roll back incompatible/newer saves.
                    catch (ArgumentException) { rejected.Add(path); }
                    catch (InvalidDataException) { rejected.Add(path); }
                }
                throw new InvalidDataException("No valid committed run save. Existing files were preserved. Rejected: " + rejected.Count);
            }
        }
        private RunSaveData Read(string path, string replacingRunId = null)
        {
            var info = new FileInfo(path);
            if (info.Length <= 0 || info.Length > MaxFileBytes) throw new InvalidDataException("Invalid save size.");
            string json;
            try { json = Utf8.GetString(File.ReadAllBytes(path)); }
            catch (DecoderFallbackException ex) { throw new InvalidDataException("Invalid UTF-8.", ex); }
            SaveEnvelope envelope;
            try { envelope = codec.Decode<SaveEnvelope>(json); }
            catch (Exception ex) when (IsJsonFailure(ex)) { throw new InvalidDataException("Invalid envelope JSON.", ex); }
            if (envelope == null || envelope.Format != Format) throw new InvalidDataException("Unknown save envelope.");
            if (envelope.Version != 1) throw new SaveCompatibilityException("Unsupported envelope version: " + envelope.Version);
            if (envelope.PayloadJson == null || envelope.PayloadSha256 != Hash(envelope.PayloadJson)) throw new InvalidDataException("Save checksum mismatch.");
            RunSaveData data;
            try { data = codec.Decode<RunSaveData>(envelope.PayloadJson); }
            catch (Exception ex) when (IsJsonFailure(ex)) { throw new InvalidDataException("Invalid run JSON.", ex); }
            // A new run may replace an older configuration's run, keeping its complete bytes in the backup.
            // Still validate every schema/component/extension and ledger; loading or updating the same run stays strict.
            bool differentRun = data != null && Guid.TryParse(data.RunId, out var savedRun)
                && Guid.TryParse(replacingRunId, out var newRun) && savedRun != newRun;
            RunSaveValidation.Validate(data, differentRun ? data.ContentRevision : revision,
                extensions, legacyStoryV1Revision, compatiblePresentationRevisions); return data;
        }
        private static bool IsJsonFailure(Exception ex) => ex is ArgumentException || ex is FormatException || ex.GetType().Name == "JsonException";
        private static string Hash(string text)
        {
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(Utf8.GetBytes(text))).Replace("-", "").ToLowerInvariant();
        }
    }
}
