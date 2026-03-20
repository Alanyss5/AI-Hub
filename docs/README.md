# 文档索引

建议阅读顺序：
1. `AI-Hub-可投入使用冲刺计划.md`
2. `AI-Hub-开发路线图.md`
3. `AI-Hub-快速开始.md`
4. `AI-Hub-使用手册.md`
5. `AI-Hub-故障排查.md`
6. `AI-Hub-版本变更.md`

补充文档：
- `AI-Hub-跨会话交接.md`：新会话交接规则与下一步入口
- `AI-Hub-工作流沉淀与Skill固化指南.md`：并行 agent 编排与共享文件归属规则
- `文档维护约定.md`：文档维护约束

说明：
- 冲刺计划是当前范围与完成度的唯一真相源。
- 路线图只保留阶段摘要、优先级与后置项。
- 使用手册与快速开始面向实际使用，不记录会话背景。
- 动态 profile / catalog 改造的主文档入口是：
  - `architecture/avalonia-solution-blueprint.md`：架构口径、单一事实源与 v1 约束
  - `AI-Hub-使用手册.md`：实际操作规则、删除失败原因、旧仓库兼容行为
  - `migration-notes.md`：迁移口径、`ProfileKind` 废弃说明、自动补齐默认 profile
  - `AI-Hub-版本变更.md`：本轮文档更新摘要
- 本轮补充的能力说明：
  - `Skills` 支持单个 skill 同时绑定到多个 profile
  - `skills/global/superpowers` 这类顶层仓库或文件夹按“技能组”管理，可直接查看其中包含的具体 skills
  - `MCP` 支持单个 server 同时绑定到多个 profile
  - 桌面端界面已切换为暗黑、现代化视觉风格

## 2026-03 动态分类补充

本轮动态分类改造已覆盖下面几个直接可用的能力：

- 单个 Skill 可以同时绑定到多个 profile，并且可以随时把它加入或移出 `global`、`frontend`、`backend` 以及自定义 profile
- `skills/<profile>/<repo-or-folder>/...` 这类仓库型 Skills 会按顶层文件夹统一展示和管理，例如 `skills/global/superpowers`
- MCP 不再只能按整份 manifest 管理；单个 MCP server 也可以直接绑定到多个 profile，并自由增减
- 桌面端界面已切到统一的暗黑现代化风格，Settings、Skills、MCP 等页签使用一致的深色面板和高对比交互
