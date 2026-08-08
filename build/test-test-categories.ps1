[CmdletBinding()]
param(
    [string]$TestRoot = (Join-Path $PSScriptRoot "../tests/Mythosia.AI.Test")
)

$ErrorActionPreference = "Stop"

function Get-TestCategories {
    param([string]$AttributeText)

    $categories = @()
    $matches = [regex]::Matches(
        $AttributeText,
        'TestCategory(?:Attribute)?\s*\(\s*"(?<category>[^"]+)"\s*\)',
        [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)

    foreach ($match in $matches) {
        $categories += $match.Groups["category"].Value
    }

    return $categories
}

$resolvedTestRoot = (Resolve-Path -LiteralPath $TestRoot).Path.TrimEnd(
    [System.IO.Path]::DirectorySeparatorChar,
    [System.IO.Path]::AltDirectorySeparatorChar)

$classPattern = [regex]::new(
    '(?ms)(?<attributes>(?:^[ \t]*\[[^\r\n]*\][ \t]*\r?\n)+)' +
    '[ \t]*(?<modifiers>(?:(?:public|internal|protected|private|abstract|sealed|static|partial)\s+)*)' +
    'class\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)')

$methodPattern = [regex]::new(
    '(?ms)(?<attributes>(?:^[ \t]*\[[^\r\n]*\][ \t]*\r?\n)+)' +
    '[ \t]*(?:(?:public|internal|protected|private|static|virtual|override|abstract|sealed|async|new)\s+)+' +
    '(?:[A-Za-z_][A-Za-z0-9_\.<>?,\[\]]*\s+)+' +
    '(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*(?:<[^\r\n{]+>)?\s*\(')

$failures = New-Object System.Collections.Generic.List[string]
$scopedFileCount = 0
$scopedTestCount = 0
$unitTestCount = 0
$liveTestCount = 0

$sourceFiles = Get-ChildItem -LiteralPath $resolvedTestRoot -Recurse -File -Filter "*.cs" |
    Where-Object {
        $_.FullName -notmatch '[\\/](?:bin|obj)[\\/]' -and
        $_.FullName -notmatch '[\\/]TestCases[\\/]' -and
        $_.FullName -notmatch '[\\/]Providers[\\/]OpenAI[\\/]Modular[\\/]'
    }

foreach ($sourceFile in $sourceFiles) {
    $content = [System.IO.File]::ReadAllText($sourceFile.FullName)
    $relativePath = $sourceFile.FullName.Substring($resolvedTestRoot.Length).TrimStart('\', '/')
    $normalizedPath = $relativePath.Replace('\', '/')

    # Common and Streaming contain the deterministic CI contracts. Provider-side
    # parsers/contracts opt in by already carrying a Unit category.
    $isCiScope =
        $normalizedPath.StartsWith('Common/', [System.StringComparison]::OrdinalIgnoreCase) -or
        $normalizedPath.StartsWith('Streaming/', [System.StringComparison]::OrdinalIgnoreCase) -or
        $content -match 'TestCategory(?:Attribute)?\s*\(\s*"Unit"\s*\)'

    if (-not $isCiScope) {
        continue
    }

    $scopedFileCount++
    $sourceTestAttributeCount = [regex]::Matches(
        $content,
        '(?<![A-Za-z0-9_])(?:Data)?TestMethod(?:Attribute)?(?=\s*(?:\(|[,\]]))').Count
    $parsedTestAttributeCount = 0
    $classMatches = $classPattern.Matches($content) |
        Where-Object { $_.Groups["attributes"].Value -match 'TestClass(?:Attribute)?(?:\s*\(|\s*[,\]])' }

    for ($classIndex = 0; $classIndex -lt $classMatches.Count; $classIndex++) {
        $classMatch = $classMatches[$classIndex]
        $className = $classMatch.Groups["name"].Value
        $classModifiers = $classMatch.Groups["modifiers"].Value

        if ($classModifiers -match '(?:^|\s)abstract(?:\s|$)') {
            continue
        }

        $regionStart = $classMatch.Index
        $regionEnd = if ($classIndex + 1 -lt $classMatches.Count) {
            $classMatches[$classIndex + 1].Index
        }
        else {
            $content.Length
        }

        $classRegion = $content.Substring($regionStart, $regionEnd - $regionStart)
        $classCategories = @(Get-TestCategories $classMatch.Groups["attributes"].Value)
        $methodMatches = $methodPattern.Matches($classRegion) |
            Where-Object { $_.Groups["attributes"].Value -match '(?:Data)?TestMethod(?:Attribute)?(?:\s*\(|\s*[,\]])' }
        $parsedTestAttributeCount += $methodMatches.Count

        foreach ($methodMatch in $methodMatches) {
            $methodName = $methodMatch.Groups["name"].Value
            $methodCategories = @(Get-TestCategories $methodMatch.Groups["attributes"].Value)
            $effectiveCategories = @($classCategories + $methodCategories)
            $hasUnit = $effectiveCategories -contains "Unit"
            $hasLive = $effectiveCategories -contains "Live"

            $scopedTestCount++
            if ($hasUnit) {
                $unitTestCount++
            }
            if ($hasLive) {
                $liveTestCount++
            }

            if (-not $hasUnit -and -not $hasLive) {
                $lineNumber = 1 + ([regex]::Matches(
                    $content.Substring(0, $regionStart + $methodMatch.Index),
                    "`n").Count)
                $failures.Add(
                    "$normalizedPath`:$lineNumber $className.$methodName has neither an effective Unit nor Live category.")
            }
        }
    }

    if ($parsedTestAttributeCount -ne $sourceTestAttributeCount) {
        $failures.Add(
            "$normalizedPath contains $sourceTestAttributeCount test attribute(s), but the validator parsed $parsedTestAttributeCount. Update the validator for this source shape.")
    }
}

if ($scopedTestCount -eq 0) {
    throw "No CI-scoped MSTest methods were found under '$resolvedTestRoot'."
}

if ($failures.Count -gt 0) {
    Write-Host "Test category validation failed:" -ForegroundColor Red
    foreach ($failure in $failures) {
        Write-Host "  - $failure" -ForegroundColor Red
    }
    throw "$($failures.Count) CI-scoped test method(s) are unclassified."
}

Write-Host (
    "Test category validation passed: {0} files, {1} tests ({2} Unit, {3} Live)." -f
    $scopedFileCount,
    $scopedTestCount,
    $unitTestCount,
    $liveTestCount)
