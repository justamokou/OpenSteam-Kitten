# GitHub Actions 自动更新说明

## 功能

本项目使用 GitHub Actions 自动监控 [OpenSteamTool](https://github.com/OpenSteam001/OpenSteamTool) 的更新，并自动执行以下操作：

1. **每天自动检查** OpenSteamTool 是否有新版本发布
2. **自动下载**最新的 DLL 文件（OpenSteamTool.dll、dwmapi.dll、xinput1_4.dll）
3. **自动编译**新的 Release 版本
4. **自动发布** GitHub Release，用户可直接下载使用

## 工作流程

### 自动触发（每天一次）
- 时间：每天北京时间 08:00（UTC 00:00）
- 操作：自动检查上游更新

### 手动触发
1. 访问：https://github.com/justamokou/OpenSteam-Kitten/actions/workflows/auto-update-dlls.yml
2. 点击 "Run workflow" 按钮
3. 点击绿色的 "Run workflow" 确认

## 工作流文件

- 文件位置：`.github/workflows/auto-update-dlls.yml`
- 版本记录：`OpenSteamKitten/Resources/dlls/VERSION.txt`

## 发布规则

- **标签名**：与 OpenSteamTool 的版本号一致（如 `1.4.8`）
- **Release 名称**：`[版本号] - 自动更新`
- **文件名**：`OpenSteamKitten-[版本号].zip`

## 注意事项

1. 只有当 OpenSteamTool 发布新版本时才会创建新的 Release
2. 如果已经是最新版本，工作流会跳过构建
3. 所有操作完全自动化，无需人工干预
