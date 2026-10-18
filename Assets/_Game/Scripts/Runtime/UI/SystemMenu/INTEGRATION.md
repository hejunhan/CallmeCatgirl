# Esc drawer and shared settings

This folder is independently owned. It does not edit scenes, the game controller, save DTOs, simulation pause, playback speed, or `Time.timeScale`.

## Integration contract

Reference `CallmeCatgirl.UI.SystemMenu`. The assembly `CallmeCatgirl.SystemMenu` is automatically referenced by `Assembly-CSharp`.

```csharp
using CallmeCatgirl.UI.SystemMenu;

private SystemMenuHost systemMenu;
private bool systemMenuPauseClaim;

// Create once after the scene's Canvas and the existing Chinese Font are available.
systemMenu = SystemMenuHost.Create(
    canvasRect,
    existingChineseFont,
    gameplay: true,
    pauseClaimChanged: value => systemMenuPauseClaim = value,
    consumeEscapeBeforeMenu: ConsumeInputOrTopModal,
    saveAndReturnToMenu: () =>
    {
        // The host must allow saving while ONLY this menu's pause claim is active.
        // Keep real unfinished installation/training transaction restrictions.
        if (!SaveCurrentRunSafely())
            return MenuSaveResult.Failure("存档未能保存，请继续游戏后重试。");
        NavigateToMainMenu();
        return MenuSaveResult.Success;
    });

// Replace the old Escape -> immediate-return action. Call this from ONE input entry point.
if (escapePressed)
    systemMenu.HandleEscape();

// Add this claim to the simulation's advance/input gates without changing manual pause or speed.
bool shouldAdvance = existingAdvanceCondition && !systemMenuPauseClaim;
bool isModalOpen = existingModalCondition || systemMenu.IsBlockingInput;
```

`ConsumeInputOrTopModal` must return `true` after cancelling one highest-priority input field, top modal, active drag, or installation minigame. It must not also open the drawer in that input event. Include both UGUI and TMP input fields if present. When the settings sheet is open, Escape first reverts a pending display preview; a second Escape returns to the drawer; the next closes the drawer.

The public methods are `HandleEscape()`, `OpenPause()`, `OpenSettings()`, `CloseSettings()`, and `CloseAll()`. State is available through `IsPauseClaimed`, `IsBlockingInput`, `IsPauseOpen`, and `IsSettingsOpen`. Closing releases only this host's pause callback. A failed save leaves the drawer open with a readable message.

For the main menu, create the same host with `gameplay: false`, and wire a settings button to `host.OpenSettings`. It does not claim a gameplay pause. Do not instantiate a second settings implementation. Destroying or disabling the scene-owned host releases its own pause claim and reverts an unconfirmed display preview.

## Settings behavior

- `SystemSettingsService` has injectable `ISystemSettingsBackend` and `ISystemSettingsStore`. Construction only reads state. `LoadAndApply()` explicitly loads persisted confirmed settings and applies them.
- `UnitySystemSettingsBackend` controls `AudioListener.volume`, `Screen.SetResolution`, `Application.targetFrameRate`, and `QualitySettings.vSyncCount`.
- One real master volume control; window/borderless fullscreen; distinct available resolutions plus the actual current size; FPS choices `-1,30,60,90,120,144,240`; VSync `0/1`.
- Existing exclusive/maximized modes can be captured and restored exactly. The UI offers the requested window/borderless choices.
- `PlayerPrefsSystemSettingsStore` uses `CallmeCatgirl.SystemSettings.v1`. It accepts an explicit alternative key for isolated use. No tests use the production PlayerPrefs adapter.
- Display previews last 15 unscaled seconds. Confirmation alone persists the proposed display. Timeout, Escape, focus loss, leaving settings, disabling the host, and application exit revert. Saving another setting during a display preview persists the old display.
- `Screen.SetResolution` applies after the current frame. The service captures the actual pre-preview display and remembers pending requests so an immediate second preview cannot mistake an intermediate screen size for a confirmed mode. It does not claim success by reading `Screen.width` immediately after requesting a change.
- Desktop VSync overrides Unity's frame-rate cap; turning VSync off preserves the chosen cap. `-1` is labeled as the platform default rather than a promised rate. Editor preview is explicitly distinguished from desktop Player behavior.
- Only the drawer itself slides for 0.15 unscaled seconds. No full-screen wipe, inversion, or window movement is introduced. Settings content and drawer errors can scroll while the settings Back button stays fixed.

## Verification

`SystemMenuSessionTests` and `SystemSettingsTests` contain 12 NUnit tests for input priority, independent pause-claim lifetime, save failure, shared main-menu settings, distinct resolutions, confirmation/persistence, timeout, focus/exit rollback, delayed display application, manual window resizing, OS-adjusted modes, and invalid input. They run against in-memory adapters and can run without Unity.

`SystemMenuLayoutTests` adds three fake-backed Unity tests: 1280x720 and 1920x1080 layout bounds/scroll/fixed Back button; and the three-step Escape sequence for preview/settings/drawer. These tests do not write Screen, AudioListener, QualitySettings or PlayerPrefs. They require the lead's Unity test run and are not reported as executed by this module worker.

Still required after integration: use the game's actual Chinese font to inspect both resolutions; verify focus/drag/minigame cancellation before opening the drawer; verify manual pause, speed and other modal claims with the real controller; inspect save failure and menu Continue; build a desktop Player and check audio, window/fullscreen, display preview timeout/Alt-Tab, VSync/cap, relaunch persistence and safe close during preview. Editor or pure C# tests do not establish these Player behaviors.

Unity source checked by the lead: <https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Screen.SetResolution.html> and the Unity `QualitySettings.vSyncCount` / `Application.targetFrameRate` documentation.
