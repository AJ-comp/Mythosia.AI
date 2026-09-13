[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
$repoRoot = [System.IO.Path]::GetFullPath((Resolve-Path (Join-Path $PSScriptRoot "..")).Path)
$issues = [System.Collections.Generic.List[string]]::new()

function Add-Issue {
    param([string]$Message)

    $issues.Add($Message)
}

function Get-RepositoryRelativePath {
    param([string]$Path)

    $fullPath = [System.IO.Path]::GetFullPath($Path)
    $rootPrefix = $repoRoot.TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
    if ($fullPath.StartsWith($rootPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        return $fullPath.Substring($rootPrefix.Length).Replace("\", "/")
    }

    return $fullPath
}

function Get-ProjectPropertyValue {
    param(
        [xml]$ProjectXml,
        [string]$Name
    )

    foreach ($propertyGroup in $ProjectXml.Project.PropertyGroup) {
        $node = $propertyGroup.$Name
        if ($null -eq $node) {
            continue
        }

        $value = [string]$node
        if (-not [string]::IsNullOrWhiteSpace($value)) {
            return $value.Trim()
        }
    }

    return $null
}

$releasePackages = @(
    [pscustomobject]@{
        Id = "Mythosia.AI.Abstractions"
        Version = "4.0.0"
        Project = "src/core/Mythosia.AI.Abstractions/Mythosia.AI.Abstractions.csproj"
        Readme = "src/core/Mythosia.AI.Abstractions/README.md"
        ReleaseNotes = "src/core/Mythosia.AI.Abstractions/RELEASE_NOTES.md"
        ReleaseNotesUrl = "https://github.com/AJ-comp/Mythosia.AI/blob/main/src/core/Mythosia.AI.Abstractions/RELEASE_NOTES.md#v400"
    },
    [pscustomobject]@{
        Id = "Mythosia.AI"
        Version = "8.0.0"
        Project = "src/core/Mythosia.AI/Mythosia.AI.csproj"
        Readme = "src/core/Mythosia.AI/README.md"
        ReleaseNotes = "src/core/Mythosia.AI/RELEASE_NOTES.md"
        ReleaseNotesUrl = "https://github.com/AJ-comp/Mythosia.AI/blob/main/src/core/Mythosia.AI/RELEASE_NOTES.md#v800"
    },
    [pscustomobject]@{
        Id = "Mythosia.AI.Providers.Alibaba"
        Version = "3.0.0"
        Project = "src/core/Mythosia.AI.Providers.Alibaba/Mythosia.AI.Providers.Alibaba.csproj"
        Readme = "src/core/Mythosia.AI.Providers.Alibaba/README.md"
        ReleaseNotes = "src/core/Mythosia.AI.Providers.Alibaba/RELEASE_NOTES.md"
        ReleaseNotesUrl = "https://github.com/AJ-comp/Mythosia.AI/blob/main/src/core/Mythosia.AI.Providers.Alibaba/RELEASE_NOTES.md#v300"
    },
    [pscustomobject]@{
        Id = "Mythosia.AI.Rag"
        Version = "8.0.0"
        Project = "src/rag/Mythosia.AI.Rag/Mythosia.AI.Rag.csproj"
        Readme = "src/rag/Mythosia.AI.Rag/README.md"
        ReleaseNotes = "src/rag/Mythosia.AI.Rag/RELEASE_NOTES.md"
        ReleaseNotesUrl = "https://github.com/AJ-comp/Mythosia.AI/blob/main/src/rag/Mythosia.AI.Rag/RELEASE_NOTES.md#v800"
    },
    [pscustomobject]@{
        Id = "Mythosia.AI.Mcp"
        Version = "0.1.0-preview"
        Project = "src/integrations/Mythosia.AI.Mcp/Mythosia.AI.Mcp.csproj"
        Readme = "src/integrations/Mythosia.AI.Mcp/README.md"
        ReleaseNotes = "src/integrations/Mythosia.AI.Mcp/RELEASE_NOTES.md"
        ReleaseNotesUrl = "https://github.com/AJ-comp/Mythosia.AI/blob/main/src/integrations/Mythosia.AI.Mcp/RELEASE_NOTES.md#v010-preview"
    },
    [pscustomobject]@{
        Id = "Mythosia.AI.Serving.Vllm"
        Version = "1.0.0"
        Project = "src/serving/Mythosia.AI.Serving.Vllm/Mythosia.AI.Serving.Vllm.csproj"
        Readme = "src/serving/Mythosia.AI.Serving.Vllm/README.md"
        ReleaseNotes = "src/serving/Mythosia.AI.Serving.Vllm/RELEASE_NOTES.md"
        ReleaseNotesUrl = "https://github.com/AJ-comp/Mythosia.AI/blob/main/src/serving/Mythosia.AI.Serving.Vllm/RELEASE_NOTES.md#v100"
    }
)

foreach ($package in $releasePackages) {
    $projectPath = Join-Path $repoRoot $package.Project
    $readmePath = Join-Path $repoRoot $package.Readme
    $releaseNotesPath = Join-Path $repoRoot $package.ReleaseNotes

    if (-not (Test-Path -LiteralPath $projectPath -PathType Leaf)) {
        Add-Issue "Missing release project: $($package.Project)"
        continue
    }
    if (-not (Test-Path -LiteralPath $readmePath -PathType Leaf)) {
        Add-Issue "Missing package README: $($package.Readme)"
        continue
    }
    if (-not (Test-Path -LiteralPath $releaseNotesPath -PathType Leaf)) {
        Add-Issue "Missing package release notes: $($package.ReleaseNotes)"
        continue
    }

    [xml]$projectXml = Get-Content -Raw -LiteralPath $projectPath
    $packageId = Get-ProjectPropertyValue -ProjectXml $projectXml -Name "PackageId"
    $version = Get-ProjectPropertyValue -ProjectXml $projectXml -Name "Version"
    $description = Get-ProjectPropertyValue -ProjectXml $projectXml -Name "Description"
    $tags = Get-ProjectPropertyValue -ProjectXml $projectXml -Name "PackageTags"
    $packageReadme = Get-ProjectPropertyValue -ProjectXml $projectXml -Name "PackageReadmeFile"
    $packageReleaseNotes = Get-ProjectPropertyValue -ProjectXml $projectXml -Name "PackageReleaseNotes"

    if ($packageId -ne $package.Id) {
        Add-Issue "$($package.Project) declares PackageId '$packageId', expected '$($package.Id)'."
    }
    if ($version -ne $package.Version) {
        Add-Issue "$($package.Id) declares version '$version', expected '$($package.Version)'."
    }
    if ([string]::IsNullOrWhiteSpace($description) -or $description.Length -gt 4000) {
        Add-Issue "$($package.Id) needs a non-empty Description of at most 4,000 characters."
    }
    if ([string]::IsNullOrWhiteSpace($tags) -or $tags.Length -gt 4000) {
        Add-Issue "$($package.Id) needs non-empty PackageTags of at most 4,000 characters."
    }
    if ($packageReadme -ne "README.md") {
        Add-Issue "$($package.Id) must declare README.md as PackageReadmeFile."
    }
    if ([string]::IsNullOrWhiteSpace($packageReleaseNotes) -or
        $packageReleaseNotes.Length -gt 35000 -or
        -not $packageReleaseNotes.Contains("v$($package.Version)") -or
        -not $packageReleaseNotes.Contains($package.ReleaseNotesUrl)) {
        Add-Issue "$($package.Id) needs current PackageReleaseNotes metadata with an absolute full-notes URL."
    }

    $readmeText = Get-Content -Raw -LiteralPath $readmePath
    if (-not $readmeText.Contains($package.ReleaseNotesUrl)) {
        Add-Issue "$($package.Readme) must link to $($package.ReleaseNotesUrl)."
    }
    if ($readmeText -match '(?i)\]\(\s*RELEASE_NOTES\.md(?:[#?][^)]*)?\)') {
        Add-Issue "$($package.Readme) contains a relative release-notes link that breaks on NuGet.org."
    }

    $releaseNotesText = Get-Content -Raw -LiteralPath $releaseNotesPath
    $firstReleaseHeading = [regex]::Match($releaseNotesText, '(?m)^## v(?<version>[^\r\n]+)').Groups["version"].Value.Trim()
    if ($firstReleaseHeading -ne $package.Version) {
        Add-Issue "$($package.ReleaseNotes) must start with release v$($package.Version), found '$firstReleaseHeading'."
    }
    if ($package.Id -in @("Mythosia.AI.Abstractions", "Mythosia.AI", "Mythosia.AI.Providers.Alibaba") -and
        -not $releaseNotesText.Contains("https://github.com/AJ-comp/Mythosia.AI/blob/main/docs/v7-migration.md")) {
        Add-Issue "$($package.ReleaseNotes) does not link to the v7 migration guide."
    }
    # Check the current release independently: a historical migration link is not
    # sufficient guidance for the new image, completion, Run and tool contracts.
    $currentRelease = [regex]::Match($releaseNotesText, '(?ms)^## v[^\r\n]+\r?\n(?<body>.*?)(?=^## v|\z)').Groups['body'].Value
    # The independent vLLM control-plane client has no core v8 API migration.
    if ($package.Id -ne "Mythosia.AI.Serving.Vllm" -and
        -not $currentRelease.Contains("https://github.com/AJ-comp/Mythosia.AI/blob/main/docs/v8-migration.md")) {
        Add-Issue "$($package.ReleaseNotes) current release does not link to the v8 migration guide."
    }

    $packedReleaseNotes = @($projectXml.Project.ItemGroup.None | Where-Object {
        [string]$_.Update -eq "RELEASE_NOTES.md" -and
        [string]$_.Pack -ieq "true" -and
        @("/", "\") -contains [string]$_.PackagePath
    })
    if ($packedReleaseNotes.Count -ne 1) {
        Add-Issue "$($package.Project) must pack RELEASE_NOTES.md at the package root."
    }
}

$docfxPath = Join-Path $repoRoot "docfx.json"
$docfx = Get-Content -Raw -LiteralPath $docfxPath | ConvertFrom-Json
$metadata = @($docfx.metadata)[0]
$metadataSource = @($metadata.src)[0]
$configuredInputs = @($metadataSource.files | ForEach-Object {
    ([string]$_).Replace("\", "/")
} | Sort-Object)
$expectedInputs = @(Get-ChildItem -LiteralPath (Join-Path $repoRoot "src") -Filter "*.csproj" -File -Recurse |
ForEach-Object {
    [xml]$projectXml = Get-Content -Raw -LiteralPath $_.FullName
    $targetFramework = Get-ProjectPropertyValue -ProjectXml $projectXml -Name "TargetFramework"
    $targetFrameworks = Get-ProjectPropertyValue -ProjectXml $projectXml -Name "TargetFrameworks"
    $assemblyName = Get-ProjectPropertyValue -ProjectXml $projectXml -Name "AssemblyName"

    if ([string]::IsNullOrWhiteSpace($targetFramework) -or
        -not [string]::IsNullOrWhiteSpace($targetFrameworks)) {
        Add-Issue "$(Get-RepositoryRelativePath -Path $_.FullName) must declare exactly one TargetFramework for DocFX assembly metadata."
        return
    }
    if ([string]::IsNullOrWhiteSpace($assemblyName)) {
        $assemblyName = $_.BaseName
    }

    $projectDirectory = Get-RepositoryRelativePath -Path $_.DirectoryName
    "$projectDirectory/bin/Release/$targetFramework/$assemblyName.dll"
} | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Sort-Object)

if ([string]$metadataSource.src -ne ".") {
    Add-Issue "docfx.json metadata source root must be the repository root."
}
$pathComparer = if ([System.Environment]::OSVersion.Platform -eq [System.PlatformID]::Win32NT) {
    [System.StringComparer]::OrdinalIgnoreCase
}
else {
    [System.StringComparer]::Ordinal
}
$configuredInputSet = [System.Collections.Generic.HashSet[string]]::new($pathComparer)
$expectedInputSet = [System.Collections.Generic.HashSet[string]]::new($pathComparer)
foreach ($input in $configuredInputs) {
    [void]$configuredInputSet.Add($input)
    if (-not (Test-Path -LiteralPath (Join-Path $repoRoot $input) -PathType Leaf)) {
        Add-Issue "docfx.json references a missing compiled assembly: $input"
    }
}
foreach ($input in $expectedInputs) {
    [void]$expectedInputSet.Add($input)
}
foreach ($input in $expectedInputs) {
    if (-not $configuredInputSet.Contains($input)) {
        Add-Issue "docfx.json omits compiled source assembly: $input"
    }
}
foreach ($input in $configuredInputs) {
    if (-not $expectedInputSet.Contains($input)) {
        Add-Issue "docfx.json references an unexpected compiled assembly: $input"
    }
}

$configuredReferences = @($metadata.references | ForEach-Object {
    ([string]$_).Replace("\", "/")
})
if ($configuredReferences.Count -ne 1 -or
    $configuredReferences[0] -ne "artifacts/docfx-references/*.dll") {
    Add-Issue "docfx.json must resolve external API dependencies from the prepared DocFX reference directory."
}

$packageReadmes = @(Get-ChildItem -LiteralPath (Join-Path $repoRoot "src") -Filter "README.md" -File -Recurse |
    Where-Object { $_.FullName -notmatch '[/\\](bin|obj)[/\\]' })
$internalDocumentation = @(Get-ChildItem -LiteralPath (Join-Path $repoRoot "src") -Filter "*.md" -File -Recurse |
    Where-Object { $_.FullName -match '[/\\]docs[/\\]' -and $_.FullName -notmatch '[/\\](bin|obj)[/\\]' })

$activeDocumentation = @(
    Get-Item -LiteralPath (Join-Path $repoRoot "README.md")
    Get-ChildItem -LiteralPath (Join-Path $repoRoot "docs") -Filter "*.md" -File -Recurse
    $packageReadmes
    $internalDocumentation
) | Sort-Object -Property FullName -Unique

# A translated page pointing at the English guide is a valid link, but an incomplete translation.
# Check each published locale independently so missing guides cannot pass the link-only check.
$documentationRoot = Join-Path $repoRoot "docs"
$localizedDirectories = @(Get-ChildItem -LiteralPath $documentationRoot -Directory |
    Where-Object { Test-Path -LiteralPath (Join-Path $_.FullName "toc.yml") -PathType Leaf })
$guideDirectories = @(Get-Item -LiteralPath $documentationRoot) + $localizedDirectories
foreach ($directory in $guideDirectories) {
    $guidePath = Join-Path $directory.FullName "execution-api-transition.md"
    $tocPath = Join-Path $directory.FullName "toc.yml"
    if (-not (Test-Path -LiteralPath $guidePath -PathType Leaf)) {
        Add-Issue "Missing Run guide: $(Get-RepositoryRelativePath -Path $guidePath)"
    }
    $tocText = Get-Content -Raw -LiteralPath $tocPath
    $migrationGuidePath = Join-Path $directory.FullName 'v8-migration.md'
    if (-not (Test-Path -LiteralPath $migrationGuidePath -PathType Leaf)) {
        Add-Issue "Missing v8 migration guide: $(Get-RepositoryRelativePath -Path $migrationGuidePath)"
    }
    else {
        $migrationGuideText = Get-Content -Raw -LiteralPath $migrationGuidePath
        foreach ($contract in @('CreateRequest', 'AIRequestBuilder', 'AIRunResult',
                'ImageSize.Pixels', 'ImageSize.Preset', 'CancellationToken',
                'HandlerWithCancellation', 'GetCapabilities', 'PerplexityAgentOptions', 'McpException',
                '4.0.0', '8.0.0', '3.0.0', '0.1.0-preview')) {
            if (-not $migrationGuideText.Contains($contract)) {
                Add-Issue "$(Get-RepositoryRelativePath -Path $migrationGuidePath) omits the $contract migration contract."
            }
        }
        foreach ($relatedGuide in @('request-building.md', 'execution-api-transition.md',
                'providers.md#image-options-migration', 'completions.md#completion-cancellation',
                'function-calling.md#tool-execution-contract', 'model-capabilities.md', 'perplexity.md')) {
            if (-not $migrationGuideText.Contains($relatedGuide)) {
                Add-Issue "$(Get-RepositoryRelativePath -Path $migrationGuidePath) must link to its local $relatedGuide guide."
            }
        }
        if ([regex]::Matches($migrationGuideText, '(?m)^```csharp\s*$').Count -lt 3) {
            Add-Issue "$(Get-RepositoryRelativePath -Path $migrationGuidePath) must retain current image, request and Run migration examples."
        }
    }
    if ([regex]::Matches($tocText, '(?m)^\s*href:\s*v8-migration\.md\s*$').Count -ne 1) {
        Add-Issue "$(Get-RepositoryRelativePath -Path $tocPath) must link to its local v8 migration guide exactly once."
    }
    $capabilityGuidePath = Join-Path $directory.FullName 'model-capabilities.md'
    if (-not (Test-Path -LiteralPath $capabilityGuidePath -PathType Leaf)) {
        Add-Issue "Missing model capability guide: $(Get-RepositoryRelativePath -Path $capabilityGuidePath)"
    }
    else {
        $capabilityGuideText = Get-Content -Raw -LiteralPath $capabilityGuidePath
        foreach ($contract in @('GetCapabilities()', 'GetImageCapabilities', 'AIModelCapabilities',
                'ImageModelCapabilities', 'CapabilitySupport', 'Supported', 'Unsupported', 'Unknown',
                'GetReasoningSupport', 'NativeReasoning', 'ThinkingBudgetPresets', 'StructuredOutput',
                'ResolveRequestCapabilities', 'ApplyCapabilityRequestProfile', 'run.CanSteer')) {
            if (-not $capabilityGuideText.Contains($contract)) {
                Add-Issue "$(Get-RepositoryRelativePath -Path $capabilityGuidePath) omits the $contract capability contract."
            }
        }
        if ([regex]::Matches($capabilityGuideText, '(?m)^```csharp\s*$').Count -lt 3) {
            Add-Issue "$(Get-RepositoryRelativePath -Path $capabilityGuidePath) must retain Before/After and image capability examples."
        }
        if ($directory.FullName -ne $documentationRoot -and
            $capabilityGuideText.StartsWith('# Choose controls the selected model supports')) {
            Add-Issue "$(Get-RepositoryRelativePath -Path $capabilityGuidePath) still uses the English guide title."
        }
    }
    if ([regex]::Matches($tocText, '(?m)^\s*href:\s*model-capabilities\.md\s*$').Count -ne 1) {
        Add-Issue "$(Get-RepositoryRelativePath -Path $tocPath) must link to its local model capability guide exactly once."
    }
    $guideEntries = [regex]::Matches($tocText, '(?m)^\s*href:\s*execution-api-transition\.md\s*$')
    if ($guideEntries.Count -ne 1) {
        Add-Issue "$(Get-RepositoryRelativePath -Path $tocPath) must link to its local Run guide exactly once."
    }

    $requestBuilderGuidePath = Join-Path $directory.FullName "request-building.md"
    $completionGuidePath = Join-Path $directory.FullName 'completions.md'
    if (-not (Test-Path -LiteralPath $completionGuidePath -PathType Leaf)) {
        Add-Issue "Missing completion guide: $(Get-RepositoryRelativePath -Path $completionGuidePath)"
    }
    else {
        $completionGuideText = Get-Content -Raw -LiteralPath $completionGuidePath
        foreach ($contract in @('completion-cancellation', 'completion-cancellation-migration',
                'CancellationTokenSource(TimeSpan.FromSeconds(30))', 'cancellation.Cancel()',
                'OperationCanceledException', 'FunctionCallingPolicy.TimeoutSeconds',
                'GetCompletionAsync(cancellationToken: cancellation.Token)',
                'GetCompletionAsync<Dictionary<string, string>>', 'SendAsync(cancellationToken: token)',
                'SendOnceAsync(cancellationToken: token)', 'RequestCancellationToken',
                'CancellationToken cancellationToken = default', 'CancelAsync()',
                'WaitForCompletionAsync(cancellationToken:', 'GenerateContentConfig.abortSignal')) {
            if (-not $completionGuideText.Contains($contract)) {
                Add-Issue "$(Get-RepositoryRelativePath -Path $completionGuidePath) omits the $contract completion cancellation contract."
            }
        }
        if ([regex]::Matches($completionGuideText, '<a id="completion-cancellation">').Count -ne 1) {
            Add-Issue "$(Get-RepositoryRelativePath -Path $completionGuidePath) must define one completion-cancellation anchor."
        }
        foreach ($relatedGuide in @('request-building.md', 'execution-api-transition.md',
                'structured-output.md', 'request-contexts.md', 'function-calling.md', 'rag.md')) {
            $relatedPath = Join-Path $directory.FullName $relatedGuide
            if ((Test-Path -LiteralPath $relatedPath) -and
                -not (Get-Content -Raw -LiteralPath $relatedPath).Contains('completions.md#completion-cancellation')) {
                Add-Issue "$(Get-RepositoryRelativePath -Path $relatedPath) must link to its local completion cancellation guide."
            }
        }
        $contextPath = Join-Path $directory.FullName 'request-contexts.md'
        if ((Test-Path -LiteralPath $contextPath) -and
            (Get-Content -Raw -LiteralPath $contextPath) -match '(?m)^[^\r\n]*GetCompletionAsync[^\r\n]*RunAgentAsync[^\r\n]*(?:CancellationToken\.None|не принимают|не приймають|ไม่รับ)') {
            Add-Issue "$(Get-RepositoryRelativePath -Path $contextPath) still says ordinary completion cannot cancel context loading."
        }
    }
    $toolGuidePath = Join-Path $directory.FullName 'function-calling.md'
    if (-not (Test-Path -LiteralPath $toolGuidePath -PathType Leaf)) {
        Add-Issue "Missing tool guide: $(Get-RepositoryRelativePath -Path $toolGuidePath)"
    }
    else {
        $toolGuideText = Get-Content -Raw -LiteralPath $toolGuidePath
        foreach ($contract in @('tool-execution-contract', 'Task<T>', 'ValueTask<T>',
                'CancellationToken', 'HandlerWithCancellation', 'IsCancelled')) {
            if (-not $toolGuideText.Contains($contract)) {
                Add-Issue "$(Get-RepositoryRelativePath -Path $toolGuidePath) omits the $contract tool execution contract."
            }
        }
        foreach ($relatedGuide in @('agent.md', 'request-building.md', 'execution-api-transition.md')) {
            $relatedPath = Join-Path $directory.FullName $relatedGuide
            if ((Test-Path -LiteralPath $relatedPath) -and
                -not (Get-Content -Raw -LiteralPath $relatedPath).Contains('function-calling.md#tool-execution-contract')) {
                Add-Issue "$(Get-RepositoryRelativePath -Path $relatedPath) must link to its local tool execution contract."
            }
        }
    }
    if (-not (Test-Path -LiteralPath $requestBuilderGuidePath -PathType Leaf)) {
        Add-Issue "Missing request builder guide: $(Get-RepositoryRelativePath -Path $requestBuilderGuidePath)"
    }
    else {
        $requestBuilderGuideText = Get-Content -Raw -LiteralPath $requestBuilderGuidePath
        foreach ($contract in @('CreateRequest', 'AIRequestBuilder', 'AIRequest',
                'basis.WithTemperature(0.2f)', 'basis.WithTemperature(0.8f)',
                'ReferenceEquals(summary, creative); // false', 'GetCompletionAsync()',
                'StartRunAsync(', 'run.Result', 'WithProfile', 'WithContext',
                'WithStatelessMode()', 'WithFunctions', 'WithStaticFunctions<T>()',
                'ArgumentOutOfRangeException', 'MessageChain', 'IAIService')) {
            if (-not $requestBuilderGuideText.Contains($contract)) {
                Add-Issue "$(Get-RepositoryRelativePath -Path $requestBuilderGuidePath) omits the $contract request builder contract."
            }
        }
        if ([regex]::Matches($requestBuilderGuideText, '(?m)^## ').Count -lt 7 -or
            [regex]::Matches($requestBuilderGuideText, '(?m)^```csharp\s*$').Count -lt 4) {
            Add-Issue "$(Get-RepositoryRelativePath -Path $requestBuilderGuidePath) must explain independent branches, execution, copied settings, and shared conversation limits with Before/After examples."
        }
        if ($directory.FullName -ne $documentationRoot -and
            $requestBuilderGuideText.StartsWith('# Keep each request')) {
            Add-Issue "$(Get-RepositoryRelativePath -Path $requestBuilderGuidePath) still uses the English guide title."
        }
    }
    if ([regex]::Matches($tocText, '(?m)^\s*href:\s*request-building\.md\s*$').Count -ne 1) {
        Add-Issue "$(Get-RepositoryRelativePath -Path $tocPath) must link to its local request builder guide exactly once."
    }
    $builderReadmePath = if ($directory.FullName -eq $documentationRoot) {
        Join-Path $repoRoot 'README.md'
    } else {
        Join-Path $directory.FullName 'README.md'
    }
    if (-not (Test-Path -LiteralPath $builderReadmePath -PathType Leaf)) {
        Add-Issue "Missing request builder README entry: $(Get-RepositoryRelativePath -Path $builderReadmePath)"
    }
    else {
        $builderReadmeText = Get-Content -Raw -LiteralPath $builderReadmePath
        $expectedBuilderLink = if ($directory.FullName -eq $documentationRoot) { '(docs/request-building.md)' } else { '(request-building.md)' }
        if (-not $builderReadmeText.Contains($expectedBuilderLink) -or
            -not $builderReadmeText.Contains('CreateRequest')) {
            Add-Issue "$(Get-RepositoryRelativePath -Path $builderReadmePath) must introduce CreateRequest and link to its request builder guide."
        }
    }

    $runGuidePath = Join-Path $directory.FullName 'execution-api-transition.md'
    if (-not (Test-Path -LiteralPath $runGuidePath -PathType Leaf)) {
        Add-Issue "Missing run guide: $(Get-RepositoryRelativePath -Path $runGuidePath)"
    }
    else {
        $runGuideText = Get-Content -Raw -LiteralPath $runGuidePath
        foreach ($contract in @('AIRunResult', 'RequestedModel', 'RawFinishReason',
                'RoundCount', 'AIFinishReason', 'result.Text', 'result.Usage', 'result.Citations')) {
            if (-not $runGuideText.Contains($contract)) {
                Add-Issue "$(Get-RepositoryRelativePath -Path $runGuidePath) omits the $contract run result contract."
            }
        }
    }

    $featureGuidePath = Join-Path $directory.FullName "reasoning-and-search.md"
    if (-not (Test-Path -LiteralPath $featureGuidePath -PathType Leaf)) {
        Add-Issue "Missing reasoning/search guide: $(Get-RepositoryRelativePath -Path $featureGuidePath)"
    }
    else {
        $featureGuideText = Get-Content -Raw -LiteralPath $featureGuidePath
        foreach ($requiredFeature in @('WithReasoning', 'CachePreservation.Required', 'WithWebSearch',
                'WithFileSearch', 'FileSearchStore', 'run.Citations', 'ContentIndex', 'IAIRequestFeatureService')) {
            if (-not $featureGuideText.Contains($requiredFeature)) {
                Add-Issue "$(Get-RepositoryRelativePath -Path $featureGuidePath) omits the $requiredFeature contract."
            }
        }
        if ([regex]::Matches($featureGuideText, '(?m)^## ').Count -ne 6 -or
            [regex]::Matches($featureGuideText, '(?m)^```csharp\s*$').Count -ne 6) {
            Add-Issue "$(Get-RepositoryRelativePath -Path $featureGuidePath) must retain all six guide sections and examples."
        }
        if ($directory.FullName -ne $documentationRoot -and
            $featureGuideText.StartsWith('# Choose reasoning effort and answer with sources')) {
            Add-Issue "$(Get-RepositoryRelativePath -Path $featureGuidePath) still uses the English guide title."
        }
    }
    if ([regex]::Matches($tocText, '(?m)^\s*href:\s*reasoning-and-search\.md\s*$').Count -ne 1) {
        Add-Issue "$(Get-RepositoryRelativePath -Path $tocPath) must link to its local reasoning/search guide exactly once."
    }

    $providerGuidePath = Join-Path $directory.FullName "providers.md"
    if (Test-Path -LiteralPath $providerGuidePath -PathType Leaf) {
        $providerGuideText = Get-Content -Raw -LiteralPath $providerGuidePath
        if ($providerGuideText -notmatch 'Grok4_6|grok-4\.6' -or
            $providerGuideText -notmatch 'GrokReasoning\.XHigh') {
            Add-Issue "$(Get-RepositoryRelativePath -Path $providerGuidePath) omits Grok 4.6 or its XHigh reasoning example."
        }
        foreach ($imageContract in @('GrokImagineImage2_0', 'IImageGenerationService',
                'GenerateImagesAsync', 'EditImagesAsync', 'ImageAspectRatio', 'ImageOutputFormat.Auto')) {
            if (-not $providerGuideText.Contains($imageContract)) {
                Add-Issue "$(Get-RepositoryRelativePath -Path $providerGuidePath) omits the $imageContract image contract."
            }
        }
        foreach ($image25Contract in @('GptImage2_5Sunburst', 'GptImage2_5Flare',
                'gpt-image-2.5-sunburst', 'gpt-image-2.5-flare', '2026-09-08', 'ImageQuality.XHigh', 'ImageBackground.Transparent')) {
            if (-not $providerGuideText.Contains($image25Contract)) {
                Add-Issue "$(Get-RepositoryRelativePath -Path $providerGuidePath) omits the $image25Contract GPT Image 2.5 contract."
            }
        }
        foreach ($typedImageContract in @('ImageSize.Pixels', 'ImageSize.Preset', 'ImageResolution.TwoK',
                'ImageAspectRatio.ThreeByTwo', 'image-options-migration')) {
            if (-not $providerGuideText.Contains($typedImageContract)) {
                Add-Issue "$(Get-RepositoryRelativePath -Path $providerGuidePath) omits the $typedImageContract typed image contract."
            }
        }
        foreach ($codeBlock in [regex]::Matches($providerGuideText, '(?s)```csharp\s*\r?\n(.*?)```')) {
            if ($codeBlock.Groups[1].Value -match '\b(?:Quality|Background|OutputFormat|Size)\s*=\s*"' -or
                $codeBlock.Groups[1].Value -match '\bAspectRatio\s*=') {
                Add-Issue "$(Get-RepositoryRelativePath -Path $providerGuidePath) contains an executable example with legacy string image options. Use a text block for historical migration examples."
            }
        }
        foreach ($minor in @(7, 8)) {
            if ($providerGuideText -notmatch "Gemini3_${minor}Flash|gemini-3\.${minor}-flash") {
                Add-Issue "$(Get-RepositoryRelativePath -Path $providerGuidePath) omits Gemini 3.$minor Flash."
            }
        }
        foreach ($deepSeekContract in @('AIModels.DeepSeek.Flash', 'DeepSeekReasoning',
                'WithReasoning', 'reasoning_content', '393216')) {
            if (-not $providerGuideText.Contains($deepSeekContract)) {
                Add-Issue "$(Get-RepositoryRelativePath -Path $providerGuidePath) omits the $deepSeekContract DeepSeek contract."
            }
        }
    }
    else {
        Add-Issue "Missing provider guide: $(Get-RepositoryRelativePath -Path $providerGuidePath)"
    }

    $perplexityGuidePath = Join-Path $directory.FullName "perplexity.md"
    if (-not (Test-Path -LiteralPath $perplexityGuidePath -PathType Leaf)) {
        Add-Issue "Missing Perplexity guide: $(Get-RepositoryRelativePath -Path $perplexityGuidePath)"
    }
    else {
        $perplexityGuideText = Get-Content -Raw -LiteralPath $perplexityGuidePath
        foreach ($contract in @('PerplexityAgentOptions', 'StartRunAsync', 'run.Citations', 'MaxSteps',
            'StartBackgroundAsync', 'WaitForCompletionAsync', 'ResumeBackgroundRun', 'CancelAsync',
            'PreviousResponseId', 'PerplexitySearchClient', 'PerplexityContextualizedEmbeddingProvider',
            'GetQueryEmbeddingAsync', 'HammingDistance', 'pplx-embed-v1-4b', 'perplexity/sonar', '2026-09-27')) {
            if (-not $perplexityGuideText.Contains($contract)) {
                Add-Issue "$(Get-RepositoryRelativePath -Path $perplexityGuidePath) omits the $contract contract."
            }
        }
        if ([regex]::Matches($perplexityGuideText, '(?m)^## ').Count -lt 8 -or
            [regex]::Matches($perplexityGuideText, '(?m)^```csharp\s*$').Count -lt 6) {
            Add-Issue "$(Get-RepositoryRelativePath -Path $perplexityGuidePath) must retain all feature explanations and examples."
        }
    }
    if ([regex]::Matches($tocText, '(?m)^\s*href:\s*perplexity\.md\s*$').Count -ne 1) {
        Add-Issue "$(Get-RepositoryRelativePath -Path $tocPath) must link to its local Perplexity guide exactly once."
    }

    $fableGuidePath = Join-Path $directory.FullName "fable-5-1.md"
    if (-not (Test-Path -LiteralPath $fableGuidePath -PathType Leaf)) {
        Add-Issue "Missing Fable 5.1 guide: $(Get-RepositoryRelativePath -Path $fableGuidePath)"
    }
    else {
        $fableGuideText = Get-Content -Raw -LiteralPath $fableGuidePath
        foreach ($requiredContract in @('ClaudeFable5_1', 'ClaudeMythos5_1', 'ClaudeThinkingDisplay.Updates',
                'StreamingContentType.Reasoning', 'WithTurnInstruction', 'WithConversationInstruction',
                'CachePreservation.Required', 'WithThinkingBinding', 'LastInputTransformations',
                'prefix_binding_mismatch', 'model_binding_mismatch', 'clear_at', 'ForceFunctionName')) {
            if (-not $fableGuideText.Contains($requiredContract)) {
                Add-Issue "$(Get-RepositoryRelativePath -Path $fableGuidePath) omits the $requiredContract contract."
            }
        }
        if ([regex]::Matches($fableGuideText, '(?m)^## ').Count -ne 7 -or
            [regex]::Matches($fableGuideText, '(?m)^```csharp\s*$').Count -ne 4) {
            Add-Issue "$(Get-RepositoryRelativePath -Path $fableGuidePath) must retain all seven guide sections and four examples."
        }
        if ($directory.FullName -ne $documentationRoot -and
            $fableGuideText.StartsWith('# Keep long Claude Fable 5.1 tasks observable')) {
            Add-Issue "$(Get-RepositoryRelativePath -Path $fableGuidePath) still uses the English guide title."
        }
    }
    if ([regex]::Matches($tocText, '(?m)^\s*href:\s*fable-5-1\.md\s*$').Count -ne 1) {
        Add-Issue "$(Get-RepositoryRelativePath -Path $tocPath) must link to its local Fable 5.1 guide exactly once."
    }
}
foreach ($directory in $localizedDirectories) {
    foreach ($document in Get-ChildItem -LiteralPath $directory.FullName -Filter "*.md" -File -Recurse) {
        $text = Get-Content -Raw -LiteralPath $document.FullName
        if ($text -match '\]\(\.\./execution-api-transition\.md(?:[?#][^)]*)?\)') {
            Add-Issue "$(Get-RepositoryRelativePath -Path $document.FullName) must link to its translated Run guide."
        }
        if ($text -match '\]\(\.\./reasoning-and-search\.md(?:[?#][^)]*)?\)') {
            Add-Issue "$(Get-RepositoryRelativePath -Path $document.FullName) must link to its translated reasoning/search guide."
        }
        if ($text -match '\]\(\.\./request-building\.md(?:[?#][^)]*)?\)') {
            Add-Issue "$(Get-RepositoryRelativePath -Path $document.FullName) must link to its translated request builder guide."
        }
        if ($text -match '\]\(\.\./fable-5-1\.md(?:[?#][^)]*)?\)') {
            Add-Issue "$(Get-RepositoryRelativePath -Path $document.FullName) must link to its translated Fable 5.1 guide."
        }
        if ($text -match '\]\(\.\./perplexity\.md(?:[?#][^)]*)?\)') {
            Add-Issue "$(Get-RepositoryRelativePath -Path $document.FullName) must link to its translated Perplexity guide."
        }
        if ($text -match '\]\(\.\./v8-migration\.md(?:[?#][^)]*)?\)') {
            Add-Issue "$(Get-RepositoryRelativePath -Path $document.FullName) must link to its translated v8 migration guide."
        }
    }
}

$forbiddenPatterns = [ordered]@{
    'incorrect xAI namespace casing' = '(?-i:using\s+Mythosia\.AI\.Services\.XAI\s*;)'
    'service.FunctionCallingPolicy assignment' = 'service\.FunctionCallingPolicy\s*='
    'direct assignment to the read-only Model property' = 'service\.Model\s*=\s*AIModels\.'
    'nonexistent AlibabaCloud endpoint enum' = 'EndpointPlatform\.AlibabaCloud'
    'removed FunctionCallingPolicy.Auto/Required/None value' = 'FunctionCallingPolicy\.(Auto|Required|None)'
    'removed generic AlibabaModels.Qwen3 constant' = 'AlibabaModels\.Qwen3\b'
    'nonexistent UseQwenMaxModel helper' = '\.UseQwenMaxModel\('
    'stale GitHub Wiki link' = 'https://github\.com/AJ-comp/Mythosia\.AI/wiki'
    'old Chat UI samples directory' = 'samples/Mythosia\.AI\.Samples\.ChatUi'
}

foreach ($document in $activeDocumentation) {
    $text = Get-Content -Raw -LiteralPath $document.FullName
    foreach ($pattern in $forbiddenPatterns.GetEnumerator()) {
        if ($text -match $pattern.Value) {
            $relativeDocument = Get-RepositoryRelativePath -Path $document.FullName
            Add-Issue "$relativeDocument contains $($pattern.Key)."
        }
    }

    # Migration guides may intentionally show old calls. Current executable examples
    # should use supported catalogue entries, even when compatibility constants remain.
    if ($document.Name -notmatch '(?i)migration') {
        foreach ($codeBlock in [regex]::Matches($text, '(?ms)^```(?:csharp|cs|c#)[ \t]*\r?\n(?<code>.*?)^```[ \t]*$')) {
            if ($codeBlock.Groups['code'].Value -match 'AIModels\.(?:OpenAI\.(?:Gpt5|Gpt5Mini|Gpt5Nano|Gpt5Pro|O3|O3Pro)|DeepSeek\.(?:Chat|Reasoner|V4Flash))\b') {
                Add-Issue "$(Get-RepositoryRelativePath -Path $document.FullName) uses a deprecated model constant in a current C# example."
            }
        }
    }
}

$alibabaReadmePath = Join-Path $repoRoot "src/core/Mythosia.AI.Providers.Alibaba/README.md"
$alibabaReadme = Get-Content -Raw -LiteralPath $alibabaReadmePath
if ($alibabaReadme -match '(?s)StreamAsync\([^)]*\).*?chunk\.Content') {
    Add-Issue "The Alibaba README treats string streaming chunks as StreamingContent."
}

$linkDocuments = @(
    $activeDocumentation
    Get-Item -LiteralPath (Join-Path $repoRoot "RELEASE_NOTES.md")
    $releasePackages | ForEach-Object {
        Get-Item -LiteralPath (Join-Path $repoRoot $_.ReleaseNotes)
    }
) | Sort-Object -Property FullName -Unique

$rootPrefix = $repoRoot.TrimEnd(
    [System.IO.Path]::DirectorySeparatorChar,
    [System.IO.Path]::AltDirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar

foreach ($document in $linkDocuments) {
    $text = Get-Content -Raw -LiteralPath $document.FullName
    $matches = [regex]::Matches($text, '(?<!\!)\[[^\]]+\]\((?<target>[^)]+)\)')
    foreach ($match in $matches) {
        $target = $match.Groups["target"].Value.Trim()
        if ($target.StartsWith("<") -and $target.EndsWith(">")) {
            $target = $target.Substring(1, $target.Length - 2)
        }
        if ($target -match '^(?i:https?|mailto):' -or $target.StartsWith("#")) {
            continue
        }

        $target = ($target -split '\s+["'']', 2)[0]
        $targetPath = ($target -split '#', 2)[0]
        $targetPath = ($targetPath -split '\?', 2)[0]
        if ([string]::IsNullOrWhiteSpace($targetPath) -or $targetPath -match '[{}*]') {
            continue
        }

        $targetPath = [System.Uri]::UnescapeDataString($targetPath)
        if ($targetPath.StartsWith("/")) {
            $resolvedTarget = [System.IO.Path]::GetFullPath(
                (Join-Path $repoRoot $targetPath.TrimStart("/")))
        }
        else {
            $resolvedTarget = [System.IO.Path]::GetFullPath(
                (Join-Path $document.DirectoryName $targetPath))
        }

        if (-not $resolvedTarget.StartsWith($rootPrefix, [System.StringComparison]::OrdinalIgnoreCase) -and
            -not $resolvedTarget.Equals($repoRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
            $relativeDocument = Get-RepositoryRelativePath -Path $document.FullName
            Add-Issue "$relativeDocument has a local link outside the repository: $target"
            continue
        }
        if (-not (Test-Path -LiteralPath $resolvedTarget)) {
            $relativeDocument = Get-RepositoryRelativePath -Path $document.FullName
            Add-Issue "$relativeDocument has a missing local link target: $target"
        }
        elseif (Test-Path -LiteralPath $resolvedTarget -PathType Container) {
            $relativeDocument = Get-RepositoryRelativePath -Path $document.FullName
            Add-Issue "$relativeDocument links to a directory instead of a document: $target"
        }
    }
}

if ($issues.Count -ne 0) {
    $details = $issues | Sort-Object -Unique | ForEach-Object { " - $_" }
    throw "Release documentation validation failed:`n$($details -join [Environment]::NewLine)"
}

Write-Host "Release documentation and NuGet metadata validation passed."
Write-Host "Validated $($releasePackages.Count) release packages and $($linkDocuments.Count) Markdown files."
Write-Host "Validated v8 migration, model capabilities, completion cancellation and implementation migration, local tool returns/errors/cancellation, request builders, GPT Image 2.5, Perplexity Agent/Search/embeddings, DeepSeek Flash, Grok 4.6, Grok Imagine Image 2.0, Gemini 3.7/3.8, Run, reasoning/search, and Fable 5.1 guide coverage and navigation for $($guideDirectories.Count) documentation languages."
