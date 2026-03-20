using System.Collections.ObjectModel;
using AIHub.Application.Models;
using AIHub.Contracts;

namespace AIHub.Desktop.ViewModels;

public sealed partial class MainWindowViewModel
{
    private const string AllSkillsFilterValue = "__all__";

    private readonly ObservableCollection<SkillBrowserFilterOption> _skillFilterOptions = new();
    private IReadOnlyList<InstalledSkillRecord> _installedSkillCache = Array.Empty<InstalledSkillRecord>();
    private IReadOnlyList<SkillSourceRecord> _skillSourceCache = Array.Empty<SkillSourceRecord>();
    private SkillBrowserFilterOption? _selectedSkillFilterOption;
    private string _skillSearchText = string.Empty;

    public ObservableCollection<SkillBrowserFilterOption> SkillFilterOptions => _skillFilterOptions;

    public SkillBrowserFilterOption? SelectedSkillFilterOption
    {
        get => _selectedSkillFilterOption;
        set
        {
            if (SetProperty(ref _selectedSkillFilterOption, value))
            {
                ApplySkillBrowserFilters();
            }
        }
    }

    public string SkillSearchText
    {
        get => _skillSearchText;
        set
        {
            if (SetProperty(ref _skillSearchText, value))
            {
                ApplySkillBrowserFilters();
            }
        }
    }

    private void InitializeSkillBrowserState()
    {
        RefreshSkillBrowserFilterOptions(_workspaceProfileCatalog);
    }

    private void RefreshSkillBrowserFilterOptions(IReadOnlyList<WorkspaceProfileRecord> profiles)
    {
        var selectedValue = SelectedSkillFilterOption?.Value ?? AllSkillsFilterValue;
        var options = new List<SkillBrowserFilterOption>
        {
            new(AllSkillsFilterValue, "全部 Skills")
        };
        options.AddRange(profiles
            .OrderBy(profile => profile.SortOrder)
            .ThenBy(profile => profile.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(profile => new SkillBrowserFilterOption(profile.Id, profile.DisplayName)));

        ReplaceCollection(SkillFilterOptions, options);
        SelectedSkillFilterOption = SkillFilterOptions.FirstOrDefault(option => string.Equals(option.Value, selectedValue, StringComparison.OrdinalIgnoreCase))
            ?? SkillFilterOptions.FirstOrDefault();
    }

    private void CacheSkillSnapshot(SkillCatalogSnapshot snapshot)
    {
        _installedSkillCache = snapshot.InstalledSkills;
        _skillSourceCache = snapshot.Sources;
    }

    private void ApplySkillBrowserFilters(string? preferredLocalName = null, string? preferredProfile = null)
    {
        var selectedFilter = SelectedSkillFilterOption?.Value ?? AllSkillsFilterValue;
        var includeAllProfiles = string.Equals(selectedFilter, AllSkillsFilterValue, StringComparison.OrdinalIgnoreCase);
        var search = SkillSearchText.Trim();

        var filteredSkills = _installedSkillCache
            .Where(skill => includeAllProfiles || string.Equals(skill.Profile, selectedFilter, StringComparison.OrdinalIgnoreCase))
            .Where(skill => MatchesSkillSearch(skill, search))
            .ToArray();
        var filteredSources = _skillSourceCache
            .Where(source => includeAllProfiles || string.Equals(source.Profile, selectedFilter, StringComparison.OrdinalIgnoreCase))
            .Where(source => MatchesSkillSourceSearch(source, search))
            .ToArray();

        ReplaceCollection(InstalledSkills, filteredSkills);
        ReplaceCollection(SkillSources, filteredSources);
        RefreshSkillGroups(filteredSkills);

        var registeredSkillCount = filteredSkills.Count(skill => skill.IsRegistered);
        SkillsSummaryDisplay = Text.State.InstalledSkillsSummary(filteredSkills.Length, registeredSkillCount);
        SkillSourcesSummaryDisplay = Text.State.SkillSourcesSummary(filteredSources.Length);

        var selectedInstalledSkill = FindInstalledSkill(filteredSkills, SelectedInstalledSkill?.RelativePath, SelectedInstalledSkill?.Profile)
            ?? filteredSkills.FirstOrDefault();
        SelectedInstalledSkill = selectedInstalledSkill;

        var selectedSource = FindSkillSource(filteredSources, preferredLocalName, preferredProfile)
            ?? FindSkillSource(filteredSources, SelectedSkillSource?.LocalName, SelectedSkillSource?.Profile)
            ?? FindSkillSource(filteredSources, SelectedSkillInstallSource?.LocalName, SelectedSkillInstallSource?.Profile)
            ?? filteredSources.FirstOrDefault();
        SelectedSkillSource = selectedSource;

        if (selectedSource is null)
        {
            ClearSkillSourceFormFields();
        }

        if (SelectedInstalledSkill is null)
        {
            SelectedSkillInstallSource = null;
        }
        else if (SelectedSkillInstallSource is not null)
        {
            SelectedSkillInstallSource = FindSkillSource(filteredSources, SelectedSkillInstallSource.LocalName, SelectedSkillInstallSource.Profile);
        }
    }

    private static bool MatchesSkillSearch(InstalledSkillRecord skill, string search)
    {
        if (string.IsNullOrWhiteSpace(search))
        {
            return true;
        }

        return ContainsIgnoreCase(skill.Name, search)
               || ContainsIgnoreCase(skill.RelativePath, search)
               || ContainsIgnoreCase(skill.ProfileDisplay, search)
               || ContainsIgnoreCase(skill.SourceDisplay, search);
    }

    private static bool MatchesSkillSourceSearch(SkillSourceRecord source, string search)
    {
        if (string.IsNullOrWhiteSpace(search))
        {
            return true;
        }

        return ContainsIgnoreCase(source.LocalName, search)
               || ContainsIgnoreCase(source.ProfileDisplay, search)
               || ContainsIgnoreCase(source.LocationDisplay, search)
               || ContainsIgnoreCase(source.ReferenceDisplay, search);
    }

    private static bool ContainsIgnoreCase(string? value, string search)
    {
        return !string.IsNullOrWhiteSpace(value)
               && value.Contains(search, StringComparison.OrdinalIgnoreCase);
    }
}
