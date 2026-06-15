using System;
using System.Diagnostics;
using System.Threading;
using System.Windows;
using Microsoft.Win32;

namespace OpenSteamKitten.Services
{
    /// <summary>
    /// 「随 Steam 启动」：注册表开机自启（值带 --steamwatch）+ Steam 进程监听。
    /// 静默模式下监听 steam.exe 进程，触发回调显示悬浮窗。
    /// 用 System.Threading.Timer（线程池）而非 DispatcherTimer——确保静默模式（窗口未 Show）下也能正常触发。
    /// 启动时若 Steam 已在（如开机自启时 Steam 先于 Kitten 启动），立即触发，不等 false→true。
    /// </summary>
    public class SteamLaunchService : IDisposable
    {
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "OpenSteamKitten";
        private const string WatchArg = "--steamwatch";

        private readonly Action _onSteamStarted;
        private System.Threading.Timer? _timer;
        private volatile bool _steamWasRunning;
        private bool _disposed;

        public SteamLaunchService(Action onSteamStarted)
        {
            _onSteamStarted = onSteamStarted;
        }

        #region 注册表自启

        /// <summary>当前是否已注册「随 Steam 启动」。</summary>
        public static bool IsEnabled()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
                return key?.GetValue(ValueName) != null;
            }
            catch { return false; }
        }

        /// <summary>注册开机自启（带 --steamwatch，启动后进入静默监听）。</summary>
        public static void Enable()
        {
            var exePath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exePath)) return;
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
                key?.SetValue(ValueName, $"\"{exePath}\" {WatchArg}", RegistryValueKind.String);
            }
            catch { }
        }

        /// <summary>取消「随 Steam 启动」。</summary>
        public static void Disable()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
                key?.DeleteValue(ValueName, throwOnMissingValue: false);
            }
            catch { }
        }

        #endregion

        #region Steam 进程监听

        /// <summary>启动监听。以当前 Steam 状态为基线。</summary>
        public void Start()
        {
            _steamWasRunning = IsSteamRunning();
            // 启动时 Steam 已在（如开机自启时 Steam 先于 Kitten 启动）→ 立即触发，不等 false→true
            if (_steamWasRunning)
            {
                OnTrigger();
            }
            _timer = new System.Threading.Timer(OnTick, null, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(3));
        }

        private void OnTrigger()
        {
            var app = System.Windows.Application.Current;
            try { app?.Dispatcher.Invoke(_onSteamStarted); }
            catch { /* 静默失败，不打断监听 */ }
        }

        private void OnTick(object? state)
        {
            if (_disposed) return;
            bool now = IsSteamRunning();
            if (!_steamWasRunning && now)
            {
                // Steam 进程从无到有 → 触发显示
                OnTrigger();
            }
            _steamWasRunning = now;
        }

        /// <summary>Steam 是否在运行（steam.exe 进程存在）。</summary>
        private static bool IsSteamRunning()
        {
            try
            {
                var procs = Process.GetProcessesByName("steam");
                bool running = procs.Length > 0;
                foreach (var p in procs) p.Dispose();
                return running;
            }
            catch { return false; }
        }

        #endregion

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _timer?.Dispose();
            _timer = null;
        }
    }
}
