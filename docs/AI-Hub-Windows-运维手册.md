# AI-Hub Windows 运维手册

## 适用范围
本手册面向 Windows 内部正式使用场景，覆盖发布、安装、升级、回滚、日志与诊断导出。

## 发布产物
- 桌面端发布目标固定为 `win-x64 self-contained`
- 发布脚本：`C:\AI-Hub\scripts\windows\publish-desktop.ps1`
- 安装包脚本：`C:\AI-Hub\scripts\windows\package-installer.ps1`
- 安装器模板：`C:\AI-Hub\installer\AIHub.Desktop.iss`
- 发布前验证脚本：`C:\AI-Hub\scripts\windows\verify-desktop.ps1`

## 常用命令
```powershell
powershell -ExecutionPolicy Bypass -File C:\AI-Hub\scripts\windows\verify-desktop.ps1
powershell -ExecutionPolicy Bypass -File C:\AI-Hub\scripts\windows\publish-desktop.ps1 -Configuration Release -VersionSuffix internal
powershell -ExecutionPolicy Bypass -File C:\AI-Hub\scripts\windows\backup-hub-state.ps1 -HubRoot C:\AI-Hub
```

## 安装与升级
1. 先关闭正在运行的 `AIHub.Desktop.exe`。
2. 若需要保留现有 Hub 数据，先执行 `backup-hub-state.ps1`。
3. 运行 `publish-desktop.ps1` 生成 `publish` 目录和 `release-manifest.json`。
4. 若已安装 Inno Setup 6，再执行 `package-installer.ps1` 生成安装包。
5. 升级安装前，如已设置环境变量 `AIHUB_HUBROOT`，安装器会自动调用备份脚本。

## 回滚
1. 卸载当前版本或直接停止当前桌面端进程。
2. 使用 `backups\upgrade-preflight`、`backups\release` 或 `installer-backups` 中最近一次备份。
3. 恢复 `config`、`projects`、`mcp`、`skills-overrides` 后重新启动桌面端。
4. 如需要给支持人员排障，优先导出诊断包后再回滚。

## 诊断与日志
- 诊断目录：`%LOCALAPPDATA%\AIHub\diagnostics`
- 日志目录：`%LOCALAPPDATA%\AIHub\diagnostics\logs`
- 最近一次启动失败和未处理异常会记录到 `state.json`
- 桌面端“设置”页支持导出诊断包
- 导出的诊断包会对常见 token、password、secret 以及 `%USERPROFILE%` 做脱敏

## 信任边界
- 脚本执行：会触发 PowerShell，可能修改链接、配置和项目目录
- 托管 MCP：会拉起本机进程、写日志、执行健康检查并可能自动拉起
- 外部 MCP 导入：会把客户端既有配置纳入 AI-Hub 管理，并可能同步到其他客户端
- 以上三类行为首次都需要显式风险确认，确认状态可在“设置”页重置

## 故障排查
- 启动失败：先查看“设置 > 诊断与恢复”里的最近一次启动失败
- 后台维护异常：检查桌面端通知、托盘摘要和 `%LOCALAPPDATA%\AIHub\diagnostics\logs`
- MCP 拉起失败：确认工作目录、命令、环境变量和健康检查地址
- 配置导入/导出失败：优先检查目标目录是否可写，以及文件是否被占用