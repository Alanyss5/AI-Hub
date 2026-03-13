# 脚本目录说明

这个目录保存当前 AI-Hub 仍在使用的兼容脚本。

虽然桌面程序已经把主要动作做成了可视化入口，但底层仍有一部分逻辑暂时通过这些 PowerShell 脚本执行。

## 当前脚本

- setup-global.ps1
  - 配置用户级入口。
  - 把 Claude、Codex 兼容客户端、Antigravity 的全局 Skills 指向 C:\AI-Hub。
  - 生成或更新 Claude 用户级 settings.json。

- use-profile.ps1
  - 配置项目级入口。
  - 把 global / frontend / backend Profile 应用到目标项目。
  - 按需要创建项目级 .claude、.agents、.agent、.codex 目录或文件。
  - 写入项目级 .mcp.json、.codex\config.toml、.claude\settings.json。

- sync-mcp.ps1
  - 根据 C:\AI-Hub\mcp\manifest\*.json 生成客户端专用的 MCP 配置。
  - 输出 Claude、Codex、Antigravity 三端配置。
  - 把 global MCP 服务器合并到 rontend 和 ackend 的生成结果里。

- hooks\session-start.ps1
  - Claude SessionStart Hook 模板。
  - 可以替换成你自己的会话初始化逻辑。

- hooks\pre-tool-check.ps1
  - Claude PreToolUse Hook 模板。
  - 可以替换成你自己的检查、限制或保护逻辑。

## 典型用法

### 1. 重新生成 MCP 配置

`powershell
powershell -ExecutionPolicy Bypass -File C:\AI-Hub\scripts\sync-mcp.ps1
`

### 2. 重建全局入口

`powershell
powershell -ExecutionPolicy Bypass -File C:\AI-Hub\scripts\setup-global.ps1
`

### 3. 给项目应用 Profile

`powershell
powershell -ExecutionPolicy Bypass -File C:\AI-Hub\scripts\use-profile.ps1 -ProjectPath "C:\OverSeaFramework" -Profile frontend
`

## 什么时候运行哪个脚本

- 新增或修改了 MCP 服务器配置：运行 sync-mcp.ps1
- 新电脑或重置全局环境：运行 setup-global.ps1
- 新项目或项目切换了 Profile：运行 use-profile.ps1

## 与桌面程序的关系

当前桌面程序已经提供：

- “项目与 Profile”页：封装 setup-global.ps1 与 use-profile.ps1
- “MCP 管理”页：封装 sync-mcp.ps1
- “脚本中心”页：可以直接运行这个目录下的脚本和 Hook

所以正常情况下，你不一定需要手工打开终端执行它们。

## 安全行为

- 覆盖已有目标文件前，会先生成 .bak.<timestamp> 备份
- 只替换这些脚本负责管理的入口点和链接
- 旧的 ~/.codex/skills 默认不会被强制替换，因为历史会话可能仍在占用
