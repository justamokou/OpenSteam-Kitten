using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using OpenSteamKitten.Services;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage = System.Windows.MessageBoxImage;

namespace OpenSteamKitten
{
    public partial class App : Application
    {
        private static Mutex? _mutex;
        private const string MutexName = "OpenSteamKitten_SingleInstance";
        private MainWindow? _mainWindow;

        // Windows API 用于激活已存在的窗口
        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern IntPtr FindWindow(string? lpClassName, string lpWindowName);

        private const int SW_RESTORE = 9;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // 尝试创建 Mutex
            _mutex = new Mutex(true, MutexName, out bool createdNew);

            if (!createdNew)
            {
                // 已有实例在运行，尝试激活现有窗口
                ActivateExistingWindow();
                Shutdown();
                return;
            }

            // 保持 Mutex 直到应用退出
            GC.KeepAlive(_mutex);

            // 校验上次更新是否成功（壳更新重启后）
            VerifyPendingUpdate();

            // 手动创建主窗口（App.xaml 已移除 StartupUri，按启动模式决定是否显示）
            bool steamWatch = e.Args != null &&
                e.Args.Contains("--steamwatch", StringComparer.OrdinalIgnoreCase);

            _mainWindow = new MainWindow();
            if (steamWatch)
            {
                // 开机自启（--steamwatch）：静默监听，不显示窗口，等 Steam 出现再弹出
                _mainWindow.StartSteamWatch(silent: true);
            }
            else
            {
                _mainWindow.Show();
                // 正常启动：若用户已开启「随 Steam 启动」，也监听（配合隐藏后随 Steam 弹出）
                if (SteamLaunchService.IsEnabled())
                {
                    _mainWindow.StartSteamWatch();
                }
            }
        }

        // 重启后校验 pending marker：成功则静默删除，失败则提示手动下载
        private void VerifyPendingUpdate()
        {
            try
            {
                var svc = new UpdateService();
                var marker = svc.ReadPendingMarker();
                if (marker == null) return;

                var (targetShell, targetCore, manifestJson) = marker.Value;
                string currentShell = svc.GetCurrentVersion();
                string currentCore = svc.GetCoreVersion();

                // 当前版本已达目标 → 成功；否则视为未完成
                bool shellOk = !svc.IsNewerVersion(currentShell, targetShell);
                bool coreOk = string.IsNullOrEmpty(targetCore) || !svc.IsNewerVersion(currentCore, targetCore);
                bool filesOk = svc.LocalFilesMatchManifest(manifestJson);

                svc.DeletePendingMarker(); // 无论成败都清掉，避免重复提示

                if (!(shellOk && coreOk && filesOk))
                {
                    MessageBox.Show(
                        "上次更新未完成，可能是替换文件失败。\n\n" +
                        "请手动前往下载最新版本：\n" +
                        "https://github.com/justamokou/OpenSteam-Kitten/releases",
                        "更新未完成", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch { }
        }

        private void ActivateExistingWindow()
        {
            // 尝试找到已存在的窗口并激活
            IntPtr hWnd = FindWindow(null, "OpenSteam Kitten");
            if (hWnd != IntPtr.Zero)
            {
                ShowWindow(hWnd, SW_RESTORE);
                SetForegroundWindow(hWnd);
            }
            else
            {
                MessageBox.Show("OpenSteam Kitten 已在后台运行（随 Steam 启动模式）。\n请右键右下角托盘的小猫图标 → 「显示悬浮窗」。",
                    "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _mutex?.ReleaseMutex();
            _mutex?.Dispose();
            base.OnExit(e);
        }
    }
}
