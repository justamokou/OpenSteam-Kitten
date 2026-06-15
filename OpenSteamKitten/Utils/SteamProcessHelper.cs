using System;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace OpenSteamKitten.Utils
{
    /// <summary>
    /// Steam 进程相关共享工具：文件占用检测 + Steam 进程终止。
    /// 供 InstallService / CleanupService 复用，避免逻辑重复。
    /// </summary>
    internal static class SteamProcessHelper
    {
        /// <summary>文件是否被占用（无法以独占方式打开即视为占用）。</summary>
        public static bool IsFileLocked(string filePath)
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

        /// <summary>终止所有 Steam 相关进程（steam / steamservice / steamwebhelper）。</summary>
        public static bool TerminateSteamProcesses()
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
