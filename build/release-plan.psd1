# Explicit publication allowlist. Consumer-only packages are never packed or published.
@{
    SchemaVersion = 1
    PreviousReleaseCommit = 'b3b389f778bc68f0941e6e0a418246fe7be601b4'
    BaselineNote = 'RAG 8.1.1 publication was verified against the official NuGet package repository metadata at b3b389f. This compatible feature release publishes RAG and RAG.Abstractions for document/query-aware embeddings. All other packages resolve from their existing published versions.'
    Packages = @(
        @{
            Id = 'Mythosia.AI.Rag.Abstractions'
            Version = '6.4.0'
            Project = 'src/rag/Mythosia.AI.Rag.Abstractions/Mythosia.AI.Rag.Abstractions.csproj'
            TargetFramework = 'netstandard2.1'
            LicenseExpression = 'MIT'
            ProjectUrl = 'https://github.com/AJ-comp/Mythosia.AI/tree/main/src/rag/Mythosia.AI.Rag.Abstractions'
            ReleaseNotesUrl = 'https://github.com/AJ-comp/Mythosia.AI/blob/main/src/rag/Mythosia.AI.Rag.Abstractions/RELEASE_NOTES.md#v640'
            Dependencies = @{}
            FixedDependencies = @{ 'Mythosia.VectorDb.Abstractions' = '4.1.0' }
        }
        @{
            Id = 'Mythosia.AI.Rag'
            Version = '8.2.0'
            Project = 'src/rag/Mythosia.AI.Rag/Mythosia.AI.Rag.csproj'
            TargetFramework = 'netstandard2.1'
            LicenseExpression = 'MIT'
            ProjectUrl = 'https://aj-comp.github.io/Mythosia.AI/docs/rag.html'
            ReleaseNotesUrl = 'https://github.com/AJ-comp/Mythosia.AI/blob/main/src/rag/Mythosia.AI.Rag/RELEASE_NOTES.md#v820'
            Dependencies = @{ 'Mythosia.AI.Rag.Abstractions' = 'Mythosia.AI.Rag.Abstractions' }
            FixedDependencies = @{
                'Mythosia.AI.Abstractions' = '4.1.0'
                'Mythosia.VectorDb.InMemory' = '4.2.0'
                'Mythosia.Documents.Office' = '1.1.1'
                'Mythosia.Documents.Pdf' = '1.1.2'
                'SharpZipLib' = '1.4.2'
            }
        }
    )
    # Keep the complete compatibility matrix, resolving these unchanged packages from NuGet.
    ConsumerOnlyPackages = @(
        @{ Id = 'Mythosia.AI.Abstractions'; Version = '4.1.0' }
        @{ Id = 'Mythosia.AI'; Version = '8.1.0' }
        @{ Id = 'Mythosia.AI.Providers.Alibaba'; Version = '3.0.1' }
        @{ Id = 'Mythosia.VectorDb.Abstractions'; Version = '4.1.0' }
        @{ Id = 'Mythosia.VectorDb.Postgres'; Version = '10.8.1' }
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
