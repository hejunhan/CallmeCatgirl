using System;

namespace CallmeCatgirl.Persistence
{
    /// <summary>Serializes the owning module's native versioned DTO. Defines no duplicate module state.</summary>
    public sealed class StateSaveExtension<TState> : IRunSaveExtension, IRunTaskRoleProvider where TState : class
    {
        private readonly int version;
        private readonly ISaveJsonCodec codec;
        private readonly Func<TState> capture;
        private readonly Action<TState> validate;
        private readonly Func<TState, string[]> nonCustomerTasks;
        public string Id { get; }
        public bool Required { get; }
        public StateSaveExtension(string id, int version, bool required, ISaveJsonCodec codec, Func<TState> capture,
            Action<TState> validate, Func<TState, string[]> nonCustomerTasks = null)
        {
            if (string.IsNullOrWhiteSpace(id) || version <= 0) throw new ArgumentException("Stable module identity/version required.");
            Id = id; this.version = version; Required = required; this.codec = codec ?? throw new ArgumentNullException(nameof(codec));
            this.capture = capture ?? throw new ArgumentNullException(nameof(capture)); this.validate = validate ?? throw new ArgumentNullException(nameof(validate));
            this.nonCustomerTasks = nonCustomerTasks;
        }
        public SaveExtensionState Capture()
        {
            var native = capture() ?? throw new ArgumentException("Module capture returned no state."); validate(native);
            return new SaveExtensionState { Id = Id, Version = version, Required = Required, PayloadJson = codec.Encode(native) };
        }
        public TState DecodeForRestore(SaveExtensionState state)
        {
            if (state == null || state.Id != Id || state.Version != version || state.Required != Required)
                throw new SaveCompatibilityException("Unsupported module state: " + Id);
            TState native;
            try { native = codec.Decode<TState>(state.PayloadJson); }
            catch (Exception ex) when (ex is ArgumentException || ex is FormatException || ex.GetType().Name == "JsonException")
            { throw new ArgumentException("Invalid module JSON: " + Id, ex); }
            if (native == null) throw new ArgumentException("Missing module payload.");
            validate(native); return native;
        }
        public void Validate(SaveExtensionState state) => DecodeForRestore(state);
        public string[] NonCustomerRequestIds(SaveExtensionState state) => nonCustomerTasks?.Invoke(DecodeForRestore(state)) ?? Array.Empty<string>();
    }
}
