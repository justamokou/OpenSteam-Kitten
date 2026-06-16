using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace OpenSteamKitten.Services
{
    /// <summary>
    /// 监听前台进程是否加载 Steam Overlay，用于 Steam 游戏运行时自动隐藏悬浮窗。
    /// </summary>
    public sealed class GameVisibilityService : IDisposable
    {
        private const string SettingsKeyPath = @"Software\OpenSteamKitten";
        private const string HideInGameValueName = "HideFloatingWindowInGame";

        private readonly Action<bool> _onSteamOverlayStateChanged;
        private System.Threading.Timer? _timer;
        private bool _lastSteamOverlayActive;
        private bool _disposed;

        public GameVisibilityService(Action<bool> onSteamOverlayStateChanged)
        {
            _onSteamOverlayStateChanged = onSteamOverlayStateChanged;
        }

        public static bool IsEnabled()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(SettingsKeyPath);
                return Convert.ToInt32(key?.GetValue(HideInGameValueName, 0)) == 1;
            }
            catch { return false; }
        }

        public static void SetEnabled(bool enabled)
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(SettingsKeyPath, writable: true);
                key?.SetValue(HideInGameValueName, enabled ? 1 : 0, RegistryValueKind.DWord);
            }
            catch { }
        }

        public void Start()
        {
            _lastSteamOverlayActive = IsSteamOverlayActiveInForegroundProcess();
            Notify(_lastSteamOverlayActive);
            _timer = new System.Threading.Timer(OnTick, null, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2));
        }

        private void OnTick(object? state)
        {
            if (_disposed) return;

            bool now = IsSteamOverlayActiveInForegroundProcess();
            if (now == _lastSteamOverlayActive) return;

            _lastSteamOverlayActive = now;
            Notify(now);
        }

        private void Notify(bool isSteamOverlayActive)
        {
            var app = System.Windows.Application.Current;
            try { app?.Dispatcher.Invoke(() => _onSteamOverlayStateChanged(isSteamOverlayActive)); }
            catch { }
        }

        private static bool IsSteamOverlayActiveInForegroundProcess()
        {
            IntPtr hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero || !IsWindowVisible(hwnd))
                return false;

            GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == Environment.ProcessId)
                return false;

            try
            {
                using var proc = Process.GetProcessById((int)pid);
                foreach (ProcessModule module in proc.Modules)
                {
                    string? name = module.ModuleName;
                    if (string.IsNullOrEmpty(name)) continue;

                    if (name.Equals("GameOverlayRenderer.dll", StringComparison.OrdinalIgnoreCase) ||
                        name.Equals("GameOverlayRenderer64.dll", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }
            catch (Win32Exception) { }
            catch (InvalidOperationException) { }
            catch { }

            return false;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _timer?.Dispose();
            _timer = null;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
    }
}
