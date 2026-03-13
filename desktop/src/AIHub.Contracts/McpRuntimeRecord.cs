namespace AIHub.Contracts;
public sealed record McpRuntimeRecord
{
    public string Name { get; init; } = string.Empty;
    public McpServerMode Mode { get; init; } = McpServerMode.ProcessManaged;
    public bool IsEnabled { get; init; } = true;
    public bool AutoStart { get; init; }
    public bool KeepAlive { get; init; }
    public string Command { get; init; } = string.Empty;
    public string[] Arguments { get; init; } = Array.Empty<string>();
    public string? WorkingDirectory { get; init; }
    public Dictionary<string, string> EnvironmentVariables { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public string? HealthCheckUrl { get; init; }
    public int HealthCheckTimeoutSeconds { get; init; } = 5;
    public bool IsRunning { get; init; }
    public int? ProcessId { get; init; }
    public DateTimeOffset? ProcessStartedAt { get; init; }
    public int? LastExitCode { get; init; }
    public string? LastHealthStatus { get; init; }
    public string? LastHealthMessage { get; init; }
    public DateTimeOffset? LastCheckedAt { get; init; }
    public DateTimeOffset? LastRestartAt { get; init; }
    public int RestartCount { get; init; }
    public int BackoffSeconds { get; init; } = 30;
    public int RestartWindowMinutes { get; init; } = 10;
    public int MaxRestartAttemptsInWindow { get; init; } = 3;
    public McpSupervisorState SupervisorState { get; init; } = McpSupervisorState.Idle;
    public int ConsecutiveRestartFailures { get; init; }
    public DateTimeOffset? LastExitAt { get; init; }
    public string? StandardOutputLogPath { get; init; }
    public string? StandardErrorLogPath { get; init; }
    public string? LastOutputSnippet { get; init; }
    public string? LastErrorSnippet { get; init; }
}