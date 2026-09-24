[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"

$repoRoot = [System.IO.Path]::GetFullPath((Resolve-Path (Join-Path $PSScriptRoot "..")).Path)
$publishScriptPath = Join-Path $repoRoot "build/publish-nuget.ps1"
$consumerScriptPath = Join-Path $repoRoot "build/test-nuget-packages.ps1"
$prepareDocfxScriptPath = Join-Path $repoRoot "build/prepare-docfx-references.ps1"

function Assert-True {
    param(
        [bool]$Condition,
        [string]$Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

$tokens = $null
$parseErrors = $null
$publishAst = [System.Management.Automation.Language.Parser]::ParseFile(
    $publishScriptPath,
    [ref]$tokens,
    [ref]$parseErrors)
if (@($parseErrors).Count -ne 0) {
    throw "publish-nuget.ps1 has PowerShell parse errors: $($parseErrors -join '; ')"
}

$publishText = [System.IO.File]::ReadAllText($publishScriptPath)
$consumerText = [System.IO.File]::ReadAllText($consumerScriptPath)
$prepareDocfxText = [System.IO.File]::ReadAllText($prepareDocfxScriptPath)
$consumerAst = [System.Management.Automation.Language.Parser]::ParseFile(
    $consumerScriptPath,
    [ref]$tokens,
    [ref]$parseErrors)
Assert-True (@($parseErrors).Count -eq 0) `
    "test-nuget-packages.ps1 must parse before consumer validation can run."

$releaseSetAssignment = $publishAst.Find({
    param($node)
    $node -is [System.Management.Automation.Language.AssignmentStatementAst] -and
        $node.Left -is [System.Management.Automation.Language.VariableExpressionAst] -and
        $node.Left.VariablePath.UserPath -eq 'releasePackages'
}, $true)
$consumerIdsAssignment = $consumerAst.Find({
    param($node)
    $node -is [System.Management.Automation.Language.AssignmentStatementAst] -and
        $node.Left -is [System.Management.Automation.Language.VariableExpressionAst] -and
        $node.Left.VariablePath.UserPath -eq 'expectedIds'
}, $true)
Assert-True ($null -ne $releaseSetAssignment -and $null -ne $consumerIdsAssignment) `
    "Both publication and consumer validation must declare their explicit release set."
. (Join-Path $PSScriptRoot 'release-plan.ps1')
$releasePlan = Get-ReleasePlan
$releaseDefinitions = @($releasePlan.Packages)
$consumerIds = @(Invoke-Expression $consumerIdsAssignment.Right.Extent.Text)
$releaseIds = @($releaseDefinitions | ForEach-Object { [string]$_.Id })
Assert-True ($releaseSetAssignment.Right.Extent.Text.Contains('$releasePlan.Packages') -and
    $publishText.Contains("Get-ReleasePlan") -and $consumerText.Contains("Get-ReleasePlan")) `
    'Publication and consumers must load the shared explicit release plan.'
Assert-True (($consumerIds -join '|') -ceq ($releaseIds -join '|')) `
    'Package consumers must validate every shared-plan publication target.'
$precedingReleaseIds = @{}
foreach ($definition in $releaseDefinitions) {
    Assert-True (-not $precedingReleaseIds.ContainsKey([string]$definition.Id)) `
        "The release set must not repeat package $($definition.Id)."
    foreach ($dependency in $definition.Dependencies.GetEnumerator()) {
        Assert-True ($dependency.Key -ceq $dependency.Value -and
            $precedingReleaseIds.ContainsKey([string]$dependency.Value)) `
            "$($definition.Id) must depend on an earlier matching package in the release set: $($dependency.Key)."
    }
    foreach ($dependencyId in $definition.FixedDependencies.Keys) {
        Assert-True ($releaseIds -notcontains $dependencyId) `
            "$($definition.Id) must use the release version for $dependencyId instead of a fixed published version."
    }
    $precedingReleaseIds[[string]$definition.Id] = $true
}
$expectedRagDependencies = @{
    'Mythosia.AI.Rag.Abstractions' = @('Mythosia.VectorDb.Abstractions')
    'Mythosia.VectorDb.InMemory' = @('Mythosia.VectorDb.Abstractions', 'Mythosia.AI.Rag.Abstractions')
    'Mythosia.AI.Rag' = @('Mythosia.AI.Abstractions', 'Mythosia.AI.Rag.Abstractions', 'Mythosia.VectorDb.InMemory', 'Mythosia.Documents.Office', 'Mythosia.Documents.Pdf')
}
foreach ($packageId in $expectedRagDependencies.Keys) {
    $definition = @($releaseDefinitions | Where-Object { $_.Id -eq $packageId })[0]
    $expectedDependencies = @($expectedRagDependencies[$packageId])
    Assert-True ($definition.Dependencies.Count -eq $expectedDependencies.Count) `
        "$packageId must use the complete source-built RAG dependency set."
    foreach ($dependencyId in $expectedDependencies) {
        Assert-True ($definition.Dependencies[$dependencyId] -ceq $dependencyId) `
            "$packageId must validate $dependencyId against the version packed in this release."
    }
}
$luceneWarningExceptions = @{
    'Mythosia.VectorDb.Abstractions' = '4.1.0'
    'Mythosia.VectorDb.InMemory' = '4.2.0'
}
foreach ($definition in $releaseDefinitions) {
    [xml]$projectXml = Get-Content -Raw -LiteralPath (Join-Path $repoRoot $definition.Project)
    $warningExceptions = @($projectXml.SelectNodes('/Project/PropertyGroup/WarningsNotAsErrors') |
        Where-Object { $_.InnerText -match '(?i)(^|;)NU5104(;|$)' })
    $suppressedWarnings = @($projectXml.SelectNodes('/Project/PropertyGroup/NoWarn') |
        Where-Object { $_.InnerText -match '(?i)(^|;)NU5104(;|$)' })
    Assert-True ($suppressedWarnings.Count -eq 0) `
        "$($definition.Id) must not hide the stable-package/prerelease-dependency warning."
    if ($luceneWarningExceptions.ContainsKey([string]$definition.Id)) {
        $version = $luceneWarningExceptions[[string]$definition.Id]
        $expectedCondition = "'`$(Version)' == '$version'"
        Assert-True ($warningExceptions.Count -eq 1 -and
            $warningExceptions[0].InnerText -ceq '$(WarningsNotAsErrors);NU5104' -and
            $warningExceptions[0].Condition -ceq $expectedCondition -and
            [string]$projectXml.Project.PropertyGroup.Version -ceq $version) `
            "$($definition.Id) may only keep NU5104 nonfatal for its existing version $version."
        Assert-True ($definition.FixedDependencies.Count -eq 2 -and
            $definition.FixedDependencies['Lucene.Net'] -ceq '4.8.0-beta00016' -and
            $definition.FixedDependencies['Lucene.Net.Analysis.Common'] -ceq '4.8.0-beta00016') `
            "$($definition.Id) must retain only the reviewed pinned Lucene prerelease dependencies."
        $packageReferences = @($projectXml.Project.ItemGroup.PackageReference | Where-Object { $null -ne $_ })
        Assert-True ($packageReferences.Count -eq 2) `
            "$($definition.Id) must not extend the NU5104 exception to other package references."
        foreach ($reference in $packageReferences) {
            Assert-True (@('Lucene.Net', 'Lucene.Net.Analysis.Common') -contains [string]$reference.Include -and
                [string]$reference.Version -ceq '4.8.0-beta00016') `
                "$($definition.Id) must use the reviewed Lucene IDs and versions for its NU5104 exception."
        }
    }
    else {
        Assert-True ($warningExceptions.Count -eq 0) `
            "$($definition.Id) must not inherit the Lucene-specific NU5104 exception."
    }
}
$mcpRelease = @($releaseDefinitions | Where-Object { $_.Id -eq 'Mythosia.AI.Mcp' })
Assert-True ($mcpRelease.Count -eq 1 -and $mcpRelease[0].Dependencies.Count -eq 1 -and
    $mcpRelease[0].Dependencies['Mythosia.AI'] -eq 'Mythosia.AI' -and
    $mcpRelease[0].FixedDependencies.Count -eq 0) `
    "The MCP package must validate its minimum dependency against the core built in the same release."
$vllmConsumerOnly = @($releasePlan.ConsumerOnlyPackages | Where-Object { $_.Id -eq 'Mythosia.AI.Serving.Vllm' })
Assert-True ($releaseIds -notcontains 'Mythosia.AI.Serving.Vllm' -and
    $vllmConsumerOnly.Count -eq 1 -and $vllmConsumerOnly[0].Version -ceq '1.0.0') `
    'The unchanged vLLM package must be tested from NuGet without becoming a publication target.'
$mappingFunction = $consumerAst.Find({
    param($node)
    $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and
        $node.Name -eq 'Get-ReleasePackageSourceMapping'
}, $true)
Assert-True ($null -ne $mappingFunction) `
    "Consumer package source mapping must be generated from the explicit release set."
Invoke-Expression $mappingFunction.Extent.Text
[xml]$mappingXml = '<mapping>' + (Get-ReleasePackageSourceMapping -PackageIds $consumerIds) + '</mapping>'
$mappedIds = @($mappingXml.mapping.package | ForEach-Object { [string]$_.pattern })
Assert-True (($mappedIds -join '|') -ceq ($releaseIds -join '|')) `
    "Each released package, including RAG contracts, must map to local artifacts by exact ID; only unchanged dependencies may come from NuGet."
Assert-True ($consumerText.Contains('Get-ReleasePackageSourceMapping -PackageIds $expectedIds') -and
    [regex]::IsMatch($consumerText, '<packageSource key="release-artifacts">\r?\n\$releasePackageSourceMapping')) `
    "The generated exact-ID source mapping must be used in the consumer NuGet.config."

$consumerCommands = @($consumerAst.FindAll({
    param($node)
    $node -is [System.Management.Automation.Language.CommandAst] -and
        $node.GetCommandName() -eq 'Invoke-PackageConsumer'
}, $true))
foreach ($consumerName in @('RagConsumer', 'RagNetStandardConsumer')) {
    $ragConsumers = @($consumerCommands | Where-Object { $_.Extent.Text.Contains('-Name "' + $consumerName + '"') })
    Assert-True ($ragConsumers.Count -eq 1 -and
        $ragConsumers[0].Extent.Text.Contains('-PackageId "Mythosia.AI.Rag"') -and
        $ragConsumers[0].Extent.Text.Contains('"Mythosia.AI.Abstractions/$($versions[''Mythosia.AI.Abstractions''])"') -and
        $ragConsumers[0].Extent.Text.Contains('"Mythosia.VectorDb.Abstractions/$($versions[''Mythosia.VectorDb.Abstractions''])"') -and
        $ragConsumers[0].Extent.Text.Contains('"Mythosia.AI.Rag.Abstractions/$($versions[''Mythosia.AI.Rag.Abstractions''])"') -and
        $ragConsumers[0].Extent.Text.Contains('"Mythosia.VectorDb.InMemory/$($versions[''Mythosia.VectorDb.InMemory''])"') -and
        $ragConsumers[0].Extent.Text.Contains('-UnexpectedLibraries @("Mythosia.AI/*", "Mythosia.AI.Providers.Alibaba/*")')) `
        "$consumerName must consume the RAG package with every changed dependency from this release and without the core implementation."
}
foreach ($consumerName in @('McpConsumer', 'McpNetStandardConsumer')) {
    $mcpConsumers = @($consumerCommands | Where-Object { $_.Extent.Text.Contains('-Name "' + $consumerName + '"') })
    Assert-True ($mcpConsumers.Count -eq 1 -and
        $mcpConsumers[0].Extent.Text.Contains('-PackageId "Mythosia.AI.Mcp"') -and
        $mcpConsumers[0].Extent.Text.Contains('"Mythosia.AI/$($versions[''Mythosia.AI''])"') -and
        $mcpConsumers[0].Extent.Text.Contains('"Mythosia.AI.Abstractions/$($versions[''Mythosia.AI.Abstractions''])"')) `
        "$consumerName must consume the MCP package with the core and abstractions from the same release."
}
foreach ($consumerName in @('VllmConsumer', 'VllmNetStandardConsumer')) {
    $vllmConsumers = @($consumerCommands | Where-Object { $_.Extent.Text.Contains('-Name "' + $consumerName + '"') })
    Assert-True ($vllmConsumers.Count -eq 1 -and
        $vllmConsumers[0].Extent.Text.Contains('-PackageId "Mythosia.AI.Serving.Vllm"') -and
        $vllmConsumers[0].Extent.Text.Contains('"Newtonsoft.Json/13.0.4"') -and
        $vllmConsumers[0].Extent.Text.Contains('-UnexpectedLibraries @("Mythosia.AI/*", "Mythosia.AI.Abstractions/*", "Mythosia.AI.Providers.*", "Mythosia.AI.Rag*", "Mythosia.AI.Mcp/*")')) `
        "$consumerName must consume the standalone vLLM serving package without pulling in chat, RAG or MCP packages."
}
Assert-True ($publishText.Contains('Add-Type -AssemblyName System.Net.Http')) `
    "Windows PowerShell publication paths must load System.Net.Http before creating HttpClient."

$packStart = $publishText.IndexOf('if ($Mode -eq "Pack") {', [System.StringComparison]::Ordinal)
$packCommand = $publishText.IndexOf('& dotnet pack', $packStart, [System.StringComparison]::Ordinal)
$packCleanCheck = $publishText.IndexOf('Assert-CleanGitWorktree', $packStart, [System.StringComparison]::Ordinal)
Assert-True ($packStart -ge 0 -and $packCommand -gt $packStart) `
    "Could not locate the release pack branch."
Assert-True ($packCleanCheck -gt $packStart -and $packCleanCheck -lt $packCommand) `
    "Release packing must verify a clean worktree before invoking dotnet pack."
Assert-True ($publishText.Contains('-p:TreatWarningsAsErrors=true')) `
    "Release packing must fail when a package build emits a compiler or NuGet warning."
Assert-True ($publishText.Contains('[switch]$AllowDirtyValidationPack')) `
    "Development-only dirty packing must be an explicit switch."
Assert-True ($publishText.Contains('provenance = $packProvenance')) `
    "The release manifest must record pack provenance."
Assert-True ($publishText.Contains('schemaVersion = 3')) `
    "The publish script must emit release manifest schema 3."
Assert-True ($consumerText.Contains('[int]$manifest.schemaVersion -ne 3')) `
    "Consumer smoke tests must validate release manifest schema 3."
Assert-True ($consumerText.Contains('$gitExitCode = $LASTEXITCODE')) `
    "Consumer smoke tests must capture git's exit code before running a PowerShell pipeline."
Assert-True ($prepareDocfxText.Contains('"-p:TreatWarningsAsErrors=true"')) `
    "The DocFX reference build must reject compiler and analyzer warnings."

$functions = @($publishAst.FindAll({
    param($node)
    $node -is [System.Management.Automation.Language.FunctionDefinitionAst]
}, $true))
$manifestFunction = $functions |
    Where-Object { $_.Name -eq "Assert-PublishableManifest" } |
    Select-Object -First 1
Assert-True ($null -ne $manifestFunction) `
    "The publishable-manifest guard is missing."
Invoke-Expression $manifestFunction.Extent.Text
$testCommit = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"
Assert-PublishableManifest -Manifest ([pscustomobject]@{
    schemaVersion = 3
    sourceCommit = $testCommit
    provenance = "clean-release"
}) -ExpectedSourceCommit $testCommit
$developmentManifestRejected = $false
try {
    Assert-PublishableManifest -Manifest ([pscustomobject]@{
        schemaVersion = 3
        sourceCommit = $testCommit
        provenance = "development-validation"
    }) -ExpectedSourceCommit $testCommit
}
catch {
    if ($_.Exception.Message -like "Release manifest is not publishable*") {
        $developmentManifestRejected = $true
    }
    else {
        throw
    }
}
Assert-True $developmentManifestRejected `
    "Push mode must reject development-validation manifests."

$duplicateVersionGuard = $publishAst.Find({
    param($node)
    $node -is [System.Management.Automation.Language.IfStatementAst] -and
        $node.Extent.Text.StartsWith('if ($publishedCount -gt 0 -and -not $AllowPartialResume)')
}, $true)
Assert-True ($null -ne $duplicateVersionGuard) `
    "Publication must reject existing versions before attempting any package push."
foreach ($packageId in @('Mythosia.VectorDb.Abstractions', 'Mythosia.AI.Rag.Abstractions', 'Mythosia.VectorDb.InMemory')) {
    $publicationState = @([pscustomobject]@{
        Package = [pscustomobject]@{ Id = $packageId; Version = '1.0.0' }
        Exists = $true
    })
    $publishedCount = 1
    $AllowPartialResume = $false
    $existingVersionRejected = $false
    try {
        Invoke-Expression $duplicateVersionGuard.Extent.Text
    }
    catch {
        if ($_.Exception.Message -like "A target package version already exists*$packageId*") {
            $existingVersionRejected = $true
        }
        else {
            throw
        }
    }
    Assert-True $existingVersionRejected `
        "Packing $packageId from source must not permit republishing an existing version."
}

$symbolResumeFunction = $functions |
    Where-Object { $_.Name -eq "Push-VerifiedResumeSymbols" } |
    Select-Object -First 1
Assert-True ($null -ne $symbolResumeFunction) `
    "The provenance-gated symbol resume function is missing."
$symbolResumeText = $symbolResumeFunction.Extent.Text
Assert-True ($symbolResumeText.Contains('ResumeProvenanceVerified')) `
    "Symbol resume must require verified main-package provenance."
Assert-True ($symbolResumeText.Contains('--skip-duplicate')) `
    "Verified symbol resume must tolerate NuGet's pending-symbol 409 response."

$conflictFunction = $functions |
    Where-Object { $_.Name -eq "Test-IsNuGetConflictOutput" } |
    Select-Object -First 1
Assert-True ($null -ne $conflictFunction) `
    "NuGet conflict classification is missing."
Invoke-Expression $conflictFunction.Extent.Text
Assert-True (Test-IsNuGetConflictOutput -Output @(
    "Response status code does not indicate success: 409 (Conflict).")) `
    "A NuGet 409 must be recognized for provenance-gated resume recovery."
Assert-True (-not (Test-IsNuGetConflictOutput -Output @(
    "Response status code does not indicate success: 400 (Bad Request)."))) `
    "A non-conflict NuGet failure must not enter resume recovery."

$waitFunction = $functions |
    Where-Object { $_.Name -eq "Wait-ForVerifiedRemotePackage" } |
    Select-Object -First 1
Assert-True ($null -ne $waitFunction) `
    "Eventual-consistency provenance polling is missing."
Invoke-Expression $waitFunction.Extent.Text
$expectedRemoteCommit = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"
$remotePackage = [pscustomobject]@{ Id = "Safety.Test"; Version = "1.0.0" }
$script:indexChecks = 0
$script:packageReads = 0
function Test-PackageVersionExists {
    $script:indexChecks++
    return $true
}
function Get-RemotePackageSourceCommit {
    $script:packageReads++
    if ($script:packageReads -eq 1) {
        return $null
    }
    return $expectedRemoteCommit
}
Wait-ForVerifiedRemotePackage `
    -PackageBase "https://example.invalid/" `
    -Package $remotePackage `
    -ExpectedSourceCommit $expectedRemoteCommit `
    -MaxAttempts 2 `
    -DelaySeconds 0
Assert-True ($script:indexChecks -eq 2 -and $script:packageReads -eq 2) `
    "Provenance polling must retry when the index is visible before the remote nupkg is readable."

function Get-RemotePackageSourceCommit {
    return "cccccccccccccccccccccccccccccccccccccccc"
}
$mismatchedRemoteRejected = $false
try {
    Wait-ForVerifiedRemotePackage `
        -PackageBase "https://example.invalid/" `
        -Package $remotePackage `
        -ExpectedSourceCommit $expectedRemoteCommit `
        -MaxAttempts 1 `
        -DelaySeconds 0
}
catch {
    if ($_.Exception.Message -like "Published package*was not built from release manifest commit*") {
        $mismatchedRemoteRejected = $true
    }
    else {
        throw
    }
}
Assert-True $mismatchedRemoteRejected `
    "A resume-time remote package with different provenance must be rejected."

Invoke-Expression $symbolResumeFunction.Extent.Text
$AllowPartialResume = $true
$unverifiedState = [pscustomobject]@{
    Package = [pscustomobject]@{
        Id = "Safety.Test"
        Version = "1.0.0"
        SymbolPackagePath = "must-not-be-pushed.snupkg"
    }
    ResumeProvenanceVerified = $false
}
$unverifiedResumeRejected = $false
try {
    Push-VerifiedResumeSymbols -State $unverifiedState
}
catch {
    if ($_.Exception.Message -like "Symbol resume requires verified main-package provenance*") {
        $unverifiedResumeRejected = $true
    }
    else {
        throw
    }
}
Assert-True $unverifiedResumeRejected `
    "Symbol-only duplicate tolerance must reject unverified main-package provenance before invoking dotnet."

$commands = @($publishAst.FindAll({
    param($node)
    $node -is [System.Management.Automation.Language.CommandAst]
}, $true))
$skipCommands = @($commands | Where-Object { $_.Extent.Text.Contains('--skip-duplicate') })
Assert-True ($skipCommands.Count -eq 1) `
    "--skip-duplicate must appear exactly once in the release script."
Assert-True ($skipCommands[0].Extent.StartOffset -ge $symbolResumeFunction.Extent.StartOffset -and
    $skipCommands[0].Extent.EndOffset -le $symbolResumeFunction.Extent.EndOffset) `
    "--skip-duplicate is permitted only inside provenance-verified symbol resume."

$mainPushCommands = @($commands | Where-Object {
    $_.GetCommandName() -eq "dotnet" -and
    $_.Extent.Text.Contains('$package.PackagePath')
})
Assert-True ($mainPushCommands.Count -eq 1) `
    "Expected exactly one main-package push command."
Assert-True (-not $mainPushCommands[0].Extent.Text.Contains('--skip-duplicate')) `
    "Main-package pushes must never skip a duplicate before provenance verification."
Assert-True ($duplicateVersionGuard.Extent.EndOffset -lt $mainPushCommands[0].Extent.StartOffset) `
    "Every existing-version check must complete before the first main-package push."

$conflictRecoveryStart = $publishText.IndexOf(
    'if ($AllowPartialResume -and (Test-IsNuGetConflictOutput -Output $pushOutput))',
    [System.StringComparison]::Ordinal)
$conflictVerification = $publishText.IndexOf(
    'Wait-ForVerifiedRemotePackage',
    $conflictRecoveryStart,
    [System.StringComparison]::Ordinal)
$conflictTrust = $publishText.IndexOf(
    '$state.ResumeProvenanceVerified = $true',
    $conflictRecoveryStart,
    [System.StringComparison]::Ordinal)
Assert-True ($conflictRecoveryStart -ge 0 -and
    $conflictVerification -gt $conflictRecoveryStart -and
    $conflictTrust -gt $conflictVerification) `
    "A resume-time main-package 409 must become trusted only after the remote nupkg commit is verified."

$expectedActions = [ordered]@{
    "actions/checkout" = "3d3c42e5aac5ba805825da76410c181273ba90b1"
    "actions/setup-dotnet" = "a98b56852c35b8e3190ac28c8c2271da59106c68"
    "actions/setup-python" = "5fda3b95a4ea91299a34e894583c3862153e4b97"
    "actions/cache" = "55cc8345863c7cc4c66a329aec7e433d2d1c52a9"
    "actions/upload-artifact" = "043fb46d1a93c77aae656e7c1c64a875d1fc6a0a"
    "actions/download-artifact" = "3e5f45b2cfb9172054b4087a40e8e0b5a5461e7c"
    "actions/upload-pages-artifact" = "fc324d3547104276b827a68afc52ff2a11cc49c9"
    "actions/deploy-pages" = "cd2ce8fcbc39b97be8ca5fce6e763baed58fa128"
}
$workflowPaths = @(
    (Join-Path $repoRoot ".github/workflows/ci.yml"),
    (Join-Path $repoRoot ".github/workflows/publish-nuget.yml"),
    (Join-Path $repoRoot ".github/workflows/docs.yml")
)
$workflowText = ($workflowPaths | ForEach-Object {
    [System.IO.File]::ReadAllText($_)
}) -join [Environment]::NewLine
foreach ($workflowPath in $workflowPaths | Select-Object -First 2) {
    Assert-True ([System.IO.File]::ReadAllText($workflowPath).Contains('./build/test-nuget-packages.ps1 -ArtifactsDirectory ./artifacts')) `
        "CI and NuGet publication must run the shared release-plan consumer validation."
    $text = [System.IO.File]::ReadAllText($workflowPath)
    Assert-True ($text.Contains('python build/prepare-pixie-model.py') -and
        $text.Contains('./build/test-pixie-model.ps1 -NoBuild') -and
        $text.Contains('./build/test-release-readiness-safety.ps1')) `
        "Every package validation workflow must prepare pinned model assets, execute real inference, and test readiness rejection cases."
    Assert-True (-not $text.Contains('prepare-pixie-model.py --validate')) `
        'Release validation must not regenerate tracked PIXIE parity fixtures.'
}
$ciText = [System.IO.File]::ReadAllText($workflowPaths[0])
$publicationText = [System.IO.File]::ReadAllText($workflowPaths[1])
Assert-True ($ciText.Contains('./build/test-release-readiness.ps1 -Offline')) `
    'Regular CI must verify release-plan structure and production change coverage.'
Assert-True ($publicationText.Contains('./build/test-release-readiness.ps1 @parameters') -and
    -not $publicationText.Contains('./build/test-release-readiness.ps1 -Offline')) `
    'Manual release validation must establish online NuGet availability even before publication is selected.'
Assert-True ($consumerText.Contains('test-release-package-probes.ps1') -and
    $consumerText.Contains('$verifiedLibraries.Contains("$id/$($versions[$id])")')) `
    'Isolated consumers must exercise every package from the shared release plan.'

$actionReferences = [regex]::Matches(
    $workflowText,
    'uses:\s+(actions/[A-Za-z0-9-]+)@([0-9a-f]{40})')
Assert-True ($actionReferences.Count -gt 0) `
    "No full-SHA GitHub Action references were found."
foreach ($reference in $actionReferences) {
    $action = $reference.Groups[1].Value
    $commit = $reference.Groups[2].Value
    Assert-True ($expectedActions.Contains($action)) `
        "Unexpected official action reference without a reviewed Node24 pin: $action"
    Assert-True ($expectedActions[$action] -eq $commit) `
        "Action $action is not pinned to its reviewed Node24 commit."
}
foreach ($expectedAction in $expectedActions.Keys) {
    Assert-True ($workflowText.Contains("$expectedAction@$($expectedActions[$expectedAction])")) `
        "Reviewed Node24 action pin is not used: $expectedAction"
}
$hardenedSolutionBuilds = [regex]::Matches(
    $workflowText,
    'dotnet build Mythosia\.AI\.slnx[^\r\n]*-p:TreatWarningsAsErrors=true')
Assert-True ($hardenedSolutionBuilds.Count -eq 3) `
    "CI, NuGet publication, and docs workflows must all reject solution build warnings."

Write-Host "NuGet publication safety contracts passed."
