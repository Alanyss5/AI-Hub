# AI-Hub 工作流沉淀与 Skill 固化指南

## 目的
本文件用于固定 AI-Hub 后续开发时的并行编排、共享文件归属和收口顺序，避免多人或多 agent 同时修改同一核心文件。

## 并行编排模板
- Agent A：Skills 线。负责来源、安装登记、同步、回滚、覆盖层相关能力。
- Agent B：设置与安全护栏。负责确认对话、配置包导入导出、设置页流程。
- Agent C：MCP 运维。负责托盘、运行摘要、自恢复、告警相关能力。
- Agent D：集成与验证。负责共享文件收口、测试工程、文档、build/test/smoke。

## 共享文件归属规则
优先独占：
- `SkillsCatalogService*` 默认归 Agent A
- `WorkspaceControlService.Advanced.cs` 默认归 Agent B
- `McpControlService*` 与 `App.Tray.cs` 默认归 Agent C

统一收口：
- `MainWindow.axaml`
- `MainWindowViewModel.cs`
- 中文文档
- 测试工程

以上共享文件默认由 Agent D 收口，其他 agent 不直接改最终版本。

## 合并顺序
1. A / B / C 分支并行开发各自独占文件。
2. D 在接口稳定后统一合并共享文件。
3. D 完成构建、测试、手工 smoke。
4. D 更新冲刺计划、路线图、交接文档和中文手册。

## 完成前检查
- 代码已合并到共享文件
- `build` 通过
- `test` 通过
- 冲刺计划中的状态已更新
- 路线图、交接文档、使用手册已同步

## 不要做的事
- 不要依赖聊天记录确认范围
- 不要在共享文件上多 agent 直接抢改
- 不要在未更新文档的情况下结束本轮开发
