using System.Diagnostics;
using System.Text.Json;
using AIHub.Application.Services;
using AIHub.Contracts;

namespace AIHub.Application.Tests;

public sealed class SkillsCatalogServiceTests
{
    [Fact]
    public async Task LoadAsync_OrdersBackupHistoryNewestFirst()
    {
        using var scope = new TestHubRootScope();
        var skillDirectory = Path.Combine(scope.RootPath, "skills", "global", "demo-skill");
        Directory.CreateDirectory(skillDirectory);
        await File.WriteAllTextAsync(Path.Combine(skillDirectory, "SKILL.md"), "current");

        Directory.CreateDirectory(Path.Combine(scope.RootPath, "backups", "skills", "global", "demo-skill", "20260307-010101-sync"));
        Directory.CreateDirectory(Path.Combine(scope.RootPath, "backups", "skills", "global", "demo-skill", "20260308-020202-sync"));

        var service = new SkillsCatalogService(new FixedHubRootLocator(scope.RootPath));

        var snapshot = await service.LoadAsync();

        var skill = Assert.Single(snapshot.InstalledSkills);
        Assert.Equal(new[]
        {
            "20260308-020202-sync",
            "20260307-010101-sync"
        }, skill.BackupRecords.Select(record => record.Name).ToArray());
    }

    [Fact]
    public async Task RollbackInstalledSkillAsync_UsesSelectedBackupDirectory()
    {
        using var scope = new TestHubRootScope();
        var installDirectory = Path.Combine(scope.RootPath, "skills", "global", "demo-skill");
        Directory.CreateDirectory(installDirectory);
        await File.WriteAllTextAsync(Path.Combine(installDirectory, "SKILL.md"), "current-version");

        var selectedBackupDirectory = Path.Combine(scope.RootPath, "backups", "skills", "global", "demo-skill", "20260308-030303-sync");
        Directory.CreateDirectory(selectedBackupDirectory);
        await File.WriteAllTextAsync(Path.Combine(selectedBackupDirectory, "SKILL.md"), "backup-version");

        Directory.CreateDirectory(Path.Combine(scope.RootPath, "config"));
        var statesJson = JsonSerializer.Serialize(new
        {
            states = new[]
            {
                new SkillInstallStateRecord
                {
                    Profile = ProfileKind.Global,
                    InstalledRelativePath = "demo-skill",
                    BaselineFiles = new List<SkillFileFingerprintRecord>()
                }
            }
        });
        await File.WriteAllTextAsync(Path.Combine(scope.RootPath, "config", "skills-state.json"), statesJson);

        var service = new SkillsCatalogService(new FixedHubRootLocator(scope.RootPath));

        var result = await service.RollbackInstalledSkillAsync(ProfileKind.Global, "demo-skill", selectedBackupDirectory);

        Assert.True(result.Success, result.Details);
        Assert.Equal("backup-version", await File.ReadAllTextAsync(Path.Combine(installDirectory, "SKILL.md")));
        Assert.Contains("pre-rollback", result.Details ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LoadAsync_MigratesLegacyAutoUpdateToDailyCheckOnly()
    {
        using var scope = new TestHubRootScope();
        Directory.CreateDirectory(Path.Combine(scope.RootPath, "skills"));
        await File.WriteAllTextAsync(
            Path.Combine(scope.RootPath, "skills", "sources.json"),
            """
            {
              "sources": [
                {
                  "localName": "legacy-source",
                  "profile": "Global",
                  "kind": "LocalDirectory",
                  "location": "C:\\legacy-source",
                  "reference": "",
                  "isEnabled": true,
                  "autoUpdate": true
                }
              ]
            }
            """);

        var service = new SkillsCatalogService(new FixedHubRootLocator(scope.RootPath));

        var snapshot = await service.LoadAsync();

        var source = Assert.Single(snapshot.Sources);
        Assert.True(source.AutoUpdate);
        Assert.Equal(24, source.ScheduledUpdateIntervalHours);
        Assert.Equal(SkillScheduledUpdateAction.CheckOnly, source.ScheduledUpdateAction);
    }

    [Fact]
    public async Task RunDueScheduledUpdatesAsync_OnlyExecutesDueSources()
    {
        using var scope = new TestHubRootScope();
        Directory.CreateDirectory(Path.Combine(scope.RootPath, "skills"));
        var now = DateTimeOffset.UtcNow;
        await File.WriteAllTextAsync(
            Path.Combine(scope.RootPath, "skills", "sources.json"),
            JsonSerializer.Serialize(new
            {
                sources = new object[]
                {
                    new
                    {
                        localName = "due-source",
                        profile = "Global",
                        kind = "LocalDirectory",
                        location = "C:\\due-source",
                        reference = "",
                        isEnabled = true,
                        autoUpdate = true,
                        scheduledUpdateIntervalHours = 24,
                        scheduledUpdateAction = "CheckOnly",
                        lastScheduledRunAt = now.AddDays(-2)
                    },
                    new
                    {
                        localName = "not-due-source",
                        profile = "Global",
                        kind = "LocalDirectory",
                        location = "C:\\not-due-source",
                        reference = "",
                        isEnabled = true,
                        autoUpdate = true,
                        scheduledUpdateIntervalHours = 24,
                        scheduledUpdateAction = "CheckOnly",
                        lastScheduledRunAt = now.AddHours(-1)
                    }
                }
            },
            new JsonSerializerOptions
            {
                WriteIndented = true
            }));

        var service = new SkillsCatalogService(new FixedHubRootLocator(scope.RootPath));

        var result = await service.RunDueScheduledUpdatesAsync();

        var sourceResult = Assert.Single(result.Sources);
        Assert.Contains("due-source", sourceResult.SourceDisplayName, StringComparison.OrdinalIgnoreCase);

        var snapshot = await service.LoadAsync();
        var dueSource = Assert.Single(snapshot.Sources.Where(item => item.LocalName == "due-source"));
        var notDueSource = Assert.Single(snapshot.Sources.Where(item => item.LocalName == "not-due-source"));
        Assert.True(dueSource.LastScheduledRunAt.HasValue);
        Assert.Equal(now.AddHours(-1).ToUniversalTime().ToString("yyyyMMddHH"), notDueSource.LastScheduledRunAt!.Value.ToUniversalTime().ToString("yyyyMMddHH"));
    }

    [Fact]
    public async Task PreviewAndApplyOverlayMergeAsync_UsesFileLevelDecisions()
    {
        using var scope = new TestHubRootScope();
        var sourceRoot = Path.Combine(scope.RootPath, "source-root");
        var sourceCatalogRoot = Path.Combine(sourceRoot, "catalog");
        var sourceSkillDirectory = Path.Combine(sourceCatalogRoot, "demo-skill");
        var installDirectory = Path.Combine(scope.RootPath, "skills", "global", "demo-skill");
        Directory.CreateDirectory(sourceSkillDirectory);
        Directory.CreateDirectory(installDirectory);

        await File.WriteAllTextAsync(Path.Combine(sourceSkillDirectory, "SKILL.md"), "baseline-skill");
        await File.WriteAllTextAsync(Path.Combine(sourceSkillDirectory, "README.md"), "baseline-readme");
        await File.WriteAllTextAsync(Path.Combine(sourceSkillDirectory, "delete.txt"), "baseline-delete");
        await File.WriteAllTextAsync(Path.Combine(sourceSkillDirectory, "conflict.txt"), "baseline-conflict");

        await File.WriteAllTextAsync(Path.Combine(installDirectory, "SKILL.md"), "baseline-skill");
        await File.WriteAllTextAsync(Path.Combine(installDirectory, "README.md"), "baseline-readme");
        await File.WriteAllTextAsync(Path.Combine(installDirectory, "delete.txt"), "baseline-delete");
        await File.WriteAllTextAsync(Path.Combine(installDirectory, "conflict.txt"), "baseline-conflict");

        var service = new SkillsCatalogService(new FixedHubRootLocator(scope.RootPath));
        var saveSourceResult = await service.SaveSourceAsync(
            null,
            null,
            new SkillSourceRecord
            {
                LocalName = "local-source",
                Profile = ProfileKind.Global,
                Kind = SkillSourceKind.LocalDirectory,
                Location = sourceRoot,
                CatalogPath = "catalog",
                IsEnabled = true,
                AutoUpdate = true,
                ScheduledUpdateIntervalHours = 24,
                ScheduledUpdateAction = SkillScheduledUpdateAction.CheckOnly
            });
        Assert.True(saveSourceResult.Success, saveSourceResult.Details);

        var saveInstallResult = await service.SaveInstallAsync(new SkillInstallRecord
        {
            Name = "demo-skill",
            Profile = ProfileKind.Global,
            InstalledRelativePath = "demo-skill",
            SourceLocalName = "local-source",
            SourceProfile = ProfileKind.Global,
            SourceSkillPath = "demo-skill",
            CustomizationMode = SkillCustomizationMode.Overlay
        });
        Assert.True(saveInstallResult.Success, saveInstallResult.Details);

        await File.WriteAllTextAsync(Path.Combine(sourceSkillDirectory, "README.md"), "source-readme");
        File.Delete(Path.Combine(sourceSkillDirectory, "delete.txt"));
        await File.WriteAllTextAsync(Path.Combine(sourceSkillDirectory, "conflict.txt"), "source-conflict");
        await File.WriteAllTextAsync(Path.Combine(sourceSkillDirectory, "new.txt"), "source-new");

        await File.WriteAllTextAsync(Path.Combine(installDirectory, "conflict.txt"), "local-conflict");
        await File.WriteAllTextAsync(Path.Combine(installDirectory, "local-only.txt"), "local-only");

        var preview = await service.PreviewOverlayMergeAsync(ProfileKind.Global, "demo-skill");

        Assert.NotNull(preview);
        Assert.True(preview!.HasChanges);
        Assert.Contains(preview.Files, item => item.RelativePath == "README.md" && item.Status == SkillMergeFileStatus.SourceChanged && item.SuggestedDecision == SkillMergeDecisionMode.UseSource);
        Assert.Contains(preview.Files, item => item.RelativePath == "delete.txt" && item.Status == SkillMergeFileStatus.SourceDeleted && item.SuggestedDecision == SkillMergeDecisionMode.ApplyDeletion);
        Assert.Contains(preview.Files, item => item.RelativePath == "conflict.txt" && item.Status == SkillMergeFileStatus.Conflict && item.SuggestedDecision == SkillMergeDecisionMode.Skip);
        Assert.Contains(preview.Files, item => item.RelativePath == "new.txt" && item.Status == SkillMergeFileStatus.SourceOnly && item.SuggestedDecision == SkillMergeDecisionMode.UseSource);
        Assert.Contains(preview.Files, item => item.RelativePath == "local-only.txt" && item.Status == SkillMergeFileStatus.LocalOnly && item.SuggestedDecision == SkillMergeDecisionMode.KeepLocal);

        var applyResult = await service.ApplyOverlayMergeAsync(
            ProfileKind.Global,
            "demo-skill",
            new[]
            {
                new SkillMergeDecision("README.md", SkillMergeDecisionMode.UseSource),
                new SkillMergeDecision("delete.txt", SkillMergeDecisionMode.ApplyDeletion),
                new SkillMergeDecision("conflict.txt", SkillMergeDecisionMode.KeepLocal),
                new SkillMergeDecision("new.txt", SkillMergeDecisionMode.UseSource),
                new SkillMergeDecision("local-only.txt", SkillMergeDecisionMode.KeepLocal)
            });

        Assert.True(applyResult.Success, applyResult.Details);
        Assert.Equal("source-readme", await File.ReadAllTextAsync(Path.Combine(installDirectory, "README.md")));
        Assert.False(File.Exists(Path.Combine(installDirectory, "delete.txt")));
        Assert.Equal("local-conflict", await File.ReadAllTextAsync(Path.Combine(installDirectory, "conflict.txt")));
        Assert.Equal("source-new", await File.ReadAllTextAsync(Path.Combine(installDirectory, "new.txt")));
        Assert.Equal("local-only", await File.ReadAllTextAsync(Path.Combine(installDirectory, "local-only.txt")));

        var backupDirectory = Directory.EnumerateDirectories(
            Path.Combine(scope.RootPath, "backups", "skills", "global", "demo-skill"),
            "*pre-merge",
            SearchOption.TopDirectoryOnly).Single();
        Assert.True(Directory.Exists(backupDirectory));

        var overlayFilesRoot = Path.Combine(scope.RootPath, "skills-overrides", "global", "demo-skill", "files");
        Assert.True(File.Exists(Path.Combine(overlayFilesRoot, "conflict.txt")));
        Assert.True(File.Exists(Path.Combine(overlayFilesRoot, "local-only.txt")));
    }

    [Fact]
    public async Task CheckSourceVersionsAsync_Tracks_Latest_Stable_Tag_And_Ignores_Prerelease()
    {
        using var scope = new TestHubRootScope();
        var repositoryPath = Path.Combine(scope.RootPath, "repo");
        InitializeGitRepository(repositoryPath);
        await File.WriteAllTextAsync(Path.Combine(repositoryPath, "SKILL.md"), "v1.0.0");
        RunGit(repositoryPath, "add", ".");
        RunGit(repositoryPath, "commit", "-m", "v1.0.0");
        RunGit(repositoryPath, "tag", "v1.0.0");
        await File.WriteAllTextAsync(Path.Combine(repositoryPath, "SKILL.md"), "v1.1.0-beta");
        RunGit(repositoryPath, "add", ".");
        RunGit(repositoryPath, "commit", "-m", "v1.1.0-beta");
        RunGit(repositoryPath, "tag", "v1.1.0-beta");
        await File.WriteAllTextAsync(Path.Combine(repositoryPath, "SKILL.md"), "v1.1.0");
        RunGit(repositoryPath, "add", ".");
        RunGit(repositoryPath, "commit", "-m", "v1.1.0");
        RunGit(repositoryPath, "tag", "v1.1.0");

        var service = new SkillsCatalogService(new FixedHubRootLocator(scope.RootPath));
        var saveResult = await service.SaveSourceAsync(
            null,
            null,
            new SkillSourceRecord
            {
                LocalName = "git-source",
                Profile = ProfileKind.Global,
                Kind = SkillSourceKind.GitRepository,
                Location = repositoryPath,
                Reference = "v1.0.0",
                IsEnabled = true,
                VersionTrackingMode = SkillVersionTrackingMode.FollowLatestStableTag
            });
        Assert.True(saveResult.Success, saveResult.Details);

        var checkResult = await service.CheckSourceVersionsAsync("git-source", ProfileKind.Global);
        Assert.True(checkResult.Success, checkResult.Details);

        var source = Assert.Single((await service.LoadAsync()).Sources);
        Assert.Equal(SkillVersionTrackingMode.FollowLatestStableTag, source.VersionTrackingMode);
        Assert.Equal("v1.0.0", source.ResolvedVersionTag);
        Assert.Equal("v1.1.0", source.AvailableVersionTags.First());
        Assert.DoesNotContain("v1.1.0-beta", source.AvailableVersionTags, StringComparer.OrdinalIgnoreCase);
        Assert.True(source.HasPendingVersionUpgrade);

        var upgradeResult = await service.UpgradeSourceVersionAsync("git-source", ProfileKind.Global);
        Assert.True(upgradeResult.Success, upgradeResult.Details);

        source = Assert.Single((await service.LoadAsync()).Sources);
        Assert.Equal("v1.1.0", source.Reference);
        Assert.False(source.HasPendingVersionUpgrade);
    }

    [Fact]
    public async Task CheckSourceVersionsAsync_Falls_Back_To_Legacy_Mode_When_No_Stable_Tags()
    {
        using var scope = new TestHubRootScope();
        var repositoryPath = Path.Combine(scope.RootPath, "repo-no-tags");
        InitializeGitRepository(repositoryPath);
        await File.WriteAllTextAsync(Path.Combine(repositoryPath, "SKILL.md"), "main");
        RunGit(repositoryPath, "add", ".");
        RunGit(repositoryPath, "commit", "-m", "initial");

        var service = new SkillsCatalogService(new FixedHubRootLocator(scope.RootPath));
        var saveResult = await service.SaveSourceAsync(
            null,
            null,
            new SkillSourceRecord
            {
                LocalName = "legacy-fallback",
                Profile = ProfileKind.Global,
                Kind = SkillSourceKind.GitRepository,
                Location = repositoryPath,
                Reference = "main",
                IsEnabled = true,
                VersionTrackingMode = SkillVersionTrackingMode.FollowLatestStableTag
            });
        Assert.True(saveResult.Success, saveResult.Details);

        var checkResult = await service.CheckSourceVersionsAsync("legacy-fallback", ProfileKind.Global);
        Assert.True(checkResult.Success, checkResult.Details);

        var source = Assert.Single((await service.LoadAsync()).Sources);
        Assert.Equal(SkillVersionTrackingMode.FollowReferenceLegacy, source.VersionTrackingMode);
        Assert.Empty(source.AvailableVersionTags);
        Assert.False(source.HasPendingVersionUpgrade);
    }

    private static void InitializeGitRepository(string repositoryPath)
    {
        Directory.CreateDirectory(repositoryPath);
        RunGit(repositoryPath, "init", "--initial-branch=main");
        RunGit(repositoryPath, "config", "user.email", "tests@example.invalid");
        RunGit(repositoryPath, "config", "user.name", "AIHub Tests");
    }

    private static void RunGit(string workingDirectory, params string[] arguments)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "git",
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        process.Start();
        var standardOutput = process.StandardOutput.ReadToEnd();
        var standardError = process.StandardError.ReadToEnd();
        process.WaitForExit();

        Assert.True(process.ExitCode == 0, string.IsNullOrWhiteSpace(standardError) ? standardOutput : standardError);
    }
}

