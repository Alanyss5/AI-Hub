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

        _boundViewModel = DataContext as MainWindowViewModel;
        if (_boundViewModel is not null)
        {
            _boundViewModel.ConfirmationHandler = ShowConfirmationAsync;
        }
    }

    private async Task<bool> ShowConfirmationAsync(ConfirmationRequest request)
    {
        var dialog = new ConfirmationDialogWindow(request);
        return await dialog.ShowDialog<bool>(this);
    }
}
