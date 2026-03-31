$script:ToolDll = [System.IO.Path]::Combine($PSScriptRoot, 'net10.0', 'any', 'Mythosia.VectorDb.Tools.dll')

function Invoke-MythosiaVectorDb {
    & dotnet exec $script:ToolDll $args
}

Set-Alias -Name 'mythosia-vectordb' -Value 'Invoke-MythosiaVectorDb' -Scope Global

Export-ModuleMember -Function Invoke-MythosiaVectorDb -Alias mythosia-vectordb
