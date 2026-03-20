using System.Text.RegularExpressions;
using AIHub.Contracts;

namespace AIHub.Application.Services;

public sealed partial class SkillsCatalogService
{
    private static readonly Regex SemanticVersionTagRegex = new(
        "^v?(?<major>0|[1-9]\\d*)\\.(?<minor>0|[1-9]\\d*)\\.(?<patch>0|[1-9]\\d*)(?<suffix>-[0-9A-Za-z.-]+)?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public async Task<OperationResult> CheckSourceVersionsAsync(
        string localName,
        string profile,
        CancellationToken cancellationToken = default)
    {
        var resolution = await _hubRootLocator.ResolveAsync(cancellationToken);
        if (!resolution.IsValid || string.IsNullOrWhiteSpace(resolution.RootPath))
        {
            return OperationResult.Fail("AI-Hub 根目录无效，无法检查 Skills 版本。", string.Join(Environment.NewLine, resolution.Errors));
        }

        var profileId = WorkspaceProfiles.NormalizeId(profile);
        var sources = (await LoadSourcesAsync(resolution.RootPath, cancellationToken)).ToList();
        var source = sources.FirstOrDefault(item => MatchesSource(item, localName, profileId));
        if (source is null)
        {
            return OperationResult.Fail("未找到要检查版本的 Skills 来源。", localName);
        }

        var updatedSource = await RefreshSourceVersionMetadataAsync(source, refreshRemote: true, cancellationToken);
        UpsertSourceRecord(sources, updatedSource);
        await SaveSourcesAsync(resolution.RootPath, sources, cancellationToken);

        return OperationResult.Ok(
            "Skills 版本检查已完成。",
            BuildSourceVersionDetails(updatedSource));
    }

    public async Task<OperationResult> UpgradeSourceVersionAsync(
        string localName,
        string profile,
        CancellationToken cancellationToken = default)
    {
        var resolution = await _hubRootLocator.ResolveAsync(cancellationToken);
        if (!resolution.IsValid || string.IsNullOrWhiteSpace(resolution.RootPath))
        {
            return OperationResult.Fail("AI-Hub 根目录无效，无法升级 Skills 来源。", string.Join(Environment.NewLine, resolution.Errors));
        }

        var profileId = WorkspaceProfiles.NormalizeId(profile);
        var sources = (await LoadSourcesAsync(resolution.RootPath, cancellationToken)).ToList();
        var source = sources.FirstOrDefault(item => MatchesSource(item, localName, profileId));
        if (source is null)
        {
            return OperationResult.Fail("未找到要升级的 Skills 来源。", localName);
        }

        var refreshedSource = await RefreshSourceVersionMetadataAsync(source, refreshRemote: true, cancellationToken);
        var targetTag = ResolveUpgradeTargetTag(refreshedSource);
        if (string.IsNullOrWhiteSpace(targetTag))
        {
            UpsertSourceRecord(sources, refreshedSource);
            await SaveSourcesAsync(resolution.RootPath, sources, cancellationToken);
            return OperationResult.Ok("当前来源没有可升级的稳定标签。", BuildSourceVersionDetails(refreshedSource));
        }

        if (string.Equals(refreshedSource.Reference, targetTag, StringComparison.OrdinalIgnoreCase) && !refreshedSource.HasPendingVersionUpgrade)
        {
            UpsertSourceRecord(sources, refreshedSource);
            await SaveSourcesAsync(resolution.RootPath, sources, cancellationToken);
            return OperationResult.Ok("当前来源已经固定在目标版本。", BuildSourceVersionDetails(refreshedSource));
        }

        var upgradedSource = refreshedSource with
        {
            Reference = targetTag,
            ResolvedVersionTag = targetTag,
            HasPendingVersionUpgrade = false,
            LastScheduledResult = "已切换到版本标签 " + targetTag
        };
        UpsertSourceRecord(sources, upgradedSource);
        await SaveSourcesAsync(resolution.RootPath, sources, cancellationToken);

        var installs = await LoadInstallsAsync(resolution.RootPath, cancellationToken);
        var targetInstalls = installs
            .Where(install => install.CustomizationMode is SkillCustomizationMode.Managed or SkillCustomizationMode.Overlay)
            .Where(install => MatchesSource(upgradedSource, install.SourceLocalName, install.SourceProfile))
            .OrderBy(install => GetProfileSortOrder(install.Profile))
            .ThenBy(install => install.Profile, StringComparer.OrdinalIgnoreCase)
            .ThenBy(install => install.InstalledRelativePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var updated = 0;
        var blocked = 0;
        var failed = 0;
        var detailLines = new List<string>
        {
            "版本切换：" + refreshedSource.Reference + " -> " + targetTag,
            BuildSourceVersionDetails(upgradedSource)
        };

        foreach (var install in targetInstalls)
        {
            var result = await SyncInstalledSkillAsync(install.Profile, install.InstalledRelativePath, force: false, cancellationToken);
            if (result.Success)
            {
                updated++;
            }
            else if (!string.IsNullOrWhiteSpace(result.Message) && result.Message.Contains("本地", StringComparison.OrdinalIgnoreCase))
            {
                blocked++;
            }
            else
            {
                failed++;
            }

            detailLines.Add($"{install.Name} ({WorkspaceProfiles.ToDisplayName(install.Profile)})");
            detailLines.Add(result.Message);
            if (!string.IsNullOrWhiteSpace(result.Details))
            {
                detailLines.Add(result.Details);
            }
        }

        return failed == 0
            ? OperationResult.Ok(
                "来源版本升级已执行。",
                string.Join(Environment.NewLine + Environment.NewLine, detailLines) + Environment.NewLine + Environment.NewLine + $"升级结果：成功 {updated} / 阻塞 {blocked} / 失败 {failed}")
            : OperationResult.Fail(
                "来源版本升级过程中存在失败项。",
                string.Join(Environment.NewLine + Environment.NewLine, detailLines) + Environment.NewLine + Environment.NewLine + $"升级结果：成功 {updated} / 阻塞 {blocked} / 失败 {failed}");
    }

    private async Task<SkillSourceRecord> RefreshSourceVersionMetadataAsync(
        SkillSourceRecord source,
        bool refreshRemote,
        CancellationToken cancellationToken)
    {
        var normalizedSource = NormalizeSource(source);
        if (normalizedSource.Kind != SkillSourceKind.GitRepository)
        {
            return normalizedSource with
            {
                VersionTrackingMode = SkillVersionTrackingMode.FollowReferenceLegacy,
                ResolvedVersionTag = null,
                AvailableVersionTags = [],
                HasPendingVersionUpgrade = false
            };
        }

        var cacheDirectory = await EnsureGitWorkingCopyAsync(normalizedSource, refreshRemote, cancellationToken);
        var resolvedReference = await ResolveGitReferenceAsync(cacheDirectory, cancellationToken);
        var availableReferences = await GetAvailableReferencesAsync(
            new ResolvedSkillSource(cacheDirectory, ResolveCatalogRootPath(cacheDirectory, normalizedSource.CatalogPath), resolvedReference),
            normalizedSource,
            cancellationToken);
        var stableTags = ExtractStableTags(availableReferences);
        var resolvedVersionTag = await ResolveStableTagAtHeadAsync(cacheDirectory, cancellationToken)
            ?? (TryParseSemanticVersionTag(normalizedSource.Reference, includePrerelease: false, out _) ? normalizedSource.Reference.Trim() : null);
        var versionMode = NormalizeVersionTrackingMode(normalizedSource.Kind, normalizedSource.VersionTrackingMode, normalizedSource.PinnedTag, stableTags, allowLegacyFallbackWhenNoTags: true);
        var hasPendingUpgrade = versionMode switch
        {
            SkillVersionTrackingMode.FollowLatestStableTag => stableTags.Length > 0 && !string.Equals(resolvedVersionTag, stableTags[0], StringComparison.OrdinalIgnoreCase),
            SkillVersionTrackingMode.PinTag => !string.IsNullOrWhiteSpace(normalizedSource.PinnedTag) && !string.Equals(resolvedVersionTag, normalizedSource.PinnedTag, StringComparison.OrdinalIgnoreCase),
            _ => false
        };

        return normalizedSource with
        {
            VersionTrackingMode = versionMode,
            ResolvedVersionTag = resolvedVersionTag,
            AvailableVersionTags = stableTags,
            HasPendingVersionUpgrade = hasPendingUpgrade,
            AvailableReferences = availableReferences.ToArray(),
            LastScanReference = string.IsNullOrWhiteSpace(normalizedSource.LastScanReference) ? resolvedReference : normalizedSource.LastScanReference
        };
    }

    private static SkillVersionTrackingMode NormalizeVersionTrackingMode(
        SkillSourceKind kind,
        SkillVersionTrackingMode mode,
        string? pinnedTag,
        IReadOnlyList<string>? availableVersionTags,
        bool allowLegacyFallbackWhenNoTags)
    {
        if (kind != SkillSourceKind.GitRepository)
        {
            return SkillVersionTrackingMode.FollowReferenceLegacy;
        }

        if (!string.IsNullOrWhiteSpace(pinnedTag))
        {
            return mode == SkillVersionTrackingMode.FollowReferenceLegacy
                ? SkillVersionTrackingMode.PinTag
                : mode;
        }

        if (allowLegacyFallbackWhenNoTags && availableVersionTags is not null && availableVersionTags.Count == 0)
        {
            return SkillVersionTrackingMode.FollowReferenceLegacy;
        }

        return mode;
    }

    private static string[] ExtractStableTags(IEnumerable<string> references)
    {
        return references
            .Where(reference => TryParseSemanticVersionTag(reference, includePrerelease: false, out _))
            .Select(reference => NormalizeSemanticVersionTag(reference))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(reference => ParseSemanticVersionTag(reference)!.Value)
            .ToArray();
    }

    private static string? ResolveUpgradeTargetTag(SkillSourceRecord source)
    {
        return source.VersionTrackingMode switch
        {
            SkillVersionTrackingMode.PinTag => string.IsNullOrWhiteSpace(source.PinnedTag) ? null : source.PinnedTag,
            SkillVersionTrackingMode.FollowLatestStableTag => source.AvailableVersionTags.FirstOrDefault(),
            _ => null
        };
    }

    private static void UpsertSourceRecord(List<SkillSourceRecord> sources, SkillSourceRecord source)
    {
        sources.RemoveAll(item => MatchesSource(item, source.LocalName, source.Profile));
        sources.Add(NormalizeSource(source));
    }

    private static string BuildSourceVersionDetails(SkillSourceRecord source)
    {
        return string.Join(Environment.NewLine, new[]
        {
            "来源：" + source.SourceDisplayName,
            "策略：" + source.VersionTrackingDisplay,
            source.PinnedTagDisplay,
            source.ResolvedVersionDisplay,
            source.AvailableVersionSummary,
            source.PendingVersionUpgradeDisplay
        });
    }

    private static async Task<string?> ResolveStableTagAtHeadAsync(string cacheDirectory, CancellationToken cancellationToken)
    {
        try
        {
            var result = await RunProcessAsync(
                "git",
                ["-C", cacheDirectory, "tag", "--points-at", "HEAD"],
                workingDirectory: null,
                cancellationToken);
            if (result.ExitCode != 0)
            {
                return null;
            }

            return SplitLines(result.StandardOutput)
                .Select(line => line.Trim())
                .Where(line => TryParseSemanticVersionTag(line, includePrerelease: false, out _))
                .Select(NormalizeSemanticVersionTag)
                .OrderByDescending(line => ParseSemanticVersionTag(line)!.Value)
                .FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    private static bool TryParseSemanticVersionTag(string? value, bool includePrerelease, out SemanticVersionTag parsed)
    {
        parsed = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var match = SemanticVersionTagRegex.Match(value.Trim());
        if (!match.Success)
        {
            return false;
        }

        var suffix = match.Groups["suffix"].Value;
        if (!includePrerelease && !string.IsNullOrWhiteSpace(suffix))
        {
            return false;
        }

        parsed = new SemanticVersionTag(
            int.Parse(match.Groups["major"].Value),
            int.Parse(match.Groups["minor"].Value),
            int.Parse(match.Groups["patch"].Value),
            string.IsNullOrWhiteSpace(suffix) ? null : suffix);
        return true;
    }

    private static SemanticVersionTag? ParseSemanticVersionTag(string value)
    {
        return TryParseSemanticVersionTag(value, includePrerelease: true, out var parsed) ? parsed : null;
    }

    private static string NormalizeSemanticVersionTag(string value)
    {
        return value.Trim();
    }

    private readonly record struct SemanticVersionTag(int Major, int Minor, int Patch, string? Suffix) : IComparable<SemanticVersionTag>
    {
        public int CompareTo(SemanticVersionTag other)
        {
            var major = Major.CompareTo(other.Major);
            if (major != 0)
            {
                return major;
            }

            var minor = Minor.CompareTo(other.Minor);
            if (minor != 0)
            {
                return minor;
            }

            var patch = Patch.CompareTo(other.Patch);
            if (patch != 0)
            {
                return patch;
            }

            if (string.IsNullOrWhiteSpace(Suffix) && string.IsNullOrWhiteSpace(other.Suffix))
            {
                return 0;
            }

            if (string.IsNullOrWhiteSpace(Suffix))
            {
                return 1;
            }

            if (string.IsNullOrWhiteSpace(other.Suffix))
            {
                return -1;
            }

            return string.CompareOrdinal(Suffix, other.Suffix);
        }
    }
}
