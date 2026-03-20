using System.IO.Compression;
using System.Text.Json;
using AIHub.Application.Services;
using AIHub.Contracts;
using AIHub.Infrastructure;

namespace AIHub.Application.Tests;

public sealed class PersistenceAndDiagnosticsTests
{
    [Fact]
    public async Task JsonHubSettingsStore_Loads_Legacy_Format_And_Save_Creates_Backup()
    {
        using var scope = new TestHubRootScope();
        var settingsPath = Path.Combine(scope.RootPath, "config", "hub-settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
        await File.WriteAllTextAsync(settingsPath, JsonSerializer.Serialize(new HubSettingsRecord
        {
            HubRoot = scope.RootPath,
            PreferredClients = ["codex"],
            ScriptExecutionRiskAccepted = true
        }));

        var store = new JsonHubSettingsStore(scope.RootPath);

        var loaded = await store.LoadAsync();
        Assert.True(loaded.ScriptExecutionRiskAccepted);
        Assert.Equal(["codex"], loaded.PreferredClients);

        await store.SaveAsync(loaded with { LastOpenedProject = "demo-project" });

        var json = await File.ReadAllTextAsync(settingsPath);
        Assert.Contains("\"schemaVersion\"", json);
        Assert.Contains("\"settings\"", json);
        Assert.NotEmpty(Directory.EnumerateFiles(Path.Combine(scope.RootPath, "backups", "state-writes"), "hub-settings.json", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task JsonMcpRuntimeStore_Save_Normalizes_Records_And_Creates_Backup()
    {
        using var scope = new TestHubRootScope();
        var runtimePath = Path.Combine(scope.RootPath, "mcp", "runtime.json");
        Directory.CreateDirectory(Path.GetDirectoryName(runtimePath)!);
        await File.WriteAllTextAsync(runtimePath, "{\"managedProcesses\":[]}");

        var store = new JsonMcpRuntimeStore(scope.RootPath);
        await store.SaveAllAsync([
            new McpRuntimeRecord
            {
                Name = "  demo-process  ",
                Command = "  node  ",
                Arguments = ["  server.js  ", " ", " --port ", "8787"],
                EnvironmentVariables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["TOKEN"] = "secret"
                },
                HealthCheckTimeoutSeconds = 0
            }
        ]);

        var records = await store.GetAllAsync();
        var saved = Assert.Single(records);
        Assert.Equal("demo-process", saved.Name);
        Assert.Equal("node", saved.Command);
        Assert.Equal(["server.js", "--port", "8787"], saved.Arguments);
        Assert.Equal(5, saved.HealthCheckTimeoutSeconds);
        Assert.NotEmpty(Directory.EnumerateFiles(Path.Combine(scope.RootPath, "backups", "state-writes"), "runtime.json", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task WorkspaceControlService_Confirm_And_Reset_Risk_Acceptance_Persists_Settings()
    {
        using var scope = new TestHubRootScope();
        var service = new WorkspaceControlService(
            new FixedHubRootLocator(scope.RootPath),
            _ => new JsonProjectRegistry(scope.RootPath),
            _ => new JsonHubSettingsStore(scope.RootPath),
            _ => new JsonWorkspaceProfileCatalogStore(scope.RootPath),
            new NoOpWorkspaceAutomationService(),
            new HubDashboardService());

        var acceptResult = await service.ConfirmRiskAcceptanceAsync(HubRiskConsentKind.ManagedMcpExecution);
        Assert.True(acceptResult.Success);

        var settingsStore = new JsonHubSettingsStore(scope.RootPath);
        var settings = await settingsStore.LoadAsync();
        Assert.True(settings.ManagedMcpRiskAccepted);
        Assert.NotNull(settings.ManagedMcpRiskAcceptedAt);

        var resetResult = await service.ResetRiskAcceptancesAsync();
        Assert.True(resetResult.Success);

        settings = await settingsStore.LoadAsync();
        Assert.False(settings.ScriptExecutionRiskAccepted);
        Assert.False(settings.ManagedMcpRiskAccepted);
        Assert.False(settings.ExternalMcpImportRiskAccepted);
        Assert.Null(settings.ManagedMcpRiskAcceptedAt);
    }

    [Fact]
    public async Task FileDiagnosticLogService_ExportBundle_Redacts_Secrets_And_User_Profile()
    {
        using var scope = new TestHubRootScope();
        var diagnosticsRoot = Path.Combine(scope.RootPath, "diagnostics");
        var service = new FileDiagnosticLogService(diagnosticsRoot);

        service.RecordStartupFailure("bootstrap", new InvalidOperationException("startup exploded"));
        service.RecordUnhandledException("background-loop", new InvalidOperationException("background exploded"));

        var settingsPath = Path.Combine(scope.RootPath, "config", "hub-settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
        var userHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        await File.WriteAllTextAsync(settingsPath, "apiKey: super-secret" + Environment.NewLine + "home=" + userHome);

        var exportPath = Path.Combine(scope.RootPath, "exports", "diagnostics.zip");
        var result = await service.ExportBundleAsync(exportPath, scope.RootPath);
        Assert.True(result.Success, result.Message + " / " + result.Details);
        Assert.True(File.Exists(exportPath));

        using var archive = ZipFile.OpenRead(exportPath);
        var stateEntry = archive.GetEntry("hub-state/config/hub-settings.json");
        Assert.NotNull(stateEntry);
        using var reader = new StreamReader(stateEntry!.Open());
        var redacted = await reader.ReadToEndAsync();
        Assert.DoesNotContain("super-secret", redacted);
        Assert.Contains("***", redacted);
        Assert.DoesNotContain(userHome, redacted, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("%USERPROFILE%", redacted);

        var snapshot = await service.LoadSnapshotAsync();
        Assert.NotNull(snapshot.LastExportedAt);
        Assert.Equal(Path.GetFullPath(exportPath), snapshot.LastExportPath);
    }
}
