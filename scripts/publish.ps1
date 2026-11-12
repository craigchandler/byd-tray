param([ValidateSet('win-x64','win-arm64')][string]$Runtime='win-x64')
$ErrorActionPreference = 'Stop'
$repoDir = Split-Path $PSScriptRoot -Parent
dotnet publish (Join-Path $repoDir 'src/Byd.Tray/Byd.Tray.csproj') -c Release -r $Runtime --self-contained true -o (Join-Path $repoDir "artifacts/$Runtime")
exit $LASTEXITCODE
