using AIHub.Contracts;

namespace AIHub.Application.Models;

public sealed record McpProcessCommandResult(
    OperationResult Result,
    McpRuntimeRecord Record);
