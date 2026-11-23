param(
    [int]$Duration = 300,
    [int]$Pid = 0,
    [string]$Out = "artifacts/etw-validation.json"
)

# Запуск ETW-проверки от имени администратора.
# Скрипт НЕ обходит UAC: это только ярлык, который вы запускаете явно.
$root = Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path))
$project = Join-Path $root "tools/EtwProbe/EtwProbe.csproj"

$arguments = @("run", "--project", $project, "-c", "Release", "--", "--duration", $Duration, "--out", $Out)
if ($Pid -gt 0) {
    $arguments += @("--pid", $Pid)
}

$psi = New-Object System.Diagnostics.ProcessStartInfo
$psi.FileName = "dotnet.exe"
$psi.Arguments = ($arguments | ForEach-Object { '"' + $_ + '"' }) -join ' '
$psi.WorkingDirectory = $root
$psi.UseShellExecute = $true
$psi.Verb = "RunAs"

try {
    $process = [System.Diagnostics.Process]::Start($psi)
    $process.WaitForExit()
    exit $process.ExitCode
}
catch {
    Write-Host "Elevation was not granted: $($_.Exception.Message)"
    Write-Host "Run PowerShell as Administrator manually and execute:"
    Write-Host "  dotnet run --project `"$project`" -c Release -- --duration $Duration --out `"$Out`""
    exit 3
}
