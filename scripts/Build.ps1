param([string]$OutputDirectory, [switch]$SkipTests)
. (Join-Path $PSScriptRoot 'Enter-BuildEnvironment.ps1')
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $repoRoot ('artifacts\portable\' + [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffZ')) }
$OutputDirectory = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputDirectory)
if (Test-Path -LiteralPath $OutputDirectory) { throw "拒绝覆盖已有输出：$OutputDirectory" }
if (-not $SkipTests) { & (Join-Path $PSScriptRoot 'Test.ps1') }
New-Item -ItemType Directory -Path $OutputDirectory -ErrorAction Stop | Out-Null
Push-Location -LiteralPath $repoRoot
try {
    & dotnet publish 'src\PawnAppraiser.Desktop\PawnAppraiser.Desktop.csproj' -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishTrimmed=false -o $OutputDirectory
    if ($LASTEXITCODE -ne 0) { throw '发布构建失败' }
    Write-Output "PORTABLE_OUTPUT=$OutputDirectory"
} finally { Pop-Location }
