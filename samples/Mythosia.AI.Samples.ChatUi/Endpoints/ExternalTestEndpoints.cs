using Mythosia.AI.Models;
using Mythosia.AI.Models.Enums;
using Mythosia.AI.Models.Messages;
using Mythosia.AI.Models.Streaming;
using Mythosia.AI.Rag;
using Mythosia.AI.Services.Base;
using System.Diagnostics;
using System.Text;
using static Mythosia.AI.Samples.ChatUi.ChatUiModelHelpers;
using static Mythosia.AI.Samples.ChatUi.ChatUiUtilityHelpers;

namespace Mythosia.AI.Samples.ChatUi;

internal static class ExternalTestEndpoints
{
    public static void MapExternalTestEndpoints(
        this WebApplication app,
        Func<AIService?> getCurrentService,
        Func<string?> getCurrentProvider,
        RagReferenceState ragState,
        ChatUiRagEndpointState ragEndpointState,
        HttpClient embeddingHttpClient)
    {
        app.MapGet("/api/test/models", () => Results.Ok(BuildModelCatalogue()));

        app.MapGet("/api/test/rag/status", () =>
        {
            return Results.Ok(new
            {
                hasStore = ragState.Store != null,
                hasReference = ragState.TryGetSnapshot(out _, out _),
                historyCount = ragState.GetHistory().Count
            });
        });

        app.MapPost("/api/test/chat", async (ExternalChatRequest req, CancellationToken ct) =>
        {
            var currentService = getCurrentService();
            if (currentService == null)
                return Results.BadRequest(new { error = "Service not configured. Call POST /api/configure first." });

            if (string.IsNullOrWhiteSpace(req.Message))
                return Results.BadRequest(new { error = "message is required." });

            if (req.RagSettings == null)
                return Results.BadRequest(new { error = "ragSettings is required." });

            var ragReq = req.RagSettings;

            if (string.IsNullOrWhiteSpace(ragReq.EmbeddingProvider))
                return Results.BadRequest(new { error = "ragSettings.embeddingProvider is required." });

            if (string.IsNullOrWhiteSpace(ragReq.EmbeddingModel))
                return Results.BadRequest(new { error = "ragSettings.embeddingModel is required." });

            if (ragReq.EmbeddingDimensions is not > 0)
                return Results.BadRequest(new { error = "ragSettings.embeddingDimensions must be a positive integer." });

            var effectiveRagSettings = new RagPipelineSettings(
                ChunkSize: ragReq.ChunkSize is > 0 ? ragReq.ChunkSize.Value : 300,
                ChunkOverlap: ragReq.ChunkOverlap is >= 0 ? ragReq.ChunkOverlap.Value : 30,
                Chunker: string.IsNullOrWhiteSpace(ragReq.Chunker) ? "recursive" : ragReq.Chunker.Trim().ToLowerInvariant(),
                EmbeddingProvider: ragReq.EmbeddingProvider.Trim().ToLowerInvariant(),
                EmbeddingModel: ragReq.EmbeddingModel.Trim(),
                EmbeddingDimensions: ragReq.EmbeddingDimensions.Value,
                EmbeddingBaseUrl: string.IsNullOrWhiteSpace(ragReq.EmbeddingBaseUrl) ? string.Empty : ragReq.EmbeddingBaseUrl.Trim(),
                FinalFilter: new RagFilter
                {
                    TopK = ragReq.FinalFilter?.TopK > 0 ? ragReq.FinalFilter.TopK : 5,
                    MinScore = ragReq.FinalFilter?.MinScore ?? 0.2
                },
                RetrievalDerivation: new RagRetrievalDerivation
                {
                    TopKMultiplier = ragReq.RetrievalDerivation?.TopKMultiplier > 0 ? ragReq.RetrievalDerivation.TopKMultiplier : 3,
                    MinScoreDivider = ragReq.RetrievalDerivation?.MinScoreDivider > 0d ? ragReq.RetrievalDerivation.MinScoreDivider : 2d
                },
                PromptTemplate: ragReq.PromptTemplate,
                QueryRewriterEnabled: ragReq.QueryRewriterEnabled ?? false,
                QueryRewriteMaxTokens: ragReq.QueryRewriteMaxTokens is > 0 ? ragReq.QueryRewriteMaxTokens.Value : 250,
                ExtractKeywords: ragReq.ExtractKeywords ?? true,
                RewriterModelOverride: ragReq.RewriterModelOverride,
                HybridSearchEnabled: ragReq.HybridSearchEnabled ?? false,
                HybridSearchVectorWeight: ragReq.HybridSearchVectorWeight ?? 0.5f,
                RerankEnabled: ragReq.RerankEnabled ?? false,
                RerankProvider: string.IsNullOrWhiteSpace(ragReq.RerankProvider) ? "" : ragReq.RerankProvider.Trim().ToLowerInvariant(),
                RerankModel: string.IsNullOrWhiteSpace(ragReq.RerankModel) ? "" : ragReq.RerankModel.Trim(),
                RerankBaseUrl: string.IsNullOrWhiteSpace(ragReq.RerankBaseUrl) ? "" : ragReq.RerankBaseUrl.Trim(),
                RerankApiKey: ragReq.RerankApiKey,
                FinalSelectionMode: ParseFinalSelectionMode(ragReq.FinalSelection?.Mode) ?? RagFinalSelectionMode.RerankerOnly,
                FinalSelectionRetrievalWeight: ragReq.FinalSelection?.RetrievalWeight ?? RagFinalSelectionOptions.DefaultRetrievalWeight);

            if (ragReq.RewriterApiKey != null)
                ragEndpointState.RewriterApiKey = string.IsNullOrWhiteSpace(ragReq.RewriterApiKey)
                    ? null
                    : ragReq.RewriterApiKey;

            ragState.UpdateSettings(effectiveRagSettings);
            ragState.TryApplyQuerySettings(effectiveRagSettings);

            var vectorStoreProvider = string.IsNullOrWhiteSpace(req.VectorStore?.Provider)
                ? "inmemory"
                : req.VectorStore!.Provider!.Trim().ToLowerInvariant();

            try
            {
                var externalStoreWarning = await ChatUiRagCoreEndpoints.EnsureExternalStoreMatchesSettingsAsync(
                    ragState, embeddingHttpClient, effectiveRagSettings, req.VectorStore);
                if (!string.IsNullOrWhiteSpace(externalStoreWarning))
                    Console.WriteLine($"[TestAPI][RAG] External store refresh warning: {externalStoreWarning}");
            }
            catch (Exception syncEx)
            {
                return Results.BadRequest(new { error = syncEx.Message });
            }

            try
            {
                // ── RAG retrieval ──
                RagProcessedQuery? ragProcessed = null;
                string? rewriterModelName = null;
                object? ragDiagnostics = null;

                if (ragState.Store != null)
                {
                    var ragSw = Stopwatch.StartNew();
                    var ragOptions = new RagQueryOptions
                    {
                        FinalFilter = effectiveRagSettings.FinalFilter,
                        RetrievalDerivation = effectiveRagSettings.RetrievalDerivation,
                        FinalSelection = new RagFinalSelectionOptions
                        {
                            Mode = effectiveRagSettings.FinalSelectionMode,
                            RetrievalWeight = effectiveRagSettings.FinalSelectionRetrievalWeight
                        }
                    };

                    if (effectiveRagSettings.QueryRewriterEnabled)
                    {
                        var rewriterService = ragEndpointState.GetOrCreateRewriterService(
                            effectiveRagSettings.RewriterModelOverride, currentService);
                        rewriterModelName = rewriterService.Model;
                        var rewriteMaxTokens = (uint)Math.Max(1, effectiveRagSettings.QueryRewriteMaxTokens);
                        ragState.Store.SetQueryRewriter(new LlmQueryRewriter(rewriterService, rewriteMaxTokens, effectiveRagSettings.ExtractKeywords));
                    }
                    else
                    {
                        ragState.Store.SetQueryRewriter(null);
                    }

                    var ragService = currentService.WithRag(ragState.Store);
                    ragProcessed = await ragService.RetrieveAsync(req.Message, ragOptions, ct);

                    if (ragProcessed is { HasReferences: false })
                        ragProcessed = null;

                    ragDiagnostics = BuildRagDiagnosticsResponse(
                        ragProcessed, effectiveRagSettings, vectorStoreProvider, rewriterModelName);
                }

                // ── LLM call (non-streaming) ──
                var message = new Message(ActorRole.User, req.Message);
                AIRequestContext? requestContext = null;
                if (ragProcessed != null)
                {
                    message.Metadata = new Dictionary<string, object>
                    {
                        ["rag"] = true,
                        ["rag_original_query"] = req.Message,
                        ["rag_reference_count"] = ragProcessed.References.Count
                    };
                    requestContext = new AIRequestContext
                    {
                        RequestMessageOverride = new Message(ActorRole.User, ragProcessed.RequestMessageContent)
                    };
                }

                var responseText = new StringBuilder();
                var reasoningText = new StringBuilder();
                var options = new StreamOptions
                {
                    IncludeReasoning = true,
                    IncludeMetadata = true,
                    IncludeFunctionCalls = currentService.ShouldUseFunctions,
                    TextOnly = false
                };

                var stream = currentService.StreamAsync(message, options, requestContext, ct);

                await foreach (var sc in stream)
                {
                    switch (sc.Type)
                    {
                        case StreamingContentType.Text:
                            if (!string.IsNullOrEmpty(sc.Content))
                                responseText.Append(sc.Content);
                            break;
                        case StreamingContentType.Reasoning:
                            if (!string.IsNullOrEmpty(sc.Content))
                                reasoningText.Append(sc.Content);
                            break;
                    }
                }

                return Results.Ok(new
                {
                    provider = getCurrentProvider(),
                    model = currentService.Model,
                    response = responseText.ToString(),
                    reasoning = reasoningText.Length > 0 ? reasoningText.ToString() : null,
                    rag = ragDiagnostics
                });
            }
            catch (OperationCanceledException)
            {
                return Results.StatusCode(499); // Client closed request
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });
    }

    private static RagFinalSelectionMode? ParseFinalSelectionMode(string? value) => value switch
    {
        "RerankerOnly" => RagFinalSelectionMode.RerankerOnly,
        "WeightedBlend" => RagFinalSelectionMode.WeightedBlend,
        _ => null,
    };

    private static object? BuildRagDiagnosticsResponse(
        RagProcessedQuery? ragProcessed,
        RagPipelineSettings settings,
        string vectorStoreProvider,
        string? rewriterModelName)
    {
        if (ragProcessed == null) return null;

        return new
        {
            augmentedPrompt = ragProcessed.RequestMessageContent,
            originalQuery = ragProcessed.OriginalQuery,
            rewrittenQuery = ragProcessed.RewrittenQuery,
            searchSkipped = ragProcessed.SearchSkipped,
            rewriteResult = ragProcessed.RewriteResult != null ? new
            {
                query = ragProcessed.RewriteResult.Query,
                needsSearch = ragProcessed.RewriteResult.NeedsSearch,
                keywords = ragProcessed.RewriteResult.Keywords
            } : null,
            searchKeywords = ragProcessed.SearchKeywords,
            rewriterModel = rewriterModelName,
            searchMode = settings.HybridSearchEnabled
                ? (ragProcessed.SearchKeywords != null && ragProcessed.SearchKeywords.Count > 0 ? "hybrid" : "hybrid_dense_fallback")
                : "vector",
            hybridWeight = settings.HybridSearchEnabled ? settings.HybridSearchVectorWeight : (float?)null,
            vectorStoreProvider,
            reranking = new
            {
                enabled = settings.RerankEnabled,
                provider = settings.RerankEnabled ? settings.RerankProvider : null,
                model = settings.RerankEnabled ? settings.RerankModel : null,
                retrievalMultiplier = settings.RerankEnabled ? settings.RetrievalDerivation.TopKMultiplier : (int?)null,
                finalSelectionMode = settings.FinalSelectionMode.ToString(),
                finalSelectionRetrievalWeight = settings.FinalSelectionRetrievalWeight
            },
            diagnostics = new
            {
                appliedNamespace = ragProcessed.Diagnostics.AppliedNamespace,
                finalTopK = ragProcessed.Diagnostics.FinalTopK,
                retrievalTopK = ragProcessed.Diagnostics.RetrievalTopK,
                appliedFinalMinScore = ragProcessed.Diagnostics.AppliedFinalMinScore,
                appliedRetrievalMinScore = ragProcessed.Diagnostics.AppliedRetrievalMinScore,
                elapsedMs = ragProcessed.Diagnostics.ElapsedMs,
                rewriteElapsedMs = ragProcessed.Diagnostics.RewriteElapsedMs
            },
            retrievalResults = ragProcessed.RetrievalCandidates.Select(r => new
            {
                id = r.Record.Id,
                score = r.Score,
                content = r.Record.Content,
                metadata = r.Record.Metadata
            }),
            rerankedCandidates = ragProcessed.RerankedCandidates?.Select(r => new
            {
                id = r.Record.Id,
                score = r.Score,
                content = r.Record.Content,
                metadata = r.Record.Metadata
            }),
            references = ragProcessed.References.Select(r => new
            {
                id = r.Record.Id,
                score = r.Score,
                content = r.Record.Content,
                metadata = r.Record.Metadata
            })
        };
    }
}
