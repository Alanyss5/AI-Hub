using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AIHub.Application.Abstractions;
using AIHub.Application.Models;
using AIHub.Contracts;

namespace AIHub.Infrastructure;

public sealed class FileDiagnosticLogService : IDiagnosticLogService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private static readonly Regex SecretRegex = new(
        @"(?im)(?<key>(token|api[_-]?key|secret|password|passwd|authorization))(?<separator>\s*[:=]\s*)(?<value>[^\r\n,;]+)",
        RegexOptions.Compiled);

    private static readonly Regex EnvSecretRegex = new(
        @"(?im)(?<key>[A-Z0-9_]*(TOKEN|SECRET|PASSWORD|KEY)[A-Z0-9_]*)(?<separator>\s*=\s*)(?<value>[^\r\n]+)",
        RegexOptions.Compiled);

    private static readonly UTF8Encoding Utf8NoBom = new(false);
    private readonly object _gate = new();
    private readonly string _rootPath;
    private readonly string _logDirectory;
    private readonly string _statePath;
    private readonly string _userHome;

    public FileDiagnosticLogService(string? rootPath = null)
    {
        _rootPath = string.IsNullOrWhiteSpace(rootPath)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AIHub", "diagnostics")
            : Path.GetFullPath(rootPath);
        _logDirectory = Path.Combine(_rootPath, "logs");
        _statePath = Path.Combine(_rootPath, "state.json");
        _userHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        Directory.CreateDirectory(_logDirectory);
    }

    public void RecordInfo(string category, string message, string? details = null)
    {
        WriteEntry("INFO", category, message, details);
    }

    public void RecordWarning(string category, string message, string? details = null)
    {
        WriteEntry("WARN", category, message, details);
    }

    public void RecordError(string category, string message, Exception? exception = null, string? details = null)
    {
        var combinedDetails = BuildExceptionDetails(details, exception);
        WriteEntry("ERROR", category, message, combinedDetails);
    }

    public void RecordStartupFailure(string stage, Exception exception)
    {
        var message = $"启动失败：{stage}";
        var details = BuildExceptionDetails(null, exception);

        lock (_gate)
        {
            WriteEntryCore("FATAL", "startup", message, details);
            var state = LoadStateCore();
            state.LastStartupFailureAt = DateTimeOffset.Now;
            state.LastStartupFailureSummary = Redact(message + " / " + exception.Message);
            state.LastStartupFailureDetails = Redact(details);
            SaveStateCore(state);
        }
    }

    public void RecordUnhandledException(string stage, Exception exception)
    {
        var message = $"未处理异常：{stage}";
        var details = BuildExceptionDetails(null, exception);

        lock (_gate)
        {
            WriteEntryCore("FATAL", "unhandled", message, details);
            var state = LoadStateCore();
            state.LastUnhandledExceptionAt = DateTimeOffset.Now;
            state.LastUnhandledExceptionSummary = Redact(message + " / " + exception.Message);
            state.LastUnhandledExceptionDetails = Redact(details);
            SaveStateCore(state);
        }
    }

    public Task<DiagnosticSnapshot> LoadSnapshotAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            var state = LoadStateCore();
            return Task.FromResult(new DiagnosticSnapshot
            {
                DiagnosticsRoot = _rootPath,
                LastStartupFailureAt = state.LastStartupFailureAt,
                LatestStartupFailureSummary = state.LastStartupFailureSummary ?? string.Empty,
                LatestStartupFailureDetails = state.LastStartupFailureDetails ?? string.Empty,
                LastUnhandledExceptionAt = state.LastUnhandledExceptionAt,
                LatestUnhandledExceptionSummary = state.LastUnhandledExceptionSummary ?? string.Empty,
                LatestUnhandledExceptionDetails = state.LastUnhandledExceptionDetails ?? string.Empty,
                LastExportedAt = state.LastExportedAt,
                LastExportPath = state.LastExportPath ?? string.Empty
            });
        }
    }

    public Task<OperationResult> ExportBundleAsync(string destinationPath, string? hubRoot = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(destinationPath))
        {
            return Task.FromResult(OperationResult.Fail("请先选择诊断包导出路径。"));
        }

        try
        {
            var fullDestinationPath = Path.GetFullPath(destinationPath);
            var destinationDirectory = Path.GetDirectoryName(fullDestinationPath);
            if (!string.IsNullOrWhiteSpace(destinationDirectory))
            {
                Directory.CreateDirectory(destinationDirectory);
            }

            var exportRoot = Path.Combine(Path.GetTempPath(), "AIHub", "diagnostics-export", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(exportRoot);

            try
            {
                lock (_gate)
                {
                    CopyDirectory(_logDirectory, Path.Combine(exportRoot, "logs"));
                    CopyFileIfExists(_statePath, Path.Combine(exportRoot, "state.json"));
                }

                WriteSummary(exportRoot, hubRoot);
                CopyHubStateIfExists(exportRoot, hubRoot);

                if (File.Exists(fullDestinationPath))
                {
                    File.Delete(fullDestinationPath);
                }

                ZipFile.CreateFromDirectory(exportRoot, fullDestinationPath, CompressionLevel.Optimal, includeBaseDirectory: false);

                lock (_gate)
                {
                    var state = LoadStateCore();
                    state.LastExportedAt = DateTimeOffset.Now;
                    state.LastExportPath = fullDestinationPath;
                    SaveStateCore(state);
                    WriteEntryCore("INFO", "diagnostics", "已导出诊断包。", fullDestinationPath);
                }

                return Task.FromResult(OperationResult.Ok("诊断包已导出。", fullDestinationPath));
            }
            finally
            {
                if (Directory.Exists(exportRoot))
                {
                    Directory.Delete(exportRoot, recursive: true);
                }
            }
        }
        catch (Exception exception)
        {
            RecordError("diagnostics", "导出诊断包失败。", exception);
            return Task.FromResult(OperationResult.Fail("导出诊断包失败。", exception.Message));
        }
    }

    private void WriteEntry(string level, string category, string message, string? details)
    {
        lock (_gate)
        {
            WriteEntryCore(level, category, message, details);
        }
    }

    private void WriteEntryCore(string level, string category, string message, string? details)
    {
        try
        {
            Directory.CreateDirectory(_logDirectory);
            RotateLogs();

            var logPath = Path.Combine(_logDirectory, $"{DateTimeOffset.Now:yyyyMMdd}.log");
            var builder = new StringBuilder();
            builder.Append(DateTimeOffset.Now.ToString("O"));
            builder.Append(' ');
            builder.Append('[').Append(level).Append("] ");
            builder.Append('[').Append(category).Append("] ");
            builder.AppendLine(Redact(message));

            if (!string.IsNullOrWhiteSpace(details))
            {
                builder.AppendLine(Redact(details));
            }

            File.AppendAllText(logPath, builder.ToString(), Utf8NoBom);
        }
        catch
        {
        }
    }

    private void RotateLogs()
    {
        try
        {
            var files = new DirectoryInfo(_logDirectory)
                .GetFiles("*.log")
                .OrderByDescending(file => file.CreationTimeUtc)
                .ThenByDescending(file => file.LastWriteTimeUtc)
                .ToArray();

            foreach (var staleFile in files.Skip(14))
            {
                staleFile.Delete();
            }
        }
        catch
        {
        }
    }

    private void WriteSummary(string exportRoot, string? hubRoot)
    {
        var snapshot = LoadSnapshotAsync().GetAwaiter().GetResult();
        var summary = new
        {
            exportedAt = DateTimeOffset.Now,
            diagnosticsRoot = snapshot.DiagnosticsRoot,
            hubRoot = string.IsNullOrWhiteSpace(hubRoot) ? null : Path.GetFullPath(hubRoot),
            lastStartupFailureAt = snapshot.LastStartupFailureAt,
            latestStartupFailureSummary = snapshot.LatestStartupFailureSummary,
            latestUnhandledExceptionSummary = snapshot.LatestUnhandledExceptionSummary,
            lastExportedAt = snapshot.LastExportedAt,
            lastExportPath = snapshot.LastExportPath
        };

        File.WriteAllText(
            Path.Combine(exportRoot, "summary.json"),
            JsonSerializer.Serialize(summary, SerializerOptions),
            Utf8NoBom);
    }

    private void CopyHubStateIfExists(string exportRoot, string? hubRoot)
    {
        if (string.IsNullOrWhiteSpace(hubRoot) || !Directory.Exists(hubRoot))
        {
            return;
        }

        var fullHubRoot = Path.GetFullPath(hubRoot);
        var files = new[]
        {
            Path.Combine(fullHubRoot, "config", "hub-settings.json"),
            Path.Combine(fullHubRoot, "projects", "projects.json"),
            Path.Combine(fullHubRoot, "mcp", "runtime.json"),
            Path.Combine(fullHubRoot, "mcp", "manifest", "global.json"),
            Path.Combine(fullHubRoot, "mcp", "manifest", "frontend.json"),
            Path.Combine(fullHubRoot, "mcp", "manifest", "backend.json")
        };

        foreach (var file in files)
        {
            CopyRedactedTextFileIfExists(file, Path.Combine(exportRoot, "hub-state", Path.GetRelativePath(fullHubRoot, file)));
        }
    }

    private void CopyRedactedTextFileIfExists(string sourcePath, string destinationPath)
    {
        if (!File.Exists(sourcePath))
        {
            return;
        }

        var directory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var content = File.ReadAllText(sourcePath);
        File.WriteAllText(destinationPath, Redact(content), Utf8NoBom);
    }

    private static void CopyDirectory(string sourceDirectory, string destinationDirectory)
    {
        if (!Directory.Exists(sourceDirectory))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(destinationDirectory, Path.GetRelativePath(sourceDirectory, file));
            var directory = Path.GetDirectoryName(target);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.Copy(file, target, overwrite: true);
        }
    }

    private static void CopyFileIfExists(string sourcePath, string destinationPath)
    {
        if (!File.Exists(sourcePath))
        {
            return;
        }

        var directory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.Copy(sourcePath, destinationPath, overwrite: true);
    }

    private DiagnosticStateRecord LoadStateCore()
    {
        try
        {
            if (!File.Exists(_statePath))
            {
                return new DiagnosticStateRecord();
            }

            var json = File.ReadAllText(_statePath);
            return JsonSerializer.Deserialize<DiagnosticStateRecord>(json, SerializerOptions) ?? new DiagnosticStateRecord();
        }
        catch
        {
            return new DiagnosticStateRecord();
        }
    }

    private void SaveStateCore(DiagnosticStateRecord state)
    {
        try
        {
            Directory.CreateDirectory(_rootPath);
            File.WriteAllText(_statePath, JsonSerializer.Serialize(state, SerializerOptions), Utf8NoBom);
        }
        catch
        {
        }
    }

    private string Redact(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var redacted = value.Replace(_userHome, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);
        redacted = SecretRegex.Replace(redacted, match => match.Groups["key"].Value + match.Groups["separator"].Value + "***");
        redacted = EnvSecretRegex.Replace(redacted, match => match.Groups["key"].Value + match.Groups["separator"].Value + "***");
        return redacted;
    }

    private static string BuildExceptionDetails(string? details, Exception? exception)
    {
        var sections = new List<string>();
        if (!string.IsNullOrWhiteSpace(details))
        {
            sections.Add(details);
        }

        if (exception is not null)
        {
            sections.Add(exception.ToString());
        }

        return string.Join(Environment.NewLine + Environment.NewLine, sections);
    }

    private sealed class DiagnosticStateRecord
    {
        public DateTimeOffset? LastStartupFailureAt { get; set; }

        public string? LastStartupFailureSummary { get; set; }

        public string? LastStartupFailureDetails { get; set; }

        public DateTimeOffset? LastUnhandledExceptionAt { get; set; }

        public string? LatestUnhandledExceptionSummary => LastUnhandledExceptionSummary;

        public string? LastUnhandledExceptionSummary { get; set; }

        public string? LastUnhandledExceptionDetails { get; set; }

        public DateTimeOffset? LastExportedAt { get; set; }

        public string? LastExportPath { get; set; }
    }
}