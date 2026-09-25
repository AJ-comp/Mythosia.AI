[CmdletBinding()]
param(
    # Optional Python 3.12 environment with pixie-model-requirements.txt installed.
    [string]$PixiePython
)

$ErrorActionPreference = 'Stop'
$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$runName = 'release-check-' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8)
$runDirectory = Join-Path $repoRoot "artifacts/$runName"
$packageDirectory = Join-Path $runDirectory 'packages'
New-Item -ItemType Directory -Path $runDirectory | Out-Null
$pwsh = (Get-Command pwsh -ErrorAction Stop).Source
$completed = [System.Collections.Generic.List[string]]::new()

function Invoke-ReleaseCheck {
    param([string]$Name, [string]$Command, [string[]]$Arguments)

    Write-Host "`n[$Name]"
    $logPath = Join-Path $runDirectory ($Name + '.log')
    & $Command @Arguments *> $logPath
    $commandExit = $LASTEXITCODE
    Get-Content -LiteralPath $logPath -Tail 15 | Write-Host
    if ($commandExit -ne 0) {
        throw "$Name failed (exit $commandExit). See $logPath. No commit, push or publication was performed."
    }
    $completed.Add($Name)
}

function Invoke-ReleaseScript {
    param([string]$Name, [string]$Script, [string[]]$Arguments = @())
    Invoke-ReleaseCheck $Name $pwsh (@('-NoProfile', '-File', (Join-Path $PSScriptRoot $Script)) + $Arguments)
}

Push-Location -LiteralPath $repoRoot
try {
    # Unlike ordinary offline CI, release readiness must check NuGet availability.
    Invoke-ReleaseScript 'readiness-before' 'test-release-readiness.ps1'
    Invoke-ReleaseCheck 'restore' 'dotnet' @('restore', 'Mythosia.AI.slnx')
    Invoke-ReleaseScript 'vulnerabilities' 'test-package-vulnerabilities.ps1'
    Invoke-ReleaseScript 'publication-safety' 'test-publish-nuget-safety.ps1'
    Invoke-ReleaseScript 'readiness-safety' 'test-release-readiness-safety.ps1'
    Invoke-ReleaseScript 'test-categories' 'test-test-categories.ps1'
    Invoke-ReleaseScript 'evaluation-categories' 'test-test-categories.ps1' @('-TestRoot', 'tests/Mythosia.AI.Rag.Evaluation.Tests')
    Invoke-ReleaseCheck 'rewriter-ui' 'node' @('--experimental-vm-modules', 'build/test-deepseek-rewriter-ui.mjs')
    Invoke-ReleaseCheck 'ui-test-dependencies' 'npm' @('ci', '--prefix', 'build/ui-tests', '--ignore-scripts', '--registry=https://registry.npmjs.org/', '--no-fund')
    Invoke-ReleaseCheck 'model-capabilities-ui' 'node' @('--experimental-vm-modules', 'build/test-model-capabilities-ui.mjs')
    Invoke-ReleaseCheck 'perplexity-agent-ui' 'node' @('--experimental-vm-modules', 'build/test-perplexity-agent-ui.mjs')
    Invoke-ReleaseCheck 'perplexity-embedding-ui' 'node' @('--experimental-vm-modules', 'build/test-perplexity-embedding-ui.mjs')
    Invoke-ReleaseCheck 'chat-cancellation-ui' 'node' @('--experimental-vm-modules', 'build/test-chat-cancellation-ui.mjs')
    Invoke-ReleaseCheck 'chat-markdown-ui' 'node' @('--experimental-vm-modules', 'build/test-chat-markdown-ui.mjs')
    Invoke-ReleaseCheck 'console-ui' 'node' @('--experimental-vm-modules', 'build/test-console-ui.mjs')
    Invoke-ReleaseCheck 'i18n-ui' 'node' @('--experimental-vm-modules', 'build/test-i18n-ui.mjs')
    Invoke-ReleaseCheck 'build' 'dotnet' @('build', 'Mythosia.AI.slnx', '--configuration', 'Release', '--no-restore', '-p:TreatWarningsAsErrors=true')
    Invoke-ReleaseScript 'docfx-references' 'prepare-docfx-references.ps1' @('-NoRestore')
    Invoke-ReleaseCheck 'docfx' 'docfx' @('docfx.json', '--warningsAsErrors')
    Invoke-ReleaseScript 'release-documentation' 'test-release-documentation.ps1'
    Invoke-ReleaseCheck 'api-manifest' 'git' @('diff', '--exit-code', '--', 'api/.manifest')

    $suites = @(
        @{ Name = 'core'; Project = 'Mythosia.AI.Test'; Args = @('--settings', 'tests/Mythosia.AI.Test/serial.runsettings', '--filter', 'TestCategory=Unit') },
        @{ Name = 'rag'; Project = 'Mythosia.AI.Rag.Tests'; Args = @() },
        @{ Name = 'pixie-unit'; Project = 'Mythosia.AI.Rag.Search.Pixie.Tests'; Args = @('--filter', 'TestCategory=Unit') },
        @{ Name = 'evaluation'; Project = 'Mythosia.AI.Rag.Evaluation.Tests'; Args = @('--filter', 'TestCategory=Unit') },
        @{ Name = 'documents'; Project = 'Mythosia.Documents.Tests'; Args = @() },
        @{ Name = 'mcp'; Project = 'Mythosia.AI.Mcp.Tests'; Args = @() },
        @{ Name = 'vllm'; Project = 'Mythosia.AI.Serving.Vllm.Tests'; Args = @() },
        @{ Name = 'vectordb'; Project = 'Mythosia.VectorDb.Tests'; Args = @() }
    )
    foreach ($suite in $suites) {
        $testArguments = @('test', '--project', "tests/$($suite.Project)/$($suite.Project).csproj",
            '--configuration', 'Release', '--no-build', '--minimum-expected-tests', '1',
            '--report-trx', '--report-trx-filename', ($suite.Name + '.trx'),
            '--results-directory', (Join-Path $runDirectory 'tests')) + $suite.Args
        Invoke-ReleaseCheck $suite.Name 'dotnet' $testArguments
    }

    if ($PixiePython) {
        Invoke-ReleaseCheck 'pixie-preparation' $PixiePython @('build/prepare-pixie-model.py')
    }
    # Missing models and skipped inference tests are failures, never a successful check.
    Invoke-ReleaseScript 'pixie-model' 'test-pixie-model.ps1' @('-NoBuild')
    Invoke-ReleaseScript 'retrieval-smoke' 'test-retrieval-evaluation.ps1' @(
        '-Dataset', 'tests/Mythosia.AI.Rag.Evaluation/Datasets/smoke.json',
        '-Methods', 'bm25,dense,hybrid-bm25', '-Dense', 'local-hash',
        '-OutputDirectory', (Join-Path $runDirectory 'retrieval-smoke'),
        '-Baseline', 'tests/Mythosia.AI.Rag.Evaluation/Datasets/Baselines/smoke-local-hash.json',
        '-MaxRegression', '0.02', '-NoBuild')
    Invoke-ReleaseScript 'pack' 'publish-nuget.ps1' @('-Mode', 'Pack', '-ArtifactsDirectory', $packageDirectory, '-AllowDirtyValidationPack')
    Invoke-ReleaseScript 'consumers' 'test-nuget-packages.ps1' @('-ArtifactsDirectory', $packageDirectory)
    Invoke-ReleaseScript 'readiness-after' 'test-release-readiness.ps1'
    Invoke-ReleaseCheck 'whitespace' 'git' @('-c', 'core.safecrlf=false', 'diff', '--check', 'HEAD')

    [ordered]@{
        status = 'passed'
        completedUtc = [DateTime]::UtcNow.ToString('O')
        checks = $completed.ToArray()
        packageArtifacts = $packageDirectory
        packageProvenance = 'development-validation'
        publicationPerformed = $false
    } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $runDirectory 'result.json') -Encoding utf8
    Write-Host "`nAll local release checks passed. Reports: $runDirectory"
    Write-Host 'No commit, push or publication was performed. These development-validation packages cannot be published; the release workflow must rebuild the committed source.'
}
finally {
    Pop-Location
}
