using System.Reflection;
using AIHub.Application.Models;
using AIHub.Contracts;
using AIHub.Desktop.ViewModels;

namespace AIHub.Application.Tests;

public sealed class MainWindowBindingImpactFailureTests
{
    private const string BlockedImpactPrefix = "影响说明：当前保存会被阻止";
    private const string NoProfileMutationPhrase = "不会改动任何 profile";

    [Fact]
    public void Failed_Skill_Preview_Blocks_Current_And_Selected_Impact_Displays()
    {
        var viewModel = CreateSkillBindingViewModel();

        SetPrivateField(
            viewModel,
            "_pendingSkillBindingPreviewState",
            ParsePreviewState("Failed"));

        var currentImpact = viewModel.CurrentSkillBindingImpactDisplay;
        var selectedImpact = viewModel.SelectedBindingTargetsImpactDisplay;

        AssertBlockedImpact(currentImpact, "保存后上游来源解析失败，请稍后重试。");
        Assert.Equal(currentImpact, selectedImpact);
        Assert.DoesNotContain("BackendImpactProject", currentImpact, StringComparison.Ordinal);
        Assert.DoesNotContain(WorkspaceProfiles.BackendDisplayName, currentImpact, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(nameof(BindingResolutionStatus.Unresolvable), "No usable physical skill mirror exists for the requested binding.")]
    [InlineData(nameof(BindingResolutionStatus.Ambiguous), "Multiple target mirrors disagree; select a single usable donor first.")]
    public void NonResolved_Skill_Preview_Blocks_Current_And_Selected_Impact_Displays(string resolutionStatusName, string reason)
    {
        var viewModel = CreateSkillBindingViewModel();

        SetPrivateField(
            viewModel,
            "_pendingSkillBindingResolution",
            CreateBlockedPreview(Enum.Parse<BindingResolutionStatus>(resolutionStatusName), reason));
        SetPrivateField(
            viewModel,
            "_pendingSkillBindingPreviewState",
            ParsePreviewState("Resolved"));

        var currentImpact = viewModel.CurrentSkillBindingImpactDisplay;
        var selectedImpact = viewModel.SelectedBindingTargetsImpactDisplay;

        AssertBlockedImpact(currentImpact, reason);
        Assert.Equal(currentImpact, selectedImpact);
        Assert.DoesNotContain("BackendImpactProject", currentImpact, StringComparison.Ordinal);
        Assert.DoesNotContain(WorkspaceProfiles.BackendDisplayName, currentImpact, StringComparison.Ordinal);
    }

    [Fact]
    public void Failed_Skill_Group_Preview_Blocks_Current_And_Selected_Impact_Displays()
    {
        var viewModel = CreateSkillGroupBindingViewModel();

        SetPrivateField(
            viewModel,
            "_pendingSkillGroupBindingPreviewState",
            ParsePreviewState("Failed"));

        var currentImpact = viewModel.CurrentSkillGroupBindingImpactDisplay;
        var selectedImpact = viewModel.SelectedBindingTargetsImpactDisplay;

        AssertBlockedImpact(currentImpact, "保存后上游来源解析失败，请稍后重试。");
        Assert.Equal(currentImpact, selectedImpact);
        Assert.DoesNotContain("BackendImpactProject", currentImpact, StringComparison.Ordinal);
        Assert.DoesNotContain(WorkspaceProfiles.BackendDisplayName, currentImpact, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(nameof(BindingResolutionStatus.Unresolvable), "No usable physical skill group mirror exists for the requested binding.")]
    [InlineData(nameof(BindingResolutionStatus.Ambiguous), "Multiple target group mirrors disagree; select a single usable donor first.")]
    public void NonResolved_Skill_Group_Preview_Blocks_Current_And_Selected_Impact_Displays(string resolutionStatusName, string reason)
    {
        var viewModel = CreateSkillGroupBindingViewModel();

        SetPrivateField(
            viewModel,
            "_pendingSkillGroupBindingResolution",
            CreateBlockedPreview(Enum.Parse<BindingResolutionStatus>(resolutionStatusName), reason));
        SetPrivateField(
            viewModel,
            "_pendingSkillGroupBindingPreviewState",
            ParsePreviewState("Resolved"));

        var currentImpact = viewModel.CurrentSkillGroupBindingImpactDisplay;
        var selectedImpact = viewModel.SelectedBindingTargetsImpactDisplay;

        AssertBlockedImpact(currentImpact, reason);
        Assert.Equal(currentImpact, selectedImpact);
        Assert.DoesNotContain("BackendImpactProject", currentImpact, StringComparison.Ordinal);
        Assert.DoesNotContain(WorkspaceProfiles.BackendDisplayName, currentImpact, StringComparison.Ordinal);
    }

    private static MainWindowViewModel CreateSkillBindingViewModel()
    {
        var viewModel = new MainWindowViewModel();
        viewModel.Projects.Add(new ProjectRecord("BackendImpactProject", "C:/backend-impact-project", WorkspaceProfiles.BackendId));
        viewModel.Projects.Add(new ProjectRecord("FrontendImpactProject", "C:/frontend-impact-project", WorkspaceProfiles.FrontendId));
        viewModel.SelectedInstalledSkill = new InstalledSkillRecord
        {
            Name = "demo-skill",
            Profile = WorkspaceProfiles.GlobalId,
            RelativePath = "demo-skill",
            BindingProfileIds = new[] { WorkspaceProfiles.FrontendId },
            BindingDisplayTags = new[] { WorkspaceProfiles.FrontendDisplayName },
            IsRegistered = true
        };
        viewModel.SelectedSkillsBindingEditorIndex = 0;

        viewModel.SkillBindingProfiles.Single(option => option.ProfileId == WorkspaceProfiles.GlobalId).IsSelected = false;
        viewModel.SkillBindingProfiles.Single(option => option.ProfileId == WorkspaceProfiles.FrontendId).IsSelected = false;
        viewModel.SkillBindingProfiles.Single(option => option.ProfileId == WorkspaceProfiles.BackendId).IsSelected = true;
        return viewModel;
    }

    private static MainWindowViewModel CreateSkillGroupBindingViewModel()
    {
        var viewModel = new MainWindowViewModel();
        viewModel.Projects.Add(new ProjectRecord("BackendImpactProject", "C:/backend-impact-project", WorkspaceProfiles.BackendId));
        viewModel.Projects.Add(new ProjectRecord("FrontendImpactProject", "C:/frontend-impact-project", WorkspaceProfiles.FrontendId));
        viewModel.SelectedSkillGroup = new SkillFolderGroupItem(
            "superpowers",
            new[]
            {
                new InstalledSkillRecord
                {
                    Name = "brainstorming",
                    Profile = WorkspaceProfiles.GlobalId,
                    RelativePath = "superpowers/brainstorming",
                    BindingProfileIds = new[] { WorkspaceProfiles.FrontendId },
                    BindingDisplayTags = new[] { WorkspaceProfiles.FrontendDisplayName },
                    IsRegistered = true
                }
            },
            new[] { WorkspaceProfiles.GlobalId },
            new[] { WorkspaceProfiles.FrontendId },
            new[] { WorkspaceProfiles.FrontendDisplayName },
            new[] { "superpowers/brainstorming" });
        viewModel.SelectedSkillsBindingEditorIndex = 1;

        viewModel.SkillGroupBindingProfiles.Single(option => option.ProfileId == WorkspaceProfiles.GlobalId).IsSelected = false;
        viewModel.SkillGroupBindingProfiles.Single(option => option.ProfileId == WorkspaceProfiles.FrontendId).IsSelected = false;
        viewModel.SkillGroupBindingProfiles.Single(option => option.ProfileId == WorkspaceProfiles.BackendId).IsSelected = true;
        return viewModel;
    }

    private static BindingResolutionPreview CreateBlockedPreview(BindingResolutionStatus status, string reason)
    {
        return new BindingResolutionPreview(
            status,
            reason,
            BindingSourceKind.None,
            string.Empty,
            BindingSourceKind.None,
            string.Empty,
            Array.Empty<string>(),
            Array.Empty<string>());
    }

    private static object ParsePreviewState(string stateName)
    {
        var previewStateType = typeof(MainWindowViewModel).GetNestedType("BindingPreviewState", BindingFlags.NonPublic);
        Assert.NotNull(previewStateType);
        return Enum.Parse(previewStateType!, stateName);
    }

    private static void SetPrivateField(object target, string fieldName, object? value)
    {
        var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        field!.SetValue(target, value);
    }

    private static void AssertBlockedImpact(string impactDisplay, string reason)
    {
        Assert.Contains(BlockedImpactPrefix, impactDisplay, StringComparison.Ordinal);
        Assert.Contains(NoProfileMutationPhrase, impactDisplay, StringComparison.Ordinal);
        Assert.Contains(reason, impactDisplay, StringComparison.Ordinal);
        Assert.DoesNotContain("保存后会影响使用", impactDisplay, StringComparison.Ordinal);
        Assert.DoesNotContain("当前项目：", impactDisplay, StringComparison.Ordinal);
    }
}
