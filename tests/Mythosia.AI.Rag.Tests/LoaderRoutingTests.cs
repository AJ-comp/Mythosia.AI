using Mythosia.AI.Loaders;
using Mythosia.AI.Rag;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Mythosia.AI.Rag.Tests;

[TestClass]
public class LoaderRoutingTests
{
    [TestMethod]
    public async Task AddDocuments_PerExtensionRouting_UsesConfiguredLoaders()
    {
        using var tempDir = new TempDirectory();
        var pdfPath = tempDir.CreateFile("policy.pdf", "pdf content");
        var docxPath = tempDir.CreateFile("report.docx", "docx content");
        var txtPath = tempDir.CreateFile("notes.txt", "txt content");

        var pdfLoader = new TrackingLoader("pdf");
        var docxLoader = new TrackingLoader("docx");
        var txtLoader = new TrackingLoader("txt");
        var embedding = new TrackingEmbeddingProvider(8);
        var store = new TrackingVectorStore();

        await RagStore.BuildAsync(config => config
            .AddDocuments(tempDir.RootPath, src => src.WithExtension(".pdf").WithLoader(pdfLoader))
            .AddDocuments(tempDir.RootPath, src => src.WithExtension(".docx").WithLoader(docxLoader))
            .AddDocuments(tempDir.RootPath, src => src.WithExtension(".txt").WithLoader(txtLoader))
            .UseEmbedding(embedding)
            .UseStore(store)
            .WithChunkSize(64)
            .WithChunkOverlap(0)
        );

        var expectedContents = new[] { "pdf content", "docx content", "txt content" };
        CollectionAssert.AreEquivalent(new[] { pdfPath }, pdfLoader.Sources.ToArray());
        CollectionAssert.AreEquivalent(new[] { docxPath }, docxLoader.Sources.ToArray());
        CollectionAssert.AreEquivalent(new[] { txtPath }, txtLoader.Sources.ToArray());
        CollectionAssert.AreEquivalent(expectedContents, embedding.EmbeddedTexts.ToArray());
        CollectionAssert.AreEquivalent(expectedContents, store.Records.Select(r => r.Content).ToArray());
        Assert.AreEqual(expectedContents.Length, store.Records.Count);
    }

    [TestMethod]
    public async Task AddDocuments_WithExtensions_RoutesMultipleExtensionsToSingleLoader()
    {
        using var tempDir = new TempDirectory();
        var txtPath = tempDir.CreateFile("readme.txt", "abcdefghij");
        var mdPath = tempDir.CreateFile("guide.md", "klmnopqrst");
        var pdfPath = tempDir.CreateFile("manual.pdf", "ignored");

        var loader = new TrackingLoader("text");
        var embedding = new TrackingEmbeddingProvider(8);
        var store = new TrackingVectorStore();

        await RagStore.BuildAsync(config => config
            .AddDocuments(tempDir.RootPath, src => src
                .WithExtensions(".txt", ".md")
                .WithLoader(loader))
            .UseEmbedding(embedding)
            .UseStore(store)
            .WithChunkSize(4)
            .WithChunkOverlap(0)
        );

        var expectedChunks = new[] { "abcd", "efgh", "ij", "klmn", "opqr", "st" };
        CollectionAssert.AreEquivalent(new[] { txtPath, mdPath }, loader.Sources.ToArray());
        Assert.IsFalse(loader.Sources.Contains(pdfPath));
        Assert.AreEqual(2, loader.Sources.Count);
        CollectionAssert.AreEquivalent(expectedChunks, embedding.EmbeddedTexts.ToArray());
        CollectionAssert.AreEquivalent(expectedChunks, store.Records.Select(r => r.Content).ToArray());
        Assert.AreEqual(expectedChunks.Length, store.Records.Count);
    }

    private sealed class TrackingLoader : IDocumentLoader
    {
        private readonly List<string> _sources = new();
        private readonly string _contentPrefix;

        public TrackingLoader(string contentPrefix)
        {
            _contentPrefix = contentPrefix;
        }

        public IReadOnlyList<string> Sources => _sources;

        public Task<IReadOnlyList<RagDocument>> LoadAsync(string source, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_sources)
            {
                _sources.Add(source);
            }

            var doc = new RagDocument
            {
                Id = Path.GetFileName(source),
                Content = File.ReadAllText(source),
                Source = source,
                Metadata = { ["loader"] = _contentPrefix }
            };

            return Task.FromResult<IReadOnlyList<RagDocument>>(new[] { doc });
        }
    }

    private sealed class TrackingEmbeddingProvider : IEmbeddingProvider
    {
        private readonly List<string> _embeddedTexts = new();

        public int Dimensions { get; }
        public IReadOnlyList<string> EmbeddedTexts => _embeddedTexts;

        public TrackingEmbeddingProvider(int dimensions)
        {
            Dimensions = dimensions;
        }

        public Task<float[]> GetEmbeddingAsync(string text, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_embeddedTexts)
            {
                _embeddedTexts.Add(text);
            }

            return Task.FromResult(CreateVector(text));
        }

        public Task<IReadOnlyList<float[]>> GetEmbeddingsAsync(IEnumerable<string> texts, CancellationToken cancellationToken = default)
        {
            var list = texts.ToList();
            var vectors = new List<float[]>(list.Count);

            foreach (var text in list)
            {
                cancellationToken.ThrowIfCancellationRequested();
                vectors.Add(CreateVector(text));
            }

            lock (_embeddedTexts)
            {
                _embeddedTexts.AddRange(list);
            }

            return Task.FromResult<IReadOnlyList<float[]>>(vectors);
        }

        private float[] CreateVector(string text)
        {
            var vector = new float[Dimensions];
            var value = text.Length;

            for (int i = 0; i < vector.Length; i++)
                vector[i] = value;

            return vector;
        }
    }

    private sealed class TrackingVectorStore : IVectorStore
    {
        private readonly List<VectorRecord> _records = new();

        public IReadOnlyList<VectorRecord> Records => _records;

        public Task UpsertAsync(string collection, VectorRecord record, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_records)
            {
                _records.Add(record);
            }

            return Task.CompletedTask;
        }

        public Task UpsertBatchAsync(string collection, IEnumerable<VectorRecord> records, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_records)
            {
                _records.AddRange(records);
            }

            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<VectorSearchResult>> SearchAsync(
            string collection,
            float[] queryVector,
            int topK = 5,
            VectorFilter? filter = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<VectorSearchResult>>(Array.Empty<VectorSearchResult>());
        }

        public Task<VectorRecord?> GetAsync(string collection, string id, CancellationToken cancellationToken = default)
        {
            lock (_records)
            {
                return Task.FromResult(_records.FirstOrDefault(r => r.Id == id));
            }
        }

        public Task DeleteAsync(string collection, string id, CancellationToken cancellationToken = default)
        {
            lock (_records)
            {
                _records.RemoveAll(r => r.Id == id);
            }

            return Task.CompletedTask;
        }

        public Task DeleteByFilterAsync(string collection, VectorFilter filter, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task<bool> CollectionExistsAsync(string collection, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(true);
        }

        public Task CreateCollectionAsync(string collection, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task DeleteCollectionAsync(string collection, CancellationToken cancellationToken = default)
        {
            lock (_records)
            {
                _records.Clear();
            }

            return Task.CompletedTask;
        }
    }

    private sealed class TempDirectory : IDisposable
    {
        public string RootPath { get; }

        public TempDirectory()
        {
            RootPath = Path.Combine(Path.GetTempPath(), "Mythosia.AI.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(RootPath);
        }

        public string CreateFile(string fileName, string content)
        {
            var filePath = Path.Combine(RootPath, fileName);
            File.WriteAllText(filePath, content);
            return filePath;
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(RootPath))
                    Directory.Delete(RootPath, true);
            }
            catch
            {
            }
        }
    }
}
