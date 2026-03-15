# AI-Hub 第三方依赖与 Notices

## .NET 包依赖
- Avalonia 11.3.9
- Avalonia.Desktop 11.3.9
- Avalonia.Headless.XUnit 11.3.9
- Avalonia.Themes.Fluent 11.3.9
- Microsoft.NET.Test.Sdk 17.8.0
- Tomlyn 0.17.0
- xUnit 2.5.3
- xUnit Runner Visual Studio 2.5.3
- coverlet.collector 6.0.0

## 仓库内置脚本与模板
- `scripts\setup-global.ps1`
- `scripts\sync-mcp.ps1`
- `scripts\use-profile.ps1`
- `scripts\hooks\pre-tool-check.ps1`
- `scripts\hooks\session-start.ps1`
- `skills\` 下的 Skill 模板与说明文档

## 外部引入的共享技能
- `skills\global\superpowers\*`
  - 来源: `obra/superpowers` (`https://github.com/obra/superpowers`)
  - 许可证: `MIT`

## 说明
- 以上依赖和脚本均按各自许可证与来源要求使用。
- 若后续引入新的 NuGet 包、内置脚本或 vendored MCP 服务，请同步更新本文件。
- 对外分发前，应补齐每个第三方组件的许可证文本和来源链接。
