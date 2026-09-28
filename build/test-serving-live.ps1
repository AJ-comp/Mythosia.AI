[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('ollama', 'llamacpp', 'vllm')][string]$Runtime,
    [Parameter(Mandatory)][string]$Endpoint,
    [string]$Model,
    [string]$InventoryModel,
    [ValidateRange(1, 86400)][int]$TimeoutSeconds = 120,
    [switch]$Lifecycle,
    [switch]$Download,
    [switch]$ModelMetrics,
    [switch]$NoBuild
)

$ErrorActionPreference = 'Stop'
if (($Lifecycle -or $Download -or $ModelMetrics) -and [string]::IsNullOrWhiteSpace($Model)) {
    throw 'Lifecycle, download and model-metrics checks require an explicit test model.'
}
if (-not [string]::IsNullOrWhiteSpace($InventoryModel) -and -not $Download) {
    throw 'InventoryModel is only used with Download to identify the resulting canonical inventory entry.'
}
if ($ModelMetrics -and ($Runtime -ne 'llamacpp' -or $Lifecycle -or $Download)) {
    throw 'ModelMetrics is a separate llama.cpp check and cannot be combined with Lifecycle or Download.'
}
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$values = @{
    MYTHOSIA_SERVING_RUNTIME = $Runtime
    MYTHOSIA_SERVING_ENDPOINT = $Endpoint
    MYTHOSIA_SERVING_MODEL = $Model
    MYTHOSIA_SERVING_INVENTORY_MODEL = $InventoryModel
    MYTHOSIA_SERVING_TIMEOUT_SECONDS = [string]$TimeoutSeconds
}
$original = @{}
try {
    foreach ($entry in $values.GetEnumerator()) {
        $original[$entry.Key] = [Environment]::GetEnvironmentVariable($entry.Key, 'Process')
        [Environment]::SetEnvironmentVariable($entry.Key, $entry.Value, 'Process')
    }
    $arguments = @('run', '--project', (Join-Path $repoRoot 'tests/Mythosia.AI.Serving.Live/Mythosia.AI.Serving.Live.csproj'), '--configuration', 'Release')
    if ($NoBuild) { $arguments += '--no-build' }
    $arguments += '--'
    if ($Lifecycle) { $arguments += '--lifecycle' }
    if ($Download) { $arguments += '--download' }
    if ($ModelMetrics) { $arguments += '--model-metrics' }
    & dotnet @arguments
    if ($LASTEXITCODE -ne 0) { throw 'Live serving verification failed. Missing servers are not reported as passed or skipped.' }
}
finally {
    foreach ($entry in $original.GetEnumerator()) {
        [Environment]::SetEnvironmentVariable($entry.Key, $entry.Value, 'Process')
    }
}
