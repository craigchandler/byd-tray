param([ValidateSet('win-x64','win-arm64')][string]$Runtime='win-x64')
$ErrorActionPreference = 'Stop'
$repoDir = Split-Path $PSScriptRoot -Parent
python (Join-Path $repoDir 'tools/build-release.py') $Runtime
exit $LASTEXITCODE
