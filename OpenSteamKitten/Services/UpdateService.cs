using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace OpenSteamKitten.Services
{
    /// <summary>壳/内核两条线的检查结果（纯数据，不含 UI）。</summary>
    public sealed class UpdateCheckResult
    {
        public bool ShellUpdateAvailable { get; set; }
        public bool CoreUpdateAvailable { get; set; }
        public string CurrentShell { get; set; } = "";
        public string LatestShell { get; set; } = "";
        public string CurrentCore { get; set; } = "";
        public string LatestCore { get; set; } = "";
        public string? ZipDownloadUrl { get; set; }
        public string? PackageSha256 { get; set; }
        public string? RemoteManifestJson { get; set; }
        public string ReleaseHtmlUrl { get; set; } = "";
        public string ReleaseName { get; set; } = "";
        public string ReleaseNotes { get; set; } = "";
        public bool ShellContentChanged { get; set; }
        public bool CoreContentChanged { get; set; }
        /// <summary>非 null 表示检查本身失败（网络/解析），调用方自行决定是否提示。</summary>
        public string? ErrorMessage { get; set; }

        public bool AnyUpdateAvailable => ShellUpdateAvailable || CoreUpdateAvailable;
    }

    /// <summary>应用更新的结果。</summary>
    public sealed class UpdateApplyResult
    {
        public enum OutcomeKind
        {
            /// <summary>已原地完成（仅内核，免重启）。Message = 摘要。</summary>
            SuccessRestartFree,
            /// <summary>文件已就位，需要重启替换 exe。Message = update.bat 的绝对路径。</summary>
            SuccessRestartNeeded,
            /// <summary>应用失败。Message = 失败原因（应引导用户手动下载）。</summary>
            FailedNeedsManual
        }

        public OutcomeKind Outcome { get; set; }
        public string Message { get; set; } = "";
    }

    public class UpdateService
    {
        // 不走 api.github.com（共享代理出口 IP 极易触发 60/小时限流）。
        // 用 releases.atom（网页）+ releases/download（CDN 资产），都不受 API 限流影响。
        private const string REPO_URL = "https://github.com/justamokou/OpenSteam-Kitten";
        private const string RELEASES_PAGE_URL = REPO_URL + "/releases";
        private const string ATOM_FEED_URL = REPO_URL + "/releases.atom";
        private const string ZIP_NAME_PREFIX = "OpenSteamKitten-";
        private const string ZIP_NAME_SUFFIX = "-Release.zip";

        // 与 InstallService 保持一致的 3 个核心 DLL
        private static readonly string[] CoreDllNames = { "OpenSteamTool.dll", "dwmapi.dll", "xinput1_4.dll" };
        private sealed class UpdateManifest
        {
            public int Schema { get; set; }
            public string Shell { get; set; } = "";
            public string Core { get; set; } = "";
            public PackageDigest? Package { get; set; }
            public Dictionary<string, FileDigest> Files { get; set; } = new Dictionary<string, FileDigest>(StringComparer.OrdinalIgnoreCase);
        }

        private sealed class PackageDigest
        {
            public string Name { get; set; } = "";
            public string Sha256 { get; set; } = "";
            public long Size { get; set; }
        }

        private sealed class FileDigest
        {
            public string Sha256 { get; set; } = "";
            public long Size { get; set; }
        }

        private static readonly JsonSerializerOptions ManifestJsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        private static readonly HttpClient _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(8)
        };

        static UpdateService()
        {
            // GitHub API 要求 User-Agent
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "OpenSteamKitten");
        }

        /// <summary>真实 exe 所在目录。单文件发布下比 AppDomain.BaseDirectory 更可靠。</summary>
        private static string AppDir =>
            Path.GetDirectoryName(Environment.ProcessPath) ?? AppDomain.CurrentDomain.BaseDirectory;

        private static string ShellVersionPath => Path.Combine(AppDir, "VERSION");
        private static string CoreVersionPath => Path.Combine(AppDir, "Resources", "dlls", "VERSION.txt");
        private static string ManifestPath => Path.Combine(AppDir, "version.json");
        private static string PendingMarkerPath => Path.Combine(AppDir, "update_pending.json");
        private static string NewExePath => Path.Combine(AppDir, "OpenSteamKitten.exe.new");
        private static string ExePath => Path.Combine(AppDir, "OpenSteamKitten.exe");
        private static string SwapBatchPath => Path.Combine(AppDir, "update.bat");
        private static string CoreDllDir => Path.Combine(AppDir, "Resources", "dlls");

        public string GetCurrentVersion()
        {
            try
            {
                if (File.Exists(ShellVersionPath))
                    return File.ReadAllText(ShellVersionPath).Trim();
            }
            catch { }
            return "1.1.0";
        }

        public string GetCoreVersion()
        {
            try
            {
                if (File.Exists(CoreVersionPath))
                    return File.ReadAllText(CoreVersionPath).Trim();
            }
            catch { }
            return "未知";
        }

        /// <summary>静默检查壳 + 内核两条线，返回纯数据；任何失败都写入 ErrorMessage，不弹窗。</summary>
        public async Task<UpdateCheckResult> CheckForUpdatesAsync()
        {
            var result = new UpdateCheckResult
            {
                CurrentShell = GetCurrentVersion(),
                CurrentCore = GetCoreVersion()
            };

            try
            {
                // 1) 拉 Atom feed（网页，不受 api.github.com 的 60/小时限流影响）
                string feedXml;
                using (var resp = await _httpClient.GetAsync(ATOM_FEED_URL))
                {
                    if (!resp.IsSuccessStatusCode)
                    {
                        result.ErrorMessage = resp.StatusCode == System.Net.HttpStatusCode.Forbidden
                            ? "GitHub 访问受限（可能是代理出口 IP 被限流），请稍后重试"
                            : $"获取发布信息失败 (HTTP {(int)resp.StatusCode})";
                        return result;
                    }
                    feedXml = await resp.Content.ReadAsStringAsync();
                }

                // 2) 解析最新 entry 的 tag + 标题
                if (!TryParseLatestRelease(feedXml, out string rawTag, out string title))
                {
                    result.ErrorMessage = "无法解析发布信息（仓库可能还没有 release）";
                    return result;
                }

                result.ReleaseName = title;
                result.LatestShell = rawTag.TrimStart('v'); // 剥 v 前缀
                result.ReleaseHtmlUrl = $"{REPO_URL}/releases/tag/{rawTag}";

                // 3) 探测该 release 的真实资产列表（expanded_assets，网页端点，不受 API 限流）。
                //    历史命名不统一（-Release.zip / -final.zip / 无后缀都有），不硬编码文件名。
                var assets = await FetchAssetUrls(rawTag);

                // version.json（如果该 release 带）
                string? versionJsonUrl = null;
                foreach (var kv in assets)
                {
                    if (kv.Key.Equals("version.json", StringComparison.OrdinalIgnoreCase))
                    {
                        versionJsonUrl = kv.Value;
                        break;
                    }
                }
                var remoteManifest = versionJsonUrl != null ? await TryFetchManifestFromUrl(versionJsonUrl) : null;
                if (remoteManifest != null)
                {
                    result.RemoteManifestJson = remoteManifest.Value.RawJson;
                    result.PackageSha256 = remoteManifest.Value.Manifest.Package?.Sha256;
                }

                string? manifestShell = remoteManifest?.Manifest.Shell;
                string? manifestCore = remoteManifest?.Manifest.Core;
                result.LatestShell = !string.IsNullOrWhiteSpace(manifestShell)
                                    ? manifestShell.TrimStart('v')
                                    : result.LatestShell;
                result.LatestCore = !string.IsNullOrWhiteSpace(manifestCore)
                                    ? manifestCore.TrimStart('v')
                                    : ParseCoreFromReleaseName(title) ?? result.CurrentCore;

                // 4) zip 下载地址：从资产列表里挑真实的（优先 -Release.zip），都没有则回退构造命名
                result.ZipDownloadUrl = PickZipUrl(assets)
                                        ?? $"{REPO_URL}/releases/download/{rawTag}/{ZIP_NAME_PREFIX}{result.LatestShell}{ZIP_NAME_SUFFIX}";

                result.ShellUpdateAvailable = IsNewerVersion(result.CurrentShell, result.LatestShell);
                result.CoreUpdateAvailable = IsNewerVersion(result.CurrentCore, result.LatestCore);

                if (remoteManifest != null)
                {
                    var manifest = remoteManifest.Value.Manifest;
                    if (!result.ShellUpdateAvailable && IsSameVersion(result.CurrentShell, result.LatestShell) &&
                        HasManifestMismatch(manifest, IsShellFile))
                    {
                        result.ShellContentChanged = true;
                        result.ShellUpdateAvailable = true;
                    }

                    if (!result.CoreUpdateAvailable && IsSameVersion(result.CurrentCore, result.LatestCore) &&
                        HasManifestMismatch(manifest, IsCoreFile))
                    {
                        result.CoreContentChanged = true;
                        result.CoreUpdateAvailable = true;
                    }
                }
            }
            catch (Exception ex)
            {
                result.ErrorMessage = (ex is HttpRequestException || ex is TaskCanceledException)
                    ? "无法连接 GitHub（请检查网络/代理）"
                    : ex.Message;
            }

            return result;
        }

        /// <summary>
        /// 从 Atom feed 挑语义版本号最高的 release（GitHub 的 releases.atom 排序不可靠，
        /// 第一个 entry 未必是最新版，必须遍历后取最大版本）。
        /// </summary>
        private bool TryParseLatestRelease(string feedXml, out string rawTag, out string title)
        {
            rawTag = "";
            title = "";
            try
            {
                var doc = XDocument.Parse(feedXml);
                var atom = (XNamespace)"http://www.w3.org/2005/Atom";
                if (doc.Root == null) return false;

                foreach (var entry in doc.Root.Elements(atom + "entry"))
                {
                    // id 形如 "tag:github.com,2008:Repository/<repoid>/<TAG>"，最后一段就是 tag
                    string id = entry.Element(atom + "id")?.Value ?? "";
                    var parts = id.Split('/');
                    string tag = parts.Length > 0 ? parts[^1] : "";
                    if (string.IsNullOrEmpty(tag)) continue;

                    string shell = tag.TrimStart('v');
                    if (!IsParsableVersion(shell)) continue; // 跳过非数字版本（如 *-beta、latest）

                    // 比"当前已知最新"还新就更新
                    if (string.IsNullOrEmpty(rawTag) || IsNewerVersion(rawTag.TrimStart('v'), shell))
                    {
                        rawTag = tag;
                        title = entry.Element(atom + "title")?.Value ?? "";
                    }
                }
                return !string.IsNullOrEmpty(rawTag);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>判断字符串是否是纯数字版本号（x.y.z[...]）。用于过滤掉 -beta/latest 之类的 tag。</summary>
        private static bool IsParsableVersion(string v)
        {
            v = (v ?? "").TrimStart('v').Trim();
            if (string.IsNullOrEmpty(v)) return false;
            foreach (string part in v.Split('.'))
            {
                if (!int.TryParse(part, out _)) return false;
            }
            return true;
        }

        /// <summary>从 expanded_assets 端点读某 release 的真实资产列表（filename -> 绝对下载 URL）。</summary>
        private async Task<Dictionary<string, string>> FetchAssetUrls(string rawTag)
        {
            var assets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                string url = $"{REPO_URL}/releases/expanded_assets/{rawTag}";
                string html = await _httpClient.GetStringAsync(url);
                foreach (Match m in Regex.Matches(html, @"/[^""\s]*?/releases/download/[^""\s]+"))
                {
                    string abs = "https://github.com" + m.Value;
                    int slash = abs.LastIndexOf('/');
                    string fileName = slash >= 0 ? abs.Substring(slash + 1) : abs;
                    if (!assets.ContainsKey(fileName))
                        assets[fileName] = abs;
                }
            }
            catch { }
            return assets;
        }

        /// <summary>从资产列表挑 zip：优先 *-Release.zip，其次不含 Debug 的 zip，最后任一 .zip。</summary>
        private static string? PickZipUrl(Dictionary<string, string> assets)
        {
            string? pick(Func<KeyValuePair<string, string>, bool> pred)
            {
                foreach (var kv in assets)
                    if (pred(kv)) return kv.Value;
                return null;
            }
            return pick(kv => kv.Key.EndsWith("-Release.zip", StringComparison.OrdinalIgnoreCase))
                ?? pick(kv => kv.Key.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
                              && !kv.Key.Contains("Debug", StringComparison.OrdinalIgnoreCase))
                ?? pick(kv => kv.Key.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>从 version.json URL 读权威更新清单（CDN，不受 API 限流）。失败返回 null。</summary>
        private async Task<(UpdateManifest Manifest, string RawJson)?> TryFetchManifestFromUrl(string versionJsonUrl)
        {
            try
            {
                using var resp = await _httpClient.GetAsync(versionJsonUrl);
                if (!resp.IsSuccessStatusCode) return null;
                string body = await resp.Content.ReadAsStringAsync();
                var manifest = JsonSerializer.Deserialize<UpdateManifest>(body, ManifestJsonOptions);
                if (manifest == null) return null;

                manifest.Shell = (manifest.Shell ?? "").Trim().TrimStart('v');
                manifest.Core = (manifest.Core ?? "").Trim().TrimStart('v');
                manifest.Files = NormalizeManifestFiles(manifest.Files);

                if (string.IsNullOrEmpty(manifest.Shell) && string.IsNullOrEmpty(manifest.Core))
                    return null;

                return (manifest, body);
            }
            catch { }
            return null;
        }

        private static Dictionary<string, FileDigest> NormalizeManifestFiles(Dictionary<string, FileDigest>? files)
        {
            var normalized = new Dictionary<string, FileDigest>(StringComparer.OrdinalIgnoreCase);
            if (files == null) return normalized;

            foreach (var kv in files)
            {
                string key = NormalizeRelativePath(kv.Key);
                if (string.IsNullOrEmpty(key) || kv.Value == null) continue;
                normalized[key] = kv.Value;
            }
            return normalized;
        }

        private static bool IsShellFile(string relativePath)
            => NormalizeRelativePath(relativePath).Equals("OpenSteamKitten.exe", StringComparison.OrdinalIgnoreCase);

        private static bool IsCoreFile(string relativePath)
        {
            string rel = NormalizeRelativePath(relativePath);
            if (!rel.StartsWith("Resources/dlls/", StringComparison.OrdinalIgnoreCase))
                return false;

            string name = Path.GetFileName(rel);
            if (name.Equals("VERSION.txt", StringComparison.OrdinalIgnoreCase))
                return true;

            foreach (string dll in CoreDllNames)
            {
                if (name.Equals(dll, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private static bool IsSameVersion(string a, string b)
        {
            a = (a ?? "").Trim().TrimStart('v');
            b = (b ?? "").Trim().TrimStart('v');
            if (!IsParsableVersion(a) || !IsParsableVersion(b)) return false;

            string[] aParts = a.Split('.');
            string[] bParts = b.Split('.');
            int max = Math.Max(aParts.Length, bParts.Length);
            for (int i = 0; i < max; i++)
            {
                int ai = i < aParts.Length ? int.Parse(aParts[i]) : 0;
                int bi = i < bParts.Length ? int.Parse(bParts[i]) : 0;
                if (ai != bi) return false;
            }
            return true;
        }

        private static bool HasManifestMismatch(UpdateManifest manifest, Func<string, bool> includeFile)
        {
            foreach (var kv in manifest.Files)
            {
                string rel = NormalizeRelativePath(kv.Key);
                if (!includeFile(rel)) continue;
                if (!LocalFileMatches(rel, kv.Value))
                    return true;
            }
            return false;
        }

        public bool LocalFilesMatchManifest(string? manifestJson)
        {
            var manifest = TryParseManifest(manifestJson);
            if (manifest == null) return true;

            return !HasManifestMismatch(manifest, rel => IsShellFile(rel) || IsCoreFile(rel));
        }

        private static UpdateManifest? TryParseManifest(string? manifestJson)
        {
            if (string.IsNullOrWhiteSpace(manifestJson)) return null;
            try
            {
                var manifest = JsonSerializer.Deserialize<UpdateManifest>(manifestJson, ManifestJsonOptions);
                if (manifest == null) return null;
                manifest.Files = NormalizeManifestFiles(manifest.Files);
                manifest.Shell = (manifest.Shell ?? "").Trim().TrimStart('v');
                manifest.Core = (manifest.Core ?? "").Trim().TrimStart('v');
                return manifest;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>从 release 标题里解析内核版本号，如 "v1.1.1 - 内核更新 (OpenSteamTool 1.4.9)" → "1.4.9"。</summary>
        private static string? ParseCoreFromReleaseName(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            var m = Regex.Match(name, @"OpenSteamTool\s+v?(\d+\.\d+\.\d+)", RegexOptions.IgnoreCase);
            return m.Success ? m.Groups[1].Value : null;
        }

        /// <summary>语义比较：latest 是否严格大于 current。解析失败一律返回 false（避免误报）。</summary>
        public bool IsNewerVersion(string currentVersion, string latestVersion)
        {
            currentVersion = (currentVersion ?? "").TrimStart('v');
            latestVersion = (latestVersion ?? "").TrimStart('v');

            try
            {
                var currentParts = currentVersion.Split('.');
                var latestParts = latestVersion.Split('.');

                for (int i = 0; i < Math.Max(currentParts.Length, latestParts.Length); i++)
                {
                    int currentPart = i < currentParts.Length ? int.Parse(currentParts[i]) : 0;
                    int latestPart = i < latestParts.Length ? int.Parse(latestParts[i]) : 0;

                    if (latestPart > currentPart) return true;
                    if (latestPart < currentPart) return false;
                }
                return false; // 版本相同
            }
            catch
            {
                // 解析失败：不当作"有更新"，避免用 Ordinal 字符串比较把 1.10.0 误判为低于 1.9.0
                return false;
            }
        }

        private static bool LocalFileMatches(string relativePath, FileDigest expected)
        {
            if (expected == null) return true;

            string localPath = Path.Combine(AppDir, NormalizeRelativePath(relativePath).Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(localPath)) return false;

            try
            {
                var info = new FileInfo(localPath);
                if (expected.Size > 0 && info.Length != expected.Size)
                    return false;

                if (!string.IsNullOrWhiteSpace(expected.Sha256))
                {
                    string actual = ComputeSha256(localPath);
                    return actual.Equals(expected.Sha256.Trim(), StringComparison.OrdinalIgnoreCase);
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static string ComputeSha256(string path)
        {
            using var stream = File.OpenRead(path);
            using var sha = SHA256.Create();
            return Convert.ToHexString(sha.ComputeHash(stream));
        }

        private static string ComputeSha256(Stream stream)
        {
            using var sha = SHA256.Create();
            return Convert.ToHexString(sha.ComputeHash(stream));
        }

        private static string NormalizeRelativePath(string relativePath)
            => (relativePath ?? "").Trim().Replace('\\', '/').TrimStart('/');

        /// <summary>
        /// 下载并应用更新。仅内核 → 原地覆盖 DLL（免重启）；壳有更新 → 暂存新 exe + 生成替换 bat（需重启）。
        /// </summary>
        public async Task<UpdateApplyResult> ApplyUpdateAsync(
            UpdateCheckResult info, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
        {
            if (info == null || string.IsNullOrEmpty(info.ZipDownloadUrl))
                return Failed("没有可用的下载地址。");

            var manifest = TryParseManifest(info.RemoteManifestJson);

            // 先写 pending marker，再开始应用（用于重启后校验是否成功）
            WritePendingMarker(info);

            string tempZip = Path.Combine(Path.GetTempPath(), $"OpenSteamKitten-update-{Guid.NewGuid():N}.zip");
            string extractDir = Path.Combine(Path.GetTempPath(), $"OpenSteamKitten-update-{Guid.NewGuid():N}");

            try
            {
                // 1) 下载 zip（增量读，UI 不卡）
                using (var resp = await _httpClient.GetAsync(info.ZipDownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
                {
                    resp.EnsureSuccessStatusCode();
                    using (var fs = File.Create(tempZip))
                    {
                        await resp.Content.CopyToAsync(fs, cancellationToken);
                    }
                }

                VerifyDownloadedPackage(tempZip, manifest, info.ShellUpdateAvailable);
                progress?.Report(0.5);

                if (!info.ShellUpdateAvailable)
                {
                    // 2a) 仅内核更新：原地覆盖 DLL + VERSION.txt + version.json，免重启
                    await Task.Run(() => ApplyCoreOnlyFromZip(tempZip), cancellationToken);
                    WriteRemoteManifest(info.RemoteManifestJson);
                    progress?.Report(1.0);

                    string nowCore = GetCoreVersion();
                    string target = (info.LatestCore ?? "").TrimStart('v');
                    if (!string.IsNullOrEmpty(target) && nowCore.Trim() == target &&
                        LocalFilesMatchManifest(info.RemoteManifestJson))
                    {
                        DeletePendingMarker();
                        return new UpdateApplyResult
                        {
                            Outcome = UpdateApplyResult.OutcomeKind.SuccessRestartFree,
                            Message = $"内核已更新：{info.CurrentCore} → {nowCore}"
                        };
                    }
                    return Failed($"内核校验失败（当前 {nowCore}，目标 {target}）。");
                }
                else
                {
                    // 2b) 壳有更新：解压到临时目录，复制非 exe 文件，暂存新 exe，生成替换 bat
                    await Task.Run(() =>
                    {
                        if (Directory.Exists(extractDir)) Directory.Delete(extractDir, true);
                        ZipFile.ExtractToDirectory(tempZip, extractDir, overwriteFiles: true);
                        CopyNonExeFiles(extractDir);
                        WriteRemoteManifest(info.RemoteManifestJson);
                        StageNewExe(extractDir);
                    }, cancellationToken);
                    progress?.Report(1.0);

                    string? batchPath = GenerateSwapBatch();
                    if (batchPath == null)
                        return Failed("无法生成更新脚本。");

                    return new UpdateApplyResult
                    {
                        Outcome = UpdateApplyResult.OutcomeKind.SuccessRestartNeeded,
                        Message = batchPath // 把 bat 路径塞进 Message，供调用方启动
                    };
                }
            }
            catch (OperationCanceledException)
            {
                return Failed("更新已取消。");
            }
            catch (Exception ex)
            {
                return Failed(ex.Message);
            }
            finally
            {
                try { if (File.Exists(tempZip)) File.Delete(tempZip); } catch { }
                try { if (Directory.Exists(extractDir)) Directory.Delete(extractDir, true); } catch { }
            }
        }

        /// <summary>仅内核：从 zip 里挑出 3 个 DLL + VERSION.txt + version.json，原地覆盖。</summary>
        private static void VerifyDownloadedPackage(string zipPath, UpdateManifest? manifest, bool shellUpdate)
        {
            if (manifest?.Package != null && !string.IsNullOrWhiteSpace(manifest.Package.Sha256))
            {
                string actualZipHash = ComputeSha256(zipPath);
                if (!actualZipHash.Equals(manifest.Package.Sha256.Trim(), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("下载包校验失败（SHA256 不匹配）。");
            }

            if (manifest == null || manifest.Files.Count == 0)
                return;

            using var archive = ZipFile.OpenRead(zipPath);
            foreach (var kv in manifest.Files)
            {
                string rel = NormalizeRelativePath(kv.Key);
                bool shouldVerify = shellUpdate ? (IsShellFile(rel) || IsCoreFile(rel)) : IsCoreFile(rel);
                if (!shouldVerify) continue;

                var entry = FindEntry(archive, rel);
                if (entry == null)
                    throw new InvalidDataException($"下载包缺少文件：{rel}");

                if (kv.Value.Size > 0 && entry.Length != kv.Value.Size)
                    throw new InvalidDataException($"下载包文件大小不匹配：{rel}");

                if (!string.IsNullOrWhiteSpace(kv.Value.Sha256))
                {
                    using var stream = entry.Open();
                    string actual = ComputeSha256(stream);
                    if (!actual.Equals(kv.Value.Sha256.Trim(), StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException($"下载包文件校验失败：{rel}");
                }
            }
        }

        private static void WriteRemoteManifest(string? manifestJson)
        {
            if (string.IsNullOrWhiteSpace(manifestJson)) return;

            File.WriteAllText(ManifestPath, manifestJson, Encoding.UTF8);
        }

        /// <summary>仅内核：从 zip 里挑出 3 个 DLL + VERSION.txt + version.json，原地覆盖。</summary>
        private void ApplyCoreOnlyFromZip(string zipPath)
        {
            Directory.CreateDirectory(CoreDllDir);
            using var archive = ZipFile.OpenRead(zipPath);

            foreach (string dll in CoreDllNames)
            {
                var entry = FindEntry(archive, dll);
                if (entry != null)
                    entry.ExtractToFile(Path.Combine(CoreDllDir, dll), overwrite: true);
            }

            var versionEntry = FindEntry(archive, "VERSION.txt");
            if (versionEntry != null)
                versionEntry.ExtractToFile(CoreVersionPath, overwrite: true);

            var manifestEntry = FindEntry(archive, "version.json");
            if (manifestEntry != null)
                manifestEntry.ExtractToFile(ManifestPath, overwrite: true);
        }

        private static ZipArchiveEntry? FindEntry(ZipArchive archive, string fileName)
        {
            string normalized = NormalizeRelativePath(fileName);
            foreach (var e in archive.Entries)
            {
                if (string.Equals(NormalizeRelativePath(e.FullName), normalized, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(e.Name, fileName, StringComparison.OrdinalIgnoreCase))
                    return e;
            }
            return null;
        }

        /// <summary>把解压目录里的 Resources/、VERSION、version.json 覆盖到 app 目录（不动 exe）。</summary>
        private void CopyNonExeFiles(string extractDir)
        {
            foreach (var dir in Directory.GetDirectories(extractDir))
            {
                string destDir = Path.Combine(AppDir, Path.GetFileName(dir));
                CopyDirectory(dir, destDir);
            }
            foreach (string file in Directory.GetFiles(extractDir))
            {
                string name = Path.GetFileName(file);
                if (name.Equals("OpenSteamKitten.exe", StringComparison.OrdinalIgnoreCase)) continue; // exe 由 bat 替换
                File.Copy(file, Path.Combine(AppDir, name), overwrite: true);
            }
        }

        private static void CopyDirectory(string source, string dest)
        {
            Directory.CreateDirectory(dest);
            foreach (string file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            {
                string rel = Path.GetRelativePath(source, file);
                string target = Path.Combine(dest, rel);
                string? targetDir = Path.GetDirectoryName(target);
                if (targetDir != null) Directory.CreateDirectory(targetDir);
                File.Copy(file, target, overwrite: true);
            }
        }

        private void StageNewExe(string extractDir)
        {
            string newExe = Path.Combine(extractDir, "OpenSteamKitten.exe");
            if (File.Exists(newExe))
                File.Copy(newExe, NewExePath, overwrite: true);
        }

        /// <summary>生成自删除的 update.bat：等本进程退出 → rename-aside 替换 exe → 重启。返回 bat 绝对路径。</summary>
        private string? GenerateSwapBatch()
        {
            int pid = Environment.ProcessId;
            var sb = new StringBuilder();
            sb.AppendLine("@echo off");
            sb.AppendLine("setlocal");
            sb.AppendLine($"set \"EXE={ExePath}\"");
            sb.AppendLine($"set \"NEW={NewExePath}\"");
            sb.AppendLine("set \"BAT=%~f0\"");
            sb.AppendLine($"set \"PID={pid}\"");
            sb.AppendLine();
            sb.AppendLine($"rem 等待 PID {pid} 退出（最多 ~30s）");
            sb.AppendLine("for /l %%i in (1,1,60) do (");
            sb.AppendLine("  tasklist /fi \"PID eq %PID%\" 2>nul | find \"%PID%\" >nul");
            sb.AppendLine("  if errorlevel 1 goto :swap");
            sb.AppendLine("  ping -n 2 127.0.0.1 >nul");
            sb.AppendLine(")");
            sb.AppendLine("goto :swap");
            sb.AppendLine();
            sb.AppendLine(":swap");
            sb.AppendLine("if exist \"%EXE%.old\" del /f /q \"%EXE%.old\" 2>nul");
            sb.AppendLine("if exist \"%EXE%\" ren \"%EXE%\" \"OpenSteamKitten.exe.old\"");
            sb.AppendLine("if not exist \"%NEW%\" goto :fail");
            sb.AppendLine("move /y \"%NEW%\" \"%EXE%\" >nul 2>&1");
            sb.AppendLine("if errorlevel 1 goto :fail");
            sb.AppendLine("if exist \"%EXE%.old\" del /f /q \"%EXE%.old\" 2>nul");
            sb.AppendLine("start \"\" \"%EXE%\"");
            sb.AppendLine("del /f /q \"%BAT%\" 2>nul");
            sb.AppendLine("exit /b 0");
            sb.AppendLine();
            sb.AppendLine(":fail");
            sb.AppendLine("if not exist \"%EXE%\" if exist \"%EXE%.old\" ren \"%EXE%.old\" \"OpenSteamKitten.exe\"");
            sb.AppendLine("del /f /q \"%NEW%\" 2>nul");
            sb.AppendLine("del /f /q \"%BAT%\" 2>nul");
            sb.AppendLine("exit /b 1");

            try
            {
                // 用系统 ANSI 编码写，cmd.exe 原生可读；内容为纯 ASCII，任何编码都安全
                File.WriteAllText(SwapBatchPath, sb.ToString(), Encoding.Default);
                return SwapBatchPath;
            }
            catch
            {
                return null;
            }
        }

        // ---------- pending marker（重启后校验） ----------

        private void WritePendingMarker(UpdateCheckResult info)
        {
            try
            {
                string kind = info.ShellUpdateAvailable
                    ? (info.CoreUpdateAvailable ? "both" : "shell")
                    : "core";
                var obj = new
                {
                    targetShell = info.LatestShell ?? "",
                    targetCore = (info.LatestCore ?? "").TrimStart('v'),
                    kind,
                    manifestJson = info.RemoteManifestJson ?? "",
                    timestamp = DateTimeOffset.UtcNow.ToString("o")
                };
                File.WriteAllText(PendingMarkerPath, JsonSerializer.Serialize(obj));
            }
            catch { }
        }

        public void DeletePendingMarker()
        {
            try { if (File.Exists(PendingMarkerPath)) File.Delete(PendingMarkerPath); }
            catch { }
        }

        /// <summary>读取 pending marker 的目标版本/清单；不存在或解析失败返回 null。</summary>
        public (string TargetShell, string TargetCore, string? ManifestJson)? ReadPendingMarker()
        {
            try
            {
                if (!File.Exists(PendingMarkerPath)) return null;
                using var doc = JsonDocument.Parse(File.ReadAllText(PendingMarkerPath));
                var root = doc.RootElement;
                string ts = root.TryGetProperty("targetShell", out var tsEl) ? (tsEl.GetString() ?? "") : "";
                string tc = root.TryGetProperty("targetCore", out var tcEl) ? (tcEl.GetString() ?? "") : "";
                string? manifestJson = root.TryGetProperty("manifestJson", out var manifestEl) ? manifestEl.GetString() : null;
                return (ts, tc, manifestJson);
            }
            catch { return null; }
        }

        public void OpenReleasesPage(string? url)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = string.IsNullOrEmpty(url) ? RELEASES_PAGE_URL : url,
                    UseShellExecute = true
                });
            }
            catch { }
        }

        public string ReleasesPageUrl => RELEASES_PAGE_URL;

        private static UpdateApplyResult Failed(string msg) => new UpdateApplyResult
        {
            Outcome = UpdateApplyResult.OutcomeKind.FailedNeedsManual,
            Message = msg
        };
    }
}
