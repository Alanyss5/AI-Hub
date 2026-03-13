using AIHub.Application.Abstractions;
using AIHub.Contracts;

namespace AIHub.Platform.Mac;

public sealed class MacPlatformCapabilitiesService : IPlatformCapabilitiesService
{
    public PlatformCapabilitySnapshot Describe()
    {
        return new PlatformCapabilitySnapshot(
            PlatformName: "macOS",
            SupportsJunctionLinks: false,
            SupportsTrayIcon: false,
            SupportsNotifications: false,
            SupportsManagedProcessSupervisor: false,
            IsSupported: false,
            Summary: "当前版本未交付 macOS 本地集成能力，仅返回明确的未支持状态。");
    }
}
