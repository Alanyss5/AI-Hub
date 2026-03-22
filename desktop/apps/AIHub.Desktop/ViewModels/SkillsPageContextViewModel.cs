using System.Collections.ObjectModel;
using AIHub.Contracts;

namespace AIHub.Desktop.ViewModels;

public sealed class SkillsPageContextViewModel : ObservableObject
{
    private readonly MainWindowViewModel _vm;
    private readonly ObservableCollection<SkillsPageTargetOption> _targetOptions = new();
    private SkillsPageTargetOption? _selectedTarget;

    public SkillsPageContextViewModel(MainWindowViewModel vm, IReadOnlyList<WorkspaceProfileRecord> profiles)
    {
        _vm = vm;
        UpdateProfiles(profiles);
    }

    public ObservableCollection<SkillsPageTargetOption> TargetOptions => _targetOptions;

    public SkillsPageTargetOption? SelectedTarget
    {
        get => _selectedTarget;
        set
        {
            if (SetProperty(ref _selectedTarget, value))
            {
                RaisePropertyChanged(nameof(CurrentContextDisplay));
                _vm.ApplySkillsPageContextSelection(value?.ProfileId);
            }
        }
    }

    public string CurrentContextDisplay => $"当前分类：{SelectedTarget?.DisplayName ?? WorkspaceProfiles.GlobalDisplayName}";

    public void UpdateProfiles(IReadOnlyList<WorkspaceProfileRecord> profiles)
    {
        var previousProfileId = SelectedTarget?.ProfileId ?? WorkspaceProfiles.GlobalId;
        var profileCatalog = profiles.Count == 0 ? WorkspaceProfiles.CreateDefaultCatalog() : profiles;
        var options = profileCatalog
            .OrderBy(profile => profile.SortOrder)
            .ThenBy(profile => profile.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(profile => new SkillsPageTargetOption(
                string.Equals(profile.Id, WorkspaceProfiles.GlobalId, StringComparison.OrdinalIgnoreCase)
                    ? SkillsPageTargetKind.Global
                    : SkillsPageTargetKind.Category,
                profile.Id,
                profile.DisplayName))
            .ToArray();

        ReplaceCollection(TargetOptions, options);
        var nextTarget = options.FirstOrDefault(option => string.Equals(option.ProfileId, previousProfileId, StringComparison.OrdinalIgnoreCase))
                         ?? options.FirstOrDefault();
        if (_selectedTarget is not null
            && nextTarget is not null
            && string.Equals(_selectedTarget.ProfileId, nextTarget.ProfileId, StringComparison.OrdinalIgnoreCase))
        {
            _selectedTarget = nextTarget;
            RaisePropertyChanged(nameof(SelectedTarget));
            RaisePropertyChanged(nameof(CurrentContextDisplay));
            return;
        }

        SelectedTarget = nextTarget;
    }

    private static void ReplaceCollection<T>(ObservableCollection<T> target, IEnumerable<T> source)
    {
        target.Clear();
        foreach (var item in source)
        {
            target.Add(item);
        }
    }
}
