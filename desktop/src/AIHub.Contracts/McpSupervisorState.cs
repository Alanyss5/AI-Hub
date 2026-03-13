namespace AIHub.Contracts;
public enum McpSupervisorState
{
    Idle = 0,
    Recovering = 1,
    BackingOff = 2,
    SuspendedBySupervisor = 3
}