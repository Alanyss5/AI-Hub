using AIHub.Contracts;

namespace AIHub.Desktop.ViewModels;

public sealed record SkillSourceKindOption(
    SkillSourceKind Value,
    string DisplayName,
    string Description);