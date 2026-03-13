namespace AIHub.Contracts;
public sealed record McpRuntimeSummary(
    int ManagedProcessCount,
    int RunningProcessCount,
    int StoppedProcessCount,
    int AlertProcessCount,
    int RecoverableProcessCount,
    int AttentionProcessCount,
    int SuspendedProcessCount,
    IReadOnlyList<string> ManagedProcessNames);