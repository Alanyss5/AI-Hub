# AI-Hub 快速开始

## 1. 构建与启动
```powershell
C:\Users\Administrator\.dotnet\dotnet.exe build C:\AI-Hub\desktop\AIHub.sln
C:\AI-Hub\desktop\apps\AIHub.Desktop\bin\Debug\net8.0\AIHub.Desktop.exe
```

## 2. 首次进入建议顺序
1. 在“设置”中确认 AI-Hub 根目录。
2. 在“Projects”登记常用项目。
3. 在“Skills”登记来源并扫描。
4. 对已安装 Skill 保存安装登记。
5. 在“MCP”中确认托管进程与 manifest。
6. 按需导出一份配置包作为基线。

## 3. 本轮新增重点
- Skills 已支持备份历史列表与指定备份回滚。
- Skills 来源已支持从扫描结果中快速选择 branch/tag 引用。
- MCP 托盘已显示运行中、停止、异常、可自恢复、需人工关注等摘要。
- 危险操作会先弹出确认对话。
- 配置包导入会先做预检，只接受 `Version == "1.0"`。

## 4. 验证命令
```powershell
C:\Users\Administrator\.dotnet\dotnet.exe build C:\AI-Hub\desktop\AIHub.sln
C:\Users\Administrator\.dotnet\dotnet.exe test C:\AI-Hub\desktop\AIHub.sln --no-build
```

## 5. 文档入口
- `docs/AI-Hub-使用手册.md`
- `docs/AI-Hub-故障排查.md`
- `docs/AI-Hub-可投入使用冲刺计划.md`
