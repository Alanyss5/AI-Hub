# AI-Hub 快速操作手册

更新日期：2026-03-15

## 1. 先记住这 4 个动作

- `切换为全局级`：只切换 AI-Hub 当前作用域
- `切换为项目级`：只切换 AI-Hub 当前作用域到项目
- `应用全局链接`：生成 `.runtime/effective/global`，再更新用户目录入口
- `应用项目 Profile`：生成 `.runtime/effective/<profile>`，再更新项目目录入口

## 2. 当前生效规则

- 所有项目默认继承全局层
- 项目再叠加所选 `frontend` 或 `backend`
- 私人层统一放在 `AI-Personal`
- 真正生效的是 merged effective output，不是原始目录并排暴露

## 3. 第一次在新电脑接入

1. 打开 worktree 版程序
2. 在“项目与 Profile”页确认运行标识
3. 点击 `应用全局链接`
4. 按向导把现有全局 `Skills / commands / agents / Claude settings / MCP` 选择导入到：
   - `AI-Hub`
   - `AI-Personal`
   - `忽略`
5. 完成后确认用户目录入口已经切到 `.runtime/effective/global`

## 4. 新项目第一次接入

1. 新增或选择项目
2. 选好 `Profile`
3. 如果项目目录改过，先点 `新增或更新项目`
4. 再点 `应用项目 Profile`
5. 按向导处理项目现有 `Skills / commands / agents / Claude settings / MCP`
6. 完成后确认项目入口已经切到 `.runtime/effective/<profile>`

## 5. 如果提示“项目路径尚未保存”

这表示：

- 当前登记路径和表单目录不一致
- AI-Hub 不会自动迁移

正确顺序：

1. 先点 `新增或更新项目`
2. 再点 `应用项目 Profile`、`设为当前项目` 或 `重新扫描项目接管`

## 6. 如何判断全局有没有叠加到项目

不要在项目目录里找单独的 `global` 文件夹。

应该看：

- `项目\.claude\skills`
- `项目\.agents\skills`
- `项目\.agent\skills`

它们应该统一指向：

- `C:\AI-Hub\.runtime\effective\<profile>\skills`

## 7. 什么时候点“重新扫描”

只有在这几种情况才需要：

- 你刚在用户目录或项目目录手工放入了旧资源
- 你之前选过 `忽略`，现在想重新导入
- 你怀疑外部资源有新增

如果没有新增资源，重扫会弹出明确提示：

- `未发现可重新导入资源`

这不算异常。

## 8. worktree 版最短验收

1. 启动：
   `C:\Users\Administrator\.config\superpowers\worktrees\AI-Hub\codex\four-layer-onboarding\desktop\apps\AIHub.Desktop\bin\Debug\net8.0\AIHub.Desktop.exe`
2. 在项目页确认显示：
   - 构建来源
   - 可执行文件路径
   - `HubRoot`
3. 保存项目路径为 `C:\OverSeaFramework`
4. 选择 `backend`
5. 点击 `应用项目 Profile`
6. 看结果区是否显示：
   - 项目路径
   - Profile
   - effective 输出根目录
   - `.claude\skills`
   - `.agents\skills`
   - `.agent\skills`
7. 再点 `重新扫描项目接管`
8. 如果没有新增候选项，应该弹出明确结果提示
