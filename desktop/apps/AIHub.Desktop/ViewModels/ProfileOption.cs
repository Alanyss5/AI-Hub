using AIHub.Contracts;

namespace AIHub.Desktop.ViewModels;

public sealed class ProfileOption
{
    public ProfileOption(ProfileKind value)
    {
        Value = value;
        DisplayName = value.ToDisplayName();
    }

    public ProfileKind Value { get; }

    public string DisplayName { get; }
}
