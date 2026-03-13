namespace AIHub.Contracts;

public sealed record SkillSourceRecord
{
    public string LocalName { get; init; } = string.Empty;

    public ProfileKind Profile { get; init; } = ProfileKind.Global;

    public SkillSourceKind Kind { get; init; } = SkillSourceKind.GitRepository;

    public string Location { get; init; } = string.Empty;

    public string? CatalogPath { get; init; }

    public string Reference { get; init; } = "main";

    public bool IsEnabled { get; init; } = true;

    public bool AutoUpdate { get; init; } = true;

    public int? ScheduledUpdateIntervalHours { get; init; }

    public SkillScheduledUpdateAction ScheduledUpdateAction { get; init; } = SkillScheduledUpdateAction.CheckOnly;

    public DateTimeOffset? LastScheduledRunAt { get; init; }

    public string? LastScheduledResult { get; init; }

    public DateTimeOffset? LastScannedAt { get; init; }

    public string? LastScanReference { get; init; }

    public string[] LastDiscoveredSkills { get; init; } = [];

    public string[] AvailableReferences { get; init; } = [];

    public SkillVersionTrackingMode VersionTrackingMode { get; init; } = SkillVersionTrackingMode.FollowLatestStableTag;

    public string? PinnedTag { get; init; }

    public string? ResolvedVersionTag { get; init; }

    public string[] AvailableVersionTags { get; init; } = [];

    public bool HasPendingVersionUpgrade { get; init; }

    public string ProfileDisplay => Profile.ToDisplayName();

    public string KindDisplay => Kind.ToDisplayName();

    public string SourceDisplayName => $"{LocalName} / {ProfileDisplay}";

    public string LocationDisplay => string.IsNullOrWhiteSpace(Location) ? "未设置" : Location;

    public string ReferenceDisplay => Kind == SkillSourceKind.LocalDirectory ? "本地目录来源" : Reference;

    public string LastScanDisplay => LastScannedAt.HasValue
        ? "最近扫描：" + LastScannedAt.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")
        : "尚未扫描来源";

    public string DiscoveredSkillSummary => LastDiscoveredSkills.Length == 0
        ? "尚无扫描结果"
        : "发现 Skill：" + LastDiscoveredSkills.Length + " 个";

    public string AvailableReferenceSummary => AvailableReferences.Length == 0
        ? "尚未记录可用引用"
        : "可用引用：" + string.Join(" / ", AvailableReferences.Take(8)) + (AvailableReferences.Length > 8 ? " ..." : string.Empty);

    public bool ScheduledUpdateEnabled => AutoUpdate && ScheduledUpdateIntervalHours.HasValue;

    public string ScheduledUpdateIntervalDisplay => ScheduledUpdateIntervalHours switch
    {
        6 => "每 6 小时",
        12 => "每 12 小时",
        24 => "每 24 小时",
        168 => "每 7 天",
        _ => "关闭"
    };

    public string ScheduledUpdateActionDisplay => ScheduledUpdateAction switch
    {
        SkillScheduledUpdateAction.CheckAndSyncSafe => "检查并安全同步",
        _ => "仅检查"
    };

    public string ScheduledUpdateLastRunDisplay => LastScheduledRunAt.HasValue
        ? "最近定时执行：" + LastScheduledRunAt.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")
        : "尚未定时执行";

    public string ScheduledUpdateLastResultDisplay => string.IsNullOrWhiteSpace(LastScheduledResult)
        ? "尚无最近结果"
        : LastScheduledResult!;

    public string VersionTrackingDisplay => VersionTrackingMode switch
    {
        SkillVersionTrackingMode.PinTag => "固定标签",
        SkillVersionTrackingMode.FollowReferenceLegacy => "传统引用",
        _ => "跟踪最新稳定标签"
    };

    public string PinnedTagDisplay => string.IsNullOrWhiteSpace(PinnedTag)
        ? "未固定标签"
        : "固定标签：" + PinnedTag;

    public string ResolvedVersionDisplay => string.IsNullOrWhiteSpace(ResolvedVersionTag)
        ? "当前版本：未解析到稳定标签"
        : "当前版本：" + ResolvedVersionTag;

    public string AvailableVersionSummary => AvailableVersionTags.Length == 0
        ? "未发现可用稳定标签"
        : "稳定标签：" + string.Join(" / ", AvailableVersionTags.Take(6)) + (AvailableVersionTags.Length > 6 ? " ..." : string.Empty);

    public string PendingVersionUpgradeDisplay => HasPendingVersionUpgrade
        ? "检测到可升级版本"
        : "当前没有待升级版本";
}
