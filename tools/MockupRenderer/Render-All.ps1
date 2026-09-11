param([string]$Renderer = '.agent_build/MockupRenderer.exe')
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
Push-Location $repoRoot
try {
    $captures = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'captures.json') -Encoding UTF8 -Raw | ConvertFrom-Json
    foreach ($capture in $captures) {
        & $Renderer --view $capture.view --state $capture.state --output $capture.output --width $capture.width --height $capture.height
        if ($LASTEXITCODE -ne 0) { throw "Capture failed: $($capture.output)" }
    }
    Write-Output "Rendered $($captures.Count) captures."
} finally { Pop-Location }
