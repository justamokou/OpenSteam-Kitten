using OpenSteamKitten.Services;
using OpenSteamKitten.Utils;
using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage = System.Windows.MessageBoxImage;
using Color = System.Windows.Media.Color;

namespace OpenSteamKitten
{
    public partial class MainWindow : Window
    {
        private readonly SteamPathService _steamPathService;
        private readonly InstallService _installService;
        private readonly LuaFileService _luaFileService;
        private readonly ProcessService _processService;
        private SimpleTrayIcon? _trayIcon;

        public MainWindow()
        {
            InitializeComponent();

            // 初始化服务
            _steamPathService = new SteamPathService();
            _installService = new InstallService(_steamPathService);
            _luaFileService = new LuaFileService(_steamPathService);
            _processService = new ProcessService(_steamPathService);

            // 初始化托盘图标
            _trayIcon = new SimpleTrayIcon(
                "OpenSteam Kitten - 双击打开 Steam",
                () => _processService.StartSteam(),
                () => Application.Current.Shutdown()
            );
        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            _trayIcon?.Dispose();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // 确保窗口在屏幕可见区域内
            EnsureWindowVisible();
        }

        private void EnsureWindowVisible()
        {
            // 获取屏幕工作区
            var workArea = SystemParameters.WorkArea;

            // 默认位置：右下角
            double targetLeft = workArea.Right - Width - 20;
            double targetTop = workArea.Bottom - Height - 20;

            // 确保不超出屏幕边界
            if (targetLeft < 0) targetLeft = 20;
            if (targetTop < 0) targetTop = 20;
            if (targetLeft + Width > workArea.Right) targetLeft = workArea.Right - Width - 20;
            if (targetTop + Height > workArea.Bottom) targetTop = workArea.Bottom - Height - 20;

            // 设置窗口位置
            Left = targetLeft;
            Top = targetTop;

            // 激活窗口，确保可见
            Activate();
            Topmost = true;

            // 检查 Steam 路径
            var steamPath = _steamPathService.GetSteamPath();
            if (string.IsNullOrEmpty(steamPath))
            {
                MessageBox.Show(
                    "未检测到 Steam 安装路径！\n请确保 Steam 已正确安装。",
                    "警告",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        // 拖拽移动窗口
        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                try
                {
                    DragMove();
                }
                catch { }
            }
        }

        // 双击打开 Steam
        private void Window_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            _processService.StartSteam();
        }

        // 拖拽进入
        private void Window_DragEnter(object sender, System.Windows.DragEventArgs e)
        {
            if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
            {
                e.Effects = System.Windows.DragDropEffects.Copy;
                // 视觉反馈：变色为浅灰
                BackgroundEllipse.Fill = new SolidColorBrush(Color.FromRgb(0x40, 0x40, 0x40));
            }
            else
            {
                e.Effects = System.Windows.DragDropEffects.None;
            }
        }

        private void Window_DragOver(object sender, System.Windows.DragEventArgs e)
        {
            if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
            {
                e.Effects = System.Windows.DragDropEffects.Copy;
            }
            else
            {
                e.Effects = System.Windows.DragDropEffects.None;
            }
            e.Handled = true;
        }

        // 拖拽释放
        private async void Window_Drop(object sender, System.Windows.DragEventArgs e)
        {
            // 恢复颜色
            BackgroundEllipse.Fill = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A));

            if (!e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
                return;

            string[] files = (string[])e.Data.GetData(System.Windows.DataFormats.FileDrop);
            bool isCtrlPressed = Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl);

            var supportedFiles = files.Where(f =>
                f.EndsWith(".lua", StringComparison.OrdinalIgnoreCase) ||
                f.EndsWith(".manifest", StringComparison.OrdinalIgnoreCase)).ToArray();

            if (supportedFiles.Length == 0)
            {
                MessageBox.Show("请拖入 .lua 或 .manifest 文件！", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (isCtrlPressed)
            {
                // Ctrl + 拖拽 = 删除
                int removedCount = 0;
                foreach (string file in supportedFiles)
                {
                    string fileName = Path.GetFileName(file);
                    if (_luaFileService.RemoveLuaFile(fileName))
                    {
                        removedCount++;
                    }
                }

                if (removedCount > 0)
                {
                    MessageBox.Show(
                        $"已移除 {removedCount} 个文件！",
                        "删除成功",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
            }
            else
            {
                // 普通拖拽 = 添加
                int addedCount = 0;
                int luaCount = 0;
                int manifestCount = 0;

                foreach (string file in supportedFiles)
                {
                    if (await _luaFileService.AddLuaFileAsync(file))
                    {
                        addedCount++;
                        if (file.EndsWith(".lua", StringComparison.OrdinalIgnoreCase))
                            luaCount++;
                        else if (file.EndsWith(".manifest", StringComparison.OrdinalIgnoreCase))
                            manifestCount++;
                    }
                }

                if (addedCount > 0)
                {
                    string message = $"添加成功：\n";
                    if (luaCount > 0)
                        message += $"- {luaCount} 个 Lua 文件 → config/lua/\n";
                    if (manifestCount > 0)
                        message += $"- {manifestCount} 个 Manifest 文件 → config/depotcache/";

                    MessageBox.Show(
                        message,
                        "添加成功",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
            }
        }

        // 右键菜单：安装 DLL
        private async void InstallDlls_Click(object sender, RoutedEventArgs e)
        {
            await _installService.InstallDllsAsync();
        }

        // 右键菜单：打开 Lua 目录
        private void OpenLuaFolder_Click(object sender, RoutedEventArgs e)
        {
            _processService.OpenLuaDirectory();
        }

        // 右键菜单：打开 Steam
        private void OpenSteam_Click(object sender, RoutedEventArgs e)
        {
            _processService.StartSteam();
        }

        // 右键菜单：关于
        private void About_Click(object sender, RoutedEventArgs e)
        {
            string steamPath = _steamPathService.GetSteamPath() ?? "未检测到";
            string installStatus = _installService.GetInstallStatus();

            MessageBox.Show(
                $"OpenSteam Kitten v1.0\n\n" +
                $"一个轻量级 GUI 壳程序，用于简化 OpenSteamTool 的使用。\n\n" +
                $"Steam 路径: {steamPath}\n" +
                $"安装状态: {installStatus}\n\n" +
                $"使用说明:\n" +
                $"• 双击悬浮窗：启动 Steam\n" +
                $"• 拖入 .lua 文件：添加到配置目录\n" +
                $"• Ctrl + 拖入：删除对应文件\n" +
                $"• 右键菜单：更多功能\n\n" +
                $"项目地址: https://github.com/OpenSteam001/OpenSteamTool",
                "关于 OpenSteam Kitten",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        // 右键菜单：退出
        private void Exit_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }
    }
}
