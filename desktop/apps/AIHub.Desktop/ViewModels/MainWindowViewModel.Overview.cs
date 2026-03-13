using System.Collections.ObjectModel;
using AIHub.Application.Models;

namespace AIHub.Desktop.ViewModels;

public sealed partial class MainWindowViewModel
{
    public ObservableCollection<HubReadinessItem> ReadinessItems { get; } = new();

    public ObservableCollection<HubReadinessItem> RemainingGates { get; } = new();
}
