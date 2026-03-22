using System.Diagnostics;
using System.Text.Json;
using AIHub.Application.Models;
using AIHub.Application.Services;
using AIHub.Contracts;
using AIHub.Infrastructure;

namespace AIHub.Application.Tests;

public sealed class SkillsCatalogServiceTests
{
    private static string GetCompanySourceRoot(string hubRoot) => Path.Combine(hubRoot, "source");

    private static string GetProfileSkillsRoot(string hubRoot, string profile) => Path.Combine(GetCompanySourceRoot(hubRoot), "profiles", profile, "skills");

    private static string GetSkillLibraryRoot(string hubRoot) => Path.Combine(GetCompanySourceRoot(hubRoot), "library", "skills");

    private static string GetSkillSourcesPath(string hubRoot) => Path.Combine(GetCompanySourceRoot(hubRoot), "registry", "skills-sources.json");

    private static string GetSkillInstallsPath(string hubRoot) => Path.Combine(GetCompanySourceRoot(hubRoot), "registry", "skills-installs.json");

    private static string GetSkillStatesPath(string hubRoot) => Path.Combine(GetCompanySourceRoot(hubRoot), "registry", "skills-state.json");

    private static SkillsCatalogService CreateService(string rootPath, RecordingWorkspaceAutomationService? automationService = null)
        => new(new FixedHubRootLocator(rootPath), null, automationService ?? new RecordingWorkspaceAutomationService());

    [Fact]
    public async Task LoadAsync_OrdersBackupHistoryNewestFirst()
    {
        using var scope = new TestHubRootScope();
        var skillDirectory = GetProfileSkillsRoot(scope.RootPath, WorkspaceProfiles.GlobalId);
        skillDirectory = Path.Combine(skillDirectory, "demo-skill");
        Directory.CreateDirectory(skillDirectory);
        await File.WriteAllTextAsync(Path.Combine(skillDirectory, "SKILL.md"), "current");

        Directory.CreateDirectory(Path.Combine(scope.RootPath, "backups", "skills", "global", "demo-skill", "20260307-010101-sync"));
        Directory.CreateDirectory(Path.Combine(scope.RootPath, "backups", "skills", "global", "demo-skill", "20260308-020202-sync"));

        var service = CreateService(scope.RootPath);

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
        var installDirectory = Path.Combine(GetProfileSkillsRoot(scope.RootPath, WorkspaceProfiles.GlobalId), "demo-skill");
        Directory.CreateDirectory(installDirectory);
        await File.WriteAllTextAsync(Path.Combine(installDirectory, "SKILL.md"), "current-version");

        var selectedBackupDirectory = Path.Combine(scope.RootPath, "backups", "skills", "global", "demo-skill", "20260308-030303-sync");
        Directory.CreateDirectory(selectedBackupDirectory);
        await File.WriteAllTextAsync(Path.Combine(selectedBackupDirectory, "SKILL.md"), "backup-version");

        Directory.CreateDirectory(Path.GetDirectoryName(GetSkillStatesPath(scope.RootPath))!);
        var statesJson = JsonSerializer.Serialize(new
        {
            states = new[]
            {
                new SkillInstallStateRecord
                {
                    Profile = WorkspaceProfiles.GlobalId,
                    InstalledRelativePath = "demo-skill",
                    BaselineFiles = new List<SkillFileFingerprintRecord>()
                }
            }
        });
        await File.WriteAllTextAsync(GetSkillStatesPath(scope.RootPath), statesJson);

        var service = CreateService(scope.RootPath);

        var result = await service.RollbackInstalledSkillAsync(WorkspaceProfiles.GlobalId, "demo-skill", selectedBackupDirectory);

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
                  "profile": "global",
                  "kind": "LocalDirectory",
                  "location": "C:\\legacy-source",
                  "reference": "",
                  "isEnabled": true,
                  "autoUpdate": true
                }
              ]
            }
            """);

        var service = CreateService(scope.RootPath);

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
                        profile = WorkspaceProfiles.GlobalId,
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
                        profile = WorkspaceProfiles.GlobalId,
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

        var service = CreateService(scope.RootPath);

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
        var installDirectory = Path.Combine(GetProfileSkillsRoot(scope.RootPath, WorkspaceProfiles.GlobalId), "demo-skill");
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

        var service = CreateService(scope.RootPath);
        var saveSourceResult = await service.SaveSourceAsync(
            null,
            (string?)null,
            new SkillSourceRecord
            {
                LocalName = "local-source",
                Profile = WorkspaceProfiles.GlobalId,
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
            Profile = WorkspaceProfiles.GlobalId,
            InstalledRelativePath = "demo-skill",
            SourceLocalName = "local-source",
            SourceProfile = WorkspaceProfiles.GlobalId,
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

        var preview = await service.PreviewOverlayMergeAsync(WorkspaceProfiles.GlobalId, "demo-skill");

        Assert.NotNull(preview);
        Assert.True(preview!.HasChanges);
        Assert.Contains(preview.Files, item => item.RelativePath == "README.md" && item.Status == SkillMergeFileStatus.SourceChanged && item.SuggestedDecision == SkillMergeDecisionMode.UseSource);
        Assert.Contains(preview.Files, item => item.RelativePath == "delete.txt" && item.Status == SkillMergeFileStatus.SourceDeleted && item.SuggestedDecision == SkillMergeDecisionMode.ApplyDeletion);
        Assert.Contains(preview.Files, item => item.RelativePath == "conflict.txt" && item.Status == SkillMergeFileStatus.Conflict && item.SuggestedDecision == SkillMergeDecisionMode.Skip);
        Assert.Contains(preview.Files, item => item.RelativePath == "new.txt" && item.Status == SkillMergeFileStatus.SourceOnly && item.SuggestedDecision == SkillMergeDecisionMode.UseSource);
        Assert.Contains(preview.Files, item => item.RelativePath == "local-only.txt" && item.Status == SkillMergeFileStatus.LocalOnly && item.SuggestedDecision == SkillMergeDecisionMode.KeepLocal);

        var applyResult = await service.ApplyOverlayMergeAsync(
            WorkspaceProfiles.GlobalId,
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
            (string?)null,
            new SkillSourceRecord
            {
                LocalName = "git-source",
                Profile = WorkspaceProfiles.GlobalId,
                Kind = SkillSourceKind.GitRepository,
                Location = repositoryPath,
                Reference = "v1.0.0",
                IsEnabled = true,
                VersionTrackingMode = SkillVersionTrackingMode.FollowLatestStableTag
            });
        Assert.True(saveResult.Success, saveResult.Details);

        var checkResult = await service.CheckSourceVersionsAsync("git-source", WorkspaceProfiles.GlobalId);
        Assert.True(checkResult.Success, checkResult.Details);

        var source = Assert.Single((await service.LoadAsync()).Sources);
        Assert.Equal(SkillVersionTrackingMode.FollowLatestStableTag, source.VersionTrackingMode);
        Assert.Equal("v1.0.0", source.ResolvedVersionTag);
        Assert.Equal("v1.1.0", source.AvailableVersionTags.First());
        Assert.DoesNotContain("v1.1.0-beta", source.AvailableVersionTags, StringComparer.OrdinalIgnoreCase);
        Assert.True(source.HasPendingVersionUpgrade);

        var upgradeResult = await service.UpgradeSourceVersionAsync("git-source", WorkspaceProfiles.GlobalId);
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
            (string?)null,
            new SkillSourceRecord
            {
                LocalName = "legacy-fallback",
                Profile = WorkspaceProfiles.GlobalId,
                Kind = SkillSourceKind.GitRepository,
                Location = repositoryPath,
                Reference = "main",
                IsEnabled = true,
                VersionTrackingMode = SkillVersionTrackingMode.FollowLatestStableTag
            });
        Assert.True(saveResult.Success, saveResult.Details);

        var checkResult = await service.CheckSourceVersionsAsync("legacy-fallback", WorkspaceProfiles.GlobalId);
        Assert.True(checkResult.Success, checkResult.Details);

        var source = Assert.Single((await service.LoadAsync()).Sources);
        Assert.Equal(SkillVersionTrackingMode.FollowReferenceLegacy, source.VersionTrackingMode);
        Assert.Empty(source.AvailableVersionTags);
        Assert.False(source.HasPendingVersionUpgrade);
    }

    [Fact]
    public async Task LoadAsync_And_DeleteSourceAsync_Scope_Same_LocalName_By_Profile()
    {
        using var scope = new TestHubRootScope();
        Directory.CreateDirectory(Path.Combine(scope.RootPath, "skills"));
        await File.WriteAllTextAsync(
            Path.Combine(scope.RootPath, "skills", "sources.json"),
            JsonSerializer.Serialize(new
            {
                sources = new[]
                {
                    new SkillSourceRecord
                    {
                        LocalName = "shared-source",
                        Profile = WorkspaceProfiles.GlobalId,
                        Kind = SkillSourceKind.LocalDirectory,
                        Location = "C:\\global"
                    },
                    new SkillSourceRecord
                    {
                        LocalName = "shared-source",
                        Profile = WorkspaceProfiles.FrontendId,
                        Kind = SkillSourceKind.LocalDirectory,
                        Location = "C:\\frontend"
                    }
                }
            }));

        var service = CreateService(scope.RootPath);

        var snapshot = await service.LoadAsync();
        Assert.Equal(2, snapshot.Sources.Count(item => item.LocalName == "shared-source"));
        Assert.Contains(snapshot.Sources, item => item.Profile == WorkspaceProfiles.GlobalId && item.Location == "C:\\global");
        Assert.Contains(snapshot.Sources, item => item.Profile == WorkspaceProfiles.FrontendId && item.Location == "C:\\frontend");

        var deleteResult = await service.DeleteSourceAsync("shared-source", WorkspaceProfiles.FrontendId);
        Assert.True(deleteResult.Success, deleteResult.Details);

        snapshot = await service.LoadAsync();
        Assert.Single(snapshot.Sources.Where(item => item.LocalName == "shared-source"));
        Assert.Contains(snapshot.Sources, item => item.Profile == WorkspaceProfiles.GlobalId);
    }

    [Fact]
    public async Task LoadAsync_Includes_Unbound_Library_Skills()
    {
        using var scope = new TestHubRootScope();
        var librarySkillDirectory = Path.Combine(GetSkillLibraryRoot(scope.RootPath), "demo-skill");
        Directory.CreateDirectory(librarySkillDirectory);
        await File.WriteAllTextAsync(Path.Combine(librarySkillDirectory, "SKILL.md"), "demo");

        var service = CreateService(scope.RootPath);

        var snapshot = await service.LoadAsync();

        var skill = Assert.Single(snapshot.InstalledSkills);
        Assert.Equal("library", skill.Profile);
        Assert.Equal("未绑定", skill.ProfileDisplayName);
        Assert.Empty(skill.BindingProfileIds);
        Assert.Equal(new[] { "未绑定" }, skill.BindingDisplayTags);
        Assert.Equal("未绑定", skill.BindingSummaryDisplay);
        Assert.Equal("demo-skill", skill.RelativePath);
    }

    [Fact]
    public async Task LoadAsync_Aggregates_MultiBound_Skill_Into_One_Record()
    {
        using var scope = new TestHubRootScope();
        var globalSkillDirectory = Path.Combine(GetProfileSkillsRoot(scope.RootPath, WorkspaceProfiles.GlobalId), "demo-skill");
        Directory.CreateDirectory(globalSkillDirectory);
        await File.WriteAllTextAsync(Path.Combine(globalSkillDirectory, "SKILL.md"), "demo");

        var service = CreateService(scope.RootPath);
        await service.SaveInstallAsync(new SkillInstallRecord
        {
            Name = "demo-skill",
            Profile = WorkspaceProfiles.GlobalId,
            InstalledRelativePath = "demo-skill",
            CustomizationMode = SkillCustomizationMode.Local
        });

        var bindResult = await service.SaveSkillBindingsAsync(
            WorkspaceProfiles.GlobalId,
            "demo-skill",
            new[] { WorkspaceProfiles.GlobalId, WorkspaceProfiles.FrontendId });

        Assert.True(bindResult.Success, bindResult.Details);

        var snapshot = await service.LoadAsync();

        var skill = Assert.Single(snapshot.InstalledSkills.Where(item => item.RelativePath == "demo-skill"));
        Assert.Equal(WorkspaceProfiles.GlobalId, skill.Profile);
        Assert.Equal(
            new[] { WorkspaceProfiles.GlobalId, WorkspaceProfiles.FrontendId },
            skill.BindingProfileIds.OrderBy(SortProfileId).ToArray());
        Assert.Equal(
            new[] { WorkspaceProfiles.GlobalDisplayName, WorkspaceProfiles.FrontendDisplayName },
            skill.BindingDisplayTags.OrderBy(SortProfileDisplay).ToArray());
        Assert.Contains(WorkspaceProfiles.GlobalDisplayName, skill.BindingSummaryDisplay, StringComparison.Ordinal);
        Assert.Contains(WorkspaceProfiles.FrontendDisplayName, skill.BindingSummaryDisplay, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SaveSkillBindingsAsync_Fans_Out_And_Removes_Profile_Copies()
    {
        using var scope = new TestHubRootScope();
        var globalSkillDirectory = Path.Combine(GetProfileSkillsRoot(scope.RootPath, WorkspaceProfiles.GlobalId), "demo-skill");
        Directory.CreateDirectory(globalSkillDirectory);
        await File.WriteAllTextAsync(Path.Combine(globalSkillDirectory, "SKILL.md"), "demo");

        var recorder = new RecordingWorkspaceAutomationService();
        var service = CreateService(scope.RootPath, recorder);
        await service.SaveInstallAsync(new SkillInstallRecord
        {
            Name = "demo-skill",
            Profile = WorkspaceProfiles.GlobalId,
            InstalledRelativePath = "demo-skill",
            CustomizationMode = SkillCustomizationMode.Local
        });
        await service.CaptureBaselineAsync(WorkspaceProfiles.GlobalId, "demo-skill");

        var bindResult = await service.SaveSkillBindingsAsync(
            WorkspaceProfiles.GlobalId,
            "demo-skill",
            new[] { WorkspaceProfiles.GlobalId, WorkspaceProfiles.FrontendId });

        Assert.True(bindResult.Success, bindResult.Details);
        Assert.True(File.Exists(Path.Combine(GetProfileSkillsRoot(scope.RootPath, WorkspaceProfiles.FrontendId), "demo-skill", "SKILL.md")));

        var snapshot = await service.LoadAsync();
        var skill = Assert.Single(snapshot.InstalledSkills.Where(item => item.RelativePath == "demo-skill"));
        Assert.Contains(WorkspaceProfiles.FrontendId, skill.BindingProfileIds, StringComparer.OrdinalIgnoreCase);

        var removeResult = await service.SaveSkillBindingsAsync(
            WorkspaceProfiles.GlobalId,
            "demo-skill",
            Array.Empty<string>());

        Assert.True(removeResult.Success, removeResult.Details);
        Assert.True(File.Exists(Path.Combine(GetSkillLibraryRoot(scope.RootPath), "demo-skill", "SKILL.md")));
        Assert.False(Directory.Exists(Path.Combine(GetProfileSkillsRoot(scope.RootPath, WorkspaceProfiles.GlobalId), "demo-skill")));
        Assert.False(Directory.Exists(Path.Combine(GetProfileSkillsRoot(scope.RootPath, WorkspaceProfiles.FrontendId), "demo-skill")));

        snapshot = await service.LoadAsync();
        skill = Assert.Single(snapshot.InstalledSkills.Where(item => item.RelativePath == "demo-skill"));
        Assert.Equal("library", skill.Profile);
        Assert.Empty(skill.BindingProfileIds);
        Assert.Equal(new[] { "未绑定" }, skill.BindingDisplayTags);
        Assert.Equal("未绑定", skill.BindingSummaryDisplay);
        Assert.True(recorder.ApplyGlobalLinksCallCount > 0);
    }

    [Fact]
    public async Task PreviewSkillBindingResolutionAsync_Move_To_Library_Reports_Library_As_Materialized_Result()
    {
        using var scope = new TestHubRootScope();
        var globalSkillDirectory = Path.Combine(GetProfileSkillsRoot(scope.RootPath, WorkspaceProfiles.GlobalId), "demo-skill");
        Directory.CreateDirectory(globalSkillDirectory);
        await File.WriteAllTextAsync(Path.Combine(globalSkillDirectory, "SKILL.md"), "global-version");

        var service = CreateService(scope.RootPath);
        await service.SaveInstallAsync(new SkillInstallRecord
        {
            Name = "demo-skill",
            Profile = WorkspaceProfiles.GlobalId,
            InstalledRelativePath = "demo-skill",
            CustomizationMode = SkillCustomizationMode.Local
        });

        var preview = await service.PreviewSkillBindingResolutionAsync(
            WorkspaceProfiles.GlobalId,
            "demo-skill",
            Array.Empty<string>());

        Assert.Equal(BindingResolutionStatus.Resolved, preview.ResolutionStatus);
        Assert.Equal(BindingSourceKind.Library, preview.PrimaryDestinationKind);
        Assert.Equal(new[] { "library" }, preview.MaterializedProfileIds);
        Assert.Equal(new[] { "library" }, preview.RefreshedProfileIds);
        Assert.Contains(WorkspaceProfiles.GlobalId, preview.RemovedProfileIds, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(WorkspaceProfiles.GlobalId, preview.ContentDonorProfileId);
    }

    [Fact]
    public async Task PreviewSkillBindingResolutionAsync_Unresolvable_Reports_No_Content_Donor()
    {
        using var scope = new TestHubRootScope();
        var service = CreateService(scope.RootPath);

        var preview = await service.PreviewSkillBindingResolutionAsync(
            WorkspaceProfiles.BackendId,
            "demo-skill",
            new[] { WorkspaceProfiles.FrontendId });

        Assert.Equal(BindingResolutionStatus.Unresolvable, preview.ResolutionStatus);
        Assert.Equal(BindingSourceKind.None, preview.ContentDonorKind);
        Assert.Equal(string.Empty, preview.ContentDonorProfileId);
        Assert.Equal(BindingSourceKind.None, preview.MetadataDonorKind);
        Assert.Equal(string.Empty, preview.MetadataDonorProfileId);
        Assert.Equal(BindingSourceKind.None, preview.PrimaryDestinationKind);
        Assert.Equal(string.Empty, preview.PrimaryDestinationProfileId);
        Assert.Empty(preview.MaterializedProfileIds);
        Assert.Empty(preview.RefreshedProfileIds);
        Assert.Empty(preview.RemovedProfileIds);
        Assert.Equal(BindingSourceKind.None, typeof(BindingResolutionPreview).GetProperty("SourceKind")?.GetValue(preview));
        Assert.Equal(string.Empty, typeof(BindingResolutionPreview).GetProperty("SourceProfileId")?.GetValue(preview));
    }

    [Fact]
    public void BindingResolutionPreview_Does_Not_Expose_Legacy_Effective_Source_Aliases()
    {
        var preview = new BindingResolutionPreview(
            BindingResolutionStatus.Resolved,
            string.Empty,
            BindingSourceKind.Library,
            "library",
            BindingSourceKind.Category,
            WorkspaceProfiles.FrontendId,
            new[] { WorkspaceProfiles.FrontendId },
            new[] { "demo-skill" })
        {
            MetadataDonorKind = BindingSourceKind.Category,
            MetadataDonorProfileId = WorkspaceProfiles.BackendId
        };

        Assert.Null(typeof(BindingResolutionPreview).GetProperty("EffectiveSourceKind"));
        Assert.Null(typeof(BindingResolutionPreview).GetProperty("EffectiveSourceProfileId"));
        Assert.Equal(BindingSourceKind.Category, typeof(BindingResolutionPreview).GetProperty("SourceKind")?.GetValue(preview));
        Assert.Equal(WorkspaceProfiles.BackendId, typeof(BindingResolutionPreview).GetProperty("SourceProfileId")?.GetValue(preview));
    }

    [Fact]
    public async Task PreviewSkillBindingResolutionAsync_Empty_Path_Matches_Save_Failure_And_Leaves_Profile_Lists_Empty()
    {
        using var scope = new TestHubRootScope();
        var service = CreateService(scope.RootPath);

        var preview = await service.PreviewSkillBindingResolutionAsync(
            WorkspaceProfiles.BackendId,
            "",
            new[] { WorkspaceProfiles.FrontendId });

        var result = await service.SaveSkillBindingsAsync(
            WorkspaceProfiles.BackendId,
            "",
            new[] { WorkspaceProfiles.FrontendId });

        Assert.Equal(BindingResolutionStatus.Unresolvable, preview.ResolutionStatus);
        Assert.Equal("Select a skill before editing bindings.", preview.ResolutionReason);
        Assert.False(result.Success);
        Assert.Equal(result.Message, preview.ResolutionReason);
        Assert.Equal(BindingSourceKind.None, preview.PrimaryDestinationKind);
        Assert.Equal(string.Empty, preview.PrimaryDestinationProfileId);
        Assert.Empty(preview.MaterializedProfileIds);
        Assert.Empty(preview.RefreshedProfileIds);
        Assert.Empty(preview.RemovedProfileIds);
    }

    [Fact]
    public async Task PreviewSkillBindingResolutionAsync_Exposes_Metadata_Donor_When_Content_Falls_Back_To_Library()
    {
        using var scope = new TestHubRootScope();
        var librarySkillDirectory = Path.Combine(GetSkillLibraryRoot(scope.RootPath), "demo-skill");
        Directory.CreateDirectory(librarySkillDirectory);
        await File.WriteAllTextAsync(Path.Combine(librarySkillDirectory, "SKILL.md"), "library-version");

        Directory.CreateDirectory(Path.GetDirectoryName(GetSkillInstallsPath(scope.RootPath))!);
        await File.WriteAllTextAsync(
            GetSkillInstallsPath(scope.RootPath),
            JsonSerializer.Serialize(
                new
                {
                    installs = new object[]
                    {
                        new SkillInstallRecord
                        {
                            Name = "demo-skill",
                            Profile = WorkspaceProfiles.BackendId,
                            InstalledRelativePath = "demo-skill",
                            SourceLocalName = "shared-source",
                            SourceProfile = WorkspaceProfiles.GlobalId,
                            SourceSkillPath = "catalog/demo-skill",
                            CustomizationMode = SkillCustomizationMode.Managed
                        }
                    }
                },
                new JsonSerializerOptions
                {
                    WriteIndented = true
                }));

        var service = CreateService(scope.RootPath);

        var preview = await service.PreviewSkillBindingResolutionAsync(
            WorkspaceProfiles.BackendId,
            "demo-skill",
            new[] { WorkspaceProfiles.FrontendId });

        Assert.Equal(BindingResolutionStatus.Resolved, preview.ResolutionStatus);
        Assert.Equal(BindingSourceKind.Library, preview.ContentDonorKind);
        Assert.Equal("library", preview.ContentDonorProfileId);
        Assert.Equal(BindingSourceKind.Category, preview.MetadataDonorKind);
        Assert.Equal(WorkspaceProfiles.BackendId, preview.MetadataDonorProfileId);
        Assert.Equal(BindingSourceKind.Category, typeof(BindingResolutionPreview).GetProperty("SourceKind")?.GetValue(preview));
        Assert.Equal(WorkspaceProfiles.BackendId, typeof(BindingResolutionPreview).GetProperty("SourceProfileId")?.GetValue(preview));
    }

    [Fact]
    public async Task SaveSkillBindingsAsync_Retains_Library_Donor_When_Publishing_To_Category()
    {
        using var scope = new TestHubRootScope();
        var librarySkillDirectory = Path.Combine(GetSkillLibraryRoot(scope.RootPath), "demo-skill");
        var frontendSkillDirectory = Path.Combine(GetProfileSkillsRoot(scope.RootPath, WorkspaceProfiles.FrontendId), "demo-skill");
        Directory.CreateDirectory(librarySkillDirectory);
        await File.WriteAllTextAsync(Path.Combine(librarySkillDirectory, "SKILL.md"), "library-version");

        Directory.CreateDirectory(Path.GetDirectoryName(GetSkillInstallsPath(scope.RootPath))!);
        await File.WriteAllTextAsync(
            GetSkillInstallsPath(scope.RootPath),
            JsonSerializer.Serialize(
                new
                {
                    installs = new object[]
                    {
                        new SkillInstallRecord
                        {
                            Name = "demo-skill",
                            Profile = WorkspaceProfiles.BackendId,
                            InstalledRelativePath = "demo-skill",
                            SourceLocalName = "shared-source",
                            SourceProfile = WorkspaceProfiles.GlobalId,
                            SourceSkillPath = "catalog/demo-skill",
                            CustomizationMode = SkillCustomizationMode.Managed
                        }
                    }
                },
                new JsonSerializerOptions
                {
                    WriteIndented = true
                }));

        var service = CreateService(scope.RootPath);

        var result = await service.SaveSkillBindingsAsync(
            WorkspaceProfiles.BackendId,
            "demo-skill",
            new[] { WorkspaceProfiles.FrontendId });

        Assert.True(result.Success, result.Details);
        Assert.True(File.Exists(Path.Combine(librarySkillDirectory, "SKILL.md")));
        Assert.True(File.Exists(Path.Combine(frontendSkillDirectory, "SKILL.md")));
        Assert.Equal("library-version", await File.ReadAllTextAsync(Path.Combine(frontendSkillDirectory, "SKILL.md")));

        var snapshot = await service.LoadAsync();
        var skill = Assert.Single(snapshot.InstalledSkills.Where(item => item.RelativePath == "demo-skill"));
        Assert.Equal(new[] { WorkspaceProfiles.FrontendId }, skill.BindingProfileIds);
        Assert.True(skill.IsRegistered);
        Assert.True(skill.HasBaseline);
        Assert.False(skill.IsDirty);
    }

    [Fact]
    public async Task SaveSkillBindingsAsync_Removes_Source_Profile_Copy_When_Targets_Remain()
    {
        using var scope = new TestHubRootScope();
        var globalSkillDirectory = Path.Combine(GetProfileSkillsRoot(scope.RootPath, WorkspaceProfiles.GlobalId), "demo-skill");
        var frontendSkillDirectory = Path.Combine(GetProfileSkillsRoot(scope.RootPath, WorkspaceProfiles.FrontendId), "demo-skill");
        Directory.CreateDirectory(globalSkillDirectory);
        Directory.CreateDirectory(frontendSkillDirectory);
        await File.WriteAllTextAsync(Path.Combine(globalSkillDirectory, "SKILL.md"), "global-version");
        await File.WriteAllTextAsync(Path.Combine(frontendSkillDirectory, "SKILL.md"), "frontend-version");

        var service = CreateService(scope.RootPath);
        await service.SaveInstallAsync(new SkillInstallRecord
        {
            Name = "demo-skill",
            Profile = WorkspaceProfiles.GlobalId,
            InstalledRelativePath = "demo-skill",
            CustomizationMode = SkillCustomizationMode.Local
        });
        await service.SaveInstallAsync(new SkillInstallRecord
        {
            Name = "demo-skill",
            Profile = WorkspaceProfiles.FrontendId,
            InstalledRelativePath = "demo-skill",
            CustomizationMode = SkillCustomizationMode.Local
        });
        await service.CaptureBaselineAsync(WorkspaceProfiles.GlobalId, "demo-skill");
        await service.CaptureBaselineAsync(WorkspaceProfiles.FrontendId, "demo-skill");

        var result = await service.SaveSkillBindingsAsync(
            WorkspaceProfiles.FrontendId,
            "demo-skill",
            new[] { WorkspaceProfiles.GlobalId });

        Assert.True(result.Success, result.Details);
        Assert.False(Directory.Exists(frontendSkillDirectory));

        var snapshot = await service.LoadAsync();
        var skill = Assert.Single(snapshot.InstalledSkills.Where(item => item.RelativePath == "demo-skill"));
        Assert.Equal(new[] { WorkspaceProfiles.GlobalId }, skill.BindingProfileIds.OrderBy(SortProfileId).ToArray());
        Assert.Equal("frontend-version", await File.ReadAllTextAsync(Path.Combine(globalSkillDirectory, "SKILL.md")));
    }

    [Fact]
    public async Task SaveSkillBindingsAsync_Prefers_Source_Profile_Content_Over_Stale_Target_Mirror()
    {
        using var scope = new TestHubRootScope();
        var backendSkillDirectory = Path.Combine(GetProfileSkillsRoot(scope.RootPath, WorkspaceProfiles.BackendId), "demo-skill");
        var frontendSkillDirectory = Path.Combine(GetProfileSkillsRoot(scope.RootPath, WorkspaceProfiles.FrontendId), "demo-skill");
        Directory.CreateDirectory(backendSkillDirectory);
        Directory.CreateDirectory(frontendSkillDirectory);
        await File.WriteAllTextAsync(Path.Combine(backendSkillDirectory, "SKILL.md"), "backend-source-version");
        await File.WriteAllTextAsync(Path.Combine(frontendSkillDirectory, "SKILL.md"), "frontend-stale-version");

        var service = CreateService(scope.RootPath);
        await service.SaveInstallAsync(new SkillInstallRecord
        {
            Name = "demo-skill",
            Profile = WorkspaceProfiles.BackendId,
            InstalledRelativePath = "demo-skill",
            SourceLocalName = "backend-source",
            SourceProfile = WorkspaceProfiles.BackendId,
            SourceSkillPath = "catalog/demo-skill",
            CustomizationMode = SkillCustomizationMode.Local
        });
        await service.CaptureBaselineAsync(WorkspaceProfiles.BackendId, "demo-skill");
        await service.SaveInstallAsync(new SkillInstallRecord
        {
            Name = "demo-skill",
            Profile = WorkspaceProfiles.FrontendId,
            InstalledRelativePath = "demo-skill",
            SourceLocalName = "frontend-source",
            SourceProfile = WorkspaceProfiles.FrontendId,
            SourceSkillPath = "catalog/demo-skill",
            CustomizationMode = SkillCustomizationMode.Local
        });
        await service.CaptureBaselineAsync(WorkspaceProfiles.FrontendId, "demo-skill");

        var preview = await service.PreviewSkillBindingResolutionAsync(
            WorkspaceProfiles.BackendId,
            "demo-skill",
            new[] { WorkspaceProfiles.FrontendId });

        Assert.Equal(BindingResolutionStatus.Resolved, preview.ResolutionStatus);
        Assert.Equal(WorkspaceProfiles.BackendId, preview.ContentDonorProfileId);
        Assert.Equal(WorkspaceProfiles.FrontendId, preview.PrimaryDestinationProfileId);
        Assert.Equal(new[] { WorkspaceProfiles.FrontendId }, preview.MaterializedProfileIds);

        var result = await service.SaveSkillBindingsAsync(
            WorkspaceProfiles.BackendId,
            "demo-skill",
            new[] { WorkspaceProfiles.FrontendId });

        Assert.True(result.Success, result.Details);
        Assert.Equal("backend-source-version", await File.ReadAllTextAsync(Path.Combine(frontendSkillDirectory, "SKILL.md")));

        var snapshot = await service.LoadAsync();
        var skill = Assert.Single(snapshot.InstalledSkills.Where(item => item.RelativePath == "demo-skill"));
        Assert.Equal(new[] { WorkspaceProfiles.FrontendId }, skill.BindingProfileIds);
        Assert.Equal(WorkspaceProfiles.BackendId, skill.SourceProfile);
        Assert.Equal("backend-source", skill.SourceLocalName);
        Assert.Equal(SkillCustomizationMode.Local, skill.CustomizationMode);
    }

    [Fact]
    public async Task SaveSkillBindingsAsync_Fails_When_Source_And_Library_Are_Missing_And_Target_Fallbacks_Differ()
    {
        using var scope = new TestHubRootScope();
        var frontendSkillDirectory = Path.Combine(GetProfileSkillsRoot(scope.RootPath, WorkspaceProfiles.FrontendId), "demo-skill");
        var backendSkillDirectory = Path.Combine(GetProfileSkillsRoot(scope.RootPath, WorkspaceProfiles.BackendId), "demo-skill");
        Directory.CreateDirectory(frontendSkillDirectory);
        Directory.CreateDirectory(backendSkillDirectory);
        await File.WriteAllTextAsync(Path.Combine(frontendSkillDirectory, "SKILL.md"), "frontend-version");
        await File.WriteAllTextAsync(Path.Combine(backendSkillDirectory, "SKILL.md"), "backend-version");

        var service = CreateService(scope.RootPath);

        var preview = await service.PreviewSkillBindingResolutionAsync(
            WorkspaceProfiles.GlobalId,
            "demo-skill",
            new[] { WorkspaceProfiles.FrontendId, WorkspaceProfiles.BackendId });

        Assert.Equal(BindingResolutionStatus.Ambiguous, preview.ResolutionStatus);
        Assert.Equal(BindingSourceKind.None, preview.PrimaryDestinationKind);
        Assert.Equal(string.Empty, preview.PrimaryDestinationProfileId);
        Assert.Empty(preview.MaterializedProfileIds);
        Assert.Empty(preview.RefreshedProfileIds);
        Assert.Empty(preview.RemovedProfileIds);

        var result = await service.SaveSkillBindingsAsync(
            WorkspaceProfiles.GlobalId,
            "demo-skill",
            new[] { WorkspaceProfiles.FrontendId, WorkspaceProfiles.BackendId });

        Assert.False(result.Success);
    }

    [Fact]
    public async Task PreviewSkillBindingResolutionAsync_Equivalent_Target_Fallbacks_Report_No_Metadata_Donor()
    {
        using var scope = new TestHubRootScope();
        var frontendSkillDirectory = Path.Combine(GetProfileSkillsRoot(scope.RootPath, WorkspaceProfiles.FrontendId), "demo-skill");
        var backendSkillDirectory = Path.Combine(GetProfileSkillsRoot(scope.RootPath, WorkspaceProfiles.BackendId), "demo-skill");
        Directory.CreateDirectory(frontendSkillDirectory);
        Directory.CreateDirectory(backendSkillDirectory);
        await File.WriteAllTextAsync(Path.Combine(frontendSkillDirectory, "SKILL.md"), "shared-version");
        await File.WriteAllTextAsync(Path.Combine(backendSkillDirectory, "SKILL.md"), "shared-version");

        Directory.CreateDirectory(Path.GetDirectoryName(GetSkillInstallsPath(scope.RootPath))!);
        await File.WriteAllTextAsync(
            GetSkillInstallsPath(scope.RootPath),
            JsonSerializer.Serialize(
                new
                {
                    installs = new object[]
                    {
                        new SkillInstallRecord
                        {
                            Name = "demo-skill",
                            Profile = WorkspaceProfiles.FrontendId,
                            InstalledRelativePath = "demo-skill",
                            SourceLocalName = "frontend-source",
                            SourceProfile = WorkspaceProfiles.FrontendId,
                            SourceSkillPath = "catalog/demo-skill",
                            CustomizationMode = SkillCustomizationMode.Managed
                        },
                        new SkillInstallRecord
                        {
                            Name = "demo-skill",
                            Profile = WorkspaceProfiles.BackendId,
                            InstalledRelativePath = "demo-skill",
                            SourceLocalName = "backend-source",
                            SourceProfile = WorkspaceProfiles.BackendId,
                            SourceSkillPath = "catalog/demo-skill",
                            CustomizationMode = SkillCustomizationMode.Managed
                        }
                    }
                },
                new JsonSerializerOptions
                {
                    WriteIndented = true
                }));

        var service = CreateService(scope.RootPath);

        var preview = await service.PreviewSkillBindingResolutionAsync(
            WorkspaceProfiles.GlobalId,
            "demo-skill",
            new[] { WorkspaceProfiles.FrontendId, WorkspaceProfiles.BackendId, "alpha" });

        Assert.Equal(BindingResolutionStatus.Resolved, preview.ResolutionStatus);
        Assert.Equal(WorkspaceProfiles.FrontendId, preview.ContentDonorProfileId);
        Assert.Equal(BindingSourceKind.None, preview.MetadataDonorKind);
        Assert.Equal(string.Empty, preview.MetadataDonorProfileId);
    }

    [Fact]
    public async Task SaveSkillBindingsAsync_Equivalent_Target_Fallbacks_Preserve_Existing_Metadata_And_Synthesize_New_Target_Metadata()
    {
        using var scope = new TestHubRootScope();
        const string newProfile = "alpha";
        var frontendSkillDirectory = Path.Combine(GetProfileSkillsRoot(scope.RootPath, WorkspaceProfiles.FrontendId), "demo-skill");
        var backendSkillDirectory = Path.Combine(GetProfileSkillsRoot(scope.RootPath, WorkspaceProfiles.BackendId), "demo-skill");
        Directory.CreateDirectory(frontendSkillDirectory);
        Directory.CreateDirectory(backendSkillDirectory);
        await File.WriteAllTextAsync(Path.Combine(frontendSkillDirectory, "SKILL.md"), "shared-version");
        await File.WriteAllTextAsync(Path.Combine(backendSkillDirectory, "SKILL.md"), "shared-version");

        var preservedSyncAt = DateTimeOffset.UtcNow.AddHours(-3);
        var preservedCheckAt = DateTimeOffset.UtcNow.AddHours(-1);
        var originalBaselineCapturedAt = DateTimeOffset.UtcNow.AddDays(-2);

        Directory.CreateDirectory(Path.GetDirectoryName(GetSkillInstallsPath(scope.RootPath))!);
        await File.WriteAllTextAsync(
            GetSkillInstallsPath(scope.RootPath),
            JsonSerializer.Serialize(
                new
                {
                    installs = new object[]
                    {
                        new SkillInstallRecord
                        {
                            Name = "demo-skill",
                            Profile = WorkspaceProfiles.FrontendId,
                            InstalledRelativePath = "demo-skill",
                            SourceLocalName = "frontend-source",
                            SourceProfile = WorkspaceProfiles.FrontendId,
                            SourceSkillPath = "catalog/demo-skill",
                            CustomizationMode = SkillCustomizationMode.Managed
                        },
                        new SkillInstallRecord
                        {
                            Name = "demo-skill",
                            Profile = WorkspaceProfiles.BackendId,
                            InstalledRelativePath = "demo-skill",
                            SourceLocalName = "backend-source",
                            SourceProfile = WorkspaceProfiles.BackendId,
                            SourceSkillPath = "catalog/demo-skill",
                            CustomizationMode = SkillCustomizationMode.Managed
                        }
                    }
                },
                new JsonSerializerOptions
                {
                    WriteIndented = true
                }));
        await File.WriteAllTextAsync(
            GetSkillStatesPath(scope.RootPath),
            JsonSerializer.Serialize(
                new
                {
                    states = new object[]
                    {
                        new SkillInstallStateRecord
                        {
                            Profile = WorkspaceProfiles.BackendId,
                            InstalledRelativePath = "demo-skill",
                            BaselineCapturedAt = originalBaselineCapturedAt,
                            BaselineFiles = new List<SkillFileFingerprintRecord>
                            {
                                new()
                                {
                                    RelativePath = "SKILL.md",
                                    Sha256 = "STALE-BASELINE",
                                    Size = 1
                                }
                            },
                            SourceBaselineFiles = new List<SkillFileFingerprintRecord>
                            {
                                new()
                                {
                                    RelativePath = "SKILL.md",
                                    Sha256 = "PRESERVE-BACKEND-SOURCE",
                                    Size = 2
                                }
                            },
                            LastSyncAt = preservedSyncAt,
                            LastCheckedAt = preservedCheckAt,
                            LastAppliedReference = "backend-ref",
                            LastBackupPath = "backups/backend/demo-skill"
                        }
                    }
                },
                new JsonSerializerOptions
                {
                    WriteIndented = true
                }));

        var service = CreateService(scope.RootPath);

        var result = await service.SaveSkillBindingsAsync(
            WorkspaceProfiles.GlobalId,
            "demo-skill",
            new[] { WorkspaceProfiles.FrontendId, WorkspaceProfiles.BackendId, newProfile });

        Assert.True(result.Success, result.Details);

        using var installsDocument = JsonDocument.Parse(await File.ReadAllTextAsync(GetSkillInstallsPath(scope.RootPath)));
        var installs = installsDocument.RootElement.GetProperty("installs").EnumerateArray().ToArray();
        var savedBackendInstall = installs.Single(item =>
            string.Equals(item.GetProperty("profile").GetString(), WorkspaceProfiles.BackendId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(item.GetProperty("installedRelativePath").GetString(), "demo-skill", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("backend-source", savedBackendInstall.GetProperty("sourceLocalName").GetString());
        Assert.Equal(WorkspaceProfiles.BackendId, savedBackendInstall.GetProperty("sourceProfile").GetString());
        Assert.Equal("catalog/demo-skill", savedBackendInstall.GetProperty("sourceSkillPath").GetString());

        var savedAlphaInstall = installs.Single(item =>
            string.Equals(item.GetProperty("profile").GetString(), newProfile, StringComparison.OrdinalIgnoreCase)
            && string.Equals(item.GetProperty("installedRelativePath").GetString(), "demo-skill", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("local", savedAlphaInstall.GetProperty("customizationMode").GetString());
        Assert.Equal(JsonValueKind.Null, savedAlphaInstall.GetProperty("sourceLocalName").ValueKind);
        Assert.Equal(JsonValueKind.Null, savedAlphaInstall.GetProperty("sourceProfile").ValueKind);
        Assert.Equal(JsonValueKind.Null, savedAlphaInstall.GetProperty("sourceSkillPath").ValueKind);

        using var statesDocument = JsonDocument.Parse(await File.ReadAllTextAsync(GetSkillStatesPath(scope.RootPath)));
        var states = statesDocument.RootElement.GetProperty("states").EnumerateArray().ToArray();
        var savedBackendState = states.Single(item =>
            string.Equals(item.GetProperty("profile").GetString(), WorkspaceProfiles.BackendId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(item.GetProperty("installedRelativePath").GetString(), "demo-skill", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("backend-ref", savedBackendState.GetProperty("lastAppliedReference").GetString());
        Assert.Equal("backups/backend/demo-skill", savedBackendState.GetProperty("lastBackupPath").GetString());
        Assert.Equal(preservedSyncAt.ToUniversalTime().ToString("O"), savedBackendState.GetProperty("lastSyncAt").GetDateTimeOffset().ToUniversalTime().ToString("O"));
        Assert.Equal(preservedCheckAt.ToUniversalTime().ToString("O"), savedBackendState.GetProperty("lastCheckedAt").GetDateTimeOffset().ToUniversalTime().ToString("O"));
        Assert.Contains(
            savedBackendState.GetProperty("sourceBaselineFiles").EnumerateArray().Select(item => item.GetProperty("sha256").GetString()),
            value => string.Equals(value, "PRESERVE-BACKEND-SOURCE", StringComparison.OrdinalIgnoreCase));
        Assert.True(savedBackendState.GetProperty("baselineCapturedAt").GetDateTimeOffset() > originalBaselineCapturedAt);

        var savedAlphaState = states.Single(item =>
            string.Equals(item.GetProperty("profile").GetString(), newProfile, StringComparison.OrdinalIgnoreCase)
            && string.Equals(item.GetProperty("installedRelativePath").GetString(), "demo-skill", StringComparison.OrdinalIgnoreCase));
        Assert.NotEmpty(savedAlphaState.GetProperty("baselineFiles").EnumerateArray());
        Assert.NotEmpty(savedAlphaState.GetProperty("sourceBaselineFiles").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, savedAlphaState.GetProperty("lastAppliedReference").ValueKind);
        Assert.Equal(JsonValueKind.Null, savedAlphaState.GetProperty("lastBackupPath").ValueKind);
    }

    [Fact]
    public async Task SaveSkillBindingsAsync_Retained_Content_Donor_Refreshes_Lineage_From_Metadata_Donor()
    {
        using var scope = new TestHubRootScope();
        const string newProfile = "alpha";
        var backendSkillDirectory = Path.Combine(GetProfileSkillsRoot(scope.RootPath, WorkspaceProfiles.BackendId), "demo-skill");
        Directory.CreateDirectory(backendSkillDirectory);
        await File.WriteAllTextAsync(Path.Combine(backendSkillDirectory, "SKILL.md"), "backend-version");

        var metadataSyncAt = DateTimeOffset.UtcNow.AddHours(-6);
        var metadataCheckedAt = DateTimeOffset.UtcNow.AddHours(-2);

        Directory.CreateDirectory(Path.GetDirectoryName(GetSkillInstallsPath(scope.RootPath))!);
        await File.WriteAllTextAsync(
            GetSkillInstallsPath(scope.RootPath),
            JsonSerializer.Serialize(
                new
                {
                    installs = new object[]
                    {
                        new SkillInstallRecord
                        {
                            Name = "demo-skill",
                            Profile = WorkspaceProfiles.GlobalId,
                            InstalledRelativePath = "demo-skill",
                            SourceLocalName = "global-source",
                            SourceProfile = WorkspaceProfiles.GlobalId,
                            SourceSkillPath = "catalog/global-demo-skill",
                            CustomizationMode = SkillCustomizationMode.Managed
                        },
                        new SkillInstallRecord
                        {
                            Name = "demo-skill",
                            Profile = WorkspaceProfiles.BackendId,
                            InstalledRelativePath = "demo-skill",
                            SourceLocalName = "backend-source",
                            SourceProfile = WorkspaceProfiles.BackendId,
                            SourceSkillPath = "catalog/backend-demo-skill",
                            CustomizationMode = SkillCustomizationMode.Managed
                        }
                    }
                },
                new JsonSerializerOptions
                {
                    WriteIndented = true
                }));
        await File.WriteAllTextAsync(
            GetSkillStatesPath(scope.RootPath),
            JsonSerializer.Serialize(
                new
                {
                    states = new object[]
                    {
                        new SkillInstallStateRecord
                        {
                            Profile = WorkspaceProfiles.GlobalId,
                            InstalledRelativePath = "demo-skill",
                            BaselineFiles = new List<SkillFileFingerprintRecord>(),
                            SourceBaselineFiles = new List<SkillFileFingerprintRecord>
                            {
                                new()
                                {
                                    RelativePath = "SKILL.md",
                                    Sha256 = "GLOBAL-SOURCE-BASELINE",
                                    Size = 21
                                }
                            },
                            LastSyncAt = metadataSyncAt,
                            LastCheckedAt = metadataCheckedAt,
                            LastAppliedReference = "global-ref",
                            LastBackupPath = "backups/global/demo-skill"
                        },
                        new SkillInstallStateRecord
                        {
                            Profile = WorkspaceProfiles.BackendId,
                            InstalledRelativePath = "demo-skill",
                            BaselineFiles = new List<SkillFileFingerprintRecord>(),
                            SourceBaselineFiles = new List<SkillFileFingerprintRecord>
                            {
                                new()
                                {
                                    RelativePath = "SKILL.md",
                                    Sha256 = "BACKEND-STALE-SOURCE-BASELINE",
                                    Size = 27
                                }
                            },
                            LastSyncAt = metadataSyncAt.AddHours(1),
                            LastCheckedAt = metadataCheckedAt.AddHours(1),
                            LastAppliedReference = "backend-ref",
                            LastBackupPath = "backups/backend/demo-skill"
                        }
                    }
                },
                new JsonSerializerOptions
                {
                    WriteIndented = true
                }));

        var service = CreateService(scope.RootPath);

        var preview = await service.PreviewSkillBindingResolutionAsync(
            WorkspaceProfiles.GlobalId,
            "demo-skill",
            new[] { WorkspaceProfiles.BackendId, newProfile });

        Assert.Equal(BindingResolutionStatus.Resolved, preview.ResolutionStatus);
        Assert.Equal(WorkspaceProfiles.BackendId, preview.ContentDonorProfileId);
        Assert.Equal(WorkspaceProfiles.GlobalId, preview.MetadataDonorProfileId);

        var result = await service.SaveSkillBindingsAsync(
            WorkspaceProfiles.GlobalId,
            "demo-skill",
            new[] { WorkspaceProfiles.BackendId, newProfile });

        Assert.True(result.Success, result.Details);

        using var installsDocument = JsonDocument.Parse(await File.ReadAllTextAsync(GetSkillInstallsPath(scope.RootPath)));
        var savedBackendInstall = installsDocument.RootElement.GetProperty("installs")
            .EnumerateArray()
            .Single(item =>
                string.Equals(item.GetProperty("profile").GetString(), WorkspaceProfiles.BackendId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(item.GetProperty("installedRelativePath").GetString(), "demo-skill", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("global-source", savedBackendInstall.GetProperty("sourceLocalName").GetString());
        Assert.Equal(WorkspaceProfiles.GlobalId, savedBackendInstall.GetProperty("sourceProfile").GetString());
        Assert.Equal("catalog/global-demo-skill", savedBackendInstall.GetProperty("sourceSkillPath").GetString());

        using var statesDocument = JsonDocument.Parse(await File.ReadAllTextAsync(GetSkillStatesPath(scope.RootPath)));
        var savedBackendState = statesDocument.RootElement.GetProperty("states")
            .EnumerateArray()
            .Single(item =>
                string.Equals(item.GetProperty("profile").GetString(), WorkspaceProfiles.BackendId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(item.GetProperty("installedRelativePath").GetString(), "demo-skill", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            savedBackendState.GetProperty("sourceBaselineFiles").EnumerateArray().Select(item => item.GetProperty("sha256").GetString()),
            value => string.Equals(value, "GLOBAL-SOURCE-BASELINE", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(
            savedBackendState.GetProperty("sourceBaselineFiles").EnumerateArray().Select(item => item.GetProperty("sha256").GetString()),
            value => string.Equals(value, "BACKEND-STALE-SOURCE-BASELINE", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(metadataSyncAt.ToUniversalTime().ToString("O"), savedBackendState.GetProperty("lastSyncAt").GetDateTimeOffset().ToUniversalTime().ToString("O"));
        Assert.Equal(metadataCheckedAt.ToUniversalTime().ToString("O"), savedBackendState.GetProperty("lastCheckedAt").GetDateTimeOffset().ToUniversalTime().ToString("O"));
        Assert.Equal("global-ref", savedBackendState.GetProperty("lastAppliedReference").GetString());
        Assert.Equal("backups/global/demo-skill", savedBackendState.GetProperty("lastBackupPath").GetString());
    }

    [Fact]
    public async Task SaveSkillBindingsAsync_Uses_Source_Content_When_Target_State_Is_Missing()
    {
        using var scope = new TestHubRootScope();
        var globalSkillDirectory = Path.Combine(GetProfileSkillsRoot(scope.RootPath, WorkspaceProfiles.GlobalId), "demo-skill");
        var backendSkillDirectory = Path.Combine(GetProfileSkillsRoot(scope.RootPath, WorkspaceProfiles.BackendId), "demo-skill");
        Directory.CreateDirectory(globalSkillDirectory);
        Directory.CreateDirectory(backendSkillDirectory);
        await File.WriteAllTextAsync(Path.Combine(globalSkillDirectory, "SKILL.md"), "stale-global-version");
        await File.WriteAllTextAsync(Path.Combine(backendSkillDirectory, "SKILL.md"), "backend-version");

        var service = CreateService(scope.RootPath);
        await service.SaveInstallAsync(new SkillInstallRecord
        {
            Name = "demo-skill",
            Profile = WorkspaceProfiles.BackendId,
            InstalledRelativePath = "demo-skill",
            CustomizationMode = SkillCustomizationMode.Local
        });
        await service.CaptureBaselineAsync(WorkspaceProfiles.BackendId, "demo-skill");

        var result = await service.SaveSkillBindingsAsync(
            WorkspaceProfiles.BackendId,
            "demo-skill",
            new[] { WorkspaceProfiles.GlobalId });

        Assert.True(result.Success, result.Details);
        Assert.False(Directory.Exists(backendSkillDirectory));
        Assert.Equal("backend-version", await File.ReadAllTextAsync(Path.Combine(globalSkillDirectory, "SKILL.md")));

        var snapshot = await service.LoadAsync();
        var skill = Assert.Single(snapshot.InstalledSkills.Where(item => item.RelativePath == "demo-skill"));
        Assert.Equal(new[] { WorkspaceProfiles.GlobalId }, skill.BindingProfileIds.OrderBy(SortProfileId).ToArray());
        Assert.True(skill.IsRegistered);
        Assert.True(skill.HasBaseline);
        Assert.False(skill.IsDirty);
    }

    [Fact]
    public async Task SaveSkillBindingsAsync_Restores_Retained_Source_Copy_When_Source_Directory_Is_Missing()
    {
        using var scope = new TestHubRootScope();
        var librarySkillDirectory = Path.Combine(GetSkillLibraryRoot(scope.RootPath), "demo-skill");
        var backendSkillDirectory = Path.Combine(GetProfileSkillsRoot(scope.RootPath, WorkspaceProfiles.BackendId), "demo-skill");
        Directory.CreateDirectory(librarySkillDirectory);
        Directory.CreateDirectory(backendSkillDirectory);
        await File.WriteAllTextAsync(Path.Combine(librarySkillDirectory, "SKILL.md"), "library-version");
        await File.WriteAllTextAsync(Path.Combine(backendSkillDirectory, "SKILL.md"), "backend-version");

        var service = CreateService(scope.RootPath);
        await service.SaveInstallAsync(new SkillInstallRecord
        {
            Name = "demo-skill",
            Profile = WorkspaceProfiles.BackendId,
            InstalledRelativePath = "demo-skill",
            CustomizationMode = SkillCustomizationMode.Local
        });
        await service.CaptureBaselineAsync(WorkspaceProfiles.BackendId, "demo-skill");
        Directory.Delete(backendSkillDirectory, recursive: true);

        var result = await service.SaveSkillBindingsAsync(
            WorkspaceProfiles.BackendId,
            "demo-skill",
            new[] { WorkspaceProfiles.BackendId });

        Assert.True(result.Success, result.Details);
        Assert.True(File.Exists(Path.Combine(backendSkillDirectory, "SKILL.md")));
        Assert.Equal("library-version", await File.ReadAllTextAsync(Path.Combine(backendSkillDirectory, "SKILL.md")));

        var snapshot = await service.LoadAsync();
        var skill = Assert.Single(snapshot.InstalledSkills.Where(item => item.RelativePath == "demo-skill"));
        Assert.Equal(new[] { WorkspaceProfiles.BackendId }, skill.BindingProfileIds);
        Assert.True(skill.IsRegistered);
    }

    [Fact]
    public async Task SaveSkillBindingsAsync_Preserves_Retained_Profile_Metadata_When_Restoring_Missing_Directory()
    {
        using var scope = new TestHubRootScope();
        var librarySkillDirectory = Path.Combine(GetSkillLibraryRoot(scope.RootPath), "demo-skill");
        var backendSkillDirectory = Path.Combine(GetProfileSkillsRoot(scope.RootPath, WorkspaceProfiles.BackendId), "demo-skill");
        Directory.CreateDirectory(librarySkillDirectory);
        Directory.CreateDirectory(backendSkillDirectory);
        await File.WriteAllTextAsync(Path.Combine(librarySkillDirectory, "SKILL.md"), "library-version");
        await File.WriteAllTextAsync(Path.Combine(backendSkillDirectory, "SKILL.md"), "backend-version");

        var expectedLastSyncAt = DateTimeOffset.UtcNow.AddHours(-4);
        var expectedLastCheckedAt = DateTimeOffset.UtcNow.AddHours(-1);
        var expectedReference = "refs/tags/v2.0.0";
        var expectedBackupPath = "backups/skills/backend/demo-skill/20260321-010101-sync";

        Directory.CreateDirectory(Path.GetDirectoryName(GetSkillInstallsPath(scope.RootPath))!);
        await File.WriteAllTextAsync(
            GetSkillInstallsPath(scope.RootPath),
            JsonSerializer.Serialize(
                new
                {
                    installs = new object[]
                    {
                        new SkillInstallRecord
                        {
                            Name = "demo-skill",
                            Profile = WorkspaceProfiles.BackendId,
                            InstalledRelativePath = "demo-skill",
                            SourceLocalName = "shared-source",
                            SourceProfile = WorkspaceProfiles.FrontendId,
                            SourceSkillPath = "catalog/demo-skill",
                            CustomizationMode = SkillCustomizationMode.Managed
                        }
                    }
                },
                new JsonSerializerOptions
                {
                    WriteIndented = true
                }));

        await File.WriteAllTextAsync(
            GetSkillStatesPath(scope.RootPath),
            JsonSerializer.Serialize(
                new
                {
                    states = new object[]
                    {
                        new SkillInstallStateRecord
                        {
                            Profile = WorkspaceProfiles.BackendId,
                            InstalledRelativePath = "demo-skill",
                            BaselineCapturedAt = DateTimeOffset.UtcNow.AddDays(-2),
                            BaselineFiles = new List<SkillFileFingerprintRecord>(),
                            SourceBaselineFiles = new List<SkillFileFingerprintRecord>
                            {
                                new()
                                {
                                    RelativePath = "SKILL.md",
                                    Sha256 = "ABCDEF",
                                    Size = 12
                                }
                            },
                            OverlayDeletedFiles = new List<string> { "notes.md" },
                            LastSyncAt = expectedLastSyncAt,
                            LastCheckedAt = expectedLastCheckedAt,
                            LastAppliedReference = expectedReference,
                            LastBackupPath = expectedBackupPath
                        }
                    }
                },
                new JsonSerializerOptions
                {
                    WriteIndented = true
                }));

        var service = CreateService(scope.RootPath);
        Directory.Delete(backendSkillDirectory, recursive: true);

        var result = await service.SaveSkillBindingsAsync(
            WorkspaceProfiles.BackendId,
            "demo-skill",
            new[] { WorkspaceProfiles.BackendId });

        Assert.True(result.Success, result.Details);

        var snapshot = await service.LoadAsync();
        var skill = Assert.Single(snapshot.InstalledSkills.Where(item => item.RelativePath == "demo-skill"));
        Assert.Equal(SkillCustomizationMode.Managed, skill.CustomizationMode);
        Assert.Equal("shared-source", skill.SourceLocalName);
        Assert.Equal(WorkspaceProfiles.FrontendId, skill.SourceProfile);
        Assert.Equal("catalog/demo-skill", skill.SourceSkillPath);
        Assert.True(skill.HasBaseline);
        Assert.False(skill.IsDirty);
        Assert.Contains(expectedLastSyncAt.ToLocalTime().ToString("yyyy-MM-dd"), skill.LastSyncDisplay, StringComparison.Ordinal);

        using var statesDocument = JsonDocument.Parse(await File.ReadAllTextAsync(GetSkillStatesPath(scope.RootPath)));
        var savedState = statesDocument.RootElement.GetProperty("states")
            .EnumerateArray()
            .Single(item =>
                string.Equals(item.GetProperty("profile").GetString(), WorkspaceProfiles.BackendId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(item.GetProperty("installedRelativePath").GetString(), "demo-skill", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(expectedReference, savedState.GetProperty("lastAppliedReference").GetString());
        Assert.Equal(expectedBackupPath, savedState.GetProperty("lastBackupPath").GetString());
        Assert.Equal(expectedLastSyncAt.ToUniversalTime().ToString("O"), savedState.GetProperty("lastSyncAt").GetDateTimeOffset().ToUniversalTime().ToString("O"));
        Assert.Equal(expectedLastCheckedAt.ToUniversalTime().ToString("O"), savedState.GetProperty("lastCheckedAt").GetDateTimeOffset().ToUniversalTime().ToString("O"));
        Assert.NotEmpty(savedState.GetProperty("baselineFiles").EnumerateArray());
    }

    [Fact]
    public async Task SaveSkillBindingsAsync_Recaptures_Clean_Baseline_For_New_Target_From_Written_Files()
    {
        using var scope = new TestHubRootScope();
        var sourceSkillDirectory = Path.Combine(GetProfileSkillsRoot(scope.RootPath, WorkspaceProfiles.GlobalId), "demo-skill");
        Directory.CreateDirectory(sourceSkillDirectory);
        await File.WriteAllTextAsync(Path.Combine(sourceSkillDirectory, "SKILL.md"), "current-skill");

        var staleBaselineCapturedAt = DateTimeOffset.UtcNow.AddDays(-7);
        Directory.CreateDirectory(Path.GetDirectoryName(GetSkillInstallsPath(scope.RootPath))!);
        await File.WriteAllTextAsync(
            GetSkillInstallsPath(scope.RootPath),
            JsonSerializer.Serialize(
                new
                {
                    installs = new object[]
                    {
                        new SkillInstallRecord
                        {
                            Name = "demo-skill",
                            Profile = WorkspaceProfiles.GlobalId,
                            InstalledRelativePath = "demo-skill",
                            CustomizationMode = SkillCustomizationMode.Local
                        }
                    }
                },
                new JsonSerializerOptions
                {
                    WriteIndented = true
                }));
        await File.WriteAllTextAsync(
            GetSkillStatesPath(scope.RootPath),
            JsonSerializer.Serialize(
                new
                {
                    states = new object[]
                    {
                        new SkillInstallStateRecord
                        {
                            Profile = WorkspaceProfiles.GlobalId,
                            InstalledRelativePath = "demo-skill",
                            BaselineCapturedAt = staleBaselineCapturedAt,
                            BaselineFiles = new List<SkillFileFingerprintRecord>
                            {
                                new()
                                {
                                    RelativePath = "SKILL.md",
                                    Sha256 = "STALE-BASELINE",
                                    Size = 1
                                }
                            },
                            SourceBaselineFiles = new List<SkillFileFingerprintRecord>()
                        }
                    }
                },
                new JsonSerializerOptions
                {
                    WriteIndented = true
                }));

        var service = CreateService(scope.RootPath);

        var result = await service.SaveSkillBindingsAsync(
            WorkspaceProfiles.GlobalId,
            "demo-skill",
            new[] { WorkspaceProfiles.FrontendId });

        Assert.True(result.Success, result.Details);

        var snapshot = await service.LoadAsync();
        var skill = Assert.Single(snapshot.InstalledSkills.Where(item =>
            string.Equals(item.Profile, WorkspaceProfiles.FrontendId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(item.RelativePath, "demo-skill", StringComparison.OrdinalIgnoreCase)));
        Assert.True(skill.HasBaseline);
        Assert.False(skill.IsDirty);

        using var statesDocument = JsonDocument.Parse(await File.ReadAllTextAsync(GetSkillStatesPath(scope.RootPath)));
        var savedState = statesDocument.RootElement.GetProperty("states")
            .EnumerateArray()
            .Single(item =>
                string.Equals(item.GetProperty("profile").GetString(), WorkspaceProfiles.FrontendId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(item.GetProperty("installedRelativePath").GetString(), "demo-skill", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(
            savedState.GetProperty("baselineFiles").EnumerateArray().Select(item => item.GetProperty("sha256").GetString()),
            value => string.Equals(value, "STALE-BASELINE", StringComparison.OrdinalIgnoreCase));
        Assert.True(savedState.GetProperty("baselineCapturedAt").GetDateTimeOffset() > staleBaselineCapturedAt);
    }

    [Fact]
    public async Task SaveSkillBindingsAsync_Fills_Missing_Source_Metadata_From_Donor_Without_Overwriting_Existing_Mode()
    {
        using var scope = new TestHubRootScope();
        var librarySkillDirectory = Path.Combine(GetSkillLibraryRoot(scope.RootPath), "demo-skill");
        var backendSkillDirectory = Path.Combine(GetProfileSkillsRoot(scope.RootPath, WorkspaceProfiles.BackendId), "demo-skill");
        Directory.CreateDirectory(librarySkillDirectory);
        Directory.CreateDirectory(backendSkillDirectory);
        await File.WriteAllTextAsync(Path.Combine(librarySkillDirectory, "SKILL.md"), "library-version");
        await File.WriteAllTextAsync(Path.Combine(backendSkillDirectory, "SKILL.md"), "backend-version");

        Directory.CreateDirectory(Path.GetDirectoryName(GetSkillInstallsPath(scope.RootPath))!);
        await File.WriteAllTextAsync(
            GetSkillInstallsPath(scope.RootPath),
            JsonSerializer.Serialize(
                new
                {
                    installs = new object[]
                    {
                        new SkillInstallRecord
                        {
                            Name = "demo-skill",
                            Profile = "library",
                            InstalledRelativePath = "demo-skill",
                            SourceLocalName = "shared-source",
                            SourceProfile = WorkspaceProfiles.FrontendId,
                            SourceSkillPath = "catalog/demo-skill",
                            CustomizationMode = SkillCustomizationMode.Managed
                        },
                        new SkillInstallRecord
                        {
                            Name = "demo-skill",
                            Profile = WorkspaceProfiles.BackendId,
                            InstalledRelativePath = "demo-skill",
                            SourceLocalName = null,
                            SourceProfile = null,
                            SourceSkillPath = null,
                            CustomizationMode = SkillCustomizationMode.Managed
                        }
                    }
                },
                new JsonSerializerOptions
                {
                    WriteIndented = true
                }));
        await File.WriteAllTextAsync(
            GetSkillStatesPath(scope.RootPath),
            JsonSerializer.Serialize(
                new
                {
                    states = new object[]
                    {
                        new SkillInstallStateRecord
                        {
                            Profile = WorkspaceProfiles.BackendId,
                            InstalledRelativePath = "demo-skill",
                            BaselineCapturedAt = DateTimeOffset.UtcNow.AddDays(-1),
                            BaselineFiles = new List<SkillFileFingerprintRecord>()
                        }
                    }
                },
                new JsonSerializerOptions
                {
                    WriteIndented = true
                }));

        var service = CreateService(scope.RootPath);
        Directory.Delete(backendSkillDirectory, recursive: true);

        var result = await service.SaveSkillBindingsAsync(
            WorkspaceProfiles.BackendId,
            "demo-skill",
            new[] { WorkspaceProfiles.BackendId });

        Assert.True(result.Success, result.Details);

        using var installsDocument = JsonDocument.Parse(await File.ReadAllTextAsync(GetSkillInstallsPath(scope.RootPath)));
        var savedInstall = installsDocument.RootElement.GetProperty("installs")
            .EnumerateArray()
            .Single(item =>
                string.Equals(item.GetProperty("profile").GetString(), WorkspaceProfiles.BackendId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(item.GetProperty("installedRelativePath").GetString(), "demo-skill", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("managed", savedInstall.GetProperty("customizationMode").GetString());
        Assert.Equal("shared-source", savedInstall.GetProperty("sourceLocalName").GetString());
        Assert.Equal(WorkspaceProfiles.FrontendId, savedInstall.GetProperty("sourceProfile").GetString());
        Assert.Equal("catalog/demo-skill", savedInstall.GetProperty("sourceSkillPath").GetString());
    }

    [Fact]
    public async Task SaveSkillBindingsAsync_Does_Not_Inherit_Donor_Collections_When_Retained_State_Collections_Are_Empty()
    {
        using var scope = new TestHubRootScope();
        var librarySkillDirectory = Path.Combine(GetSkillLibraryRoot(scope.RootPath), "demo-skill");
        var backendSkillDirectory = Path.Combine(GetProfileSkillsRoot(scope.RootPath, WorkspaceProfiles.BackendId), "demo-skill");
        Directory.CreateDirectory(librarySkillDirectory);
        Directory.CreateDirectory(backendSkillDirectory);
        await File.WriteAllTextAsync(Path.Combine(librarySkillDirectory, "SKILL.md"), "library-version");
        await File.WriteAllTextAsync(Path.Combine(backendSkillDirectory, "SKILL.md"), "backend-version");

        Directory.CreateDirectory(Path.GetDirectoryName(GetSkillInstallsPath(scope.RootPath))!);
        await File.WriteAllTextAsync(
            GetSkillInstallsPath(scope.RootPath),
            JsonSerializer.Serialize(
                new
                {
                    installs = new object[]
                    {
                        new SkillInstallRecord
                        {
                            Name = "demo-skill",
                            Profile = WorkspaceProfiles.BackendId,
                            InstalledRelativePath = "demo-skill",
                            CustomizationMode = SkillCustomizationMode.Managed,
                            SourceLocalName = "shared-source",
                            SourceProfile = WorkspaceProfiles.FrontendId,
                            SourceSkillPath = "catalog/demo-skill"
                        }
                    }
                },
                new JsonSerializerOptions
                {
                    WriteIndented = true
                }));
        await File.WriteAllTextAsync(
            GetSkillStatesPath(scope.RootPath),
            JsonSerializer.Serialize(
                new
                {
                    states = new object[]
                    {
                        new SkillInstallStateRecord
                        {
                            Profile = "library",
                            InstalledRelativePath = "demo-skill",
                            BaselineCapturedAt = DateTimeOffset.UtcNow.AddDays(-3),
                            BaselineFiles = new List<SkillFileFingerprintRecord>(),
                            SourceBaselineFiles = new List<SkillFileFingerprintRecord>
                            {
                                new()
                                {
                                    RelativePath = "SKILL.md",
                                    Sha256 = "LIB123",
                                    Size = 7
                                }
                            },
                            OverlayDeletedFiles = new List<string> { "obsolete.md" }
                        },
                        new SkillInstallStateRecord
                        {
                            Profile = WorkspaceProfiles.BackendId,
                            InstalledRelativePath = "demo-skill",
                            BaselineCapturedAt = DateTimeOffset.UtcNow.AddDays(-1),
                            BaselineFiles = new List<SkillFileFingerprintRecord>(),
                            SourceBaselineFiles = new List<SkillFileFingerprintRecord>(),
                            OverlayDeletedFiles = new List<string>()
                        }
                    }
                },
                new JsonSerializerOptions
                {
                    WriteIndented = true
                }));

        var service = CreateService(scope.RootPath);
        Directory.Delete(backendSkillDirectory, recursive: true);

        var result = await service.SaveSkillBindingsAsync(
            WorkspaceProfiles.BackendId,
            "demo-skill",
            new[] { WorkspaceProfiles.BackendId });

        Assert.True(result.Success, result.Details);

        using var statesDocument = JsonDocument.Parse(await File.ReadAllTextAsync(GetSkillStatesPath(scope.RootPath)));
        var savedState = statesDocument.RootElement.GetProperty("states")
            .EnumerateArray()
            .Single(item =>
                string.Equals(item.GetProperty("profile").GetString(), WorkspaceProfiles.BackendId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(item.GetProperty("installedRelativePath").GetString(), "demo-skill", StringComparison.OrdinalIgnoreCase));
        var sourceBaselineHashes = savedState.GetProperty("sourceBaselineFiles")
            .EnumerateArray()
            .Select(item => item.GetProperty("sha256").GetString())
            .ToArray();
        Assert.NotEmpty(sourceBaselineHashes);
        Assert.DoesNotContain(sourceBaselineHashes, value => string.Equals(value, "LIB123", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(savedState.GetProperty("overlayDeletedFiles").EnumerateArray());
    }

    [Fact]
    public async Task SaveSkillBindingsAsync_Removes_Stale_Metadata_When_Source_Profile_Has_No_Directory()
    {
        using var scope = new TestHubRootScope();
        var globalSkillDirectory = Path.Combine(GetProfileSkillsRoot(scope.RootPath, WorkspaceProfiles.GlobalId), "demo-skill");
        Directory.CreateDirectory(globalSkillDirectory);
        await File.WriteAllTextAsync(Path.Combine(globalSkillDirectory, "SKILL.md"), "global-version");

        Directory.CreateDirectory(Path.GetDirectoryName(GetSkillInstallsPath(scope.RootPath))!);
        await File.WriteAllTextAsync(
            GetSkillInstallsPath(scope.RootPath),
            JsonSerializer.Serialize(
                new
                {
                    installs = new object[]
                    {
                        new SkillInstallRecord
                        {
                            Name = "demo-skill",
                            Profile = WorkspaceProfiles.GlobalId,
                            InstalledRelativePath = "demo-skill",
                            CustomizationMode = SkillCustomizationMode.Local
                        },
                        new SkillInstallRecord
                        {
                            Name = "demo-skill",
                            Profile = WorkspaceProfiles.BackendId,
                            InstalledRelativePath = "demo-skill",
                            CustomizationMode = SkillCustomizationMode.Managed,
                            SourceLocalName = "ghost-source"
                        }
                    }
                },
                new JsonSerializerOptions
                {
                    WriteIndented = true
                }));
        await File.WriteAllTextAsync(
            GetSkillStatesPath(scope.RootPath),
            JsonSerializer.Serialize(
                new
                {
                    states = new object[]
                    {
                        new SkillInstallStateRecord
                        {
                            Profile = WorkspaceProfiles.BackendId,
                            InstalledRelativePath = "demo-skill",
                            BaselineCapturedAt = DateTimeOffset.UtcNow.AddDays(-1),
                            BaselineFiles = new List<SkillFileFingerprintRecord>()
                        }
                    }
                },
                new JsonSerializerOptions
                {
                    WriteIndented = true
                }));

        var service = CreateService(scope.RootPath);

        var result = await service.SaveSkillBindingsAsync(
            WorkspaceProfiles.BackendId,
            "demo-skill",
            new[] { WorkspaceProfiles.GlobalId });

        Assert.True(result.Success, result.Details);

        var snapshot = await service.LoadAsync();
        var skill = Assert.Single(snapshot.InstalledSkills.Where(item => item.RelativePath == "demo-skill"));
        Assert.Equal(new[] { WorkspaceProfiles.GlobalId }, skill.BindingProfileIds);
        Assert.DoesNotContain(snapshot.InstalledSkills, item =>
            string.Equals(item.Profile, WorkspaceProfiles.BackendId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(item.RelativePath, "demo-skill", StringComparison.OrdinalIgnoreCase));

        using var installsDocument = JsonDocument.Parse(await File.ReadAllTextAsync(GetSkillInstallsPath(scope.RootPath)));
        Assert.DoesNotContain(
            installsDocument.RootElement.GetProperty("installs").EnumerateArray(),
            item => string.Equals(item.GetProperty("profile").GetString(), WorkspaceProfiles.BackendId, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task SaveSkillGroupBindingsAsync_Replicates_Repository_Folder()
    {
        using var scope = new TestHubRootScope();
        var repoRoot = Path.Combine(GetProfileSkillsRoot(scope.RootPath, WorkspaceProfiles.GlobalId), "superpowers");
        var brainstormingDirectory = Path.Combine(repoRoot, "brainstorming");
        var dispatchingDirectory = Path.Combine(repoRoot, "dispatching-parallel-agents");
        Directory.CreateDirectory(brainstormingDirectory);
        Directory.CreateDirectory(dispatchingDirectory);
        await File.WriteAllTextAsync(Path.Combine(repoRoot, "README.md"), "repo-root");
        await File.WriteAllTextAsync(Path.Combine(brainstormingDirectory, "SKILL.md"), "brainstorming");
        await File.WriteAllTextAsync(Path.Combine(dispatchingDirectory, "SKILL.md"), "dispatching");

        var service = CreateService(scope.RootPath);
        await service.SaveInstallAsync(new SkillInstallRecord
        {
            Name = "brainstorming",
            Profile = WorkspaceProfiles.GlobalId,
            InstalledRelativePath = "superpowers/brainstorming",
            CustomizationMode = SkillCustomizationMode.Local
        });
        await service.SaveInstallAsync(new SkillInstallRecord
        {
            Name = "dispatching-parallel-agents",
            Profile = WorkspaceProfiles.GlobalId,
            InstalledRelativePath = "superpowers/dispatching-parallel-agents",
            CustomizationMode = SkillCustomizationMode.Local
        });

        var result = await service.SaveSkillGroupBindingsAsync(
            WorkspaceProfiles.GlobalId,
            "superpowers",
            new[] { WorkspaceProfiles.GlobalId, WorkspaceProfiles.BackendId });

        Assert.True(result.Success, result.Details);
        Assert.True(File.Exists(Path.Combine(GetProfileSkillsRoot(scope.RootPath, WorkspaceProfiles.BackendId), "superpowers", "README.md")));
        Assert.True(File.Exists(Path.Combine(GetProfileSkillsRoot(scope.RootPath, WorkspaceProfiles.BackendId), "superpowers", "brainstorming", "SKILL.md")));
        Assert.True(File.Exists(Path.Combine(GetProfileSkillsRoot(scope.RootPath, WorkspaceProfiles.BackendId), "superpowers", "dispatching-parallel-agents", "SKILL.md")));

        var snapshot = await service.LoadAsync();
        Assert.Contains(snapshot.InstalledSkills, item =>
            item.RelativePath == "superpowers/brainstorming"
            && item.BindingProfileIds.Contains(WorkspaceProfiles.BackendId, StringComparer.OrdinalIgnoreCase));
        Assert.Contains(snapshot.InstalledSkills, item =>
            item.RelativePath == "superpowers/dispatching-parallel-agents"
            && item.BindingProfileIds.Contains(WorkspaceProfiles.BackendId, StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task SaveSkillGroupBindingsAsync_Removes_Source_Profile_Group_When_Targets_Remain()
    {
        using var scope = new TestHubRootScope();
        var globalRepoRoot = Path.Combine(GetProfileSkillsRoot(scope.RootPath, WorkspaceProfiles.GlobalId), "superpowers");
        var frontendRepoRoot = Path.Combine(GetProfileSkillsRoot(scope.RootPath, WorkspaceProfiles.FrontendId), "superpowers");
        Directory.CreateDirectory(Path.Combine(globalRepoRoot, "brainstorming"));
        Directory.CreateDirectory(Path.Combine(globalRepoRoot, "dispatching-parallel-agents"));
        Directory.CreateDirectory(Path.Combine(frontendRepoRoot, "brainstorming"));
        Directory.CreateDirectory(Path.Combine(frontendRepoRoot, "dispatching-parallel-agents"));
        await File.WriteAllTextAsync(Path.Combine(globalRepoRoot, "README.md"), "global-root");
        await File.WriteAllTextAsync(Path.Combine(globalRepoRoot, "brainstorming", "SKILL.md"), "global-brainstorming");
        await File.WriteAllTextAsync(Path.Combine(globalRepoRoot, "dispatching-parallel-agents", "SKILL.md"), "global-dispatching");
        await File.WriteAllTextAsync(Path.Combine(frontendRepoRoot, "README.md"), "frontend-root");
        await File.WriteAllTextAsync(Path.Combine(frontendRepoRoot, "brainstorming", "SKILL.md"), "frontend-brainstorming");
        await File.WriteAllTextAsync(Path.Combine(frontendRepoRoot, "dispatching-parallel-agents", "SKILL.md"), "frontend-dispatching");

        var service = CreateService(scope.RootPath);
        await service.SaveInstallAsync(new SkillInstallRecord
        {
            Name = "brainstorming",
            Profile = WorkspaceProfiles.GlobalId,
            InstalledRelativePath = "superpowers/brainstorming",
            CustomizationMode = SkillCustomizationMode.Local
        });
        await service.SaveInstallAsync(new SkillInstallRecord
        {
            Name = "dispatching-parallel-agents",
            Profile = WorkspaceProfiles.GlobalId,
            InstalledRelativePath = "superpowers/dispatching-parallel-agents",
            CustomizationMode = SkillCustomizationMode.Local
        });
        await service.SaveInstallAsync(new SkillInstallRecord
        {
            Name = "brainstorming",
            Profile = WorkspaceProfiles.FrontendId,
            InstalledRelativePath = "superpowers/brainstorming",
            CustomizationMode = SkillCustomizationMode.Local
        });
        await service.SaveInstallAsync(new SkillInstallRecord
        {
            Name = "dispatching-parallel-agents",
            Profile = WorkspaceProfiles.FrontendId,
            InstalledRelativePath = "superpowers/dispatching-parallel-agents",
            CustomizationMode = SkillCustomizationMode.Local
        });

        var result = await service.SaveSkillGroupBindingsAsync(
            WorkspaceProfiles.FrontendId,
            "superpowers",
            new[] { WorkspaceProfiles.GlobalId });

        Assert.True(result.Success, result.Details);
        Assert.False(Directory.Exists(frontendRepoRoot));

        var snapshot = await service.LoadAsync();
        Assert.Contains(snapshot.InstalledSkills, item =>
            item.RelativePath == "superpowers/brainstorming"
            && item.BindingProfileIds.SequenceEqual(new[] { WorkspaceProfiles.GlobalId }, StringComparer.OrdinalIgnoreCase));
        Assert.Contains(snapshot.InstalledSkills, item =>
            item.RelativePath == "superpowers/dispatching-parallel-agents"
            && item.BindingProfileIds.SequenceEqual(new[] { WorkspaceProfiles.GlobalId }, StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task SaveSkillGroupBindingsAsync_Prefers_Source_Group_When_It_Is_Still_Usable()
    {
        using var scope = new TestHubRootScope();
        const string partialProfile = "alpha";
        const string completeProfile = "zeta";

        var backendRepoRoot = Path.Combine(GetProfileSkillsRoot(scope.RootPath, WorkspaceProfiles.BackendId), "superpowers");
        var partialRepoRoot = Path.Combine(GetProfileSkillsRoot(scope.RootPath, partialProfile), "superpowers");
        var completeRepoRoot = Path.Combine(GetProfileSkillsRoot(scope.RootPath, completeProfile), "superpowers");
        Directory.CreateDirectory(Path.Combine(backendRepoRoot, "brainstorming"));
        Directory.CreateDirectory(Path.Combine(backendRepoRoot, "dispatching-parallel-agents"));
        Directory.CreateDirectory(Path.Combine(partialRepoRoot, "brainstorming"));
        Directory.CreateDirectory(Path.Combine(completeRepoRoot, "brainstorming"));
        Directory.CreateDirectory(Path.Combine(completeRepoRoot, "dispatching-parallel-agents"));

        await File.WriteAllTextAsync(Path.Combine(backendRepoRoot, "README.md"), "backend-root");
        await File.WriteAllTextAsync(Path.Combine(backendRepoRoot, "brainstorming", "SKILL.md"), "backend-brainstorming");
        await File.WriteAllTextAsync(Path.Combine(backendRepoRoot, "dispatching-parallel-agents", "SKILL.md"), "backend-dispatching");
        await File.WriteAllTextAsync(Path.Combine(partialRepoRoot, "README.md"), "partial-root");
        await File.WriteAllTextAsync(Path.Combine(partialRepoRoot, "brainstorming", "SKILL.md"), "partial-brainstorming");
        await File.WriteAllTextAsync(Path.Combine(completeRepoRoot, "README.md"), "complete-root");
        await File.WriteAllTextAsync(Path.Combine(completeRepoRoot, "brainstorming", "SKILL.md"), "complete-brainstorming");
        await File.WriteAllTextAsync(Path.Combine(completeRepoRoot, "dispatching-parallel-agents", "SKILL.md"), "complete-dispatching");

        var service = CreateService(scope.RootPath);
        foreach (var profile in new[] { WorkspaceProfiles.BackendId, completeProfile })
        {
            await service.SaveInstallAsync(new SkillInstallRecord
            {
                Name = "brainstorming",
                Profile = profile,
                InstalledRelativePath = "superpowers/brainstorming",
                CustomizationMode = SkillCustomizationMode.Local
            });
            await service.SaveInstallAsync(new SkillInstallRecord
            {
                Name = "dispatching-parallel-agents",
                Profile = profile,
                InstalledRelativePath = "superpowers/dispatching-parallel-agents",
                CustomizationMode = SkillCustomizationMode.Local
            });
            await service.CaptureBaselineAsync(profile, "superpowers/brainstorming");
            await service.CaptureBaselineAsync(profile, "superpowers/dispatching-parallel-agents");
        }

        await service.SaveInstallAsync(new SkillInstallRecord
        {
            Name = "brainstorming",
            Profile = partialProfile,
            InstalledRelativePath = "superpowers/brainstorming",
            CustomizationMode = SkillCustomizationMode.Local
        });
        await service.CaptureBaselineAsync(partialProfile, "superpowers/brainstorming");

        var result = await service.SaveSkillGroupBindingsAsync(
            WorkspaceProfiles.BackendId,
            "superpowers",
            new[] { partialProfile, completeProfile });

        Assert.True(result.Success, result.Details);
        Assert.False(Directory.Exists(backendRepoRoot));
        Assert.True(File.Exists(Path.Combine(partialRepoRoot, "dispatching-parallel-agents", "SKILL.md")));
        Assert.True(File.Exists(Path.Combine(completeRepoRoot, "dispatching-parallel-agents", "SKILL.md")));
        Assert.Equal("backend-dispatching", await File.ReadAllTextAsync(Path.Combine(completeRepoRoot, "dispatching-parallel-agents", "SKILL.md")));

        var snapshot = await service.LoadAsync();
        Assert.Contains(snapshot.InstalledSkills, item =>
            item.RelativePath == "superpowers/dispatching-parallel-agents"
            && item.BindingProfileIds.SequenceEqual(new[] { partialProfile, completeProfile }, StringComparer.OrdinalIgnoreCase));
        var dispatching = Assert.Single(snapshot.InstalledSkills.Where(item => item.RelativePath == "superpowers/dispatching-parallel-agents"));
        Assert.False(dispatching.IsDirty);
    }

    [Fact]
    public async Task SaveSkillGroupBindingsAsync_Falls_Back_To_Original_Source_And_Synthesizes_Missing_Metadata_When_No_Target_Is_Complete()
    {
        using var scope = new TestHubRootScope();
        const string targetProfile = "alpha";

        var backendRepoRoot = Path.Combine(GetProfileSkillsRoot(scope.RootPath, WorkspaceProfiles.BackendId), "superpowers");
        var targetRepoRoot = Path.Combine(GetProfileSkillsRoot(scope.RootPath, targetProfile), "superpowers");
        Directory.CreateDirectory(Path.Combine(backendRepoRoot, "brainstorming"));
        Directory.CreateDirectory(Path.Combine(backendRepoRoot, "dispatching-parallel-agents"));
        await File.WriteAllTextAsync(Path.Combine(backendRepoRoot, "README.md"), "backend-root");
        await File.WriteAllTextAsync(Path.Combine(backendRepoRoot, "brainstorming", "SKILL.md"), "backend-brainstorming");
        await File.WriteAllTextAsync(Path.Combine(backendRepoRoot, "dispatching-parallel-agents", "SKILL.md"), "backend-dispatching");

        var service = CreateService(scope.RootPath);
        await service.SaveInstallAsync(new SkillInstallRecord
        {
            Name = "brainstorming",
            Profile = WorkspaceProfiles.BackendId,
            InstalledRelativePath = "superpowers/brainstorming",
            CustomizationMode = SkillCustomizationMode.Local
        });
        await service.CaptureBaselineAsync(WorkspaceProfiles.BackendId, "superpowers/brainstorming");

        var result = await service.SaveSkillGroupBindingsAsync(
            WorkspaceProfiles.BackendId,
            "superpowers",
            new[] { targetProfile });

        Assert.True(result.Success, result.Details);
        Assert.False(Directory.Exists(backendRepoRoot));
        Assert.True(File.Exists(Path.Combine(targetRepoRoot, "dispatching-parallel-agents", "SKILL.md")));

        var snapshot = await service.LoadAsync();
        var brainstorming = Assert.Single(snapshot.InstalledSkills.Where(item => item.RelativePath == "superpowers/brainstorming"));
        var dispatching = Assert.Single(snapshot.InstalledSkills.Where(item => item.RelativePath == "superpowers/dispatching-parallel-agents"));
        Assert.Equal(new[] { targetProfile }, brainstorming.BindingProfileIds);
        Assert.Equal(new[] { targetProfile }, dispatching.BindingProfileIds);
        Assert.True(brainstorming.IsRegistered);
        Assert.True(brainstorming.HasBaseline);
        Assert.True(dispatching.IsRegistered);
        Assert.True(dispatching.HasBaseline);
    }

    [Fact]
    public async Task SaveSkillGroupBindingsAsync_Restores_Retained_Source_Group_When_Source_Directory_Is_Missing()
    {
        using var scope = new TestHubRootScope();
        var libraryRepoRoot = Path.Combine(GetSkillLibraryRoot(scope.RootPath), "superpowers");
        var backendRepoRoot = Path.Combine(GetProfileSkillsRoot(scope.RootPath, WorkspaceProfiles.BackendId), "superpowers");
        Directory.CreateDirectory(Path.Combine(libraryRepoRoot, "brainstorming"));
        Directory.CreateDirectory(Path.Combine(libraryRepoRoot, "dispatching-parallel-agents"));
        Directory.CreateDirectory(Path.Combine(backendRepoRoot, "brainstorming"));
        Directory.CreateDirectory(Path.Combine(backendRepoRoot, "dispatching-parallel-agents"));

        await File.WriteAllTextAsync(Path.Combine(libraryRepoRoot, "README.md"), "library-root");
        await File.WriteAllTextAsync(Path.Combine(libraryRepoRoot, "brainstorming", "SKILL.md"), "library-brainstorming");
        await File.WriteAllTextAsync(Path.Combine(libraryRepoRoot, "dispatching-parallel-agents", "SKILL.md"), "library-dispatching");
        await File.WriteAllTextAsync(Path.Combine(backendRepoRoot, "README.md"), "backend-root");
        await File.WriteAllTextAsync(Path.Combine(backendRepoRoot, "brainstorming", "SKILL.md"), "backend-brainstorming");
        await File.WriteAllTextAsync(Path.Combine(backendRepoRoot, "dispatching-parallel-agents", "SKILL.md"), "backend-dispatching");

        var service = CreateService(scope.RootPath);
        foreach (var relativeSkillPath in new[] { "superpowers/brainstorming", "superpowers/dispatching-parallel-agents" })
        {
            await service.SaveInstallAsync(new SkillInstallRecord
            {
                Name = Path.GetFileName(relativeSkillPath),
                Profile = WorkspaceProfiles.BackendId,
                InstalledRelativePath = relativeSkillPath,
                CustomizationMode = SkillCustomizationMode.Local
            });
            await service.CaptureBaselineAsync(WorkspaceProfiles.BackendId, relativeSkillPath);
        }

        Directory.Delete(backendRepoRoot, recursive: true);

        var result = await service.SaveSkillGroupBindingsAsync(
            WorkspaceProfiles.BackendId,
            "superpowers",
            new[] { WorkspaceProfiles.BackendId });

        Assert.True(result.Success, result.Details);
        Assert.True(File.Exists(Path.Combine(backendRepoRoot, "README.md")));
        Assert.Equal("library-dispatching", await File.ReadAllTextAsync(Path.Combine(backendRepoRoot, "dispatching-parallel-agents", "SKILL.md")));

        var snapshot = await service.LoadAsync();
        var dispatching = Assert.Single(snapshot.InstalledSkills.Where(item => item.RelativePath == "superpowers/dispatching-parallel-agents"));
        Assert.Equal(new[] { WorkspaceProfiles.BackendId }, dispatching.BindingProfileIds);
        Assert.True(dispatching.IsRegistered);
    }

    [Fact]
    public async Task SaveSkillGroupBindingsAsync_Uses_Source_Group_Members_When_Source_Group_Is_Partial()
    {
        using var scope = new TestHubRootScope();
        const string completeProfile = "zeta";

        var backendRepoRoot = Path.Combine(GetProfileSkillsRoot(scope.RootPath, WorkspaceProfiles.BackendId), "superpowers");
        var completeRepoRoot = Path.Combine(GetProfileSkillsRoot(scope.RootPath, completeProfile), "superpowers");
        Directory.CreateDirectory(Path.Combine(backendRepoRoot, "brainstorming"));
        Directory.CreateDirectory(Path.Combine(completeRepoRoot, "brainstorming"));
        Directory.CreateDirectory(Path.Combine(completeRepoRoot, "dispatching-parallel-agents"));

        await File.WriteAllTextAsync(Path.Combine(backendRepoRoot, "README.md"), "backend-root");
        await File.WriteAllTextAsync(Path.Combine(backendRepoRoot, "brainstorming", "SKILL.md"), "backend-brainstorming");
        await File.WriteAllTextAsync(Path.Combine(completeRepoRoot, "README.md"), "complete-root");
        await File.WriteAllTextAsync(Path.Combine(completeRepoRoot, "brainstorming", "SKILL.md"), "complete-brainstorming");
        await File.WriteAllTextAsync(Path.Combine(completeRepoRoot, "dispatching-parallel-agents", "SKILL.md"), "complete-dispatching");

        var service = CreateService(scope.RootPath);
        await service.SaveInstallAsync(new SkillInstallRecord
        {
            Name = "brainstorming",
            Profile = WorkspaceProfiles.BackendId,
            InstalledRelativePath = "superpowers/brainstorming",
            CustomizationMode = SkillCustomizationMode.Local
        });
        await service.CaptureBaselineAsync(WorkspaceProfiles.BackendId, "superpowers/brainstorming");

        await service.SaveInstallAsync(new SkillInstallRecord
        {
            Name = "brainstorming",
            Profile = completeProfile,
            InstalledRelativePath = "superpowers/brainstorming",
            CustomizationMode = SkillCustomizationMode.Local
        });
        await service.SaveInstallAsync(new SkillInstallRecord
        {
            Name = "dispatching-parallel-agents",
            Profile = completeProfile,
            InstalledRelativePath = "superpowers/dispatching-parallel-agents",
            CustomizationMode = SkillCustomizationMode.Local
        });
        await service.CaptureBaselineAsync(completeProfile, "superpowers/brainstorming");
        await service.CaptureBaselineAsync(completeProfile, "superpowers/dispatching-parallel-agents");

        var preview = await service.PreviewSkillGroupBindingResolutionAsync(
            WorkspaceProfiles.BackendId,
            "superpowers",
            new[] { completeProfile });

        Assert.Equal(BindingResolutionStatus.Resolved, preview.ResolutionStatus);
        Assert.Equal(WorkspaceProfiles.BackendId, preview.ContentDonorProfileId);
        Assert.Equal(completeProfile, preview.PrimaryDestinationProfileId);
        Assert.Equal(new[] { completeProfile }, preview.MaterializedProfileIds);
        Assert.Equal(
            new[] { "superpowers/brainstorming" },
            preview.MaterializedMemberPaths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray());

        var result = await service.SaveSkillGroupBindingsAsync(
            WorkspaceProfiles.BackendId,
            "superpowers",
            new[] { completeProfile });

        Assert.True(result.Success, result.Details);
        Assert.False(Directory.Exists(backendRepoRoot));
        Assert.False(File.Exists(Path.Combine(completeRepoRoot, "dispatching-parallel-agents", "SKILL.md")));
        Assert.Equal("backend-brainstorming", await File.ReadAllTextAsync(Path.Combine(completeRepoRoot, "brainstorming", "SKILL.md")));

        var snapshot = await service.LoadAsync();
        Assert.DoesNotContain(snapshot.InstalledSkills, item => item.RelativePath == "superpowers/dispatching-parallel-agents");
        var brainstorming = Assert.Single(snapshot.InstalledSkills.Where(item => item.RelativePath == "superpowers/brainstorming"));
        Assert.Equal(new[] { completeProfile }, brainstorming.BindingProfileIds);
        Assert.True(brainstorming.IsRegistered);
        Assert.True(brainstorming.HasBaseline);
    }

    [Fact]
    public async Task PreviewSkillGroupBindingResolutionAsync_Falls_Back_To_Library_When_Source_Group_Is_Missing()
    {
        using var scope = new TestHubRootScope();
        var libraryRepoRoot = Path.Combine(GetSkillLibraryRoot(scope.RootPath), "superpowers");
        Directory.CreateDirectory(Path.Combine(libraryRepoRoot, "brainstorming"));
        Directory.CreateDirectory(Path.Combine(libraryRepoRoot, "dispatching-parallel-agents"));
        await File.WriteAllTextAsync(Path.Combine(libraryRepoRoot, "README.md"), "library-root");
        await File.WriteAllTextAsync(Path.Combine(libraryRepoRoot, "brainstorming", "SKILL.md"), "library-brainstorming");
        await File.WriteAllTextAsync(Path.Combine(libraryRepoRoot, "dispatching-parallel-agents", "SKILL.md"), "library-dispatching");

        var service = CreateService(scope.RootPath);

        var preview = await service.PreviewSkillGroupBindingResolutionAsync(
            WorkspaceProfiles.BackendId,
            "superpowers",
            new[] { WorkspaceProfiles.FrontendId });

        Assert.Equal(BindingResolutionStatus.Resolved, preview.ResolutionStatus);
        Assert.Equal(BindingSourceKind.Library, preview.ContentDonorKind);
        Assert.Equal("library", preview.ContentDonorProfileId);
        Assert.Equal(WorkspaceProfiles.FrontendId, preview.PrimaryDestinationProfileId);
        Assert.Equal(new[] { WorkspaceProfiles.FrontendId }, preview.MaterializedProfileIds);
        Assert.Equal(new[] { WorkspaceProfiles.FrontendId }, preview.RefreshedProfileIds);
        Assert.Contains(WorkspaceProfiles.BackendId, preview.RemovedProfileIds, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(
            new[] { "superpowers/brainstorming", "superpowers/dispatching-parallel-agents" },
            preview.MaterializedMemberPaths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray());
    }

    [Fact]
    public async Task PreviewSkillGroupBindingResolutionAsync_Empty_Group_Path_Matches_Save_Failure()
    {
        using var scope = new TestHubRootScope();
        var service = CreateService(scope.RootPath);

        var preview = await service.PreviewSkillGroupBindingResolutionAsync(
            WorkspaceProfiles.BackendId,
            "",
            new[] { WorkspaceProfiles.FrontendId });

        var result = await service.SaveSkillGroupBindingsAsync(
            WorkspaceProfiles.BackendId,
            "",
            new[] { WorkspaceProfiles.FrontendId });

        Assert.Equal(BindingResolutionStatus.Unresolvable, preview.ResolutionStatus);
        Assert.Equal("Select a skill repository or folder before editing bindings.", preview.ResolutionReason);
        Assert.False(result.Success);
        Assert.Equal(result.Message, preview.ResolutionReason);
        Assert.Equal(BindingSourceKind.None, preview.PrimaryDestinationKind);
        Assert.Equal(string.Empty, preview.PrimaryDestinationProfileId);
        Assert.Empty(preview.MaterializedProfileIds);
        Assert.Empty(preview.RefreshedProfileIds);
        Assert.Empty(preview.RemovedProfileIds);
    }

    [Fact]
    public async Task PreviewSkillGroupBindingResolutionAsync_Unresolvable_Does_Not_Expose_Destination_Or_Profile_Changes()
    {
        using var scope = new TestHubRootScope();
        var service = CreateService(scope.RootPath);

        var preview = await service.PreviewSkillGroupBindingResolutionAsync(
            WorkspaceProfiles.BackendId,
            "superpowers",
            new[] { WorkspaceProfiles.FrontendId });

        var result = await service.SaveSkillGroupBindingsAsync(
            WorkspaceProfiles.BackendId,
            "superpowers",
            new[] { WorkspaceProfiles.FrontendId });

        Assert.Equal(BindingResolutionStatus.Unresolvable, preview.ResolutionStatus);
        Assert.False(result.Success);
        Assert.Equal(result.Message, preview.ResolutionReason);
        Assert.Equal(BindingSourceKind.None, preview.PrimaryDestinationKind);
        Assert.Equal(string.Empty, preview.PrimaryDestinationProfileId);
        Assert.Empty(preview.MaterializedProfileIds);
        Assert.Empty(preview.RefreshedProfileIds);
        Assert.Empty(preview.RemovedProfileIds);
    }

    [Fact]
    public async Task SaveSkillGroupBindingsAsync_Retains_Library_Donor_And_Prunes_Target_To_Authoritative_Members()
    {
        using var scope = new TestHubRootScope();
        var libraryRepoRoot = Path.Combine(GetSkillLibraryRoot(scope.RootPath), "superpowers");
        var frontendRepoRoot = Path.Combine(GetProfileSkillsRoot(scope.RootPath, WorkspaceProfiles.FrontendId), "superpowers");

        Directory.CreateDirectory(Path.Combine(libraryRepoRoot, "brainstorming"));
        Directory.CreateDirectory(Path.Combine(frontendRepoRoot, "brainstorming"));
        Directory.CreateDirectory(Path.Combine(frontendRepoRoot, "dispatching-parallel-agents"));

        await File.WriteAllTextAsync(Path.Combine(libraryRepoRoot, "README.md"), "library-root");
        await File.WriteAllTextAsync(Path.Combine(libraryRepoRoot, "brainstorming", "SKILL.md"), "library-brainstorming");
        await File.WriteAllTextAsync(Path.Combine(frontendRepoRoot, "README.md"), "frontend-root");
        await File.WriteAllTextAsync(Path.Combine(frontendRepoRoot, "brainstorming", "SKILL.md"), "frontend-brainstorming");
        await File.WriteAllTextAsync(Path.Combine(frontendRepoRoot, "dispatching-parallel-agents", "SKILL.md"), "frontend-dispatching");

        var service = CreateService(scope.RootPath);
        await service.SaveInstallAsync(new SkillInstallRecord
        {
            Name = "brainstorming",
            Profile = WorkspaceProfiles.FrontendId,
            InstalledRelativePath = "superpowers/brainstorming",
            CustomizationMode = SkillCustomizationMode.Local
        });
        await service.SaveInstallAsync(new SkillInstallRecord
        {
            Name = "dispatching-parallel-agents",
            Profile = WorkspaceProfiles.FrontendId,
            InstalledRelativePath = "superpowers/dispatching-parallel-agents",
            CustomizationMode = SkillCustomizationMode.Local
        });

        var preview = await service.PreviewSkillGroupBindingResolutionAsync(
            WorkspaceProfiles.BackendId,
            "superpowers",
            new[] { WorkspaceProfiles.FrontendId });

        Assert.Equal(BindingResolutionStatus.Resolved, preview.ResolutionStatus);
        Assert.Equal(BindingSourceKind.Library, preview.ContentDonorKind);
        Assert.Equal("library", preview.ContentDonorProfileId);
        Assert.Equal(new[] { WorkspaceProfiles.FrontendId }, preview.MaterializedProfileIds);
        Assert.Equal(new[] { "superpowers/brainstorming" }, preview.MaterializedMemberPaths);

        var result = await service.SaveSkillGroupBindingsAsync(
            WorkspaceProfiles.BackendId,
            "superpowers",
            new[] { WorkspaceProfiles.FrontendId });

        Assert.True(result.Success, result.Details);
        Assert.True(File.Exists(Path.Combine(libraryRepoRoot, "brainstorming", "SKILL.md")));
        Assert.False(File.Exists(Path.Combine(libraryRepoRoot, "dispatching-parallel-agents", "SKILL.md")));
        Assert.False(File.Exists(Path.Combine(frontendRepoRoot, "dispatching-parallel-agents", "SKILL.md")));
        Assert.Equal("library-brainstorming", await File.ReadAllTextAsync(Path.Combine(frontendRepoRoot, "brainstorming", "SKILL.md")));

        var snapshot = await service.LoadAsync();
        Assert.DoesNotContain(snapshot.InstalledSkills, item => string.Equals(item.RelativePath, "superpowers/dispatching-parallel-agents", StringComparison.OrdinalIgnoreCase));
        var frontendSkill = Assert.Single(snapshot.InstalledSkills.Where(item =>
            string.Equals(item.Profile, WorkspaceProfiles.FrontendId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(item.RelativePath, "superpowers/brainstorming", StringComparison.OrdinalIgnoreCase)));
        Assert.True(frontendSkill.HasBaseline);
        Assert.False(frontendSkill.IsDirty);
    }

    [Fact]
    public async Task PreviewSkillGroupBindingResolutionAsync_Is_Ambiguous_When_Target_Group_Mirrors_Diverge_And_Source_Is_Missing()
    {
        using var scope = new TestHubRootScope();
        const string divergentProfile = "zeta";

        var frontendRepoRoot = Path.Combine(GetProfileSkillsRoot(scope.RootPath, WorkspaceProfiles.FrontendId), "superpowers");
        var divergentRepoRoot = Path.Combine(GetProfileSkillsRoot(scope.RootPath, divergentProfile), "superpowers");
        Directory.CreateDirectory(Path.Combine(frontendRepoRoot, "brainstorming"));
        Directory.CreateDirectory(Path.Combine(frontendRepoRoot, "dispatching-parallel-agents"));
        Directory.CreateDirectory(Path.Combine(divergentRepoRoot, "brainstorming"));
        await File.WriteAllTextAsync(Path.Combine(frontendRepoRoot, "README.md"), "frontend-root");
        await File.WriteAllTextAsync(Path.Combine(frontendRepoRoot, "brainstorming", "SKILL.md"), "frontend-brainstorming");
        await File.WriteAllTextAsync(Path.Combine(frontendRepoRoot, "dispatching-parallel-agents", "SKILL.md"), "frontend-dispatching");
        await File.WriteAllTextAsync(Path.Combine(divergentRepoRoot, "README.md"), "divergent-root");
        await File.WriteAllTextAsync(Path.Combine(divergentRepoRoot, "brainstorming", "SKILL.md"), "divergent-brainstorming");

        var service = CreateService(scope.RootPath);

        var preview = await service.PreviewSkillGroupBindingResolutionAsync(
            WorkspaceProfiles.BackendId,
            "superpowers",
            new[] { WorkspaceProfiles.FrontendId, divergentProfile });

        Assert.Equal(BindingResolutionStatus.Ambiguous, preview.ResolutionStatus);
        Assert.NotEmpty(preview.ResolutionReason);
        Assert.Equal(BindingSourceKind.None, preview.PrimaryDestinationKind);
        Assert.Equal(string.Empty, preview.PrimaryDestinationProfileId);
        Assert.Empty(preview.MaterializedProfileIds);
        Assert.Empty(preview.RefreshedProfileIds);
        Assert.Empty(preview.RemovedProfileIds);

        var result = await service.SaveSkillGroupBindingsAsync(
            WorkspaceProfiles.BackendId,
            "superpowers",
            new[] { WorkspaceProfiles.FrontendId, divergentProfile });

        Assert.False(result.Success);
    }

    [Fact]
    public async Task PreviewSkillGroupBindingResolutionAsync_Equivalent_Target_Fallbacks_Report_No_Metadata_Donor()
    {
        using var scope = new TestHubRootScope();
        var frontendRepoRoot = Path.Combine(GetProfileSkillsRoot(scope.RootPath, WorkspaceProfiles.FrontendId), "superpowers");
        var backendRepoRoot = Path.Combine(GetProfileSkillsRoot(scope.RootPath, WorkspaceProfiles.BackendId), "superpowers");
        Directory.CreateDirectory(Path.Combine(frontendRepoRoot, "brainstorming"));
        Directory.CreateDirectory(Path.Combine(frontendRepoRoot, "dispatching-parallel-agents"));
        Directory.CreateDirectory(Path.Combine(backendRepoRoot, "brainstorming"));
        Directory.CreateDirectory(Path.Combine(backendRepoRoot, "dispatching-parallel-agents"));
        await File.WriteAllTextAsync(Path.Combine(frontendRepoRoot, "README.md"), "shared-root");
        await File.WriteAllTextAsync(Path.Combine(backendRepoRoot, "README.md"), "shared-root");
        await File.WriteAllTextAsync(Path.Combine(frontendRepoRoot, "brainstorming", "SKILL.md"), "shared-brainstorming");
        await File.WriteAllTextAsync(Path.Combine(backendRepoRoot, "brainstorming", "SKILL.md"), "shared-brainstorming");
        await File.WriteAllTextAsync(Path.Combine(frontendRepoRoot, "dispatching-parallel-agents", "SKILL.md"), "shared-dispatching");
        await File.WriteAllTextAsync(Path.Combine(backendRepoRoot, "dispatching-parallel-agents", "SKILL.md"), "shared-dispatching");

        Directory.CreateDirectory(Path.GetDirectoryName(GetSkillInstallsPath(scope.RootPath))!);
        await File.WriteAllTextAsync(
            GetSkillInstallsPath(scope.RootPath),
            JsonSerializer.Serialize(
                new
                {
                    installs = new object[]
                    {
                        new SkillInstallRecord
                        {
                            Name = "brainstorming",
                            Profile = WorkspaceProfiles.FrontendId,
                            InstalledRelativePath = "superpowers/brainstorming",
                            SourceLocalName = "frontend-source",
                            SourceProfile = WorkspaceProfiles.FrontendId,
                            SourceSkillPath = "catalog/brainstorming",
                            CustomizationMode = SkillCustomizationMode.Managed
                        },
                        new SkillInstallRecord
                        {
                            Name = "brainstorming",
                            Profile = WorkspaceProfiles.BackendId,
                            InstalledRelativePath = "superpowers/brainstorming",
                            SourceLocalName = "backend-source",
                            SourceProfile = WorkspaceProfiles.BackendId,
                            SourceSkillPath = "catalog/brainstorming",
                            CustomizationMode = SkillCustomizationMode.Managed
                        }
                    }
                },
                new JsonSerializerOptions
                {
                    WriteIndented = true
                }));

        var service = CreateService(scope.RootPath);

        var preview = await service.PreviewSkillGroupBindingResolutionAsync(
            WorkspaceProfiles.GlobalId,
            "superpowers",
            new[] { WorkspaceProfiles.FrontendId, WorkspaceProfiles.BackendId, "alpha" });

        Assert.Equal(BindingResolutionStatus.Resolved, preview.ResolutionStatus);
        Assert.Equal(WorkspaceProfiles.FrontendId, preview.ContentDonorProfileId);
        Assert.Equal(BindingSourceKind.None, preview.MetadataDonorKind);
        Assert.Equal(string.Empty, preview.MetadataDonorProfileId);
    }

    [Fact]
    public async Task SaveSkillGroupBindingsAsync_Equivalent_Target_Fallbacks_Preserve_Existing_Metadata_And_Synthesize_New_Target_Metadata()
    {
        using var scope = new TestHubRootScope();
        const string newProfile = "alpha";
        var frontendRepoRoot = Path.Combine(GetProfileSkillsRoot(scope.RootPath, WorkspaceProfiles.FrontendId), "superpowers");
        var backendRepoRoot = Path.Combine(GetProfileSkillsRoot(scope.RootPath, WorkspaceProfiles.BackendId), "superpowers");
        Directory.CreateDirectory(Path.Combine(frontendRepoRoot, "brainstorming"));
        Directory.CreateDirectory(Path.Combine(frontendRepoRoot, "dispatching-parallel-agents"));
        Directory.CreateDirectory(Path.Combine(backendRepoRoot, "brainstorming"));
        Directory.CreateDirectory(Path.Combine(backendRepoRoot, "dispatching-parallel-agents"));
        await File.WriteAllTextAsync(Path.Combine(frontendRepoRoot, "README.md"), "shared-root");
        await File.WriteAllTextAsync(Path.Combine(backendRepoRoot, "README.md"), "shared-root");
        await File.WriteAllTextAsync(Path.Combine(frontendRepoRoot, "brainstorming", "SKILL.md"), "shared-brainstorming");
        await File.WriteAllTextAsync(Path.Combine(backendRepoRoot, "brainstorming", "SKILL.md"), "shared-brainstorming");
        await File.WriteAllTextAsync(Path.Combine(frontendRepoRoot, "dispatching-parallel-agents", "SKILL.md"), "shared-dispatching");
        await File.WriteAllTextAsync(Path.Combine(backendRepoRoot, "dispatching-parallel-agents", "SKILL.md"), "shared-dispatching");

        var preservedSyncAt = DateTimeOffset.UtcNow.AddHours(-4);

        Directory.CreateDirectory(Path.GetDirectoryName(GetSkillInstallsPath(scope.RootPath))!);
        await File.WriteAllTextAsync(
            GetSkillInstallsPath(scope.RootPath),
            JsonSerializer.Serialize(
                new
                {
                    installs = new object[]
                    {
                        new SkillInstallRecord
                        {
                            Name = "brainstorming",
                            Profile = WorkspaceProfiles.FrontendId,
                            InstalledRelativePath = "superpowers/brainstorming",
                            SourceLocalName = "frontend-source",
                            SourceProfile = WorkspaceProfiles.FrontendId,
                            SourceSkillPath = "catalog/brainstorming",
                            CustomizationMode = SkillCustomizationMode.Managed
                        },
                        new SkillInstallRecord
                        {
                            Name = "dispatching-parallel-agents",
                            Profile = WorkspaceProfiles.FrontendId,
                            InstalledRelativePath = "superpowers/dispatching-parallel-agents",
                            SourceLocalName = "frontend-source",
                            SourceProfile = WorkspaceProfiles.FrontendId,
                            SourceSkillPath = "catalog/dispatching-parallel-agents",
                            CustomizationMode = SkillCustomizationMode.Managed
                        },
                        new SkillInstallRecord
                        {
                            Name = "brainstorming",
                            Profile = WorkspaceProfiles.BackendId,
                            InstalledRelativePath = "superpowers/brainstorming",
                            SourceLocalName = "backend-source",
                            SourceProfile = WorkspaceProfiles.BackendId,
                            SourceSkillPath = "catalog/brainstorming",
                            CustomizationMode = SkillCustomizationMode.Managed
                        },
                        new SkillInstallRecord
                        {
                            Name = "dispatching-parallel-agents",
                            Profile = WorkspaceProfiles.BackendId,
                            InstalledRelativePath = "superpowers/dispatching-parallel-agents",
                            SourceLocalName = "backend-source",
                            SourceProfile = WorkspaceProfiles.BackendId,
                            SourceSkillPath = "catalog/dispatching-parallel-agents",
                            CustomizationMode = SkillCustomizationMode.Managed
                        }
                    }
                },
                new JsonSerializerOptions
                {
                    WriteIndented = true
                }));
        await File.WriteAllTextAsync(
            GetSkillStatesPath(scope.RootPath),
            JsonSerializer.Serialize(
                new
                {
                    states = new object[]
                    {
                        new SkillInstallStateRecord
                        {
                            Profile = WorkspaceProfiles.BackendId,
                            InstalledRelativePath = "superpowers/brainstorming",
                            BaselineFiles = new List<SkillFileFingerprintRecord>
                            {
                                new()
                                {
                                    RelativePath = "SKILL.md",
                                    Sha256 = "STALE-BACKEND",
                                    Size = 1
                                }
                            },
                            SourceBaselineFiles = new List<SkillFileFingerprintRecord>
                            {
                                new()
                                {
                                    RelativePath = "SKILL.md",
                                    Sha256 = "PRESERVE-GROUP-SOURCE",
                                    Size = 2
                                }
                            },
                            LastSyncAt = preservedSyncAt,
                            LastAppliedReference = "backend-group-ref",
                            LastBackupPath = "backups/backend/superpowers/brainstorming"
                        }
                    }
                },
                new JsonSerializerOptions
                {
                    WriteIndented = true
                }));

        var service = CreateService(scope.RootPath);

        var result = await service.SaveSkillGroupBindingsAsync(
            WorkspaceProfiles.GlobalId,
            "superpowers",
            new[] { WorkspaceProfiles.FrontendId, WorkspaceProfiles.BackendId, newProfile });

        Assert.True(result.Success, result.Details);

        using var installsDocument = JsonDocument.Parse(await File.ReadAllTextAsync(GetSkillInstallsPath(scope.RootPath)));
        var installs = installsDocument.RootElement.GetProperty("installs").EnumerateArray().ToArray();
        var savedBackendInstall = installs.Single(item =>
            string.Equals(item.GetProperty("profile").GetString(), WorkspaceProfiles.BackendId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(item.GetProperty("installedRelativePath").GetString(), "superpowers/brainstorming", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("backend-source", savedBackendInstall.GetProperty("sourceLocalName").GetString());
        Assert.Equal(WorkspaceProfiles.BackendId, savedBackendInstall.GetProperty("sourceProfile").GetString());

        var savedAlphaInstall = installs.Single(item =>
            string.Equals(item.GetProperty("profile").GetString(), newProfile, StringComparison.OrdinalIgnoreCase)
            && string.Equals(item.GetProperty("installedRelativePath").GetString(), "superpowers/brainstorming", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("local", savedAlphaInstall.GetProperty("customizationMode").GetString());
        Assert.Equal(JsonValueKind.Null, savedAlphaInstall.GetProperty("sourceLocalName").ValueKind);
        Assert.Equal(JsonValueKind.Null, savedAlphaInstall.GetProperty("sourceProfile").ValueKind);
        Assert.Equal(JsonValueKind.Null, savedAlphaInstall.GetProperty("sourceSkillPath").ValueKind);

        using var statesDocument = JsonDocument.Parse(await File.ReadAllTextAsync(GetSkillStatesPath(scope.RootPath)));
        var states = statesDocument.RootElement.GetProperty("states").EnumerateArray().ToArray();
        var savedBackendState = states.Single(item =>
            string.Equals(item.GetProperty("profile").GetString(), WorkspaceProfiles.BackendId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(item.GetProperty("installedRelativePath").GetString(), "superpowers/brainstorming", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("backend-group-ref", savedBackendState.GetProperty("lastAppliedReference").GetString());
        Assert.Equal("backups/backend/superpowers/brainstorming", savedBackendState.GetProperty("lastBackupPath").GetString());
        Assert.Equal(preservedSyncAt.ToUniversalTime().ToString("O"), savedBackendState.GetProperty("lastSyncAt").GetDateTimeOffset().ToUniversalTime().ToString("O"));
        Assert.Contains(
            savedBackendState.GetProperty("sourceBaselineFiles").EnumerateArray().Select(item => item.GetProperty("sha256").GetString()),
            value => string.Equals(value, "PRESERVE-GROUP-SOURCE", StringComparison.OrdinalIgnoreCase));

        var savedAlphaState = states.Single(item =>
            string.Equals(item.GetProperty("profile").GetString(), newProfile, StringComparison.OrdinalIgnoreCase)
            && string.Equals(item.GetProperty("installedRelativePath").GetString(), "superpowers/brainstorming", StringComparison.OrdinalIgnoreCase));
        Assert.NotEmpty(savedAlphaState.GetProperty("baselineFiles").EnumerateArray());
        Assert.NotEmpty(savedAlphaState.GetProperty("sourceBaselineFiles").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, savedAlphaState.GetProperty("lastAppliedReference").ValueKind);
        Assert.Equal(JsonValueKind.Null, savedAlphaState.GetProperty("lastBackupPath").ValueKind);
    }

    [Fact]
    public async Task SaveSkillGroupBindingsAsync_Retained_Content_Donor_Members_Refresh_Lineage_From_Metadata_Donor()
    {
        using var scope = new TestHubRootScope();
        const string newProfile = "alpha";
        var backendRepoRoot = Path.Combine(GetProfileSkillsRoot(scope.RootPath, WorkspaceProfiles.BackendId), "superpowers");
        Directory.CreateDirectory(Path.Combine(backendRepoRoot, "brainstorming"));
        await File.WriteAllTextAsync(Path.Combine(backendRepoRoot, "brainstorming", "SKILL.md"), "backend-brainstorming");

        var metadataSyncAt = DateTimeOffset.UtcNow.AddHours(-5);

        Directory.CreateDirectory(Path.GetDirectoryName(GetSkillInstallsPath(scope.RootPath))!);
        await File.WriteAllTextAsync(
            GetSkillInstallsPath(scope.RootPath),
            JsonSerializer.Serialize(
                new
                {
                    installs = new object[]
                    {
                        new SkillInstallRecord
                        {
                            Name = "brainstorming",
                            Profile = WorkspaceProfiles.GlobalId,
                            InstalledRelativePath = "superpowers/brainstorming",
                            SourceLocalName = "global-source",
                            SourceProfile = WorkspaceProfiles.GlobalId,
                            SourceSkillPath = "catalog/global-brainstorming",
                            CustomizationMode = SkillCustomizationMode.Managed
                        },
                        new SkillInstallRecord
                        {
                            Name = "brainstorming",
                            Profile = WorkspaceProfiles.BackendId,
                            InstalledRelativePath = "superpowers/brainstorming",
                            SourceLocalName = "backend-source",
                            SourceProfile = WorkspaceProfiles.BackendId,
                            SourceSkillPath = "catalog/backend-brainstorming",
                            CustomizationMode = SkillCustomizationMode.Managed
                        }
                    }
                },
                new JsonSerializerOptions
                {
                    WriteIndented = true
                }));
        await File.WriteAllTextAsync(
            GetSkillStatesPath(scope.RootPath),
            JsonSerializer.Serialize(
                new
                {
                    states = new object[]
                    {
                        new SkillInstallStateRecord
                        {
                            Profile = WorkspaceProfiles.GlobalId,
                            InstalledRelativePath = "superpowers/brainstorming",
                            BaselineFiles = new List<SkillFileFingerprintRecord>(),
                            LastSyncAt = metadataSyncAt,
                            LastAppliedReference = "global-group-ref",
                            LastBackupPath = "backups/global/superpowers/brainstorming"
                        },
                        new SkillInstallStateRecord
                        {
                            Profile = WorkspaceProfiles.BackendId,
                            InstalledRelativePath = "superpowers/brainstorming",
                            BaselineFiles = new List<SkillFileFingerprintRecord>(),
                            LastSyncAt = metadataSyncAt.AddHours(1),
                            LastAppliedReference = "backend-group-ref",
                            LastBackupPath = "backups/backend/superpowers/brainstorming"
                        }
                    }
                },
                new JsonSerializerOptions
                {
                    WriteIndented = true
                }));

        var service = CreateService(scope.RootPath);

        var preview = await service.PreviewSkillGroupBindingResolutionAsync(
            WorkspaceProfiles.GlobalId,
            "superpowers",
            new[] { WorkspaceProfiles.BackendId, newProfile });

        Assert.Equal(BindingResolutionStatus.Resolved, preview.ResolutionStatus);
        Assert.Equal(WorkspaceProfiles.BackendId, preview.ContentDonorProfileId);
        Assert.Equal(WorkspaceProfiles.GlobalId, preview.MetadataDonorProfileId);
        Assert.Equal(new[] { "superpowers/brainstorming" }, preview.MaterializedMemberPaths);

        var result = await service.SaveSkillGroupBindingsAsync(
            WorkspaceProfiles.GlobalId,
            "superpowers",
            new[] { WorkspaceProfiles.BackendId, newProfile });

        Assert.True(result.Success, result.Details);

        using var installsDocument = JsonDocument.Parse(await File.ReadAllTextAsync(GetSkillInstallsPath(scope.RootPath)));
        var savedBackendInstall = installsDocument.RootElement.GetProperty("installs")
            .EnumerateArray()
            .Single(item =>
                string.Equals(item.GetProperty("profile").GetString(), WorkspaceProfiles.BackendId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(item.GetProperty("installedRelativePath").GetString(), "superpowers/brainstorming", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("global-source", savedBackendInstall.GetProperty("sourceLocalName").GetString());
        Assert.Equal(WorkspaceProfiles.GlobalId, savedBackendInstall.GetProperty("sourceProfile").GetString());
        Assert.Equal("catalog/global-brainstorming", savedBackendInstall.GetProperty("sourceSkillPath").GetString());

        using var statesDocument = JsonDocument.Parse(await File.ReadAllTextAsync(GetSkillStatesPath(scope.RootPath)));
        var savedBackendState = statesDocument.RootElement.GetProperty("states")
            .EnumerateArray()
            .Single(item =>
                string.Equals(item.GetProperty("profile").GetString(), WorkspaceProfiles.BackendId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(item.GetProperty("installedRelativePath").GetString(), "superpowers/brainstorming", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(metadataSyncAt.ToUniversalTime().ToString("O"), savedBackendState.GetProperty("lastSyncAt").GetDateTimeOffset().ToUniversalTime().ToString("O"));
        Assert.Equal("global-group-ref", savedBackendState.GetProperty("lastAppliedReference").GetString());
        Assert.Equal("backups/global/superpowers/brainstorming", savedBackendState.GetProperty("lastBackupPath").GetString());
    }

    [Fact]
    public async Task SaveSkillGroupBindingsAsync_Synthesizes_Metadata_For_Physical_Donor_Members_When_Source_Is_Authoritative()
    {
        using var scope = new TestHubRootScope();
        const string targetProfile = "alpha";

        var sourceRepoRoot = Path.Combine(GetProfileSkillsRoot(scope.RootPath, WorkspaceProfiles.BackendId), "superpowers");
        var targetRepoRoot = Path.Combine(GetProfileSkillsRoot(scope.RootPath, targetProfile), "superpowers");
        Directory.CreateDirectory(Path.Combine(sourceRepoRoot, "brainstorming"));
        Directory.CreateDirectory(Path.Combine(sourceRepoRoot, "ghost-skill"));
        await File.WriteAllTextAsync(Path.Combine(sourceRepoRoot, "README.md"), "source-root");
        await File.WriteAllTextAsync(Path.Combine(sourceRepoRoot, "brainstorming", "SKILL.md"), "source-brainstorming");
        await File.WriteAllTextAsync(Path.Combine(sourceRepoRoot, "ghost-skill", "SKILL.md"), "source-ghost");

        var service = CreateService(scope.RootPath);
        await service.SaveInstallAsync(new SkillInstallRecord
        {
            Name = "brainstorming",
            Profile = WorkspaceProfiles.BackendId,
            InstalledRelativePath = "superpowers/brainstorming",
            CustomizationMode = SkillCustomizationMode.Local
        });
        await service.CaptureBaselineAsync(WorkspaceProfiles.BackendId, "superpowers/brainstorming");

        var result = await service.SaveSkillGroupBindingsAsync(
            WorkspaceProfiles.BackendId,
            "superpowers",
            new[] { targetProfile });

        Assert.True(result.Success, result.Details);
        Assert.True(File.Exists(Path.Combine(targetRepoRoot, "brainstorming", "SKILL.md")));
        Assert.True(File.Exists(Path.Combine(targetRepoRoot, "ghost-skill", "SKILL.md")));

        var snapshot = await service.LoadAsync();
        var ghostSkill = Assert.Single(snapshot.InstalledSkills.Where(item => item.RelativePath == "superpowers/ghost-skill"));
        Assert.Equal(new[] { targetProfile }, ghostSkill.BindingProfileIds);
        Assert.True(ghostSkill.IsRegistered);
        Assert.True(ghostSkill.HasBaseline);
    }

    [Fact]
    public async Task SaveSkillGroupBindingsAsync_Ignores_Phantom_Metadata_When_Target_Profile_Has_No_Matching_Directory()
    {
        using var scope = new TestHubRootScope();
        const string targetProfile = "alpha";

        var backendRepoRoot = Path.Combine(GetProfileSkillsRoot(scope.RootPath, WorkspaceProfiles.BackendId), "superpowers");
        var targetRepoRoot = Path.Combine(GetProfileSkillsRoot(scope.RootPath, targetProfile), "superpowers");
        Directory.CreateDirectory(Path.Combine(backendRepoRoot, "brainstorming"));
        Directory.CreateDirectory(Path.Combine(targetRepoRoot, "brainstorming"));
        await File.WriteAllTextAsync(Path.Combine(backendRepoRoot, "README.md"), "backend-root");
        await File.WriteAllTextAsync(Path.Combine(backendRepoRoot, "brainstorming", "SKILL.md"), "backend-brainstorming");
        await File.WriteAllTextAsync(Path.Combine(targetRepoRoot, "README.md"), "target-root");
        await File.WriteAllTextAsync(Path.Combine(targetRepoRoot, "brainstorming", "SKILL.md"), "target-brainstorming");

        Directory.CreateDirectory(Path.GetDirectoryName(GetSkillInstallsPath(scope.RootPath))!);
        await File.WriteAllTextAsync(
            GetSkillInstallsPath(scope.RootPath),
            JsonSerializer.Serialize(
                new
                {
                    installs = new object[]
                    {
                        new SkillInstallRecord
                        {
                            Name = "brainstorming",
                            Profile = WorkspaceProfiles.BackendId,
                            InstalledRelativePath = "superpowers/brainstorming",
                            CustomizationMode = SkillCustomizationMode.Local
                        },
                        new SkillInstallRecord
                        {
                            Name = "brainstorming",
                            Profile = targetProfile,
                            InstalledRelativePath = "superpowers/brainstorming",
                            CustomizationMode = SkillCustomizationMode.Local
                        },
                        new SkillInstallRecord
                        {
                            Name = "ghost-skill",
                            Profile = targetProfile,
                            InstalledRelativePath = "superpowers/ghost-skill",
                            CustomizationMode = SkillCustomizationMode.Local
                        }
                    }
                },
                new JsonSerializerOptions
                {
                    WriteIndented = true
                }));
        await File.WriteAllTextAsync(
            GetSkillStatesPath(scope.RootPath),
            JsonSerializer.Serialize(
                new
                {
                    states = new object[]
                    {
                        new SkillInstallStateRecord
                        {
                            Profile = targetProfile,
                            InstalledRelativePath = "superpowers/ghost-skill",
                            BaselineCapturedAt = DateTimeOffset.UtcNow,
                            BaselineFiles = new List<SkillFileFingerprintRecord>()
                        }
                    }
                },
                new JsonSerializerOptions
                {
                    WriteIndented = true
                }));

        var service = CreateService(scope.RootPath);

        var result = await service.SaveSkillGroupBindingsAsync(
            WorkspaceProfiles.BackendId,
            "superpowers",
            new[] { targetProfile });

        Assert.True(result.Success, result.Details);
        Assert.False(Directory.Exists(Path.Combine(targetRepoRoot, "ghost-skill")));

        var snapshot = await service.LoadAsync();
        Assert.DoesNotContain(snapshot.InstalledSkills, item => item.RelativePath == "superpowers/ghost-skill");
        var brainstorming = Assert.Single(snapshot.InstalledSkills.Where(item => item.RelativePath == "superpowers/brainstorming"));
        Assert.Equal(new[] { targetProfile }, brainstorming.BindingProfileIds);
    }

    [Fact]
    public async Task SaveSkillGroupBindingsAsync_Ignores_Metadata_Only_Members_When_Target_Group_Root_Is_Missing()
    {
        using var scope = new TestHubRootScope();
        const string targetProfile = "alpha";

        var backendRepoRoot = Path.Combine(GetProfileSkillsRoot(scope.RootPath, WorkspaceProfiles.BackendId), "superpowers");
        Directory.CreateDirectory(Path.Combine(backendRepoRoot, "brainstorming"));
        await File.WriteAllTextAsync(Path.Combine(backendRepoRoot, "README.md"), "backend-root");
        await File.WriteAllTextAsync(Path.Combine(backendRepoRoot, "brainstorming", "SKILL.md"), "backend-brainstorming");

        Directory.CreateDirectory(Path.GetDirectoryName(GetSkillInstallsPath(scope.RootPath))!);
        await File.WriteAllTextAsync(
            GetSkillInstallsPath(scope.RootPath),
            JsonSerializer.Serialize(
                new
                {
                    installs = new object[]
                    {
                        new SkillInstallRecord
                        {
                            Name = "brainstorming",
                            Profile = WorkspaceProfiles.BackendId,
                            InstalledRelativePath = "superpowers/brainstorming",
                            CustomizationMode = SkillCustomizationMode.Local
                        },
                        new SkillInstallRecord
                        {
                            Name = "brainstorming",
                            Profile = targetProfile,
                            InstalledRelativePath = "superpowers/brainstorming",
                            CustomizationMode = SkillCustomizationMode.Local
                        },
                        new SkillInstallRecord
                        {
                            Name = "ghost-skill",
                            Profile = targetProfile,
                            InstalledRelativePath = "superpowers/ghost-skill",
                            CustomizationMode = SkillCustomizationMode.Local
                        }
                    }
                },
                new JsonSerializerOptions
                {
                    WriteIndented = true
                }));

        await File.WriteAllTextAsync(
            GetSkillStatesPath(scope.RootPath),
            JsonSerializer.Serialize(
                new
                {
                    states = new object[]
                    {
                        new SkillInstallStateRecord
                        {
                            Profile = WorkspaceProfiles.BackendId,
                            InstalledRelativePath = "superpowers/brainstorming",
                            BaselineFiles = new List<SkillFileFingerprintRecord>()
                        },
                        new SkillInstallStateRecord
                        {
                            Profile = targetProfile,
                            InstalledRelativePath = "superpowers/brainstorming",
                            BaselineFiles = new List<SkillFileFingerprintRecord>()
                        },
                        new SkillInstallStateRecord
                        {
                            Profile = targetProfile,
                            InstalledRelativePath = "superpowers/ghost-skill",
                            BaselineFiles = new List<SkillFileFingerprintRecord>()
                        }
                    }
                },
                new JsonSerializerOptions
                {
                    WriteIndented = true
                }));

        var service = CreateService(scope.RootPath);

        var result = await service.SaveSkillGroupBindingsAsync(
            WorkspaceProfiles.BackendId,
            "superpowers",
            new[] { targetProfile });

        Assert.True(result.Success, result.Details);
        Assert.True(File.Exists(Path.Combine(GetProfileSkillsRoot(scope.RootPath, targetProfile), "superpowers", "brainstorming", "SKILL.md")));
        Assert.False(Directory.Exists(Path.Combine(GetProfileSkillsRoot(scope.RootPath, targetProfile), "superpowers", "ghost-skill")));

        var snapshot = await service.LoadAsync();
        Assert.DoesNotContain(snapshot.InstalledSkills, item => item.RelativePath == "superpowers/ghost-skill");
    }

    [Fact]
    public async Task PreviewSkillGroupBindingResolutionAsync_Uses_Source_Members_Instead_Of_Stale_Target_Superset()
    {
        using var scope = new TestHubRootScope();
        var sourceGroupRoot = Path.Combine(GetProfileSkillsRoot(scope.RootPath, WorkspaceProfiles.BackendId), "superpowers");
        var targetGroupRoot = Path.Combine(GetProfileSkillsRoot(scope.RootPath, WorkspaceProfiles.FrontendId), "superpowers");
        Directory.CreateDirectory(Path.Combine(sourceGroupRoot, "brainstorming"));
        Directory.CreateDirectory(Path.Combine(sourceGroupRoot, "dispatching-parallel-agents"));
        Directory.CreateDirectory(Path.Combine(targetGroupRoot, "brainstorming"));
        Directory.CreateDirectory(Path.Combine(targetGroupRoot, "dispatching-parallel-agents"));
        Directory.CreateDirectory(Path.Combine(targetGroupRoot, "ghost-skill"));
        await File.WriteAllTextAsync(Path.Combine(sourceGroupRoot, "brainstorming", "SKILL.md"), "backend-brainstorming");
        await File.WriteAllTextAsync(Path.Combine(sourceGroupRoot, "dispatching-parallel-agents", "SKILL.md"), "backend-dispatching");
        await File.WriteAllTextAsync(Path.Combine(targetGroupRoot, "brainstorming", "SKILL.md"), "frontend-brainstorming");
        await File.WriteAllTextAsync(Path.Combine(targetGroupRoot, "dispatching-parallel-agents", "SKILL.md"), "frontend-dispatching");
        await File.WriteAllTextAsync(Path.Combine(targetGroupRoot, "ghost-skill", "SKILL.md"), "frontend-ghost");

        var service = CreateService(scope.RootPath);

        var preview = await service.PreviewSkillGroupBindingResolutionAsync(
            WorkspaceProfiles.BackendId,
            "superpowers",
            new[] { WorkspaceProfiles.FrontendId });

        Assert.Equal(BindingResolutionStatus.Resolved, preview.ResolutionStatus);
        Assert.Equal(WorkspaceProfiles.BackendId, preview.ContentDonorProfileId);
        Assert.Equal(
            new[] { "superpowers/brainstorming", "superpowers/dispatching-parallel-agents" },
            preview.MaterializedMemberPaths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray());
        Assert.DoesNotContain("superpowers/ghost-skill", preview.MaterializedMemberPaths, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PreviewSkillGroupBindingResolutionAsync_Ignores_Ghost_Metadata_When_Selecting_Metadata_Donor()
    {
        using var scope = new TestHubRootScope();
        const string newProfile = "alpha";
        var backendRepoRoot = Path.Combine(GetProfileSkillsRoot(scope.RootPath, WorkspaceProfiles.BackendId), "superpowers");
        Directory.CreateDirectory(Path.Combine(backendRepoRoot, "brainstorming"));
        await File.WriteAllTextAsync(Path.Combine(backendRepoRoot, "brainstorming", "SKILL.md"), "backend-brainstorming");

        Directory.CreateDirectory(Path.GetDirectoryName(GetSkillInstallsPath(scope.RootPath))!);
        await File.WriteAllTextAsync(
            GetSkillInstallsPath(scope.RootPath),
            JsonSerializer.Serialize(
                new
                {
                    installs = new object[]
                    {
                        new SkillInstallRecord
                        {
                            Name = "ghost-skill",
                            Profile = WorkspaceProfiles.GlobalId,
                            InstalledRelativePath = "superpowers/ghost-skill",
                            SourceLocalName = "ghost-source",
                            SourceProfile = WorkspaceProfiles.GlobalId,
                            SourceSkillPath = "catalog/global-ghost"
                        }
                    }
                },
                new JsonSerializerOptions
                {
                    WriteIndented = true
                }));
        await File.WriteAllTextAsync(
            GetSkillStatesPath(scope.RootPath),
            JsonSerializer.Serialize(
                new
                {
                    states = new object[]
                    {
                        new SkillInstallStateRecord
                        {
                            Profile = WorkspaceProfiles.GlobalId,
                            InstalledRelativePath = "superpowers/ghost-skill",
                            BaselineFiles = new List<SkillFileFingerprintRecord>()
                        }
                    }
                },
                new JsonSerializerOptions
                {
                    WriteIndented = true
                }));

        var service = CreateService(scope.RootPath);

        var preview = await service.PreviewSkillGroupBindingResolutionAsync(
            WorkspaceProfiles.GlobalId,
            "superpowers",
            new[] { WorkspaceProfiles.BackendId, newProfile });

        Assert.Equal(BindingResolutionStatus.Resolved, preview.ResolutionStatus);
        Assert.Equal(WorkspaceProfiles.BackendId, preview.ContentDonorProfileId);
        Assert.Equal(WorkspaceProfiles.BackendId, preview.MetadataDonorProfileId);
        Assert.Equal(new[] { "superpowers/brainstorming" }, preview.MaterializedMemberPaths);
        Assert.DoesNotContain("superpowers/ghost-skill", preview.MaterializedMemberPaths, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SaveSkillBindingsAsync_Tolerates_Null_State_Collections_From_Legacy_Json()
    {
        using var scope = new TestHubRootScope();
        var globalSkillDirectory = Path.Combine(GetProfileSkillsRoot(scope.RootPath, WorkspaceProfiles.GlobalId), "demo-skill");
        Directory.CreateDirectory(globalSkillDirectory);
        await File.WriteAllTextAsync(Path.Combine(globalSkillDirectory, "SKILL.md"), "global");

        Directory.CreateDirectory(Path.GetDirectoryName(GetSkillInstallsPath(scope.RootPath))!);
        await File.WriteAllTextAsync(
            GetSkillInstallsPath(scope.RootPath),
            JsonSerializer.Serialize(
                new
                {
                    installs = new object[]
                    {
                        new SkillInstallRecord
                        {
                            Name = "demo-skill",
                            Profile = WorkspaceProfiles.GlobalId,
                            InstalledRelativePath = "demo-skill",
                            CustomizationMode = SkillCustomizationMode.Local
                        }
                    }
                },
                new JsonSerializerOptions
                {
                    WriteIndented = true
                }));

        await File.WriteAllTextAsync(
            GetSkillStatesPath(scope.RootPath),
            """
            {
              "states": [
                {
                  "profile": "global",
                  "installedRelativePath": "demo-skill",
                  "baselineCapturedAt": "2026-03-21T00:00:00Z",
                  "baselineFiles": null,
                  "sourceBaselineFiles": null,
                  "overlayDeletedFiles": null
                }
              ]
            }
            """);

        var service = CreateService(scope.RootPath);

        var result = await service.SaveSkillBindingsAsync(
            WorkspaceProfiles.GlobalId,
            "demo-skill",
            new[] { WorkspaceProfiles.GlobalId });

        Assert.True(result.Success, result.Details);

        var snapshot = await service.LoadAsync();
        var skill = Assert.Single(snapshot.InstalledSkills.Where(item => item.RelativePath == "demo-skill"));
        Assert.Equal(new[] { WorkspaceProfiles.GlobalId }, skill.BindingProfileIds);
    }

    [Fact]
    public async Task SaveInstallAsync_Reapplies_Onboarded_Project_Profile_For_NonGlobal_Skills()
    {
        using var scope = new TestHubRootScope();
        var projectPath = Path.Combine(scope.RootPath, "project");
        Directory.CreateDirectory(projectPath);

        var settingsStore = new JsonHubSettingsStore(scope.RootPath);
        await settingsStore.SaveAsync(new HubSettingsRecord
        {
            HubRoot = scope.RootPath,
            OnboardedProjectPaths = new[] { projectPath }
        });

        var projectRegistry = new JsonProjectRegistry(scope.RootPath);
        await projectRegistry.SaveAllAsync(new[]
        {
            new ProjectRecord("demo", projectPath, WorkspaceProfiles.FrontendId)
        });

        var automation = new RecordingWorkspaceAutomationService();
        var service = new SkillsCatalogService(
            new FixedHubRootLocator(scope.RootPath),
            _ => settingsStore,
            _ => projectRegistry,
            automation);

        var installDirectory = Path.Combine(GetProfileSkillsRoot(scope.RootPath, WorkspaceProfiles.FrontendId), "demo-skill");
        Directory.CreateDirectory(installDirectory);
        await File.WriteAllTextAsync(Path.Combine(installDirectory, "SKILL.md"), "demo");

        var result = await service.SaveInstallAsync(new SkillInstallRecord
        {
            Name = "demo-skill",
            Profile = WorkspaceProfiles.FrontendId,
            InstalledRelativePath = "demo-skill"
        });

        Assert.True(result.Success, result.Details);
        Assert.Equal(1, automation.ApplyGlobalLinksCallCount);
        Assert.Equal(1, automation.ApplyProjectProfileCallCount);
        Assert.Equal(projectPath, automation.LastAppliedProjectPath);
        Assert.Equal(WorkspaceProfiles.FrontendId, automation.LastAppliedProjectProfile);
    }

    [Fact]
    public async Task LoadAsync_Migrates_Legacy_Custom_Profiles_From_Settings_And_Manifest_Files()
    {
        using var scope = new TestHubRootScope();
        Directory.CreateDirectory(Path.Combine(scope.RootPath, "claude", "settings"));
        Directory.CreateDirectory(Path.Combine(scope.RootPath, "mcp", "manifest"));

        await File.WriteAllTextAsync(
            Path.Combine(scope.RootPath, "claude", "settings", "data-ops.settings.json"),
            "{}");
        await File.WriteAllTextAsync(
            Path.Combine(scope.RootPath, "mcp", "manifest", "research.json"),
            """
            {
              "mcpServers": {}
            }
            """);

        var service = CreateService(scope.RootPath);

        _ = await service.LoadAsync();

        Assert.True(Directory.Exists(Path.Combine(GetCompanySourceRoot(scope.RootPath), "profiles", "data-ops")));
        Assert.True(Directory.Exists(Path.Combine(GetCompanySourceRoot(scope.RootPath), "profiles", "research")));
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

    private static int SortProfileId(string profileId) => profileId switch
    {
        var value when string.Equals(value, WorkspaceProfiles.GlobalId, StringComparison.OrdinalIgnoreCase) => 0,
        var value when string.Equals(value, WorkspaceProfiles.FrontendId, StringComparison.OrdinalIgnoreCase) => 1,
        var value when string.Equals(value, WorkspaceProfiles.BackendId, StringComparison.OrdinalIgnoreCase) => 2,
        _ => 10
    };

    private static int SortProfileDisplay(string displayName) => displayName switch
    {
        var value when string.Equals(value, WorkspaceProfiles.GlobalDisplayName, StringComparison.OrdinalIgnoreCase) => 0,
        var value when string.Equals(value, WorkspaceProfiles.FrontendDisplayName, StringComparison.OrdinalIgnoreCase) => 1,
        var value when string.Equals(value, WorkspaceProfiles.BackendDisplayName, StringComparison.OrdinalIgnoreCase) => 2,
        _ => 10
    };
}
