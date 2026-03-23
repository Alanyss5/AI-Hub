namespace AIHub.Application.Models;

public enum BindingSourceKind
{
    None,
    Category,
    Library
}

public enum BindingResolutionStatus
{
    Resolved,
    Unresolvable,
    Ambiguous
}

public sealed record BindingResolutionPreview(
    BindingResolutionStatus ResolutionStatus,
    string ResolutionReason,
    BindingSourceKind ContentDonorKind,
    string ContentDonorProfileId,
    BindingSourceKind PrimaryDestinationKind,
    string PrimaryDestinationProfileId,
    IReadOnlyList<string> MaterializedProfileIds,
    IReadOnlyList<string> MaterializedMemberPaths)
{
    // Front-end source semantics follow the metadata donor unless preview is
    // using synthesized metadata derived from equivalent physical mirrors.
    public BindingSourceKind SourceKind => UsesSyntheticMetadataSource
        ? ContentDonorKind
        : MetadataDonorKind;

    public string SourceProfileId => UsesSyntheticMetadataSource
        ? ContentDonorProfileId
        : MetadataDonorProfileId;

    public bool UsesSyntheticMetadataSource { get; init; }

    public BindingSourceKind MetadataDonorKind { get; init; }

    public string MetadataDonorProfileId { get; init; } = string.Empty;

    public IReadOnlyList<string> RefreshedProfileIds { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> RemovedProfileIds { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> MaterializedTargetProfiles => MaterializedProfileIds;
}
