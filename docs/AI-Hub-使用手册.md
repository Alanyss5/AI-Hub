# AI-Hub 使用手册

更新日期：2026-03-19

## 1. 这版的核心变化

AI-Hub 当前采用统一的四层资源模型：

1. 全局公司层：`C:\AI-Hub`
2. 全局私人层：`C:\Users\Administrator\AI-Personal`
3. 项目公司层：所选 `Profile` 在 `AI-Hub` 下的目录
4. 项目私人层：所选 `Profile` 在 `AI-Personal` 下的目录

最终生效顺序固定为：

1. 全局公司
2. 全局私人
3. 项目公司
4. 项目私人

同名冲突规则固定为：

- 项目层覆盖全局层
- 同层内私人覆盖公司

对外真正暴露给客户端和项目目录的不是四层原始目录，而是 `.runtime/effective/<profile>` 这套“已合并的有效输出”。

## 1.1 Profile Catalog 规则

当前版本开始，Profile 不再以固定枚举作为主入口，而是以
`C:\AI-Hub\config\profile-catalog.json` 作为唯一事实源。

你需要记住以下规则：

- `config/profile-catalog.json` 决定了“有哪些 profile 可以被项目、Skills、MCP 使用”
- 旧文档里提到的 `ProfileKind` 现在只用于兼容旧数据和旧流程，不再是新的业务真相
- 旧仓库如果还没有 `profile-catalog.json`，AI-Hub 会自动补齐三项默认 profile：
  `global`、`frontend`、`backend`
- `global` 是基础层，必须存在，不能删除

## 1.2 v1 支持范围

v1 只支持下面三件事：

1. 新增 profile
2. 删除未被引用的非 `global` profile
3. 修改 profile 的显示名 `DisplayName`

v1 不支持修改 profile Id。

如果你想把某个 profile 从 `frontend-tools` 改成 `client-tools`，正确做法不是直接改 Id，而是：

1. 新增 `client-tools`
2. 把项目、Skills、MCP 逐步迁过去
3. 确认旧 profile 已无引用
4. 再删除旧 profile

## 2. 四个按钮分别做什么

### 切换为全局级

只切换 AI-Hub 当前作用域到全局，不写用户目录。

### 切换为项目级

只切换 AI-Hub 当前作用域到当前项目，不写项目目录。

### 应用全局链接

会做三件事：

1. 首次时扫描现有全局 `Skills / commands / agents / Claude settings / MCP`
2. 通过接管向导决定导入到 `AI-Hub`、`AI-Personal` 或 `忽略`
3. 生成 `.runtime/effective/global`，再把用户目录入口切过去

### 应用项目 Profile

会做三件事：

1. 首次时扫描当前项目目录现有 `Skills / commands / agents / Claude settings / MCP`
2. 通过接管向导决定导入到 `AI-Hub`、`AI-Personal` 或 `忽略`
3. 生成 `.runtime/effective/<profile>`，再把项目入口切过去

补充说明：

- 项目页里的 Profile 下拉项来自 `config/profile-catalog.json`
- 如果当前选中的 profile 已不在 catalog 中，项目保存、设为当前项目、项目接管都应先改回有效 profile 后再执行

## 3. 首次接管向导怎么理解

首次全局接管和首次项目接管都会扫描以下资源：

- Skills
- Claude commands
- Claude agents
- Claude settings
- MCP

每一项都支持三种选择：

- 导入到 `AI-Hub`
- 导入到 `AI-Personal`
- 忽略

导入策略固定为“复制导入”：

- 原位置保留
- 目标已存在时先创建 `.bak.<timestamp>` 备份
- 不做原地搬迁

## 4. 为什么项目目录里看不到单独的 global

这是当前实现的正常行为。

“全局层是否生效”不再通过项目目录里出现一个 `global` 子目录来体现，而是通过：

- 项目 `.claude\skills`
- 项目 `.agents\skills`
- 项目 `.agent\skills`

这些入口统一指向：

- `C:\AI-Hub\.runtime\effective\<profile>\skills`

也就是说，全局层已经和所选 `Profile` 合并进 effective 输出里了。

如果你要验证“全局是否生效”，请看：

1. 项目 skill 入口的实际目标路径
2. `C:\AI-Hub\.runtime\effective\<profile>` 下的合并结果

不要再去项目目录里找单独的 `global` 文件夹。

## 4.1 项目 / Skills / MCP 如何跟随动态分类

### 项目

- `projects/projects.json` 中的每个项目都保存一个 profile Id
- 项目页的下拉项来自 catalog，而不是写死的 `ProfileKind`
- 应用项目 Profile 时，AI-Hub 会把该项目入口切到 `.runtime/effective/<profile>`

### Skills

- 已安装 Skills 按 `skills/<profile>/...` 组织
- Skills 来源、安装记录、状态记录中的 `profile` 字段都必须引用 catalog 中存在的 profile
- 旧仓库没有 catalog 时，会先自动补齐 `global/frontend/backend`，再继续扫描 `skills/` 下的内容

### MCP

- 每个 profile 对应自己的 `mcp/manifest/<profile>.json`
- 生成给客户端的配置时，`global` 是基础层，非 `global` profile 会在此基础上叠加自己的 MCP
- MCP 总览应以 catalog 中的全部 profile 为范围，而不是只看固定三种分类

## 5. 路径不一致时为什么会被阻断

如果你已经选中了一个已登记项目，但表单里的目录被改成了另一个路径，例如：

- 当前登记路径：`C:\Project\OverSeaFramework`
- 当前表单路径：`C:\OverSeaFramework`

那么以下操作会被阻断，并弹出明确提示：

- `应用项目 Profile`
- `设为当前项目`
- `重新扫描项目接管`

这是刻意设计，不会自动迁移。

正确流程是：

1. 先点击 `新增或更新项目`
2. 把新路径保存到注册表
3. 再执行 `应用项目 Profile` 或项目重扫

## 6. 重扫为什么有时“没反应”

当前版本里，重扫有两种明确结果：

- 扫到候选项：进入接管向导
- 没有候选项：弹出“未发现可重新导入资源”的结果提示

提示框里会明确显示：

- 当前作用域
- 检查路径
- 当前 Profile（项目级时）

所以如果你点击重扫后看到的是结果提示框，表示这次扫描确实完成了，只是没有新增可导入项。

## 6.1 删除 Profile 为什么会失败

删除 profile 失败通常不是异常，而是保护机制在生效。

只要下面任意一项还在引用该 profile，删除就会被阻断：

- 已登记项目仍在使用它
- Skills 来源仍在使用它
- Skills 安装记录仍在使用它
- Skills 状态记录仍在使用它
- `skills/<profile>/` 下还有真实目录资产
- 该 profile 的 MCP manifest 仍有内容或仍被当作有效配置使用
- `hub-settings.json` 仍把它当默认 profile 或当前作用域所依赖的 profile

正确理解是：

- “删不掉”通常说明这个 profile 还在被系统使用
- 需要先迁移项目、Skills、MCP 和默认设置，再重试删除
- `global` 即使没有引用也不能删除

## 7. 如何确认自己打开的是 worktree 版程序

当前项目页会直接显示：

- 构建来源
- 可执行文件路径
- 当前 `HubRoot`

如果你是在本分支验收，应该看到可执行文件来自：

`C:\Users\Administrator\.config\superpowers\worktrees\AI-Hub\codex\four-layer-onboarding\desktop\apps\AIHub.Desktop\bin\Debug\net8.0\AIHub.Desktop.exe`

如果显示的是 `C:\AI-Hub\...` 下的 exe，说明你打开的还是主目录旧程序。

## 8. 最短验收路径

按下面顺序就能验证这轮修复：

1. 打开 worktree 版 `AIHub.Desktop.exe`
2. 在“项目与 Profile”页确认已经显示构建来源、可执行文件路径和 `HubRoot`
3. 选中 `OverSeaFramework`
4. 把项目目录保存为 `C:\OverSeaFramework`
5. 选择 `backend`
6. 点击 `应用项目 Profile`
7. 在结果区确认：
   - 实际项目路径是 `C:\OverSeaFramework`
   - Profile 是 `backend`
   - `.claude\skills`
   - `.agents\skills`
   - `.agent\skills`
   都指向 `.runtime/effective/backend/skills`
8. 点击 `重新扫描项目接管`
9. 如果没有新增候选项，应弹出“未发现可重新导入资源”的结果提示

## 9. 常见判断

### 什么时候只需要切换

当你只是想让 AI-Hub 之后的“当前作用域”操作改到全局或项目时，用“切换”。

### 什么时候必须应用

当你希望用户目录或项目目录里的真实入口、链接、配置文件发生变化时，用“应用”。

### 什么时候必须先保存

当你修改了已登记项目的目录路径，并且接下来还要做项目接管、设当前项目或项目重扫时，必须先保存。

### 什么时候需要先补 catalog

当你打开的是旧仓库，且还没有 `config/profile-catalog.json` 时，不需要手工创建。

AI-Hub 会自动补齐：

- `global`
- `frontend`
- `backend`

然后项目、Skills、MCP 页面都会以这三项作为初始动态分类继续工作。

## 2026-03-19 补充说明：动态分类最终能力

### Skills 可以单项绑定多个分类

- 现在不仅可以按 profile 浏览 Skills，也可以对单个 Skill 直接勾选多个 profile
- 你可以把某个 Skill 同时放进 `global`、`frontend`、`backend` 或自定义 profile
- 取消勾选某个 profile 后，该 Skill 会从该 profile 的目录与安装记录中移除

### 仓库型 Skills 按顶层文件夹统一管理

- 对于 `skills/<profile>/<repo-or-folder>/...` 这类结构，桌面端会把顶层文件夹当成一个技能组
- 例如 `skills/global/superpowers` 会直接按 `superpowers` 这个仓库组展示
- 详情区会显示这个组当前分配到了哪些 profile，以及组内包含的 skill 路径
- 这样在管理来自 GitHub 仓库的 Skills 时，不需要逐个翻目录才能看清来源

### MCP 可以单个 Server 绑定多个分类

- MCP 页面现在支持直接选择某一个 server，并勾选它要写入哪些 profile
- 保存后，该 server 会自动加入选中的 `mcp/manifest/<profile>.json`
- 同时它也会从未勾选的 profile manifest 中移除，实现自由增减

### 界面已切换为暗黑现代化风格

- 桌面端主窗口、Settings、Skills、MCP、项目页和对话框已经统一到深色主题
- 面板、输入框、列表、按钮使用统一的深色背景和高对比描边
- 这次调整重点是提高长时间使用时的可读性，并让 Skills / MCP 的配置区更聚焦
