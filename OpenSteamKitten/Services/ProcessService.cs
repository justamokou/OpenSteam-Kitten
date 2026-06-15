using System;
using System.Diagnostics;
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
    public class ProcessService
    {
        private readonly SteamPathService _steamPathService;

        public ProcessService(SteamPathService steamPathService)
        {
            _steamPathService = steamPathService;
        }

        public void StartSteam()
        {
            try
            {
                string? steamPath = _steamPathService.GetSteamPath();
                if (string.IsNullOrEmpty(steamPath))
                {
                    MessageBox.Show("未找到 Steam 安装路径！", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                string steamExe = Path.Combine(steamPath, "steam.exe");
                if (!File.Exists(steamExe))
                {
                    MessageBox.Show("未找到 steam.exe！", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                Process.Start(new ProcessStartInfo
                {
                    FileName = steamExe,
                    UseShellExecute = true,
                    WorkingDirectory = steamPath
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"启动 Steam 失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>重启 Steam：关闭现有 Steam 进程，等待退出后重新启动。</summary>
        public async Task RestartSteamAsync()
        {
            SteamProcessHelper.TerminateSteamProcesses(); // 关闭现有 Steam
            await Task.Delay(1000);                        // 等进程退出、文件句柄释放
            StartSteam();                                  // 重新启动
        }

        public void OpenLuaDirectory()
        {
            try
            {
                string luaDir = _steamPathService.GetLuaDirectory();

                if (!Directory.Exists(luaDir))
                {
                    var result = MessageBox.Show(
                        $"Lua 目录不存在:\n{luaDir}\n\n是否创建该目录？",
                        "目录不存在",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                    if (result == MessageBoxResult.Yes)
                    {
                        Directory.CreateDirectory(luaDir);
                    }
                    else
                    {
                        return;
                    }
                }

                Process.Start(new ProcessStartInfo
                {
                    FileName = luaDir,
                    UseShellExecute = true,
                    Verb = "open"
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"打开目录失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
