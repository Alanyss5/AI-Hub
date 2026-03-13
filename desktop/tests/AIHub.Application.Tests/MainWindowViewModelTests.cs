using AIHub.Contracts;
using AIHub.Desktop.ViewModels;

namespace AIHub.Application.Tests;

public sealed class MainWindowViewModelTests
{
    [Fact]
    public void SelectedSkillSourceReferenceOption_UpdatesSkillSourceReference()
    {
        var viewModel = new MainWindowViewModel();
        viewModel.SelectedSkillSource = new SkillSourceRecord
        {
            LocalName = "demo-source",
            Profile = ProfileKind.Global,
            Kind = SkillSourceKind.GitRepository,
            Location = "https://example.invalid/repo.git",
            Reference = "main",
            AvailableReferences = new[] { "main", "release" }
        };

        Assert.Equal("main", viewModel.SelectedSkillSourceReferenceOption);

        viewModel.SelectedSkillSourceReferenceOption = "release";

        Assert.Equal("release", viewModel.SkillSourceReference);
    }

    [Fact]
    public void SelectedSkillSourceKindOption_LocalDirectory_Forces_Legacy_Version_Mode()
    {
        var viewModel = new MainWindowViewModel();
        viewModel.SelectedSkillSourceKindOption = new SkillSourceKindOption(
            SkillSourceKind.LocalDirectory,
            "本地目录",
            "本地目录来源");

        var option = Assert.IsType<SkillVersionTrackingOption>(viewModel.SelectedSkillVersionTrackingOption);
        Assert.Equal(SkillVersionTrackingMode.FollowReferenceLegacy, option.Value);
    }
}
