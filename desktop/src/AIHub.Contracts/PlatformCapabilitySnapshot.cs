namespace AIHub.Contracts;
public sealed record PlatformCapabilitySnapshot(
    string PlatformName,
    bool SupportsJunctionLinks,
    bool SupportsTrayIcon,
    bool SupportsNotifications,
    bool SupportsManagedProcessSupervisor,
    bool IsSupported,
    string Summary);