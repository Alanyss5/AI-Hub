using System.Text.Json.Nodes;
using AIHub.Contracts;

namespace AIHub.Infrastructure;

public sealed partial class NativeWorkspaceAutomationService
{
    private List<ScannedCandidate> ScanGlobalCandidates(string hubRoot, string userHome, string personalRoot)
    {
        var candidates = new List<ScannedCandidate>();
        var dedupePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        AddSkillCandidates(
            candidates,
            dedupePaths,
            WorkspaceScope.Global,
            WorkspaceProfiles.GlobalId,
            null,
            hubRoot,
            personalRoot,
            new[]
            {
                new ScanRoot(Path.Combine(userHome, ".claude", "skills"), "claude"),
                new ScanRoot(Path.Combine(userHome, ".agents", "skills"), "agents"),
                new ScanRoot(Path.Combine(userHome, ".gemini", "antigravity", "skills"), "gemini"),
                new ScanRoot(Path.Combine(userHome, ".codex", "skills"), "codex")
            });

        AddFileCandidates(candidates, dedupePaths, WorkspaceOnboardingResourceKind.ClaudeCommand, WorkspaceScope.Global, WorkspaceProfiles.GlobalId, null, Path.Combine(userHome, ".claude", "commands"), "legacy", Path.Combine(hubRoot, "claude", "commands", "global"), Path.Combine(personalRoot, "claude", "commands", "global"));
        AddFileCandidates(candidates, dedupePaths, WorkspaceOnboardingResourceKind.ClaudeAgent, WorkspaceScope.Global, WorkspaceProfiles.GlobalId, null, Path.Combine(userHome, ".claude", "agents"), "legacy", Path.Combine(hubRoot, "claude", "agents", "global"), Path.Combine(personalRoot, "claude", "agents", "global"));
        AddSettingsCandidate(candidates, WorkspaceScope.Global, WorkspaceProfiles.GlobalId, null, hubRoot, personalRoot, Path.Combine(userHome, ".claude", "settings.json"));
        AddMcpCandidates(candidates, WorkspaceScope.Global, WorkspaceProfiles.GlobalId, null, hubRoot, personalRoot, new[]
        {
            new McpTarget("Claude", Path.Combine(userHome, ".claude.json"), ClientConfigFormat.Json),
            new McpTarget("Codex", Path.Combine(userHome, ".codex", "config.toml"), ClientConfigFormat.Toml),
            new McpTarget("Antigravity", Path.Combine(userHome, ".gemini", "antigravity", "mcp_config.json"), ClientConfigFormat.Json)
        });

        return candidates;
    }

    private List<ScannedCandidate> ScanProjectCandidates(string hubRoot, string userHome, string personalRoot, string projectPath, string profile)
    {
        var candidates = new List<ScannedCandidate>();
        var dedupePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        AddSkillCandidates(
            candidates,
            dedupePaths,
            WorkspaceScope.Project,
            profile,
            projectPath,
            hubRoot,
            personalRoot,
            new[]
            {
                new ScanRoot(Path.Combine(projectPath, ".claude", "skills"), "claude"),
                new ScanRoot(Path.Combine(projectPath, ".agents", "skills"), "agents"),
                new ScanRoot(Path.Combine(projectPath, ".agent", "skills"), "agent")
            });

        AddFileCandidates(candidates, dedupePaths, WorkspaceOnboardingResourceKind.ClaudeCommand, WorkspaceScope.Project, profile, projectPath, Path.Combine(projectPath, ".claude", "commands"), "project", Path.Combine(hubRoot, "claude", "commands", WorkspaceProfiles.NormalizeId(profile)), Path.Combine(personalRoot, "claude", "commands", WorkspaceProfiles.NormalizeId(profile)));
        AddFileCandidates(candidates, dedupePaths, WorkspaceOnboardingResourceKind.ClaudeAgent, WorkspaceScope.Project, profile, projectPath, Path.Combine(projectPath, ".claude", "agents"), "project", Path.Combine(hubRoot, "claude", "agents", WorkspaceProfiles.NormalizeId(profile)), Path.Combine(personalRoot, "claude", "agents", WorkspaceProfiles.NormalizeId(profile)));
        AddSettingsCandidate(candidates, WorkspaceScope.Project, profile, projectPath, hubRoot, personalRoot, Path.Combine(projectPath, ".claude", "settings.json"));
        AddMcpCandidates(candidates, WorkspaceScope.Project, profile, projectPath, hubRoot, personalRoot, new[]
        {
            new McpTarget("Claude", Path.Combine(projectPath, ".mcp.json"), ClientConfigFormat.Json),
            new McpTarget("Codex", Path.Combine(projectPath, ".codex", "config.toml"), ClientConfigFormat.Toml)
        });

        return candidates;
    }

    private void AddSkillCandidates(List<ScannedCandidate> candidates, HashSet<string> dedupePaths, WorkspaceScope scope, string profile, string? projectPath, string hubRoot, string personalRoot, IEnumerable<ScanRoot> roots)
    {
        var companyBase = Path.Combine(hubRoot, "skills", WorkspaceProfiles.NormalizeId(profile), "imported");
        var privateBase = Path.Combine(personalRoot, "skills", WorkspaceProfiles.NormalizeId(profile), "imported");

        foreach (var root in roots)
        {
            foreach (var filePath in EnumerateFilesSkippingReparsePoints(root.Path))
            {
                if (!string.Equals(Path.GetFileName(filePath), "SKILL.md", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var skillPath = Path.GetDirectoryName(filePath);
                if (string.IsNullOrWhiteSpace(skillPath))
                {
                    continue;
                }

                var normalizedSkillPath = NormalizePath(skillPath);
                if (!dedupePaths.Add(normalizedSkillPath))
                {
                    continue;
                }

                var rawRelativePath = Path.GetRelativePath(root.Path, normalizedSkillPath);
                var importRelativePath = NormalizeImportedRelativePath(rawRelativePath);
                if (string.IsNullOrWhiteSpace(importRelativePath))
                {
                    importRelativePath = Path.GetFileName(normalizedSkillPath);
                }

                var relativeTarget = Path.Combine(root.Label, importRelativePath);
                var suggestedTarget = DetermineSuggestedTarget(rawRelativePath, normalizedSkillPath);
                candidates.Add(new ScannedCandidate(
                    new WorkspaceOnboardingCandidate(
                        CreatePathCandidateId(WorkspaceOnboardingResourceKind.Skill, normalizedSkillPath),
                        WorkspaceOnboardingResourceKind.Skill,
                        Path.GetFileName(normalizedSkillPath),
                        normalizedSkillPath,
                        "Skill 目录：" + normalizedSkillPath,
                        Path.Combine(companyBase, relativeTarget),
                        Path.Combine(privateBase, relativeTarget),
                        Directory.Exists(Path.Combine(companyBase, relativeTarget)) || File.Exists(Path.Combine(companyBase, relativeTarget)),
                        Directory.Exists(Path.Combine(privateBase, relativeTarget)) || File.Exists(Path.Combine(privateBase, relativeTarget)),
                        suggestedTarget),
                    normalizedSkillPath,
                    true,
                    scope,
                    profile,
                    projectPath));
            }
        }
    }

    private void AddFileCandidates(List<ScannedCandidate> candidates, HashSet<string> dedupePaths, WorkspaceOnboardingResourceKind resourceKind, WorkspaceScope scope, string profile, string? projectPath, string rootPath, string importFolderName, string companyBaseRoot, string privateBaseRoot)
    {
        foreach (var filePath in EnumerateFilesSkippingReparsePoints(rootPath))
        {
            var normalizedFilePath = NormalizePath(filePath);
            if (!dedupePaths.Add(normalizedFilePath))
            {
                continue;
            }

            var rawRelativePath = Path.GetRelativePath(rootPath, normalizedFilePath);
            var importRelativePath = NormalizeImportedRelativePath(rawRelativePath);
            if (string.IsNullOrWhiteSpace(importRelativePath))
            {
                importRelativePath = Path.GetFileName(normalizedFilePath);
            }

            var suggestedTarget = DetermineSuggestedTarget(rawRelativePath, normalizedFilePath);
            var companyTarget = Path.Combine(companyBaseRoot, "imported", importFolderName, importRelativePath);
            var privateTarget = Path.Combine(privateBaseRoot, "imported", importFolderName, importRelativePath);
            candidates.Add(new ScannedCandidate(
                new WorkspaceOnboardingCandidate(
                    CreatePathCandidateId(resourceKind, normalizedFilePath),
                    resourceKind,
                    Path.GetFileName(normalizedFilePath),
                    normalizedFilePath,
                    SafeReadTextPreview(normalizedFilePath),
                    companyTarget,
                    privateTarget,
                    File.Exists(companyTarget) || Directory.Exists(companyTarget),
                    File.Exists(privateTarget) || Directory.Exists(privateTarget),
                    suggestedTarget),
                normalizedFilePath,
                false,
                scope,
                profile,
                projectPath));
        }
    }

    private void AddSettingsCandidate(List<ScannedCandidate> candidates, WorkspaceScope scope, string profile, string? projectPath, string hubRoot, string personalRoot, string sourcePath)
    {
        if (!File.Exists(sourcePath))
        {
            return;
        }

        var currentContent = File.ReadAllText(sourcePath);
        var effectiveContent = BuildEffectiveSettingsPreview(hubRoot, personalRoot, profile);
        if (string.Equals(NormalizeText(currentContent), NormalizeText(effectiveContent), StringComparison.Ordinal))
        {
            return;
        }

        var fileName = WorkspaceProfiles.NormalizeId(profile) + ".settings.json";
        candidates.Add(new ScannedCandidate(
            new WorkspaceOnboardingCandidate(
                CreatePathCandidateId(WorkspaceOnboardingResourceKind.ClaudeSettings, sourcePath),
                WorkspaceOnboardingResourceKind.ClaudeSettings,
                Path.GetFileName(sourcePath),
                NormalizePath(sourcePath),
                currentContent,
                Path.Combine(hubRoot, "claude", "settings", fileName),
                Path.Combine(personalRoot, "claude", "settings", fileName),
                File.Exists(Path.Combine(hubRoot, "claude", "settings", fileName)),
                File.Exists(Path.Combine(personalRoot, "claude", "settings", fileName))),
            NormalizePath(sourcePath),
            false,
            scope,
            profile,
            projectPath));
    }

    private static List<ScannedCandidate> SortCandidates(IEnumerable<ScannedCandidate> candidates)
    {
        return candidates
            .OrderBy(item => item.Candidate.ResourceKind)
            .ThenBy(item => item.Candidate.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Candidate.SourcePath, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IEnumerable<string> EnumerateFilesSkippingReparsePoints(string rootPath)
    {
        if (!Directory.Exists(rootPath) || IsReparsePoint(rootPath))
        {
            yield break;
        }

        var pending = new Stack<string>();
        pending.Push(rootPath);
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            foreach (var filePath in Directory.EnumerateFiles(current))
            {
                yield return filePath;
            }

            foreach (var childDirectory in Directory.EnumerateDirectories(current))
            {
                if (!IsReparsePoint(childDirectory))
                {
                    pending.Push(childDirectory);
                }
            }
        }
    }

    private static bool IsReparsePoint(string path)
    {
        if (!Directory.Exists(path) && !File.Exists(path))
        {
            return false;
        }

        FileSystemInfo info = Directory.Exists(path) ? new DirectoryInfo(path) : new FileInfo(path);
        return (info.Attributes & FileAttributes.ReparsePoint) != 0;
    }

    private static WorkspaceImportTargetKind DetermineSuggestedTarget(string relativePath, string sourcePath)
    {
        var segments = SplitRelativePath(relativePath);
        if (segments.Length > 0 && IsPrivateSegment(segments[0]))
        {
            return WorkspaceImportTargetKind.Private;
        }

        return sourcePath.Contains("AI-Personal", StringComparison.OrdinalIgnoreCase)
            ? WorkspaceImportTargetKind.Private
            : WorkspaceImportTargetKind.AIHub;
    }

    private static string NormalizeImportedRelativePath(string relativePath)
    {
        var segments = SplitRelativePath(relativePath);
        if (segments.Length == 0)
        {
            return string.Empty;
        }

        if (IsPrivateSegment(segments[0]) || IsCompanySegment(segments[0]))
        {
            segments = segments[1..];
        }

        return segments.Length == 0 ? string.Empty : Path.Combine(segments);
    }

    private static string[] SplitRelativePath(string relativePath)
    {
        return string.IsNullOrWhiteSpace(relativePath)
            ? Array.Empty<string>()
            : relativePath.Split(
                [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                StringSplitOptions.RemoveEmptyEntries);
    }

    private static bool IsPrivateSegment(string segment)
        => string.Equals(segment, "personal", StringComparison.OrdinalIgnoreCase)
           || string.Equals(segment, "private", StringComparison.OrdinalIgnoreCase);

    private static bool IsCompanySegment(string segment)
        => string.Equals(segment, "company", StringComparison.OrdinalIgnoreCase)
           || string.Equals(segment, "ai-hub", StringComparison.OrdinalIgnoreCase);

    private sealed record ScanRoot(string Path, string Label);
}
