namespace AIHub.Application.Abstractions;
public interface IPlatformLinkService
{
    void EnsureDirectory(string path);
    void EnsureJunction(string linkPath, string targetPath, bool ignoreIfLocked = false);
}