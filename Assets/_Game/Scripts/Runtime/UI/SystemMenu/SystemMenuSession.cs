using System;

namespace CallmeCatgirl.UI.SystemMenu
{
    public readonly struct MenuSaveResult
    {
        public bool Succeeded { get; }
        public string Message { get; }
        private MenuSaveResult(bool succeeded, string message) { Succeeded = succeeded; Message = message; }
        public static MenuSaveResult Success => new MenuSaveResult(true, null);
        public static MenuSaveResult Failure(string message) => new MenuSaveResult(false, string.IsNullOrWhiteSpace(message) ? "保存失败，请留在游戏中重试。" : message);
    }

    /// <summary>Owns only this menu's pause claim. It has no access to game pause or playback speed.</summary>
    public sealed class SystemMenuSession
    {
        private readonly bool gameplay;
        private readonly Action<bool> pauseClaimChanged;
        private readonly Func<bool> consumeEscapeBeforeMenu;
        private readonly Func<MenuSaveResult> saveAndReturnToMenu;
        public bool IsPauseOpen { get; private set; }
        public bool IsSettingsOpen { get; private set; }
        public bool IsPauseClaimed => gameplay && (IsPauseOpen || IsSettingsOpen);
        public bool IsBlockingInput => IsPauseOpen || IsSettingsOpen;
        public string SaveError { get; private set; }
        public event Action Changed;

        public SystemMenuSession(bool gameplay, Action<bool> pauseClaimChanged = null,
            Func<bool> consumeEscapeBeforeMenu = null, Func<MenuSaveResult> saveAndReturnToMenu = null)
        {
            this.gameplay = gameplay;
            this.pauseClaimChanged = pauseClaimChanged;
            this.consumeEscapeBeforeMenu = consumeEscapeBeforeMenu;
            this.saveAndReturnToMenu = saveAndReturnToMenu;
        }

        // Called by the host's ONE Escape input entry point. Existing modals/input get first refusal.
        public bool HandleEscape()
        {
            if (IsSettingsOpen) { CloseSettings(); return true; }
            if (IsPauseOpen) { CloseAll(); return true; }
            if (consumeEscapeBeforeMenu != null && consumeEscapeBeforeMenu()) return true;
            if (!gameplay) return false;
            OpenPause();
            return true;
        }

        public void OpenPause()
        {
            if (!gameplay) return;
            Change(true, false);
        }
        public void OpenSettings() => Change(IsPauseOpen, true);
        public void CloseSettings() => Change(IsPauseOpen, false);
        public void CloseAll() => Change(false, false);

        public bool SaveAndReturn()
        {
            if (!gameplay || !IsPauseOpen || IsSettingsOpen) return false;
            MenuSaveResult result;
            try { result = saveAndReturnToMenu == null ? MenuSaveResult.Failure("尚未连接保存入口，请继续游戏。") : saveAndReturnToMenu(); }
            catch (Exception exception) { result = MenuSaveResult.Failure("保存失败：" + exception.Message); }
            if (!result.Succeeded)
            {
                SaveError = string.IsNullOrWhiteSpace(result.Message) ? "保存失败，请重试。" : result.Message;
                Changed?.Invoke();
                return false;
            }
            CloseAll();
            return true;
        }

        private void Change(bool pause, bool settings)
        {
            bool previousClaim = IsPauseClaimed;
            if (IsPauseOpen == pause && IsSettingsOpen == settings) return;
            IsPauseOpen = pause;
            IsSettingsOpen = settings;
            SaveError = null;
            if (previousClaim != IsPauseClaimed) pauseClaimChanged?.Invoke(IsPauseClaimed);
            Changed?.Invoke();
        }
    }
}
