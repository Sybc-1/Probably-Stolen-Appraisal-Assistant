$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$localRoot = Join-Path $repoRoot '.local'
foreach ($name in @('cli','packages','http','temp')) { New-Item -ItemType Directory -Force -Path (Join-Path $localRoot $name) | Out-Null }
$env:DOTNET_CLI_HOME = Join-Path $localRoot 'cli'
$env:NUGET_PACKAGES = Join-Path $localRoot 'packages'
$env:NUGET_HTTP_CACHE_PATH = Join-Path $localRoot 'http'
$env:TEMP = Join-Path $localRoot 'temp'
$env:TMP = $env:TEMP
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
