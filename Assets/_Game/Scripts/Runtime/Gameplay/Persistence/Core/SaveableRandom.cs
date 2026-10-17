using System;
using System.Globalization;

namespace CallmeCatgirl.Persistence
{
    /// <summary>Explicit xorshift32 System.Random stream for NEW runs; replace the one shared generation stream at its creation seam.</summary>
    public sealed class SaveableRandom : System.Random
    {
        private const string Algorithm = "callmecatgirl.xorshift32.v1";
        private uint state;
        private readonly int seed;
        public SaveableRandom(int seed) { this.seed = seed; state = unchecked((uint)seed) ^ 0x9e3779b9U; if (state == 0) state = 1; }
        private uint Word()
        { unchecked { uint x = state; x ^= x << 13; x ^= x >> 17; x ^= x << 5; state = x; return x; } }
        protected override double Sample() => Word() / 4294967296.0;
        public override double NextDouble() => Sample();
        public override int Next() => Next(int.MaxValue);
        public override int Next(int maxValue)
        { if (maxValue < 0) throw new ArgumentOutOfRangeException(nameof(maxValue)); return (int)(Sample() * maxValue); }
        public override int Next(int minValue, int maxValue)
        { if (minValue > maxValue) throw new ArgumentOutOfRangeException(nameof(minValue)); return (int)(minValue + (long)(Sample() * ((long)maxValue - minValue))); }
        public override void NextBytes(byte[] buffer)
        { if (buffer == null) throw new ArgumentNullException(nameof(buffer)); for (int i = 0; i < buffer.Length; i++) buffer[i] = (byte)Word(); }
        public override void NextBytes(Span<byte> buffer)
        { for (int i = 0; i < buffer.Length; i++) buffer[i] = (byte)Word(); }
        public RandomStreamState Capture() => new RandomStreamState
        { Version = 1, AlgorithmId = Algorithm, Seed = seed, StateJson = "{\"word\":\"" + state.ToString("x8", CultureInfo.InvariantCulture) + "\"}" };
        public static SaveableRandom Restore(RandomStreamState saved)
        {
            if (saved == null || saved.Version != 1 || saved.AlgorithmId != Algorithm) throw new SaveCompatibilityException("Unsupported generation random stream.");
            const string prefix = "{\"word\":\""; const string suffix = "\"}";
            if (saved.StateJson == null || saved.StateJson.Length != prefix.Length + 8 + suffix.Length
                || !saved.StateJson.StartsWith(prefix, StringComparison.Ordinal) || !saved.StateJson.EndsWith(suffix, StringComparison.Ordinal)
                || !uint.TryParse(saved.StateJson.Substring(prefix.Length, 8), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var word) || word == 0)
                throw new ArgumentException("Invalid random stream state.");
            return new SaveableRandom(saved.Seed) { state = word };
        }
    }
}
