using Mythosia.AI.Rag.Embeddings;
using Mythosia.AI.Rag.Loaders;
using Mythosia.VectorDb;
using Mythosia.VectorDb.InMemory;

namespace Mythosia.AI.Rag.Tests;

[TestClass]
public sealed class DocumentPersistenceCallbackTests
{
    [TestMethod]
    public async Task DocumentIdentityCallback_ReplacesLongWithShortAndPreservesOtherDocuments()
    {
        using var vectors = new InMemoryVectorStore();
        IVectorStore vectorStore = vectors;
        IEmbeddingProvider embeddingProvider = new LocalEmbeddingProvider(8);
        var cancellationToken = CancellationToken.None;
        var loader = new PlainTextDocumentLoader();
        string directory = Path.Combine(Path.GetTempPath(), "mythosia-callback-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string filePath = Path.Combine(directory, "policy.txt");
        try
        {
            await RagStore.BuildAsync(config => config.AddText("KEEP", "other").UseStore(vectorStore).UseEmbedding(embeddingProvider));
            int callbacks = 0;
            async Task Build()
            {
                await RagStore.BuildAsync(config => config
                    .AddDocuments(loader, filePath)
                    .WithChunkSize(4).WithChunkOverlap(0)
                    .UseEmbedding(embeddingProvider)
                    .UseStore(vectorStore),
                    onDocumentEmbedded: records =>
                    {
                        callbacks++;
                        return vectorStore.ReplaceByFilterAsync(
                            new VectorFilter().Where("document_id", records[0].Metadata["document_id"]),
                            records, cancellationToken);
                    },
                    cancellationToken: cancellationToken);
            }
            await File.WriteAllTextAsync(filePath, "AAAABBBBCCCC");
            await Build();
            Assert.AreEqual(4, (await vectors.ListAllRecordsAsync()).Count);

            await File.WriteAllTextAsync(filePath, "NEW!");
            await Build();

            var records = await vectors.ListAllRecordsAsync();
            Assert.AreEqual(2, callbacks);
            CollectionAssert.AreEquivalent(new[] { "NEW!", "KEEP" }, records.Select(r => r.Content).ToArray());
            Assert.AreEqual(Path.GetFullPath(filePath), records.Single(r => r.Content == "NEW!").Metadata["document_id"]);

            // The nonempty callback has no document ID to receive for an empty update.
            await File.WriteAllTextAsync(filePath, "");
            await Build();
            Assert.AreEqual(2, callbacks);
            Assert.AreEqual(2, (await vectors.ListAllRecordsAsync()).Count);
            await vectorStore.DeleteByFilterAsync(new VectorFilter().Where("document_id", Path.GetFullPath(filePath)), cancellationToken);
            CollectionAssert.AreEqual(new[] { "KEEP" }, (await vectors.ListAllRecordsAsync()).Select(r => r.Content).ToArray());
        }
        finally
        {
            // Delete only the single file and empty directory created by this test.
            File.Delete(filePath);
            Directory.Delete(directory);
        }
    }
}
