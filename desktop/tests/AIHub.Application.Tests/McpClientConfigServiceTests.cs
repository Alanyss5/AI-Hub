using System.Text.Json.Nodes;
using AIHub.Contracts;
using AIHub.Infrastructure;

namespace AIHub.Application.Tests;

public sealed class McpClientConfigServiceTests
{
    [Fact]
    public async Task SyncAsync_PreservesExternalEntriesAndBacksUpExistingFiles()
    {
        using var scope = new TestHubRootScope();
        var userHome = Path.Combine(scope.RootPath, "user-home");
        Directory.CreateDirectory(userHome);

        var claudePath = Path.Combine(userHome, ".claude.json");
        var codexPath = Path.Combine(userHome, ".codex", "config.toml");
        var antigravityPath = Path.Combine(userHome, ".gemini", "antigravity", "mcp_config.json");
        Directory.CreateDirectory(Path.GetDirectoryName(codexPath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(antigravityPath)!);

        await File.WriteAllTextAsync(
            claudePath,
            """
            {
              "theme": "keep-me",
              "mcpServers": {
                "external-one": {
                  "command": "cmd",
                  "args": ["/c", "echo external"],
                  "env": {
                    "EXTERNAL": "1"
                  }
                }
              }
            }
            """);
        await File.WriteAllTextAsync(
            codexPath,
            """
            theme = "dark"

            [mcp_servers.external-one]
            command = "cmd"
            args = ["/c", "echo external"]

            [mcp_servers.external-one.env]
            EXTERNAL = "1"
            """);
        await File.WriteAllTextAsync(
            antigravityPath,
            """
            {
              "workspace": "keep-me",
              "mcpServers": {
                "external-one": {
                  "command": "cmd",
                  "args": ["/c", "echo external"],
                  "env": {
                    "EXTERNAL": "1"
                  }
                }
              }
            }
            """);

        var managedServers = new Dictionary<string, McpServerDefinitionRecord>(StringComparer.OrdinalIgnoreCase)
        {
            ["managed-one"] = new(
                "cmd",
                new[] { "/c", "echo managed" },
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["AI_HUB"] = "1"
                })
        };

        var service = new McpClientConfigService(userHome);

        var result = await service.SyncAsync(scope.RootPath, WorkspaceScope.Global, ProfileKind.Global, null, managedServers);

        Assert.True(result.Success, result.Details);

        var claudeRoot = JsonNode.Parse(await File.ReadAllTextAsync(claudePath))!.AsObject();
        var claudeServers = claudeRoot["mcpServers"]!.AsObject();
        Assert.Equal("keep-me", claudeRoot["theme"]!.GetValue<string>());
        Assert.NotNull(claudeServers["external-one"]);
        Assert.NotNull(claudeServers["managed-one"]);

        var antigravityRoot = JsonNode.Parse(await File.ReadAllTextAsync(antigravityPath))!.AsObject();
        var antigravityServers = antigravityRoot["mcpServers"]!.AsObject();
        Assert.Equal("keep-me", antigravityRoot["workspace"]!.GetValue<string>());
        Assert.NotNull(antigravityServers["external-one"]);
        Assert.NotNull(antigravityServers["managed-one"]);

        var codexText = await File.ReadAllTextAsync(codexPath);
        Assert.Contains("theme = \"dark\"", codexText, StringComparison.Ordinal);
        Assert.Contains("[mcp_servers.external-one]", codexText, StringComparison.Ordinal);
        Assert.Contains("[mcp_servers.managed-one]", codexText, StringComparison.Ordinal);

        Assert.Single(Directory.EnumerateFiles(userHome, ".claude.json.bak.*", SearchOption.TopDirectoryOnly));
        Assert.Single(Directory.EnumerateFiles(Path.Combine(userHome, ".codex"), "config.toml.bak.*", SearchOption.TopDirectoryOnly));
        Assert.Single(Directory.EnumerateFiles(Path.Combine(userHome, ".gemini", "antigravity"), "mcp_config.json.bak.*", SearchOption.TopDirectoryOnly));
    }

    [Fact]
    public async Task InspectAsync_FindsExternalServersAndProjectAntigravityUnsupported()
    {
        using var scope = new TestHubRootScope();
        var projectPath = Path.Combine(scope.RootPath, "sample-project");
        Directory.CreateDirectory(Path.Combine(projectPath, ".codex"));

        await File.WriteAllTextAsync(
            Path.Combine(projectPath, ".mcp.json"),
            """
            {
              "mcpServers": {
                "managed-one": {
                  "command": "cmd",
                  "args": ["/c", "echo managed"],
                  "env": {}
                },
                "external-one": {
                  "command": "cmd",
                  "args": ["/c", "echo external-claude"],
                  "env": {}
                }
              }
            }
            """);
        await File.WriteAllTextAsync(
            Path.Combine(projectPath, ".codex", "config.toml"),
            """
            [mcp_servers.managed-one]
            command = "cmd"
            args = ["/c", "echo managed"]

            [mcp_servers.external-one]
            command = "cmd"
            args = ["/c", "echo external-codex"]
            """);

        var managedServers = new Dictionary<string, McpServerDefinitionRecord>(StringComparer.OrdinalIgnoreCase)
        {
            ["managed-one"] = new(
                "cmd",
                new[] { "/c", "echo managed" },
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase))
        };

        var service = new McpClientConfigService(Path.Combine(scope.RootPath, "user-home"));

        var snapshot = await service.InspectAsync(
            scope.RootPath,
            WorkspaceScope.Project,
            ProfileKind.Frontend,
            projectPath,
            managedServers);

        Assert.Equal(3, snapshot.ClientStatuses.Count);
        var antigravityStatus = Assert.Single(snapshot.ClientStatuses.Where(item => item.Client == McpClientKind.Antigravity));
        Assert.False(antigravityStatus.IsSupported);
        Assert.False(antigravityStatus.Exists);

        var externalServer = Assert.Single(snapshot.ExternalServers);
        Assert.Equal("external-one", externalServer.Name);
        Assert.True(externalServer.HasConflict);
        Assert.Equal(2, externalServer.Variants.Count);
        Assert.Contains(externalServer.Variants, item => item.Client == McpClientKind.Claude);
        Assert.Contains(externalServer.Variants, item => item.Client == McpClientKind.Codex);
    }

    [Fact]
    public async Task SyncAsync_Writes_Codex_Server_Aliases_Using_Codex_Naming()
    {
        using var scope = new TestHubRootScope();
        var userHome = Path.Combine(scope.RootPath, "user-home");
        Directory.CreateDirectory(Path.Combine(userHome, ".codex"));

        var managedServers = new Dictionary<string, McpServerDefinitionRecord>(StringComparer.OrdinalIgnoreCase)
        {
            ["coplay-mcp"] = new(
                "uvx",
                new[] { "--python", ">=3.11", "coplay-mcp-server@latest" },
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["MCP_TOOL_TIMEOUT"] = "720000"
                })
        };

        var service = new McpClientConfigService(userHome);

        var result = await service.SyncAsync(scope.RootPath, WorkspaceScope.Global, ProfileKind.Global, null, managedServers);

        Assert.True(result.Success, result.Details);
        var codexText = await File.ReadAllTextAsync(Path.Combine(userHome, ".codex", "config.toml"));
        Assert.Contains("[mcp_servers.coplay_mcp]", codexText, StringComparison.Ordinal);
        Assert.DoesNotContain("[mcp_servers.coplay-mcp]", codexText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InspectAsync_Treats_Codex_Server_Alias_As_Managed_Server()
    {
        using var scope = new TestHubRootScope();
        var projectPath = Path.Combine(scope.RootPath, "sample-project");
        Directory.CreateDirectory(Path.Combine(projectPath, ".codex"));

        await File.WriteAllTextAsync(
            Path.Combine(projectPath, ".codex", "config.toml"),
            """
            [mcp_servers.coplay_mcp]
            command = "uvx"
            args = ["--python", ">=3.11", "coplay-mcp-server@latest"]

            [mcp_servers.coplay_mcp.env]
            MCP_TOOL_TIMEOUT = "720000"
            """);

        var managedServers = new Dictionary<string, McpServerDefinitionRecord>(StringComparer.OrdinalIgnoreCase)
        {
            ["coplay-mcp"] = new(
                "uvx",
                new[] { "--python", ">=3.11", "coplay-mcp-server@latest" },
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["MCP_TOOL_TIMEOUT"] = "720000"
                })
        };

        var service = new McpClientConfigService(Path.Combine(scope.RootPath, "user-home"));

        var snapshot = await service.InspectAsync(
            scope.RootPath,
            WorkspaceScope.Project,
            ProfileKind.Frontend,
            projectPath,
            managedServers);

        var codexStatus = Assert.Single(snapshot.ClientStatuses.Where(item => item.Client == McpClientKind.Codex));
        Assert.True(codexStatus.InSync);
        Assert.Contains("coplay-mcp", codexStatus.ManagedServerNames);
        Assert.Empty(snapshot.ExternalServers);
    }
}
