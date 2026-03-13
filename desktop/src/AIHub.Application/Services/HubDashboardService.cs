using AIHub.Application.Abstractions;
using AIHub.Application.Models;
using AIHub.Contracts;

namespace AIHub.Application.Services;

public sealed class HubDashboardService
{
    private static readonly string[] Modules =
    {
        "Dashboard",
        "Projects",
        "Profiles",
        "Skills",
        "Scripts",
        "MCP",
        "Settings"
    };

    private static readonly string[] Clients =
    {
        "Claude",
        "Codex",
        "Antigravity"
    };

    private readonly IPlatformCapabilitiesService? _platformCapabilitiesService;
    private readonly bool _usesInternalWorkspaceAutomation;
    private readonly bool _usesInternalMcpAutomation;
    private readonly bool _skillsVersionChannelReady;

    public HubDashboardService()
    {
    }

    public HubDashboardService(IHubRootLocator _, IProjectRegistry __)
    {
    }

    public HubDashboardService(
        IPlatformCapabilitiesService? platformCapabilitiesService,
        bool usesInternalWorkspaceAutomation,
        bool usesInternalMcpAutomation,
        bool skillsVersionChannelReady)
    {
        _platformCapabilitiesService = platformCapabilitiesService;
        _usesInternalWorkspaceAutomation = usesInternalWorkspaceAutomation;
        _usesInternalMcpAutomation = usesInternalMcpAutomation;
        _skillsVersionChannelReady = skillsVersionChannelReady;
    }

    public HubDashboardSnapshot CreateSnapshot(HubRootResolution resolution, IReadOnlyList<ProjectRecord> projects)
    {
        var status = resolution.IsValid
            ? "已识别 AI-Hub 根目录，来源：" + resolution.Source
            : "尚未识别到有效的 AI-Hub 根目录。";
        var platform = _platformCapabilitiesService?.Describe();
        var readinessItems = BuildReadinessItems(platform);
        var remainingGates = BuildRemainingGates(resolution, platform);

        return new HubDashboardSnapshot(
            AppTitle: "AI-Hub 控制台",
            HubRoot: resolution.RootPath,
            HubStatus: status,
            ProjectCount: projects.Count,
            EnabledClients: Clients,
            Modules: Modules,
            ReadinessItems: readinessItems,
            RemainingGates: remainingGates,
            ValidationErrors: resolution.Errors);
    }

    private IReadOnlyList<HubReadinessItem> BuildReadinessItems(PlatformCapabilitySnapshot? platform)
    {
        var windowsMcpReady = platform is not null
            && platform.SupportsJunctionLinks
            && platform.SupportsTrayIcon
            && platform.SupportsNotifications
            && platform.SupportsManagedProcessSupervisor;

        return new[]
        {
            new HubReadinessItem(
                "脚本能力内化",
                _usesInternalWorkspaceAutomation && _usesInternalMcpAutomation
                    ? "全局初始化、项目 Profile 应用和 MCP generated 配置生成都已切换到内部 C# 实现；PowerShell 只保留为专家模式兼容入口。"
                    : "核心自动化仍依赖外部脚本，尚未完成程序内化。",
                _usesInternalWorkspaceAutomation && _usesInternalMcpAutomation),
            new HubReadinessItem(
                "MCP Windows 闭环",
                windowsMcpReady
                    ? "Windows 平台已经具备 Junction、托盘、系统通知和托管 MCP 监督恢复能力。"
                    : platform?.Summary ?? "当前平台尚未完成 MCP 本地运行闭环。",
                windowsMcpReady),
            new HubReadinessItem(
                "Skills 版本通道",
                _skillsVersionChannelReady
                    ? "Skills 来源已支持 Tag 优先版本跟踪、固定标签、稳定版本检查和批量安全升级。"
                    : "Skills 来源仍停留在传统 Reference 模式，版本通道尚未产品化。",
                _skillsVersionChannelReady)
        };
    }

    private static IReadOnlyList<HubReadinessItem> BuildRemainingGates(HubRootResolution resolution, PlatformCapabilitySnapshot? platform)
    {
        var items = new List<HubReadinessItem>();
        if (!resolution.IsValid)
        {
            items.Add(new HubReadinessItem(
                "工作区校验",
                "需要先绑定有效的 AI-Hub 根目录，后续发布验证和客户端同步才能完整执行。",
                false));
        }

        if (platform is null || !platform.IsSupported)
        {
            items.Add(new HubReadinessItem(
                "平台交付边界",
                platform?.Summary ?? "当前平台能力尚未接入。",
                false));
        }

        items.Add(new HubReadinessItem(
            "Windows 内部正式使用门槛",
            "仍需继续完成安装包签名、升级回滚、长时间运行 smoke 和内部支持流程验证。",
            false));

        return items;
    }
}
