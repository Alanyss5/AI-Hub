using AIHub.Desktop.ViewModels;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace AIHub.Desktop;

public partial class MainWindow : Window
{
    private MainWindowViewModel? _boundViewModel;

    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_boundViewModel is not null && ReferenceEquals(_boundViewModel.ConfirmationHandler, (Func<ConfirmationRequest, Task<bool>>)ShowConfirmationAsync))
        {
            _boundViewModel.ConfirmationHandler = null;
        }

        if (_boundViewModel is not null && ReferenceEquals(_boundViewModel.WorkspaceOnboardingDialogHandler, (Func<WorkspaceOnboardingDialogRequest, Task<WorkspaceOnboardingDialogResult?>>)ShowWorkspaceOnboardingAsync))
        {
            _boundViewModel.WorkspaceOnboardingDialogHandler = null;
        }

        if (_boundViewModel is not null && ReferenceEquals(_boundViewModel.NoticeDialogHandler, (Func<NoticeDialogRequest, Task>)ShowNoticeAsync))
        {
            _boundViewModel.NoticeDialogHandler = null;
        }

        _boundViewModel = DataContext as MainWindowViewModel;
        if (_boundViewModel is not null)
        {
            _boundViewModel.ConfirmationHandler = ShowConfirmationAsync;
            _boundViewModel.WorkspaceOnboardingDialogHandler = ShowWorkspaceOnboardingAsync;
            _boundViewModel.NoticeDialogHandler = ShowNoticeAsync;
        }
    }

    private async Task<bool> ShowConfirmationAsync(ConfirmationRequest request)
    {
        var dialog = new ConfirmationDialogWindow(request);
        return await dialog.ShowDialog<bool>(this);
    }

    private async Task<WorkspaceOnboardingDialogResult?> ShowWorkspaceOnboardingAsync(WorkspaceOnboardingDialogRequest request)
    {
        var dialog = new WorkspaceOnboardingDialogWindow(request);
        return await dialog.ShowDialog<WorkspaceOnboardingDialogResult?>(this);
    }

    private async Task ShowNoticeAsync(NoticeDialogRequest request)
    {
        var dialog = new NoticeDialogWindow(request);
        await dialog.ShowDialog(this);
    }
}
