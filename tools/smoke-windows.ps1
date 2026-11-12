param([Parameter(Mandatory=$true)][string]$Executable)
$ErrorActionPreference = 'Stop'
if (-not $IsWindows) { throw 'Run this check on Windows.' }
$path = (Resolve-Path $Executable).Path
if ((Split-Path $path -Leaf) -ne 'Byd.Tray.exe') { throw 'Use the published Byd.Tray.exe.' }
# Only our synthetic demo process is started/stopped. It cannot read the real wallet,
# load startup preferences, authenticate, fetch tiles or operate a vehicle.
$process = Start-Process -FilePath $path -ArgumentList '--demo' -PassThru
try {
    if (-not $process.WaitForInputIdle(30000)) { throw 'Demo did not reach its Windows message loop.' }
    $deadline = [DateTime]::UtcNow.AddSeconds(20)
    do {
        $process.Refresh()
        if ($process.HasExited) { throw "Demo exited before showing its window (code $($process.ExitCode))." }
        if ($process.MainWindowHandle -ne 0 -and $process.MainWindowTitle -match 'demo' -and $process.Responding) {
            Write-Output 'Native Windows demo window is present and responding; no BYD requests.'
            return
        }
        Start-Sleep -Milliseconds 250
    } while ([DateTime]::UtcNow -lt $deadline)
    throw 'A responding, labelled demo window was not observed.'
} finally {
    if (-not $process.HasExited) { Stop-Process -Id $process.Id -Force }
    $process.Dispose()
}
