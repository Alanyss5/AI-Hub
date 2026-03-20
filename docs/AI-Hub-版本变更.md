# AI-Hub 版本变更

## 2026-03-19
- 文档补充动态 profile / catalog 改造说明：`config/profile-catalog.json` 作为 profile catalog 的单一事实源
- 明确 `ProfileKind` 已降级为兼容层，不再作为新的业务真相
- 明确 `global` 为不可删除的基础 profile
- 明确 v1 只支持新增 / 删除 / 修改 `DisplayName`，不支持修改 profile Id
- 补充项目、Skills、MCP 基于动态分类工作的行为说明
- 补充 profile 删除失败时的常见原因与迁移顺序
- 补充旧仓库自动回填 `global/frontend/backend` 的兼容规则
- 补充 `Skills` 单技能多 profile 绑定与顶层仓库/文件夹技能组展示规则
- 补充 `MCP` 单 server 多 profile 绑定规则
- 补充桌面端暗黑现代化界面的视觉更新说明

## 2026-03-19 - 文档收尾补充
- `docs/README.md` 增补了本轮新增能力的入口说明
- `docs/migration-notes.md` 增补了技能组和 MCP server 的多 profile 绑定迁移口径
- `docs/AI-Hub-使用手册.md` 增补了可见的操作说明段落，便于用户直接查阅
- 补充“单个 Skill / 单个 MCP 可自由加入或移出多个 profile”的最终交付说明
- 补充“仓库型 Skills 按顶层文件夹统一管理并可查看包含技能”的交付说明
- 补充深色现代化桌面主题的交付说明

## 2026-03-08
- 新增桌面端单实例：第二次启动会唤起已有窗口并退出新进程
- 新增 Windows 单实例协调器与二次启动激活监听
- 新增本体目录待办标记守卫与备份文件守卫
- 清理产品遗留备份文件 `MainWindowViewModel.cs.bak`
- README、冲刺计划、路线图、跨会话交接新增“Windows 内部正式使用门槛”入口
- 测试更新为 `27/27`
- 新增版本管理文件 `desktop\Version.props` 与桌面端版本显示
- 新增 Windows 发布、安装、升级前备份与验证脚本：`publish-desktop.ps1`、`package-installer.ps1`、`backup-hub-state.ps1`、`verify-desktop.ps1`
- 新增 Inno Setup 安装器模板 `installer\AIHub.Desktop.iss`
- 新增 `win-x64 self-contained` 发布成功验证与便携 zip 打包回退
- 恢复 solution 级 `dotnet test C:\AI-Hub\desktop\AIHub.sln --no-build` 验证路径
- 新增诊断导出、状态文件 schema / 备份、风险确认持久化相关自动化测试
- 新增 `AI-Hub-Windows-运维手册.md` 与 `AI-Hub-第三方依赖与Notices.md`
