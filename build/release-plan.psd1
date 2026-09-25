# Explicit publication allowlist. Consumer-only packages are never packed or published.
@{
    SchemaVersion = 1
    PreviousReleaseCommit = 'ce2af19ab559845da2bd477b824b3bdf76846988'
    BaselineNote = 'The preceding fifteen-package release was published from ce2af19, verified against official NuGet repository metadata. Only RAG and PostgreSQL production code changed for this patch; their published dependency versions remain unchanged.'
    Packages = @(
        @{
            Id = 'Mythosia.AI.Rag'
            Version = '8.1.1'
            Project = 'src/rag/Mythosia.AI.Rag/Mythosia.AI.Rag.csproj'
            TargetFramework = 'netstandard2.1'
            LicenseExpression = 'MIT'
            ProjectUrl = 'https://aj-comp.github.io/Mythosia.AI/docs/rag.html'
            ReleaseNotesUrl = 'https://github.com/AJ-comp/Mythosia.AI/blob/main/src/rag/Mythosia.AI.Rag/RELEASE_NOTES.md#v811'
            Dependencies = @{}
            FixedDependencies = @{
                'Mythosia.AI.Abstractions' = '4.1.0'
                'Mythosia.AI.Rag.Abstractions' = '6.3.0'
                'Mythosia.VectorDb.InMemory' = '4.2.0'
                'Mythosia.Documents.Office' = '1.1.1'
                'Mythosia.Documents.Pdf' = '1.1.2'
                'SharpZipLib' = '1.4.2'
            }
        }
        @{
            Id = 'Mythosia.VectorDb.Postgres'
            Version = '10.8.1'
            Project = 'src/vectordb/Mythosia.VectorDb.Postgres/Mythosia.VectorDb.Postgres.csproj'
            TargetFramework = 'net10.0'
            LicenseExpression = 'MIT'
            ProjectUrl = 'https://github.com/AJ-comp/Mythosia.AI'
            ReleaseNotesUrl = 'https://github.com/AJ-comp/Mythosia.AI/blob/main/src/vectordb/Mythosia.VectorDb.Postgres/RELEASE_NOTES.md#v1081'
            Dependencies = @{}
            FixedDependencies = @{ 'Mythosia.VectorDb.Abstractions' = '4.1.0'; 'Npgsql' = '10.0.2' }
        }
    )
    # Keep the complete compatibility matrix, resolving these unchanged packages from NuGet.
    ConsumerOnlyPackages = @(
        @{ Id = 'Mythosia.AI.Abstractions'; Version = '4.1.0' }
        @{ Id = 'Mythosia.AI'; Version = '8.1.0' }
        @{ Id = 'Mythosia.AI.Providers.Alibaba'; Version = '3.0.1' }
        @{ Id = 'Mythosia.VectorDb.Abstractions'; Version = '4.1.0' }
        @{ Id = 'Mythosia.AI.Rag.Abstractions'; Version = '6.3.0' }
        @{ Id = 'Mythosia.VectorDb.InMemory'; Version = '4.2.0' }
        @{ Id = 'Mythosia.Documents.Office'; Version = '1.1.1' }
        @{ Id = 'Mythosia.Documents.Pdf'; Version = '1.1.2' }
        @{ Id = 'Mythosia.Documents.Hwp'; Version = '1.0.2' }
        @{ Id = 'Mythosia.VectorDb.Qdrant'; Version = '4.2.0' }
        @{ Id = 'Mythosia.VectorDb.Pinecone'; Version = '4.0.2' }
        @{ Id = 'Mythosia.AI.Rag.Search.Pixie'; Version = '0.1.0-preview' }
        @{ Id = 'Mythosia.AI.Mcp'; Version = '0.1.1-preview' }
        @{ Id = 'Mythosia.AI.Serving.Vllm'; Version = '1.0.0' }
    )
}
