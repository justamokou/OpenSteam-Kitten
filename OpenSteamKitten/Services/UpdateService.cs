using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Windows;
using System.IO;
using MessageBox = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage = System.Windows.MessageBoxImage;
using MessageBoxResult = System.Windows.MessageBoxResult;

namespace OpenSteamKitten.Services
{
    public class UpdateService
    {
        private const string GITHUB_API_URL = "https://api.github.com/repos/justamokou/OpenSteam-Kitten/releases/latest";
        private const string RELEASES_PAGE_URL = "https://github.com/justamokou/OpenSteam-Kitten/releases";

        private static readonly HttpClient _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10)
        };

        static UpdateService()
        {
            // GitHub API 要求 User-Agent
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "OpenSteamKitten");
        }

        public string GetCurrentVersion()
        {
            try
            {
                string versionFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "VERSION");
                if (File.Exists(versionFile))
                {
                    return File.ReadAllText(versionFile).Trim();
                }
            }
            catch { }
            return "1.1.0"; // 默认版本
        }

        public string GetCoreVersion()
        {
            try
            {
                string versionFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "dlls", "VERSION.txt");
                if (File.Exists(versionFile))
                {
                    return File.ReadAllText(versionFile).Trim();
                }
            }
            catch { }
            return "未知";
        }

        public async Task CheckForUpdatesAsync(bool showNoUpdateMessage = true)
        {
            string currentVersion = GetCurrentVersion();

            try
            {
                // 获取最新版本信息
                var response = await _httpClient.GetStringAsync(GITHUB_API_URL);
                var jsonDoc = JsonDocument.Parse(response);
                var root = jsonDoc.RootElement;

                string latestVersion = root.GetProperty("tag_name").GetString() ?? "";
                latestVersion = latestVersion.TrimStart('v'); // 移除 v 前缀

                string releaseName = root.GetProperty("name").GetString() ?? "";
                string releaseNotes = root.GetProperty("body").GetString() ?? "";
                string releaseUrl = root.GetProperty("html_url").GetString() ?? RELEASES_PAGE_URL;

                // 比较版本
                if (IsNewerVersion(currentVersion, latestVersion))
                {
                    // 有新版本
                    var result = MessageBox.Show(
                        $"发现新版本！\n\n" +
                        $"当前版本：v{currentVersion}\n" +
                        $"最新版本：v{latestVersion}\n\n" +
                        $"{releaseName}\n\n" +
                        $"是否前往下载页面？",
                        "发现新版本",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Information);

                    if (result == MessageBoxResult.Yes)
                    {
                        OpenReleasesPage(releaseUrl);
                    }
                }
                else
                {
                    // 已是最新版本
                    if (showNoUpdateMessage)
                    {
                        MessageBox.Show(
                            $"已是最新版本 v{currentVersion} 🎉",
                            "检查更新",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);
                    }
                }
            }
            catch (HttpRequestException)
            {
                if (showNoUpdateMessage)
                {
                    MessageBox.Show(
                        "无法连接到 GitHub 服务器，请检查网络连接。",
                        "检查更新失败",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
            }
            catch (TaskCanceledException)
            {
                if (showNoUpdateMessage)
                {
                    MessageBox.Show(
                        "连接超时，请稍后重试。",
                        "检查更新失败",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                if (showNoUpdateMessage)
                {
                    MessageBox.Show(
                        $"检查更新时出错：{ex.Message}",
                        "错误",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            }
        }

        private bool IsNewerVersion(string currentVersion, string latestVersion)
        {
            // 移除 'v' 前缀
            currentVersion = currentVersion.TrimStart('v');
            latestVersion = latestVersion.TrimStart('v');

            try
            {
                // 简单的版本比较（假设格式为 x.y.z）
                var currentParts = currentVersion.Split('.');
                var latestParts = latestVersion.Split('.');

                for (int i = 0; i < Math.Max(currentParts.Length, latestParts.Length); i++)
                {
                    int currentPart = i < currentParts.Length ? int.Parse(currentParts[i]) : 0;
                    int latestPart = i < latestParts.Length ? int.Parse(latestParts[i]) : 0;

                    if (latestPart > currentPart)
                        return true;
                    if (latestPart < currentPart)
                        return false;
                }

                return false; // 版本相同
            }
            catch
            {
                // 解析失败，使用字符串比较
                return string.Compare(latestVersion, currentVersion, StringComparison.Ordinal) > 0;
            }
        }

        private void OpenReleasesPage(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"无法打开浏览器：{ex.Message}\n\n请手动访问：\n{RELEASES_PAGE_URL}",
                    "错误",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
    }
}
