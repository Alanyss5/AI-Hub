using System.Text.Json;
using System.Text.Json.Nodes;
using AIHub.Contracts;
using Tomlyn;
using Tomlyn.Model;

namespace AIHub.Infrastructure;

public sealed partial class NativeWorkspaceAutomationService
{
    private void AddMcpCandidates(List<ScannedCandidate> candidates, WorkspaceScope scope, string profile, string? projectPath, string hubRoot, string personalRoot, IEnumerable<McpTarget> targets)
    {
        var variantsByName = new Dictionary<string, List<ScannedMcpVariant>>(StringComparer.OrdinalIgnoreCase);
        var managedServers = LayeredWorkspaceMaterializer.BuildEffectiveServerMap(hubRoot, personalRoot, profile);
        var companySourceRoot = SourcePathLayout.GetCompanySourceRoot(hubRoot);
        var personalSourceRoot = SourcePathLayout.GetPersonalSourceRoot(personalRoot);
        var companyManifestPath = SourcePathLayout.GetProfileManifestPath(companySourceRoot, profile);
        var privateManifestPath = SourcePathLayout.GetProfileManifestPath(personalSourceRoot, profile);
        var companyServers = ReadManifestServers(companyManifestPath);
        var privateServers = ReadManifestServers(privateManifestPath);

        foreach (var target in targets)
        {
            var parsedServers = target.Format == ClientConfigFormat.Toml
                ? ParseTomlServers(target.FilePath)
                : ParseJsonServers(target.FilePath);

            foreach (var entry in parsedServers)
            {
                if (managedServers.TryGetValue(entry.Key, out var managed) && managed == entry.Value)
                {
                    continue;
                }

                variantsByName.TryAdd(entry.Key, new List<ScannedMcpVariant>());
                variantsByName[entry.Key].Add(new ScannedMcpVariant(target.ClientName, target.FilePath, entry.Value));
            }
        }

        foreach (var entry in variantsByName.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
        {
            var hasVariantConflict = entry.Value.Select(item => item.Definition).Distinct().Count() > 1;
            var details = BuildMcpCandidateDetails(entry.Value);
            candidates.Add(new ScannedCandidate(
                new WorkspaceOnboardingCandidate(
                    CreateMcpCandidateId(entry.Key),
                    WorkspaceOnboardingResourceKind.McpServer,
                    entry.Key,
                    string.Join(Environment.NewLine, entry.Value.Select(item => item.FilePath).Distinct(StringComparer.OrdinalIgnoreCase)),
                    details,
                    companyManifestPath,
                    privateManifestPath,
                    companyServers.ContainsKey(entry.Key),
                    privateServers.ContainsKey(entry.Key),
                    hasVariantConflict ? WorkspaceImportTargetKind.Ignore : WorkspaceImportTargetKind.AIHub),
                null,
                false,
                scope,
                profile,
                projectPath,
                entry.Value,
                hasVariantConflict));
        }
    }

    private OperationResult ExecuteImportDecisions(IReadOnlyList<WorkspaceImportDecisionRecord> decisions, IReadOnlyList<ScannedCandidate> scannedCandidates, CancellationToken cancellationToken)
    {
        var candidateMap = scannedCandidates.ToDictionary(item => item.Candidate.Id, StringComparer.OrdinalIgnoreCase);
        var details = new List<string>();

        foreach (var decision in decisions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!candidateMap.TryGetValue(decision.CandidateId, out var candidate))
            {
                return OperationResult.Fail("接管候选项已失效，请重新扫描后再试。", decision.CandidateId);
            }

            if (decision.Target == WorkspaceImportTargetKind.Ignore)
            {
                details.Add("忽略：" + candidate.Candidate.DisplayName);
                continue;
            }

            if (candidate.Candidate.ResourceKind == WorkspaceOnboardingResourceKind.McpServer && candidate.HasMcpVariantConflict)
            {
                return OperationResult.Fail(
                    "检测到同名 MCP 在多个客户端中的定义不一致，暂不支持直接导入，请先手工统一后再重新扫描。",
                    candidate.Candidate.DisplayName + Environment.NewLine + candidate.Candidate.SourceDetails);
            }

            var destinationPath = decision.Target == WorkspaceImportTargetKind.AIHub
                ? candidate.Candidate.CompanyDestinationPath
                : candidate.Candidate.PrivateDestinationPath;

            if (candidate.Candidate.ResourceKind == WorkspaceOnboardingResourceKind.McpServer)
            {
                ImportMcpServer(candidate, destinationPath);
            }
            else if (candidate.IsDirectory)
            {
                CopyDirectoryWithBackup(candidate.SourcePath!, destinationPath);
            }
            else
            {
                CopyFileWithBackup(candidate.SourcePath!, destinationPath);
            }

            details.Add($"{candidate.Candidate.DisplayName} -> {(decision.Target == WorkspaceImportTargetKind.AIHub ? "AI-Hub" : "私人目录")}");
        }

        return OperationResult.Ok("接管导入已完成。", string.Join(Environment.NewLine, details));
    }

    private static void ImportMcpServer(ScannedCandidate candidate, string destinationManifestPath)
    {
        var selectedVariant = candidate.McpVariants!
            .OrderBy(item => GetClientPriority(item.ClientName))
            .ThenBy(item => item.FilePath, StringComparer.OrdinalIgnoreCase)
            .First();

        var root = File.Exists(destinationManifestPath)
            ? JsonNode.Parse(File.ReadAllText(destinationManifestPath)) as JsonObject ?? new JsonObject()
            : new JsonObject();
        var servers = root["mcpServers"] as JsonObject ?? new JsonObject();
        root["mcpServers"] = servers;
        servers[candidate.Candidate.DisplayName] = CreateJsonServerDefinition(selectedVariant.Definition);

        WriteTextIfChanged(destinationManifestPath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    private void LinkGlobalEntrypoints(string hubRoot, string userHome)
    {
        var effectiveRoot = LayeredWorkspaceMaterializer.GetEffectiveProfileRoot(hubRoot, WorkspaceProfiles.GlobalId);
        var effectiveSkills = Path.Combine(effectiveRoot, "skills");
        var effectiveCommands = Path.Combine(effectiveRoot, "claude", "commands");
        var effectiveAgents = Path.Combine(effectiveRoot, "claude", "agents");

        Directory.CreateDirectory(effectiveSkills);

        _platformLinkService.EnsureDirectory(Path.Combine(userHome, ".claude"));
        _platformLinkService.EnsureDirectory(Path.Combine(userHome, ".claude", "skills"));
        _platformLinkService.EnsureDirectory(Path.Combine(userHome, ".agents"));
        _platformLinkService.EnsureDirectory(Path.Combine(userHome, ".agents", "skills"));
        _platformLinkService.EnsureDirectory(Path.Combine(userHome, ".codex"));
        _platformLinkService.EnsureDirectory(Path.Combine(userHome, ".codex", "skills"));
        _platformLinkService.EnsureDirectory(Path.Combine(userHome, ".gemini"));
        _platformLinkService.EnsureDirectory(Path.Combine(userHome, ".gemini", "antigravity"));
        _platformLinkService.EnsureDirectory(Path.Combine(userHome, ".gemini", "antigravity", "skills"));

        _platformLinkService.EnsureJunction(Path.Combine(userHome, ".claude", "skills", "company"), effectiveSkills);
        _platformLinkService.EnsureJunction(Path.Combine(userHome, ".claude", "skills", "personal"), effectiveSkills);
        _platformLinkService.EnsureJunction(Path.Combine(userHome, ".agents", "skills", "company"), effectiveSkills);
        _platformLinkService.EnsureJunction(Path.Combine(userHome, ".agents", "skills", "personal"), effectiveSkills);
        _platformLinkService.EnsureJunction(Path.Combine(userHome, ".gemini", "antigravity", "skills", "company"), effectiveSkills);
        _platformLinkService.EnsureJunction(Path.Combine(userHome, ".gemini", "antigravity", "skills", "personal"), effectiveSkills);
        _platformLinkService.EnsureJunction(Path.Combine(userHome, ".codex", "skills", "ai-hub"), effectiveSkills, ignoreIfLocked: true);
        _platformLinkService.EnsureJunction(Path.Combine(userHome, ".codex", "skills", "personal"), effectiveSkills, ignoreIfLocked: true);
        _platformLinkService.EnsureJunction(Path.Combine(userHome, ".claude", "commands"), effectiveCommands);
        _platformLinkService.EnsureJunction(Path.Combine(userHome, ".claude", "agents"), effectiveAgents);
        _platformLinkService.EnsureJunction(Path.Combine(userHome, ".agents", "agents"), effectiveAgents);

        CopyTextIfChanged(Path.Combine(effectiveRoot, "claude", "settings.json"), Path.Combine(userHome, ".claude", "settings.json"));
        CopyTextIfChanged(Path.Combine(effectiveRoot, "mcp", "claude.mcp.json"), Path.Combine(userHome, ".claude.json"));
        CopyTextIfChanged(Path.Combine(effectiveRoot, "mcp", "codex.config.toml"), Path.Combine(userHome, ".codex", "config.toml"));
        CopyTextIfChanged(Path.Combine(effectiveRoot, "mcp", "antigravity.mcp.json"), Path.Combine(userHome, ".gemini", "antigravity", "mcp_config.json"));
        CopyAgentBootstrapIfChanged(effectiveRoot, userHome);
    }

    private void LinkProjectEntrypoints(string hubRoot, string projectPath, string profile)
    {
        var effectiveRoot = LayeredWorkspaceMaterializer.GetEffectiveProfileRoot(hubRoot, profile);
        _platformLinkService.EnsureDirectory(Path.Combine(projectPath, ".claude"));
        _platformLinkService.EnsureDirectory(Path.Combine(projectPath, ".agents"));
        _platformLinkService.EnsureDirectory(Path.Combine(projectPath, ".agent"));
        _platformLinkService.EnsureDirectory(Path.Combine(projectPath, ".codex"));

        _platformLinkService.EnsureJunction(Path.Combine(projectPath, ".claude", "skills"), Path.Combine(effectiveRoot, "skills"));
        _platformLinkService.EnsureJunction(Path.Combine(projectPath, ".claude", "commands"), Path.Combine(effectiveRoot, "claude", "commands"));
        _platformLinkService.EnsureJunction(Path.Combine(projectPath, ".claude", "agents"), Path.Combine(effectiveRoot, "claude", "agents"));
        _platformLinkService.EnsureJunction(Path.Combine(projectPath, ".agents", "skills"), Path.Combine(effectiveRoot, "skills"));
        _platformLinkService.EnsureJunction(Path.Combine(projectPath, ".agents", "agents"), Path.Combine(effectiveRoot, "claude", "agents"));
        _platformLinkService.EnsureJunction(Path.Combine(projectPath, ".agent", "skills"), Path.Combine(effectiveRoot, "skills"));

        CopyTextIfChanged(Path.Combine(effectiveRoot, "claude", "settings.json"), Path.Combine(projectPath, ".claude", "settings.json"));
        CopyTextIfChanged(Path.Combine(effectiveRoot, "mcp", "claude.mcp.json"), Path.Combine(projectPath, ".mcp.json"));
        CopyTextIfChanged(Path.Combine(effectiveRoot, "mcp", "codex.config.toml"), Path.Combine(projectPath, ".codex", "config.toml"));
        CopyAgentBootstrapIfChanged(effectiveRoot, projectPath);
    }

    private static void CopyAgentBootstrapIfChanged(string effectiveRoot, string destinationRoot)
    {
        CopyTextIfChanged(
            Path.Combine(effectiveRoot, ".agents", "AGENTS.md"),
            Path.Combine(destinationRoot, ".agents", "AGENTS.md"));
    }

    private static string BuildEffectiveSettingsPreview(string hubRoot, string personalRoot, string profile)
    {
        LayeredWorkspaceMaterializer.MaterializeProfile(hubRoot, personalRoot, profile);
        var effectiveSettingsPath = Path.Combine(LayeredWorkspaceMaterializer.GetEffectiveProfileRoot(hubRoot, profile), "claude", "settings.json");
        return File.ReadAllText(effectiveSettingsPath);
    }

    private static string BuildPreviewSummary(IReadOnlyList<ScannedCandidate> candidates)
    {
        if (candidates.Count == 0)
        {
            return "未发现需要导入的现有资源。";
        }

        var counts = candidates.GroupBy(item => item.Candidate.ResourceKind)
            .Select(group => $"{GetResourceDisplayName(group.Key)} {group.Count()} 项");
        return $"共检测到 {candidates.Count} 项候选资源：" + string.Join(" / ", counts);
    }

    private static string GetResourceDisplayName(WorkspaceOnboardingResourceKind resourceKind)
    {
        return resourceKind switch
        {
            WorkspaceOnboardingResourceKind.Skill => "Skills",
            WorkspaceOnboardingResourceKind.ClaudeCommand => "commands",
            WorkspaceOnboardingResourceKind.ClaudeAgent => "agents",
            WorkspaceOnboardingResourceKind.ClaudeSettings => "Claude settings",
            WorkspaceOnboardingResourceKind.McpServer => "MCP",
            _ => resourceKind.ToString()
        };
    }

    private static string BuildMcpCandidateDetails(IEnumerable<ScannedMcpVariant> variants)
    {
        var items = variants.ToArray();
        var details = new List<string>();
        if (items.Select(item => item.Definition).Distinct().Count() > 1)
        {
            details.Add("检测到同名 MCP 在多个客户端中存在差异。为避免静默覆盖，默认建议忽略，并要求先手工统一。");
        }

        foreach (var variant in items.OrderBy(item => GetClientPriority(item.ClientName)))
        {
            details.Add("来源：" + variant.ClientName);
            details.Add("文件：" + variant.FilePath);
            details.Add("命令：" + variant.Definition.Command);
        }

        return string.Join(Environment.NewLine, details);
    }

    private static int GetClientPriority(string clientName)
    {
        return clientName switch
        {
            "Claude" => 0,
            "Codex" => 1,
            "Antigravity" => 2,
            _ => 99
        };
    }

    private static string CreateMcpCandidateId(string serverName) => "mcp|" + serverName.Trim().ToLowerInvariant();

    private static string CreatePathCandidateId(WorkspaceOnboardingResourceKind resourceKind, string sourcePath)
        => resourceKind.ToString().ToLowerInvariant() + "|" + NormalizePath(sourcePath);

    private static string NormalizePath(string path)
        => Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private sealed record McpTarget(string ClientName, string FilePath, ClientConfigFormat Format);
    private sealed record ScannedMcpVariant(string ClientName, string FilePath, McpServerDefinitionRecord Definition);

    private sealed record ScannedCandidate(
        WorkspaceOnboardingCandidate Candidate,
        string? SourcePath,
        bool IsDirectory,
        WorkspaceScope Scope,
        string Profile,
        string? ProjectPath,
        IReadOnlyList<ScannedMcpVariant>? McpVariants = null,
        bool HasMcpVariantConflict = false);

    private enum ClientConfigFormat
    {
        Json = 0,
        Toml = 1
    }
}
