param([string]$ResultsDirectory)
. (Join-Path $PSScriptRoot 'Enter-BuildEnvironment.ps1')
if (-not $ResultsDirectory) { $ResultsDirectory = Join-Path $repoRoot ('artifacts\tests\' + [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffZ')) }
Push-Location -LiteralPath $repoRoot
try {
    & dotnet test 'PawnAppraiser.slnx' -c Release --logger trx --results-directory $ResultsDirectory -p:SelfContained=false -p:PublishSingleFile=false --collect 'XPlat Code Coverage'
    if ($LASTEXITCODE -ne 0) { throw '自动化测试失败' }
} finally { Pop-Location }
