# AI-Hub

AI-Hub 是一个本地化的 Projects、Skills、Scripts 与 MCP 桌面控制台，当前统一以 `C:\AI-Hub` 作为工作根目录，桌面端基于 `.NET 8 + Avalonia`。

## 当前状态
当前阶段是“Windows 自用可投入使用收口已完成，并补上单实例、发布脚本与仓库卫生守卫”。桌面端已经具备：

- 项目登记、当前项目切换、全局级 / 项目级作用域切换
- Skills 来源登记、扫描、安装登记、基线管理、差异预览、安全同步
- Skills 来源级定时更新策略：`6h / 12h / 24h / 7d`
- Skills Overlay 文件级合并预览、应用、备份与回滚
- MCP manifest 编辑、配置生成、当前作用域体检、客户端同步、外部 MCP 纳管
- Claude / Codex / Antigravity 全局 MCP 配置接管；Claude / Codex 项目级 MCP 配置接管
- 托管型 MCP 启停 / 重启 / 健康检查 / 日志预览 / 托盘后台维护
- Windows 系统通知：仅在进入异常状态时提醒，并按告警键 15 分钟去重
- 桌面端单实例：第二次启动会唤起已有窗口并立即退出新进程
- AI-Hub 本体目录零待办标记守卫与备份文件守卫
- 配置包导入导出与导入预检
- `win-x64 self-contained` 发布脚本、发布清单与便携包打包回退

2026-03-15 补充：

- 工作区接入已升级为“四层资源叠加”模型：`全局公司 -> 全局私人 -> 项目公司(Profile) -> 项目私人(Profile)`
- `AI-Personal` 不再只放个人 skills，而是同步承载 `skills / claude commands / claude agents / claude settings / mcp manifest`
- `应用全局链接` 与 `应用项目 Profile` 会先生成 `.runtime/effective/<profile>` 有效输出，再把用户目录或项目目录入口切到这套输出
- 首次全局接管、首次项目接管会先扫描现有 `Skills / commands / agents / Claude settings / MCP`，通过专用向导选择导入到 `AI-Hub`、`私人目录` 或 `忽略`
- MCP generated 配置已按四层优先级生成，`frontend/backend` 默认继承全局层，再叠加对应 Profile 层
- 项目页现在会显式显示运行标识：构建来源、当前可执行文件路径和 `HubRoot`
- 如果已登记项目的路径和表单目录不一致，`应用项目 Profile`、`设为当前项目` 和 `重新扫描项目接管` 会先阻断并提示“请先保存项目”
- 项目目录里不会出现独立的 `global` 子目录；全局层已经合并进 `.runtime/effective/<profile>`
- 全局重扫和项目重扫在没有候选项时，会弹出“未发现可重新导入资源”的明确结果提示

## 构建与测试
```powershell
C:\Users\Administrator\.dotnet\dotnet.exe build C:\AI-Hub\desktop\AIHub.sln
C:\Users\Administrator\.dotnet\dotnet.exe test C:\AI-Hub\desktop\AIHub.sln --no-build
```

标准 `build` 在 Windows 下会在构建前自动关闭当前目标路径上正在运行的桌面端 Debug 实例；如需保留运行中的实例，可追加 `/p:AutoStopRunningDesktopAppBeforeBuild=false`。

2026-03-08 最新验证结果：

- `build` 通过，`0 warning / 0 error`
- `test` 通过，`27/27`
- 已验证双启动烟测通过：第一次启动后进程数为 1，第二次启动后仍为 1
- 已验证 `win-x64 self-contained publish` 成功，产物落在 `desktop\.artifacts\publish\0.10.0-internal\win-x64`

## Windows 内部正式使用前仍需补齐
当前还没有达到“Windows 内部正式使用候选”状态，还需要补齐：

- 真实安装器编译环境固化与安装 / 升级 / 卸载 smoke
- 长时间运行验证与后台维护稳定性回归
- 发布签名、许可证文本归档与最终受控升级流程演练
- 内部支持流程的实战化演练

详细清单见：`docs/AI-Hub-Windows-内部正式使用门槛.md`

## 目录
- `desktop/`：桌面端解决方案与源代码
- `agents/`：跨 Claude / Codex 共享的 agent 事实源
- `skills/`：Skills 目录、来源登记、覆盖层与缓存数据
- `claude/`：Claude 专属 commands 与 settings 适配层
- `mcp/`：MCP manifest、生成配置、运行时数据
- `.runtime/effective/`：四层资源合并后的有效输出
- `config/`：Hub 设置与 Skills 状态登记
- `projects/`：项目注册表
- `docs/`：中文文档

## 文档入口
建议从这里开始：

- `docs/README.md`
- `docs/AI-Hub-可投入使用冲刺计划.md`
- `docs/AI-Hub-开发路线图.md`
- `docs/AI-Hub-Windows-内部正式使用门槛.md`
- `docs/AI-Hub-快速操作手册.md`
- `docs/AI-Hub-使用手册.md`

## Windows 发布与运维
- 版本号统一由 `C:\AI-Hub\desktop\Version.props` 管理，桌面端头部会显示当前版本。
- 发布脚本：`C:\AI-Hub\scripts\windows\publish-desktop.ps1`
- 安装包脚本：`C:\AI-Hub\scripts\windows\package-installer.ps1`
- 升级前备份脚本：`C:\AI-Hub\scripts\windows\backup-hub-state.ps1`
- 发布前验证脚本：`C:\AI-Hub\scripts\windows\verify-desktop.ps1`
- Inno Setup 模板：`C:\AI-Hub\installer\AIHub.Desktop.iss`
- 如果本机未安装 `ISCC.exe`，安装包脚本会自动退化为便携 zip 打包，并生成 `package-manifest.json`

推荐验证命令：
```powershell
powershell -ExecutionPolicy Bypass -File C:\AI-Hub\scripts\windows\verify-desktop.ps1
```

推荐发布命令：
```powershell
powershell -ExecutionPolicy Bypass -File C:\AI-Hub\scripts\windows\publish-desktop.ps1 -SkipBackup
```

说明：当前 solution 级 `dotnet test C:\AI-Hub\desktop\AIHub.sln --no-build` 已恢复可用；发布脚本默认产出 `win-x64 self-contained` 目录，并在缺少 Inno Setup 时自动生成便携 zip 包。

## Worktree 验收

如果你正在验收 `codex/four-layer-onboarding` 分支，请运行 worktree 里的程序，而不是主目录旧版：

```powershell
C:\Users\Administrator\.config\superpowers\worktrees\AI-Hub\codex\four-layer-onboarding\desktop\apps\AIHub.Desktop\bin\Debug\net8.0\AIHub.Desktop.exe
```

最短验收路径：

1. 打开上面的 worktree exe
2. 在“项目与 Profile”页确认已经显示构建来源、可执行文件路径和 `HubRoot`
3. 把 `OverSeaFramework` 保存为 `C:\OverSeaFramework`
4. 应用 `backend`
5. 确认项目 skill 入口改为 `.runtime/effective/backend/skills`
6. 点击项目重扫且无新增资源时，确认会弹出明确结果提示
