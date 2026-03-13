# ADR-0001：构建 AI-Hub 桌面控制台

## 状态

已接受

## 日期

2026-03-07

## 背景

AI-Hub 目前已经承担了统一工作区的角色，主要集中管理以下内容：

- `skills/global`、`skills/frontend`、`skills/backend` 下的技能资源
- `claude/` 下与 Claude、Codex 等相关的 profile 资源
- `mcp/` 下的 MCP 服务源码、清单与生成后的客户端配置
- `scripts/` 下的运维脚本

当前操作方式仍然以脚本为主。`setup-global.ps1`、`use-profile.ps1`、
`sync-mcp.ps1` 已覆盖核心流程，但它们依赖手工输入路径、终端执行，以及
对 profile 覆盖规则的隐性理解。

下一步目标是提供一个桌面应用，让 AI-Hub 更易用、更安全，并支持以下能力：

- 通过可视化方式执行现有 AI-Hub 操作
- 集中管理 skills、commands、agents 与 MCP 资产
- 明确区分全局级、profile 级、项目级配置
- 在全局上下文与项目上下文之间快速切换
- 通过一键方式启用或禁用 MCP 集成
- 在服务模型允许的前提下，后续支持 MCP 的启动、停止与重启
- 统一生成面向 Claude、Codex、Antigravity 等客户端的配置
- 不再硬编码 `C:\AI-Hub` 路径
- 长期以程序本身作为主要维护对象，而不是持续扩张脚本体系

当前 MCP 模型已经支持 `global`、`frontend`、`backend` 叠加，但仍缺少显式
的项目注册表、GUI 工作流和运行态状态层。

## 决策驱动因素

- 必须保留一个 AI-Hub 根目录作为事实源，但该根目录必须可配置、可发现，
  不能写死
- 当前必须适配 Windows 优先的使用环境
- 后续应保留对 macOS 的支持路径
- 必须同时支持全局级与项目级 profile 应用
- 必须支持从一个标准化模型生成多个 MCP 客户端配置
- 必须区分“配置型 MCP”与“进程型 MCP”
- 应尽量降低迁移风险，先包裹现有脚本，再逐步迁移为强类型代码
- 应逐步收敛到“程序是主要维护面”，避免长期脚本膨胀

## 备选方案

### 方案 1：继续使用脚本和文档

- 优点：工程成本最低，不新增应用程序维护成本
- 缺点：可发现性差，没有统一状态视图，难以扩展到项目级与 MCP 生命周期管理，重复操作体验差

### 方案 2：使用 Electron 或 Tauri 构建桌面壳

- 优点：界面灵活，前端生态成熟，容易做出丰富的桌面交互
- 缺点：核心本地能力会引入第二套主要语言与运行时，文件系统与进程管理桥接复杂，
  对于以本地运维与配置为核心的产品并不占优

### 方案 3：使用 .NET 8 + Avalonia 构建跨平台桌面应用

- 优点：UI 与编排逻辑可以统一在同一主语言栈中，适合 Windows 当前环境，也保留 macOS 路径，
  更适合文件系统、进程管理、配置生成这类本地工具场景
- 缺点：UI 生态相对 Web 技术栈更小，Windows 与 macOS 的平台差异仍需通过抽象层解决

## 决策

采用 `.NET 8 + Avalonia` 构建 AI-Hub 桌面控制台，并使用分层架构逐步替代现有
PowerShell 脚本。

`AI-Hub` 继续作为 skills、profiles、MCP 清单、生成配置和运维元数据的事实源，
但根目录路径必须可配置。程序应通过用户设置、环境变量或手动选择目录来识别
Hub 根目录，而不是假定固定的 `C:\AI-Hub`。

实施节奏分为四步：

1. 先构建 GUI 外壳，调用并监控现有脚本。
2. 增加项目、上游 skill 来源、MCP 运行态等状态层。
3. 将关键编排逻辑从脚本迁移到强类型应用服务。
4. 最终仅保留少量兼容脚本或扩展脚本入口。

## 决策理由

AI-Hub 目前已经具备“本地控制平面”的资源结构。真正缺少的不是数据和目录，而是：

- 可视化管理界面
- 显式状态层
- 更强的生命周期语义

Avalonia 在当前阶段是最合适的选择，因为它兼顾了两个关键目标：

- 适合本地文件、进程、配置管理这类强本地能力场景
- 保留未来支持 macOS 的现实路径，而不是将来整体重写

与 Web 外壳相比，AI-Hub 更像一个本地运维产品，它的核心工作包括：

- 应用 profile 链接与本地配置落盘
- 生成 JSON 与 TOML 配置
- 管理本地工具与 MCP 运行时
- 校验路径、依赖与环境变量

同时，使用 .NET 统一 UI 与编排逻辑，也更符合“以后主要维护程序本身”的长期目标。

## 影响

### 正面影响

- AI-Hub 将获得一个统一的 skills、profiles、MCP、项目操作入口
- 迁移初期可以继续利用现有脚本，不必一次性重写
- 全局级、profile 级、项目级边界会更清晰
- 多客户端 MCP 配置可以集中生成与管理
- 以后接入上游 skill 同步时不必重做使用流程
- 主实现语言统一为 C#，长期维护成本更可控
- 架构上保留了后续接入 macOS 的空间

### 负面影响

- AI-Hub 生态内会新增一个需要长期维护的应用程序代码库
- 平台差异仍需通过抽象层显式处理
- 一部分脚本行为在暴露为按钮前需要先标准化
- MCP 运行控制比单纯配置生成更复杂，因为并非所有 MCP 都是常驻进程

### 风险

- 如果把所有 MCP 都当作可启动/停止的守护进程，会产生误导性的 UI
- 如果不尽早引入平台抽象，Windows 假设会在未来阻碍 macOS 支持
- 如果项目级覆盖规则定义不清晰，后续配置会变得难以理解

## 实施说明

### 仓库结构

程序源码应放在 AI-Hub 仓库内部，但为了保持根目录整洁，统一下沉到 `desktop/` 目录，这样功能变更与资源变更仍可一次性版本化。

推荐的顶层结构如下：

- `desktop/apps/AIHub.Desktop`：Avalonia 桌面程序
- `desktop/src/AIHub.Core`：业务规则与领域模型
- `desktop/src/AIHub.Application`：用例与编排服务
- `desktop/src/AIHub.Infrastructure`：存储、进程、文件系统、配置生成
- `desktop/src/AIHub.Platform.Windows`：Windows 平台特有实现
- `desktop/src/AIHub.Platform.Mac`：macOS 平台特有实现
- `desktop/src/AIHub.Contracts`：DTO 与共享契约
- `skills/`、`mcp/`、`claude/`、`projects/`、`config/`：Hub 管理的数据目录
- `scripts-legacy/`：迁移期间保留的兼容脚本

### 产品模块

- `Dashboard`：Hub 状态、当前 profile、路径链接、近期操作、健康状态
- `Profiles`：将 `global`、`frontend`、`backend` 应用到项目
- `Projects`：注册项目、分类项目、切换全局/项目上下文
- `Skills`：查看已安装 skills，后续支持同步、更新、移除
- `MCP`：管理清单、启用/禁用服务、生成配置、测试可用性、展示运行态
- `Scripts`：仅作为兼容期的旧脚本入口
- `Logs`：执行日志、文件变更、生成结果、失败信息

### 状态模型

建议新增以下元数据文件：

- `hub.json`：标记当前目录为 AI-Hub 根目录，并存放基础元数据
- `config/hub-settings.json`：应用设置与默认值
- `projects/projects.json`：项目注册信息及分配的 profile
- `skills/sources.json`：skill 上游来源清单，用于同步
- `mcp/runtime.json`：受控 MCP 进程的 PID、端口、健康状态、最近运行信息

### 范围模型

配置优先级应定义为：

1. `global`
2. `profile`，例如 `frontend` 或 `backend`
3. `project`

现有 `mcp/manifest/*.json` 模型不应被推翻，而应在其基础上扩展项目级覆盖。

### MCP 生命周期模型

UI 必须区分两类 MCP：

1. `配置型 MCP`
   适用于由 Claude、Codex 等客户端按需拉起的 `stdio` 型 MCP。
   这类 MCP 在 UI 中应提供启用、禁用、编辑参数、重新生成配置、诊断测试等能力，
   但不应伪造“常驻运行中”状态。

2. `进程型 MCP`
   适用于可以常驻运行的本地服务或守护进程。
   这类 MCP 才适合提供启动、停止、重启、日志、健康检查和自启动策略等能力。

### 迁移规则

后续新的核心功能应优先实现为程序内能力，而不是新增主脚本。
现有脚本可先被 GUI 包装，随后在对应应用服务完成后退役，或迁入 `scripts-legacy/`。

## 相关决策

- 暂无

## 参考

- `scripts/setup-global.ps1`
- `scripts/use-profile.ps1`
- `scripts/sync-mcp.ps1`
- `docs/migration-notes.md`
- `docs/architecture/avalonia-solution-blueprint.md`



