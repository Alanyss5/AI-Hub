using AIHub.Contracts;

namespace AIHub.Application.Services;

public sealed partial class McpControlService
{
    public async Task<OperationResult> StartManagedProcessesAsync(bool autoStartOnly, CancellationToken cancellationToken = default)
    {
        var resolution = await _hubRootLocator.ResolveAsync(cancellationToken);
        if (!resolution.IsValid || string.IsNullOrWhiteSpace(resolution.RootPath))
        {
            return OperationResult.Fail("AI-Hub 根目录无效，无法启动托管进程。", string.Join(Environment.NewLine, resolution.Errors));
        }

        if (_hubSettingsStoreFactory is not null)
        {
            var settings = await _hubSettingsStoreFactory(resolution.RootPath).LoadAsync(cancellationToken);
            if (!settings.ManagedMcpRiskAccepted)
            {
                return autoStartOnly
                    ? OperationResult.Ok("已跳过托管进程自启动。", "尚未完成托管 MCP 风险确认。")
                    : OperationResult.Fail("首次运行托管 MCP 前，请先完成风险确认。", resolution.RootPath);
            }
        }

        var runtimeStore = _mcpRuntimeStoreFactory(resolution.RootPath);
        var records = (await runtimeStore.GetAllAsync(cancellationToken)).ToList();
        var started = 0;
        var skipped = 0;

        for (var index = 0; index < records.Count; index++)
        {
            var record = NormalizeSupervisorSettings(await _mcpProcessController.RefreshAsync(records[index], cancellationToken));
            if (record.Mode != McpServerMode.ProcessManaged || !record.IsEnabled || record.IsRunning || (autoStartOnly && !record.AutoStart))
            {
                records[index] = record;
                skipped++;
                continue;
            }

            var startResult = await _mcpProcessController.StartAsync(PrepareManualStart(record), cancellationToken);
            records[index] = FinalizeManualStart(startResult.Record, startResult.Result.Success);
            if (startResult.Result.Success)
            {
                started++;
            }
        }

        await runtimeStore.SaveAllAsync(SortManagedProcesses(records), cancellationToken);
        return OperationResult.Ok(
            autoStartOnly ? "已执行自启动托管进程拉起。" : "已执行全部托管进程拉起。",
            string.Join(Environment.NewLine, new[]
            {
                "已启动：" + started,
                "跳过：" + skipped
            }));
    }

    public async Task<OperationResult> StopManagedProcessesAsync(CancellationToken cancellationToken = default)
    {
        var resolution = await _hubRootLocator.ResolveAsync(cancellationToken);
        if (!resolution.IsValid || string.IsNullOrWhiteSpace(resolution.RootPath))
        {
            return OperationResult.Fail("AI-Hub 根目录无效，无法停止托管进程。", string.Join(Environment.NewLine, resolution.Errors));
        }

        var runtimeStore = _mcpRuntimeStoreFactory(resolution.RootPath);
        var records = (await runtimeStore.GetAllAsync(cancellationToken)).ToList();
        var stopped = 0;

        for (var index = 0; index < records.Count; index++)
        {
            var record = NormalizeSupervisorSettings(await _mcpProcessController.RefreshAsync(records[index], cancellationToken));
            if (record.Mode != McpServerMode.ProcessManaged || !record.IsRunning)
            {
                records[index] = ClearSupervisorState(record);
                continue;
            }

            var stopResult = await _mcpProcessController.StopAsync(record, cancellationToken);
            records[index] = ClearSupervisorState(stopResult.Record);
            if (stopResult.Result.Success)
            {
                stopped++;
            }
        }

        await runtimeStore.SaveAllAsync(SortManagedProcesses(records), cancellationToken);
        return OperationResult.Ok("已执行托管进程停止。", "已停止：" + stopped);
    }

    public async Task<OperationResult> MaintainManagedProcessesAsync(CancellationToken cancellationToken = default)
    {
        var resolution = await _hubRootLocator.ResolveAsync(cancellationToken);
        if (!resolution.IsValid || string.IsNullOrWhiteSpace(resolution.RootPath))
        {
            return OperationResult.Fail("AI-Hub 根目录无效，无法执行托管进程自恢复。", string.Join(Environment.NewLine, resolution.Errors));
        }

        if (_hubSettingsStoreFactory is not null)
        {
            var settings = await _hubSettingsStoreFactory(resolution.RootPath).LoadAsync(cancellationToken);
            if (!settings.ManagedMcpRiskAccepted)
            {
                return OperationResult.Ok("已跳过托管进程自恢复。", "尚未完成托管 MCP 风险确认。");
            }
        }

        var runtimeStore = _mcpRuntimeStoreFactory(resolution.RootPath);
        var records = (await runtimeStore.GetAllAsync(cancellationToken)).ToList();
        if (records.Count == 0)
        {
            return OperationResult.Ok("当前没有托管型 MCP 定义。", string.Empty);
        }

        var now = DateTimeOffset.UtcNow;
        var restarted = 0;
        var backingOff = 0;
        var suspended = 0;
        var changed = false;

        for (var index = 0; index < records.Count; index++)
        {
            var current = NormalizeSupervisorSettings(await _mcpProcessController.RefreshAsync(records[index], cancellationToken));
            var updated = current;

            if (current.Mode != McpServerMode.ProcessManaged || !current.IsEnabled || !current.KeepAlive)
            {
                updated = ClearSupervisorState(current);
            }
            else if (current.IsRunning)
            {
                updated = NormalizeRunningSupervisorState(current, now);
            }
            else
            {
                updated = await RecoverManagedProcessAsync(current, now, cancellationToken);
                if (updated.IsRunning)
                {
                    restarted++;
                }
                else if (updated.SupervisorState == McpSupervisorState.BackingOff)
                {
                    backingOff++;
                }
                else if (updated.SupervisorState == McpSupervisorState.SuspendedBySupervisor)
                {
                    suspended++;
                }
            }

            if (!EqualityComparer<McpRuntimeRecord>.Default.Equals(updated, records[index]))
            {
                changed = true;
            }

            records[index] = updated;
        }

        if (changed)
        {
            await runtimeStore.SaveAllAsync(SortManagedProcesses(records), cancellationToken);
        }

        return OperationResult.Ok(
            "托管进程监督巡检已完成。",
            string.Join(Environment.NewLine, new[]
            {
                "已恢复：" + restarted,
                "退避中：" + backingOff,
                "已暂停：" + suspended
            }));
    }

    public async Task<OperationResult> ResumeSuspendedManagedProcessesAsync(CancellationToken cancellationToken = default)
    {
        var resolution = await _hubRootLocator.ResolveAsync(cancellationToken);
        if (!resolution.IsValid || string.IsNullOrWhiteSpace(resolution.RootPath))
        {
            return OperationResult.Fail("AI-Hub 根目录无效，无法恢复被暂停的托管进程。", string.Join(Environment.NewLine, resolution.Errors));
        }

        if (_hubSettingsStoreFactory is not null)
        {
            var settings = await _hubSettingsStoreFactory(resolution.RootPath).LoadAsync(cancellationToken);
            if (!settings.ManagedMcpRiskAccepted)
            {
                return OperationResult.Fail("首次运行托管 MCP 前，请先完成风险确认。", resolution.RootPath);
            }
        }

        var runtimeStore = _mcpRuntimeStoreFactory(resolution.RootPath);
        var records = (await runtimeStore.GetAllAsync(cancellationToken)).ToList();
        if (records.Count == 0)
        {
            return OperationResult.Ok("当前没有托管型 MCP 定义。", string.Empty);
        }

        var resumed = 0;
        var skipped = 0;
        var now = DateTimeOffset.UtcNow;

        for (var index = 0; index < records.Count; index++)
        {
            var current = NormalizeSupervisorSettings(await _mcpProcessController.RefreshAsync(records[index], cancellationToken));
            if (current.SupervisorState != McpSupervisorState.SuspendedBySupervisor)
            {
                records[index] = current;
                skipped++;
                continue;
            }

            var prepared = current with
            {
                SupervisorState = McpSupervisorState.Idle,
                ConsecutiveRestartFailures = 0,
                LastHealthMessage = "已手动解除监督暂停，准备重新拉起。"
            };

            var started = await RecoverManagedProcessAsync(prepared, now, cancellationToken, ignoreSuspendedState: true, resetAttemptWindow: true);
            records[index] = started;
            if (started.IsRunning)
            {
                resumed++;
            }
        }

        await runtimeStore.SaveAllAsync(SortManagedProcesses(records), cancellationToken);
        return OperationResult.Ok(
            "已执行被监督暂停 MCP 的恢复。",
            string.Join(Environment.NewLine, new[]
            {
                "已恢复：" + resumed,
                "跳过：" + skipped
            }));
    }

    private async Task<McpRuntimeRecord> RecoverManagedProcessAsync(
        McpRuntimeRecord record,
        DateTimeOffset now,
        CancellationToken cancellationToken,
        bool ignoreSuspendedState = false,
        bool resetAttemptWindow = false)
    {
        var current = record;
        var attempts = resetAttemptWindow ? 0 : GetRestartAttemptsInWindow(current, now);
        if (!ignoreSuspendedState && current.SupervisorState == McpSupervisorState.SuspendedBySupervisor)
        {
            return current with
            {
                LastHealthMessage = string.IsNullOrWhiteSpace(current.LastHealthMessage)
                    ? "已被监督器暂停，请手动恢复。"
                    : current.LastHealthMessage
            };
        }

        var backoff = TimeSpan.FromSeconds(Math.Clamp(current.BackoffSeconds, 5, 3600));
        if (current.LastRestartAt.HasValue && now - current.LastRestartAt.Value < backoff)
        {
            return current with
            {
                SupervisorState = McpSupervisorState.BackingOff,
                ConsecutiveRestartFailures = attempts,
                LastHealthMessage = $"监督器退避中，将在 {backoff.TotalSeconds:0} 秒后重试。"
            };
        }

        if (attempts >= Math.Max(1, current.MaxRestartAttemptsInWindow))
        {
            return current with
            {
                SupervisorState = McpSupervisorState.SuspendedBySupervisor,
                ConsecutiveRestartFailures = attempts,
                LastHealthStatus = "异常",
                LastHealthMessage = $"监督器在窗口期内已达到 {current.MaxRestartAttemptsInWindow} 次重启上限，已暂停自动拉起。"
            };
        }

        var nextAttempt = attempts + 1;
        var startResult = await _mcpProcessController.StartAsync(current, cancellationToken);
        if (startResult.Result.Success)
        {
            return startResult.Record with
            {
                LastRestartAt = now,
                RestartCount = current.RestartCount + 1,
                ConsecutiveRestartFailures = nextAttempt,
                SupervisorState = McpSupervisorState.Recovering,
                LastHealthMessage = "监督器已重新拉起进程。"
            };
        }

        var failed = startResult.Record with
        {
            IsRunning = false,
            ProcessId = null,
            ProcessStartedAt = null,
            LastRestartAt = now,
            LastExitAt = startResult.Record.LastExitAt ?? now,
            ConsecutiveRestartFailures = nextAttempt,
            LastHealthStatus = "异常"
        };

        return nextAttempt >= Math.Max(1, current.MaxRestartAttemptsInWindow)
            ? failed with
            {
                SupervisorState = McpSupervisorState.SuspendedBySupervisor,
                LastHealthMessage = $"监督器重启失败并达到 {current.MaxRestartAttemptsInWindow} 次上限：{startResult.Result.Details ?? startResult.Result.Message}"
            }
            : failed with
            {
                SupervisorState = McpSupervisorState.BackingOff,
                LastHealthMessage = "监督器拉起失败，进入退避：" + (startResult.Result.Details ?? startResult.Result.Message)
            };
    }

    private static McpRuntimeRecord NormalizeSupervisorSettings(McpRuntimeRecord record)
    {
        return record with
        {
            BackoffSeconds = Math.Clamp(record.BackoffSeconds <= 0 ? 30 : record.BackoffSeconds, 5, 3600),
            RestartWindowMinutes = Math.Clamp(record.RestartWindowMinutes <= 0 ? 10 : record.RestartWindowMinutes, 1, 1440),
            MaxRestartAttemptsInWindow = Math.Clamp(record.MaxRestartAttemptsInWindow <= 0 ? 3 : record.MaxRestartAttemptsInWindow, 1, 20)
        };
    }

    private static McpRuntimeRecord NormalizeRunningSupervisorState(McpRuntimeRecord record, DateTimeOffset now)
    {
        if (record.SupervisorState == McpSupervisorState.Recovering && record.LastRestartAt.HasValue)
        {
            var window = TimeSpan.FromMinutes(Math.Max(1, record.RestartWindowMinutes));
            if (now - record.LastRestartAt.Value >= window)
            {
                return record with
                {
                    SupervisorState = McpSupervisorState.Idle,
                    ConsecutiveRestartFailures = 0,
                    LastHealthMessage = string.IsNullOrWhiteSpace(record.LastHealthMessage) ? "进程运行中。" : record.LastHealthMessage
                };
            }
        }

        if (record.SupervisorState is McpSupervisorState.BackingOff or McpSupervisorState.SuspendedBySupervisor)
        {
            return record with
            {
                SupervisorState = McpSupervisorState.Idle,
                LastHealthMessage = string.IsNullOrWhiteSpace(record.LastHealthMessage) ? "进程运行中。" : record.LastHealthMessage
            };
        }

        return record;
    }

    private static McpRuntimeRecord PrepareManualStart(McpRuntimeRecord record)
    {
        return record with
        {
            SupervisorState = McpSupervisorState.Idle,
            ConsecutiveRestartFailures = 0
        };
    }

    private static McpRuntimeRecord FinalizeManualStart(McpRuntimeRecord record, bool success)
    {
        return success
            ? record with
            {
                SupervisorState = McpSupervisorState.Idle,
                ConsecutiveRestartFailures = 0,
                LastHealthMessage = string.IsNullOrWhiteSpace(record.LastHealthMessage) ? "进程运行中。" : record.LastHealthMessage
            }
            : record;
    }

    private static McpRuntimeRecord ClearSupervisorState(McpRuntimeRecord record)
    {
        return record with
        {
            SupervisorState = McpSupervisorState.Idle,
            ConsecutiveRestartFailures = 0
        };
    }

    private static int GetRestartAttemptsInWindow(McpRuntimeRecord record, DateTimeOffset now)
    {
        if (!record.LastRestartAt.HasValue)
        {
            return 0;
        }

        var window = TimeSpan.FromMinutes(Math.Max(1, record.RestartWindowMinutes));
        return now - record.LastRestartAt.Value <= window
            ? Math.Max(0, record.ConsecutiveRestartFailures)
            : 0;
    }
}
