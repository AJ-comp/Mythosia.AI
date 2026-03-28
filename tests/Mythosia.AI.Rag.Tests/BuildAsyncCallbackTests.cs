using Mythosia.AI.Rag;
using Mythosia.AI.Rag.Embeddings;
using Mythosia.VectorDb;
using Mythosia.VectorDb.InMemory;

namespace Mythosia.AI.Rag.Tests;

[TestClass]
public class BuildAsyncCallbackTests
{
    /// <summary>
    /// Default behavior (no callback): records are saved to the store automatically.
    /// </summary>
    [TestMethod]
    public async Task BuildAsync_NoCallback_SavesRecordsToStore()
    {
        var vectorStore = new InMemoryVectorStore();

        var store = await RagStore.BuildAsync(config => config
            .AddText("환불은 14일 이내 가능합니다.", id: "refund")
            .UseLocalEmbedding(256)
            .UseStore(vectorStore)
        );

        // Records should be in the store
        var results = await vectorStore.SearchAsync(
            await new LocalEmbeddingProvider(256).GetEmbeddingAsync("환불"),
            topK: 5);

        Assert.IsTrue(results.Count > 0, "Default behavior should save records to store");
    }

    /// <summary>
    /// When callback is provided, default UpsertBatchAsync is skipped.
    /// The callback receives the records instead.
    /// </summary>
    [TestMethod]
    public async Task BuildAsync_WithCallback_SkipsDefaultUpsert()
    {
        var vectorStore = new InMemoryVectorStore();
        var callbackRecords = new List<VectorRecord>();

        var store = await RagStore.BuildAsync(config => config
            .AddText("환불은 14일 이내 가능합니다.", id: "refund")
            .UseLocalEmbedding(256)
            .UseStore(vectorStore),
            onDocumentEmbedded: records =>
            {
                callbackRecords.AddRange(records);
                return Task.CompletedTask; // Don't save — just collect
            }
        );

        // Callback should have received records
        Assert.IsTrue(callbackRecords.Count > 0, "Callback should receive records");

        // Store should be empty (default upsert was skipped)
        var results = await vectorStore.SearchAsync(
            await new LocalEmbeddingProvider(256).GetEmbeddingAsync("환불"),
            topK: 5);
        Assert.AreEqual(0, results.Count, "Store should be empty when callback replaces default upsert");
    }

    /// <summary>
    /// Callback can save records to a different store or with custom logic.
    /// </summary>
    [TestMethod]
    public async Task BuildAsync_WithCallback_CanSaveToStore()
    {
        var vectorStore = new InMemoryVectorStore();
        var callbackCount = 0;

        var store = await RagStore.BuildAsync(config => config
            .AddText("환불은 14일 이내 가능합니다.", id: "refund")
            .AddText("배송은 2-3일 소요됩니다.", id: "shipping")
            .UseLocalEmbedding(256)
            .UseStore(vectorStore),
            onDocumentEmbedded: async records =>
            {
                callbackCount++;
                await vectorStore.UpsertBatchAsync(records);
            }
        );

        // Callback should have been invoked for each document
        Assert.AreEqual(2, callbackCount, "Callback should be invoked once per document");

        // Records should be in the store (saved by callback)
        var results = await vectorStore.SearchAsync(
            await new LocalEmbeddingProvider(256).GetEmbeddingAsync("환불"),
            topK: 5);
        Assert.IsTrue(results.Count > 0, "Records should be in store when callback saves them");
    }

    /// <summary>
    /// Callback receives correct VectorRecord data with metadata and embeddings.
    /// </summary>
    [TestMethod]
    public async Task BuildAsync_WithCallback_RecordsHaveCorrectData()
    {
        var receivedRecords = new List<VectorRecord>();

        var store = await RagStore.BuildAsync(config => config
            .AddText("테스트 문서 내용입니다.", id: "test-doc")
            .UseLocalEmbedding(256)
            .UseInMemoryStore(),
            onDocumentEmbedded: records =>
            {
                receivedRecords.AddRange(records);
                return Task.CompletedTask;
            }
        );

        Assert.IsTrue(receivedRecords.Count > 0, "Should receive at least one record");

        var record = receivedRecords[0];
        Assert.IsNotNull(record.Id, "Record should have an ID");
        Assert.IsNotNull(record.Content, "Record should have content");
        Assert.IsNotNull(record.Vector, "Record should have an embedding vector");
        Assert.AreEqual(256, record.Vector.Length, "Embedding dimension should match");
        Assert.IsNotNull(record.Namespace, "Record should have a namespace");
    }

    /// <summary>
    /// ReplaceByFilterAsync on InMemoryVectorStore (default interface method):
    /// deletes by filter then inserts new records.
    /// </summary>
    [TestMethod]
    public async Task ReplaceByFilterAsync_InMemory_DeletesAndInserts()
    {
        var vectorStore = new InMemoryVectorStore();
        var embedding = new LocalEmbeddingProvider(256);

        // Insert initial records with metadata
        var oldRecord = new VectorRecord
        {
            Id = "old-chunk-1",
            Content = "이전 버전의 문서입니다.",
            Vector = await embedding.GetEmbeddingAsync("이전 버전의 문서입니다."),
            Metadata = new Dictionary<string, string> { ["full_path"] = "/docs/file.md" },
            Namespace = "default"
        };
        await vectorStore.UpsertAsync(oldRecord);

        // Verify old record exists
        var oldResult = await vectorStore.GetAsync("old-chunk-1");
        Assert.IsNotNull(oldResult, "Old record should exist before replace");

        // Replace using filter
        var newRecords = new List<VectorRecord>
        {
            new VectorRecord
            {
                Id = "new-chunk-1",
                Content = "새로운 버전의 문서입니다.",
                Vector = await embedding.GetEmbeddingAsync("새로운 버전의 문서입니다."),
                Metadata = new Dictionary<string, string> { ["full_path"] = "/docs/file.md" },
                Namespace = "default"
            }
        };

        var filter = VectorFilter.ByMetadata("full_path", "/docs/file.md");
        filter.Namespace = "default";
        await ((IVectorStore)vectorStore).ReplaceByFilterAsync(filter, newRecords);

        // Old record should be gone
        var afterOld = await vectorStore.GetAsync("old-chunk-1");
        Assert.IsNull(afterOld, "Old record should be deleted after replace");

        // New record should exist
        var afterNew = await vectorStore.GetAsync("new-chunk-1");
        Assert.IsNotNull(afterNew, "New record should exist after replace");
        Assert.AreEqual("새로운 버전의 문서입니다.", afterNew.Content);
    }

    /// <summary>
    /// End-to-end: BuildAsync with callback using ReplaceByFilterAsync
    /// simulates the file re-embedding replacement scenario.
    /// </summary>
    [TestMethod]
    public async Task BuildAsync_WithReplaceCallback_AtomicReplacement()
    {
        var vectorStore = new InMemoryVectorStore();
        var embedding = new LocalEmbeddingProvider(256);
        const string filePath = "/docs/policy.md";

        var filter = VectorFilter.ByMetadata("full_path", filePath);
        filter.Namespace = "default";

        // Step 1: Initial build — save with metadata via callback
        await RagStore.BuildAsync(config => config
            .AddText("환불은 7일 이내 가능합니다.", id: "policy-v1")
            .UseLocalEmbedding(256)
            .UseStore(vectorStore),
            onDocumentEmbedded: records =>
            {
                foreach (var r in records)
                    r.Metadata["full_path"] = filePath;
                return vectorStore.UpsertBatchAsync(records);
            }
        );

        // Verify initial data exists
        var initialResults = await vectorStore.SearchAsync(
            await embedding.GetEmbeddingAsync("환불"), topK: 5);
        Assert.IsTrue(initialResults.Count > 0, "Initial data should exist");
        Assert.IsTrue(initialResults[0].Record.Content.Contains("7일"), "Should contain v1 content");

        // Step 2: Re-build with replace callback — simulates file modification
        await RagStore.BuildAsync(config => config
            .AddText("환불은 14일 이내 가능합니다. 정책이 변경되었습니다.", id: "policy-v2")
            .UseLocalEmbedding(256)
            .UseStore(vectorStore),
            onDocumentEmbedded: records =>
            {
                foreach (var r in records)
                    r.Metadata["full_path"] = filePath;
                return ((IVectorStore)vectorStore).ReplaceByFilterAsync(filter, records);
            }
        );

        // Verify: old data replaced, only new data exists
        var allResults = await vectorStore.SearchAsync(
            await embedding.GetEmbeddingAsync("환불"), topK: 10);
        Assert.IsTrue(allResults.Count > 0, "New data should exist after replace");
        Assert.IsTrue(allResults.All(r => r.Record.Content.Contains("14일")),
            "All results should be v2 content — v1 should be deleted");
    }
}
