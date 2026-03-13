using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Http;
using System.Text;
using AIHub.Application.Abstractions;
using AIHub.Application.Models;
using AIHub.Contracts;

namespace AIHub.Infrastructure;

public sealed class LocalMcpProcessController : IMcpProcessController
{
    private static readonly HttpClient HttpClient = new();
    private static readonly UTF8Encoding Utf8NoBom = new(false);
    private readonly ConcurrentDictionary<string, TrackedProcess> _trackedProcesses = new(StringComparer.OrdinalIgnoreCase);
    private readonly Func<string?> _hubRootAccessor;
    private readonly IDiagnosticLogService? _diagnosticLogService;

    public LocalMcpProcessController()
        : this(() => null, null)
    {
    }

    public LocalMcpProcessController(Func<string?> hubRootAccessor, IDiagnosticLogService? diagnosticLogService = null)
    {
        _hubRootAccessor = hubRootAccessor ?? (() => null);
        _diagnosticLogService = diagnosticLogService;
    }

    public async Task<McpRuntimeRecord> RefreshAsync(McpRuntimeRecord record, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (record.Mode != McpServerMode.ProcessManaged)
        {
            return EnrichLogSnippets(record with
            {
                IsRunning = false,
                ProcessId = null,
                ProcessStartedAt = null,
                LastHealthStatus = "不适用",
                LastHealthMessage = "该条目不是托管型 MCP。",
                LastCheckedAt = DateTimeOffset.Now
            });
        }

        if (_trackedProcesses.TryGetValue(record.Name, out var tracked))
        {
            return await BuildProcessStateAsync(record, tracked.Process, tracked.OutputLogPath, tracked.ErrorLogPath, true, cancellationToken);
        }

        using var attachedProcess = TryGetAttachedProcess(record);
        return await BuildProcessStateAsync(record, attachedProcess, record.StandardOutputLogPath, record.StandardErrorLogPath, false, cancellationToken);
    }

    public async Task<McpProcessCommandResult> StartAsync(McpRuntimeRecord record, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var refreshedRecord = await RefreshAsync(record, cancellationToken);
        if (!refreshedRecord.IsEnabled)
        {
            var disabledRecord = refreshedRecord with
            {
                LastHealthStatus = "已禁用",
                LastHealthMessage = "该托管进程已禁用，无法启动。",
                LastCheckedAt = DateTimeOffset.Now
            };

            return new McpProcessCommandResult(OperationResult.Fail("该托管进程已禁用。", disabledRecord.Name), disabledRecord);
        }

        if (refreshedRecord.IsRunning)
        {
            return new McpProcessCommandResult(
                OperationResult.Ok("托管 MCP 已在运行中。", refreshedRecord.ProcessId?.ToString()),
                refreshedRecord);
        }

        if (string.IsNullOrWhiteSpace(refreshedRecord.Command))
        {
            var invalidRecord = refreshedRecord with
            {
                LastHealthStatus = "异常",
                LastHealthMessage = "启动命令为空。",
                LastCheckedAt = DateTimeOffset.Now
            };

            return new McpProcessCommandResult(OperationResult.Fail("启动命令为空，无法启动托管 MCP。"), invalidRecord);
        }

        if (!string.IsNullOrWhiteSpace(refreshedRecord.WorkingDirectory) && !Directory.Exists(refreshedRecord.WorkingDirectory))
        {
            var invalidDirectoryRecord = refreshedRecord with
            {
                LastHealthStatus = "异常",
                LastHealthMessage = "工作目录不存在。",
                LastCheckedAt = DateTimeOffset.Now
            };

            return new McpProcessCommandResult(OperationResult.Fail("工作目录不存在。", refreshedRecord.WorkingDirectory), invalidDirectoryRecord);
        }

        try
        {
            var (outputLogPath, errorLogPath) = CreateLogPaths(refreshedRecord.Name);
            InitializeLogFile(outputLogPath, "stdout");
            InitializeLogFile(errorLogPath, "stderr");

            var process = new Process
            {
                StartInfo = BuildStartInfo(refreshedRecord)
            };

            process.Start();
            _diagnosticLogService?.RecordInfo(
                "managed-mcp",
                "已启动托管 MCP。",
                refreshedRecord.Name + Environment.NewLine + BuildCommandLine(refreshedRecord));

            var tracked = new TrackedProcess(
                process,
                outputLogPath,
                errorLogPath,
                PumpStreamAsync(process.StandardOutput, outputLogPath),
                PumpStreamAsync(process.StandardError, errorLogPath));

            _trackedProcesses.AddOrUpdate(refreshedRecord.Name, tracked, (_, previous) =>
            {
                previous.Dispose();
                return tracked;
            });

            var startedRecord = await RefreshAsync(refreshedRecord with
            {
                IsRunning = true,
                ProcessId = process.Id,
                ProcessStartedAt = ToDateTimeOffset(process.StartTime),
                LastExitCode = null,
                StandardOutputLogPath = outputLogPath,
                StandardErrorLogPath = errorLogPath,
                LastHealthStatus = "启动中",
                LastHealthMessage = "进程已启动。",
                LastCheckedAt = DateTimeOffset.Now
            }, cancellationToken);

            return new McpProcessCommandResult(OperationResult.Ok("已启动托管 MCP。", startedRecord.ProcessId?.ToString()), startedRecord);
        }
        catch (Exception exception)
        {
            var failedRecord = refreshedRecord with
            {
                IsRunning = false,
                ProcessId = null,
                ProcessStartedAt = null,
                LastHealthStatus = "异常",
                LastHealthMessage = exception.Message,
                LastCheckedAt = DateTimeOffset.Now
            };

            _diagnosticLogService?.RecordError(
                "managed-mcp",
                "启动托管 MCP 失败。",
                exception,
                refreshedRecord.Name + Environment.NewLine + BuildCommandLine(refreshedRecord));
            return new McpProcessCommandResult(OperationResult.Fail("启动托管 MCP 失败。", exception.Message), failedRecord);
        }
    }

    public async Task<McpProcessCommandResult> StopAsync(McpRuntimeRecord record, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var refreshedRecord = await RefreshAsync(record, cancellationToken);
        if (!refreshedRecord.IsRunning)
        {
            var stoppedRecord = refreshedRecord with
            {
                LastHealthStatus = refreshedRecord.IsEnabled ? "已停止" : "已禁用",
                LastHealthMessage = refreshedRecord.IsEnabled ? "进程已经处于停止状态。" : "进程已禁用。",
                LastCheckedAt = DateTimeOffset.Now
            };

            return new McpProcessCommandResult(OperationResult.Ok("托管 MCP 已停止。", stoppedRecord.Name), stoppedRecord);
        }

        Process? process = null;
        TrackedProcess? tracked = null;
        var ownsProcess = false;

        if (_trackedProcesses.TryGetValue(refreshedRecord.Name, out tracked))
        {
            process = tracked.Process;
        }
        else
        {
            process = TryGetAttachedProcess(refreshedRecord);
            ownsProcess = true;
        }

        if (process is null)
        {
            var missingRecord = EnrichLogSnippets(refreshedRecord with
            {
                IsRunning = false,
                ProcessId = null,
                ProcessStartedAt = null,
                LastHealthStatus = "已停止",
                LastHealthMessage = "进程状态已失效，已视为停止。",
                LastCheckedAt = DateTimeOffset.Now
            });

            return new McpProcessCommandResult(OperationResult.Ok("托管 MCP 已视为停止。", missingRecord.Name), missingRecord);
        }

        try
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(cancellationToken);

            if (tracked is not null)
            {
                _trackedProcesses.TryRemove(refreshedRecord.Name, out _);
                await AwaitPumpTasksAsync(tracked);
                tracked.Dispose();
            }

            var exitCode = TryGetExitCode(process);
            _diagnosticLogService?.RecordInfo("managed-mcp", "已停止托管 MCP。", refreshedRecord.Name);
            var stoppedRecord = EnrichLogSnippets(refreshedRecord with
            {
                IsRunning = false,
                ProcessId = null,
                ProcessStartedAt = null,
                LastExitCode = exitCode,
                LastHealthStatus = "已停止",
                LastHealthMessage = "进程已停止。",
                LastCheckedAt = DateTimeOffset.Now
            });

            return new McpProcessCommandResult(OperationResult.Ok("已停止托管 MCP。", stoppedRecord.Name), stoppedRecord);
        }
        catch (Exception exception)
        {
            var failedRecord = EnrichLogSnippets(refreshedRecord with
            {
                LastHealthStatus = "异常",
                LastHealthMessage = exception.Message,
                LastCheckedAt = DateTimeOffset.Now
            });

            _diagnosticLogService?.RecordError("managed-mcp", "停止托管 MCP 失败。", exception, refreshedRecord.Name);
            return new McpProcessCommandResult(OperationResult.Fail("停止托管 MCP 失败。", exception.Message), failedRecord);
        }
        finally
        {
            if (ownsProcess)
            {
                process.Dispose();
            }
        }
    }

    private async Task<McpRuntimeRecord> BuildProcessStateAsync(
        McpRuntimeRecord record,
        Process? process,
        string? outputLogPath,
        string? errorLogPath,
        bool tracked,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.Now;
        if (process is null)
        {
            if (tracked && _trackedProcesses.TryRemove(record.Name, out var removed))
            {
                await AwaitPumpTasksAsync(removed);
                removed.Dispose();
            }

            return EnrichLogSnippets(record with
            {
                IsRunning = false,
                ProcessId = null,
                ProcessStartedAt = null,
                LastHealthStatus = record.IsEnabled ? "已停止" : "已禁用",
                LastHealthMessage = record.IsEnabled ? "进程未运行。" : "进程已禁用。",
                LastCheckedAt = now
            });
        }

        if (process.HasExited)
        {
            if (tracked && _trackedProcesses.TryRemove(record.Name, out var removed))
            {
                await AwaitPumpTasksAsync(removed);
                removed.Dispose();
            }

            return EnrichLogSnippets(record with
            {
                IsRunning = false,
                ProcessId = null,
                ProcessStartedAt = null,
                LastExitCode = TryGetExitCode(process),
                LastHealthStatus = "已停止",
                LastHealthMessage = "进程已退出。",
                LastCheckedAt = now
            });
        }

        var runningRecord = record with
        {
            IsRunning = true,
            ProcessId = process.Id,
            ProcessStartedAt = ToDateTimeOffset(process.StartTime),
            LastExitCode = null,
            StandardOutputLogPath = outputLogPath ?? record.StandardOutputLogPath,
            StandardErrorLogPath = errorLogPath ?? record.StandardErrorLogPath,
            LastCheckedAt = now
        };

        runningRecord = await ApplyHealthStateAsync(runningRecord, cancellationToken);
        return EnrichLogSnippets(runningRecord with { LastCheckedAt = now });
    }

    private async Task<McpRuntimeRecord> ApplyHealthStateAsync(McpRuntimeRecord record, CancellationToken cancellationToken)
    {
        if (!record.IsEnabled)
        {
            return record with
            {
                LastHealthStatus = "已禁用",
                LastHealthMessage = "进程已禁用。"
            };
        }

        if (!record.IsRunning)
        {
            return record with
            {
                LastHealthStatus = "已停止",
                LastHealthMessage = "进程未运行。"
            };
        }

        if (string.IsNullOrWhiteSpace(record.HealthCheckUrl))
        {
            return record with
            {
                LastHealthStatus = "运行中",
                LastHealthMessage = "进程运行中。"
            };
        }

        if (!Uri.TryCreate(record.HealthCheckUrl, UriKind.Absolute, out var healthUri))
        {
            return record with
            {
                LastHealthStatus = "异常",
                LastHealthMessage = "健康检查地址无效。"
            };
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(record.HealthCheckTimeoutSeconds, 1, 60)));

        try
        {
            using var response = await HttpClient.GetAsync(healthUri, timeoutCts.Token);
            if (response.IsSuccessStatusCode)
            {
                return record with
                {
                    LastHealthStatus = "健康",
                    LastHealthMessage = "健康检查通过。"
                };
            }

            var failureMessage = $"健康检查返回 {(int)response.StatusCode}。";
            _diagnosticLogService?.RecordWarning("managed-mcp", "健康检查失败。", record.Name + Environment.NewLine + failureMessage);
            return record with
            {
                LastHealthStatus = "异常",
                LastHealthMessage = failureMessage
            };
        }
        catch (Exception exception)
        {
            var failureMessage = "健康检查失败：" + exception.Message;
            _diagnosticLogService?.RecordWarning("managed-mcp", "健康检查失败。", record.Name + Environment.NewLine + failureMessage);
            return record with
            {
                LastHealthStatus = "异常",
                LastHealthMessage = failureMessage
            };
        }
    }

    private McpRuntimeRecord EnrichLogSnippets(McpRuntimeRecord record)
    {
        return record with
        {
            LastOutputSnippet = ReadTail(record.StandardOutputLogPath),
            LastErrorSnippet = ReadTail(record.StandardErrorLogPath)
        };
    }

    private static ProcessStartInfo BuildStartInfo(McpRuntimeRecord record)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = record.Command,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = false,
            CreateNoWindow = true
        };

        if (!string.IsNullOrWhiteSpace(record.WorkingDirectory))
        {
            startInfo.WorkingDirectory = record.WorkingDirectory;
        }

        foreach (var argument in record.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        foreach (var variable in record.EnvironmentVariables)
        {
            startInfo.Environment[variable.Key] = variable.Value ?? string.Empty;
        }

        return startInfo;
    }

    private (string OutputLogPath, string ErrorLogPath) CreateLogPaths(string name)
    {
        var root = _hubRootAccessor();
        var logDirectory = string.IsNullOrWhiteSpace(root)
            ? Path.Combine(Path.GetTempPath(), "AIHub", "mcp", "logs")
            : Path.Combine(root, "mcp", "logs");

        Directory.CreateDirectory(logDirectory);

        var safeName = SanitizeFileName(name);
        var timestamp = DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss");
        return (
            Path.Combine(logDirectory, $"{safeName}-{timestamp}.out.log"),
            Path.Combine(logDirectory, $"{safeName}-{timestamp}.err.log"));
    }

    private static void InitializeLogFile(string path, string streamName)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, $"[{DateTimeOffset.Now:O}] {streamName} begin{Environment.NewLine}", Utf8NoBom);
    }

    private static async Task PumpStreamAsync(StreamReader reader, string path)
    {
        await using var writer = new StreamWriter(path, append: true, Utf8NoBom);

        while (true)
        {
            var line = await reader.ReadLineAsync();
            if (line is null)
            {
                break;
            }

            await writer.WriteLineAsync(line);
            await writer.FlushAsync();
        }
    }

    private static string ReadTail(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return "暂无日志内容。";
        }

        try
        {
            var lines = File.ReadAllLines(path);
            var tail = lines.Skip(Math.Max(0, lines.Length - 12));
            var text = string.Join(Environment.NewLine, tail).Trim();

            if (string.IsNullOrWhiteSpace(text))
            {
                return "暂无日志内容。";
            }

            return text.Length <= 1600 ? text : text[^1600..];
        }
        catch (Exception exception)
        {
            return "读取日志失败：" + exception.Message;
        }
    }

    private static Process? TryGetAttachedProcess(McpRuntimeRecord record)
    {
        if (record.ProcessId is null)
        {
            return null;
        }

        try
        {
            var process = Process.GetProcessById(record.ProcessId.Value);
            if (process.HasExited)
            {
                process.Dispose();
                return null;
            }

            if (record.ProcessStartedAt.HasValue)
            {
                var actualStart = ToDateTimeOffset(process.StartTime);
                var delta = (actualStart - record.ProcessStartedAt.Value).Duration();
                if (delta > TimeSpan.FromSeconds(2))
                {
                    process.Dispose();
                    return null;
                }
            }

            return process;
        }
        catch
        {
            return null;
        }
    }

    private static int? TryGetExitCode(Process process)
    {
        try
        {
            return process.ExitCode;
        }
        catch
        {
            return null;
        }
    }

    private static async Task AwaitPumpTasksAsync(TrackedProcess tracked)
    {
        try
        {
            await Task.WhenAll(tracked.OutputPumpTask, tracked.ErrorPumpTask);
        }
        catch
        {
        }
    }

    private static string SanitizeFileName(string value)
    {
        var invalidCharacters = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(value.Length);

        foreach (var character in value)
        {
            builder.Append(invalidCharacters.Contains(character) ? '_' : character);
        }

        return builder.ToString();
    }

    private static DateTimeOffset ToDateTimeOffset(DateTime value)
    {
        return value.Kind == DateTimeKind.Unspecified
            ? new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Local))
            : new DateTimeOffset(value);
    }

    private static string BuildCommandLine(McpRuntimeRecord record)
    {
        return record.Arguments.Length == 0
            ? record.Command
            : record.Command + " " + string.Join(" ", record.Arguments.Select(QuoteArgument));
    }

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

    private sealed class TrackedProcess : IDisposable
    {
        public TrackedProcess(
            Process process,
            string outputLogPath,
            string errorLogPath,
            Task outputPumpTask,
            Task errorPumpTask)
        {
            Process = process;
            OutputLogPath = outputLogPath;
            ErrorLogPath = errorLogPath;
            OutputPumpTask = outputPumpTask;
            ErrorPumpTask = errorPumpTask;
        }

        public Process Process { get; }

        public string OutputLogPath { get; }

        public string ErrorLogPath { get; }

        public Task OutputPumpTask { get; }

        public Task ErrorPumpTask { get; }

        public void Dispose()
        {
            Process.Dispose();
        }
    }
}
