$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$root = Split-Path -Parent $root
$shell = New-Object -ComObject WScript.Shell

function New-Link {
    param(
        [string]$Name,
        [string]$Target,
        [string]$Arguments = '',
        [string]$WorkingDirectory = '',
        [string]$IconLocation = ''
    )

    if (-not (Test-Path $Target)) {
        Write-Host "SKIP $Name : target not found ($Target)"
        return
    }

    $path = Join-Path $root "$Name.lnk"
    $link = $shell.CreateShortcut($path)
    $link.TargetPath = $Target
    $link.Arguments = $Arguments
    $link.WorkingDirectory = if ($WorkingDirectory) { $WorkingDirectory } else { Split-Path -Parent $Target }
    if ($IconLocation) { $link.IconLocation = $IconLocation }
    $link.Save()
    Write-Host "CREATED $path"
}

$releaseExe = Join-Path $root 'src\DotaChecker.App\bin\Release\net10.0-windows\DotaChecker.App.exe'
$debugExe = Join-Path $root 'src\DotaChecker.App\bin\Debug\net10.0-windows\DotaChecker.App.exe'
$warpscoutExe = Join-Path $root 'tools\warpscout\warpscout.exe'
$etwScript = Join-Path $root 'tools\EtwProbe\run-elevated.ps1'

New-Link -Name 'Dota Checker' -Target $releaseExe
New-Link -Name 'Dota Checker (Debug)' -Target $debugExe
New-Link -Name 'WARPSCOUT' -Target $warpscoutExe

if (Test-Path $etwScript) {
    New-Link -Name 'ETW Probe (admin)' -Target "$env:WINDIR\System32\WindowsPowerShell\v1.0\powershell.exe" `
        -Arguments "-NoProfile -ExecutionPolicy Bypass -File `"$etwScript`" -Duration 300" `
        -WorkingDirectory $root
}

Write-Host 'Done.'
