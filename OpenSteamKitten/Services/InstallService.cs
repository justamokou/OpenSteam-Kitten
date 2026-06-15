using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using OpenSteamKitten.Utils;
using MessageBox = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage = System.Windows.MessageBoxImage;
using MessageBoxResult = System.Windows.MessageBoxResult;

namespace OpenSteamKitten.Services
{
    public class InstallService
    {
        private readonly SteamPathService _steamPathService;

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
                var lockedFiles = new List<string>();
                var existingFiles = new List<string>();
                var missingSourceFiles = new List<string>();

                foreach (var dllName in OpenSteamToolFiles.KernelDlls)
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
                        if (SteamProcessHelper.IsFileLocked(dest))
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
                        if (!SteamProcessHelper.TerminateSteamProcesses())
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
                var installedFiles = new List<string>();
                var failedFiles = new List<string>();

                foreach (var dllName in OpenSteamToolFiles.KernelDlls)
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
    }
}
