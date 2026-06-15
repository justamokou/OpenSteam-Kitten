using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OpenSteamKitten.Utils;
using MessageBox = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage = System.Windows.MessageBoxImage;
using MessageBoxResult = System.Windows.MessageBoxResult;

namespace OpenSteamKitten.Services
{
    /// <summary>
    /// 清理 OpenSteamTool 写入 Steam 目录的产物。支持按类别独立清理：
    ///   Dll      — 3 个内核 DLL（删除前需关闭 Steam，文件被占用）。
    ///   Lua      — config/lua/*.lua。
    ///   Manifest — config/depotcache/*.manifest（仅 .manifest，不动 .bin 等 Steam 缓存）。
    /// 对称反向操作——只删「安装路径」曾写入的东西，绝不越界删 Steam 自身文件。
    /// 提示流程仿 InstallService：占用提示（仅 Dll）→ 确认 → 执行 → 结果。
    /// </summary>
    public class CleanupService
    {
        private readonly SteamPathService _steamPathService;

        public CleanupService(SteamPathService steamPathService)
        {
            _steamPathService = steamPathService;
        }

        public async Task CleanupAsync(CleanupScope scope)
        {
            if (scope == CleanupScope.None) return;

            string? steamPath = _steamPathService.GetSteamPath();
            if (string.IsNullOrEmpty(steamPath))
            {
                MessageBox.Show("未找到 Steam 安装路径！\n请确保 Steam 已正确安装。",
                    "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var counts = CountTargets(steamPath, scope);
            if (counts.Total == 0)
            {
                MessageBox.Show("已是干净状态，没有需要清理的内容。", "清理",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // 仅清理 DLL 时需关闭 Steam（DLL 被 Steam 进程占用）；lua/manifest 不被占用，可在线删
            if (scope.HasFlag(CleanupScope.Dll))
            {
                var lockedFiles = new List<string>();
                foreach (var dll in OpenSteamToolFiles.KernelDlls)
                {
                    string p = Path.Combine(steamPath, dll);
                    if (File.Exists(p) && SteamProcessHelper.IsFileLocked(p))
                        lockedFiles.Add(dll);
                }

                if (lockedFiles.Count > 0)
                {
                    var lockedResult = MessageBox.Show(
                        "检测到 Steam 正在运行，清理内核 DLL 需要先关闭 Steam。\n\n是否关闭 Steam 后继续？",
                        "Steam 正在运行", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                    if (lockedResult != MessageBoxResult.Yes) return;

                    if (!SteamProcessHelper.TerminateSteamProcesses())
                    {
                        MessageBox.Show("无法关闭 Steam 进程。\n请手动关闭 Steam 后重试。",
                            "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }
                    await Task.Delay(1000); // 等待进程完全退出、文件句柄释放
                }
            }

            // 确认框：仅列出本次范围内的数量 + 风险说明
            var confirmSb = new StringBuilder();
            confirmSb.AppendLine("确认清理以下内容？\n");
            if (scope.HasFlag(CleanupScope.Dll))      confirmSb.AppendLine($"• 内核 DLL：{counts.Dll} 个");
            if (scope.HasFlag(CleanupScope.Lua))      confirmSb.AppendLine($"• Lua 配置：{counts.Lua} 个");
            if (scope.HasFlag(CleanupScope.Manifest)) confirmSb.AppendLine($"• Manifest 清单：{counts.Manifest} 个");
            confirmSb.AppendLine();
            confirmSb.AppendLine("将删除 OpenSteamTool 注入 Steam 的相应文件。");
            if (scope.HasFlag(CleanupScope.Manifest))
                confirmSb.AppendLine("仅删除 .manifest 文件，不影响 Steam 自身的其它缓存。");
            if (scope.HasFlag(CleanupScope.Dll))
                confirmSb.AppendLine("\n删除后请重启 Steam 使更改生效。");
            else
                confirmSb.AppendLine("\n无需重启 Steam，删除立即生效。");

            var confirm = MessageBox.Show(confirmSb.ToString(), "确认清理",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes)
            {
                MessageBox.Show("已取消清理。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // 执行删除
            var deletedDlls = new List<string>();
            var failed = new List<string>();
            int deletedLua = 0, deletedManifest = 0;

            if (scope.HasFlag(CleanupScope.Dll))
            {
                foreach (var dll in OpenSteamToolFiles.KernelDlls)
                {
                    string p = Path.Combine(steamPath, dll);
                    if (!File.Exists(p)) continue;
                    try { await Task.Run(() => File.Delete(p)); deletedDlls.Add(dll); }
                    catch (Exception ex) { failed.Add($"{dll} ({ex.Message})"); }
                }
            }
            if (scope.HasFlag(CleanupScope.Lua))
                await DeleteByPatternAsync(Path.Combine(steamPath, "config", "lua"), "*.lua", () => deletedLua++, failed);
            if (scope.HasFlag(CleanupScope.Manifest))
                await DeleteByPatternAsync(Path.Combine(steamPath, "config", "depotcache"), "*.manifest", () => deletedManifest++, failed);

            // 结果框
            var msg = new StringBuilder();
            msg.AppendLine("清理完成：");
            if (scope.HasFlag(CleanupScope.Dll))      msg.AppendLine($"• 内核 DLL：{deletedDlls.Count} 个");
            if (scope.HasFlag(CleanupScope.Lua))      msg.AppendLine($"• Lua 配置：{deletedLua} 个");
            if (scope.HasFlag(CleanupScope.Manifest)) msg.AppendLine($"• Manifest 清单：{deletedManifest} 个");
            if (failed.Count > 0)
                msg.AppendLine($"\n清理失败 {failed.Count} 项：\n{string.Join("\n", failed)}");
            if (scope.HasFlag(CleanupScope.Dll))
                msg.AppendLine("\n请重启 Steam 使更改生效。");
            else
                msg.AppendLine("\n无需重启 Steam，已立即生效。");

            MessageBox.Show(msg.ToString(), "清理完成", MessageBoxButton.OK,
                failed.Count > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
        }

        /// <summary>统计范围内各类待删文件数量（不删除）。</summary>
        private static CleanupCounts CountTargets(string steamPath, CleanupScope scope)
        {
            var c = new CleanupCounts();
            if (scope.HasFlag(CleanupScope.Dll))
                foreach (var dll in OpenSteamToolFiles.KernelDlls)
                    if (File.Exists(Path.Combine(steamPath, dll))) c.Dll++;

            if (scope.HasFlag(CleanupScope.Lua))
            {
                string luaDir = Path.Combine(steamPath, "config", "lua");
                try { if (Directory.Exists(luaDir)) c.Lua = Directory.EnumerateFiles(luaDir, "*.lua").Count(); }
                catch { /* 目录读取失败按 0 处理 */ }
            }

            if (scope.HasFlag(CleanupScope.Manifest))
            {
                string depotDir = Path.Combine(steamPath, "config", "depotcache");
                try { if (Directory.Exists(depotDir)) c.Manifest = Directory.EnumerateFiles(depotDir, "*.manifest").Count(); }
                catch { /* 同上 */ }
            }
            return c;
        }

        /// <summary>删除 dir 下匹配 pattern 的文件；每删一个调用 onDeleted，失败收集到 failed。</summary>
        private static async Task DeleteByPatternAsync(string dir, string pattern, Action onDeleted, List<string> failed)
        {
            string[] files;
            try { files = Directory.Exists(dir) ? Directory.EnumerateFiles(dir, pattern).ToArray() : Array.Empty<string>(); }
            catch { return; }

            foreach (var f in files)
            {
                try { await Task.Run(() => File.Delete(f)); onDeleted(); }
                catch (Exception ex) { failed.Add($"{Path.GetFileName(f)} ({ex.Message})"); }
            }
        }

        public class CleanupCounts
        {
            public int Dll;
            public int Lua;
            public int Manifest;
            public int Total => Dll + Lua + Manifest;
        }
    }

    /// <summary>清理范围（可组合；菜单三个按钮各传单一值）。</summary>
    [System.Flags]
    public enum CleanupScope
    {
        None = 0,
        Dll = 1,
        Lua = 2,
        Manifest = 4,
    }
}
