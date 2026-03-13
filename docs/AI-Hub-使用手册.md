# AI-Hub 使用手册

## 总览
AI-Hub 用于统一管理本地项目、Skills、MCP 和常用脚本。桌面端基于 `.NET 8 + Avalonia`，工作根目录固定为 `C:\AI-Hub`。

## Projects
支持：

- 项目登记、删除
- 项目级 / 全局级作用域切换
- 设为当前项目
- 应用项目 Profile

注意：

- 删除项目只移除注册表记录，不删除磁盘目录
- 删除前会弹确认对话框

## Skills
支持：

- 扫描已安装 Skill
- 登记来源、扫描来源、保存安装登记
- 基线捕获、差异预览、检查更新、安全同步、强制同步
- 来源级运行期定时策略
- Overlay 文件级合并预览与应用
- 查看备份历史并选择指定备份回滚
- 在来源编辑区从可用引用中快速选择 branch / tag

### 来源级定时策略
来源编辑区现在提供完整策略面板，按来源而不是按单个 Skill 配置。

可配置项：

- 频率：`6h / 12h / 24h / 7d`
- 动作：`仅检查` 或 `检查并安全同步`
- 最近执行结果、下次到期时间
- 手动立即执行

兼容规则：

- 旧数据 `AutoUpdate=true` 会迁移为 `24h + 仅检查`
- 旧数据 `AutoUpdate=false` 会迁移为关闭运行期定时
- “启动时自动检查 / 启动时自动同步”仍然只负责启动期行为

### Overlay 合并
Overlay 模式下，来源和本地覆盖层同时变化时，可以先预览再应用。

使用方式：

1. 在 Skills 页选择一个已登记的 Overlay Skill
2. 点击 `Preview Merge`
3. 按文件选择：
   - `采用来源版`
   - `保留本地版`
   - `按删除状态应用`
   - `跳过`
4. 点击 `Apply Merge`

执行特性：

- 仅做文件级决策，不做行级编辑
- 应用前自动创建 `pre-merge` 备份
- 应用后自动重算基线、来源指纹、overlay 快照与删除列表

## MCP
支持：

- manifest 编辑与客户端配置生成
- 当前作用域体检
- 当前作用域客户端同步
- 外部 MCP 识别、冲突预览与纳管
- 托管进程定义、启动、停止、重启、健康检查
- 日志预览
- 托盘菜单快捷操作
- 运行摘要统计：总数、运行中、已停止、异常、可自恢复、需人工关注

### 体检范围
当前作用域体检会检查：

- manifest JSON 是否有效
- generated 配置是否最新
- 当前作用域客户端落地文件是否与 generated 一致
- 命令、工作目录、环境变量、健康检查地址、可执行文件解析情况
- 客户端里是否存在尚未纳入 AI-Hub 的外部 MCP

### 客户端配置边界
AI-Hub 当前接管：

- Claude 全局：`C:\Users\Administrator\.claude.json`
- Codex 全局：`C:\Users\Administrator\.codex\config.toml`
- Antigravity 全局：`C:\Users\Administrator\.gemini\antigravity\mcp_config.json`
- 项目级 Claude：`项目\.mcp.json`
- 项目级 Codex：`项目\.codex\config.toml`

当前不做：

- Windows 登录自启动
- 项目级 Antigravity MCP 文件路径扩展

写回规则：

- 保留客户端原有非 MCP 设置
- 保留尚未纳管的外部 MCP 条目
- AI-Hub 只更新自己管理的 MCP 集合
- 每次写回前自动备份原文件

### 外部 MCP 纳管
体检识别到外部 MCP 后，可在 MCP 管理页直接导入。

规则：

- 全局作用域发现的外部 MCP 导入 `global` manifest
- 项目作用域发现的外部 MCP 导入当前选中 profile manifest
- 同名但定义不一致时会先显示冲突预览，需要显式选择采用哪一端
- 导入后可立即同步到当前作用域客户端

## 系统通知
Windows 系统通知当前只在“进入异常状态”时提醒，并按告警键 15 分钟去重。

会触发主动提醒的场景：

- Skills 定时检查 / 自动同步失败
- 因本地改动导致自动同步被阻塞
- MCP 当前作用域体检失败
- 托管 MCP 健康检查异常
- 托管 MCP 在当前应用会话内累计 3 次自恢复

托盘摘要仍然保留，用于持续查看状态；系统通知只负责主动提醒。

## 危险操作确认
以下操作会先弹确认框：

- 删除项目
- 删除 Skills 来源
- 删除 Skill 安装登记
- 删除托管 MCP
- 强制同步 Skill
- Skill 回滚
- Overlay 合并应用
- 停止全部托管 MCP
- 导入配置包
- 导入外部 MCP

## 配置包导入导出
导出：

- 会打包 Hub 设置、项目清单、Skills 来源与安装登记、MCP runtime、manifest、Skills overrides

导入：

- 流程为“预检 -> 确认 -> 导入”
- 预检会显示版本、导出时间、包含模块、将覆盖的配置类别、计划备份位置
- 仅接受 `Version == "1.0"`，版本不匹配直接拒绝

## 测试与验收
当前仓库已包含最小测试工程：`desktop/tests/AIHub.Application.Tests`。

当前已覆盖：

- Skills 备份历史排序
- 指定备份回滚
- 旧 `AutoUpdate` 迁移
- 定时策略到期筛选
- Overlay 合并预览与应用
- 来源引用快速选择
- MCP generated 配置漂移识别
- MCP 客户端写回、备份与外部配置识别
- 配置包版本预检
- MCP 异常摘要计算
