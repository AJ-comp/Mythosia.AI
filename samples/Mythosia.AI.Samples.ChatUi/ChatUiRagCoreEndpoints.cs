using Mythosia.AI.Rag;
using Mythosia.AI.Rag.Embeddings;
using Mythosia.AI.Rag.Loaders;
using Mythosia.AI.Rag.Splitters;
using Mythosia.AI.Loaders;
using Mythosia.VectorDb;
using Mythosia.VectorDb.InMemory;
using Mythosia.VectorDb.Pinecone;
using Mythosia.VectorDb.Postgres;
using Mythosia.VectorDb.Qdrant;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using static Mythosia.AI.Samples.ChatUi.ChatUiUtilityHelpers;

namespace Mythosia.AI.Samples.ChatUi
{
    internal static class ChatUiRagCoreEndpoints
    {
        public static void MapChatUiRagCoreEndpoints(this WebApplication app, RagReferenceState ragState, ChatUiRagEndpointState state, HttpClient embeddingHttpClient)
        {
            app.MapGet("/api/rag/pipeline-settings", () =>
            {
                return Results.Ok(ragState.GetSettings());
            });

            app.MapPost("/api/rag/pipeline-settings", (RagPipelineSettingsRequest req) =>
            {
                var current = ragState.GetSettings();
                var settings = new RagPipelineSettings(
                    ChunkSize: req.ChunkSize is > 0 ? req.ChunkSize.Value : current.ChunkSize,
                    ChunkOverlap: req.ChunkOverlap is >= 0 ? req.ChunkOverlap.Value : current.ChunkOverlap,
                    Chunker: string.IsNullOrWhiteSpace(req.Chunker) ? current.Chunker : req.Chunker.Trim().ToLowerInvariant(),
                    EmbeddingProvider: string.IsNullOrWhiteSpace(req.EmbeddingProvider) ? current.EmbeddingProvider : req.EmbeddingProvider.Trim().ToLowerInvariant(),
                    EmbeddingModel: string.IsNullOrWhiteSpace(req.EmbeddingModel) ? current.EmbeddingModel : req.EmbeddingModel.Trim(),
                    EmbeddingDimensions: req.EmbeddingDimensions is > 0 ? req.EmbeddingDimensions.Value : current.EmbeddingDimensions,
                    EmbeddingBaseUrl: string.IsNullOrWhiteSpace(req.EmbeddingBaseUrl) ? current.EmbeddingBaseUrl : req.EmbeddingBaseUrl.Trim(),
                    TopK: req.TopK is > 0 ? req.TopK.Value : current.TopK,
                    MinScore: req.MinScore ?? current.MinScore,
                    PromptTemplate: req.PromptTemplate ?? current.PromptTemplate,
                    QueryRewriterEnabled: req.QueryRewriterEnabled ?? current.QueryRewriterEnabled,
                    RewriterModelOverride: req.RewriterModelOverride);

                if (req.RewriterApiKey != null)
                    state.RewriterApiKey = string.IsNullOrWhiteSpace(req.RewriterApiKey) ? null : req.RewriterApiKey;

                ragState.UpdateSettings(settings);
                ragState.TryApplyQuerySettings(settings);
                return Results.Ok(settings);
            });

            app.MapGet("/api/rag/vector-store", () =>
            {
                return Results.Ok(new
                {
                    provider = state.VectorStoreProvider,
                    connectionString = state.PgConnectionString,
                    tableName = state.PgTableName,
                    schemaName = state.PgSchemaName,
                    dimension = state.PgDimension,
                    ensureSchema = state.PgEnsureSchema,
                    qdrantHost = state.QdrantHost,
                    qdrantPort = state.QdrantPort,
                    qdrantApiKey = (string?)null,
                    qdrantUseTls = state.QdrantUseTls,
                    qdrantDimension = state.QdrantDimension,
                    qdrantCollectionName = state.QdrantCollectionName,
                    pineconeIndexHost = state.PineconeIndexHost,
                    pineconeApiKey = (string?)null,
                    pineconeNamespace = state.PineconeNamespace
                });
            });

            app.MapPost("/api/rag/vector-store", async (VectorStoreConfigRequest req) =>
            {
                var provider = (req.Provider ?? "inmemory").Trim().ToLowerInvariant();

                if (provider == "postgres")
                {
                    if (string.IsNullOrWhiteSpace(req.ConnectionString))
                        return Results.BadRequest(new { error = "ConnectionString is required for PostgreSQL." });

                    state.PgConnectionString = req.ConnectionString.Trim();
                    state.PgTableName = string.IsNullOrWhiteSpace(req.TableName) ? "vectors" : req.TableName.Trim();
                    state.PgSchemaName = string.IsNullOrWhiteSpace(req.SchemaName) ? "public" : req.SchemaName.Trim();
                    state.PgDimension = req.Dimension is > 0 ? req.Dimension.Value : 1536;
                    state.PgEnsureSchema = req.EnsureSchema ?? true;

                    if (!string.IsNullOrWhiteSpace(req.OpenAiApiKey))
                        state.RagEmbeddingOpenAiKey = req.OpenAiApiKey.Trim();

                    try
                    {
                        var oldStore = state.VectorStore;
                        state.VectorStore = new PostgresStore(new PostgresOptions
                        {
                            ConnectionString = state.PgConnectionString,
                            Dimension = state.PgDimension,
                            TableName = state.PgTableName,
                            SchemaName = state.PgSchemaName,
                            EnsureSchema = state.PgEnsureSchema
                        });
                        state.VectorStoreProvider = "postgres";

                        if (oldStore is IDisposable disposable)
                            disposable.Dispose();

                        string? autoConnectWarning = null;
                        try
                        {
                            var settings = ragState.GetSettings();
                            var embeddingKey = !string.IsNullOrWhiteSpace(state.RagEmbeddingOpenAiKey) ? state.RagEmbeddingOpenAiKey : null;
                            var epKey = settings.EmbeddingProvider?.ToLowerInvariant() ?? "openai";

                            if (epKey != "ollama" && embeddingKey == null)
                            {
                                autoConnectWarning = "No API key provided for the embedding provider. "
                                    + "RAG queries will not work until an OpenAI API key is configured in the Document Reference panel.";
                            }
                            else
                            {
                                var store = await RagStore.BuildAsync(builder =>
                                {
                                    builder
                                        .UseStore(state.VectorStore)
                                        .WithTopK(settings.TopK);

                                    if (epKey == "ollama")
                                    {
                                        builder.UseEmbedding(new OllamaEmbeddingProvider(
                                            embeddingHttpClient,
                                            settings.EmbeddingModel ?? "qwen3-embedding:4b",
                                            settings.EmbeddingDimensions,
                                            settings.EmbeddingBaseUrl));
                                    }
                                    else
                                    {
                                        builder.UseEmbedding(new OpenAIEmbeddingProvider(
                                            embeddingKey!, embeddingHttpClient,
                                            settings.EmbeddingModel ?? "text-embedding-3-small",
                                            settings.EmbeddingDimensions));
                                    }

                                    if (settings.MinScore.HasValue)
                                        builder.WithScoreThreshold(settings.MinScore.Value);
                                    if (!string.IsNullOrWhiteSpace(settings.PromptTemplate))
                                        builder.WithPromptTemplate(settings.PromptTemplate);
                                });
                                ragState.SetExternalStore(store);
                            }
                        }
                        catch
                        {
                            // Auto-connect is best-effort; vector store connection is still valid
                        }

                        return Results.Ok(new { provider = state.VectorStoreProvider, status = "connected", warning = autoConnectWarning, tableName = state.PgTableName, schemaName = state.PgSchemaName, dimension = state.PgDimension });
                    }
                    catch (Exception ex)
                    {
                        return Results.BadRequest(new { error = $"Failed to connect: {ex.Message}" });
                    }
                }
                else if (provider == "qdrant")
                {
                    var rawHost = (req.QdrantHost ?? "").Trim();
                    if (rawHost.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                        rawHost = rawHost.Substring("https://".Length);
                    else if (rawHost.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
                        rawHost = rawHost.Substring("http://".Length);
                    var slashIdx = rawHost.IndexOf('/');
                    if (slashIdx >= 0) rawHost = rawHost.Substring(0, slashIdx);
                    state.QdrantHost = string.IsNullOrWhiteSpace(rawHost) ? "localhost" : rawHost;
                    state.QdrantPort = req.QdrantPort is > 0 ? req.QdrantPort.Value : 6334;
                    state.QdrantApiKey = string.IsNullOrWhiteSpace(req.QdrantApiKey) ? null : req.QdrantApiKey.Trim();
                    state.QdrantUseTls = req.QdrantUseTls ?? false;
                    state.QdrantDimension = req.Dimension is > 0 ? req.Dimension.Value : 1536;
                    state.QdrantCollectionName = string.IsNullOrWhiteSpace(req.QdrantCollectionName) ? "default" : req.QdrantCollectionName.Trim();

                    if (!string.IsNullOrWhiteSpace(req.OpenAiApiKey))
                        state.RagEmbeddingOpenAiKey = req.OpenAiApiKey.Trim();

                    try
                    {
                        var oldStore = state.VectorStore;
                        state.VectorStore = new QdrantStore(new QdrantOptions
                        {
                            Host = state.QdrantHost,
                            Port = state.QdrantPort,
                            ApiKey = state.QdrantApiKey,
                            UseTls = state.QdrantUseTls,
                            Dimension = state.QdrantDimension,
                            CollectionName = state.QdrantCollectionName
                        });
                        state.VectorStoreProvider = "qdrant";

                        if (oldStore is IDisposable disposable)
                            disposable.Dispose();

                        string? autoConnectWarning = null;
                        try
                        {
                            var settings = ragState.GetSettings();
                            var embeddingKey = !string.IsNullOrWhiteSpace(state.RagEmbeddingOpenAiKey) ? state.RagEmbeddingOpenAiKey : null;
                            var epKey = settings.EmbeddingProvider?.ToLowerInvariant() ?? "openai";

                            if (epKey != "ollama" && embeddingKey == null)
                            {
                                autoConnectWarning = "No API key provided for the embedding provider. "
                                    + "RAG queries will not work until an OpenAI API key is configured in the Document Reference panel.";
                            }
                            else
                            {
                                var store = await RagStore.BuildAsync(builder =>
                                {
                                    builder
                                        .UseStore(state.VectorStore)
                                        .WithTopK(settings.TopK)
                                        .WithNamespace(state.QdrantCollectionName);

                                    if (epKey == "ollama")
                                    {
                                        builder.UseEmbedding(new OllamaEmbeddingProvider(
                                            embeddingHttpClient,
                                            settings.EmbeddingModel ?? "qwen3-embedding:4b",
                                            settings.EmbeddingDimensions,
                                            settings.EmbeddingBaseUrl));
                                    }
                                    else
                                    {
                                        builder.UseEmbedding(new OpenAIEmbeddingProvider(
                                            embeddingKey!, embeddingHttpClient,
                                            settings.EmbeddingModel ?? "text-embedding-3-small",
                                            settings.EmbeddingDimensions));
                                    }

                                    if (settings.MinScore.HasValue)
                                        builder.WithScoreThreshold(settings.MinScore.Value);
                                    if (!string.IsNullOrWhiteSpace(settings.PromptTemplate))
                                        builder.WithPromptTemplate(settings.PromptTemplate);
                                });
                                ragState.SetExternalStore(store);
                            }
                        }
                        catch
                        {
                            // Auto-connect is best-effort; vector store connection is still valid
                        }

                        return Results.Ok(new { provider = state.VectorStoreProvider, status = "connected", warning = autoConnectWarning, host = state.QdrantHost, port = state.QdrantPort, dimension = state.QdrantDimension, collectionName = state.QdrantCollectionName });
                    }
                    catch (Exception ex)
                    {
                        return Results.BadRequest(new { error = $"Failed to connect: {ex.Message}" });
                    }
                }
                else if (provider == "pinecone")
                {
                    state.PineconeIndexHost = (req.PineconeIndexHost ?? "").Trim();
                    state.PineconeApiKey = string.IsNullOrWhiteSpace(req.PineconeApiKey) ? null : req.PineconeApiKey.Trim();
                    state.PineconeNamespace = string.IsNullOrWhiteSpace(req.PineconeNamespace) ? "default" : req.PineconeNamespace.Trim();

                    if (string.IsNullOrWhiteSpace(state.PineconeIndexHost))
                        return Results.BadRequest(new { error = "PineconeIndexHost is required for Pinecone." });

                    if (string.IsNullOrWhiteSpace(state.PineconeApiKey))
                        return Results.BadRequest(new { error = "PineconeApiKey is required for Pinecone." });

                    if (!string.IsNullOrWhiteSpace(req.OpenAiApiKey))
                        state.RagEmbeddingOpenAiKey = req.OpenAiApiKey.Trim();

                    try
                    {
                        var oldStore = state.VectorStore;
                        state.VectorStore = new PineconeStore(new PineconeOptions
                        {
                            IndexHost = state.PineconeIndexHost,
                            ApiKey = state.PineconeApiKey,
                            DefaultNamespace = state.PineconeNamespace
                        });
                        state.VectorStoreProvider = "pinecone";

                        if (oldStore is IDisposable disposable)
                            disposable.Dispose();

                        string? autoConnectWarning = null;
                        try
                        {
                            var settings = ragState.GetSettings();
                            var embeddingKey = !string.IsNullOrWhiteSpace(state.RagEmbeddingOpenAiKey) ? state.RagEmbeddingOpenAiKey : null;
                            var epKey = settings.EmbeddingProvider?.ToLowerInvariant() ?? "openai";

                            if (epKey != "ollama" && embeddingKey == null)
                            {
                                autoConnectWarning = "No API key provided for the embedding provider. "
                                    + "RAG queries will not work until an OpenAI API key is configured in the Document Reference panel.";
                            }
                            else
                            {
                                var store = await RagStore.BuildAsync(builder =>
                                {
                                    builder
                                        .UseStore(state.VectorStore)
                                        .WithTopK(settings.TopK)
                                        .WithNamespace(state.PineconeNamespace);

                                    if (epKey == "ollama")
                                    {
                                        builder.UseEmbedding(new OllamaEmbeddingProvider(
                                            embeddingHttpClient,
                                            settings.EmbeddingModel ?? "qwen3-embedding:4b",
                                            settings.EmbeddingDimensions,
                                            settings.EmbeddingBaseUrl));
                                    }
                                    else
                                    {
                                        builder.UseEmbedding(new OpenAIEmbeddingProvider(
                                            embeddingKey!, embeddingHttpClient,
                                            settings.EmbeddingModel ?? "text-embedding-3-small",
                                            settings.EmbeddingDimensions));
                                    }

                                    if (settings.MinScore.HasValue)
                                        builder.WithScoreThreshold(settings.MinScore.Value);
                                    if (!string.IsNullOrWhiteSpace(settings.PromptTemplate))
                                        builder.WithPromptTemplate(settings.PromptTemplate);
                                });
                                ragState.SetExternalStore(store);
                            }
                        }
                        catch
                        {
                            // Auto-connect is best-effort; vector store connection is still valid
                        }

                        return Results.Ok(new { provider = state.VectorStoreProvider, status = "connected", warning = autoConnectWarning, indexHost = state.PineconeIndexHost, @namespace = state.PineconeNamespace });
                    }
                    catch (Exception ex)
                    {
                        return Results.BadRequest(new { error = $"Failed to connect: {ex.Message}" });
                    }
                }
                else
                {
                    var oldStore = state.VectorStore;
                    state.VectorStore = new InMemoryVectorStore();
                    state.VectorStoreProvider = "inmemory";

                    if (oldStore is IDisposable disposable)
                        disposable.Dispose();

                    ragState.ClearStore();

                    return Results.Ok(new { provider = state.VectorStoreProvider, status = "switched" });
                }
            });

            app.MapGet("/api/rag/status", () =>
            {
                var settings = ragState.GetSettings();
                var hasIndex = ragState.HasStore || ragState.TryGetSnapshot(out _, out _);
                return Results.Ok(new
                {
                    hasIndex,
                    lastUpdated = ragState.LastUpdated,
                    settings,
                    vectorStoreProvider = state.VectorStoreProvider
                });
            });

            app.MapPost("/api/rag/reference", async (HttpRequest request, HttpContext ctx) =>
            {
                if (!request.HasFormContentType)
                    return Results.BadRequest(new { error = "Multipart form data is required." });

                var form = await request.ReadFormAsync(ctx.RequestAborted);
                if (form.Files.Count == 0)
                    return Results.BadRequest(new { error = "At least one file is required." });

                var settings = ragState.GetSettings();
                var chunkSize = ParsePositiveInt(form["chunkSize"], settings.ChunkSize);
                var chunkOverlap = ParsePositiveInt(form["chunkOverlap"], settings.ChunkOverlap);
                var chunkerKey = NormalizeRagKey(form["chunker"], settings.Chunker);
                var embeddingProviderKey = NormalizeRagKey(form["embeddingProvider"], settings.EmbeddingProvider);
                var embeddingModel = string.IsNullOrWhiteSpace(form["embeddingModel"])
                    ? settings.EmbeddingModel
                    : form["embeddingModel"].ToString().Trim();
                var embeddingDimensions = ParsePositiveInt(form["embeddingDimensions"], settings.EmbeddingDimensions);
                var embeddingBaseUrl = string.IsNullOrWhiteSpace(form["embeddingBaseUrl"])
                    ? settings.EmbeddingBaseUrl
                    : form["embeddingBaseUrl"].ToString().Trim();
                var topK = ParsePositiveInt(form["topK"], settings.TopK);
                var minScore = ParseOptionalDouble(form["minScore"]) ?? settings.MinScore;
                var promptTemplate = string.IsNullOrWhiteSpace(form["promptTemplate"])
                    ? settings.PromptTemplate
                    : form["promptTemplate"].ToString();
                var openAiApiKey = form["openaiApiKey"].ToString();
                if (!string.IsNullOrWhiteSpace(openAiApiKey))
                    state.RagEmbeddingOpenAiKey = openAiApiKey.Trim();

                var documents = new List<RagDocument>();
                var chunks = new List<RagChunk>();
                var records = new List<VectorRecord>();

                var splitter = new TrackingTextSplitter(BuildTextSplitter(chunkerKey, chunkSize, chunkOverlap), chunks);
                var resolvedEmbeddingModel = string.IsNullOrWhiteSpace(embeddingModel)
                    ? embeddingProviderKey == "ollama" ? "qwen3-embedding:4b" : "text-embedding-3-small"
                    : embeddingModel;
                IEmbeddingProvider embeddingProvider = embeddingProviderKey?.Equals("ollama", StringComparison.OrdinalIgnoreCase) == true
                    ? new OllamaEmbeddingProvider(
                        embeddingHttpClient,
                        resolvedEmbeddingModel,
                        embeddingDimensions,
                        embeddingBaseUrl)
                    : BuildOpenAiEmbeddingProvider(openAiApiKey, embeddingHttpClient, resolvedEmbeddingModel, embeddingDimensions);
                var trackingStore = new TrackingVectorStore(state.VectorStore, records);

                var tempRoot = Path.Combine(Path.GetTempPath(), "mythosia-rag", Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(tempRoot);
                var savedFiles = new List<(string path, string displayName)>();

                try
                {
                    foreach (var file in form.Files)
                    {
                        if (file.Length <= 0)
                            continue;

                        var safeName = Path.GetFileName(file.FileName);
                        var filePath = Path.Combine(tempRoot, safeName);

                        await using var stream = File.Create(filePath);
                        await file.CopyToAsync(stream, ctx.RequestAborted);

                        savedFiles.Add((filePath, safeName));
                    }

                    if (savedFiles.Count == 0)
                        return Results.BadRequest(new { error = "Uploaded files were empty." });

                    var store = await RagStore.BuildAsync(builder =>
                    {
                        builder
                            .WithTextSplitter(splitter)
                            .WithTopK(topK)
                            .UseEmbedding(embeddingProvider)
                            .UseStore(trackingStore);

                        if (state.VectorStoreProvider == "qdrant")
                            builder.WithNamespace(state.QdrantCollectionName);
                        else if (state.VectorStoreProvider == "pinecone")
                            builder.WithNamespace(state.PineconeNamespace);

                        if (minScore.HasValue)
                            builder.WithScoreThreshold(minScore.Value);

                        if (!string.IsNullOrWhiteSpace(promptTemplate))
                            builder.WithPromptTemplate(promptTemplate);

                        foreach (var entry in savedFiles)
                        {
                            var loader = new TrackingDocumentLoader(
                                CreateLoaderForExtension(Path.GetExtension(entry.path)),
                                documents,
                                entry.displayName);
                            builder.AddDocuments(loader, entry.path);
                        }
                    }, ctx.RequestAborted);

                    var trace = RagReferenceTraceBuilder.Build(documents, chunks, records, embeddingProvider.Dimensions);
                    var config = new RagReferenceConfig(
                        savedFiles.Select(entry => entry.displayName).ToList(),
                        chunkSize,
                        chunkOverlap,
                        NormalizeRagKey(chunkerKey, "character"),
                        NormalizeRagKey(embeddingProviderKey, "local"),
                        resolvedEmbeddingModel,
                        embeddingDimensions,
                        embeddingBaseUrl,
                        topK,
                        minScore,
                        promptTemplate);
                    ragState.Update(store, trace, config);
                    return Results.Ok(trace);
                }
                catch (Exception ex)
                {
                    return Results.BadRequest(new { error = ex.Message });
                }
                finally
                {
                    try
                    {
                        if (Directory.Exists(tempRoot))
                            Directory.Delete(tempRoot, true);
                    }
                    catch
                    {
                        // ignore cleanup failures
                    }
                }
            });
        }
    }
}
