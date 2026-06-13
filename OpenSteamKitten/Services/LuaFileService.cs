using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using MessageBox = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage = System.Windows.MessageBoxImage;

namespace OpenSteamKitten.Services
{
    public class LuaFileService
    {
        private readonly SteamPathService _steamPathService;

        public LuaFileService(SteamPathService steamPathService)
        {
            _steamPathService = steamPathService;
        }

        public string GetLuaDirectory()
        {
            return _steamPathService.GetLuaDirectory();
        }

        public void EnsureLuaDirectoryExists()
        {
            string luaDir = GetLuaDirectory();
            if (!Directory.Exists(luaDir))
            {
                Directory.CreateDirectory(luaDir);
            }
        }

        public async Task<bool> AddLuaFileAsync(string filePath)
        {
            try
            {
                if (!filePath.EndsWith(".lua", StringComparison.OrdinalIgnoreCase))
                {
                    MessageBox.Show("只支持 .lua 文件！", "错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return false;
                }

                if (!File.Exists(filePath))
                {
                    MessageBox.Show($"文件不存在: {filePath}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    return false;
                }

                EnsureLuaDirectoryExists();

                string fileName = Path.GetFileName(filePath);
                string destPath = Path.Combine(GetLuaDirectory(), fileName);

                await Task.Run(() => File.Copy(filePath, destPath, overwrite: true));

                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"添加文件失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        public bool RemoveLuaFile(string fileName)
        {
            try
            {
                string filePath = Path.Combine(GetLuaDirectory(), fileName);
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"删除文件失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        public bool LuaDirectoryExists()
        {
            return Directory.Exists(GetLuaDirectory());
        }
    }
}
