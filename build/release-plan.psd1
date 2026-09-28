# Explicit publication allowlist. Consumer-only packages are never packed or published.
@{
    SchemaVersion = 1
    PreviousReleaseCommit = 'e288647cf4a18cf0614921135f041d9830653af1'
    BaselineNote = 'RAG 8.2.0 and RAG.Abstractions 6.4.0 publication was verified against official NuGet repository metadata at e288647. This release introduces the shared Serving contracts and Ollama/llama.cpp clients, and adds the compatible vLLM integration. All other packages resolve from their existing published versions.'
    Packages = @(
        @{
            Id = 'Mythosia.AI.Serving.Abstractions'
            Version = '1.0.0'
            Project = 'src/serving/Mythosia.AI.Serving.Abstractions/Mythosia.AI.Serving.Abstractions.csproj'
            TargetFramework = 'netstandard2.1'
            LicenseExpression = 'MIT'
            ProjectUrl = 'https://github.com/AJ-comp/Mythosia.AI/tree/main/src/serving/Mythosia.AI.Serving.Abstractions'
            ReleaseNotesUrl = 'https://github.com/AJ-comp/Mythosia.AI/blob/main/src/serving/Mythosia.AI.Serving.Abstractions/RELEASE_NOTES.md#v100'
            Dependencies = @{}
            FixedDependencies = @{}
        }
        @{
            Id = 'Mythosia.AI.Serving.Ollama'
            Version = '1.0.0'
            Project = 'src/serving/Mythosia.AI.Serving.Ollama/Mythosia.AI.Serving.Ollama.csproj'
            TargetFramework = 'netstandard2.1'
            LicenseExpression = 'MIT'
            ProjectUrl = 'https://github.com/AJ-comp/Mythosia.AI/tree/main/src/serving/Mythosia.AI.Serving.Ollama'
            ReleaseNotesUrl = 'https://github.com/AJ-comp/Mythosia.AI/blob/main/src/serving/Mythosia.AI.Serving.Ollama/RELEASE_NOTES.md#v100'
            Dependencies = @{ 'Mythosia.AI.Serving.Abstractions' = 'Mythosia.AI.Serving.Abstractions' }
            FixedDependencies = @{ 'Newtonsoft.Json' = '13.0.4' }
        }
        @{
            Id = 'Mythosia.AI.Serving.LlamaCpp'
            Version = '1.0.0'
            Project = 'src/serving/Mythosia.AI.Serving.LlamaCpp/Mythosia.AI.Serving.LlamaCpp.csproj'
            TargetFramework = 'netstandard2.1'
            LicenseExpression = 'MIT'
            ProjectUrl = 'https://github.com/AJ-comp/Mythosia.AI/tree/main/src/serving/Mythosia.AI.Serving.LlamaCpp'
            ReleaseNotesUrl = 'https://github.com/AJ-comp/Mythosia.AI/blob/main/src/serving/Mythosia.AI.Serving.LlamaCpp/RELEASE_NOTES.md#v100'
            Dependencies = @{ 'Mythosia.AI.Serving.Abstractions' = 'Mythosia.AI.Serving.Abstractions' }
            FixedDependencies = @{ 'Newtonsoft.Json' = '13.0.4' }
        }
        @{
            Id = 'Mythosia.AI.Serving.Vllm'
            Version = '1.1.0'
            Project = 'src/serving/Mythosia.AI.Serving.Vllm/Mythosia.AI.Serving.Vllm.csproj'
            TargetFramework = 'netstandard2.1'
            LicenseExpression = 'MIT'
            ProjectUrl = 'https://github.com/AJ-comp/Mythosia.AI/tree/main/src/serving/Mythosia.AI.Serving.Vllm'
            ReleaseNotesUrl = 'https://github.com/AJ-comp/Mythosia.AI/blob/main/src/serving/Mythosia.AI.Serving.Vllm/RELEASE_NOTES.md#v110'
            Dependencies = @{ 'Mythosia.AI.Serving.Abstractions' = 'Mythosia.AI.Serving.Abstractions' }
            FixedDependencies = @{ 'Newtonsoft.Json' = '13.0.4' }
        }
    )
    # Keep the complete compatibility matrix, resolving these unchanged packages from NuGet.
    ConsumerOnlyPackages = @(
        @{ Id = 'Mythosia.AI.Abstractions'; Version = '4.1.0' }
        @{ Id = 'Mythosia.AI'; Version = '8.1.0' }
        @{ Id = 'Mythosia.AI.Providers.Alibaba'; Version = '3.0.1' }
        @{ Id = 'Mythosia.AI.Rag.Abstractions'; Version = '6.4.0' }
        @{ Id = 'Mythosia.AI.Rag'; Version = '8.2.0' }
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
    )
}
