using System.Collections.ObjectModel;
using AIHub.Contracts;
using AIHub.Desktop.Text;

namespace AIHub.Desktop.ViewModels;

public sealed partial class MainWindowViewModel
{
    private SkillVersionTrackingOption? _selectedSkillVersionTrackingOption;
    private string _skillSourcePinnedTag = string.Empty;

    public ObservableCollection<SkillVersionTrackingOption> SkillVersionTrackingOptions { get; } =
        new(CreateSkillVersionTrackingOptions(DefaultText));

    public AsyncDelegateCommand CheckSkillSourceVersionsCommand { get; private set; } = null!;

    public AsyncDelegateCommand UpgradeSkillSourceVersionsCommand { get; private set; } = null!;

    public SkillVersionTrackingOption? SelectedSkillVersionTrackingOption
    {
        get => _selectedSkillVersionTrackingOption;
        set => SetProperty(ref _selectedSkillVersionTrackingOption, value);
    }

    public string SkillSourcePinnedTag
    {
        get => _skillSourcePinnedTag;
        set => SetProperty(ref _skillSourcePinnedTag, value);
    }

    private void InitializeSkillVersioningState()
    {
        CheckSkillSourceVersionsCommand = new AsyncDelegateCommand(CheckSkillSourceVersionsAsync, CanUseSelectedSkillSourceVersioning);
        UpgradeSkillSourceVersionsCommand = new AsyncDelegateCommand(UpgradeSkillSourceVersionsAsync, CanUseSelectedSkillSourceVersioning);
        SelectedSkillVersionTrackingOption = SkillVersionTrackingOptions.FirstOrDefault(option => option.Value == SkillVersionTrackingMode.FollowLatestStableTag);
    }

    private void RaiseSkillVersioningCommandStates()
    {
        CheckSkillSourceVersionsCommand?.RaiseCanExecuteChanged();
        UpgradeSkillSourceVersionsCommand?.RaiseCanExecuteChanged();
    }

    private void ApplySkillSourceVersionState(SkillSourceRecord? source)
    {
        var mode = source?.VersionTrackingMode
                   ?? (SelectedSkillSourceKindOption?.Value == SkillSourceKind.LocalDirectory
                       ? SkillVersionTrackingMode.FollowReferenceLegacy
                       : SkillVersionTrackingMode.FollowLatestStableTag);
        SelectedSkillVersionTrackingOption = SkillVersionTrackingOptions.FirstOrDefault(option => option.Value == mode)
            ?? SkillVersionTrackingOptions.FirstOrDefault();
        SkillSourcePinnedTag = source?.PinnedTag ?? string.Empty;
        RaiseSkillVersioningCommandStates();
    }

    private async Task CheckSkillSourceVersionsAsync()
    {
        if (SelectedSkillSource is null)
        {
            SetOperation(false, Text.State.SelectSkillsSourceFirst, string.Empty);
            return;
        }

        await RunBusyAsync(async () =>
        {
            var result = await _skillsCatalogService!.CheckSourceVersionsAsync(
                SelectedSkillSource.LocalName,
                SelectedSkillSource.Profile);
            ApplyOperationResult(result);
            await LoadSkillsAsync(SelectedSkillSource.LocalName, SelectedSkillSource.Profile);
        });
    }

    private async Task UpgradeSkillSourceVersionsAsync()
    {
        if (SelectedSkillSource is null)
        {
            SetOperation(false, Text.State.SelectSkillsSourceFirst, string.Empty);
            return;
        }

        await RunBusyAsync(async () =>
        {
            var result = await _skillsCatalogService!.UpgradeSourceVersionAsync(
                SelectedSkillSource.LocalName,
                SelectedSkillSource.Profile);
            ApplyOperationResult(result);
            await LoadSkillsAsync(SelectedSkillSource.LocalName, SelectedSkillSource.Profile);
        });
    }

    private bool CanUseSelectedSkillSourceVersioning()
    {
        return !IsBusy && _skillsCatalogService is not null && SelectedSkillSource is not null;
    }

    private SkillVersionTrackingMode ResolveSelectedSkillVersionTrackingMode(SkillSourceKind sourceKind)
    {
        if (sourceKind == SkillSourceKind.LocalDirectory)
        {
            return SkillVersionTrackingMode.FollowReferenceLegacy;
        }

        return SelectedSkillVersionTrackingOption?.Value ?? SkillVersionTrackingMode.FollowLatestStableTag;
    }

    private static IReadOnlyList<SkillVersionTrackingOption> CreateSkillVersionTrackingOptions(DesktopTextCatalog text)
    {
        return
        [
            new SkillVersionTrackingOption(SkillVersionTrackingMode.FollowLatestStableTag, text.Skills.VersionTrackingFollowLatestStableTagOption),
            new SkillVersionTrackingOption(SkillVersionTrackingMode.PinTag, text.Skills.VersionTrackingPinTagOption),
            new SkillVersionTrackingOption(SkillVersionTrackingMode.FollowReferenceLegacy, text.Skills.VersionTrackingFollowReferenceLegacyOption)
        ];
    }
}
