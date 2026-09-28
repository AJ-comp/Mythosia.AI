using Mythosia.Documents;
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

        app.MapPost("/api/rag/vector-store", async (VectorStoreConfigRequest req, CancellationToken ct) =>
        {
            ct.ThrowIfCancellationRequested();
            if ((req.Provider?.Trim().ToLowerInvariant() ?? "inmemory") == "inmemory")
            {
                ragState.ClearStore();
                return Results.Ok(new { provider = "inmemory", status = "switched" });
            }
            var requestedSettings = MergeEmbeddingSettings(ragState.GetSettings(), req);
            try { ValidateEmbeddingSettings(requestedSettings.EmbeddingProvider, requestedSettings.EmbeddingModel,
                requestedSettings.EmbeddingDimensions, requestedSettings.EmbeddingTimeoutSeconds, requestedSettings.EmbeddingMaxConcurrency); }
            catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
            if (req.Dimension.HasValue && req.Dimension.Value != requestedSettings.EmbeddingDimensions)
                return Results.BadRequest(new { error = "The vector store dimension must match the embedding dimension. Use a compatible table or collection." });
            string connectionIdentity;
            try { connectionIdentity = GetVectorStoreIdentity(req); }
            catch (ArgumentException) { return Results.BadRequest(new { error = "The vector store connection configuration is invalid." }); }
            if (connectionIdentity == ragState.ActiveVectorStoreIdentity && ragState.RequiresReindexFor(requestedSettings))
                return ReindexRequired();
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

            // Verify the store can actually reach its backend before claiming "connected".
            try
            {
                await VerifyStoreConnectionAsync(buildResult, req, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                if (buildResult.Store is IDisposable canceledStore) canceledStore.Dispose();
                throw;
            }
            catch (Exception ex)
            {
                if (buildResult.Store is IDisposable connDisposable)
                    connDisposable.Dispose();
                return Results.BadRequest(new { error = $"Connection failed: {ex.Message}" });
            }

            // Schema / collection validation — non-fatal warnings
            List<string> schemaWarnings;
            try
            {
                schemaWarnings = await ValidateStoreSchemaAsync(buildResult, req, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                if (buildResult.Store is IDisposable canceledStore) canceledStore.Dispose();
                throw;
            }
            catch
            {
                schemaWarnings = new List<string>();
            }

            // Merge embedding settings from the request into ragState so that
            // TryAutoConnectRagStoreAsync can build the RAG pipeline even when
            // the user hasn't run an embed yet.
            ragState.UpdateSettings(requestedSettings);

            string? autoConnectWarning;
            try
            {
                autoConnectWarning = await TryAutoConnectRagStoreAsync(
                    ragState,
                    embeddingHttpClient,
                    buildResult.Store,
                    req.OpenAiApiKey,
                    req.PerplexityApiKey, req.VoyageApiKey, req.GeminiApiKey, connectionIdentity, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                if (buildResult.Store is IDisposable canceledStore) canceledStore.Dispose();
                throw;
            }
            catch (Exception ex)
            {
                if (buildResult.Store is IDisposable disposable)
                    disposable.Dispose();
                return Results.BadRequest(new { error = $"Failed to connect: {ex.Message}" });
            }

            if (autoConnectWarning != null && buildResult.Store is IDisposable warnDisposable)
                warnDisposable.Dispose();
            schemaWarnings.Add("Confirm that this database was indexed with the selected embedding provider, model, and dimensions. Existing vector metadata cannot verify the model automatically.");

            return buildResult.Provider switch
            {
                "postgres" => Results.Ok(new
                {
                    provider = buildResult.Provider,
                    status = "connected",
                    warning = autoConnectWarning,
                    schemaWarnings,
                    tableName = buildResult.TableName,
                    schemaName = buildResult.SchemaName,
                    dimension = buildResult.Dimension
                }),
                "qdrant" => Results.Ok(new
                {
                    provider = buildResult.Provider,
                    status = "connected",
                    warning = autoConnectWarning,
                    schemaWarnings,
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
                    schemaWarnings,
                    indexHost = buildResult.IndexHost
                }),
                _ => Results.Ok(new { provider = buildResult.Provider, status = "connected", warning = autoConnectWarning, schemaWarnings })
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
            var dimensions = req.Dimensions is > 0 ? req.Dimensions.Value : 1024;

            try
            {
                var healthRes = await embeddingHttpClient.GetAsync($"{baseUrl}/health");
                if (!healthRes.IsSuccessStatusCode)
                    return Results.BadRequest(new { error = $"vLLM is not reachable at {baseUrl} (HTTP {(int)healthRes.StatusCode})." });

                var provider = new VllmEmbeddingProvider(embeddingHttpClient, model, dimensions, baseUrl);
                await provider.GetEmbeddingAsync("connection test");

                return Results.Ok(new { status = "ok", baseUrl, model, dimensions, modelFound = true });
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

                await reranker.RerankAsync("test query", sampleResults);

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
                requiresReindex = ragState.RequiresReindex,
                indexedEmbedding = ragState.IndexedEmbedding,
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
            var timeoutText = NormalizeOptionalValue(form["embeddingTimeoutSeconds"]);
            var concurrencyText = NormalizeOptionalValue(form["embeddingMaxConcurrency"]);
            var embeddingTimeoutSeconds = timeoutText == null ? 120 : ParseOptionalPositiveInt(timeoutText) ?? 0;
            var embeddingMaxConcurrency = concurrencyText == null ? 4 : ParseOptionalPositiveInt(concurrencyText) ?? 0;
            var topK = ParseOptionalPositiveInt(form["finalFilterTopK"]);
            if (topK is not > 0)
                return Results.BadRequest(new { error = "TopK is required." });
            var minScore = ParseOptionalDouble(form["finalFilterMinScore"]);
            var retrievalMinScoreDivider = ParseOptionalDouble(form["retrievalDerivationMinScoreDivider"]);
            var promptTemplate = string.IsNullOrWhiteSpace(form["promptTemplate"])
                ? null
                : form["promptTemplate"].ToString();
            var queryRewriterEnabled = ParseOptionalBool(form["queryRewriterEnabled"]);
            var extractKeywords = ParseOptionalBool(form["extractKeywords"]) ?? true;
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
            var perplexityApiKey = NormalizeOptionalValue(form["perplexityApiKey"]);
            var voyageApiKey = NormalizeOptionalValue(form["voyageApiKey"]);
            var geminiApiKey = NormalizeOptionalValue(form["geminiApiKey"]);
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

            IEmbeddingProvider embeddingProvider;
            try
            {
                embeddingProvider = BuildRagEmbeddingProvider(embeddingProviderKey, openAiApiKey, perplexityApiKey,
                    embeddingHttpClient, embeddingModel, embeddingDimensionsValue, embeddingBaseUrl,
                    voyageApiKey, geminiApiKey, embeddingTimeoutSeconds, embeddingMaxConcurrency);
            }
            catch (Exception exception) when (exception is ArgumentException || exception is InvalidOperationException)
            {
                return Results.BadRequest(new { error = exception.Message });
            }

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
                ExtractKeywords: extractKeywords,
                RewriterModelOverride: rewriterModelOverride,
                HybridSearchEnabled: hybridSearchEnabledValue,
                HybridSearchVectorWeight: hybridSearchVectorWeightValue,
                RerankEnabled: rerankEnabledValue,
                RerankProvider: rerankProvider,
                RerankModel: rerankModel ?? string.Empty,
                RerankBaseUrl: rerankBaseUrl ?? string.Empty,
                RerankApiKey: rerankApiKey,
                EmbeddingTimeoutSeconds: embeddingTimeoutSeconds,
                EmbeddingMaxConcurrency: embeddingMaxConcurrency);

            var vectorStoreRequest = ParseVectorStoreConfig(form);
            var connectionIdentity = GetVectorStoreIdentity(vectorStoreRequest);
            if ((NormalizeOptionalValue(vectorStoreRequest.Provider)?.ToLowerInvariant() ?? "inmemory") != "inmemory"
                && connectionIdentity == ragState.ActiveVectorStoreIdentity
                && ragState.RequiresReindexFor(requestSettings)) return ReindexRequired();
            if (vectorStoreRequest.Dimension.HasValue && vectorStoreRequest.Dimension.Value != embeddingDimensionsValue)
                return Results.BadRequest(new { error = "The vector store dimension must match the embedding dimension. Use a compatible table or collection." });
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

            var documents = new List<DoclingDocument>();
            var chunks = new List<RagChunk>();
            var records = new List<VectorRecord>();

            var splitter = new TrackingTextSplitter(BuildTextSplitter(chunkerKey, chunkSizeValue, chunkOverlapValue), chunks);
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
                    var fileDirectory = Path.Combine(tempRoot, savedFiles.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    Directory.CreateDirectory(fileDirectory);
                    var filePath = Path.Combine(fileDirectory, safeName);

                    await using var stream = File.Create(filePath);
                    await file.CopyToAsync(stream, ctx.RequestAborted);

                    savedFiles.Add((filePath, safeName));
                }

                if (savedFiles.Count == 0)
                    return Results.BadRequest(new { error = "Uploaded files were empty." });

                // Validate search/reranker configuration before any document is persisted.
                var store = await BuildQueryStoreAsync(vectorStoreResult.Store, embeddingProvider, requestSettings, ctx.RequestAborted);
                await RagStore.BuildAsync(builder =>
                {
                    builder
                        .WithTextSplitter(splitter)
                        .WithTopK(topKValue)
                        .UseEmbedding(embeddingProvider)
                        .UseStore(trackingStore);

                    if (requestSettings.FinalFilter.MinScore.HasValue)
                        builder.WithScoreThreshold(requestSettings.FinalFilter.MinScore.Value);
                    builder.WithRetrievalMultiplier(requestSettings.RetrievalDerivation.TopKMultiplier);
                    if (requestSettings.FinalFilter.MinScore.HasValue)
                        builder.WithRetrievalMinScore(
                            requestSettings.FinalFilter.MinScore.Value / Math.Max(1d, requestSettings.RetrievalDerivation.MinScoreDivider));

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
                }, onDocumentEmbedded: null, ctx.RequestAborted);

                ctx.RequestAborted.ThrowIfCancellationRequested();
                // The tracking wrapper is only for ingestion. Search uses the original store
                // so native hybrid, text-search, and diagnostics capabilities remain visible.
                var trace = RagReferenceTraceBuilder.Build(DoclingDocumentConverter.ToRagDocuments(documents), chunks, records, embeddingProvider.Dimensions);
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
                    promptTemplate, embeddingTimeoutSeconds, embeddingMaxConcurrency);
                ctx.RequestAborted.ThrowIfCancellationRequested();
                ragState.Update(store, trace, config, vectorStoreResult.Store, connectionIdentity, requestSettings,
                    GetCredentialFingerprint(embeddingProviderKey, openAiApiKey, perplexityApiKey, voyageApiKey, geminiApiKey));
                ragState.UpdateSettings(requestSettings);
                ragState.TryApplyQuerySettings(requestSettings);
                shouldDisposeStore = false;
                return Results.Ok(trace);
            }
            catch (OperationCanceledException) when (ctx.RequestAborted.IsCancellationRequested) { throw; }
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
    private record VllmTestRequest(string? BaseUrl, string? Model, int? Dimensions);
    private record VllmRerankTestRequest(string? BaseUrl, string? Model, string? ApiKey);

    private sealed record VectorStoreBuildResult(
        string Provider,
        IVectorStore Store,
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
        VectorStoreConfigRequest? vectorStoreRequest, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateEmbeddingSettings(settings.EmbeddingProvider, settings.EmbeddingModel, settings.EmbeddingDimensions,
            settings.EmbeddingTimeoutSeconds, settings.EmbeddingMaxConcurrency);
        var provider = NormalizeOptionalValue(vectorStoreRequest?.Provider)?.ToLowerInvariant() ?? "inmemory";
        var identity = vectorStoreRequest == null ? "inmemory" : GetVectorStoreIdentity(vectorStoreRequest);
        if ((provider == "inmemory" || identity == ragState.ActiveVectorStoreIdentity) && ragState.RequiresReindexFor(settings))
            throw new RagReindexRequiredException();
        ragState.UpdateSettings(settings);
        var fingerprint = GetCredentialFingerprint(settings.EmbeddingProvider, vectorStoreRequest?.OpenAiApiKey,
            vectorStoreRequest?.PerplexityApiKey, vectorStoreRequest?.VoyageApiKey, vectorStoreRequest?.GeminiApiKey);
        var active = ragState.ActiveSettings;
        if (identity == ragState.ActiveVectorStoreIdentity && active != null && ragState.HasStore
            && active.EmbeddingTimeoutSeconds == settings.EmbeddingTimeoutSeconds
            && active.EmbeddingMaxConcurrency == settings.EmbeddingMaxConcurrency
            && active.RerankEnabled == settings.RerankEnabled
            && active.RerankProvider == settings.RerankProvider
            && active.RerankModel == settings.RerankModel
            && active.RerankBaseUrl == settings.RerankBaseUrl
            && active.RerankApiKey == settings.RerankApiKey
            && fingerprint == ragState.ActiveCredentialFingerprint)
            return null;
        if (provider == "inmemory" && (ragState.ActiveVectorStore == null || !ragState.HasStore)) return null;

        var existingBackend = identity == ragState.ActiveVectorStoreIdentity ? ragState.ActiveVectorStore : null;
        var backend = existingBackend ?? BuildVectorStore(vectorStoreRequest!).Store;
        if (vectorStoreRequest?.Dimension.HasValue == true && vectorStoreRequest.Dimension != settings.EmbeddingDimensions)
        {
            if (existingBackend == null && backend is IDisposable invalidBackend) invalidBackend.Dispose();
            throw new ArgumentException("The vector store dimension must match the embedding dimension.");
        }
        var connected = false;
        try
        {
            var warning = await TryAutoConnectRagStoreAsync(ragState, embeddingHttpClient, backend,
                vectorStoreRequest?.OpenAiApiKey, vectorStoreRequest?.PerplexityApiKey,
                vectorStoreRequest?.VoyageApiKey, vectorStoreRequest?.GeminiApiKey, identity, cancellationToken);
            connected = warning == null;
            return warning;
        }
        finally
        {
            if (!connected && existingBackend == null && backend is IDisposable disposable) disposable.Dispose();
        }
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

    private static async Task VerifyStoreConnectionAsync(VectorStoreBuildResult buildResult, VectorStoreConfigRequest req, CancellationToken cancellationToken)
    {
        await buildResult.Store.VerifyConnectionAsync(cancellationToken);
    }

    private static RagPipelineSettings MergeEmbeddingSettings(RagPipelineSettings current, VectorStoreConfigRequest req)
    {
        var ep = NormalizeOptionalValue(req.EmbeddingProvider);
        return current with
        {
            EmbeddingProvider = ep?.ToLowerInvariant() ?? current.EmbeddingProvider,
            EmbeddingModel = NormalizeOptionalValue(req.EmbeddingModel) ?? current.EmbeddingModel,
            EmbeddingDimensions = req.EmbeddingDimensions ?? current.EmbeddingDimensions,
            EmbeddingBaseUrl = NormalizeOptionalValue(req.EmbeddingBaseUrl) ?? current.EmbeddingBaseUrl,
            EmbeddingTimeoutSeconds = req.EmbeddingTimeoutSeconds ?? current.EmbeddingTimeoutSeconds,
            EmbeddingMaxConcurrency = req.EmbeddingMaxConcurrency ?? current.EmbeddingMaxConcurrency
        };
    }

    internal static IResult ReindexRequired()
        => Results.Json(new { code = "REINDEX_REQUIRED", error = RagReindexRequiredException.MessageText }, statusCode: StatusCodes.Status409Conflict);

    internal static string GetVectorStoreIdentity(VectorStoreConfigRequest req)
    {
        var provider = req.Provider?.Trim().ToLowerInvariant() ?? "inmemory";
        if (provider == "inmemory") return provider;
        // Compare the effective dataset, independent of credential rotation, connection
        // string ordering, and unused settings belonging to another backend.
        object dataset;
        if (provider == "postgres")
        {
            var connection = new Npgsql.NpgsqlConnectionStringBuilder(req.ConnectionString);
            dataset = new { provider, host = connection.Host?.Trim().ToLowerInvariant(), port = connection.Port,
                database = string.IsNullOrEmpty(connection.Database) ? connection.Username : connection.Database,
                schema = req.SchemaName?.Trim(), table = req.TableName?.Trim() };
        }
        else if (provider == "qdrant")
            dataset = new { provider, host = NormalizeHost(req.QdrantHost)?.ToLowerInvariant(), port = req.QdrantPort ?? 6334,
                collection = req.QdrantCollectionName?.Trim() };
        else
            dataset = new { provider, host = req.PineconeIndexHost?.Trim().TrimEnd('/').ToLowerInvariant(),
                scope = req.PineconeNamespace?.Trim() ?? string.Empty };
        var identity = System.Text.Json.JsonSerializer.Serialize(dataset);
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(identity)));
    }

    private static string GetCredentialFingerprint(string provider, string? openAi, string? perplexity, string? voyage, string? gemini)
    {
        var key = provider.Trim().ToLowerInvariant() switch { "openai" => openAi, "perplexity" => perplexity, "voyage" => voyage, "gemini" => gemini, _ => null };
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(key?.Trim() ?? string.Empty)));
    }

    private static async Task<List<string>> ValidateStoreSchemaAsync(VectorStoreBuildResult buildResult, VectorStoreConfigRequest req, CancellationToken cancellationToken)
    {
        var warnings = new List<string>();
        var embeddingDimensions = req.EmbeddingDimensions;

        // Cross-check: vector store dimension vs embedding model dimension
        if (buildResult.Dimension.HasValue && embeddingDimensions is > 0
            && buildResult.Dimension.Value != embeddingDimensions.Value)
        {
            warnings.Add($"Dimension mismatch: the vector store is configured with dimension {buildResult.Dimension.Value}, but the embedding model produces {embeddingDimensions.Value}-dimensional vectors. Queries will fail at runtime.");
        }

        if (buildResult.Provider == "postgres")
        {
            var connectionString = NormalizeOptionalValue(req.ConnectionString);
            if (string.IsNullOrWhiteSpace(connectionString)) return warnings;

            using var conn = new Npgsql.NpgsqlConnection(connectionString);
            await conn.OpenAsync(cancellationToken);

            // Check table existence
            using var existsCmd = conn.CreateCommand();
            existsCmd.CommandText = @"
SELECT 1
FROM information_schema.tables
WHERE table_schema = @schema AND table_name = @table";
            existsCmd.Parameters.AddWithValue("@schema", buildResult.SchemaName ?? "public");
            existsCmd.Parameters.AddWithValue("@table", buildResult.TableName ?? "vectors");

            var exists = await existsCmd.ExecuteScalarAsync(cancellationToken);
            if (exists == null)
            {
                if (req.EnsureSchema == true)
                    warnings.Add($"Table \"{buildResult.SchemaName}\".\"{buildResult.TableName}\" does not exist yet. It will be auto-created on first embed (EnsureSchema is on).");
                else
                    warnings.Add($"Table \"{buildResult.SchemaName}\".\"{buildResult.TableName}\" does not exist. Enable EnsureSchema or create the table manually.");
            }
            else if (buildResult.Dimension.HasValue || embeddingDimensions is > 0)
            {
                // Check vector column dimension
                using var dimCmd = conn.CreateCommand();
                dimCmd.CommandText = @"
SELECT a.atttypmod
FROM pg_attribute a
JOIN pg_class c ON a.attrelid = c.oid
JOIN pg_namespace n ON c.relnamespace = n.oid
WHERE n.nspname = @schema AND c.relname = @table AND a.attname = 'embedding' AND a.atttypmod > 0";
                dimCmd.Parameters.AddWithValue("@schema", buildResult.SchemaName ?? "public");
                dimCmd.Parameters.AddWithValue("@table", buildResult.TableName ?? "vectors");

                var dimResult = await dimCmd.ExecuteScalarAsync(cancellationToken);
                if (dimResult is int existingDim)
                {
                    if (buildResult.Dimension.HasValue && existingDim != buildResult.Dimension.Value)
                        warnings.Add($"Dimension mismatch: table \"{buildResult.SchemaName}\".\"{buildResult.TableName}\" has vector dimension {existingDim}, but you specified {buildResult.Dimension.Value}.");
                    if (embeddingDimensions is > 0 && existingDim != embeddingDimensions.Value)
                        warnings.Add($"Dimension mismatch: table \"{buildResult.SchemaName}\".\"{buildResult.TableName}\" has vector dimension {existingDim}, but the embedding model produces {embeddingDimensions.Value}-dimensional vectors. Queries will fail at runtime.");
                }
            }
        }
        else if (buildResult.Provider == "qdrant")
        {
            var host = NormalizeHost(req.QdrantHost);
            if (string.IsNullOrWhiteSpace(host)) return warnings;
            var port = req.QdrantPort ?? 6334;
            var useTls = req.QdrantUseTls ?? false;
            var collectionName = NormalizeOptionalValue(req.QdrantCollectionName);
            if (string.IsNullOrWhiteSpace(collectionName)) return warnings;

            using var client = new Qdrant.Client.QdrantClient(host, port, useTls, NormalizeOptionalValue(req.QdrantApiKey));
            var collections = await client.ListCollectionsAsync(cancellationToken);
            var found = collections.Any(c => c == collectionName);
            if (!found)
            {
                warnings.Add($"Collection \"{collectionName}\" does not exist yet. It will be auto-created on first embed.");
            }
            else if (buildResult.Dimension.HasValue || embeddingDimensions is > 0)
            {
                try
                {
                    var info = await client.GetCollectionInfoAsync(collectionName, cancellationToken: cancellationToken);
                    var existingDim = info.Config.Params.VectorsConfig?.Params?.Size;
                    if (existingDim.HasValue)
                    {
                        if (buildResult.Dimension.HasValue && existingDim.Value != (ulong)buildResult.Dimension.Value)
                            warnings.Add($"Dimension mismatch: collection \"{collectionName}\" has vector dimension {existingDim.Value}, but you specified {buildResult.Dimension.Value}.");
                        if (embeddingDimensions is > 0 && existingDim.Value != (ulong)embeddingDimensions.Value)
                            warnings.Add($"Dimension mismatch: collection \"{collectionName}\" has vector dimension {existingDim.Value}, but the embedding model produces {embeddingDimensions.Value}-dimensional vectors. Queries will fail at runtime.");
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch { /* unable to read collection info — skip */ }
            }
        }

        return warnings;
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

            var ns = NormalizeOptionalValue(req.PineconeNamespace);
            var store = new PineconeStore(new PineconeOptions
            {
                IndexHost = indexHost,
                ApiKey = apiKey,
                Namespace = ns
            });

            return new VectorStoreBuildResult(
                Provider: "pinecone",
                Store: store,
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
    /// itself fails. Failures preserve the last successful store; callers must block
    /// the requested query when a warning is returned rather than silently using it.
    /// </summary>
    private static async Task<string?> TryAutoConnectRagStoreAsync(
        RagReferenceState ragState,
        HttpClient embeddingHttpClient,
        IVectorStore vectorStore,
        string? openAiApiKey,
        string? perplexityApiKey, string? voyageApiKey, string? geminiApiKey,
        string connectionIdentity, CancellationToken cancellationToken)
    {
        var settings = ragState.GetSettings();
        var epKey = NormalizeOptionalValue(settings.EmbeddingProvider)?.ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(epKey))
        {
            return "Embedding provider is required before connecting an external vector store.";
        }
        if (settings.EmbeddingDimensions <= 0)
        {
            return "Embedding dimensions are required before connecting an external vector store.";
        }
        if (string.IsNullOrWhiteSpace(settings.EmbeddingModel))
        {
            return "Embedding model is required before connecting an external vector store.";
        }

        var embeddingKey = epKey switch { "perplexity" => perplexityApiKey, "voyage" => voyageApiKey, "gemini" => geminiApiKey, _ => openAiApiKey };
        if ((epKey is "openai" or "perplexity" or "voyage" or "gemini") && string.IsNullOrWhiteSpace(embeddingKey))
        {
            // A failed reconfiguration must not discard an existing in-memory index.
            // Callers treat this warning as a blocked query, never a silent fallback.
            return "No API key provided for the embedding provider. "
                + $"RAG queries will not work until a {epKey} API key is configured in the Document Reference panel.";
        }

        try
        {
            var embedding = BuildRagEmbeddingProvider(epKey, openAiApiKey, perplexityApiKey,
                embeddingHttpClient, settings.EmbeddingModel, settings.EmbeddingDimensions, settings.EmbeddingBaseUrl,
                voyageApiKey, geminiApiKey, settings.EmbeddingTimeoutSeconds, settings.EmbeddingMaxConcurrency);
            var store = await BuildQueryStoreAsync(vectorStore, embedding, settings, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            ragState.SetExternalStore(store, settings, vectorStore, connectionIdentity,
                GetCredentialFingerprint(epKey, openAiApiKey, perplexityApiKey, voyageApiKey, geminiApiKey));
            return null; // success — no warning
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            return $"Vector store connected but RAG pipeline setup failed: {HumanizeRagError(ex.Message)}";
        }
    }

    internal static Task<RagStore> BuildQueryStoreAsync(IVectorStore vectorStore, IEmbeddingProvider embedding,
        RagPipelineSettings settings, CancellationToken cancellationToken)
        => RagStore.BuildAsync(builder =>
        {
            builder.UseStore(vectorStore).UseEmbedding(embedding).WithTopK(settings.FinalFilter.TopK);
            builder.WithRetrievalMultiplier(settings.RetrievalDerivation.TopKMultiplier);
            if (settings.FinalFilter.MinScore.HasValue)
            {
                builder.WithScoreThreshold(settings.FinalFilter.MinScore.Value);
                builder.WithRetrievalMinScore(settings.FinalFilter.MinScore.Value / Math.Max(1d, settings.RetrievalDerivation.MinScoreDivider));
            }
            if (!string.IsNullOrWhiteSpace(settings.PromptTemplate)) builder.WithPromptTemplate(settings.PromptTemplate);
            ApplyHybridAndReranker(builder, settings);
        }, cancellationToken: cancellationToken);

    private static void ApplyHybridAndReranker(RagBuilder builder, RagPipelineSettings settings)
    {
        if (settings.HybridSearchEnabled)
            builder.UseHybridSearch(settings.HybridSearchVectorWeight);

        builder.WithFinalSelectionPolicy(settings.FinalSelectionMode, settings.FinalSelectionRetrievalWeight);

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

internal sealed class RagReindexRequiredException() : InvalidOperationException(MessageText)
{
    internal const string MessageText = "The selected embedding provider, model, dimensions, or endpoint differs from the indexed vectors. Re-index the documents before searching. For an external database, use a new table or collection, or restore the original embedding settings; uploading only some documents cannot safely migrate the existing vector space.";
}
