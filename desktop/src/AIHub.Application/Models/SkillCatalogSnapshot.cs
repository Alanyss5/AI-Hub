using AIHub.Contracts;

namespace AIHub.Application.Models;

public sealed record SkillCatalogSnapshot(
    HubRootResolution Resolution,
    IReadOnlyList<InstalledSkillRecord> InstalledSkills,
    IReadOnlyList<SkillSourceRecord> Sources);
