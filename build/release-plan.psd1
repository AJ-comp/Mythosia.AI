# Explicit publication allowlist. Keep versions and dependency edges coordinated here.
@{
    SchemaVersion = 1
    PreviousReleaseCommit = 'f12955458f921c31cc36e10fadc99bb0d2ded803'
    BaselineNote = 'Coverage compares production changes after f129554 with the current working tree. Hwp and Pinecone also include independently reviewed differences from their published NuGet packages that predate this Git baseline.'
    Packages = @(
        @{
            Id = 'Mythosia.AI.Abstractions'
            Version = '4.1.0'
            Project = 'src/core/Mythosia.AI.Abstractions/Mythosia.AI.Abstractions.csproj'
            TargetFramework = 'netstandard2.1'
            LicenseExpression = 'MIT'
            ProjectUrl = 'https://github.com/AJ-comp/Mythosia.AI/tree/main/src/core/Mythosia.AI.Abstractions'
            ReleaseNotesUrl = 'https://github.com/AJ-comp/Mythosia.AI/blob/main/src/core/Mythosia.AI.Abstractions/RELEASE_NOTES.md#v410'
            Dependencies = @{}
            FixedDependencies = @{ 'Mythosia' = '1.4.0' }
        }
        @{
            Id = 'Mythosia.AI'
            Version = '8.1.0'
            Project = 'src/core/Mythosia.AI/Mythosia.AI.csproj'
            TargetFramework = 'netstandard2.1'
            LicenseExpression = 'MIT'
            ProjectUrl = 'https://github.com/AJ-comp/Mythosia.AI'
            ReleaseNotesUrl = 'https://github.com/AJ-comp/Mythosia.AI/blob/main/src/core/Mythosia.AI/RELEASE_NOTES.md#v810'
            Dependencies = @{ 'Mythosia.AI.Abstractions' = 'Mythosia.AI.Abstractions' }
            FixedDependencies = @{ 'Azure.AI.OpenAI' = '2.1.0'; 'Newtonsoft.Json' = '13.0.4'; 'NJsonSchema' = '11.6.1'; 'System.Threading.Channels' = '10.0.10'; 'TiktokenSharp' = '1.2.1' }
        }
        @{
            Id = 'Mythosia.AI.Providers.Alibaba'
            Version = '3.0.1'
            Project = 'src/core/Mythosia.AI.Providers.Alibaba/Mythosia.AI.Providers.Alibaba.csproj'
            TargetFramework = 'netstandard2.1'
            LicenseExpression = 'MIT'
            ProjectUrl = 'https://github.com/AJ-comp/Mythosia.AI/tree/main/src/core/Mythosia.AI.Providers.Alibaba'
            ReleaseNotesUrl = 'https://github.com/AJ-comp/Mythosia.AI/blob/main/src/core/Mythosia.AI.Providers.Alibaba/RELEASE_NOTES.md#v301'
            Dependencies = @{ 'Mythosia.AI' = 'Mythosia.AI' }
            FixedDependencies = @{ 'TiktokenSharp' = '1.2.1' }
        }
        @{
            Id = 'Mythosia.VectorDb.Abstractions'
            Version = '4.1.0'
            Project = 'src/vectordb/Mythosia.VectorDb.Abstractions/Mythosia.VectorDb.Abstractions.csproj'
            TargetFramework = 'netstandard2.1'
            LicenseExpression = 'MIT'
            ProjectUrl = 'https://github.com/AJ-comp/Mythosia.AI'
            ReleaseNotesUrl = 'https://github.com/AJ-comp/Mythosia.AI/blob/main/src/vectordb/Mythosia.VectorDb.Abstractions/RELEASE_NOTES.md#v410'
            Dependencies = @{}
            FixedDependencies = @{ 'Lucene.Net' = '4.8.0-beta00016'; 'Lucene.Net.Analysis.Common' = '4.8.0-beta00016' }
        }
        @{
            Id = 'Mythosia.AI.Rag.Abstractions'
            Version = '6.3.0'
            Project = 'src/rag/Mythosia.AI.Rag.Abstractions/Mythosia.AI.Rag.Abstractions.csproj'
            TargetFramework = 'netstandard2.1'
            LicenseExpression = 'MIT'
            ProjectUrl = 'https://github.com/AJ-comp/Mythosia.AI/tree/main/src/rag/Mythosia.AI.Rag.Abstractions'
            ReleaseNotesUrl = 'https://github.com/AJ-comp/Mythosia.AI/blob/main/src/rag/Mythosia.AI.Rag.Abstractions/RELEASE_NOTES.md#v630'
            Dependencies = @{ 'Mythosia.VectorDb.Abstractions' = 'Mythosia.VectorDb.Abstractions' }
            FixedDependencies = @{}
        }
        @{
            Id = 'Mythosia.VectorDb.InMemory'
            Version = '4.2.0'
            Project = 'src/vectordb/Mythosia.VectorDb.InMemory/Mythosia.VectorDb.InMemory.csproj'
            TargetFramework = 'netstandard2.1'
            LicenseExpression = 'MIT'
            ProjectUrl = 'https://github.com/AJ-comp/Mythosia.AI'
            ReleaseNotesUrl = 'https://github.com/AJ-comp/Mythosia.AI/blob/main/src/vectordb/Mythosia.VectorDb.InMemory/RELEASE_NOTES.md#v420'
            Dependencies = @{ 'Mythosia.VectorDb.Abstractions' = 'Mythosia.VectorDb.Abstractions'; 'Mythosia.AI.Rag.Abstractions' = 'Mythosia.AI.Rag.Abstractions' }
            FixedDependencies = @{ 'Lucene.Net' = '4.8.0-beta00016'; 'Lucene.Net.Analysis.Common' = '4.8.0-beta00016' }
        }
        @{
            Id = 'Mythosia.Documents.Office'
            Version = '1.1.1'
            Project = 'src/loaders/Mythosia.Documents.Office/Mythosia.Documents.Office.csproj'
            TargetFramework = 'netstandard2.1'
            LicenseExpression = 'MIT'
            ProjectUrl = 'https://github.com/AJ-comp/Mythosia.AI'
            ReleaseNotesUrl = 'https://github.com/AJ-comp/Mythosia.AI/blob/main/src/loaders/Mythosia.Documents.Office/RELEASE_NOTES.md#v111'
            Dependencies = @{}
            FixedDependencies = @{ 'Mythosia.Documents.Abstractions' = '1.2.0'; 'DocumentFormat.OpenXml' = '3.5.1' }
        }
        @{
            Id = 'Mythosia.Documents.Pdf'
            Version = '1.1.2'
            Project = 'src/loaders/Mythosia.Documents.Pdf/Mythosia.Documents.Pdf.csproj'
            TargetFramework = 'netstandard2.1'
            LicenseExpression = 'MIT'
            ProjectUrl = 'https://github.com/AJ-comp/Mythosia.AI'
            ReleaseNotesUrl = 'https://github.com/AJ-comp/Mythosia.AI/blob/main/src/loaders/Mythosia.Documents.Pdf/RELEASE_NOTES.md#v112'
            Dependencies = @{}
            FixedDependencies = @{ 'Mythosia.Documents.Abstractions' = '1.2.0'; 'PdfPig' = '0.1.14' }
        }
        @{
            Id = 'Mythosia.Documents.Hwp'
            Version = '1.0.2'
            Project = 'src/loaders/Mythosia.Documents.Hwp/Mythosia.Documents.Hwp.csproj'
            TargetFramework = 'netstandard2.1'
            LicenseExpression = 'MIT'
            ProjectUrl = 'https://github.com/AJ-comp/Mythosia.AI'
            ReleaseNotesUrl = 'https://github.com/AJ-comp/Mythosia.AI/blob/main/src/loaders/Mythosia.Documents.Hwp/RELEASE_NOTES.md#v102'
            Dependencies = @{}
            FixedDependencies = @{ 'Mythosia.Documents.Abstractions' = '1.2.0'; 'HwpLibSharp' = '1.1.10.5'; 'OpenMcdf' = '3.1.4' }
        }
        @{
            Id = 'Mythosia.AI.Rag'
            Version = '8.1.0'
            Project = 'src/rag/Mythosia.AI.Rag/Mythosia.AI.Rag.csproj'
            TargetFramework = 'netstandard2.1'
            LicenseExpression = 'MIT'
            ProjectUrl = 'https://aj-comp.github.io/Mythosia.AI/docs/rag.html'
            ReleaseNotesUrl = 'https://github.com/AJ-comp/Mythosia.AI/blob/main/src/rag/Mythosia.AI.Rag/RELEASE_NOTES.md#v810'
            Dependencies = @{ 'Mythosia.AI.Abstractions' = 'Mythosia.AI.Abstractions'; 'Mythosia.AI.Rag.Abstractions' = 'Mythosia.AI.Rag.Abstractions'; 'Mythosia.VectorDb.InMemory' = 'Mythosia.VectorDb.InMemory'; 'Mythosia.Documents.Office' = 'Mythosia.Documents.Office'; 'Mythosia.Documents.Pdf' = 'Mythosia.Documents.Pdf' }
            FixedDependencies = @{ 'SharpZipLib' = '1.4.2' }
        }
        @{
            Id = 'Mythosia.VectorDb.Postgres'
            Version = '10.8.0'
            Project = 'src/vectordb/Mythosia.VectorDb.Postgres/Mythosia.VectorDb.Postgres.csproj'
            TargetFramework = 'net10.0'
            LicenseExpression = 'MIT'
            ProjectUrl = 'https://github.com/AJ-comp/Mythosia.AI'
            ReleaseNotesUrl = 'https://github.com/AJ-comp/Mythosia.AI/blob/main/src/vectordb/Mythosia.VectorDb.Postgres/RELEASE_NOTES.md#v1080'
            Dependencies = @{ 'Mythosia.VectorDb.Abstractions' = 'Mythosia.VectorDb.Abstractions' }
            FixedDependencies = @{ 'Npgsql' = '10.0.2' }
        }
        @{
            Id = 'Mythosia.VectorDb.Qdrant'
            Version = '4.2.0'
            Project = 'src/vectordb/Mythosia.VectorDb.Qdrant/Mythosia.VectorDb.Qdrant.csproj'
            TargetFramework = 'netstandard2.1'
            LicenseExpression = 'MIT'
            ProjectUrl = 'https://github.com/AJ-comp/Mythosia.AI'
            ReleaseNotesUrl = 'https://github.com/AJ-comp/Mythosia.AI/blob/main/src/vectordb/Mythosia.VectorDb.Qdrant/RELEASE_NOTES.md#v420'
            Dependencies = @{ 'Mythosia.VectorDb.Abstractions' = 'Mythosia.VectorDb.Abstractions' }
            FixedDependencies = @{ 'Qdrant.Client' = '1.17.0'; 'System.IO.Hashing' = '10.0.7' }
        }
        @{
            Id = 'Mythosia.VectorDb.Pinecone'
            Version = '4.0.2'
            Project = 'src/vectordb/Mythosia.VectorDb.Pinecone/Mythosia.VectorDb.Pinecone.csproj'
            TargetFramework = 'netstandard2.1'
            LicenseExpression = 'MIT'
            ProjectUrl = 'https://github.com/AJ-comp/Mythosia.AI'
            ReleaseNotesUrl = 'https://github.com/AJ-comp/Mythosia.AI/blob/main/src/vectordb/Mythosia.VectorDb.Pinecone/RELEASE_NOTES.md#v402'
            Dependencies = @{ 'Mythosia.VectorDb.Abstractions' = 'Mythosia.VectorDb.Abstractions' }
            FixedDependencies = @{ 'System.IO.Hashing' = '10.0.7'; 'System.Text.Json' = '10.0.7' }
        }
        @{
            Id = 'Mythosia.AI.Rag.Search.Pixie'
            Version = '0.1.0-preview'
            Project = 'src/rag/Mythosia.AI.Rag.Search.Pixie/Mythosia.AI.Rag.Search.Pixie.csproj'
            TargetFramework = 'net8.0'
            LicenseExpression = 'MIT AND Apache-2.0'
            ProjectUrl = 'https://github.com/AJ-comp/Mythosia.AI/tree/main/src/rag/Mythosia.AI.Rag.Search.Pixie'
            ReleaseNotesUrl = 'https://github.com/AJ-comp/Mythosia.AI/blob/main/src/rag/Mythosia.AI.Rag.Search.Pixie/RELEASE_NOTES.md#v010-preview'
            Dependencies = @{ 'Mythosia.VectorDb.Abstractions' = 'Mythosia.VectorDb.Abstractions' }
            FixedDependencies = @{ 'Microsoft.ML.OnnxRuntime' = '1.24.4' }
            MaxPackageBytes = 250000000
            RequiredEntries = @('NOTICE.txt', 'LICENSE', 'contentFiles/any/any/models/pixie/model.int8.onnx', 'contentFiles/any/any/models/pixie/tokenizer.json', 'contentFiles/any/any/models/pixie/manifest.json', 'contentFiles/any/any/models/pixie/LICENSE', 'buildTransitive/Mythosia.AI.Rag.Search.Pixie.targets')
            ForbiddenEntryPatterns = @('(^|/)model\.onnx$', '\.safetensors$')
            EntrySha256 = @{
                'contentFiles/any/any/models/pixie/model.int8.onnx' = 'dcf25f9fa452e61a48f5330a63a2aa68988ddac5bfc574a59c338ef1cff06e42'
                'contentFiles/any/any/models/pixie/tokenizer.json' = '36bdc1f1fe0135d10667322a493fd6a32eb93e8f4f68d09004eeb92f39fc8f25'
                'contentFiles/any/any/models/pixie/LICENSE' = 'c700489bf364cc3d1a060b0304469695ccafe181a2f2eb7858689f4ab2388e7d'
            }
        }
        @{
            Id = 'Mythosia.AI.Mcp'
            Version = '0.1.1-preview'
            Project = 'src/integrations/Mythosia.AI.Mcp/Mythosia.AI.Mcp.csproj'
            TargetFramework = 'netstandard2.1'
            LicenseExpression = 'MIT'
            ProjectUrl = 'https://github.com/AJ-comp/Mythosia.AI/tree/main/src/integrations/Mythosia.AI.Mcp'
            ReleaseNotesUrl = 'https://github.com/AJ-comp/Mythosia.AI/blob/main/src/integrations/Mythosia.AI.Mcp/RELEASE_NOTES.md#v011-preview'
            Dependencies = @{ 'Mythosia.AI' = 'Mythosia.AI' }
            FixedDependencies = @{}
        }
    )
    ConsumerOnlyPackages = @(
        @{ Id = 'Mythosia.AI.Serving.Vllm'; Version = '1.0.0' }
    )
}
