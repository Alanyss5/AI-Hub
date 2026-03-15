# AI-Hub 快速操作手册

更新日期：2026-03-08

这份文档只保留最常用的操作，不解释底层设计。

## 1. 常用入口

- 桌面程序：`C:\AI-Hub\desktop\apps\AIHub.Desktop\bin\Debug\net8.0\AIHub.Desktop.exe`
- 兼容脚本目录：`C:\AI-Hub\scripts`

如果优先走图形界面，日常大多数操作都可以直接在桌面程序里完成。

## 2. 当前仍然重要的 3 个脚本

- `C:\AI-Hub\scripts\sync-mcp.ps1`
  - 作用：根据 `C:\AI-Hub\mcp\manifest` 生成 Claude / Codex / Antigravity 的 MCP 配置

- `C:\AI-Hub\scripts\setup-global.ps1`
  - 作用：把当前电脑的全局入口接到 `C:\AI-Hub`
  - 额外会建立个人 skills 根目录：`C:\Users\Administrator\AI-Personal\skills\global`

- `C:\AI-Hub\scripts\use-profile.ps1`
  - 作用：把某个项目接到 `frontend` 或 `backend` profile

## 3. 日常最常见的 5 种操作

### 3.1 改了 MCP manifest 后

优先在桌面程序里做：

1. 打开 `MCP 管理`
2. 编辑 manifest
3. 点击 `Generate Configs`
4. 点击 `Validate Scope`
5. 需要落地到当前作用域客户端时，再点 `Sync Clients`

如果临时只想走命令行，也可以执行：

```powershell
powershell -ExecutionPolicy Bypass -File C:\AI-Hub\scripts\sync-mcp.ps1
```

### 3.2 导入现有客户端里的外部 MCP

1. 打开 `MCP 管理`
2. 在 `配置与生成` 页点击 `Validate Scope`
3. 在右侧 `外部 MCP 纳管` 区勾选要导入的服务
4. 如果同名定义冲突，先选定要采用的客户端定义
5. 点击 `Import Selected`

默认会在导入后立即同步当前作用域客户端。

### 3.3 给某个 Skills 来源开运行期定时策略

1. 打开 `Skills`
2. 选中来源
3. 在 `来源级定时策略` 里设置频率和动作
4. 保存来源
5. 如需立刻执行，点击 `立即执行该来源策略`

策略只在 AI-Hub 桌面端打开或隐藏到托盘时运行。

### 3.4 处理 Overlay Skill 的来源变更

1. 打开 `Skills`
2. 选中一个 Overlay 模式且已登记的 Skill
3. 点击 `Preview Merge`
4. 按文件选择保留本地还是采用来源
5. 点击 `Apply Merge`

应用前会自动创建 `pre-merge` 备份。

### 3.5 启动一个托管型 MCP

直接在桌面程序里做：

1. 打开 `MCP 管理`
2. 切到 `托管进程` 子页
3. 填写名称、命令、参数、工作目录
4. 点击 `保存定义`
5. 点击 `启动`

这部分当前不依赖脚本。

## 4. 日常维护时怎么判断该用桌面程序还是脚本

### 场景 A：我只是改了 skill

通常不用跑脚本，也不用重新生成。

原因：

- skill 目录本身已经通过 junction 直连到 `C:\AI-Hub` 或你的个人目录
- 你改完 skill，三端下次读取时会直接看到

### 场景 B：我改了 Claude command / agent / hook 脚本

通常也不用跑脚本。

原因：

- Claude 的全局或项目入口已经直接指向 `C:\AI-Hub`
- 只要你改的是已经被链接进去的目录，就会直接生效

例外：

- 如果你改了 `claude\settings\*.json` 模板，需要重新执行 `setup-global.ps1` 或 `use-profile.ps1`

### 场景 C：我改了 MCP 配置

至少要重新生成；如果还要落地当前作用域客户端，再做一次体检和同步：

- 图形界面：`Generate Configs -> Validate Scope -> Sync Clients`
- 命令行：执行 `sync-mcp.ps1`

### 场景 D：我新增了一个常驻型本地 MCP

优先走桌面程序。

原因：

- 托管型 MCP 的定义、PID、状态和最后消息都由桌面程序维护
- 这类运行态数据保存在 `C:\AI-Hub\mcp\runtime.json`

### 场景 E：我想知道有没有异常

先看：

- 托盘摘要
- MCP 管理页的体检结果
- Skills 来源的最近定时结果

进入异常状态时，桌面端还会主动发 Windows 通知。

## 5. 当前你的实际情况

已经完成：

- 全局共享根目录：`C:\AI-Hub`
- Claude 全局入口已接入
- Codex 新版共享入口 `~/.agents/skills` 已接入
- Antigravity 全局 skills 入口已接入
- Claude / Codex / Antigravity 全局 MCP 配置都可由 AI-Hub 接管
- 个人 skills 根目录已固定为 `C:\Users\Administrator\AI-Personal\skills\global`

当前没自动启用的内容：

- GitHub MCP
- cclsp MCP

原因：

- GitHub MCP 需要 token
- cclsp 对 Unity / C# 需要额外 LSP 环境，例如 `omnisharp`

## 6. 哪些文件可以直接改

你以后主要改这些地方：

- `C:\AI-Hub\skills\global`
- `C:\AI-Hub\skills\frontend`
- `C:\AI-Hub\skills\backend`
- `C:\AI-Hub\claude\commands\*`
- `C:\AI-Hub\claude\agents\*`
- `C:\AI-Hub\claude\settings\*`
- `C:\AI-Hub\mcp\manifest\*`
- `C:\AI-Hub\mcp\runtime.json`
- `C:\AI-Hub\scripts\hooks\*`
- `C:\Users\Administrator\AI-Personal\skills\global`

## 7. 最后记住一句话

- 公司共享内容只维护 `C:\AI-Hub`
- 你个人专属 skill 只维护 `C:\Users\Administrator\AI-Personal\skills\global`
- 改 `mcp\manifest` 后，优先走 `Generate Configs -> Validate Scope -> Sync Clients`
- 需要常驻型本地 MCP 时，优先在桌面程序里维护托管进程
- 改 `claude\settings` 模板后，要重新跑 `setup-global.ps1` 或 `use-profile.ps1`
