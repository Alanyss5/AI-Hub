# AI-Hub 快速操作手册

更新日期：2026-03-19

这份手册只保留日常最常用的入口、检查点和判断规则。

## 1. 常用入口

- 桌面程序：`C:\AI-Hub\desktop\apps\AIHub.Desktop\bin\Debug\net8.0\AIHub.Desktop.exe`
- 兼容脚本目录：`C:\AI-Hub\scripts`
- 共享 Skills：`C:\AI-Hub\skills`
- 共享 Agents：`C:\AI-Hub\agents`
- Claude 适配层：`C:\AI-Hub\claude`

如果优先走图形界面，大多数日常操作都可以直接在桌面端完成。

## 2. 当前仍重要的 3 个脚本

- `C:\AI-Hub\scripts\sync-mcp.ps1`
  - 作用：根据 `C:\AI-Hub\mcp\manifest` 生成 Claude / Codex / Antigravity 的 MCP 配置。

- `C:\AI-Hub\scripts\setup-global.ps1`
  - 作用：把当前电脑的全局入口接到 `C:\AI-Hub`。
  - 会建立共享入口：
    - `~/.agents\skills`
    - `~/.agents\agents`
    - `~/.claude\agents` -> `C:\AI-Hub\agents\global`
  - 也会建立个人 Skills 根目录：
    - `C:\Users\Administrator\AI-Personal\skills\global`

- `C:\AI-Hub\scripts\use-profile.ps1`
  - 作用：把某个项目接到 `global` / `frontend` / `backend` profile。
  - 会建立项目级共享入口：
    - `项目\.agents\skills`
    - `项目\.agents\agents`
    - `项目\.claude\agents` -> `C:\AI-Hub\agents\<profile>`

## 3. 最常见的日常操作

### 3.1 改了 MCP manifest

优先在桌面端执行：

1. 打开 `MCP 管理`
2. 编辑 manifest
3. 点击 `Generate Configs`
4. 点击 `Validate Scope`
5. 如果要把结果同步到当前作用域客户端，再点击 `Sync Clients`

命令行等价操作：

```powershell
powershell -ExecutionPolicy Bypass -File C:\AI-Hub\scripts\sync-mcp.ps1
```

### 3.2 新电脑或重建全局入口

```powershell
powershell -ExecutionPolicy Bypass -File C:\AI-Hub\scripts\setup-global.ps1
```

执行后重点检查：

- `C:\Users\Administrator\.agents\skills`
- `C:\Users\Administrator\.agents\agents`
- `C:\Users\Administrator\.claude\commands`
- `C:\Users\Administrator\.claude\agents`
- `C:\Users\Administrator\.claude\settings.json`

### 3.3 给项目应用 profile

```powershell
powershell -ExecutionPolicy Bypass -File C:\AI-Hub\scripts\use-profile.ps1 -ProjectPath "C:\OverSeaFramework" -Profile frontend
```

执行后重点检查：

- `项目\.agents\skills`
- `项目\.agents\agents`
- `项目\.claude\commands`
- `项目\.claude\agents`
- `项目\.claude\settings.json`
- `项目\.mcp.json`
- `项目\.codex\config.toml`

### 3.4 改了共享 Skills 或共享 Agents

通常不需要重跑脚本。

原因：

- `skills` 与 `agents` 入口本身就是 junction
- 只要你改的是已接入目录，客户端下次读取时就会直接看到最新内容

例外：

- 改了 `claude\settings\*.json` 模板，需要重跑 `setup-global.ps1` 或 `use-profile.ps1`
- 改了 `mcp\manifest\*`，需要重跑 `sync-mcp.ps1` 或在桌面端重新生成

### 3.5 想知道是否有异常

先看：

- 托盘摘要
- `MCP 管理` 页的体检结果
- `Skills` 页的最近定时结果

进入异常状态时，桌面端还会主动弹出 Windows 通知。

## 4. 当前结构怎么理解

- 共享层：`skills`、`agents`、`mcp`、`scripts`
- Claude 专属适配层：`claude\commands`、`claude\settings`
- Claude 仍保留 `.claude\agents` 入口，但它现在指向共享 `agents\*`
- Codex 当前优先使用 `AGENTS.md`、`.agents\skills`、`.agents\agents`、`.codex\config.toml`

## 5. 你主要会改哪些地方

- `C:\AI-Hub\skills\global`
- `C:\AI-Hub\skills\frontend`
- `C:\AI-Hub\skills\backend`
- `C:\AI-Hub\agents\global`
- `C:\AI-Hub\agents\frontend`
- `C:\AI-Hub\agents\backend`
- `C:\AI-Hub\claude\commands\*`
- `C:\AI-Hub\claude\settings\*`
- `C:\AI-Hub\mcp\manifest\*`
- `C:\AI-Hub\mcp\runtime.json`
- `C:\AI-Hub\scripts\hooks\*`
- `C:\Users\Administrator\AI-Personal\skills\global`

## 6. 最后记住 5 句话

- 公司共享能力优先维护在 `C:\AI-Hub\skills` 和 `C:\AI-Hub\agents`
- 个人专属 Skills 只维护在 `C:\Users\Administrator\AI-Personal\skills\global`
- 改 `mcp\manifest` 后，优先走 `Generate Configs -> Validate Scope -> Sync Clients`
- 共享 agent 不再长期维护在 `claude\agents`
- 改 `claude\settings` 模板后，要重跑 `setup-global.ps1` 或 `use-profile.ps1`
