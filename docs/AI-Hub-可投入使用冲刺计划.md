# AI-Hub 可投入使用冲刺计划

## 目标
当前“Windows 自用可投入使用”收口已经完成。本轮额外收口两件事：

- 桌面端改为强制单实例，第二次启动只唤起已有窗口
- AI-Hub 本体目录增加零待办标记与零备份遗留守卫

## 已完成
- Skills Overlay 文件级合并预览、应用、备份与回滚
- Skills 来源级定时更新策略与后台维护循环
- MCP 当前作用域体检、外部纳管与多客户端同步
- Windows 系统通知与异常状态去重
- 标准 `build` 自动结束当前输出目录上的运行中 Debug 实例
- 桌面端单实例：主窗口可见、最小化、隐藏到托盘三种状态下都由已有实例接管
- AI-Hub 本体目录零待办标记守卫
- AI-Hub 本体目录零备份遗留守卫
- 产品遗留备份文件 `MainWindowViewModel.cs.bak` 已清理

## 运行边界
- 仅当 AI-Hub 桌面端打开或隐藏到托盘时，后台维护循环才会执行
- 单实例按 Windows 当前用户会话隔离，不做跨用户互斥
- 第二次启动不转发命令行参数，不打开新的项目会话或 Profile 会话
- 待办标记统计只针对 `desktop/ docs/ scripts/ config/ README.md`

## 验证方式
```powershell
C:\Users\Administrator\.dotnet\dotnet.exe build C:\AI-Hub\desktop\AIHub.sln
C:\Users\Administrator\.dotnet\dotnet.exe test C:\AI-Hub\desktop\AIHub.sln --no-build
```

当前结果：
- `build` 通过，`0 warning / 0 error`
- `test` 通过，`23/23`
- 双启动烟测通过，第二次启动后未出现第二个常驻进程

## 下一阶段入口
“Windows 自用可投入使用”已不再是主要阻塞项。默认后续优先级切换为：

1. `docs/AI-Hub-Windows-内部正式使用门槛.md` 中列出的内部正式使用门槛
2. Windows 发布链路、安装包与升级策略
3. 更强的打包后 smoke 与长时间运行验证