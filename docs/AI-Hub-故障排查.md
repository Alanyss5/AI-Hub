# AI-Hub 故障排查

## build / test 因权限失败
症状：
- `dotnet restore` 提示无权访问 `NuGet.Config`
- `dotnet build` 提示 Avalonia BuildServices 无法写入用户目录日志

处理：
1. 在 `desktop/NuGet.Config` 放置本地配置。
2. 需要时使用提升权限执行：
```powershell
C:\Users\Administrator\.dotnet\dotnet.exe restore C:\AI-Hub\desktop\AIHub.sln --configfile C:\AI-Hub\desktop\NuGet.Config
C:\Users\Administrator\.dotnet\dotnet.exe build C:\AI-Hub\desktop\AIHub.sln
C:\Users\Administrator\.dotnet\dotnet.exe test C:\AI-Hub\desktop\AIHub.sln --no-build
```

## Skills 无法回滚
检查：
- 是否已生成备份目录
- 选择的备份是否位于 `backups/skills/<profile>/<skill>` 下
- 当前 Skill 是否已有安装状态记录

## 配置包无法导入
检查：
- 预检是否通过
- 配置包版本是否等于 `1.0`
- 导入前确认框中显示的覆盖范围是否符合预期

## 来源引用快速选择为空
检查：
- 是否已先扫描来源
- 该来源是否是 Git 仓库来源
- 扫描结果是否成功写回可用引用列表

## 托盘摘要与页面状态不一致
处理：
- 先执行一次 Refresh
- 再执行 MCP Maintain
- 若仍不一致，查看结果区中的最近异常信息
