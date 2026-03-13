# Avalonia 方案蓝图

## 目标

将 AI-Hub 构建为单仓库桌面应用，使程序本身成为 skills、profiles、projects 与
MCP 管理的主要维护入口。

应用应满足以下要求：

- 使用可配置的 AI-Hub 根目录，而不是硬编码路径
- 将现有脚本视为迁移期兼容工具，而不是最终后端架构
- 在同一仓库内同时维护资源数据与应用代码
- 为未来的 macOS 支持保留清晰路径
- 保持 `C:\AI-Hub` 根目录主要承载 Hub 数据与入口元数据，避免源码直接铺在根目录

## 推荐仓库结构

```text
AI-Hub/
  hub.json
  README.md
  config/
    hub-settings.json
  projects/
    projects.json
  skills/
    global/
    frontend/
    backend/
    sources.json
  mcp/
    manifest/
    generated/
    servers/
    runtime.json
  claude/
  docs/
    adr/
    architecture/
  scripts/
  scripts-legacy/
  desktop/
    AIHub.sln
    Directory.Build.props
    Directory.Packages.props
    apps/
      AIHub.Desktop/
    src/
      AIHub.Contracts/
      AIHub.Core/
      AIHub.Application/
      AIHub.Infrastructure/
      AIHub.Platform.Windows/
      AIHub.Platform.Mac/
    tests/
      AIHub.Core.Tests/
```

## 模块职责

### `AIHub.Contracts`

用于存放共享请求/响应契约、DTO、序列化模型和枚举。

示例：

- `HubLocation`
- `ProjectRecord`
- `SkillSourceRecord`
- `McpServerDefinition`
- `McpRuntimeRecord`

### `AIHub.Core`

用于存放纯业务规则和领域概念。该层不应直接依赖文件系统、进程或平台 API。

示例：

- profile 优先级规则
- MCP 覆盖合并规则
- Hub 有效性校验规则
- 项目分类规则
- skill 来源标准化规则

### `AIHub.Application`

用于存放用例与编排服务，协调 Core 规则与基础设施端口。

示例：

- `RegisterProject`
- `ApplyProfileToProject`
- `SwitchProjectScope`
- `GenerateMcpConfigs`
- `EnableMcpServer`
- `DisableMcpServer`
- `SyncSkillSources`
- `ValidateHubRoot`

### `AIHub.Infrastructure`

用于存放文件、JSON、TOML、进程执行、日志、存储等基础设施适配器。

示例：

- JSON 仓储实现
- Codex 配置 TOML 写入器
- 配置文件生成器
- 进程监管器
- 环境变量读取器
- 旧脚本执行桥接器

### `AIHub.Platform.Windows`

用于存放 Windows 平台特有行为。

示例：

- Junction 处理
- Windows 启动项集成
- Windows 进程与窗口行为
- 如有需要，接入任务计划程序

### `AIHub.Platform.Mac`

用于存放 macOS 平台特有行为。

示例：

- symlink 处理
- 如有需要，接入 LaunchAgent
- macOS 托盘或应用行为
- 平台特有进程管理

### `AIHub.Desktop`

Avalonia UI 层，负责界面、导航、命令、对话框与视图模型。

推荐结构：

- `Views/`
- `ViewModels/`
- `Styles/`
- `Services/`
- `Resources/`

## 数据归属模型

程序负责逻辑，AI-Hub 目录负责数据。

程序负责：

- 校验
- 编排
- 合并规则
- 作用域解析
- MCP 生命周期处理
- profile 应用
- skill 同步

Hub 数据负责：

- manifests
- generated configs
- 资源目录
- 项目注册信息
- 上游来源定义
- 运行态快照

这样可以保证产品行为收敛在程序内，而用户资产仍然以目录形式版本化。

## Hub 发现机制

程序应按照以下优先顺序解析 Hub 根目录：

1. 上次保存的用户设置
2. 环境变量 `AI_HUB_ROOT`
3. 当前可执行文件同级目录中查找 `hub.json`
4. 用户手动选择目录

一个有效的 Hub 根目录应至少包含 `hub.json` 和预期的顶层目录结构。

## MCP 管理模型

### 配置型 MCP

针对 `stdio` 驱动、由客户端按需拉起的 MCP，程序应提供：

- 在 manifest 中启用或禁用
- 编辑参数与环境变量
- 校验依赖
- 重新生成客户端配置
- 运行诊断测试

不应对这类 MCP 展示伪造的“常驻运行”状态。

### 进程型 MCP

针对可以常驻运行的 MCP，程序应额外支持：

- 启动
- 停止
- 重启
- 查看日志
- 健康检查
- 可选的自启动策略

## UI 模块

### Dashboard

- 当前 Hub 根目录
- 当前激活的全局 profile
- 项目数量
- 已启用 MCP 数量
- 运行时告警
- 最近操作

### Projects

- 注册项目路径
- 分配 profile
- 切换全局模式或项目模式
- 查看链接目标和生成配置目标

### Profiles

- 应用 `global`、`frontend`、`backend`
- 应用前预览将影响的文件
- 展示相关备份记录

### Skills

- 按作用域浏览已安装 skills
- 查看来源元数据
- 从上游来源同步
- 以后如有需要，可增加可见性控制

### MCP

- 列出所有服务
- 切换启用状态
- 编辑环境变量与参数
- 为 Claude、Codex、Antigravity 生成配置
- 测试可用性
- 对支持的运行时执行启动或停止

### Logs

- 操作历史
- 标准输出与错误输出
- 文件写入与生成结果
- 最近一次成功同步时间

## 迁移策略

### 第一阶段

先通过应用服务包装现有脚本，让 UI 能尽快落地。

目标：

- Hub 发现与校验
- 项目注册表
- Profile 应用界面
- MCP manifest 编辑
- 配置生成
- 旧脚本桥接器

### 第二阶段

将现有脚本逻辑迁移到强类型服务。

目标：

- 替代 `setup-global.ps1`
- 替代 `use-profile.ps1`
- 替代 `sync-mcp.ps1`
- 统一备份与差异展示逻辑

### 第三阶段

增加更强的运行时与跨平台能力。

目标：

- 支持进程型 MCP 运行时控制
- 托盘集成
- 后台检查任务
- macOS 平台适配层

## 第一版非目标

- 完整插件系统
- 远程多用户 Hub 管理
- Linux 平台专用支持
- 自动兼容所有历史遗留目录结构

## 立即实施顺序

1. 在 `desktop/` 下维护解决方案和项目结构。
2. 以 `hub.json` 作为根目录识别入口。
3. 实现 Hub 根目录发现与校验。
4. 实现项目注册表与 profile 应用流程。
5. 实现 MCP manifest 编辑与配置生成。
6. 仅保留一个很小的旧脚本兼容桥接层，直到对应能力迁移完成。
