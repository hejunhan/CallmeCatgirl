using System;
using System.Collections.Generic;
using NUnit.Framework;
using CallmeCatgirl.UI.SystemMenu;

namespace CallmeCatgirl.Tests.SystemMenu
{
    public sealed class SystemSettingsTests
    {
        private sealed class Backend : ISystemSettingsBackend
        {
            public DisplayConfiguration Actual = new DisplayConfiguration(1280, 720, GameWindowMode.Windowed);
            public DisplayConfiguration Requested;
            public bool Delayed;
            public int DisplayWrites;
            public DisplayConfiguration CurrentDisplay => Actual;
            public IReadOnlyList<DisplayResolution> AvailableResolutions { get; set; } = new[]
            { new DisplayResolution(1920,1080), new DisplayResolution(1280,720), new DisplayResolution(1920,1080), new DisplayResolution(640,360) };
            public float MasterVolume { get; set; } = .8f;
            public int TargetFrameRate { get; set; } = -1;
            public int VSyncCount { get; set; }
            public void SetDisplay(DisplayConfiguration configuration) { DisplayWrites++; Requested = configuration; if (!Delayed) Actual = configuration; }
            public void EndFrame() { Actual = Requested; }
        }
        private sealed class Store : ISystemSettingsStore
        {
            public SystemSettingsSnapshot Saved;
            public int Writes;
            public bool Fail;
            public bool TryLoad(out SystemSettingsSnapshot snapshot) { snapshot = Saved?.Copy(); return snapshot != null; }
            public void Save(SystemSettingsSnapshot snapshot)
            { if (Fail) throw new InvalidOperationException("test disk failure"); Saved = snapshot.Copy(); Writes++; }
        }
        private static readonly DisplayConfiguration FullHd = new DisplayConfiguration(1920, 1080, GameWindowMode.BorderlessFullscreen);

        // Given a device and duplicate mode entries, when a service is created,
        // then no device setting is changed and the actual current resolution remains selectable.
        [Test] public void ConstructionIsReadOnlyAndResolutionChoicesAreDistinctSortedAndIncludeCurrent()
        {
            var backend = new Backend { Actual = new DisplayConfiguration(1024, 600, GameWindowMode.Windowed) };
            var store = new Store(); var service = new SystemSettingsService(backend, store);
            CollectionAssert.AreEqual(new[] { new DisplayResolution(1024,600), new DisplayResolution(1280,720), new DisplayResolution(1920,1080) }, service.AvailableResolutions);
            Assert.That(backend.DisplayWrites, Is.Zero); Assert.That(backend.MasterVolume, Is.EqualTo(.8f)); Assert.That(store.Writes, Is.Zero);
            backend.Actual = new DisplayConfiguration(1180,680,GameWindowMode.Windowed); service.RefreshDisplayState();
            Assert.That(service.Current.Display, Is.EqualTo(backend.Actual)); CollectionAssert.Contains(service.AvailableResolutions,new DisplayResolution(1180,680));
        }

        // Given a pending preview and a paused game clock, when 15 unscaled seconds pass,
        // then the pre-preview display returns without losing a separately saved volume/FPS setting.
        [Test] public void PreviewTimeoutRevertsDisplayAndNeverPersistsTheUnconfirmedMode()
        {
            var backend = new Backend(); var store = new Store(); var service = new SystemSettingsService(backend, store);
            var original = backend.Actual; service.PreviewDisplay(FullHd); service.SetMasterVolume(.25f); service.SetFrameRateLimit(120);
            service.Tick(14.9f); Assert.That(service.IsDisplayPreviewPending, Is.True);
            Assert.That(store.Saved.Display, Is.EqualTo(original)); service.Tick(.2f);
            Assert.That(backend.Actual, Is.EqualTo(original)); Assert.That(service.IsDisplayPreviewPending, Is.False);
            Assert.That(backend.MasterVolume, Is.EqualTo(.25f)); Assert.That(backend.TargetFrameRate, Is.EqualTo(120));
        }

        // Given a successful display preview, when the player confirms,
        // then the next session restores the confirmed mode and numeric settings exactly.
        [Test] public void ConfirmationPersistsOnceAndLoadAppliesTheSameSharedSettings()
        {
            var backend = new Backend(); var store = new Store(); var service = new SystemSettingsService(backend, store);
            service.PreviewDisplay(FullHd); Assert.That(service.ConfirmDisplayPreview(), Is.True);
            Assert.That(service.ConfirmDisplayPreview(), Is.False); Assert.That(store.Writes, Is.EqualTo(1));
            service.SetFrameRateLimit(144); service.SetVSync(true); service.SetMasterVolume(.4f);
            var secondBackend = new Backend(); var second = new SystemSettingsService(secondBackend, store); second.LoadAndApply();
            Assert.That(secondBackend.Actual, Is.EqualTo(FullHd)); Assert.That(secondBackend.TargetFrameRate, Is.EqualTo(144));
            Assert.That(secondBackend.VSyncCount, Is.EqualTo(1)); Assert.That(secondBackend.MasterVolume, Is.EqualTo(.4f));
            second.SetVSync(false); Assert.That(secondBackend.TargetFrameRate, Is.EqualTo(144), "VSync toggles do not discard the selected cap.");
        }

        [Test] public void FocusLossAndApplicationExitRevertPendingDisplayWithoutSavingIt()
        {
            var backend = new Backend(); var store = new Store(); var service = new SystemSettingsService(backend, store); var original = backend.Actual;
            service.PreviewDisplay(FullHd); service.OnFocusChanged(false);
            Assert.That(backend.Actual, Is.EqualTo(original)); Assert.That(store.Writes, Is.Zero);
            service.PreviewDisplay(FullHd); service.OnLeavingApplication();
            Assert.That(backend.Actual, Is.EqualTo(original)); Assert.That(service.IsDisplayPreviewPending, Is.False); Assert.That(store.Writes, Is.Zero);
        }

        [Test] public void SeveralPreviewChoicesAndFrameDeferredApplicationRetainTheActualOriginalMode()
        {
            var backend = new Backend { Delayed = true, Actual = new DisplayConfiguration(1280,720,GameWindowMode.ExclusiveFullscreen) };
            var service = new SystemSettingsService(backend, new Store()); var original = backend.Actual;
            service.PreviewDisplay(FullHd); Assert.That(backend.Actual, Is.EqualTo(original), "SetResolution has not applied yet.");
            backend.EndFrame(); service.PreviewDisplay(new DisplayConfiguration(1280,720,GameWindowMode.Windowed));
            service.RevertDisplayPreview();
            Assert.That(backend.Requested, Is.EqualTo(original));
            // Reopening before the rollback's frame boundary must not capture the still-active Full HD preview.
            service.PreviewDisplay(FullHd); service.RevertDisplayPreview(); backend.EndFrame();
            Assert.That(backend.Actual, Is.EqualTo(original)); Assert.That(service.Current.Display, Is.EqualTo(original));
        }

        [Test] public void PreviewCapturesManualWindowResizeAndFailedPersistenceRemainsRecoverable()
        {
            var backend = new Backend(); var store = new Store { Fail = true }; var service = new SystemSettingsService(backend, store);
            backend.Actual = new DisplayConfiguration(1180, 680, GameWindowMode.Windowed);
            service.PreviewDisplay(FullHd);
            Assert.Throws<InvalidOperationException>(() => service.ConfirmDisplayPreview()); Assert.That(service.IsDisplayPreviewPending, Is.True);
            service.RevertDisplayPreview(); Assert.That(backend.Actual.Width, Is.EqualTo(1180)); Assert.That(backend.Actual.Height, Is.EqualTo(680));
        }

        [Test] public void DeferredDisplayCannotConfirmTheOldFrameAndConfirmationPersistsTheEffectiveOsAdjustedMode()
        {
            var backend = new Backend { Delayed = true }; var store = new Store(); var service = new SystemSettingsService(backend,store);
            service.PreviewDisplay(FullHd);
            Assert.That(service.CanConfirmDisplayPreview,Is.False); Assert.That(service.ConfirmDisplayPreview(),Is.False); Assert.That(store.Writes,Is.Zero);
            backend.Actual = new DisplayConfiguration(1600,900,GameWindowMode.BorderlessFullscreen);
            service.Tick(.016f); Assert.That(service.CanConfirmDisplayPreview,Is.False);
            service.Tick(.016f); Assert.That(service.CanConfirmDisplayPreview,Is.True); Assert.That(service.ConfirmDisplayPreview(),Is.True);
            Assert.That(store.Saved.Display,Is.EqualTo(backend.Actual));
        }

        [Test] public void InvalidValuesAreRejectedBeforeDeviceWritesAndBadStoredValuesUseSafeDefaults()
        {
            var backend = new Backend(); var store = new Store { Saved = new SystemSettingsSnapshot { MasterVolume = float.NaN, FrameRateLimit = 17, Display = new DisplayConfiguration(9000,9000,GameWindowMode.Windowed) } };
            var service = new SystemSettingsService(backend, store); service.LoadAndApply();
            Assert.That(backend.MasterVolume, Is.EqualTo(1)); Assert.That(backend.TargetFrameRate, Is.EqualTo(-1)); Assert.That(backend.DisplayWrites, Is.Zero);
            Assert.Throws<ArgumentOutOfRangeException>(() => service.SetMasterVolume(float.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => service.SetFrameRateLimit(17));
            Assert.Throws<ArgumentOutOfRangeException>(() => service.PreviewDisplay(new DisplayConfiguration(0,0,GameWindowMode.Windowed)));
            Assert.That(store.Writes, Is.Zero);
        }
    }
}
