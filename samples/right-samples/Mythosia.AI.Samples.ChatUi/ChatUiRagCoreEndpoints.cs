using Mythosia.AI.Loaders;
using Mythosia.AI.Rag;
using Mythosia.AI.Rag.Embeddings;
using Mythosia.AI.Rag.Reranking;
using Mythosia.VectorDb;
using Mythosia.VectorDb.InMemory;
using Mythosia.VectorDb.Pinecone;
using Mythosia.VectorDb.Postgres;
using Mythosia.VectorDb.Qdrant;
using static Mythosia.AI.Samples.ChatUi.ChatUiUtilityHelpers;

namespace Mythosia.AI.Samples.ChatUi;

internal static class ChatUiRagCoreEndpoints
{
    public static void MapChatUiRagCoreEndpoints(this WebApplication app, RagReferenceState ragState, ChatUiRagEndpointState state, HttpClient embeddingHttpClient)
    {
        app.MapGet("/api/rag/vector-store", () =>
        {
            return Results.Ok(new
            {
                provider = "inmemory"
            });
        });

        app.MapPost("/api/rag/vector-store", async (VectorStoreConfigRequest req) =>
        {
            VectorStoreBuildResult buildResult;
            try
            {
                buildResult = BuildVectorStore(req);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = $"Failed to connect: {ex.Message}" });
            }

            if (buildResult.Provider == "inmemory")
            {
                ragState.ClearStore();
                return Results.Ok(new { provider = "inmemory", status = "switched" });
            }

            string? autoConnectWarning;
            try
            {
                autoConnectWarning = await TryAutoConnectRagStoreAsync(
                    ragState,
                    embeddingHttpClient,
                    buildResult.Store,
                    req.OpenAiApiKey,
                    buildResult.Namespace);
            }
            catch (Exception ex)
            {
                if (buildResult.Store is IDisposable disposable)
                    disposable.Dispose();
                return Results.BadRequest(new { error = $"Failed to connect: {ex.Message}" });
            }

            if (autoConnectWarning != null && buildResult.Store is IDisposable warnDisposable)
                warnDisposable.Dispose();

            return buildResult.Provider switch
            {
                "postgres" => Results.Ok(new
                {
                    provider = buildResult.Provider,
                    status = "connected",
                    warning = autoConnectWarning,
                    tableName = buildResult.TableName,
                    schemaName = buildResult.SchemaName,
                    dimension = buildResult.Dimension
                }),
                "qdrant" => Results.Ok(new
                {
                    provider = buildResult.Provider,
                    status = "connected",
                    warning = autoConnectWarning,
                    host = buildResult.Host,
                    port = buildResult.Port,
                    dimension = buildResult.Dimension,
                    collectionName = buildResult.CollectionName
                }),
                "pinecone" => Results.Ok(new
                {
                    provider = buildResult.Provider,
                    status = "connected",
                    warning = autoConnectWarning,
                    indexHost = buildResult.IndexHost,
                    @namespace = buildResult.Namespace
                }),
                _ => Results.Ok(new { provider = buildResult.Provider, status = "connected", warning = autoConnectWarning })
            };
        });

        app.MapPost("/api/rag/ollama-test", async (OllamaTestRequest req) =>
        {
            var baseUrl = NormalizeOptionalValue(req.BaseUrl);
            var model = NormalizeOptionalValue(req.Model);
            if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(model))
                return Results.BadRequest(new { error = "Ollama baseUrl and model are required." });

            baseUrl = baseUrl.TrimEnd('/');

            try
            {
                // 1. Health check
                var healthRes = await embeddingHttpClient.GetAsync($"{baseUrl}/api/tags");
                if (!healthRes.IsSuccessStatusCode)
                    return Results.BadRequest(new { error = $"Ollama is not reachable at {baseUrl} (HTTP {(int)healthRes.StatusCode})." });

                var json = await healthRes.Content.ReadAsStringAsync();
                using var doc = System.Text.Json.JsonDocument.Parse(json);

                // 2. Check if model is available
                bool modelFound = false;
                if (doc.RootElement.TryGetProperty("models", out var models))
                {
                    foreach (var m in models.EnumerateArray())
                    {
                        var name = m.TryGetProperty("name", out var n) ? n.GetString() : null;
                        if (name != null && (name.Equals(model, StringComparison.OrdinalIgnoreCase)
                            || name.StartsWith(model + ":", StringComparison.OrdinalIgnoreCase)))
                        {
                            modelFound = true;
                            break;
                        }
                    }
                }

                return Results.Ok(new { status = "ok", baseUrl, model, modelFound });
            }
            catch (HttpRequestException ex)
            {
                return Results.BadRequest(new { error = $"Cannot reach Ollama at {baseUrl}: {ex.Message}" });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = $"Ollama connection test failed: {ex.Message}" });
            }
        });

        app.MapPost("/api/rag/vllm-test", async (VllmTestRequest req) =>
        {
            var baseUrl = NormalizeOptionalValue(req.BaseUrl);
            var model = NormalizeOptionalValue(req.Model);
            if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(model))
                return Results.BadRequest(new { error = "vLLM baseUrl and model are required." });

            baseUrl = baseUrl.TrimEnd('/');

            try
            {
                var healthRes = await embeddingHttpClient.GetAsync($"{baseUrl}/health");
                if (!healthRes.IsSuccessStatusCode)
                    return Results.BadRequest(new { error = $"vLLM is not reachable at {baseUrl} (HTTP {(int)healthRes.StatusCode})." });

                var provider = new VllmEmbeddingProvider(embeddingHttpClient, model, 0, baseUrl);
                await provider.GetEmbeddingAsync("connection test");

                return Results.Ok(new { status = "ok", baseUrl, model, modelFound = true });
            }
            catch (HttpRequestException ex)
            {
                return Results.BadRequest(new { error = $"Cannot reach vLLM at {baseUrl}: {ex.Message}" });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = $"vLLM connection test failed: {ex.Message}" });
            }
        });

        app.MapPost("/api/rag/vllm-rerank-test", async (VllmRerankTestRequest req) =>
        {
            var baseUrl = NormalizeOptionalValue(req.BaseUrl);
            var model = NormalizeOptionalValue(req.Model);
            if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(model))
                return Results.BadRequest(new { error = "vLLM rerank baseUrl and model are required." });

            baseUrl = baseUrl.TrimEnd('/');
            var apiKey = string.IsNullOrWhiteSpace(req.ApiKey) ? null : req.ApiKey.Trim();

            try
            {
                var healthRes = await embeddingHttpClient.GetAsync($"{baseUrl}/health");
                if (!healthRes.IsSuccessStatusCode)
                    return Results.BadRequest(new { error = $"vLLM reranker is not reachable at {baseUrl} (HTTP {(int)healthRes.StatusCode})." });

                var reranker = new VllmReranker(
                    httpClient: embeddingHttpClient,
                    model: model,
                    baseUrl: baseUrl,
                    apiKey: apiKey);

                var sampleResults = new List<VectorSearchResult>
                {
                    new(new VectorRecord { Content = "First test passage" }, 0.5),
                    new(new VectorRecord { Content = "Second test passage" }, 0.4)
                };

                await reranker.RerankAsync("test query", sampleResults, 1);

                return Results.Ok(new { status = "ok", baseUrl, model, modelFound = true });
            }
            catch (HttpRequestException ex)
            {
                return Results.BadRequest(new { error = $"Cannot reach vLLM reranker at {baseUrl}: {ex.Message}" });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = $"vLLM rerank connection test failed: {ex.Message}" });
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
                vectorStoreProvider = "inmemory"
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
            var chunkSize = ParseOptionalPositiveInt(form["chunkSize"]);
            if (chunkSize is not > 0)
                return Results.BadRequest(new { error = "Chunk size is required." });
            var chunkOverlap = ParseOptionalNonNegativeInt(form["chunkOverlap"]);
            if (chunkOverlap is null)
                return Results.BadRequest(new { error = "Chunk overlap must be zero or a positive integer." });
            var chunkerKey = RequireNormalizedRagKey(form["chunker"], "Chunker is required.");
            var embeddingProviderKey = RequireNormalizedRagKey(form["embeddingProvider"], "Embedding provider is required.");
            var embeddingModel = NormalizeOptionalValue(form["embeddingModel"]);
            if (string.IsNullOrWhiteSpace(embeddingModel))
                return Results.BadRequest(new { error = "Embedding model is required." });
            var embeddingDimensions = ParseOptionalPositiveInt(form["embeddingDimensions"]);
            var embeddingBaseUrl = NormalizeOptionalValue(form["embeddingBaseUrl"]) ?? string.Empty;
            var topK = ParseOptionalPositiveInt(form["finalFilterTopK"]);
            if (topK is not > 0)
                return Results.BadRequest(new { error = "TopK is required." });
            var minScore = ParseOptionalDouble(form["finalFilterMinScore"]);
            var retrievalMinScoreDivider = ParseOptionalDouble(form["retrievalDerivationMinScoreDivider"]);
            var promptTemplate = string.IsNullOrWhiteSpace(form["promptTemplate"])
                ? null
                : form["promptTemplate"].ToString();
            var queryRewriterEnabled = ParseOptionalBool(form["queryRewriterEnabled"]);
            var rewriterModelOverride = string.IsNullOrWhiteSpace(form["rewriterModelOverride"])
                ? null
                : form["rewriterModelOverride"].ToString().Trim();
            var hybridSearchEnabled = ParseOptionalBool(form["hybridSearchEnabled"]);
            var hybridSearchVectorWeight = ParseOptionalFloat(form["hybridSearchVectorWeight"]);
            var rerankEnabled = ParseOptionalBool(form["rerankEnabled"]);
            var rerankProvider = string.IsNullOrWhiteSpace(form["rerankProvider"])
                ? ""
                : form["rerankProvider"].ToString().Trim().ToLowerInvariant();
            var rerankModel = NormalizeOptionalValue(form["rerankModel"]);
            var rerankBaseUrl = NormalizeOptionalValue(form["rerankBaseUrl"]);
            var rerankApiKey = string.IsNullOrWhiteSpace(form["rerankApiKey"])
                ? null
                : form["rerankApiKey"].ToString().Trim();
            var retrievalMultiplier = ParseOptionalPositiveInt(form["retrievalDerivationTopKMultiplier"]);
            var openAiApiKey = form["openaiApiKey"].ToString();
            if (!string.IsNullOrWhiteSpace(openAiApiKey))
                openAiApiKey = openAiApiKey.Trim();
            var rewriterApiKey = form["rewriterApiKey"].ToString();
            if (!string.IsNullOrWhiteSpace(rewriterApiKey))
                state.RewriterApiKey = rewriterApiKey.Trim();

            if (embeddingDimensions is not > 0)
                return Results.BadRequest(new { error = "Embedding dimensions must be a positive integer." });
            if (retrievalMultiplier is not > 0)
                return Results.BadRequest(new { error = "Retrieval multiplier is required." });
            if (queryRewriterEnabled is null)
                return Results.BadRequest(new { error = "Query rewriter enabled flag is required." });
            if (hybridSearchEnabled is null)
                return Results.BadRequest(new { error = "Hybrid search enabled flag is required." });
            if (hybridSearchVectorWeight is null)
                return Results.BadRequest(new { error = "Hybrid search vector weight is required." });
            if (rerankEnabled is null)
                return Results.BadRequest(new { error = "Rerank enabled flag is required." });

            var chunkSizeValue = chunkSize.Value;
            var chunkOverlapValue = chunkOverlap.Value;
            var embeddingDimensionsValue = embeddingDimensions.Value;
            var topKValue = topK.Value;
            var retrievalMultiplierValue = retrievalMultiplier.Value;
            var queryRewriterEnabledValue = queryRewriterEnabled.Value;
            var hybridSearchEnabledValue = hybridSearchEnabled.Value;
            var hybridSearchVectorWeightValue = hybridSearchVectorWeight.Value;
            var rerankEnabledValue = rerankEnabled.Value;

            if (rerankEnabledValue && string.Equals(rerankProvider, "vllm", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(rerankModel))
                    return Results.BadRequest(new { error = "vLLM rerank model is required when re-ranking is enabled." });
                if (string.IsNullOrWhiteSpace(rerankBaseUrl))
                    return Results.BadRequest(new { error = "vLLM rerank base URL is required when re-ranking is enabled." });
            }
            if (rerankEnabledValue && string.IsNullOrWhiteSpace(rerankProvider))
                return Results.BadRequest(new { error = "Rerank provider is required when re-ranking is enabled." });

            var requestSettings = new RagPipelineSettings(
                ChunkSize: chunkSizeValue,
                ChunkOverlap: chunkOverlapValue,
                Chunker: chunkerKey,
                EmbeddingProvider: embeddingProviderKey,
                EmbeddingModel: embeddingModel,
                EmbeddingDimensions: embeddingDimensionsValue,
                EmbeddingBaseUrl: embeddingBaseUrl,
                FinalFilter: new RagFilter
                {
                    TopK = topKValue,
                    MinScore = minScore
                },
                RetrievalDerivation: new RagRetrievalDerivation
                {
                    TopKMultiplier = retrievalMultiplierValue,
                    MinScoreDivider = retrievalMinScoreDivider.HasValue && retrievalMinScoreDivider.Value > 0d
                        ? retrievalMinScoreDivider.Value
                        : 3d
                },
                PromptTemplate: promptTemplate,
                QueryRewriterEnabled: queryRewriterEnabledValue,
                RewriterModelOverride: rewriterModelOverride,
                HybridSearchEnabled: hybridSearchEnabledValue,
                HybridSearchVectorWeight: hybridSearchVectorWeightValue,
                RerankEnabled: rerankEnabledValue,
                RerankProvider: rerankProvider,
                RerankModel: rerankModel ?? string.Empty,
                RerankBaseUrl: rerankBaseUrl ?? string.Empty,
                RerankApiKey: rerankApiKey);

            var vectorStoreRequest = ParseVectorStoreConfig(form);
            VectorStoreBuildResult vectorStoreResult;
            try
            {
                vectorStoreResult = BuildVectorStore(vectorStoreRequest);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = $"Failed to connect: {ex.Message}" });
            }

            var documents = new List<RagDocument>();
            var chunks = new List<RagChunk>();
            var records = new List<VectorRecord>();

            var splitter = new TrackingTextSplitter(BuildTextSplitter(chunkerKey, chunkSizeValue, chunkOverlapValue), chunks);
            IEmbeddingProvider embeddingProvider = embeddingProviderKey?.Equals("ollama", StringComparison.OrdinalIgnoreCase) == true
                ? new OllamaEmbeddingProvider(
                    embeddingHttpClient,
                    embeddingModel,
                    embeddingDimensionsValue,
                    embeddingBaseUrl)
                : embeddingProviderKey?.Equals("vllm", StringComparison.OrdinalIgnoreCase) == true
                    ? new VllmEmbeddingProvider(
                        embeddingHttpClient,
                        embeddingModel,
                        embeddingDimensionsValue,
                        embeddingBaseUrl)
                    : BuildOpenAiEmbeddingProvider(openAiApiKey, embeddingHttpClient, embeddingModel, embeddingDimensionsValue);
            var trackingStore = new TrackingVectorStore(vectorStoreResult.Store, records);

            var tempRoot = Path.Combine(Path.GetTempPath(), "mythosia-rag", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);
            var savedFiles = new List<(string path, string displayName)>();
            var shouldDisposeStore = true;

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
                        .WithTopK(topKValue)
                        .UseEmbedding(embeddingProvider)
                        .UseStore(trackingStore);

                    if (!string.IsNullOrWhiteSpace(vectorStoreResult.Namespace))
                        builder.WithNamespace(vectorStoreResult.Namespace);

                    if (requestSettings.FinalFilter.MinScore.HasValue)
                        builder.WithScoreThreshold(requestSettings.FinalFilter.MinScore.Value);
                    builder.WithRetrievalMultiplier(requestSettings.RetrievalDerivation.TopKMultiplier);
                    if (requestSettings.FinalFilter.MinScore.HasValue)
                        builder.WithRetrievalMinScore(
                            requestSettings.FinalFilter.MinScore.Value / Math.Max(1d, requestSettings.RetrievalDerivation.MinScoreDivider));

                    if (!string.IsNullOrWhiteSpace(promptTemplate))
                        builder.WithPromptTemplate(promptTemplate);

                    ApplyHybridAndReranker(builder, requestSettings);

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
                    chunkSizeValue,
                    chunkOverlapValue,
                    chunkerKey!,
                    embeddingProviderKey!,
                    embeddingModel!,
                    embeddingDimensionsValue,
                    embeddingBaseUrl,
                    requestSettings.FinalFilter,
                    requestSettings.RetrievalDerivation,
                    promptTemplate);
                ragState.Update(store, trace, config);
                ragState.UpdateSettings(requestSettings);
                ragState.TryApplyQuerySettings(requestSettings);
                shouldDisposeStore = false;
                return Results.Ok(trace);
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
            finally
            {
                if (shouldDisposeStore && vectorStoreResult.Store is IDisposable disposable)
                    disposable.Dispose();

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

    private record OllamaTestRequest(string? BaseUrl, string? Model);
    private record VllmTestRequest(string? BaseUrl, string? Model);
    private record VllmRerankTestRequest(string? BaseUrl, string? Model, string? ApiKey);

    private sealed record VectorStoreBuildResult(
        string Provider,
        IVectorStore Store,
        string? Namespace = null,
        string? TableName = null,
        string? SchemaName = null,
        int? Dimension = null,
        string? Host = null,
        int? Port = null,
        string? CollectionName = null,
        string? IndexHost = null);

    internal static async Task<string?> EnsureExternalStoreMatchesSettingsAsync(
        RagReferenceState ragState,
        HttpClient embeddingHttpClient,
        RagPipelineSettings settings,
        VectorStoreConfigRequest? vectorStoreRequest)
    {
        if (vectorStoreRequest == null)
            return null;

        var provider = NormalizeOptionalValue(vectorStoreRequest.Provider)?.ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(provider) || provider == "inmemory")
            return null;

        ragState.UpdateSettings(settings);

        var buildResult = BuildVectorStore(vectorStoreRequest);
        var warning = await TryAutoConnectRagStoreAsync(
            ragState,
            embeddingHttpClient,
            buildResult.Store,
            vectorStoreRequest.OpenAiApiKey,
            buildResult.Namespace);

        if (warning != null && buildResult.Store is IDisposable disposable)
            disposable.Dispose();

        return warning;
    }

    private static VectorStoreConfigRequest ParseVectorStoreConfig(IFormCollection form)
    {
        string? GetValue(string key) => NormalizeOptionalValue(form[key].ToString());

        return new VectorStoreConfigRequest(
            Provider: GetValue("provider"),
            ConnectionString: GetValue("connectionString"),
            TableName: GetValue("tableName"),
            SchemaName: GetValue("schemaName"),
            Dimension: ParseOptionalPositiveInt(form["dimension"]),
            EnsureSchema: ParseOptionalBool(form["ensureSchema"]),
            OpenAiApiKey: null,
            QdrantHost: GetValue("qdrantHost"),
            QdrantPort: ParseOptionalPositiveInt(form["qdrantPort"]),
            QdrantApiKey: GetValue("qdrantApiKey"),
            QdrantUseTls: ParseOptionalBool(form["qdrantUseTls"]),
            QdrantCollectionName: GetValue("qdrantCollectionName"),
            PineconeIndexHost: GetValue("pineconeIndexHost"),
            PineconeApiKey: GetValue("pineconeApiKey"),
            PineconeNamespace: GetValue("pineconeNamespace"));
    }

    private static VectorStoreBuildResult BuildVectorStore(VectorStoreConfigRequest req)
    {
        var provider = NormalizeOptionalValue(req.Provider)?.ToLowerInvariant() ?? "inmemory";

        if (provider == "postgres")
        {
            var connectionString = NormalizeOptionalValue(req.ConnectionString);
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new ArgumentException("ConnectionString is required for PostgreSQL.");

            var tableName = NormalizeOptionalValue(req.TableName);
            var schemaName = NormalizeOptionalValue(req.SchemaName);
            if (string.IsNullOrWhiteSpace(tableName))
                throw new ArgumentException("TableName is required for PostgreSQL.");
            if (string.IsNullOrWhiteSpace(schemaName))
                throw new ArgumentException("SchemaName is required for PostgreSQL.");
            if (req.Dimension is not > 0)
                throw new ArgumentException("Dimension is required for PostgreSQL.");
            var dimension = req.Dimension.Value;
            if (req.EnsureSchema is null)
                throw new ArgumentException("EnsureSchema is required for PostgreSQL.");
            var ensureSchema = req.EnsureSchema.Value;

            var store = new PostgresStore(new PostgresOptions
            {
                ConnectionString = connectionString,
                Dimension = dimension,
                TableName = tableName,
                SchemaName = schemaName,
                EnsureSchema = ensureSchema
            });

            return new VectorStoreBuildResult(
                Provider: "postgres",
                Store: store,
                TableName: tableName,
                SchemaName: schemaName,
                Dimension: dimension);
        }

        if (provider == "qdrant")
        {
            var host = NormalizeHost(req.QdrantHost);
            if (string.IsNullOrWhiteSpace(host))
                throw new ArgumentException("QdrantHost is required for Qdrant.");
            if (req.QdrantPort is not > 0)
                throw new ArgumentException("QdrantPort is required for Qdrant.");
            var port = req.QdrantPort.Value;
            if (req.Dimension is not > 0)
                throw new ArgumentException("Dimension is required for Qdrant.");
            var dimension = req.Dimension.Value;
            var collectionName = NormalizeOptionalValue(req.QdrantCollectionName);
            if (string.IsNullOrWhiteSpace(collectionName))
                throw new ArgumentException("QdrantCollectionName is required for Qdrant.");
            if (req.QdrantUseTls is null)
                throw new ArgumentException("QdrantUseTls is required for Qdrant.");

            var store = new QdrantStore(new QdrantOptions
            {
                Host = host,
                Port = port,
                ApiKey = NormalizeOptionalValue(req.QdrantApiKey),
                UseTls = req.QdrantUseTls.Value,
                Dimension = dimension,
                CollectionName = collectionName
            });

            return new VectorStoreBuildResult(
                Provider: "qdrant",
                Store: store,
                Namespace: collectionName,
                Host: host,
                Port: port,
                Dimension: dimension,
                CollectionName: collectionName);
        }

        if (provider == "pinecone")
        {
            var indexHost = NormalizeOptionalValue(req.PineconeIndexHost);
            var apiKey = NormalizeOptionalValue(req.PineconeApiKey);
            if (string.IsNullOrWhiteSpace(indexHost))
                throw new ArgumentException("PineconeIndexHost is required for Pinecone.");
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new ArgumentException("PineconeApiKey is required for Pinecone.");

            var @namespace = NormalizeOptionalValue(req.PineconeNamespace);
            if (string.IsNullOrWhiteSpace(@namespace))
                throw new ArgumentException("PineconeNamespace is required for Pinecone.");

            var store = new PineconeStore(new PineconeOptions
            {
                IndexHost = indexHost,
                ApiKey = apiKey,
                DefaultNamespace = @namespace
            });

            return new VectorStoreBuildResult(
                Provider: "pinecone",
                Store: store,
                Namespace: @namespace,
                IndexHost: indexHost);
        }

        return new VectorStoreBuildResult("inmemory", new InMemoryVectorStore());
    }

    private static string NormalizeHost(string? host)
    {
        var rawHost = NormalizeOptionalValue(host) ?? string.Empty;
        if (rawHost.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            rawHost = rawHost.Substring("https://".Length);
        else if (rawHost.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            rawHost = rawHost.Substring("http://".Length);
        var slashIdx = rawHost.IndexOf('/');
        if (slashIdx >= 0)
            rawHost = rawHost.Substring(0, slashIdx);
        return rawHost.Trim();
    }

    private static string RequireNormalizedRagKey(string? value, string errorMessage)
    {
        var normalized = NormalizeOptionalValue(value)?.ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalized))
            throw new ArgumentException(errorMessage);
        return normalized;
    }

    private static string? NormalizeOptionalValue(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>
    /// Builds a RagStore backed by the provided <see cref="IVectorStore"/>
    /// and sets it on <paramref name="ragState"/>.  Returns a warning string when the
    /// pipeline could not be wired up (e.g. missing embedding key) or when the build
    /// itself fails.  On failure the previous ragState.Store is cleared so that
    /// subsequent queries do NOT silently fall back to a stale InMemory store.
    /// </summary>
    private static async Task<string?> TryAutoConnectRagStoreAsync(
        RagReferenceState ragState,
        HttpClient embeddingHttpClient,
        IVectorStore vectorStore,
        string? openAiApiKey,
        string? @namespace)
    {
        var settings = ragState.GetSettings();
        var embeddingKey = !string.IsNullOrWhiteSpace(openAiApiKey)
            ? openAiApiKey.Trim()
            : null;
        var epKey = NormalizeOptionalValue(settings.EmbeddingProvider)?.ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(epKey))
        {
            ragState.ClearStore();
            return "Embedding provider is required before connecting an external vector store.";
        }
        if (settings.EmbeddingDimensions <= 0)
        {
            ragState.ClearStore();
            return "Embedding dimensions are required before connecting an external vector store.";
        }
        if (string.IsNullOrWhiteSpace(settings.EmbeddingModel))
        {
            ragState.ClearStore();
            return "Embedding model is required before connecting an external vector store.";
        }

        if (epKey != "ollama" && embeddingKey == null)
        {
            // No embedding key → can't query. Clear stale store so queries don't
            // silently use an old InMemory-backed RagStore.
            ragState.ClearStore();
            return "No API key provided for the embedding provider. "
                + "RAG queries will not work until an OpenAI API key is configured in the Document Reference panel.";
        }

        try
        {
            var store = await RagStore.BuildAsync(builder =>
            {
                builder
                    .UseStore(vectorStore)
                    .WithTopK(settings.FinalFilter.TopK);

                builder.WithRetrievalMultiplier(settings.RetrievalDerivation.TopKMultiplier);

                if (!string.IsNullOrWhiteSpace(@namespace))
                    builder.WithNamespace(@namespace);

                if (epKey == "ollama")
                {
                    builder.UseEmbedding(new OllamaEmbeddingProvider(
                        embeddingHttpClient,
                        settings.EmbeddingModel,
                        settings.EmbeddingDimensions,
                        settings.EmbeddingBaseUrl));
                }
                else if (epKey == "vllm")
                {
                    builder.UseEmbedding(new VllmEmbeddingProvider(
                        embeddingHttpClient,
                        settings.EmbeddingModel,
                        settings.EmbeddingDimensions,
                        settings.EmbeddingBaseUrl));
                }
                else
                {
                    builder.UseEmbedding(new OpenAIEmbeddingProvider(
                        embeddingKey!, embeddingHttpClient,
                        settings.EmbeddingModel,
                        settings.EmbeddingDimensions));
                }

                if (settings.FinalFilter.MinScore.HasValue)
                    builder.WithScoreThreshold(settings.FinalFilter.MinScore.Value);
                if (settings.FinalFilter.MinScore.HasValue)
                    builder.WithRetrievalMinScore(
                        settings.FinalFilter.MinScore.Value / Math.Max(1d, settings.RetrievalDerivation.MinScoreDivider));
                if (!string.IsNullOrWhiteSpace(settings.PromptTemplate))
                    builder.WithPromptTemplate(settings.PromptTemplate);

                ApplyHybridAndReranker(builder, settings);
            });
            ragState.SetExternalStore(store);
            return null; // success — no warning
        }
        catch (Exception ex)
        {
            // Build failed — clear stale store so queries don't use an old InMemory store
            ragState.ClearStore();
            return $"Vector store connected but RAG pipeline setup failed: {HumanizeRagError(ex.Message)}";
        }
    }

    private static void ApplyHybridAndReranker(RagBuilder builder, RagPipelineSettings settings)
    {
        if (settings.HybridSearchEnabled)
            builder.UseHybridSearch(settings.HybridSearchVectorWeight);

        if (settings.RerankEnabled)
        {
            var provider = NormalizeOptionalValue(settings.RerankProvider)?.ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(provider))
                throw new InvalidOperationException("Rerank provider is required when reranking is enabled.");
            if (provider == "cohere")
            {
                if (!string.IsNullOrWhiteSpace(settings.RerankApiKey))
                    builder.WithReranker(new CohereReranker(settings.RerankApiKey, model: settings.RerankModel));
            }
            else if (provider == "vllm")
            {
                if (string.IsNullOrWhiteSpace(settings.RerankModel))
                    throw new InvalidOperationException("vLLM rerank model is required.");
                if (string.IsNullOrWhiteSpace(settings.RerankBaseUrl))
                    throw new InvalidOperationException("vLLM rerank base URL is required.");
                builder.WithReranker(new VllmReranker(
                    httpClient: new HttpClient(),
                    model: settings.RerankModel,
                    baseUrl: settings.RerankBaseUrl,
                    apiKey: settings.RerankApiKey));
            }
        }
    }

    private static bool? ParseOptionalBool(string? value)
        => bool.TryParse(value, out var parsed) ? parsed : null;

    private static int? ParseOptionalPositiveInt(string? value)
        => int.TryParse(value, out var parsed) && parsed > 0 ? parsed : null;

    private static float? ParseOptionalFloat(string? value)
        => float.TryParse(value, out var parsed) ? parsed : null;
}
