param($installPath, $toolsPath, $package, $project)

if (Get-Module Mythosia.VectorDb.Tools) {
    Remove-Module Mythosia.VectorDb.Tools
}

Import-Module (Join-Path $toolsPath 'Mythosia.VectorDb.Tools.psm1') -Force
