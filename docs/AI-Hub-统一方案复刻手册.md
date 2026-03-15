# AI-Hub 统一方案复刻手册

更新日期: 2026-03-07
状态: 已按本文方案落地到当前机器，文中内容以 `C:\AI-Hub` 的真实现状为准。

## 2026-03-15 四层资源补充

当前复刻方案已经从“公司全局 + 个人 skills + 项目 profile”升级为完整的四层模型：

- 全局公司层：`C:\AI-Hub`
- 全局私人层：`C:\Users\Administrator\AI-Personal`
- 项目公司层：当前项目所选 `Profile`
- 项目私人层：当前项目所选 `Profile` 在 `AI-Personal` 下的对应资源

`AI-Personal` 现在承载的不再只是 `skills`，而是完整补齐：

- `skills/<profile>`
- `claude/commands/<profile>`
- `claude/agents/<profile>`
- `claude/settings/<profile>.settings.json`
- `mcp/manifest/<profile>.json`

所有用户级和项目级入口不再直接指向原始层目录，而是先物化到：

- `C:\AI-Hub\.runtime\effective\global`
- `C:\AI-Hub\.runtime\effective\frontend`
- `C:\AI-Hub\.runtime\effective\backend`

再由入口目录链接到这套“有效输出”。

首次全局接管和首次项目接管都会先扫描现有资源，并通过桌面端向导决定：

- 导入到 `AI-Hub`
- 导入到 `AI-Personal`
- 忽略

## 1. 这份文档的定位

这份文档的用途不是再手工重写一遍脚本，而是让你在其他电脑上可以按同一套结构直接复刻。

当前单一事实源是:

- 共享根目录: `C:\AI-Hub`
- 脚本目录: `C:\AI-Hub\scripts`
- 正式说明文档目录: `C:\AI-Hub\docs`

原则:

- 目录结构和运行方式以 `C:\AI-Hub` 中真实文件为准
- 本文负责说明设计、迁移、复刻和验证
- 文档只长期维护在 `C:\AI-Hub\docs`
- 不再把过长的脚本源码内嵌进手册，避免手册和实际脚本再次脱节

## 2. 设计结论

统一方案分三层:

- 公司共享层: `skills`、`agents`、`mcp`、`scripts`
- Claude 专属适配层: `claude\commands`、`claude\settings`
- 个人专属层: `C:\Users\Administrator\AI-Personal\skills\global`

原因:

- Claude / Codex / Antigravity 的稳定共同交集已经扩展到 `skills + agents + MCP`
- Claude 仍原生支持 `commands / hooks`
- Codex 当前公开稳定约定仍以 `AGENTS.md + skills + MCP` 为主
- 共享 agent 内容需要在仓库层集中维护，再按客户端做入口适配
- 个人 skill 不能直接写进共享库，否则会影响全公司

因此:

- 真正的工作流逻辑优先写进 `skills`
- Claude 的 commands 只做快捷包装
- 共享 agent 事实源统一维护在 `agents`
- hooks 只做触发共享脚本，不承载核心逻辑
- 公司共享内容维护在 `C:\AI-Hub`
- 个人 skill 维护在 `AI-Personal`

## 3. 当前最终目录结构

当前 `C:\AI-Hub` 建议固定为:

```text
C:\AI-Hub
  agents
    global
    frontend
    backend
  skills
    global
    frontend
    backend
  claude
    commands
      global
      frontend
      backend
    settings
      global.settings.json
      frontend.settings.json
      backend.settings.json
  mcp
    manifest
      global.json
      frontend.json
      backend.json
    generated
      claude
      codex
      antigravity
    servers
      @modelcontextprotocol
        server-github
      @upstash
        context7-mcp
      cclsp
    examples
      global-github.manifest.example.json
      frontend-unity-cclsp.manifest.example.json
    configs
      unity-cclsp.json
  scripts
    setup-global.ps1
    use-profile.ps1
    sync-mcp.ps1
    hooks
      session-start.ps1
      pre-tool-check.ps1
  docs
    AI-Hub-快速操作手册.md
    AI-Hub-统一方案复刻手册.md
    migration-notes.md
```

个人层固定为:

```text
C:\Users\Administrator\AI-Personal
  skills
    global
      <your-personal-skill>
        SKILL.md
```

目录职责:

- `skills\global`: 所有项目通用的公司共享技能
- `skills\frontend`: 前端类项目启用的公司共享技能
- `skills\backend`: 后端类项目启用的公司共享技能
- `agents\*`: Claude / Codex 共享的 agent 事实源
- `claude\commands\*`: 只给 Claude 的 slash commands
- `claude\settings\*`: Claude 的全局和项目模板配置
- `mcp\manifest\*`: MCP 的单一事实源
- `mcp\generated\*`: 生成给各客户端消费的配置
- `mcp\servers\*`: 保留的 MCP 源码或包目录
- `scripts\*`: 接入和生成脚本
- `AI-Personal\skills\global`: 只在你个人机器上生效的 skills

## 4. 当前真实迁移结果

你当前原始目录:

- `C:\Users\Administrator\Desktop\整理后SKill`
- `C:\Users\Administrator\Desktop\McpServer`

已经迁入 `C:\AI-Hub` 的内容:

- `Global\skills` -> `skills\global`
- `前端\Skills` -> `skills\frontend`
- `后端\Skills` -> `skills\backend`
- `Global\commands` -> `claude\commands\global`
- `后端\commands` -> `claude\commands\backend`
- `Global\agents` -> `agents\global`
- `前端\Agent` -> `agents\frontend`
- `后端\agents` -> `agents\backend`
- `McpServer` 中以下目录已复制进 `mcp\servers`:
  - `@upstash\context7-mcp`
  - `@modelcontextprotocol\server-github`
  - `cclsp`

迁移时的规则:

- 共享流程尽量进入 `skills`
- 共享 agent 统一收口到 `agents`
- Claude 专属包装保留在 `claude\commands`
- `node_modules` 不作为长期事实源保留

## 5. 当前机器上的真实接入状态

已经完成的用户级入口:

```text
C:\Users\Administrator\.claude\skills
  company  -> C:\AI-Hub\skills\global
  personal -> C:\Users\Administrator\AI-Personal\skills\global

C:\Users\Administrator\.agents\skills
  company  -> C:\AI-Hub\skills\global
  personal -> C:\Users\Administrator\AI-Personal\skills\global

C:\Users\Administrator\.gemini\antigravity\skills
  company  -> C:\AI-Hub\skills\global
  personal -> C:\Users\Administrator\AI-Personal\skills\global
```

其他用户级入口:

- `C:\Users\Administrator\.claude\commands` -> `C:\AI-Hub\claude\commands\global`
- `C:\Users\Administrator\.claude\agents` -> `C:\AI-Hub\agents\global`
- `C:\Users\Administrator\.agents\agents` -> `C:\AI-Hub\agents\global`
- `C:\Users\Administrator\.claude\settings.json` 已由模板渲染生成

Codex 兼容策略:

```text
C:\Users\Administrator\.codex\skills
  .system
  ai-hub   -> C:\AI-Hub\skills\global
  personal -> C:\Users\Administrator\AI-Personal\skills\global
```

说明:

- `~/.agents/skills` 仍是主要全局入口
- `~/.codex/skills` 保留 `.system`，不整体替换
- 旧版兼容通过 `ai-hub` 和 `personal` 两个子目录完成

项目级已落地实例:

- 项目: `C:\OverSeaFramework`
- 已应用 profile: `frontend`
- 已创建项目入口:
  - `.claude\skills`
  - `.claude\commands`
  - `.claude\agents`
  - `.agents\agents`
  - `.agents\skills`
  - `.agent\skills`
  - `.claude\settings.json`
  - `.mcp.json`
  - `.codex\config.toml`

补充说明:

- `C:\OverSeaFramework` 实际上更像 Unity / C# 客户端项目，不是典型 Web 前端仓库
- 之所以仍挂到 `frontend`，是因为你当前“前端”资源里包含 `csharp-pro.md` 和 `unity-developer.md`

## 6. 全局、项目级、个人级如何生效

生效范围不是靠在 `SKILL.md` 里打标签，而是靠入口目录决定。

规则:

- 公司全局生效: 用户目录入口中的 `company` 指向 `skills\global`
- 个人全局生效: 用户目录入口中的 `personal` 指向 `AI-Personal\skills\global`
- 项目生效: 项目目录入口指向 `skills\frontend` 或 `skills\backend`

因此:

- 不要把 `global`、`frontend`、`backend` 混在一个入口目录下
- 不要直接把个人 skill 写进 `C:\AI-Hub`
- 不要直接把 skill 内容写进 `~/.claude/skills` 根目录，而是写进 `AI-Personal`

## 7. 三端接入原则

### 7.1 Claude Code

Claude 使用两层入口:

- 用户级: `~/.claude`
- 项目级: `项目\.claude`

Claude 可直接消费:

- `skills`
- `commands`
- `agents`
- `hooks`
- `MCP`

推荐:

- 用户级 `skills` 根目录下只保留 `company` 和 `personal`
- `company` 链接到 `C:\AI-Hub\skills\global`
- `personal` 链接到 `C:\Users\Administrator\AI-Personal\skills\global`
- `settings.json` 通过模板渲染生成，不手工长期维护

### 7.2 Codex

Codex 当前建议这样理解:

- 主入口使用 `AGENTS.md`、`~/.agents\skills`、`项目\.agents\skills`
- 共享 agent 目录使用 `~/.agents\agents` 和 `项目\.agents\agents`
- 旧版兼容使用 `~/.codex/skills\ai-hub` 与 `~/.codex/skills\personal`

推荐:

- 把 `~/.agents/skills` 视为主要全局入口
- 把 `项目\.agents\skills` 视为主要项目入口
- 把共享 agent 内容视为 `agents\*` 与 `AGENTS.md`
- Claude 的 `commands`、`hooks` 仍属于 Claude 专属适配层
- 个人 skill 一律放 `AI-Personal`，不要直接塞进 `.codex\skills` 根目录

### 7.3 Antigravity

当前采用最大公约数接法:

- 用户级 skills -> `~/.gemini/antigravity/skills`
- 项目级 skills -> `项目\.agent\skills`
- MCP 用生成后的配置文件接入

建议:

- 用户级 `skills` 根目录下只保留 `company` 和 `personal`
- Claude 独有的 `commands / hooks / agents` 不当作 Antigravity 的可移植能力

## 8. 当前脚本的真实职责

真实脚本以 `C:\AI-Hub\scripts` 为准。

### `setup-global.ps1`

作用:

- 建立用户级 Claude / Codex 兼容 / Antigravity 技能入口
- 建立个人 skills 根目录 `AI-Personal\skills\global`
- 渲染 Claude 全局 `settings.json`

当前特性:

- 支持参数 `HubRoot`
- 支持参数 `UserHome`
- 支持参数 `PersonalRoot`
- 支持参数 `SkipLegacyCodexPath`
- 如果目标已经是正确状态，不会重复制造新的 `.bak`
- 只有真正发生替换时，才会留下备份

### `use-profile.ps1`

作用:

- 给单个项目套用 `frontend` 或 `backend` profile
- 建立项目级 `.claude`、`.agents`、`.agent`、`.codex` 入口
- 写入项目级 `settings.json`、`.mcp.json`、`.codex\config.toml`

当前特性:

- 会先校验 `ProjectPath` 是否存在
- 如果目标已经正确，不会重复制造新的 `.bak`
- Claude `settings.json` 按 `HubRoot` 渲染模板

### `sync-mcp.ps1`

作用:

- 从 `mcp\manifest\*.json` 生成 Claude / Codex / Antigravity 使用的 MCP 配置

当前特性:

- 兼容当前 PowerShell 环境，不依赖 `ConvertFrom-Json -AsHashtable`
- `frontend` / `backend` 生成时会自动合并 `global`
- 生成结果输出到 `mcp\generated\*`

### `scripts\hooks\*.ps1`

作用:

- 提供 Claude hook 实际执行的脚本体
- 当前是最小可运行模板，可继续替换为你的真实检查逻辑

## 9. 当前 MCP 策略

当前默认启用:

- `mcp\manifest\global.json` -> Context7

当前默认未启用，但已经迁入并给了示例模板:

- `server-github`
- `cclsp`

为什么没默认启用:

- GitHub MCP 需要 `GITHUB_PERSONAL_ACCESS_TOKEN`
- `cclsp` 对 Unity / C# 还需要语言服务器环境，例如 `omnisharp`

当前生成行为:

- `global` 生成 `global.mcp.json` / `global.config.toml`
- `frontend` 生成时会自动包含 `global + frontend`
- `backend` 生成时会自动包含 `global + backend`

所以项目接入 `frontend` 时，仍然会拿到 `global` 里的 Context7。

## 10. 新电脑复刻步骤

在新电脑上推荐这样执行，不需要手工重写脚本。

### 10.1 准备环境

确保具备:

- Git
- PowerShell
- Node.js
- Claude Code
- Codex
- Antigravity

### 10.2 放置共享仓库

建议固定克隆到:

```powershell
git clone <你的仓库地址> C:\AI-Hub
```

### 10.3 生成 MCP 配置

```powershell
powershell -ExecutionPolicy Bypass -File C:\AI-Hub\scripts\sync-mcp.ps1
```

### 10.4 安装全局入口

```powershell
powershell -ExecutionPolicy Bypass -File C:\AI-Hub\scripts\setup-global.ps1
```

执行后会自动创建:

- `C:\Users\<你的用户名>\AI-Personal\skills\global`
- 用户级 `company + personal` 双层 skills 入口

### 10.5 给项目套用 profile

前端类项目:

```powershell
powershell -ExecutionPolicy Bypass -File C:\AI-Hub\scripts\use-profile.ps1 -ProjectPath "C:\OverSeaFramework" -Profile frontend
```

后端类项目:

```powershell
powershell -ExecutionPolicy Bypass -File C:\AI-Hub\scripts\use-profile.ps1 -ProjectPath "D:\Code\trade-api" -Profile backend
```

### 10.6 验证

检查全局入口:

- `~/.claude/skills\company`
- `~/.claude/skills\personal`
- `~/.claude/commands`
- `~/.claude/agents`
- `~/.agents/agents`
- `~/.agents/skills\company`
- `~/.agents/skills\personal`
- `~/.gemini/antigravity/skills\company`
- `~/.gemini/antigravity/skills\personal`
- 可选兼容检查: `~/.codex/skills\ai-hub`
- 可选兼容检查: `~/.codex/skills\personal`

检查项目入口:

- `项目\.claude\skills`
- `项目\.claude\commands`
- `项目\.claude\agents`
- `项目\.agents\agents`
- `项目\.agents\skills`
- `项目\.agent\skills`
- `项目\.mcp.json`
- `项目\.codex\config.toml`

## 11. 日常维护时怎么做

### 11.1 改了公司共享 skill

通常不用跑脚本。

原因:

- 入口已经通过 junction 直连到 `C:\AI-Hub`
- 你改完 `skills`，三端下次读取就会看到

### 11.2 改了个人 skill

通常也不用跑脚本。

做法:

- 直接改 `C:\Users\Administrator\AI-Personal\skills\global`

### 11.3 改了 Claude command / agent / hook 脚本

通常也不用跑脚本。

例外:

- 如果改了 `claude\settings\*.json` 模板，需要重新执行 `setup-global.ps1` 或 `use-profile.ps1`

### 11.4 改了 MCP manifest

必须先跑:

```powershell
powershell -ExecutionPolicy Bypass -File C:\AI-Hub\scripts\sync-mcp.ps1
```

然后对已接入的项目重新跑一次 `use-profile.ps1`。

### 11.5 新项目接入

步骤:

1. 判断项目属于 `frontend` 还是 `backend`
2. 执行 `use-profile.ps1`

## 12. 当前推荐的验证方法

### Claude

- 打开 Claude Code
- 确认全局 `company` 与 `personal` 两层 skills 都可见
- 打开项目后确认项目 profile 对应能力可见

### Codex

- 优先检查 `~/.agents/skills`
- 在项目里检查 `项目\.agents\skills`
- 如为旧版兼容，再看 `~/.codex/skills\ai-hub` 与 `~/.codex/skills\personal`
- 检查项目 `.codex\config.toml` 是否包含期望的 MCP

### Antigravity

- 检查 `~/.gemini/antigravity/skills`
- 检查项目 `项目\.agent\skills`
- 检查生成后的 MCP 配置是否可被读取

## 13. 排错指南

### 看不到公司共享 skill

排查:

- `company` junction 是否创建成功
- 目标是否指向 `C:\AI-Hub`
- skill 目录下是否存在 `SKILL.md`
- 项目是否从正确根目录打开

### 看不到个人 skill

排查:

- `personal` junction 是否创建成功
- 目标是否指向 `AI-Personal\skills\global`
- 个人 skill 目录下是否存在 `SKILL.md`

### Claude 有 skill 但没有 command

排查:

- `~/.claude/commands` 是否存在
- 项目 `.claude\commands` 是否指向正确 profile
- command 文件是否放在正确目录

### Codex 看不到共享 skills

优先排查:

- `~/.agents/skills\company` 是否存在
- 项目里是否存在 `.agents\skills`
- 如为旧版兼容，再看 `~/.codex/skills\ai-hub`

### MCP 不生效

排查:

- `mcp\manifest` 是否是合法 JSON
- 是否执行了 `sync-mcp.ps1`
- 是否重新执行了 `use-profile.ps1`
- 项目 `.mcp.json` 和 `.codex\config.toml` 是否包含期望内容
- Node / npx 是否在 PATH 中

### Hook 不生效

排查:

- `settings.json` 是否由模板重新渲染过
- hook command 里的路径是否指向 `C:\AI-Hub\scripts\hooks\...`
- 脚本是否被 PowerShell 执行策略阻止

### `.bak` 文件太多

结论:

- 如果当前入口和配置已经正确，旧的 `.bak.<timestamp>` 可以直接删除
- 当前脚本已经改成只有在真正发生替换时才生成备份

## 14. GitHub 同步策略

强烈建议把 `C:\AI-Hub` 作为 Git 仓库管理，并推送到 GitHub。

建议:

- 仓库名: `ai-hub`
- 使用私有仓库
- 只提交源码、文档、manifest、settings、scripts
- 不提交运行时缓存

建议 `.gitignore`:

```gitignore
node_modules/
dist/
.DS_Store
Thumbs.db
*.log
mcp/generated/
```

同步方式:

1. 主电脑维护 `C:\AI-Hub`
2. 推送到 GitHub
3. 新电脑克隆到同样的 `C:\AI-Hub`
4. 运行 `sync-mcp.ps1`
5. 运行 `setup-global.ps1`
6. 对每个项目运行 `use-profile.ps1`

## 15. 长期维护原则

请长期坚持:

- 一切公司共享能力优先进入 `C:\AI-Hub\skills` 和 `C:\AI-Hub\agents`
- 一切个人专属 skill 只进入 `C:\Users\Administrator\AI-Personal\skills\global`
- 一切外部工具入口优先进入 `mcp\manifest`
- 一切自动化脚本优先进入 `scripts`
- Claude 独有体验再放到 `claude\commands`、`claude\settings`
- 所有生成文件都不手工长期维护
- 任何项目接入都用脚本，不手工逐个点改

如果后续你新增更多 profile，例如 `mobile`、`devops`、`data`，直接按当前结构横向扩展即可。

## 16. 当前机器上的参考文档

当前最值得看的文档是:

- `C:\AI-Hub\docs\AI-Hub-快速操作手册.md`
- `C:\AI-Hub\docs\AI-Hub-统一方案复刻手册.md`
- `C:\AI-Hub\docs\migration-notes.md`

这三份文档加上 `C:\AI-Hub\scripts\*.ps1`，已经足够支撑新电脑复刻和后续维护。
