using System;
using System.Collections.Generic;
using System.Linq;

namespace CallmeCatgirl.UI.SystemMenu
{
    // The UI offers windowed/borderless. The other values preserve an existing Player mode exactly during rollback.
    public enum GameWindowMode { Windowed, BorderlessFullscreen, ExclusiveFullscreen, MaximizedWindow }

    [Serializable]
    public struct DisplayResolution : IEquatable<DisplayResolution>
    {
        public int Width;
        public int Height;
        public DisplayResolution(int width, int height) { Width = width; Height = height; }
        public bool Equals(DisplayResolution other) => Width == other.Width && Height == other.Height;
        public override bool Equals(object value) => value is DisplayResolution other && Equals(other);
        public override int GetHashCode() => unchecked(Width * 397 ^ Height);
        public override string ToString() => Width + " × " + Height;
    }

    [Serializable]
    public struct DisplayConfiguration : IEquatable<DisplayConfiguration>
    {
        public int Width;
        public int Height;
        public GameWindowMode Mode;
        public DisplayConfiguration(int width, int height, GameWindowMode mode) { Width = width; Height = height; Mode = mode; }
        public DisplayResolution Resolution => new DisplayResolution(Width, Height);
        public bool Equals(DisplayConfiguration other) => Width == other.Width && Height == other.Height && Mode == other.Mode;
        public override bool Equals(object value) => value is DisplayConfiguration other && Equals(other);
        public override int GetHashCode() => unchecked(Resolution.GetHashCode() * 397 ^ (int)Mode);
    }

    [Serializable]
    public sealed class SystemSettingsSnapshot
    {
        public int Version = 1;
        public float MasterVolume = 1;
        public DisplayConfiguration Display;
        public int FrameRateLimit = -1;
        public bool VSync;
        public SystemSettingsSnapshot Copy() => new SystemSettingsSnapshot
        { Version = Version, MasterVolume = MasterVolume, Display = Display, FrameRateLimit = FrameRateLimit, VSync = VSync };
    }

    public interface ISystemSettingsBackend
    {
        DisplayConfiguration CurrentDisplay { get; }
        IReadOnlyList<DisplayResolution> AvailableResolutions { get; }
        float MasterVolume { get; set; }
        int TargetFrameRate { get; set; }
        int VSyncCount { get; set; }
        void SetDisplay(DisplayConfiguration configuration);
    }

    public interface ISystemSettingsStore
    {
        bool TryLoad(out SystemSettingsSnapshot snapshot);
        void Save(SystemSettingsSnapshot snapshot);
    }

    /// <summary>Settings transactions; the display preview is never written to durable storage before confirmation.</summary>
    public sealed class SystemSettingsService
    {
        public const float DisplayPreviewSeconds = 15;
        private static readonly int[] allowedFrameRates = { -1, 30, 60, 90, 120, 144, 240 };
        public static IReadOnlyList<int> FrameRateOptions { get; } = Array.AsReadOnly(allowedFrameRates);
        private readonly ISystemSettingsBackend backend;
        private readonly ISystemSettingsStore store;
        private readonly IReadOnlyList<DisplayResolution> resolutions;
        private SystemSettingsSnapshot current;
        private DisplayConfiguration confirmedDisplay;
        private DisplayConfiguration stableDisplay, requestedDisplay;
        private bool displayRequestPending;
        private int displayObservationTicks;
        public event Action Changed;
        public SystemSettingsSnapshot Current => current.Copy();
        public IReadOnlyList<DisplayResolution> AvailableResolutions => Array.AsReadOnly(resolutions
            .Concat(new[] { backend.CurrentDisplay.Resolution, current.Display.Resolution })
            .Where(r => r.Width > 0 && r.Height > 0).Distinct().OrderBy(r => r.Width).ThenBy(r => r.Height).ToArray());
        public DisplayConfiguration ConfirmedDisplay => confirmedDisplay;
        public bool IsDisplayPreviewPending { get; private set; }
        public bool CanConfirmDisplayPreview => IsDisplayPreviewPending && (!displayRequestPending || backend.CurrentDisplay.Equals(requestedDisplay));
        public float PreviewSecondsRemaining { get; private set; }

        // Construction is read-only, so consumers can inspect choices without changing the host device.
        public SystemSettingsService(ISystemSettingsBackend backend, ISystemSettingsStore store)
        {
            this.backend = backend ?? throw new ArgumentNullException(nameof(backend));
            this.store = store ?? throw new ArgumentNullException(nameof(store));
            confirmedDisplay = ValidDisplay(backend.CurrentDisplay) ? backend.CurrentDisplay : new DisplayConfiguration(1280, 720, GameWindowMode.Windowed);
            stableDisplay = confirmedDisplay;
            resolutions = Array.AsReadOnly((backend.AvailableResolutions ?? Array.Empty<DisplayResolution>())
                .Where(r => r.Width >= 800 && r.Height >= 450)
                .Append(confirmedDisplay.Resolution).Distinct().OrderBy(r => r.Width).ThenBy(r => r.Height).ToArray());
            current = new SystemSettingsSnapshot
            {
                Display = confirmedDisplay,
                MasterVolume = NormalizeVolume(backend.MasterVolume),
                FrameRateLimit = allowedFrameRates.Contains(backend.TargetFrameRate) ? backend.TargetFrameRate : -1,
                VSync = backend.VSyncCount > 0
            };
        }

        public void LoadAndApply()
        {
            RevertDisplayPreview();
            if (store.TryLoad(out var saved) && saved != null && saved.Version == 1)
            {
                current.MasterVolume = NormalizeVolume(saved.MasterVolume);
                current.FrameRateLimit = allowedFrameRates.Contains(saved.FrameRateLimit) ? saved.FrameRateLimit : -1;
                current.VSync = saved.VSync;
                if (ValidDisplay(saved.Display) && resolutions.Contains(saved.Display.Resolution)) confirmedDisplay = saved.Display;
            }
            current.Display = confirmedDisplay;
            backend.MasterVolume = current.MasterVolume;
            backend.TargetFrameRate = current.FrameRateLimit;
            backend.VSyncCount = current.VSync ? 1 : 0;
            if (!backend.CurrentDisplay.Equals(confirmedDisplay)) RequestDisplay(confirmedDisplay);
            Changed?.Invoke();
        }

        public void RefreshDisplayState()
        {
            if (IsDisplayPreviewPending) return;
            confirmedDisplay = ObserveStableDisplay();
            current.Display = confirmedDisplay;
            Changed?.Invoke();
        }

        public void SetMasterVolume(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) throw new ArgumentOutOfRangeException(nameof(value));
            current.MasterVolume = Math.Max(0, Math.Min(1, value));
            backend.MasterVolume = current.MasterVolume;
            SaveCommitted(confirmedDisplay);
            Changed?.Invoke();
        }
        public void SetFrameRateLimit(int value)
        {
            if (!allowedFrameRates.Contains(value)) throw new ArgumentOutOfRangeException(nameof(value));
            current.FrameRateLimit = value;
            backend.TargetFrameRate = value;
            SaveCommitted(confirmedDisplay);
            Changed?.Invoke();
        }
        public void SetVSync(bool value)
        {
            current.VSync = value;
            backend.VSyncCount = value ? 1 : 0;
            SaveCommitted(confirmedDisplay);
            Changed?.Invoke();
        }

        public bool PreviewDisplay(DisplayConfiguration candidate)
        {
            if (!ValidDisplay(candidate) || !AvailableResolutions.Contains(candidate.Resolution)) throw new ArgumentOutOfRangeException(nameof(candidate));
            // Capture the effective pre-request display, including a player's manual window resize.
            // A previous SetResolution may still be waiting for the frame boundary: do not snapshot its stale intermediate value.
            if (!IsDisplayPreviewPending) confirmedDisplay = ObserveStableDisplay();
            if (candidate.Equals(confirmedDisplay)) { RevertDisplayPreview(); return false; }
            // Keep the original confirmed display when a player tries several choices during one preview.
            RequestDisplay(candidate);
            current.Display = candidate;
            IsDisplayPreviewPending = true;
            PreviewSecondsRemaining = DisplayPreviewSeconds;
            Changed?.Invoke();
            return true;
        }
        public bool ConfirmDisplayPreview()
        {
            if (!CanConfirmDisplayPreview) return false;
            // A failed disk write leaves the preview pending and therefore still recoverable.
            var effectiveDisplay = ValidDisplay(backend.CurrentDisplay) ? backend.CurrentDisplay : current.Display;
            SaveCommitted(effectiveDisplay);
            confirmedDisplay = effectiveDisplay;
            current.Display = effectiveDisplay;
            IsDisplayPreviewPending = false;
            PreviewSecondsRemaining = 0;
            Changed?.Invoke();
            return true;
        }
        public void RevertDisplayPreview()
        {
            if (!IsDisplayPreviewPending) return;
            RequestDisplay(confirmedDisplay);
            current.Display = confirmedDisplay;
            IsDisplayPreviewPending = false;
            PreviewSecondsRemaining = 0;
            Changed?.Invoke();
        }
        public void Tick(float unscaledDeltaTime)
        {
            if (float.IsNaN(unscaledDeltaTime) || float.IsInfinity(unscaledDeltaTime) || unscaledDeltaTime < 0) throw new ArgumentOutOfRangeException(nameof(unscaledDeltaTime));
            // The host calls Tick once per Update. Two observations cover a request made either
            // before this frame's Update or by the EventSystem after it. OS-adjusted modes need not equal the request.
            if (displayRequestPending && ++displayObservationTicks >= 2) displayRequestPending = false;
            if (!IsDisplayPreviewPending) { ObserveStableDisplay(); return; }
            PreviewSecondsRemaining = Math.Max(0, PreviewSecondsRemaining - unscaledDeltaTime);
            if (PreviewSecondsRemaining <= 0) RevertDisplayPreview();
        }
        public void OnFocusChanged(bool focused) { if (!focused) RevertDisplayPreview(); }
        public void OnLeavingApplication() => RevertDisplayPreview();

        private void SaveCommitted(DisplayConfiguration display)
        {
            var snapshot = current.Copy();
            snapshot.Display = display;
            store.Save(snapshot);
        }
        private void RequestDisplay(DisplayConfiguration display)
        {
            requestedDisplay = display;
            displayRequestPending = true;
            displayObservationTicks = 0;
            backend.SetDisplay(display);
        }
        private DisplayConfiguration ObserveStableDisplay()
        {
            var actual = backend.CurrentDisplay;
            if (ValidDisplay(actual) && (!displayRequestPending || actual.Equals(requestedDisplay)))
            {
                stableDisplay = actual;
                displayRequestPending = false;
            }
            return stableDisplay;
        }
        private static bool ValidDisplay(DisplayConfiguration display) => display.Width > 0 && display.Height > 0 && Enum.IsDefined(typeof(GameWindowMode), display.Mode);
        private static float NormalizeVolume(float value) => float.IsNaN(value) || float.IsInfinity(value) ? 1 : Math.Max(0, Math.Min(1, value));
    }
}
