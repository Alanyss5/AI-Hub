# 迁移说明

日期：2026-03-07

## 当前动态 Profile / Catalog 迁移口径

从当前版本开始，profile 的真实来源不再是固定的 `ProfileKind`，而是
`config/profile-catalog.json`。

迁移与兼容规则如下：

- `config/profile-catalog.json` 是 profile catalog 的单一事实源
- 旧仓库如果没有这个文件，AI-Hub 会自动补齐 `global`、`frontend`、`backend`
- 旧数据里出现的 `ProfileKind` 或等价固定分类值，会在读取时归一化为字符串 profile Id
- `global` 是保底基础层，不可删除
- v1 只允许新增 profile、删除未引用的非 `global` profile、修改 `DisplayName`
- v1 不支持改 profile Id；如需“改 Id”，必须新增新 profile 后逐步迁移

### 绑定方式的迁移说明

当前版本已经不再把 `Skill` 或 `MCP server` 视为只能属于单一 profile 的资源，而是允许它们按“绑定关系”同时出现在多个 profile 中：

- 单个 skill 可以同时绑定到 `global`、`frontend`、`backend` 或任意自定义 profile
- 单个 MCP server 也可以同时绑定到多个 profile
- 解绑某个 profile 时，只会移除该 profile 对应的绑定，不会自动影响其他 profile
- `skills/global/superpowers` 这类顶层目录会作为一个可管理的技能组处理，展开后可查看其中包含的具体 skills

这意味着迁移时需要把“资源归属”的心智模型从“单选”改成“多选”，但 profile catalog 仍然保持为唯一事实源。

删除 profile 时，如仍被以下任一对象引用，删除必须失败：

- `projects/projects.json`
- `skills/sources.json`
- `config/skills-installs.json`
- `config/skills-state.json`
- `skills/<profile>/` 下的真实目录
- `mcp/manifest/<profile>.json` 中仍存在的 MCP 服务定义
- `config/hub-settings.json` 中的默认 profile 或当前作用域依赖

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

关于默认三类 profile 的补齐说明：

- 这三个默认 profile 现在既是旧仓库迁移时的自动补齐项，也是没有 catalog 时的兼容回填项
- 迁移完成后，项目、Skills、MCP 都应从 `config/profile-catalog.json` 读取 profile 列表
- 因此后续可以继续新增自定义 profile，而不是把分类能力固定死在 `global/frontend/backend`

当前 MCP 的重要行为：

- 生成后的 `frontend` 和 `backend` 配置会自动合并 `global` 与 profile 专属服务
- 这意味着只要项目使用 `frontend` 或 `backend` profile，仍然会继承 `global` 中的 MCP 条目，例如 Context7
- 新增的自定义 profile 也会参与同样的叠加逻辑，只要对应 server 已绑定到该 profile

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

## 对旧仓库操作者的建议

如果你正在从固定三分类的旧仓库迁移到当前版本，请按下面的心智模型理解：

1. 先让 AI-Hub 自动补齐 `profile-catalog.json`
2. 确认 catalog 中至少有 `global/frontend/backend`
3. 再检查项目、Skills、MCP 是否都已经引用到 catalog 中的 profile Id
4. 如后续要细分分类，不要改旧 Id，而是新增新 profile 后逐步迁移

## 本轮补齐能力

当前动态 profile 改造已经从“分类列表动态化”扩展到“单项资源可跨分类绑定”：

- 单个 Skill 可以同时存在于多个 profile 中，桌面端支持直接勾选 profile 并保存
- 顶层仓库/文件夹型 Skills 现在按组管理，像 `skills/global/superpowers` 这样的目录会作为一个组展示，并可查看其中包含的 skill
- 单个 MCP server 可以直接分配到多个 profile，保存时会把该 server 加入选中的 manifest，并从未选中的 manifest 中移除
- MCP generated / effective outputs 会跟随 profile catalog 动态生成，自定义 profile 与默认三类享有同等待遇
- 桌面端视觉风格已统一为暗黑现代化主题，不再维持旧的浅色混搭样式

仍然保持不变的 v1 规则：

- profile Id 仍然是稳定标识，不支持直接 rename
- `global` 永久保留，不允许删除
- 删除 profile 时不会自动迁移 Skill / MCP / Project 绑定，必须先清理引用
