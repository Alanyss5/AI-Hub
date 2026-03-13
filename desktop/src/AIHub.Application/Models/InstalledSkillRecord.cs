using AIHub.Contracts;

namespace AIHub.Application.Models;

public sealed record InstalledSkillRecord
{
    public string Name { get; init; } = string.Empty;

    public ProfileKind Profile { get; init; } = ProfileKind.Global;

    public string DirectoryPath { get; init; } = string.Empty;

    public string RelativePath { get; init; } = string.Empty;

    public bool HasManifest { get; init; }

    public bool IsRegistered { get; init; }

    public SkillCustomizationMode CustomizationMode { get; init; } = SkillCustomizationMode.Local;

    public bool HasBaseline { get; init; }

    public bool IsDirty { get; init; }

    public string? SourceLocalName { get; init; }

    public ProfileKind? SourceProfile { get; init; }

    public string? SourceSkillPath { get; init; }

    public string BaselineDisplay { get; init; } = "尚未建立基线";

    public string StatusDisplay { get; init; } = "尚未登记来源与更新策略";

    public string LastSyncDisplay { get; init; } = "尚未同步来源";

    public string BackupSummaryDisplay { get; init; } = "尚无备份";

    public string RecentBackupsDisplay { get; init; } = "尚无备份历史";

    public IReadOnlyList<SkillBackupRecord> BackupRecords { get; init; } = Array.Empty<SkillBackupRecord>();

    public string ProfileDisplay => Profile.ToDisplayName();

    public string ManifestDisplay => HasManifest ? "已检测到 SKILL.md" : "目录中缺少 SKILL.md";

    public string RegistrationDisplay => IsRegistered ? "已登记" : "未登记";

    public string ModeDisplay => CustomizationMode.ToDisplayName();

    public string SourceDisplay
    {
        get
        {
            if (string.IsNullOrWhiteSpace(SourceLocalName))
            {
                return CustomizationMode == SkillCustomizationMode.Local ? "本地技能" : "未绑定来源";
            }

            var profileDisplay = SourceProfile.HasValue ? SourceProfile.Value.ToDisplayName() : "未指定";
            return $"{SourceLocalName} / {profileDisplay}";
        }
    }

    public string DirtyDisplay => HasBaseline ? (IsDirty ? "本地已修改" : "与基线一致") : "尚未建立基线";
}