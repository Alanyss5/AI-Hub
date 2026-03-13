# AI-Hub 跨会话交接

## 新会话读取顺序
1. `docs/AI-Hub-可投入使用冲刺计划.md`
2. `docs/AI-Hub-开发路线图.md`
3. `docs/AI-Hub-Windows-内部正式使用门槛.md`
4. `docs/AI-Hub-使用手册.md`

不要再从聊天记录反推范围，以仓库文档为准。

## 当前状态快照
已完成：

- 当前 3 条收口线已落地：桌面端单实例、仓库卫生守卫、Windows 内部正式使用门槛文档化
- `desktop/AIHub.sln` 构建通过，当前为 `0 warning / 0 error`
- `desktop/AIHub.sln` 测试通过，当前为 `23/23`
- 双启动烟测通过：第二次启动不会留下第二个常驻进程
- AI-Hub 本体目录待办标记已清零，产品遗留备份文件已清理

## 下一步默认入口
除非用户另有要求，下轮优先从以下入口继续：

- `docs/AI-Hub-Windows-内部正式使用门槛.md`
- `desktop/apps/AIHub.Desktop/Program.cs`
- `desktop/apps/AIHub.Desktop/App.SingleInstance.cs`
- `desktop/tests/AIHub.Application.Tests/SingleInstanceCoordinatorTests.cs`
- `desktop/tests/AIHub.Application.Tests/ProductRepositoryGuardTests.cs`

## 默认方向
如果用户只说“继续”，默认优先级为：

1. Windows 发布链路、安装包与升级策略
2. 崩溃诊断、日志轮转、诊断包导出
3. 打包后 smoke 与长时间运行验证
4. 安全与信任边界收口

## 交接规则
每轮结束前至少回填三件事：

- 本轮完成了什么
- 还缺什么
- 下轮从哪里继续

如果没有把这些写回文档，就不算完成交接。