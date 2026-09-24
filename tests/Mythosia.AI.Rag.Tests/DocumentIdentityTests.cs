using Mythosia.AI.Rag.Embeddings;
using Mythosia.AI.Rag.Loaders;
using Mythosia.AI.Rag.Splitters;
using Mythosia.VectorDb;
using Mythosia.VectorDb.InMemory;

namespace Mythosia.AI.Rag.Tests;

[TestClass]
public sealed class DocumentIdentityTests
{
    [TestMethod]
    [DataRow("faq.txt")]
    [DataRow("policies/faq.txt")]
    public async Task AddDocuments_DifferentRootsWithSameRelativePath_PreservesBothDocuments(string relativePath)
    {
        using var temporary = new TemporaryDocuments();
        temporary.WriteFile(Path.Combine("company-a", relativePath), "Company A policy.");
        temporary.WriteFile(Path.Combine("company-b", relativePath), "Company B policy.");
        using var vectors = new InMemoryVectorStore();

        await BuildAsync(vectors, builder => builder
            .AddDocuments(temporary.PathFor("company-a"))
            .AddDocuments(temporary.PathFor("company-b")));

        var records = await vectors.ListAllRecordsAsync();
        Assert.AreEqual(2, records.Count, "The second root must not replace the first root's document.");
        CollectionAssert.AreEquivalent(new[] { "Company A policy.", "Company B policy." },
            records.Select(record => record.Content).ToArray());
        Assert.AreEqual(2, records.Select(DocumentId).Distinct(StringComparer.Ordinal).Count());
        Assert.AreEqual(2, records.Select(record => record.Id).Distinct(StringComparer.Ordinal).Count());
    }

    [TestMethod]
    public async Task AddDocuments_ReindexingSameFile_PreservesIdAndRemovesOldTrailingChunks()
    {
        using var temporary = new TemporaryDocuments();
        temporary.WriteFile("faq.txt", "abcdefghijkl");
        using var vectors = new InMemoryVectorStore();
        await BuildAsync(vectors, builder => builder.AddDocuments(temporary.Root), chunkSize: 4);
        var before = await vectors.ListAllRecordsAsync();
        Assert.AreEqual(3, before.Count);
        var documentId = before.Select(DocumentId).Distinct(StringComparer.Ordinal).Single();

        temporary.WriteFile("faq.txt", "NEWFAQ");
        await BuildAsync(vectors, builder => builder.AddDocuments(temporary.Root), chunkSize: 4);

        var after = await vectors.ListAllRecordsAsync();
        Assert.AreEqual(2, after.Count, "Reindexing must remove chunks left over from the longer version.");
        CollectionAssert.AreEquivalent(new[] { "NEWF", "AQ" }, after.Select(record => record.Content).ToArray());
        Assert.IsTrue(after.All(record => DocumentId(record) == documentId), "Content changes must not change file identity.");
    }

    [TestMethod]
    public async Task AddDocuments_ReindexingOneRoot_DoesNotDeleteOtherRootsDocument()
    {
        using var temporary = new TemporaryDocuments();
        temporary.WriteFile("company-a/faq.txt", "Company A original.");
        temporary.WriteFile("company-b/faq.txt", "Company B original.");
        using var vectors = new InMemoryVectorStore();
        await BuildAsync(vectors, builder => builder
            .AddDocuments(temporary.PathFor("company-a"))
            .AddDocuments(temporary.PathFor("company-b")));
        var original = await vectors.ListAllRecordsAsync();
        Assert.AreEqual(2, original.Count);
        var companyAId = DocumentId(original.Single(record => record.Content == "Company A original."));
        var companyB = original.Single(record => record.Content == "Company B original.");

        temporary.WriteFile("company-a/faq.txt", "Company A revised.");
        await BuildAsync(vectors, builder => builder.AddDocuments(temporary.PathFor("company-a")));

        var current = await vectors.ListAllRecordsAsync();
        Assert.AreEqual(2, current.Count);
        Assert.AreEqual(companyAId, DocumentId(current.Single(record => record.Content == "Company A revised.")));
        var retained = current.Single(record => record.Id == companyB.Id);
        Assert.AreEqual("Company B original.", retained.Content);
        Assert.AreEqual(DocumentId(companyB), DocumentId(retained));
    }

    [TestMethod]
    [DataRow("single")]
    [DataRow("directory")]
    [DataRow("routed-directory")]
    public async Task FileIdentity_RelativeAbsoluteAndDotSegmentPaths_AreEquivalent(string registration)
    {
        using var temporary = new TemporaryDocuments();
        var file = temporary.WriteFile("documents/faq.txt", "Initial policy.");
        var directory = temporary.PathFor("documents");
        var absoluteSource = registration == "single" ? file : directory;
        var relativeSource = Path.GetRelativePath(Environment.CurrentDirectory, absoluteSource);
        var dotSegmentSource = registration == "single"
            ? Path.Combine(directory, ".", "faq.txt")
            : Path.Combine(directory, ".");
        using var vectors = new InMemoryVectorStore();
        await BuildAsync(vectors, builder => Register(builder, registration, absoluteSource));
        var initial = (await vectors.ListAllRecordsAsync()).Single();

        temporary.WriteFile("documents/faq.txt", "Updated via relative path.");
        await BuildAsync(vectors, builder => Register(builder, registration, relativeSource));
        var relative = await vectors.ListAllRecordsAsync();
        Assert.AreEqual(1, relative.Count, "Relative and absolute references must update the same document.");
        Assert.AreEqual(DocumentId(initial), DocumentId(relative[0]));
        Assert.AreEqual("Updated via relative path.", relative[0].Content);

        temporary.WriteFile("documents/faq.txt", "Updated via dot segment.");
        await BuildAsync(vectors, builder => Register(builder, registration, dotSegmentSource));
        var dotted = await vectors.ListAllRecordsAsync();
        Assert.AreEqual(1, dotted.Count, "A dot segment must not create another document identity.");
        Assert.AreEqual(DocumentId(initial), DocumentId(dotted[0]));
        Assert.AreEqual("Updated via dot segment.", dotted[0].Content);
    }

    [TestMethod]
    [DataRow("directory")]
    [DataRow("routed-directory")]
    [DataRow("custom-directory-loader")]
    public async Task FileIdentity_SingleFileAndDirectoryRegistration_UseSameIdentity(string registration)
    {
        using var temporary = new TemporaryDocuments();
        var file = temporary.WriteFile("faq.txt", "Single file version.");
        using var vectors = new InMemoryVectorStore();
        await BuildAsync(vectors, builder => builder.AddDocument(file));
        var initial = (await vectors.ListAllRecordsAsync()).Single();

        temporary.WriteFile("faq.txt", "Directory version.");
        await BuildAsync(vectors, builder => Register(builder, registration, temporary.Root));

        var current = await vectors.ListAllRecordsAsync();
        Assert.AreEqual(1, current.Count, "Changing registration overload must replace the same file, not create another document.");
        Assert.AreEqual(DocumentId(initial), DocumentId(current[0]));
        Assert.AreEqual("Directory version.", current[0].Content);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task AddDocument_OverlappingDefaultDirectory_EmbedsOnceRegardlessOfRegistrationOrder(bool singleFileFirst)
    {
        using var temporary = new TemporaryDocuments();
        var file = temporary.WriteFile("faq.txt", "abcdefghij");
        using var vectors = new InMemoryVectorStore();
        var embedding = new CountingEmbeddingProvider();

        await BuildAsync(vectors, builder => singleFileFirst
            ? builder.AddDocument(file).AddDocuments(temporary.Root)
            : builder.AddDocuments(temporary.Root).AddDocument(file), embedding, chunkSize: 5);

        CollectionAssert.AreEquivalent(new[] { "abcde", "fghij" }, embedding.Texts.ToArray(),
            "A file included individually and by its directory must be embedded only once.");
        Assert.AreEqual(2, (await vectors.ListAllRecordsAsync()).Count);
    }

    [TestMethod]
    public async Task AddDocument_CaseVariantPaths_EmbedsEachActualFileOnce()
    {
        using var temporary = new TemporaryDocuments();
        var upperPath = temporary.WriteFile("FAQ.txt", "Uppercase file content.");
        var lowerPath = temporary.WriteFile("faq.txt", "Lowercase file content.");
        // The filesystem decides whether these names identify one file or two.
        // This checks deduplication within one build, not ID migration between builds.
        var actualFiles = Directory.GetFiles(temporary.Root);
        var expectedContents = actualFiles.Select(File.ReadAllText).ToArray();
        using var vectors = new InMemoryVectorStore();
        var embedding = new CountingEmbeddingProvider();

        await BuildAsync(vectors, builder => builder
            .AddDocument(upperPath)
            .AddDocument(lowerPath), embedding);

        var records = await vectors.ListAllRecordsAsync();
        Assert.AreEqual(actualFiles.Length, records.Count,
            "Distinct files must survive; alternate names of one file must not be indexed twice.");
        CollectionAssert.AreEquivalent(expectedContents, records.Select(record => record.Content).ToArray());
        Assert.AreEqual(actualFiles.Length, embedding.Texts.Count,
            "Each actual file must be embedded exactly once during the build.");
        CollectionAssert.AreEquivalent(expectedContents, embedding.Texts.ToArray());
    }

    [TestMethod]
    public async Task CustomSingleFileSplitter_OverlappingDefaultDirectory_PreservesSingleFilePriority()
    {
        using var temporary = new TemporaryDocuments();
        var file = temporary.WriteFile("faq.txt", "abcdefghij");
        using var vectors = new InMemoryVectorStore();
        var embedding = new CountingEmbeddingProvider();

        await BuildAsync(vectors, builder => builder
            .AddDocuments(temporary.Root)
            .AddDocuments(new PlainTextDocumentLoader(), Path.Combine(temporary.Root, ".", "faq.txt"),
                new CharacterTextSplitter(5, 0, null)), embedding, chunkSize: 3);

        CollectionAssert.AreEquivalent(new[] { "abcde", "fghij" }, embedding.Texts.ToArray(),
            "The directory must not re-embed the individual file with the global three-character splitter.");
        var records = await vectors.ListAllRecordsAsync();
        CollectionAssert.AreEquivalent(new[] { "abcde", "fghij" }, records.Select(record => record.Content).ToArray());
        Assert.AreEqual(1, records.Select(DocumentId).Distinct(StringComparer.Ordinal).Count());
    }

    [TestMethod]
    [DataRow("directory")]
    public async Task AddDocuments_NestedFile_PreservesDisplayMetadataRelativeToRegisteredRoot(string registration)
    {
        using var temporary = new TemporaryDocuments();
        temporary.WriteFile("manual/policies/faq.txt", "Policy content.");
        using var vectors = new InMemoryVectorStore();

        await BuildAsync(vectors, builder => Register(builder, registration, temporary.PathFor("manual")));

        var record = (await vectors.ListAllRecordsAsync()).Single();
        Assert.AreEqual("faq.txt", record.Metadata["filename"]);
        Assert.AreEqual(".txt", record.Metadata["extension"]);
        Assert.AreEqual(Path.Combine("policies", "faq.txt"), record.Metadata["relative_path"],
            "Display metadata must be relative to the registered directory, independent of storage identity.");
    }

    [TestMethod]
    public async Task IndexDocumentAsync_ExplicitIdsWithSameFileSource_ArePreserved()
    {
        using var temporary = new TemporaryDocuments();
        var source = temporary.WriteFile("faq.txt", "File content.");
        using var vectors = new InMemoryVectorStore();
        var pipeline = new RagPipeline(new LocalEmbeddingProvider(32), vectors,
            new CharacterTextSplitter(1000, 0, null), new DefaultContextBuilder());

        await pipeline.IndexDocumentAsync(new RagDocument
        {
            Id = "customer-policy-a", Source = source, Content = "Explicit document A."
        });
        await pipeline.IndexDocumentAsync(new RagDocument
        {
            Id = "customer-policy-b", Source = source, Content = "Explicit document B."
        });
        await pipeline.IndexDocumentAsync(new RagDocument
        {
            Id = "customer-policy-a", Source = source, Content = "Revised explicit document A."
        });

        var records = await vectors.ListAllRecordsAsync();
        Assert.AreEqual(2, records.Count, "An explicit caller ID takes precedence over a shared file source.");
        CollectionAssert.AreEquivalent(new[] { "customer-policy-a", "customer-policy-b" },
            records.Select(DocumentId).ToArray());
        Assert.AreEqual("Revised explicit document A.", records.Single(record => DocumentId(record) == "customer-policy-a").Content);
        Assert.AreEqual("Explicit document B.", records.Single(record => DocumentId(record) == "customer-policy-b").Content);
    }

    [TestMethod]
    public async Task AddText_ExplicitId_IsPreservedAndUsedForReplacement()
    {
        using var vectors = new InMemoryVectorStore();
        await BuildAsync(vectors, builder => builder.AddText("Original inline policy.", id: "customer-inline-id"));
        await BuildAsync(vectors, builder => builder.AddText("Revised inline policy.", id: "customer-inline-id"));

        var records = await vectors.ListAllRecordsAsync();
        Assert.AreEqual(1, records.Count);
        Assert.AreEqual("customer-inline-id", DocumentId(records[0]));
        Assert.AreEqual("Revised inline policy.", records[0].Content);
    }

    private static string DocumentId(VectorRecord record) => record.Metadata["document_id"];

    private static RagBuilder Register(RagBuilder builder, string registration, string source) => registration switch
    {
        "single" => builder.AddDocument(source),
        "directory" => builder.AddDocuments(source),
        "routed-directory" => builder.AddDocuments(source, documents => documents.WithExtension(".txt")),
        "custom-directory-loader" => builder.AddDocuments(new DirectoryDocumentLoader(), source),
        _ => throw new ArgumentOutOfRangeException(nameof(registration), registration, "Unknown registration route.")
    };

    private static Task<RagStore> BuildAsync(InMemoryVectorStore vectors, Func<RagBuilder, RagBuilder> configure,
        IEmbeddingProvider? embedding = null, int chunkSize = 1000) =>
        RagStore.BuildAsync(builder => configure(builder)
            .UseStore(vectors)
            .UseEmbedding(embedding ?? new LocalEmbeddingProvider(32))
            .WithChunkSize(chunkSize)
            .WithChunkOverlap(0));

    private sealed class CountingEmbeddingProvider : IEmbeddingProvider
    {
        private readonly LocalEmbeddingProvider _inner = new(32);
        public int Dimensions => _inner.Dimensions;
        public List<string> Texts { get; } = new();

        public Task<float[]> GetEmbeddingAsync(string text, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Texts.Add(text);
            return _inner.GetEmbeddingAsync(text, cancellationToken);
        }

        public Task<IReadOnlyList<float[]>> GetEmbeddingsAsync(IEnumerable<string> texts, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var materialized = texts.ToList();
            Texts.AddRange(materialized);
            return _inner.GetEmbeddingsAsync(materialized, cancellationToken);
        }
    }

    private sealed class TemporaryDocuments : IDisposable
    {
        private readonly string _temporaryParent = Path.GetFullPath(Path.GetTempPath())
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        private readonly string _directoryName = "mythosia-document-identity-" + Guid.NewGuid().ToString("N");
        public string Root { get; }

        public TemporaryDocuments()
        {
            Root = Path.GetFullPath(Path.Combine(_temporaryParent, _directoryName));
            Directory.CreateDirectory(Root);
        }

        public string PathFor(string relativePath) => Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar));

        public string WriteFile(string relativePath, string content)
        {
            var path = PathFor(relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
            return path;
        }

        public void Dispose()
        {
            var target = Path.GetFullPath(Root);
            if (!target.StartsWith(_temporaryParent, StringComparison.Ordinal)
                || !string.Equals(Path.GetFileName(target), _directoryName, StringComparison.Ordinal))
                throw new InvalidOperationException("Document identity test cleanup escaped its own temporary directory.");
            if (Directory.Exists(target))
                Directory.Delete(target, recursive: true);
        }
    }
}
