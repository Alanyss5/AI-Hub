namespace AIHub.Desktop.ViewModels;

public sealed class ProfileBindingOption : ObservableObject
{
    private bool _isSelected;

    public ProfileBindingOption(string profileId, string displayName, bool isSelected = false)
    {
        ProfileId = profileId;
        DisplayName = displayName;
        _isSelected = isSelected;
    }

    public string ProfileId { get; }

    public string DisplayName { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}
