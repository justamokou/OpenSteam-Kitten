using Microsoft.Win32;
using System;
using System.IO;

namespace OpenSteamKitten.Services
{
    public class SteamPathService
    {
        private string? _cachedSteamPath;

        public string? GetSteamPath()
        {
            if (_cachedSteamPath != null)
                return _cachedSteamPath;

            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
                if (key == null)
                    return null;

                string? path = key.GetValue("SteamPath")?.ToString();
                if (string.IsNullOrEmpty(path))
                    return null;

                // 转换为 Windows 路径格式
                path = path.Replace('/', '\\');

                if (ValidateSteamPath(path))
                {
                    _cachedSteamPath = path;
                    return path;
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        public bool ValidateSteamPath(string? path)
        {
            if (string.IsNullOrEmpty(path))
                return false;

            return File.Exists(Path.Combine(path, "steam.exe"));
        }

        public string GetLuaDirectory()
        {
            var steamPath = GetSteamPath();
            if (string.IsNullOrEmpty(steamPath))
                throw new InvalidOperationException("未找到 Steam 安装路径");

            return Path.Combine(steamPath, "config", "lua");
        }

        public string GetDepotCacheDirectory()
        {
            var steamPath = GetSteamPath();
            if (string.IsNullOrEmpty(steamPath))
                throw new InvalidOperationException("未找到 Steam 安装路径");

            return Path.Combine(steamPath, "config", "depotcache");
        }
    }
}
