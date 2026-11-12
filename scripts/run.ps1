$ErrorActionPreference = 'Stop'
$repoDir = Split-Path $PSScriptRoot -Parent
dotnet run --project (Join-Path $repoDir 'src/Byd.Tray/Byd.Tray.csproj') -- @args
exit $LASTEXITCODE
