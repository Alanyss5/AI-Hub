using AIHub.Contracts;
using AIHub.Desktop.Services;
using AIHub.Desktop.Text;

namespace AIHub.Desktop.ViewModels;

public sealed partial class MainWindowViewModel
{
    private string _runtimeBuildIdentityDisplay = DesktopBuildInfo.BuildLabel + " / " + DesktopBuildInfo.Version;
    private string _runtimeExecutablePathDisplay = DesktopBuildInfo.ExecutablePath;
    private string _projectWorkspaceBindingModeDisplay = DefaultText.State.WorkspaceBindingNotSelected;
    private string _projectWorkspaceBindingDetails = DefaultText.State.WorkspaceBindingNotSelected;
    private string _projectWorkspaceBindingWarningDisplay = string.Empty;
    private bool _hasProjectWorkspaceBindingWarning;

    public Func<NoticeDialogRequest, Task>? NoticeDialogHandler { get; set; }

    public string RuntimeBuildIdentityDisplay
    {
        get => _runtimeBuildIdentityDisplay;
        private set => SetProperty(ref _runtimeBuildIdentityDisplay, value);
    }

    public string RuntimeExecutablePathDisplay
    {
        get => _runtimeExecutablePathDisplay;
        private set => SetProperty(ref _runtimeExecutablePathDisplay, value);
    }

    public string ProjectWorkspaceBindingModeDisplay
    {
        get => _projectWorkspaceBindingModeDisplay;
        private set => SetProperty(ref _projectWorkspaceBindingModeDisplay, value);
    }

    public string ProjectWorkspaceBindingDetails
    {
        get => _projectWorkspaceBindingDetails;
        private set => SetProperty(ref _projectWorkspaceBindingDetails, value);
    }

    public string ProjectWorkspaceBindingWarningDisplay
    {
        get => _projectWorkspaceBindingWarningDisplay;
        private set => SetProperty(ref _projectWorkspaceBindingWarningDisplay, value);
    }

    public bool HasProjectWorkspaceBindingWarning
    {
        get => _hasProjectWorkspaceBindingWarning;
        private set => SetProperty(ref _hasProjectWorkspaceBindingWarning, value);
    }

    private async Task ShowNoticeAsync(NoticeDialogRequest request)
    {
        var handler = NoticeDialogHandler;
        if (handler is null)
        {
            SetOperation(false, Text.State.AppNotReadyForNotice, request.Details);
            return;
        }

        await handler(request);
    }

    private bool TryCreateSelectedProjectPathMismatchNotice(string currentFormPath, out NoticeDialogRequest request)
    {
        request = default!;
        if (SelectedProject is null || string.IsNullOrWhiteSpace(currentFormPath))
        {
            return false;
        }

        var normalizedRegisteredPath = NormalizeDiagnosticPath(SelectedProject.Path);
        var normalizedFormPath = NormalizeDiagnosticPath(currentFormPath);
        if (string.Equals(normalizedRegisteredPath, normalizedFormPath, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        request = new NoticeDialogRequest(
            Text.Dialogs.ProjectPathMismatchTitle,
            Text.Dialogs.ProjectPathMismatchMessage,
            string.Join(Environment.NewLine, new[]
            {
                Text.State.RegisteredProjectPathLabel + normalizedRegisteredPath,
                Text.State.FormProjectPathLabel + normalizedFormPath,
                Text.State.NextStepLabel + Text.State.SaveProjectFirstStep
            }),
            Text.Dialogs.NoticeConfirmText);
        return true;
    }

    private void UpdateWorkspaceDiagnostics(ProjectRecord? project)
    {
        RuntimeBuildIdentityDisplay = DesktopBuildInfo.BuildLabel + " / " + DesktopBuildInfo.Version;
        RuntimeExecutablePathDisplay = DesktopBuildInfo.ExecutablePath;

        if (project is null)
        {
            ProjectWorkspaceBindingModeDisplay = Text.State.WorkspaceBindingNotSelected;
            ProjectWorkspaceBindingDetails = Text.State.WorkspaceBindingNotSelected;
            ProjectWorkspaceBindingWarningDisplay = string.Empty;
            HasProjectWorkspaceBindingWarning = false;
            return;
        }

        var entrypoints = new[]
        {
            CreateEntrypointSnapshot(project.Path, ".claude", "skills"),
            CreateEntrypointSnapshot(project.Path, ".agents", "skills"),
            CreateEntrypointSnapshot(project.Path, ".agent", "skills")
        };

        var hubRoot = ResolveDiagnosticHubRoot();
        var effectiveSkillsRoot = hubRoot is null
            ? null
            : Path.Combine(hubRoot, ".runtime", "effective", project.Profile.ToStorageValue(), "skills");
        var legacySkillsRoot = hubRoot is null
            ? null
            : Path.Combine(hubRoot, "skills", project.Profile.ToStorageValue());
        var mode = ClassifyBindingMode(entrypoints, effectiveSkillsRoot, legacySkillsRoot);

        ProjectWorkspaceBindingModeDisplay = mode switch
        {
            ProjectWorkspaceBindingMode.Effective => Text.State.WorkspaceBindingEffectiveMode,
            ProjectWorkspaceBindingMode.Legacy => Text.State.WorkspaceBindingLegacyMode,
            ProjectWorkspaceBindingMode.Mixed => Text.State.WorkspaceBindingMixedMode,
            _ => Text.State.WorkspaceBindingUnmanagedMode
        };

        ProjectWorkspaceBindingDetails = string.Join(Environment.NewLine, new[]
        {
            Text.State.DetailProjectPathLabel + project.Path,
            Text.State.CurrentProfileLabel + project.Profile.ToDisplayName(),
            Text.State.EffectiveOutputRootLabel + (effectiveSkillsRoot is null ? Text.State.NotSet : Path.GetDirectoryName(effectiveSkillsRoot)!),
            ".claude\\skills -> " + entrypoints[0].DisplayTarget,
            ".agents\\skills -> " + entrypoints[1].DisplayTarget,
            ".agent\\skills -> " + entrypoints[2].DisplayTarget
        });

        var warnings = new List<string>();
        if (TryCreateSelectedProjectPathMismatchNotice(ProjectPath, out _))
        {
            warnings.Add(Text.State.ProjectPathMismatchInlineWarning);
        }

        switch (mode)
        {
            case ProjectWorkspaceBindingMode.Legacy:
                warnings.Add(Text.State.WorkspaceBindingLegacyWarning);
                break;
            case ProjectWorkspaceBindingMode.Mixed:
                warnings.Add(Text.State.WorkspaceBindingMixedWarning);
                break;
            case ProjectWorkspaceBindingMode.Unmanaged:
                warnings.Add(Text.State.WorkspaceBindingUnmanagedWarning);
                break;
        }

        ProjectWorkspaceBindingWarningDisplay = string.Join(Environment.NewLine, warnings);
        HasProjectWorkspaceBindingWarning = warnings.Count > 0;
    }

    private static string NormalizeDiagnosticPath(string path)
    {
        try
        {
            return Path.GetFullPath(path.Trim()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return path.Trim();
        }
    }

    private string? ResolveDiagnosticHubRoot()
    {
        var candidate = string.IsNullOrWhiteSpace(HubRootDisplay) || string.Equals(HubRootDisplay, Text.State.HubRootNotResolved, StringComparison.Ordinal)
            ? HubRootInput
            : HubRootDisplay;

        if (string.IsNullOrWhiteSpace(candidate))
        {
            return null;
        }

        try
        {
            return Path.GetFullPath(candidate);
        }
        catch
        {
            return null;
        }
    }

    private static EntrypointSnapshot CreateEntrypointSnapshot(string projectPath, params string[] relativeSegments)
    {
        var entryPath = Path.Combine(new[] { projectPath }.Concat(relativeSegments).ToArray());
        if (!Directory.Exists(entryPath) && !File.Exists(entryPath))
        {
            return new EntrypointSnapshot(entryPath, null, DesktopTextCatalog.Default.State.WorkspaceBindingMissingTarget, EntrypointKind.Missing);
        }

        FileSystemInfo info = Directory.Exists(entryPath)
            ? new DirectoryInfo(entryPath)
            : new FileInfo(entryPath);

        if ((info.Attributes & FileAttributes.ReparsePoint) == 0)
        {
            return new EntrypointSnapshot(entryPath, null, DesktopTextCatalog.Default.State.WorkspaceBindingDirectDirectory, EntrypointKind.DirectDirectory);
        }

        var target = info.LinkTarget;
        if (string.IsNullOrWhiteSpace(target))
        {
            return new EntrypointSnapshot(entryPath, null, DesktopTextCatalog.Default.State.WorkspaceBindingMissingTarget, EntrypointKind.Missing);
        }

        var fullTarget = Path.IsPathRooted(target)
            ? Path.GetFullPath(target)
            : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(entryPath) ?? projectPath, target));
        return new EntrypointSnapshot(entryPath, fullTarget, fullTarget, EntrypointKind.Linked);
    }

    private static ProjectWorkspaceBindingMode ClassifyBindingMode(
        IReadOnlyList<EntrypointSnapshot> entrypoints,
        string? effectiveSkillsRoot,
        string? legacySkillsRoot)
    {
        if (entrypoints.All(item => item.Kind is EntrypointKind.Missing or EntrypointKind.DirectDirectory))
        {
            return ProjectWorkspaceBindingMode.Unmanaged;
        }

        if (!string.IsNullOrWhiteSpace(effectiveSkillsRoot)
            && entrypoints.All(item => item.Kind == EntrypointKind.Linked && PathsEqual(item.TargetPath, effectiveSkillsRoot)))
        {
            return ProjectWorkspaceBindingMode.Effective;
        }

        if (!string.IsNullOrWhiteSpace(legacySkillsRoot)
            && entrypoints.All(item => item.Kind == EntrypointKind.Linked && PathsEqual(item.TargetPath, legacySkillsRoot)))
        {
            return ProjectWorkspaceBindingMode.Legacy;
        }

        return ProjectWorkspaceBindingMode.Mixed;
    }

    private static bool PathsEqual(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return false;
        }

        return string.Equals(
            Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);
    }

    private enum ProjectWorkspaceBindingMode
    {
        Unmanaged,
        Effective,
        Legacy,
        Mixed
    }

    private enum EntrypointKind
    {
        Missing,
        DirectDirectory,
        Linked
    }

    private sealed record EntrypointSnapshot(
        string EntryPath,
        string? TargetPath,
        string DisplayTarget,
        EntrypointKind Kind);
}

public sealed record NoticeDialogRequest(
    string Title,
    string Message,
    string Details,
    string ConfirmText);
