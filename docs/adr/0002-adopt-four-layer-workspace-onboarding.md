# ADR-0002：采用四层资源叠加与首次接管向导

## 状态

已接受

## 日期

2026-03-15

## 背景

AI-Hub 原先已经具备 `global / frontend / backend` 三套资源目录，也能把用户级和项目级入口接到这些目录上，但仍有三个明显问题：

- 全局资源、项目资源和个人资源的生效规则并不统一
- `AI-Personal` 只覆盖个人 skills，没有扩展到 commands、agents、settings、MCP
- 首次接管现有用户目录或项目目录时，只做备份，不给出结构化的导入选择

这会导致用户难以判断：

- “切换为项目级”与“应用项目 Profile”到底谁在改状态，谁在真正落盘
- 全局配置是否真的对所有项目生效
- 本机已有的 Skills / MCP 应该进入公司层、私人层，还是保持忽略

## 决策

采用统一的四层资源模型，并在首次全局接管、首次项目接管时提供专用导入向导。

四层资源优先级固定为：

1. 全局公司层
2. 全局私人层
3. 项目公司层（即所选 Profile）
4. 项目私人层（即所选 Profile 在 `AI-Personal` 下的对应资源）

同名冲突规则固定为：

- 项目层覆盖全局层
- 同层内私人覆盖公司

`AI-Personal` 升级为完整私人层，至少覆盖：

- `skills/<profile>`
- `claude/commands/<profile>`
- `claude/agents/<profile>`
- `claude/settings/<profile>.settings.json`
- `mcp/manifest/<profile>.json`

所有客户端入口与项目入口统一改为：

1. 先在 `C:\AI-Hub\.runtime\effective\<profile>` 物化有效输出
2. 再把用户目录或项目目录入口切到该有效输出

首次接管向导固定扫描：

- Skills
- Claude commands
- Claude agents
- Claude settings
- MCP

每项导入决策固定为：

- 导入到 `AI-Hub`
- 导入到 `AI-Personal`
- 忽略

导入策略固定为“复制导入”，目标已存在时先备份。

## 影响

### 正面影响

- 全局、项目、私人三种边界终于被统一到同一套优先级规则
- 全局资源默认可被所有项目继承，项目 Profile 只负责追加与覆盖
- MCP generated 配置、Claude settings、skills/commands/agents 的解释不再分裂
- 首次接管时用户可以显式决定哪些资源进入公司层、私人层，哪些忽略

### 负面影响

- `AI-Personal` 目录结构变复杂，需要补齐文档与维护约定
- 物化有效输出后，生成目录和入口目录之间多了一层中间产物
- 项目接管导入到 Profile 层，意味着同一 Profile 下的多个项目共享同一套导入结果

## 落地约束

- 首次接管向导只在第一次应用或显式重扫时出现，不做持续自动弹窗
- 旧有公司层目录继续保留，不做一次性迁移
- 兼容层仍保留 `mcp/generated/*`，但其内容必须来自四层合并后的有效结果
