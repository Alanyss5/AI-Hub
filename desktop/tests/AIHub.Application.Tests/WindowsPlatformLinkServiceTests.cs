using AIHub.Platform.Windows;

namespace AIHub.Application.Tests;

public sealed class WindowsPlatformLinkServiceTests
{
    [Fact]
    public void EnsureJunction_Creates_Reparse_Point_For_Missing_Path()
    {
        using var scope = new TestHubRootScope();
        var root = Path.Combine(scope.RootPath, "junctions");
        var targetPath = Path.Combine(root, "target");
        var linkPath = Path.Combine(root, "link");
        Directory.CreateDirectory(targetPath);

        var service = new WindowsPlatformLinkService();

        service.EnsureJunction(linkPath, targetPath);

        var linkInfo = new DirectoryInfo(linkPath);
        Assert.True(linkInfo.Exists);
        Assert.True((linkInfo.Attributes & FileAttributes.ReparsePoint) != 0);
        Assert.Equal(Normalize(targetPath), Normalize(linkInfo.ResolveLinkTarget(false)!.FullName));
    }

    [Fact]
    public void EnsureJunction_Reuses_Existing_Target_Without_Backup()
    {
        using var scope = new TestHubRootScope();
        var root = Path.Combine(scope.RootPath, "junctions");
        var targetPath = Path.Combine(root, "target");
        var linkPath = Path.Combine(root, "link");
        Directory.CreateDirectory(targetPath);

        var service = new WindowsPlatformLinkService();
        service.EnsureJunction(linkPath, targetPath);

        service.EnsureJunction(linkPath, targetPath);

        Assert.Empty(Directory.GetDirectories(root, "link.bak.*"));
        Assert.Equal(
            Normalize(targetPath),
            Normalize(new DirectoryInfo(linkPath).ResolveLinkTarget(false)!.FullName));
    }

    [Fact]
    public void EnsureJunction_Backs_Up_Wrong_Reparse_Point_And_Recreates()
    {
        using var scope = new TestHubRootScope();
        var root = Path.Combine(scope.RootPath, "junctions");
        var targetPath = Path.Combine(root, "target");
        var staleTargetPath = Path.Combine(root, "stale-target");
        var linkPath = Path.Combine(root, "link");
        Directory.CreateDirectory(targetPath);
        Directory.CreateDirectory(staleTargetPath);

        var service = new WindowsPlatformLinkService();
        service.EnsureJunction(linkPath, staleTargetPath);

        service.EnsureJunction(linkPath, targetPath);

        Assert.Single(Directory.GetDirectories(root, "link.bak.*"));
        Assert.Equal(
            Normalize(targetPath),
            Normalize(new DirectoryInfo(linkPath).ResolveLinkTarget(false)!.FullName));
    }

    [Fact]
    public void EnsureJunction_Backs_Up_Direct_Directory_And_Recreates()
    {
        using var scope = new TestHubRootScope();
        var root = Path.Combine(scope.RootPath, "junctions");
        var targetPath = Path.Combine(root, "target");
        var linkPath = Path.Combine(root, "link");
        Directory.CreateDirectory(targetPath);
        Directory.CreateDirectory(linkPath);
        File.WriteAllText(Path.Combine(linkPath, "stale.txt"), "stale");

        var service = new WindowsPlatformLinkService();

        service.EnsureJunction(linkPath, targetPath);

        Assert.Single(Directory.GetDirectories(root, "link.bak.*"));
        var linkInfo = new DirectoryInfo(linkPath);
        Assert.True((linkInfo.Attributes & FileAttributes.ReparsePoint) != 0);
        Assert.Equal(Normalize(targetPath), Normalize(linkInfo.ResolveLinkTarget(false)!.FullName));
    }

    private static string Normalize(string path)
    {
        return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }
}
