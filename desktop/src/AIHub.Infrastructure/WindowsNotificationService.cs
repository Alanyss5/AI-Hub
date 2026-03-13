using System.Diagnostics;
using System.Text;
using AIHub.Application.Abstractions;
using AIHub.Application.Models;

namespace AIHub.Infrastructure;

public sealed class WindowsNotificationService : INotificationService
{
    private readonly IDiagnosticLogService? _diagnosticLogService;

    public WindowsNotificationService(IDiagnosticLogService? diagnosticLogService = null)
    {
        _diagnosticLogService = diagnosticLogService;
    }

    public Task NotifyAsync(MaintenanceAlertRecord alert, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!OperatingSystem.IsWindows())
        {
            return Task.CompletedTask;
        }

        var title = Sanitize(alert.Title, 64);
        var message = Sanitize(BuildMessage(alert), 240);
        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(message))
        {
            return Task.CompletedTask;
        }

        try
        {
            var script = $$"""
                Add-Type -AssemblyName System.Windows.Forms
                Add-Type -AssemblyName System.Drawing
                $notify = New-Object System.Windows.Forms.NotifyIcon
                $notify.Icon = [System.Drawing.SystemIcons]::Warning
                $notify.BalloonTipIcon = [System.Windows.Forms.ToolTipIcon]::Warning
                $notify.BalloonTipTitle = '{{EscapePowerShellLiteral(title)}}'
                $notify.BalloonTipText = '{{EscapePowerShellLiteral(message)}}'
                $notify.Visible = $true
                $notify.ShowBalloonTip(5000)
                Start-Sleep -Milliseconds 5500
                $notify.Dispose()
                """;

            var encodedCommand = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
            var startInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoProfile -NonInteractive -WindowStyle Hidden -EncodedCommand " + encodedCommand,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            Process.Start(startInfo);
            _diagnosticLogService?.RecordInfo("notification", "已发送系统通知。", title + Environment.NewLine + message);
        }
        catch (Exception exception)
        {
            _diagnosticLogService?.RecordWarning("notification", "发送系统通知失败。", exception.Message);
        }

        return Task.CompletedTask;
    }

    private static string BuildMessage(MaintenanceAlertRecord alert)
    {
        return string.IsNullOrWhiteSpace(alert.Details)
            ? alert.Message
            : alert.Message + Environment.NewLine + alert.Details;
    }

    private static string Sanitize(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var sanitized = value
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Trim();

        return sanitized.Length <= maxLength
            ? sanitized
            : sanitized[..(maxLength - 1)] + "…";
    }

    private static string EscapePowerShellLiteral(string value)
    {
        return value.Replace("'", "''", StringComparison.Ordinal);
    }
}