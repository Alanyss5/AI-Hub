namespace AIHub.Desktop.ViewModels;

public sealed class ProjectsViewModel : ObservableObject
{
    public ProjectsViewModel(MainWindowViewModel vm)
    {
        Vm = vm;
        Vm.PropertyChanged += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.PropertyName))
            {
                RaisePropertyChanged(e.PropertyName);
            }
        };
    }

    public MainWindowViewModel Vm { get; }
}
