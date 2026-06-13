using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using MessageBox = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage = System.Windows.MessageBoxImage;
using MessageBoxResult = System.Windows.MessageBoxResult;

namespace OpenSteamKitten.Services
{
    public class InstallService
    {
        private readonly SteamPathService _steamPathService;
        private static readonly string[] DllNames = { "OpenSteamTool.dll", "dwmapi.dll", "xinput1_4.dll" };

        public InstallService(SteamPathService steamPathService)
        {
            _steamPathService = steamPathService;
        }

        public async Task<bool> InstallDllsAsync()
        {
            string? steamPath = _steamPathService.GetSteamPath();
            if (string.IsNullOrEmpty(steamPath))
            {
                MessageBox.Show("未找到 Steam 安装路径！\n请确保 Steam 已正确安装。",
                    "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }

            try
            {
                // 预先检查所有文件状态
                var lockedFiles = new System.Collections.Generic.List<string>();
                var existingFiles = new System.Collections.Generic.List<string>();
                var missingSourceFiles = new System.Collections.Generic.List<string>();

                foreach (var dllName in DllNames)
                {
                    string source = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "dlls", dllName);
                    string dest = Path.Combine(steamPath, dllName);

                    if (!File.Exists(source))
                    {
                        missingSourceFiles.Add(dllName);
                        continue;
                    }

                    if (File.Exists(dest))
                    {
                        existingFiles.Add(dllName);
                        if (IsFileLocked(dest))
                        {
                            lockedFiles.Add(dllName);
                        }
                    }
                }

                // 检查是否有缺失的源文件
                if (missingSourceFiles.Count > 0)
                {
                    MessageBox.Show($"找不到以下源文件:\n{string.Join("\n", missingSourceFiles)}\n\n" +
                        $"BaseDirectory: {AppDomain.CurrentDomain.BaseDirectory}\n" +
                        $"请确保程序文件完整。",
                        "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    return false;
                }

                // 如果有文件被占用，询问是否关闭 Steam
                if (lockedFiles.Count > 0)
                {
                    var lockedResult = MessageBox.Show(
                        $"以下文件正在被占用(可能 Steam 正在运行):\n{string.Join("\n", lockedFiles)}\n\n是否尝试关闭 Steam 进程后继续安装？",
                        "文件被占用",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning);

                    if (lockedResult == MessageBoxResult.Yes)
                    {
                        if (!TerminateSteamProcesses())
                        {
                            MessageBox.Show("无法关闭 Steam 进程。\n请手动关闭 Steam 后重试。",
                                "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                            return false;
                        }
                        // 等待进程完全退出
                        await Task.Delay(1000);
                    }
                    else
                    {
                        return false;
                    }
                }

                // 如果有文件已存在，统一询问是否覆盖
                if (existingFiles.Count > 0)
                {
                    var result = MessageBox.Show(
                        $"以下文件已存在于 Steam 目录:\n{string.Join("\n", existingFiles)}\n\n是否覆盖？",
                        "确认覆盖",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                    if (result == MessageBoxResult.No)
                    {
                        MessageBox.Show("已取消安装。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                        return false;
                    }
                }

                // 执行安装
                var installedFiles = new System.Collections.Generic.List<string>();
                var failedFiles = new System.Collections.Generic.List<string>();

                foreach (var dllName in DllNames)
                {
                    try
                    {
                        string source = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "dlls", dllName);
                        string dest = Path.Combine(steamPath, dllName);

                        await Task.Run(() => File.Copy(source, dest, overwrite: true));
                        installedFiles.Add(dllName);
                    }
                    catch (Exception ex)
                    {
                        failedFiles.Add($"{dllName} ({ex.Message})");
                    }
                }

                // 显示安装结果
                if (installedFiles.Count > 0)
                {
                    string message = $"成功安装 {installedFiles.Count} 个文件:\n{string.Join("\n", installedFiles)}";

                    if (failedFiles.Count > 0)
                    {
                        message += $"\n\n安装失败 {failedFiles.Count} 个文件:\n{string.Join("\n", failedFiles)}";
                    }

                    message += "\n\n请重启 Steam 以使更改生效。";

                    MessageBox.Show(message, "安装完成", MessageBoxButton.OK,
                        failedFiles.Count > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
                    return true;
                }
                else
                {
                    MessageBox.Show($"安装失败:\n{string.Join("\n", failedFiles)}",
                        "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    return false;
                }
            }
            catch (UnauthorizedAccessException ex)
            {
                MessageBox.Show($"安装失败: 权限不足\n{ex.Message}\n\n请尝试以管理员身份运行本程序。",
                    "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
            catch (IOException ex)
            {
                MessageBox.Show($"安装失败: {ex.Message}\n\n文件可能正在被占用，请关闭 Steam 后重试。",
                    "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"安装失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        public bool IsInstalled()
        {
            string? steamPath = _steamPathService.GetSteamPath();
            if (string.IsNullOrEmpty(steamPath))
                return false;

            return File.Exists(Path.Combine(steamPath, "OpenSteamTool.dll"));
        }

        public string GetInstallStatus()
        {
            if (IsInstalled())
                return "已安装";
            return "未安装";
        }

        private bool IsFileLocked(string filePath)
        {
            try
            {
                using (FileStream stream = File.Open(filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    stream.Close();
                }
                return false;
            }
            catch (IOException)
            {
                return true;
            }
            catch
            {
                return false;
            }
        }

        private bool TerminateSteamProcesses()
        {
            try
            {
                var steamProcesses = Process.GetProcessesByName("steam");
                var steamServiceProcesses = Process.GetProcessesByName("steamservice");
                var steamWebHelperProcesses = Process.GetProcessesByName("steamwebhelper");

                var allProcesses = steamProcesses
                    .Concat(steamServiceProcesses)
                    .Concat(steamWebHelperProcesses)
                    .ToArray();

                if (allProcesses.Length == 0)
                    return true;

                foreach (var process in allProcesses)
                {
                    try
                    {
                        process.Kill();
                        process.WaitForExit(3000);
                    }
                    catch
                    {
                        // 忽略单个进程终止失败
                    }
                    finally
                    {
                        process.Dispose();
                    }
                }

                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
