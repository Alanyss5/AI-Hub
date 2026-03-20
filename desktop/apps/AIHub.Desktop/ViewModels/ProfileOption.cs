using AIHub.Contracts;

namespace AIHub.Desktop.ViewModels;

public sealed class ProfileOption
{
    public ProfileOption(string value, string? displayName = null)
    {
        Value = WorkspaceProfiles.NormalizeId(value);
        DisplayName = string.IsNullOrWhiteSpace(displayName)
            ? WorkspaceProfiles.ToDisplayName(Value)
            : displayName.Trim();
    }

    public string Value { get; }

    public string DisplayName { get; }
}
