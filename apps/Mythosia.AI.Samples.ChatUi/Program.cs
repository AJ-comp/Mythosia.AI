// 샘플앱은 진단 목적상 deprecated 노브(MaxMessageCount 등)도 그대로 노출한다 — v7.0 제거 시 함께 정리.
#pragma warning disable CS0618
using Mythosia.AI.Models;
using Mythosia.AI.Models.Enums;
using Mythosia.AI.Models.Messages;
using Mythosia.AI.Models.Streaming;
using Mythosia.AI.Providers.Alibaba;
using Mythosia.AI.Rag;
using Mythosia.AI.Samples.ChatUi;
using Mythosia.AI.Services.Anthropic;
using Mythosia.AI.Services.Base;
using Mythosia.AI.Services.DeepSeek;
using Mythosia.AI.Services.Google;
using Mythosia.AI.Services.OpenAI;
using Mythosia.AI.Services.Perplexity;
using Mythosia.AI.Services.xAI;
using System.Diagnostics;
using System.Text.Json;
using static Mythosia.AI.Samples.ChatUi.ChatUiModelHelpers;
using static Mythosia.AI.Samples.ChatUi.ChatUiUtilityHelpers;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        ctx.Context.Response.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate";
        ctx.Context.Response.Headers["Pragma"] = "no-cache";
        ctx.Context.Response.Headers["Expires"] = "0";
    }
});

// ── Shared state ────────────────────────────────────────────────
AIService? currentService = null;
string? currentProvider = null;
string? currentModelEnum = null;
bool streamIncludeReasoning = true;
bool presetFunctionsEnabled = true; // Whether preset functions are registered
var ragState = new RagReferenceState();
var ragEndpointState = new ChatUiRagEndpointState();
var embeddingHttpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };

// ── GET /api/models ─────────────────────────────────────────────
app.MapGet("/api/models", () => Results.Ok(BuildModelCatalogue()));

// ── POST /api/configure ─────────────────────────────────────────
app.MapPost("/api/configure", async (ConfigureRequest req) =>
{
    if (string.IsNullOrWhiteSpace(req.Model))
        return Results.BadRequest(new { error = "model is required" });

    var selectedModel = ChatUiModelHelpers.FindModelValueByName(req.Model) ?? req.Model.Trim();

    var provider = GetProviderForModel(selectedModel);

    if (provider != "Alibaba" && string.IsNullOrWhiteSpace(req.ApiKey))
        return Results.BadRequest(new { error = "apiKey is required" });

    var desc = selectedModel;

    try
    {
        var previousService = currentService;
        var httpClient = new HttpClient();
        currentService = provider switch
        {
            "OpenAI" => new OpenAIService(req.ApiKey, httpClient),
            "Anthropic" => new AnthropicService(req.ApiKey, httpClient),
            "Google" => new GoogleAIService(req.ApiKey, httpClient),
            "DeepSeek" => new DeepSeekService(req.ApiKey, httpClient),
            "xAI" => new XAIService(req.ApiKey, httpClient),
            "Perplexity" => new PerplexityService(req.ApiKey, httpClient),
            "Alibaba" => string.IsNullOrWhiteSpace(req.BaseUrl)
                ? new QwenService(req.ApiKey, httpClient)
                : new QwenService(req.BaseUrl, ParsePlatform(req.Platform), httpClient),
            _ => throw new NotSupportedException($"Provider {provider} not supported")
        };
        if (currentService is QwenService qwenService)
        {
            qwenService.ModelIdOverride = string.IsNullOrWhiteSpace(req.ModelIdOverride)
                ? null
                : req.ModelIdOverride.Trim();
        }
        currentService.ChangeModel(selectedModel);
        streamIncludeReasoning = true;

        // Carry over conversation history and settings from previous service
        if (previousService != null)
            currentService.CopyFrom(previousService);

        currentProvider = provider;
        currentModelEnum = req.Model;

        // Register preset functions if enabled
        if (presetFunctionsEnabled && !currentService.Functions.Any(f => f.Name == "get_url_content"))
            RegisterPresetFunctions(currentService);

        if (!string.IsNullOrWhiteSpace(req.SystemMessage))
            currentService.SystemMessage = req.SystemMessage;

        return Results.Ok(new { provider, model = desc, status = "configured" });
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

// ── POST /api/chat (streaming SSE) ──────────────────────────────
app.MapPost("/api/chat", async (ChatRequest req, HttpContext ctx) =>
{
    if (currentService == null)
    {
        ctx.Response.StatusCode = 400;
        ctx.Response.ContentType = "application/json";
        await ctx.Response.WriteAsJsonAsync(new { error = "Service not configured. Select a model and enter an API key first." });
        return;
    }

    if (string.IsNullOrWhiteSpace(req.Message))
    {
        ctx.Response.StatusCode = 400;
        ctx.Response.ContentType = "application/json";
        await ctx.Response.WriteAsJsonAsync(new { error = "message is required" });
        return;
    }

    var bufferingFeature = ctx.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpResponseBodyFeature>();
    bufferingFeature?.DisableBuffering();

    ctx.Response.ContentType = "text/event-stream";
    ctx.Response.Headers["Cache-Control"] = "no-cache";
    ctx.Response.Headers["X-Accel-Buffering"] = "no";
    ctx.Response.Headers["Connection"] = "keep-alive";

    var baseRagSettings = ragState.GetSettings();
    var requestRagSettings = req.RagSettings;
    if (requestRagSettings == null)
    {
        ctx.Response.StatusCode = 400;
        ctx.Response.ContentType = "application/json";
        await ctx.Response.WriteAsJsonAsync(new { error = "RAG settings are required for chat requests." });
        return;
    }

    if (string.IsNullOrWhiteSpace(requestRagSettings.EmbeddingProvider))
    {
        ctx.Response.StatusCode = 400;
        ctx.Response.ContentType = "application/json";
        await ctx.Response.WriteAsJsonAsync(new { error = "Embedding provider is required for chat requests." });
        return;
    }

    if (string.IsNullOrWhiteSpace(requestRagSettings.EmbeddingModel))
    {
        ctx.Response.StatusCode = 400;
        ctx.Response.ContentType = "application/json";
        await ctx.Response.WriteAsJsonAsync(new { error = "Embedding model is required for chat requests." });
        return;
    }

    if (requestRagSettings.EmbeddingDimensions is not > 0)
    {
        ctx.Response.StatusCode = 400;
        ctx.Response.ContentType = "application/json";
        await ctx.Response.WriteAsJsonAsync(new { error = "Embedding dimensions must be a positive integer for chat requests." });
        return;
    }

    var effectiveRagSettings = new RagPipelineSettings(
        ChunkSize: requestRagSettings.ChunkSize is > 0 ? requestRagSettings.ChunkSize.Value : baseRagSettings.ChunkSize,
        ChunkOverlap: requestRagSettings.ChunkOverlap is >= 0 ? requestRagSettings.ChunkOverlap.Value : baseRagSettings.ChunkOverlap,
        Chunker: string.IsNullOrWhiteSpace(requestRagSettings.Chunker) ? baseRagSettings.Chunker : requestRagSettings.Chunker.Trim().ToLowerInvariant(),
        EmbeddingProvider: requestRagSettings.EmbeddingProvider.Trim().ToLowerInvariant(),
        EmbeddingModel: requestRagSettings.EmbeddingModel.Trim(),
        EmbeddingDimensions: requestRagSettings.EmbeddingDimensions.Value,
        EmbeddingBaseUrl: string.IsNullOrWhiteSpace(requestRagSettings.EmbeddingBaseUrl) ? string.Empty : requestRagSettings.EmbeddingBaseUrl.Trim(),
        FinalFilter: new RagFilter
        {
            TopK = requestRagSettings.FinalFilter?.TopK > 0 ? requestRagSettings.FinalFilter.TopK : baseRagSettings.FinalFilter.TopK,
            MinScore = requestRagSettings.FinalFilter?.MinScore ?? baseRagSettings.FinalFilter.MinScore
        },
        RetrievalDerivation: new RagRetrievalDerivation
        {
            TopKMultiplier = requestRagSettings.RetrievalDerivation?.TopKMultiplier > 0 ? requestRagSettings.RetrievalDerivation.TopKMultiplier : baseRagSettings.RetrievalDerivation.TopKMultiplier,
            MinScoreDivider = requestRagSettings.RetrievalDerivation?.MinScoreDivider > 0d ? requestRagSettings.RetrievalDerivation.MinScoreDivider : baseRagSettings.RetrievalDerivation.MinScoreDivider
        },
        PromptTemplate: requestRagSettings.PromptTemplate ?? baseRagSettings.PromptTemplate,
        QueryRewriterEnabled: requestRagSettings.QueryRewriterEnabled ?? baseRagSettings.QueryRewriterEnabled,
        QueryRewriteMaxTokens: requestRagSettings.QueryRewriteMaxTokens is > 0 ? requestRagSettings.QueryRewriteMaxTokens.Value : baseRagSettings.QueryRewriteMaxTokens,
        ExtractKeywords: requestRagSettings.ExtractKeywords ?? baseRagSettings.ExtractKeywords,
        RewriterModelOverride: requestRagSettings.RewriterModelOverride ?? baseRagSettings.RewriterModelOverride,
        HybridSearchEnabled: requestRagSettings.HybridSearchEnabled ?? baseRagSettings.HybridSearchEnabled,
        HybridSearchVectorWeight: requestRagSettings.HybridSearchVectorWeight ?? baseRagSettings.HybridSearchVectorWeight,
        RerankEnabled: requestRagSettings.RerankEnabled ?? baseRagSettings.RerankEnabled,
        RerankProvider: string.IsNullOrWhiteSpace(requestRagSettings.RerankProvider) ? baseRagSettings.RerankProvider : requestRagSettings.RerankProvider.Trim().ToLowerInvariant(),
        RerankModel: string.IsNullOrWhiteSpace(requestRagSettings.RerankModel) ? baseRagSettings.RerankModel : requestRagSettings.RerankModel.Trim(),
        RerankBaseUrl: string.IsNullOrWhiteSpace(requestRagSettings.RerankBaseUrl) ? baseRagSettings.RerankBaseUrl : requestRagSettings.RerankBaseUrl.Trim(),
        RerankApiKey: requestRagSettings.RerankApiKey ?? baseRagSettings.RerankApiKey,
        FinalSelectionMode: ParseFinalSelectionMode(requestRagSettings.FinalSelection?.Mode) ?? baseRagSettings.FinalSelectionMode,
        FinalSelectionRetrievalWeight: requestRagSettings.FinalSelection?.RetrievalWeight ?? baseRagSettings.FinalSelectionRetrievalWeight);

    if (requestRagSettings?.RewriterApiKey != null)
        ragEndpointState.RewriterApiKey = string.IsNullOrWhiteSpace(requestRagSettings.RewriterApiKey)
            ? null
            : requestRagSettings.RewriterApiKey;

    if (requestRagSettings != null)
    {
        ragState.UpdateSettings(effectiveRagSettings);
        ragState.TryApplyQuerySettings(effectiveRagSettings);
    }

    var vectorStoreProvider = string.IsNullOrWhiteSpace(req.VectorStore?.Provider)
        ? "inmemory"
        : req.VectorStore!.Provider!.Trim().ToLowerInvariant();

    try
    {
        var externalStoreWarning = await ChatUiRagCoreEndpoints.EnsureExternalStoreMatchesSettingsAsync(
            ragState,
            embeddingHttpClient,
            effectiveRagSettings,
            req.VectorStore);
        if (!string.IsNullOrWhiteSpace(externalStoreWarning))
            Console.WriteLine($"[RAG] External store refresh warning: {externalStoreWarning}");
    }
    catch (Exception syncEx)
    {
        ctx.Response.StatusCode = 400;
        ctx.Response.ContentType = "application/json";
        await ctx.Response.WriteAsJsonAsync(new { error = syncEx.Message });
        return;
    }

    try
    {
        // Trigger summary policy before streaming (not called automatically in StreamAsync)
        var hasPolicy = currentService.ConversationPolicy != null;
        var isStateless = currentService.StatelessMode;
        var shouldSummarize = hasPolicy && currentService.ConversationPolicy!.ShouldSummarize(currentService.ActivateChat.Messages);
        Console.WriteLine($"[Summary Check] Policy={hasPolicy}, StatelessMode={isStateless}, MsgCount={currentService.ActivateChat.Messages.Count}, ShouldSummarize={shouldSummarize}");

        if (hasPolicy && !isStateless && shouldSummarize)
        {
            // Notify frontend that summarization is starting
            var startPayload = JsonSerializer.Serialize(new { type = "summary_start" });
            await ctx.Response.WriteAsync($"data: {startPayload}\n\n", ctx.RequestAborted);
            await ctx.Response.Body.FlushAsync(ctx.RequestAborted);

            try
            {
                await currentService.ApplySummaryPolicyIfNeededAsync();
                var endPayload = JsonSerializer.Serialize(new
                {
                    type = "summary_end",
                    summary = currentService.ConversationPolicy?.CurrentSummary ?? ""
                });
                await ctx.Response.WriteAsync($"data: {endPayload}\n\n", ctx.RequestAborted);
                await ctx.Response.Body.FlushAsync(ctx.RequestAborted);
            }
            catch (Exception summaryEx)
            {
                var errPayload = JsonSerializer.Serialize(new
                {
                    type = "summary_error",
                    content = summaryEx.Message
                });
                await ctx.Response.WriteAsync($"data: {errPayload}\n\n", ctx.RequestAborted);
                await ctx.Response.Body.FlushAsync(ctx.RequestAborted);
                // Continue with the actual chat even if summary fails
            }
        }

        RagProcessedQuery? ragProcessed = null;
        string? rewriterModelName = null;
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
                },
                ProgressAsync = async stage =>
                {
                    var progressPayload = JsonSerializer.Serialize(new
                    {
                        type = "rag_progress",
                        stage = stage.ToString(),
                        elapsedMs = ragSw.ElapsedMilliseconds
                    });
                    await ctx.Response.WriteAsync($"data: {progressPayload}\n\n", ctx.RequestAborted);
                    await ctx.Response.Body.FlushAsync(ctx.RequestAborted);
                }
            };
            try
            {
                var ragSettings = effectiveRagSettings;
                var activeService = currentService;
                if (ragSettings.QueryRewriterEnabled && activeService != null)
                {
                    var rewriterService = ragEndpointState.GetOrCreateRewriterService(
                        ragSettings.RewriterModelOverride, activeService);

                    rewriterModelName = rewriterService.Model;
                    var rewriteMaxTokens = (uint)Math.Max(1, ragSettings.QueryRewriteMaxTokens);
                    ragState.Store.SetQueryRewriter(new LlmQueryRewriter(rewriterService, rewriteMaxTokens, ragSettings.ExtractKeywords));
                }
                else
                {
                    ragState.Store.SetQueryRewriter(null);
                }

                var ragService = activeService!.WithRag(ragState.Store);
                ragProcessed = await ragService.RetrieveAsync(req.Message, ragOptions, ctx.RequestAborted);

                var ragEndPayload = JsonSerializer.Serialize(new
                {
                    type = "rag_progress_end",
                    elapsedMs = ragSw.ElapsedMilliseconds
                });
                await ctx.Response.WriteAsync($"data: {ragEndPayload}\n\n", ctx.RequestAborted);
                await ctx.Response.Body.FlushAsync(ctx.RequestAborted);

                // The library sets RequestMessageContent = OriginalQuery when no references are found,
                // so the LLM receives a clean query. We still null-out ragProcessed to skip
                // sending unnecessary RAG diagnostics to the frontend.
                if (ragProcessed is { HasReferences: false })
                {
                    ragProcessed = null;
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ragEx)
            {
                // RAG query failed — fall back to original message and notify frontend
                var progressErrorPayload = JsonSerializer.Serialize(new
                {
                    type = "rag_progress_error",
                    error = HumanizeRagError(ragEx.Message),
                    elapsedMs = ragSw.ElapsedMilliseconds
                });
                await ctx.Response.WriteAsync($"data: {progressErrorPayload}\n\n", ctx.RequestAborted);
                await ctx.Response.Body.FlushAsync(ctx.RequestAborted);

                var ragSettings2Err = effectiveRagSettings;
                var ragErrorPayload = JsonSerializer.Serialize(new
                {
                    type = "rag_info",
                    error = HumanizeRagError(ragEx.Message),
                    augmentedPrompt = (string?)null,
                    originalQuery = req.Message,
                    references = Array.Empty<object>(),
                    vectorStoreProvider,
                    searchMode = ragSettings2Err.HybridSearchEnabled ? "hybrid" : "vector",
                    reranking = new
                    {
                        enabled = ragSettings2Err.RerankEnabled,
                        provider = ragSettings2Err.RerankEnabled ? ragSettings2Err.RerankProvider : null,
                        model = ragSettings2Err.RerankEnabled ? ragSettings2Err.RerankModel : null,
                        retrievalMultiplier = ragSettings2Err.RerankEnabled ? ragSettings2Err.RetrievalDerivation.TopKMultiplier : (int?)null
                    },
                    diagnostics = new
                    {
                        finalTopK = ragSettings2Err.FinalFilter.TopK,
                        retrievalTopK = ragSettings2Err.RerankEnabled
                            ? ragSettings2Err.FinalFilter.TopK * Math.Max(1, ragSettings2Err.RetrievalDerivation.TopKMultiplier)
                            : ragSettings2Err.FinalFilter.TopK,
                        appliedFinalMinScore = ragSettings2Err.FinalFilter.MinScore,
                        appliedRetrievalMinScore = ragSettings2Err.FinalFilter.MinScore.HasValue ? ragSettings2Err.FinalFilter.MinScore.Value / Math.Max(1d, ragSettings2Err.RetrievalDerivation.MinScoreDivider) : (double?)null,
                        elapsedMs = ragSw.ElapsedMilliseconds
                    }
                });
                await ctx.Response.WriteAsync($"data: {ragErrorPayload}\n\n", ctx.RequestAborted);
                await ctx.Response.Body.FlushAsync(ctx.RequestAborted);
            }
        }

        // Send RAG info event so the frontend can show per-message diagnostics
        if (ragProcessed != null)
        {
            var ragSettings2 = effectiveRagSettings;
            var ragInfoPayload = JsonSerializer.Serialize(new
            {
                type = "rag_info",
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
                searchMode = ragSettings2.HybridSearchEnabled
                    ? (ragProcessed.SearchKeywords != null && ragProcessed.SearchKeywords.Count > 0 ? "hybrid" : "hybrid_dense_fallback")
                    : "vector",
                hybridWeight = ragSettings2.HybridSearchEnabled ? ragSettings2.HybridSearchVectorWeight : (float?)null,
                vectorStoreProvider,
                reranking = new
                {
                    enabled = ragSettings2.RerankEnabled,
                    provider = ragSettings2.RerankEnabled ? ragSettings2.RerankProvider : null,
                    model = ragSettings2.RerankEnabled ? ragSettings2.RerankModel : null,
                    retrievalMultiplier = ragSettings2.RerankEnabled ? ragSettings2.RetrievalDerivation.TopKMultiplier : (int?)null,
                    finalSelectionMode = ragSettings2.FinalSelectionMode.ToString(),
                    finalSelectionRetrievalWeight = ragSettings2.FinalSelectionRetrievalWeight
                },
                diagnostics = new
                {
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
            });
            await ctx.Response.WriteAsync($"data: {ragInfoPayload}\n\n", ctx.RequestAborted);
            await ctx.Response.Body.FlushAsync(ctx.RequestAborted);
        }

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
        var options = new StreamOptions
        {
            IncludeReasoning = streamIncludeReasoning,
            IncludeMetadata = true,
            IncludeFunctionCalls = currentService.ShouldUseFunctions,
            TextOnly = false
        };

        var stream = currentService.StreamAsync(message, options, requestContext, ctx.RequestAborted);

        await foreach (var sc in stream)
        {
            string? type = sc.Type switch
            {
                StreamingContentType.Reasoning => "reasoning",
                StreamingContentType.Text => "text",
                StreamingContentType.FunctionCall => "function_call",
                StreamingContentType.FunctionResult => "function_result",
                StreamingContentType.Error => "error",
                _ => null
            };
            if (type == null) continue;

            // Build payload based on type
            object payloadObj;
            if (type == "function_call")
            {
                // FunctionCall event: Content is null, name is in Metadata
                var name = sc.Metadata?.GetValueOrDefault("function_name")?.ToString() ?? "";
                payloadObj = new { type, name, content = (string?)null };
            }
            else if (type == "function_result")
            {
                // FunctionResult event: Content = result, arguments in Metadata
                var name = sc.Metadata?.GetValueOrDefault("function_name")?.ToString() ?? "";
                var result = sc.Content
                    ?? sc.Metadata?.GetValueOrDefault("result")?.ToString()
                    ?? "";
                var args = sc.Metadata?.GetValueOrDefault("function_arguments")?.ToString() ?? "{}";
                payloadObj = new { type, name, content = result, arguments = args };
            }
            else
            {
                // For error types, fall back to metadata if Content is null
                var content = sc.Content
                    ?? sc.Metadata?.GetValueOrDefault("error")?.ToString()
                    ?? "(unknown error)";
                if (type != "error" && string.IsNullOrEmpty(sc.Content)) continue;
                payloadObj = new { type, content };
            }

            var payload = JsonSerializer.Serialize(payloadObj);
            await ctx.Response.WriteAsync($"data: {payload}\n\n", ctx.RequestAborted);
            await ctx.Response.Body.FlushAsync(ctx.RequestAborted);
        }

        await ctx.Response.WriteAsync("data: [DONE]\n\n");
        await ctx.Response.Body.FlushAsync();
    }
    catch (OperationCanceledException) { /* client disconnected */ }
    catch (Exception ex)
    {
        var errorPayload = JsonSerializer.Serialize(new { error = ex.Message });
        await ctx.Response.WriteAsync($"data: {errorPayload}\n\n");
        await ctx.Response.Body.FlushAsync();
    }
});

// ── POST /api/clear ─────────────────────────────────────────────
app.MapPost("/api/clear", () =>
{
    if (currentService == null)
        return Results.BadRequest(new { error = "Service not configured" });

    currentService.ActivateChat.ClearMessages();
    return Results.Ok(new { status = "cleared" });
});

// ── POST /api/settings ──────────────────────────────────────────
app.MapPost("/api/settings", (SettingsRequest req) =>
{
    if (currentService == null)
        return Results.BadRequest(new { error = "Service not configured" });

    if (req.Temperature.HasValue) currentService.Temperature = req.Temperature.Value;
    if (req.TopP.HasValue) currentService.TopP = req.TopP.Value;
    if (req.MaxTokens.HasValue) currentService.MaxTokens = (uint)req.MaxTokens.Value;
    if (req.MaxMessageCount.HasValue) currentService.MaxMessageCount = (uint)req.MaxMessageCount.Value;
    if (req.FrequencyPenalty.HasValue) currentService.FrequencyPenalty = req.FrequencyPenalty.Value;
    if (req.PresencePenalty.HasValue) currentService.PresencePenalty = req.PresencePenalty.Value;
    if (req.StatelessMode.HasValue) currentService.StatelessMode = req.StatelessMode.Value;
    if (req.SystemMessage != null) currentService.SystemMessage = req.SystemMessage;
    if (req.ReasoningEnabled.HasValue) streamIncludeReasoning = req.ReasoningEnabled.Value;

    // Apply reasoning settings
    if (req.ReasoningEnabled == true && req.ReasoningType == "qwen_thinking"
        && currentService is QwenService qwenOn)
    {
        qwenOn.ThinkingMode = Mythosia.AI.Providers.Alibaba.QwenThinking.On;
    }
    else if (req.ReasoningEnabled == true && req.ReasoningLevel != null && req.ReasoningType != null)
    {
        if (currentService is OpenAIService gpt)
        {
            switch (req.ReasoningType)
            {
                case "gpt5":
                    if (Enum.TryParse<Gpt5Reasoning>(req.ReasoningLevel, out var g5))
                        gpt.Gpt5ReasoningEffort = g5;
                    gpt.Gpt5ReasoningSummary = ReasoningSummary.Detailed;
                    break;
                case "gpt5_1":
                    if (Enum.TryParse<Gpt5_1Reasoning>(req.ReasoningLevel, out var g51))
                        gpt.Gpt5_1ReasoningEffort = g51;
                    gpt.Gpt5_1ReasoningSummary = ReasoningSummary.Detailed;
                    break;
                case "gpt5_2":
                    if (Enum.TryParse<Gpt5_2Reasoning>(req.ReasoningLevel, out var g52))
                        gpt.Gpt5_2ReasoningEffort = g52;
                    gpt.Gpt5_2ReasoningSummary = ReasoningSummary.Detailed;
                    break;
                case "gpt5_3":
                    if (Enum.TryParse<Gpt5_3Reasoning>(req.ReasoningLevel, out var g53))
                        gpt.Gpt5_3ReasoningEffort = g53;
                    gpt.Gpt5_3ReasoningSummary = ReasoningSummary.Detailed;
                    break;
                case "gpt5_4":
                    if (Enum.TryParse<Gpt5_4Reasoning>(req.ReasoningLevel, out var g54))
                        gpt.Gpt5_4ReasoningEffort = g54;
                    gpt.Gpt5_4ReasoningSummary = ReasoningSummary.Detailed;
                    break;
            }
        }
        else if (currentService is AnthropicService claude)
        {
            if (int.TryParse(req.ReasoningLevel, out var budget))
                claude.ThinkingBudget = budget;
        }
        else if (currentService is XAIService grok)
        {
            if (Enum.TryParse<GrokReasoning>(req.ReasoningLevel, out var grokEffort))
                grok.ReasoningEffort = grokEffort;
        }
        else if (currentService is GoogleAIService gemini)
        {
            switch (req.ReasoningType)
            {
                case "gemini3":
                    if (Enum.TryParse<GeminiThinkingLevel>(req.ReasoningLevel, out var thinkingLevel))
                        gemini.ThinkingLevel = thinkingLevel;
                    gemini.ThinkingBudget = -1;
                    break;
                case "gemini25":
                    if (int.TryParse(req.ReasoningLevel, out var thinkingBudget))
                        gemini.ThinkingBudget = thinkingBudget;
                    gemini.ThinkingLevel = GeminiThinkingLevel.Auto;
                    break;
            }
        }
    }
    else if (req.ReasoningEnabled == false)
    {
        if (currentService is OpenAIService gptOff)
        {
            gptOff.Gpt5ReasoningEffort = Gpt5Reasoning.Auto;
            gptOff.Gpt5ReasoningSummary = null;
            gptOff.Gpt5_1ReasoningEffort = Gpt5_1Reasoning.Auto;
            gptOff.Gpt5_1ReasoningSummary = null;
            gptOff.Gpt5_2ReasoningEffort = Gpt5_2Reasoning.Auto;
            gptOff.Gpt5_2ReasoningSummary = null;
            gptOff.Gpt5_3ReasoningEffort = Gpt5_3Reasoning.Auto;
            gptOff.Gpt5_3ReasoningSummary = null;
            gptOff.Gpt5_4ReasoningEffort = Gpt5_4Reasoning.Auto;
            gptOff.Gpt5_4ReasoningSummary = null;
        }
        else if (currentService is AnthropicService claudeOff)
        {
            claudeOff.ThinkingBudget = -1;
        }
        else if (currentService is XAIService grokOff)
        {
            grokOff.ReasoningEffort = GrokReasoning.Off;
        }
        else if (currentService is GoogleAIService geminiOff)
        {
            geminiOff.ThinkingLevel = GeminiThinkingLevel.Auto;
            geminiOff.ThinkingBudget = -1;
        }
        else if (currentService is QwenService qwenOff)
        {
            qwenOff.ThinkingMode = Mythosia.AI.Providers.Alibaba.QwenThinking.Off;
        }
    }

    return Results.Ok(new { status = "updated" });
});

// ── GET /api/state ──────────────────────────────────────────────
app.MapGet("/api/state", async () =>
{
    if (currentService == null)
        return Results.Ok(new { configured = false });

    var svc = currentService;

    var conversationTokenCount = await svc.GetInputTokenCountAsync();

    // Messages
    var messages = svc.ActivateChat.Messages.Select(m => new
    {
        id = m.Id,
        role = m.Role.ToString(),
        content = m.Content,
        timestamp = m.Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff"),
        hasMultimodal = m.HasMultimodalContent,
        metadata = m.Metadata?.ToDictionary(
            kv => kv.Key,
            kv => kv.Value?.ToString() ?? "")
    }).ToList();

    // Functions
    var functions = svc.Functions.Select(f => new
    {
        name = f.Name,
        description = f.Description,
        parameters = f.Parameters?.Properties?.Select(p => new
        {
            name = p.Key,
            type = p.Value.Type,
            description = p.Value.Description,
            required = f.Parameters.Required?.Contains(p.Key) ?? false
        })
    }).ToList();

    // Policy
    var policy = svc.DefaultPolicy;
    var policyInfo = new
    {
        maxRounds = policy.MaxRounds,
        timeoutSeconds = policy.TimeoutSeconds,
        maxConcurrency = policy.MaxConcurrency,
        enableLogging = policy.EnableLogging
    };

    // Summary policy
    object? summaryPolicy = null;
    if (svc.ConversationPolicy != null)
    {
        var sp = svc.ConversationPolicy;
        summaryPolicy = new
        {
            triggerTokens = sp.TriggerTokens,
            triggerCount = sp.TriggerCount,
            keepRecentTokens = sp.KeepRecentTokens,
            keepRecentCount = sp.KeepRecentCount,
            currentSummary = sp.CurrentSummary
        };
    }

    return Results.Ok(new
    {
        configured = true,
        provider = currentProvider,
        modelEnum = currentModelEnum,

        // Model & Generation Settings
        model = svc.Model,
        temperature = svc.Temperature,
        topP = svc.TopP,
        maxTokens = svc.MaxTokens,
        frequencyPenalty = svc.FrequencyPenalty,
        presencePenalty = svc.PresencePenalty,
        maxMessageCount = svc.MaxMessageCount,
        stream = svc.Stream,

        // Modes
        statelessMode = svc.StatelessMode,
        functionsDisabled = svc.FunctionsDisabled,

        // Function Settings
        enableFunctions = svc.EnableFunctions,
        functionCallMode = svc.FunctionCallMode.ToString(),
        forceFunctionName = svc.ForceFunctionName,
        shouldUseFunctions = svc.ShouldUseFunctions,
        functions,

        // Policy
        defaultPolicy = policyInfo,

        // Summary Policy
        summaryPolicy,

        // ChatBlock
        activeChatId = svc.ActivateChat.Id,
        systemMessage = svc.ActivateChat.SystemMessage,
        messageCount = svc.ActivateChat.Messages.Count,
        conversationTokenCount,
        sentMessageCount = Math.Min(svc.ActivateChat.Messages.Count, (int)svc.MaxMessageCount),
        chatBlockCount = svc.ChatRequests.Count,

        // Messages
        messages
    });
});

// ── POST /api/summary-policy ─────────────────────────────────────
app.MapPost("/api/summary-policy", (SummaryPolicyRequest req) =>
{
    if (currentService == null)
        return Results.BadRequest(new { error = "Service not configured" });

    if (!req.Enabled)
    {
        currentService.ConversationPolicy = null;
        return Results.Ok(new { status = "disabled" });
    }

    var trigger = req.TriggerType ?? "message";
    var threshold = req.Threshold > 0 ? (uint)req.Threshold : 20u;
    var keep = req.KeepRecent > 0 ? (uint)req.KeepRecent : 5u;

    try
    {
        currentService.ConversationPolicy = trigger switch
        {
            "token" => SummaryConversationPolicy.ByToken(threshold, keep),
            "both" => SummaryConversationPolicy.ByBoth(threshold, threshold, keep, keep),
            _ => SummaryConversationPolicy.ByMessage(threshold, keep)
        };
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }

    return Results.Ok(new { status = "enabled", trigger, threshold, keep });
});

app.MapPost("/api/summary-clear", () =>
{
    if (currentService?.ConversationPolicy != null)
        currentService.ConversationPolicy.CurrentSummary = null;
    return Results.Ok(new { status = "cleared" });
});

// ── GET /api/code-snippet ────────────────────────────────────────
app.MapPost("/api/code-snippet", (CodeSnippetRequest req) =>
{
    if (currentService == null)
        return Results.BadRequest(new { error = "Service not configured" });

    var svc = currentService;
    var code = GenerateCodeSnippet(svc, currentProvider!, currentModelEnum!, req.UserMessage);
    return Results.Ok(new { code });
});

// ── GET /api/functions ──────────────────────────────────────────
app.MapGet("/api/functions", () =>
{
    if (currentService == null)
        return Results.Ok(new { functions = Array.Empty<object>(), enabled = false });

    var functions = currentService.Functions.Select(f => new
    {
        name = f.Name,
        description = f.Description,
        parameters = f.Parameters?.Properties?.Select(p => new
        {
            name = p.Key,
            type = p.Value.Type,
            description = p.Value.Description,
            required = f.Parameters.Required?.Contains(p.Key) ?? false
        })
    }).ToList();

    return Results.Ok(new
    {
        functions,
        enabled = currentService.EnableFunctions,
        shouldUseFunctions = currentService.ShouldUseFunctions,
        mode = currentService.FunctionCallMode.ToString(),
        presetEnabled = presetFunctionsEnabled
    });
});

// ── POST /api/functions/toggle-preset ───────────────────────────
app.MapPost("/api/functions/toggle-preset", (TogglePresetRequest req) =>
{
    if (currentService == null)
        return Results.BadRequest(new { error = "Service not configured" });

    presetFunctionsEnabled = req.Enabled;

    if (req.Enabled)
    {
        if (!currentService.Functions.Any(f => f.Name == "get_url_content"))
            RegisterPresetFunctions(currentService);
    }
    else
    {
        currentService.Functions.RemoveAll(f => f.Name == "get_url_content");
    }

    return Results.Ok(new { status = "updated", presetEnabled = presetFunctionsEnabled, functionCount = currentService.Functions.Count });
});

app.MapChatUiRagCoreEndpoints(ragState, ragEndpointState, embeddingHttpClient);

app.MapChatUiRagDiagnosticsEndpoints(ragState);

app.MapExternalTestEndpoints(
    () => currentService,
    () => currentProvider,
    ragState,
    ragEndpointState,
    embeddingHttpClient);

// ── Helper ──────────────────────────────────────────────────────
static Mythosia.AI.Providers.Alibaba.EndpointPlatform ParsePlatform(string? value) => value?.ToLowerInvariant() switch
{
    "ollama" => Mythosia.AI.Providers.Alibaba.EndpointPlatform.Ollama,
    "vllm" => Mythosia.AI.Providers.Alibaba.EndpointPlatform.Vllm,
    _ => Mythosia.AI.Providers.Alibaba.EndpointPlatform.Vllm,
};

static Mythosia.AI.Rag.RagFinalSelectionMode? ParseFinalSelectionMode(string? value) => value switch
{
    "RerankerOnly" => Mythosia.AI.Rag.RagFinalSelectionMode.RerankerOnly,
    "WeightedBlend" => Mythosia.AI.Rag.RagFinalSelectionMode.WeightedBlend,
    _ => null,
};

// ── Fallback to index.html ──────────────────────────────────────
app.MapFallbackToFile("index.html");

app.Run();
