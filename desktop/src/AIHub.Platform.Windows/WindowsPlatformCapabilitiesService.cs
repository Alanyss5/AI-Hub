using AIHub.Application.Abstractions;
using AIHub.Contracts;

namespace AIHub.Platform.Windows;

public sealed class WindowsPlatformCapabilitiesService : IPlatformCapabilitiesService
{
    public PlatformCapabilitySnapshot Describe()
    {
        return new PlatformCapabilitySnapshot(
            PlatformName: "Windows",
            SupportsJunctionLinks: true,
            SupportsTrayIcon: true,
            SupportsNotifications: true,
            SupportsManagedProcessSupervisor: true,
            IsSupported: true,
            Summary: "Windows 平台已接入 Junction、托盘、系统通知和托管 MCP 监督能力。");
    }
}
