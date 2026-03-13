using AIHub.Contracts;

namespace AIHub.Desktop.ViewModels;

public sealed record SkillModeOption(
    SkillCustomizationMode Value,
    string DisplayName,
    string Description);