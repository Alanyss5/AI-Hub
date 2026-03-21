namespace AIHub.Desktop.ViewModels;

public sealed class McpViewModel : ObservableObject
{
    public McpViewModel(MainWindowViewModel vm, McpPageContextViewModel context)
    {
        Vm = vm;
        Context = context;
    }

    public MainWindowViewModel Vm { get; }

    public McpPageContextViewModel Context { get; }
}
