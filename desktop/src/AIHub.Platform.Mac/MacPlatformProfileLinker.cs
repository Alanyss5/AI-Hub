using AIHub.Application.Abstractions;

namespace AIHub.Platform.Mac;

public sealed class MacPlatformProfileLinker
{
    private readonly IPlatformCapabilitiesService _capabilitiesService;

    public MacPlatformProfileLinker()
        : this(new MacPlatformCapabilitiesService())
    {
    }

    public MacPlatformProfileLinker(IPlatformCapabilitiesService capabilitiesService)
    {
        _capabilitiesService = capabilitiesService;
    }

    public string DescribeCapability()
    {
        return _capabilitiesService.Describe().Summary;
    }
}