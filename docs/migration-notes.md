# 迁移说明

日期：2026-03-07

已迁移到当前工作区 `AI-Hub` 的内容如下：

- `Desktop\整理后SKill\Global\skills` 中的 Skills 已迁移到 `skills\global`
- `Desktop\整理后SKill\前端\Skills` 中的 Skills 已迁移到 `skills\frontend`
- `Desktop\整理后SKill\后端\Skills` 中的 Skills 已迁移到 `skills\backend`
- `Global\commands` 中的 Claude commands 已迁移到 `claude\commands\global`
- `后端\commands` 中的 Claude commands 已迁移到 `claude\commands\backend`
- `Global\agents` 中的 agents 已迁移到 `agents\global`
- `前端\Agent` 中的 agents 已迁移到 `agents\frontend`
- `后端\agents` 中的 agents 已迁移到 `agents\backend`
- MCP 源目录已复制到 `mcp\servers`

当前 AI-Hub 资源统计：

- 全局 Skills：13
- 前端 Skills：1
- 后端 Skills：12
- 全局 Claude commands：15
- 后端 Claude commands：6
- 全局共享 agents：16
- 前端共享 agents：2
- 后端共享 agents：16
- MCP 服务源码目录：3

当前默认启用的内容：

- `mcp\manifest\global.json`：Context7
- `mcp\manifest\frontend.json`：空的 overlay 模板
- `mcp\manifest\backend.json`：空的 overlay 模板

当前 MCP 的重要行为：

- 生成后的 `frontend` 和 `backend` 配置会自动合并 `global` 与 profile 专属服务
- 这意味着只要项目使用 `frontend` 或 `backend` profile，仍然会继承 `global` 中的 MCP 条目，例如 Context7

已复制但默认未启用的服务：

- `mcp\servers\@modelcontextprotocol\server-github`
- `mcp\servers\cclsp`

当前未默认启用的原因：

- GitHub MCP 需要 `GITHUB_PERSONAL_ACCESS_TOKEN`
- `cclsp` 需要语言服务器配置以及外部语言服务器二进制文件

已为后续启用准备好的模板：

- `mcp\examples\global-github.manifest.example.json`
- `mcp\examples\frontend-unity-cclsp.manifest.example.json`
- `mcp\configs\unity-cclsp.json`

已应用到真实环境的内容：

- 用户级 `~/.claude/skills` 指向 AI-Hub 全局 Skills
- 用户级 `~/.claude/commands` 指向 AI-Hub 全局 Claude commands
- 用户级 `~/.claude/agents` 指向 AI-Hub 全局共享 agents
- 用户级 `~/.agents/agents` 指向 AI-Hub 全局共享 agents
- 用户级 `~/.agents/skills` 指向 AI-Hub 全局 Skills
- 用户级 `~/.gemini/antigravity/skills` 指向 AI-Hub 全局 Skills
- 用户级 `~/.claude/settings.json` 使用 AI-Hub 渲染后的全局设置

关于旧版 Codex 的说明：

- 现有 `~/.codex/skills` 因被当前 Codex 环境占用而未被改动
- 现在的共享全局 Codex 路径是 `~/.agents/skills`，较新的 Codex 构建版本会使用该路径

已应用到 `C:\OverSeaFramework` 的内容：

- 项目 profile：`frontend`
- 项目 `.claude\skills`、`.claude\commands`、`.claude\agents` 已链接到 AI-Hub 前端 profile
- 项目 `.agents\skills`、`.agents\agents` 与 `.agent\skills` 已链接到 AI-Hub 前端共享入口
- 项目 `.mcp.json` 当前写入的是合并后的前端 MCP 配置，并包含 Context7
- 项目 `.codex\config.toml` 当前写入的是合并后的 Codex MCP 配置，并包含 Context7
- 之前被覆盖的项目配置文件已使用 `.bak.<timestamp>` 后缀完成备份

关于项目分类的说明：

- `C:\OverSeaFramework` 更像是 Unity/C# 客户端项目，而不是 Web 前端仓库
- 按你当前资源划分方式，`frontend` profile 仍然比 `backend` 更贴近这个项目，因为前端共享 agents 中包含 `csharp-pro.md` 与 `unity-developer.md`
- `cclsp` 未被自动启用，因为 Unity 的安全默认配置取决于是否已经安装 `omnisharp` 或其他 C# LSP
