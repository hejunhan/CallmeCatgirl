using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CallmeCatgirl.UI.SystemMenu
{
    public sealed class UnitySystemSettingsBackend : ISystemSettingsBackend
    {
        public DisplayConfiguration CurrentDisplay => new DisplayConfiguration(Screen.width, Screen.height,
            Screen.fullScreenMode == FullScreenMode.Windowed ? GameWindowMode.Windowed :
            Screen.fullScreenMode == FullScreenMode.ExclusiveFullScreen ? GameWindowMode.ExclusiveFullscreen :
            Screen.fullScreenMode == FullScreenMode.MaximizedWindow ? GameWindowMode.MaximizedWindow : GameWindowMode.BorderlessFullscreen);
        public IReadOnlyList<DisplayResolution> AvailableResolutions => Screen.resolutions.Select(r => new DisplayResolution(r.width, r.height)).ToArray();
        public float MasterVolume { get => AudioListener.volume; set => AudioListener.volume = value; }
        public int TargetFrameRate { get => Application.targetFrameRate; set => Application.targetFrameRate = value; }
        public int VSyncCount { get => QualitySettings.vSyncCount; set => QualitySettings.vSyncCount = value; }
        public void SetDisplay(DisplayConfiguration configuration) => Screen.SetResolution(configuration.Width, configuration.Height,
            configuration.Mode == GameWindowMode.Windowed ? FullScreenMode.Windowed :
            configuration.Mode == GameWindowMode.ExclusiveFullscreen ? FullScreenMode.ExclusiveFullScreen :
            configuration.Mode == GameWindowMode.MaximizedWindow ? FullScreenMode.MaximizedWindow : FullScreenMode.FullScreenWindow);
    }

    public sealed class PlayerPrefsSystemSettingsStore : ISystemSettingsStore
    {
        public const string DefaultKey = "CallmeCatgirl.SystemSettings.v1";
        private readonly string key;
        public PlayerPrefsSystemSettingsStore(string key = DefaultKey)
        {
            if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("A settings namespace is required.", nameof(key));
            this.key = key;
        }
        public bool TryLoad(out SystemSettingsSnapshot snapshot)
        {
            snapshot = null;
            if (!PlayerPrefs.HasKey(key)) return false;
            try { snapshot = JsonUtility.FromJson<SystemSettingsSnapshot>(PlayerPrefs.GetString(key)); return snapshot != null; }
            catch (ArgumentException) { return false; }
        }
        public void Save(SystemSettingsSnapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            PlayerPrefs.SetString(key, JsonUtility.ToJson(snapshot));
            PlayerPrefs.Save();
        }
    }
}
