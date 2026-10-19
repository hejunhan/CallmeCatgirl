using UnityEngine;

namespace CallmeCatgirl.UI.SystemMenu
{
    // Own Player window only. No Editor windows, monitor modes, or durable preference writes.
    public sealed class NativeWindowStartup : MonoBehaviour
    {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        private struct Rect { public int left, top, right, bottom; public int Width => right - left; public int Height => bottom - top; }
        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct MonitorInfo { public int size; public Rect monitor, work; public uint flags; }
        private delegate bool EnumWindow(System.IntPtr window, System.IntPtr parameter);
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindow callback, System.IntPtr parameter);
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool IsWindowVisible(System.IntPtr window);
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(System.IntPtr window, out uint process);
        [System.Runtime.InteropServices.DllImport("kernel32.dll")] private static extern uint GetCurrentProcessId();
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool GetWindowRect(System.IntPtr window, out Rect rect);
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool GetClientRect(System.IntPtr window, out Rect rect);
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern System.IntPtr MonitorFromWindow(System.IntPtr window, uint flags);
        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)] private static extern bool GetMonitorInfo(System.IntPtr monitor, ref MonitorInfo info);
        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)] private static extern bool SetWindowPos(System.IntPtr window, System.IntPtr after, int x, int y, int width, int height, uint flags);
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Initialize()
        {
            var root = new GameObject("NativeWindowStartup"); DontDestroyOnLoad(root); root.AddComponent<NativeWindowStartup>();
        }
        private System.Collections.IEnumerator Start()
        {
            bool saved = new PlayerPrefsSystemSettingsStore().TryLoad(out var settings) && settings.Version == 1 && settings.Display.Width > 0 && settings.Display.Height > 0;
            // Let the existing settings transaction apply explicit user choices first.
            for (int i = 0; i < 3; i++) yield return new WaitForEndOfFrame();
            if (saved && settings.Display.Mode != GameWindowMode.Windowed) { Debug.Log("[WindowStartup] Preserved saved non-windowed display mode."); Destroy(gameObject); yield break; }
            if (!saved) Screen.SetResolution(StartupWindowPolicy.DefaultWidth, StartupWindowPolicy.DefaultHeight, FullScreenMode.Windowed);
            for (int i = 0; i < 3; i++) yield return new WaitForEndOfFrame();
            System.IntPtr handle = System.IntPtr.Zero; uint ownProcess = GetCurrentProcessId();
            EnumWindows((window, parameter) => { GetWindowThreadProcessId(window, out uint process); if (process != ownProcess || !IsWindowVisible(window)) return true; handle = window; return false; }, System.IntPtr.Zero);
            if (handle == System.IntPtr.Zero || !GetWindowRect(handle, out var outer) || !GetClientRect(handle, out var client)) { Debug.LogWarning("[WindowStartup] Own Player window was not available; no other window was touched."); Destroy(gameObject); yield break; }
            var monitor = new MonitorInfo { size = System.Runtime.InteropServices.Marshal.SizeOf<MonitorInfo>() };
            if (!GetMonitorInfo(MonitorFromWindow(handle, 2), ref monitor)) { Destroy(gameObject); yield break; }
            int requestedWidth = saved ? client.Width : StartupWindowPolicy.DefaultWidth;
            int requestedHeight = saved ? client.Height : StartupWindowPolicy.DefaultHeight;
            var fit = StartupWindowPolicy.FitClient(requestedWidth, requestedHeight, monitor.work.Width, monitor.work.Height, outer.Width - client.Width, outer.Height - client.Height);
            if (client.Width != fit.Width || client.Height != fit.Height) Screen.SetResolution(fit.Width, fit.Height, FullScreenMode.Windowed);
            double deadline = Time.realtimeSinceStartupAsDouble + 2;
            while ((Screen.width != fit.Width || Screen.height != fit.Height) && Time.realtimeSinceStartupAsDouble < deadline) yield return null;
            yield return new WaitForEndOfFrame();
            GetWindowRect(handle, out outer); GetClientRect(handle, out client);
            var center = StartupWindowPolicy.Center(monitor.work.left, monitor.work.top, monitor.work.Width, monitor.work.Height, outer.Width, outer.Height);
            bool moved = SetWindowPos(handle, System.IntPtr.Zero, center.x, center.y, 0, 0, 0x15);
            Debug.Log($"[WindowStartup] saved={saved}; requested={requestedWidth}x{requestedHeight}; client={client.Width}x{client.Height}; work={monitor.work.Width}x{monitor.work.Height}; centered={moved}; durable settings unchanged.");
            Destroy(gameObject);
        }
#endif
    }
}
