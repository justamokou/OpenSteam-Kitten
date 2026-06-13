using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
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
