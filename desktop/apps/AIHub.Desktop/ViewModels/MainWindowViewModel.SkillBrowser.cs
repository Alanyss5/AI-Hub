using System.Collections.ObjectModel;
using AIHub.Application.Models;
using AIHub.Contracts;

namespace AIHub.Desktop.ViewModels;

public sealed partial class MainWindowViewModel
{
    private const string AllSkillsFilterValue = "__all__";
    private const string UnboundSkillsFilterValue = "__unbound__";

    private readonly ObservableCollection<SkillBrowserFilterOption> _skillFilterOptions = new();
    private IReadOnlyList<InstalledSkillRecord> _installedSkillCache = Array.Empty<InstalledSkillRecord>();
    private IReadOnlyList<SkillSourceRecord> _skillSourceCache = Array.Empty<SkillSourceRecord>();
    private SkillBrowserFilterOption? _selectedSkillFilterOption;
    private string _skillSearchText = string.Empty;
    private bool _skillFilterFollowsContext = true;
    private bool _updatingSkillFilterFromContext;

    public ObservableCollection<SkillBrowserFilterOption> SkillFilterOptions => _skillFilterOptions;

    public SkillBrowserFilterOption? SelectedSkillFilterOption
    {
        get => _selectedSkillFilterOption;
        set
        {
            if (SetProperty(ref _selectedSkillFilterOption, value))
            {
                if (!_updatingSkillFilterFromContext)
                {
                    _skillFilterFollowsContext = string.Equals(
                        value?.Value,
                        GetSkillsContextDefaultFilterValue(),
                        StringComparison.OrdinalIgnoreCase);
                }

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

    internal void ApplySkillsPageContextSelection(ProjectRecord? project)
    {
        var filterValue = project?.Profile ?? WorkspaceProfiles.GlobalId;
        _skillFilterFollowsContext = true;
        SelectSkillFilter(filterValue, fromContext: true);
    }

    private void RefreshSkillBrowserFilterOptions(IReadOnlyList<WorkspaceProfileRecord> profiles)
    {
        var selectedValue = SelectedSkillFilterOption?.Value ?? AllSkillsFilterValue;
        var options = new List<SkillBrowserFilterOption>
        {
            new(AllSkillsFilterValue, "全部"),
            new(WorkspaceProfiles.GlobalId, WorkspaceProfiles.GlobalDisplayName),
            new(UnboundSkillsFilterValue, "未绑定")
        };

        options.AddRange(profiles
            .Where(profile => !string.Equals(profile.Id, WorkspaceProfiles.GlobalId, StringComparison.OrdinalIgnoreCase))
            .OrderBy(profile => profile.SortOrder)
            .ThenBy(profile => profile.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(profile => new SkillBrowserFilterOption(profile.Id, profile.DisplayName)));

        ReplaceCollection(SkillFilterOptions, options);
        SelectedSkillFilterOption = SkillFilterOptions.FirstOrDefault(option => string.Equals(option.Value, selectedValue, StringComparison.OrdinalIgnoreCase))
            ?? SkillFilterOptions.FirstOrDefault();
        if (_skillFilterFollowsContext && _skillsPageContext?.SelectedTarget is not null)
        {
            ApplySkillsPageContextSelection(_skillsPageContext.SelectedTarget.Project);
        }
    }

    private void CacheSkillSnapshot(SkillCatalogSnapshot snapshot)
    {
        _installedSkillCache = snapshot.InstalledSkills;
        _skillSourceCache = snapshot.Sources;
    }

    private void ApplySkillBrowserFilters(string? preferredLocalName = null, string? preferredProfile = null)
    {
        var selectedFilter = SelectedSkillFilterOption?.Value ?? AllSkillsFilterValue;
        var includeAll = string.Equals(selectedFilter, AllSkillsFilterValue, StringComparison.OrdinalIgnoreCase);
        var includeUnbound = string.Equals(selectedFilter, UnboundSkillsFilterValue, StringComparison.OrdinalIgnoreCase);
        var search = SkillSearchText.Trim();

        var filteredSkills = _installedSkillCache
            .Where(skill => MatchesSkillFilter(skill, selectedFilter, includeAll, includeUnbound))
            .Where(skill => MatchesSkillSearch(skill, search))
            .ToArray();
        var filteredSources = _skillSourceCache
            .Where(source => includeAll || includeUnbound || string.Equals(source.Profile, selectedFilter, StringComparison.OrdinalIgnoreCase))
            .Where(source => MatchesSkillSourceSearch(source, search))
            .ToArray();

        ReplaceCollection(InstalledSkills, filteredSkills);
        ReplaceCollection(SkillSources, filteredSources);
        RefreshSkillGroups(filteredSkills);

        var registeredSkillCount = filteredSkills.Count(skill => skill.IsRegistered);
        SkillsSummaryDisplay = Text.State.InstalledSkillsSummary(filteredSkills.Length, registeredSkillCount);
        SkillSourcesSummaryDisplay = Text.State.SkillSourcesSummary(filteredSources.Length);

        var selectedInstalledSkill = FindInstalledSkill(filteredSkills, SelectedInstalledSkill?.RelativePath)
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

    private void RefreshSkillGroups(IEnumerable<InstalledSkillRecord> skills)
    {
        var groupItems = skills
            .GroupBy(skill => GetSkillGroupRootPath(skill.RelativePath), StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var skillItems = group
                    .OrderBy(skill => skill.RelativePath, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                var sourceProfileIds = skillItems
                    .Select(skill => skill.Profile)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                var bindingProfileIds = skillItems
                    .SelectMany(skill => skill.BindingProfileIds)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(profile => profile, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                var bindingDisplayTags = skillItems
                    .SelectMany(skill => skill.BindingDisplayTags)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                return new SkillFolderGroupItem(
                    group.Key,
                    skillItems,
                    sourceProfileIds,
                    bindingProfileIds,
                    bindingDisplayTags,
                    skillItems.Select(skill => skill.RelativePath).ToArray());
            })
            .OrderBy(group => group.RelativeRootPath, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        ReplaceCollection(SkillGroups, groupItems);

        var preferredGroupPath = GetSkillGroupRootPath(SelectedInstalledSkill?.RelativePath)
            ?? SelectedSkillGroup?.RelativeRootPath;
        SelectedSkillGroup = FindSkillGroup(groupItems, preferredGroupPath) ?? groupItems.FirstOrDefault();
    }

    private string GetSkillsContextDefaultFilterValue()
    {
        return _skillsPageContext?.SelectedTarget?.Project?.Profile ?? WorkspaceProfiles.GlobalId;
    }

    private void SelectSkillFilter(string filterValue, bool fromContext = false)
    {
        var option = SkillFilterOptions.FirstOrDefault(item => string.Equals(item.Value, filterValue, StringComparison.OrdinalIgnoreCase));
        if (option is not null && !ReferenceEquals(SelectedSkillFilterOption, option))
        {
            var previous = _updatingSkillFilterFromContext;
            _updatingSkillFilterFromContext = fromContext;
            try
            {
                SelectedSkillFilterOption = option;
            }
            finally
            {
                _updatingSkillFilterFromContext = previous;
            }
        }
    }

    private static bool MatchesSkillFilter(InstalledSkillRecord skill, string selectedFilter, bool includeAll, bool includeUnbound)
    {
        if (includeAll)
        {
            return true;
        }

        if (includeUnbound)
        {
            return skill.IsUnbound;
        }

        return skill.BindingProfileIds.Contains(selectedFilter, StringComparer.OrdinalIgnoreCase);
    }

    private static bool MatchesSkillSearch(InstalledSkillRecord skill, string search)
    {
        if (string.IsNullOrWhiteSpace(search))
        {
            return true;
        }

        return ContainsIgnoreCase(skill.Name, search)
               || ContainsIgnoreCase(skill.RelativePath, search)
               || ContainsIgnoreCase(skill.BindingSummaryDisplay, search)
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
