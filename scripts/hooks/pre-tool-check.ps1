param(
  [string]$Profile = "global"
)

$timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
Write-Output "[AI-Hub] PreToolUse hook executed for profile '$Profile' at $timestamp"
exit 0
