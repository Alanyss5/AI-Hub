using AIHub.Application.Abstractions;

namespace AIHub.Platform.Windows;

public sealed class WindowsPlatformProfileLinker
{
    private readonly IPlatformCapabilitiesService _capabilitiesService;

    public WindowsPlatformProfileLinker()
        : this(new WindowsPlatformCapabilitiesService())
    {
    }

    public WindowsPlatformProfileLinker(IPlatformCapabilitiesService capabilitiesService)
    {
        _capabilitiesService = capabilitiesService;
    }

    public string DescribeCapability()
    {
        return _capabilitiesService.Describe().Summary;
    }
}