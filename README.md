# OpenSteam Kitten

OpenSteam Kitten 是 [OpenSteamTool](https://github.com/OpenSteam001/OpenSteamTool) 的轻量 WPF 壳。它提供一个小型悬浮窗和托盘菜单，用来安装 OpenSteamTool DLL、管理 Lua/manifest 文件，并处理小猫和内核更新。

## 下载与运行

从 [Releases](https://github.com/justamokou/OpenSteam-Kitten/releases) 下载最新的 `OpenSteamKitten-*-Release.zip`，解压到任意位置后运行 `OpenSteamKitten.exe`。

请保持这些文件在同一目录下：

```text
OpenSteamKitten.exe
VERSION
version.json
Resources/
```

当前发布包是精简版，需要 .NET 6 Desktop Runtime。首次运行如缺少运行时，Windows 通常会引导安装。

## 常用操作

| 操作 | 功能 |
|---|---|
| 双击悬浮窗 | 启动 Steam |
| 右键悬浮窗 | 打开功能菜单 |
| 拖入 `.lua` | 复制到 `Steam/config/lua/` |
| 拖入 `.manifest` | 复制到 `Steam/config/depotcache/` |
| Ctrl + 拖入文件 | 删除 Steam 配置目录里的对应文件 |
| 拖动悬浮窗 | 移动位置 |

## 主要功能

- 一键安装 `OpenSteamTool.dll`、`dwmapi.dll`、`xinput1_4.dll` 到 Steam 根目录。
- 一键清理已安装 DLL、Lua 配置、manifest 文件。
- 托盘常驻，可隐藏/显示悬浮窗。
- 可设置随 Steam 启动。
- 可开启“Steam 游戏时隐藏悬浮窗”：当前前台进程加载 Steam Overlay 时隐藏小猫，切出游戏后恢复。
- 内置更新检查，发布包会通过 `version.json` 里的 SHA256/size 校验关键文件。

安装 DLL 后需要重启 Steam 才会生效。

## 系统要求

- Windows 10/11
- .NET 6 Desktop Runtime
- 已安装 Steam

## 开发

```powershell
dotnet build
dotnet run --project OpenSteamKitten
```

本地发布精简版：

```powershell
.\build-release.ps1 -Version 1.4.1
```

产物输出到 `dist/`，不要把 exe、zip、publish 输出放到仓库根目录。

## 项目结构

```text
OpenSteam-Kitten/
  OpenSteamKitten/
    App.xaml(.cs)
    MainWindow.xaml(.cs)
    Services/
    Utils/
    Resources/dlls/
    version.json
  VERSION
  build-release.ps1
  .github/workflows/
```

核心版本文件：

- `VERSION`: OpenSteam Kitten 版本。
- `OpenSteamKitten/Resources/dlls/VERSION.txt`: OpenSteamTool 内核版本。
- `OpenSteamKitten/version.json`: 本地/发布更新清单。

## 常见问题

**未检测到 Steam 安装路径**  
确认 Steam 已正确安装。程序会读取 `HKEY_CURRENT_USER\Software\Valve\Steam\SteamPath`。

**安装后没有生效**  
重启 Steam；如仍失败，检查杀毒软件是否拦截 DLL。

**如何卸载 DLL**  
右键悬浮窗使用清理功能，或手动删除 Steam 根目录下的 `OpenSteamTool.dll`、`dwmapi.dll`、`xinput1_4.dll`。

## 致谢

- [OpenSteamTool](https://github.com/OpenSteam001/OpenSteamTool)

## 许可证

MIT License

## 免责声明

本项目仅供学习和研究使用。请遵守当地法律法规、平台服务条款和软件许可协议。
