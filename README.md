# OpenSteam Kitten 🐱

一个轻量级 GUI 壳程序，用于简化 [OpenSteamTool](https://github.com/OpenSteam001/OpenSteamTool) 的使用。

## ⚡ 快速开始（3 步搞定）

### 第 1 步：下载并运行

**📥 下载**：

从 [Releases](https://github.com/justamokou/OpenSteam-Kitten/releases) 下载 `OpenSteamKitten.exe`（约 **171 KB**）

> 💡 **首次运行**：如提示需要 .NET 6 Desktop Runtime，Windows 会自动引导安装（约 55 MB，一次安装永久使用）。

**运行**：双击 exe 文件，会在桌面右下角出现一个黑色圆形悬浮窗，中间有白色猫猫图标 🐱

### 第 2 步：一键安装 DLL

1. **右键点击**悬浮窗
2. 选择 **"一键安装 DLL 📦"**
3. 等待安装完成
4. **重启 Steam** 使更改生效

> ⚠️ **重要**：必须重启 Steam 才能加载 OpenSteamTool！

### 第 3 步：添加 Lua 配置

将你的 `.lua` 配置文件**直接拖到悬浮窗**上即可！

程序会自动：
- ✅ 创建 `<Steam根目录>\config\lua\` 目录
- ✅ 复制文件到正确位置
- ✅ 显示操作结果

> 🔧 **删除配置**：按住 `Ctrl` 键，再将 `.lua` 文件拖到悬浮窗上

---

## 🎮 使用技巧

| 操作 | 功能 |
|------|------|
| **双击悬浮窗** | 启动 Steam |
| **拖入 .lua 文件** | 添加到配置目录 |
| **Ctrl + 拖入 .lua** | 删除对应配置 |
| **右键菜单** | 查看更多功能 |
| **拖动悬浮窗** | 移动到你喜欢的位置 |

### 右键菜单功能

- 📦 **一键安装 DLL**：安装 OpenSteamTool 到 Steam 目录
- 📁 **打开 Lua 目录**：快速访问配置文件夹
- 🎮 **打开 Steam**：启动 Steam 客户端
- ℹ️ **关于**：查看版本信息和使用说明
- ❌ **退出**：关闭程序

## 系统要求

- Windows 10/11
- .NET 6.0 Desktop Runtime（如果使用 Lite 版本）
- 已安装 Steam

## 开发

### 技术栈

- C# WPF
- .NET 6.0
- 纯 WPF + Windows Forms，无外部 NuGet 依赖

### 构建

```powershell
# 克隆仓库
git clone https://github.com/justamokou/OpenSteam-Kitten.git
cd OpenSteam-Kitten

# 构建
dotnet build

# 运行
dotnet run --project OpenSteamKitten

# 发布（单文件可执行程序）
dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true
```

### 项目结构

```
OpenSteam-Kitten/
├── OpenSteamKitten/
│   ├── Services/           # 业务逻辑服务
│   │   ├── SteamPathService.cs
│   │   ├── InstallService.cs
│   │   ├── LuaFileService.cs
│   │   └── ProcessService.cs
│   ├── Resources/
│   │   └── dlls/          # OpenSteamTool DLL 文件
│   ├── App.xaml           # 应用入口
│   └── MainWindow.xaml    # 主窗口
└── README.md
```

## 安全说明

- ✅ 不收集任何用户数据
- ✅ 仅访问本地注册表和文件系统
- ✅ 不进行网络通信
- ✅ DLL 文件直接嵌入程序，防止篡改

## 常见问题

**Q: 提示"未检测到 Steam 安装路径"？**  
A: 确保 Steam 已正确安装，程序会从注册表读取 `HKEY_CURRENT_USER\Software\Valve\Steam\SteamPath`

**Q: 安装后 Steam 无法启动？**  
A: 请检查是否有杀毒软件拦截了 DLL 文件，或尝试以管理员权限运行 Steam

**Q: 如何卸载？**  
A: 删除 Steam 根目录下的 `OpenSteamTool.dll`、`dwmapi.dll`、`xinput1_4.dll` 三个文件即可

## 许可证

MIT License

## 致谢

- [OpenSteamTool](https://github.com/OpenSteam001/OpenSteamTool) - 核心功能提供者

## 免责声明

本项目仅供学习和研究使用，请遵守当地法律法规、平台服务条款和软件许可协议。
