using System;
using System.Collections.Generic;
using System.Linq;

namespace CallmeCatgirl.Persistence
{
    public sealed class RunSaveService
    {
        private readonly IRunSaveHost host;
        private readonly AtomicRunSaveStore store;
        private readonly IRunSaveExtension[] extensions;
        private SaveExtensionState[] retained = Array.Empty<SaveExtensionState>();
        public RunSaveService(IRunSaveHost host, AtomicRunSaveStore store, IEnumerable<IRunSaveExtension> extensions = null)
        { this.host = host ?? throw new ArgumentNullException(nameof(host)); this.store = store ?? throw new ArgumentNullException(nameof(store)); this.extensions = (extensions ?? Array.Empty<IRunSaveExtension>()).ToArray(); }
        public SaveWriteResult Save()
        {
            var data = host.CaptureRun();
            var captured = extensions.Select(x =>
            {
                var state = x.Capture();
                if (state == null || state.Id != x.Id || state.Required != x.Required) throw new InvalidOperationException("Extension capture identity/requirement mismatch.");
                return state;
            }).ToArray();
            data.Extensions = captured.Concat(retained.Where(x => !extensions.Any(e => e.Id == x.Id))).ToArray();
            data.SchemaVersion = RunSaveValidation.CurrentSchema; data.SavedAtUtc = DateTimeOffset.UtcNow.ToString("O");
            data.ContentRevision = host.ContentRevision;
            // Ensure the entire cross-module snapshot can be reconstructed before replacing the last committed file.
            // Prepare is contractually detached/pure until Commit; disposal here discards this validation graph.
            using (var prepared = host.PrepareRestore(data))
                if (prepared == null) throw new InvalidOperationException("Host did not validate the save graph.");
            return store.Save(data);
        }
        public SaveLoadResult Continue()
        {
            var loaded = store.Load();
            // Store/extension validation happened before constructing a live replacement.
            using (var prepared = host.PrepareRestore(loaded.Data))
            {
                if (prepared == null) throw new InvalidOperationException("Host did not prepare a restore.");
                prepared.Commit();
            }
            retained = loaded.Data.Extensions.Where(x => !extensions.Any(e => e.Id == x.Id)).ToArray();
            return loaded;
        }
        /// <summary>Reads the newest committed save, or a backup, without committing a restore.</summary>
        public SaveLoadResult Peek() => store.Load();
        // Call when the host starts a genuinely new run, so optional payloads cannot leak across runs.
        public void BeginNewRun() => retained = Array.Empty<SaveExtensionState>();
    }
}
