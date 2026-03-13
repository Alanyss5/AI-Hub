using System.Diagnostics;
using AIHub.Application.Abstractions;
using AIHub.Contracts;

namespace AIHub.Infrastructure;

internal static class PowerShellScriptRunner
{
    public static async Task<OperationResult> RunAsync(
        string scriptPath,
        IReadOnlyList<string> arguments,
        string successMessage,
        string failureMessage,
        CancellationToken cancellationToken,
        IDiagnosticLogService? diagnosticLogService = null)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!OperatingSystem.IsWindows())
        {
            return OperationResult.Fail("当前版本仅实现了 Windows 脚本执行。", scriptPath);
        }

        if (!File.Exists(scriptPath))
        {
            return OperationResult.Fail("脚本不存在。", scriptPath);
        }

        var processStartInfo = new ProcessStartInfo
        {
            FileName = GetPowerShellPath(),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        processStartInfo.ArgumentList.Add("-ExecutionPolicy");
        processStartInfo.ArgumentList.Add("Bypass");
        processStartInfo.ArgumentList.Add("-File");
        processStartInfo.ArgumentList.Add(scriptPath);

        foreach (var argument in arguments)
        {
            processStartInfo.ArgumentList.Add(argument);
        }

        diagnosticLogService?.RecordInfo("powershell", "开始执行 PowerShell 脚本。", string.Join(Environment.NewLine, new[]
        {
            "脚本：" + scriptPath,
            "参数：" + string.Join(' ', arguments)
        }));

        using var process = new Process { StartInfo = processStartInfo };
        process.Start();

        var standardOutputTask = process.StandardOutput.ReadToEndAsync();
        var standardErrorTask = process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync(cancellationToken);

        var standardOutput = (await standardOutputTask).Trim();
        var standardError = (await standardErrorTask).Trim();
        var details = string.Join(
            Environment.NewLine + Environment.NewLine,
            new[] { standardOutput, standardError }.Where(value => !string.IsNullOrWhiteSpace(value)));

        if (process.ExitCode == 0)
        {
            diagnosticLogService?.RecordInfo("powershell", successMessage, details);
            return OperationResult.Ok(successMessage, details);
        }

        diagnosticLogService?.RecordWarning("powershell", failureMessage, details);
        return OperationResult.Fail(failureMessage, details);
    }

    private static string GetPowerShellPath()
    {
        var systemDirectory = Environment.SystemDirectory;
        var candidate = Path.Combine(systemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
        return File.Exists(candidate) ? candidate : "powershell.exe";
    }
}