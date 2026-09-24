[CmdletBinding()]
param(
    [string]$Dataset,
    [string]$DataRoot,
    [string[]]$Methods = @('bm25', 'pixie'),
    [ValidateSet('none', 'local-hash', 'openai')]
    [string]$Dense = 'none',
    [string]$EmbeddingCache,
    [string]$EmbeddingModel = 'text-embedding-3-small',
    [ValidateRange(1, 65536)][int]$Dimensions = 1536,
    [string]$ModelDirectory,
    [string]$OutputDirectory,
    [ValidateRange(0, 128)][int]$Threads = 4,
    [ValidateRange(1, 1000000)][int]$ChunkSize = 450,
    [ValidateRange(0, 999999)][int]$ChunkOverlap = 50,
    [ValidateRange(1, 100000)][int]$CandidateLimit = 50,
    [ValidateRange(1, 1000000)][int]$RecallK = 5,
    [ValidateRange(1, 1000000)][int]$RankK = 10,
    [ValidateScript({ [double]::IsFinite($_) -and $_ -ge 0 -and $_ -le 1 })]
    [double]$VectorWeight = 0.5,
    [ValidateRange(1, 100)][int]$CandidateMultiplier = 2,
    [ValidateRange(1, 1000000)][int]$RrfK = 60,
    [ValidateRange(0, 100)][int]$Warmup = 1,
    [ValidateRange(1, 1000)][int]$Repeat = 1,
    [string]$Baseline,
    [ValidateScript({ [double]::IsFinite($_) -and $_ -ge 0 -and $_ -le 1 })]
    [double]$MaxRegression = 0.02,
    [switch]$NoBuild
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
function Resolve-RepositoryPath([string]$Path) {
    if ([System.IO.Path]::IsPathRooted($Path)) { return [System.IO.Path]::GetFullPath($Path) }
    return [System.IO.Path]::GetFullPath((Join-Path $repoRoot $Path))
}

if (-not $Dataset) { $Dataset = 'tests/Mythosia.AI.Rag.Evaluation/Datasets/repository-docs.json' }
$Dataset = Resolve-RepositoryPath $Dataset
if (-not (Test-Path -LiteralPath $Dataset -PathType Leaf)) { throw "Dataset does not exist: $Dataset" }
if (-not $DataRoot) { $DataRoot = $repoRoot }
$DataRoot = Resolve-RepositoryPath $DataRoot
if (-not (Test-Path -LiteralPath $DataRoot -PathType Container)) { throw "DataRoot directory does not exist: $DataRoot" }
if (-not $ModelDirectory) { $ModelDirectory = 'src/rag/Mythosia.AI.Rag.Search.Pixie/models/pixie' }
$ModelDirectory = Resolve-RepositoryPath $ModelDirectory
if (-not $EmbeddingCache) { $EmbeddingCache = 'artifacts/retrieval-evaluation/cache' }
$EmbeddingCache = Resolve-RepositoryPath $EmbeddingCache

$selectedMethods = @($Methods | ForEach-Object { $_.Split(',') } | ForEach-Object { $_.Trim() })
if ($selectedMethods.Count -eq 0 -or @($selectedMethods | Where-Object { [string]::IsNullOrWhiteSpace($_) }).Count -gt 0) {
    throw 'Methods must contain one or more nonempty method IDs.'
}
if (@($selectedMethods | Select-Object -Unique).Count -ne $selectedMethods.Count) { throw 'Methods must not contain duplicates.' }
# The runner owns the method registry and its dense/model requirements so new
# retrieval methods do not require a second registration in this wrapper.
if ($ChunkOverlap -ge $ChunkSize) { throw 'ChunkOverlap must be smaller than ChunkSize.' }
if ($CandidateLimit -lt [Math]::Max($RecallK, $RankK)) { throw 'CandidateLimit must cover both RecallK and RankK.' }
if ($Dense -eq 'openai') {
    if ([string]::IsNullOrWhiteSpace($env:OPENAI_API_KEY)) {
        throw 'Set OPENAI_API_KEY before selecting OpenAI embeddings. The key is never written to reports.'
    }
    Write-Host 'OpenAI embeddings send dataset text to the API and incur API charges.'
}
if ($Baseline) {
    $Baseline = Resolve-RepositoryPath $Baseline
    if (-not (Test-Path -LiteralPath $Baseline -PathType Leaf)) { throw "Baseline results do not exist: $Baseline" }
}
elseif ($PSBoundParameters.ContainsKey('MaxRegression')) {
    throw 'MaxRegression requires a Baseline results.json file.'
}

if (-not $OutputDirectory) {
    $runId = [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffZ', [Globalization.CultureInfo]::InvariantCulture) + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8)
    $OutputDirectory = Join-Path $repoRoot "artifacts/retrieval-evaluation/$runId"
}
$OutputDirectory = Resolve-RepositoryPath $OutputDirectory
if (Test-Path -LiteralPath $OutputDirectory) {
    throw "OutputDirectory already exists; select a new directory to preserve earlier evaluation results: $OutputDirectory"
}

$invariant = [Globalization.CultureInfo]::InvariantCulture
$runArgs = @('run', '--project', (Join-Path $repoRoot 'tests/Mythosia.AI.Rag.Evaluation/Mythosia.AI.Rag.Evaluation.csproj'), '--configuration', 'Release')
if ($NoBuild) { $runArgs += '--no-build' }
$runArgs += @('--', '--root', $repoRoot, '--data-root', $DataRoot, '--dataset', $Dataset, '--output', $OutputDirectory,
    '--methods', ($selectedMethods -join ','), '--dense', $Dense, '--model-directory', $ModelDirectory,
    '--embedding-cache', $EmbeddingCache, '--embedding-model', $EmbeddingModel, '--dimensions', [string]$Dimensions,
    '--threads', [string]$Threads, '--chunk-size', [string]$ChunkSize, '--chunk-overlap', [string]$ChunkOverlap,
    '--candidate-limit', [string]$CandidateLimit, '--recall-k', [string]$RecallK, '--rank-k', [string]$RankK,
    '--vector-weight', $VectorWeight.ToString($invariant), '--candidate-multiplier', [string]$CandidateMultiplier,
    '--rrf-k', [string]$RrfK, '--warmup', [string]$Warmup, '--repeat', [string]$Repeat)
if ($Baseline) { $runArgs += @('--baseline', $Baseline, '--max-regression', $MaxRegression.ToString($invariant)) }
& dotnet @runArgs
if ($LASTEXITCODE -ne 0) { throw "Retrieval evaluation failed (exit code $LASTEXITCODE). Inspect any reports in $OutputDirectory." }
Write-Host "Evaluation report: $(Join-Path $OutputDirectory 'summary.md')"
