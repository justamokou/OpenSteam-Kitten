using System;
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
        }

        // 重启后校验 pending marker：成功则静默删除，失败则提示手动下载
        private void VerifyPendingUpdate()
        {
            try
            {
                var svc = new UpdateService();
                var marker = svc.ReadPendingMarker();
                if (marker == null) return;

                var (targetShell, targetCore) = marker.Value;
                string currentShell = svc.GetCurrentVersion();
                string currentCore = svc.GetCoreVersion();

                // 当前版本已达目标 → 成功；否则视为未完成
                bool shellOk = !svc.IsNewerVersion(currentShell, targetShell);
                bool coreOk = string.IsNullOrEmpty(targetCore) || !svc.IsNewerVersion(currentCore, targetCore);

                svc.DeletePendingMarker(); // 无论成败都清掉，避免重复提示

                if (!(shellOk && coreOk))
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
                MessageBox.Show("OpenSteam Kitten 已在运行中！\n请在桌面右下角查找黑色圆形图标。",
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
