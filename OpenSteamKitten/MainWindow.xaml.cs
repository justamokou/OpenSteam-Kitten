using OpenSteamKitten.Services;
using OpenSteamKitten.Utils;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage = System.Windows.MessageBoxImage;
using MessageBoxResult = System.Windows.MessageBoxResult;
using Color = System.Windows.Media.Color;

namespace OpenSteamKitten
{
    public partial class MainWindow : Window
    {
        private readonly SteamPathService _steamPathService;
        private readonly InstallService _installService;
        private readonly LuaFileService _luaFileService;
        private readonly ProcessService _processService;
        private readonly UpdateService _updateService;
        private readonly CancellationTokenSource _updateCts = new CancellationTokenSource();
        private SimpleTrayIcon? _trayIcon;

        public MainWindow()
        {
            InitializeComponent();

            // 初始化服务
            _steamPathService = new SteamPathService();
            _installService = new InstallService(_steamPathService);
            _luaFileService = new LuaFileService(_steamPathService);
            _processService = new ProcessService(_steamPathService);
            _updateService = new UpdateService();

            // 初始化托盘图标
            _trayIcon = new SimpleTrayIcon(
                "OpenSteam Kitten - 双击打开 Steam",
                () => _processService.StartSteam(),
                () => Application.Current.Shutdown()
            );
        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            _updateCts.Cancel();
            _trayIcon?.Dispose();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // 确保窗口在屏幕可见区域内
            EnsureWindowVisible();

            // 启动时静默检查更新（fire-and-forget，永不抛出）
            _ = SilentUpdateCheckAsync(isManual: false);
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

        // 右键菜单：检查更新
        private async void CheckUpdate_Click(object sender, RoutedEventArgs e)
        {
            await SilentUpdateCheckAsync(isManual: true);
        }

        // 启动静默检查 / 手动检查共用。isManual=true 时每个分支都弹 UI。
        private async Task SilentUpdateCheckAsync(bool isManual)
        {
            UpdateCheckResult info;
            try
            {
                info = await _updateService.CheckForUpdatesAsync();
            }
            catch (Exception ex)
            {
                if (isManual)
                    MessageBox.Show($"检查更新时出错：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // 检查本身失败（网络/解析）：静默路径不打扰
            if (info.ErrorMessage != null)
            {
                if (isManual)
                    MessageBox.Show($"检查更新失败：{info.ErrorMessage}", "检查更新", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 已是最新：静默路径不打扰
            if (!info.AnyUpdateAvailable)
            {
                if (isManual)
                    MessageBox.Show($"已是最新版本 🎉\n\n小猫 v{info.CurrentShell} / 内核 {info.CurrentCore}",
                        "检查更新", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // 有更新：列出壳/内核各自 当前→最新
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("发现可用更新：\n");
            sb.AppendLine(info.ShellUpdateAvailable
                ? $"• 小猫：v{info.CurrentShell} → v{info.LatestShell}"
                : $"• 小猫：v{info.CurrentShell}（已是最新）");
            sb.AppendLine(info.CoreUpdateAvailable
                ? $"• 内核：{info.CurrentCore} → {info.LatestCore}"
                : $"• 内核：{info.CurrentCore}（已是最新）");
            sb.AppendLine("\n是否立即更新？");

            var choice = MessageBox.Show(sb.ToString(), "发现新版本", MessageBoxButton.YesNo, MessageBoxImage.Information);
            if (choice != MessageBoxResult.Yes) return;

            try
            {
                var apply = await _updateService.ApplyUpdateAsync(info, null, _updateCts.Token);
                await HandleApplyResultAsync(info, apply);
            }
            catch (Exception ex)
            {
                // 用户中途退出（取消）时不弹错误框
                if (isManual && !_updateCts.IsCancellationRequested)
                    MessageBox.Show($"更新失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // 处理应用更新结果
        private async Task HandleApplyResultAsync(UpdateCheckResult info, UpdateApplyResult apply)
        {
            switch (apply.Outcome)
            {
                case UpdateApplyResult.OutcomeKind.SuccessRestartFree:
                    // 仅内核更新：已原地完成，提示后全自动应用到 Steam
                    MessageBox.Show($"{apply.Message}", "更新成功", MessageBoxButton.OK, MessageBoxImage.Information);
                    try { await _installService.InstallDllsAsync(); }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"应用到 Steam 失败：{ex.Message}\n可稍后点击「一键安装 DLL」重试。",
                            "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                    break;

                case UpdateApplyResult.OutcomeKind.SuccessRestartNeeded:
                    // 壳有更新：文件已就位。若内核也更新了，先把新 DLL 推到 Steam，再重启替换 exe
                    if (info.CoreUpdateAvailable)
                    {
                        try { await _installService.InstallDllsAsync(); }
                        catch { /* 推 Steam 失败不阻断重启 */ }
                    }
                    _trayIcon?.Dispose();
                    MessageBox.Show("更新已下载，程序即将重启以完成更新。", "重启更新",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    try
                    {
                        var psi = new ProcessStartInfo
                        {
                            FileName = apply.Message, // update.bat 路径
                            WindowStyle = ProcessWindowStyle.Hidden,
                            CreateNoWindow = true,
                            UseShellExecute = false,
                            WorkingDirectory = Path.GetDirectoryName(apply.Message)
                        };
                        Process.Start(psi);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"无法启动更新脚本：{ex.Message}\n\n请手动前往下载：\n{info.ReleaseHtmlUrl}",
                            "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }
                    Application.Current.Shutdown();
                    break;

                case UpdateApplyResult.OutcomeKind.FailedNeedsManual:
                    _updateService.DeletePendingMarker();
                    // 用户主动取消（退出）时不弹"更新失败"，避免打扰
                    if (!_updateCts.IsCancellationRequested)
                    {
                        MessageBox.Show($"更新失败：{apply.Message}\n\n请手动前往下载：\n{info.ReleaseHtmlUrl}",
                            "更新失败", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                    break;
            }
        }

        // 右键菜单：关于
        private void About_Click(object sender, RoutedEventArgs e)
        {
            string steamPath = _steamPathService.GetSteamPath() ?? "未检测到";
            string installStatus = _installService.GetInstallStatus();
            string kittenVersion = _updateService.GetCurrentVersion();
            string coreVersion = _updateService.GetCoreVersion();

            MessageBox.Show(
                $"OpenSteam Kitten v{kittenVersion}\n\n" +
                $"一个轻量级 GUI 壳程序，用于简化 OpenSteamTool 的使用。\n\n" +
                $"版本信息:\n" +
                $"• 小猫版本: v{kittenVersion}\n" +
                $"• 内核版本: {coreVersion}\n\n" +
                $"Steam 路径: {steamPath}\n" +
                $"安装状态: {installStatus}\n\n" +
                $"使用说明:\n" +
                $"• 双击悬浮窗：启动 Steam\n" +
                $"• 拖入 .lua / .manifest 文件：添加到配置目录\n" +
                $"• Ctrl + 拖入：删除对应文件\n" +
                $"• 右键菜单：更多功能\n\n" +
                $"项目地址:\n" +
                $"OpenSteam Kitten: https://github.com/justamokou/OpenSteam-Kitten\n" +
                $"OpenSteamTool: https://github.com/OpenSteam001/OpenSteamTool",
                "关于 OpenSteam Kitten",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        // 右键菜单：退出
        private void Exit_Click(object sender, RoutedEventArgs e)
        {
            _updateCts.Cancel();
            Application.Current.Shutdown();
        }
    }
}
