using AIHub.Contracts;
namespace AIHub.Application.Abstractions;
public interface IPlatformCapabilitiesService
{
    PlatformCapabilitySnapshot Describe();
}