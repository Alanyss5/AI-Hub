namespace AIHub.Application.Models;

public sealed record SkillScheduledUpdateBatchResult(
    IReadOnlyList<SkillScheduledUpdateSourceResult> Sources)
{
    public static SkillScheduledUpdateBatchResult Empty { get; } = new(Array.Empty<SkillScheduledUpdateSourceResult>());

    public bool HasWork => Sources.Any(item => item.HadWork);
}
