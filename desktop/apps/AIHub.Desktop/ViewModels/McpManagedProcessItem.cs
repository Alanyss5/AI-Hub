using AIHub.Contracts;

namespace AIHub.Desktop.ViewModels;

public sealed class McpManagedProcessItem
{
    public McpManagedProcessItem(McpRuntimeRecord record)
    {
        Record = record;
    }

    public McpRuntimeRecord Record { get; }

    public string Name => Record.Name;

    public string StatusDisplay => Record.SupervisorState switch
    {
        McpSupervisorState.SuspendedBySupervisor => "已被监督暂停",
        McpSupervisorState.BackingOff => "退避等待中",
        McpSupervisorState.Recovering when Record.IsRunning => "恢复运行中",
        _ => !Record.IsEnabled
            ? "已禁用"
            : Record.IsRunning
                ? "运行中"
                : "已停止"
    };

    public string HealthDisplay => string.IsNullOrWhiteSpace(Record.LastHealthStatus)
        ? "未检查"
        : Record.LastHealthStatus!;

    public string ProcessInfoDisplay => Record.IsRunning && Record.ProcessId.HasValue
        ? "PID " + Record.ProcessId.Value
        : "未运行";

    public string AutoStartDisplay => Record.AutoStart ? "应用启动时自启动" : "手动启动";

    public string KeepAliveDisplay => Record.KeepAlive ? "保持运行 / 自动拉起" : "不自动拉起";

    public string SupervisorDisplay => Record.SupervisorState switch
    {
        McpSupervisorState.SuspendedBySupervisor => $"监督器已暂停自动拉起 / 窗口 {Record.RestartWindowMinutes} 分钟 / 上限 {Record.MaxRestartAttemptsInWindow} 次",
        McpSupervisorState.BackingOff => $"监督器退避中 / {Record.BackoffSeconds} 秒后重试",
        McpSupervisorState.Recovering => "监督器已恢复拉起，等待稳定",
        _ => Record.KeepAlive ? "监督器待命" : "未启用监督器"
    };

    public string RestartDisplay => Record.LastRestartAt.HasValue
        ? $"最近拉起：{Record.LastRestartAt.Value.ToLocalTime():yyyy-MM-dd HH:mm:ss} / 次数：{Record.RestartCount} / 窗口内尝试：{Record.ConsecutiveRestartFailures}"
        : "尚无自动拉起记录";

    public string LastExitDisplay => Record.LastExitAt.HasValue
        ? $"最近退出：{Record.LastExitAt.Value.ToLocalTime():yyyy-MM-dd HH:mm:ss} / 退出码：{(Record.LastExitCode?.ToString() ?? "未知")}"
        : "尚无退出记录";

    public string CommandDisplay
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Record.Command))
            {
                return "未配置命令";
            }

            return Record.Arguments.Length == 0
                ? Record.Command
                : Record.Command + " " + string.Join(" ", Record.Arguments.Select(QuoteArgument));
        }
    }

    public string LastMessageDisplay => string.IsNullOrWhiteSpace(Record.LastHealthMessage)
        ? "暂无状态消息。"
        : Record.LastHealthMessage!;

    private static string QuoteArgument(string argument)
    {
        if (string.IsNullOrWhiteSpace(argument))
        {
            return "\"\"";
        }

        return argument.IndexOfAny([' ', '\t', '"']) >= 0
            ? "\"" + argument.Replace("\"", "\\\"") + "\""
            : argument;
    }
}
