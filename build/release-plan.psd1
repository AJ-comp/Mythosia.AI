# Explicit publication allowlist. Consumer-only packages are never packed or published.
@{
    SchemaVersion = 1
    PreviousReleaseCommit = '04719065d66e8d600c8792166ca393e2b9e3f562'
    BaselineNote = 'Serving.Abstractions/Ollama/LlamaCpp 1.0.0 and Serving.Vllm 1.1.0 publication was verified against official NuGet package repository metadata at 0471906. This release adds GPT-6.1 Sol, Claude Sonnet 5.5 and the independent vector diagnostics contract, and migrates InMemory and RAG diagnostics. Core 8.1.0 and AI.Abstractions 4.1.0 were also verified as published on NuGet. Unchanged packages resolve from existing published versions.'
    Packages = @(
        @{
            Id = 'Mythosia.AI.Abstractions'
            Version = '4.2.0'
            Project = 'src/core/Mythosia.AI.Abstractions/Mythosia.AI.Abstractions.csproj'
            TargetFramework = 'netstandard2.1'
            LicenseExpression = 'MIT'
            ProjectUrl = 'https://github.com/AJ-comp/Mythosia.AI/tree/main/src/core/Mythosia.AI.Abstractions'
            ReleaseNotesUrl = 'https://github.com/AJ-comp/Mythosia.AI/blob/main/src/core/Mythosia.AI.Abstractions/RELEASE_NOTES.md#v420'
            Dependencies = @{}
            FixedDependencies = @{ 'Mythosia' = '1.4.0' }
        }
        @{
            Id = 'Mythosia.AI'
            Version = '8.2.0'
            Project = 'src/core/Mythosia.AI/Mythosia.AI.csproj'
            TargetFramework = 'netstandard2.1'
            LicenseExpression = 'MIT'
            ProjectUrl = 'https://github.com/AJ-comp/Mythosia.AI'
            ReleaseNotesUrl = 'https://github.com/AJ-comp/Mythosia.AI/blob/main/src/core/Mythosia.AI/RELEASE_NOTES.md#v820'
            Dependencies = @{ 'Mythosia.AI.Abstractions' = 'Mythosia.AI.Abstractions' }
            FixedDependencies = @{ 'Azure.AI.OpenAI' = '2.1.0'; 'Newtonsoft.Json' = '13.0.4'; 'NJsonSchema' = '11.6.1'; 'System.Threading.Channels' = '10.0.10'; 'TiktokenSharp' = '1.2.1' }
        }
        @{
            Id = 'Mythosia.AI.Providers.Alibaba'
            Version = '3.0.2'
            Project = 'src/core/Mythosia.AI.Providers.Alibaba/Mythosia.AI.Providers.Alibaba.csproj'
            TargetFramework = 'netstandard2.1'
            LicenseExpression = 'MIT'
            ProjectUrl = 'https://github.com/AJ-comp/Mythosia.AI/tree/main/src/core/Mythosia.AI.Providers.Alibaba'
            ReleaseNotesUrl = 'https://github.com/AJ-comp/Mythosia.AI/blob/main/src/core/Mythosia.AI.Providers.Alibaba/RELEASE_NOTES.md#v302'
            Dependencies = @{ 'Mythosia.AI' = 'Mythosia.AI' }
            FixedDependencies = @{ 'TiktokenSharp' = '1.2.1' }
        }
        @{
            Id = 'Mythosia.VectorDb.Abstractions'
            Version = '4.2.0'
            Project = 'src/vectordb/Mythosia.VectorDb.Abstractions/Mythosia.VectorDb.Abstractions.csproj'
            TargetFramework = 'netstandard2.1'
            LicenseExpression = 'MIT'
            ProjectUrl = 'https://github.com/AJ-comp/Mythosia.AI'
            ReleaseNotesUrl = 'https://github.com/AJ-comp/Mythosia.AI/blob/main/src/vectordb/Mythosia.VectorDb.Abstractions/RELEASE_NOTES.md#v420'
            Dependencies = @{}
            FixedDependencies = @{ 'Lucene.Net' = '4.8.0-beta00016'; 'Lucene.Net.Analysis.Common' = '4.8.0-beta00016' }
        }
        @{
            Id = 'Mythosia.AI.Rag.Abstractions'
            Version = '6.5.0'
            Project = 'src/rag/Mythosia.AI.Rag.Abstractions/Mythosia.AI.Rag.Abstractions.csproj'
            TargetFramework = 'netstandard2.1'
            LicenseExpression = 'MIT'
            ProjectUrl = 'https://github.com/AJ-comp/Mythosia.AI/tree/main/src/rag/Mythosia.AI.Rag.Abstractions'
            ReleaseNotesUrl = 'https://github.com/AJ-comp/Mythosia.AI/blob/main/src/rag/Mythosia.AI.Rag.Abstractions/RELEASE_NOTES.md#v650'
            Dependencies = @{ 'Mythosia.VectorDb.Abstractions' = 'Mythosia.VectorDb.Abstractions' }
            FixedDependencies = @{}
        }
        @{
            Id = 'Mythosia.VectorDb.InMemory'
            Version = '4.3.0'
            Project = 'src/vectordb/Mythosia.VectorDb.InMemory/Mythosia.VectorDb.InMemory.csproj'
            TargetFramework = 'netstandard2.1'
            LicenseExpression = 'MIT'
            ProjectUrl = 'https://github.com/AJ-comp/Mythosia.AI'
            ReleaseNotesUrl = 'https://github.com/AJ-comp/Mythosia.AI/blob/main/src/vectordb/Mythosia.VectorDb.InMemory/RELEASE_NOTES.md#v430'
            Dependencies = @{ 'Mythosia.VectorDb.Abstractions' = 'Mythosia.VectorDb.Abstractions' }
            FixedDependencies = @{ 'Lucene.Net' = '4.8.0-beta00016'; 'Lucene.Net.Analysis.Common' = '4.8.0-beta00016' }
        }
        @{
            Id = 'Mythosia.AI.Rag'
            Version = '8.3.0'
            Project = 'src/rag/Mythosia.AI.Rag/Mythosia.AI.Rag.csproj'
            TargetFramework = 'netstandard2.1'
            LicenseExpression = 'MIT'
            ProjectUrl = 'https://aj-comp.github.io/Mythosia.AI/docs/rag.html'
            ReleaseNotesUrl = 'https://github.com/AJ-comp/Mythosia.AI/blob/main/src/rag/Mythosia.AI.Rag/RELEASE_NOTES.md#v830'
            Dependencies = @{ 'Mythosia.AI.Abstractions' = 'Mythosia.AI.Abstractions'; 'Mythosia.AI.Rag.Abstractions' = 'Mythosia.AI.Rag.Abstractions'; 'Mythosia.VectorDb.InMemory' = 'Mythosia.VectorDb.InMemory' }
            FixedDependencies = @{ 'Mythosia.Documents.Office' = '1.1.1'; 'Mythosia.Documents.Pdf' = '1.1.2'; 'SharpZipLib' = '1.4.2' }
        }
    )
    # Keep the complete compatibility matrix, resolving these unchanged packages from NuGet.
    ConsumerOnlyPackages = @(
        @{ Id = 'Mythosia.AI.Serving.Abstractions'; Version = '1.0.0' }
        @{ Id = 'Mythosia.AI.Serving.Ollama'; Version = '1.0.0' }
        @{ Id = 'Mythosia.AI.Serving.LlamaCpp'; Version = '1.0.0' }
        @{ Id = 'Mythosia.AI.Serving.Vllm'; Version = '1.1.0' }
        @{ Id = 'Mythosia.VectorDb.Postgres'; Version = '10.8.1' }
        @{ Id = 'Mythosia.Documents.Office'; Version = '1.1.1' }
        @{ Id = 'Mythosia.Documents.Pdf'; Version = '1.1.2' }
        @{ Id = 'Mythosia.Documents.Hwp'; Version = '1.0.2' }
        @{ Id = 'Mythosia.VectorDb.Qdrant'; Version = '4.2.0' }
        @{ Id = 'Mythosia.VectorDb.Pinecone'; Version = '4.0.2' }
        @{ Id = 'Mythosia.AI.Rag.Search.Pixie'; Version = '0.1.0-preview' }
        @{ Id = 'Mythosia.AI.Mcp'; Version = '0.1.1-preview' }
    )
}
