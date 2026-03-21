using System.Collections.ObjectModel;
using System.Collections.Specialized;
using AIHub.Contracts;

namespace AIHub.Desktop.ViewModels;

public sealed class SkillsPageContextViewModel : ObservableObject
{
    private readonly MainWindowViewModel _vm;
    private readonly ObservableCollection<SkillsPageTargetOption> _targetOptions = new();
    private SkillsPageTargetOption? _selectedTarget;

    public SkillsPageContextViewModel(MainWindowViewModel vm)
    {
        _vm = vm;
        _vm.Projects.CollectionChanged += OnProjectsCollectionChanged;
        RebuildTargetOptions();
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
                _vm.ApplySkillsPageContextSelection(value?.Project);
            }
        }
    }

    public string CurrentContextDisplay
    {
        get
        {
            if (SelectedTarget?.Kind == SkillsPageTargetKind.Project && SelectedTarget.Project is not null)
            {
                return $"当前查看项目：{SelectedTarget.Project.Name} / {WorkspaceProfiles.ToDisplayName(SelectedTarget.Project.Profile)}";
            }

            return "当前查看全局 Skills";
        }
    }

    private void OnProjectsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RebuildTargetOptions();
    }

    private void RebuildTargetOptions()
    {
        var previousProjectPath = SelectedTarget?.Project?.Path;
        var options = new List<SkillsPageTargetOption>
        {
            new(SkillsPageTargetKind.Global, "全局")
        };

        options.AddRange(_vm.Projects.Select(project => new SkillsPageTargetOption(
            SkillsPageTargetKind.Project,
            $"{project.Name} / {WorkspaceProfiles.ToDisplayName(project.Profile)}",
            project)));

        ReplaceCollection(TargetOptions, options);

        SelectedTarget = options.FirstOrDefault(option =>
                             option.Project is not null &&
                             string.Equals(option.Project.Path, previousProjectPath, StringComparison.OrdinalIgnoreCase))
                         ?? options.First();
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
