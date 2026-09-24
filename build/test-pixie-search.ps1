[CmdletBinding()]
param(
    [ValidateSet('none', 'local-hash', 'openai')]
    [string]$Dense = 'none',
    [string]$ModelDirectory,
    [string]$Cases,
    [string]$OutputDirectory,
    [ValidateRange(0, 128)][int]$Threads = 4,
    [switch]$NoBuild
)

$ErrorActionPreference = 'Stop'
# Keep existing callers working while all evaluation logic lives in the shared runner.
$parameters = @{ Dense = $Dense; Threads = $Threads; NoBuild = $NoBuild; Methods = @('bm25', 'pixie') }
if ($Dense -ne 'none') { $parameters.Methods += @('dense', 'hybrid-bm25', 'hybrid-pixie') }
if ($ModelDirectory) { $parameters.ModelDirectory = $ModelDirectory }
if ($Cases) { $parameters.Dataset = $Cases }
if ($OutputDirectory) { $parameters.OutputDirectory = $OutputDirectory }
& (Join-Path $PSScriptRoot 'test-retrieval-evaluation.ps1') @parameters
