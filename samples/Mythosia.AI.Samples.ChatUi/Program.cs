using Mythosia.AI.Extensions;
using Mythosia.AI.Models;
using Mythosia.AI.Models.Enums;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Models.Messages;
using Mythosia.AI.Models.Streaming;
using Mythosia.AI.Loaders;
using Mythosia.AI.Loaders.Office.Excel;
using Mythosia.AI.Loaders.Office.PowerPoint;
using Mythosia.AI.Loaders.Office.Word;
using Mythosia.AI.Loaders.Pdf;
using Mythosia.AI.Rag;
using Mythosia.AI.Rag.Diagnostics;
using Mythosia.AI.Rag.Embeddings;
using Mythosia.AI.Rag.Loaders;
using Mythosia.AI.Rag.Splitters;
using Mythosia.VectorDb.InMemory;
using Mythosia.VectorDb;
using Mythosia.VectorDb.Pinecone;
using Mythosia.VectorDb.Postgres;
using Mythosia.VectorDb.Qdrant;
using Mythosia.AI.Services.Anthropic;
using Mythosia.AI.Services.Base;
using Mythosia.AI.Services.DeepSeek;
using Mythosia.AI.Services.Google;
using Mythosia.AI.Services.OpenAI;
using Mythosia.AI.Services.Perplexity;
using Mythosia.AI.Services.xAI;
using Mythosia.AI.Samples.ChatUi;
using static Mythosia.AI.Samples.ChatUi.ChatUiModelHelpers;
using static Mythosia.AI.Samples.ChatUi.ChatUiUtilityHelpers;
using Microsoft.AspNetCore.Http;
using System.ComponentModel;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

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
IVectorStore ragVectorStore = new InMemoryVectorStore();
string ragVectorStoreProvider = "inmemory";
string ragPgConnectionString = "";
string ragPgTableName = "vectors";
string ragPgSchemaName = "public";
int ragPgDimension = 1536;
bool ragPgEnsureSchema = true;
string ragQdrantHost = "localhost";
int ragQdrantPort = 6334;
string? ragQdrantApiKey = null;
bool ragQdrantUseTls = false;
int ragQdrantDimension = 1536;
string ragQdrantCollectionName = "default";
string ragPineconeIndexHost = "";
string? ragPineconeApiKey = null;
string ragPineconeNamespace = "default";
string? rewriterApiKey = null;
var embeddingHttpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
string ragEmbeddingOpenAiKey = "";

// ── GET /api/models ─────────────────────────────────────────────
app.MapGet("/api/models", () => Results.Ok(BuildModelCatalogue()));

// ── POST /api/configure ─────────────────────────────────────────
app.MapPost("/api/configure", (ConfigureRequest req) =>
{
    if (string.IsNullOrWhiteSpace(req.ApiKey) || string.IsNullOrWhiteSpace(req.Model))
        return Results.BadRequest(new { error = "apiKey and model are required" });

    if (!Enum.TryParse<AIModel>(req.Model, out var aiModel))
        return Results.BadRequest(new { error = $"Unknown model: {req.Model}" });

    var provider = GetProviderForModel(aiModel);
    var desc = aiModel.GetType()
        .GetField(aiModel.ToString())!
        .GetCustomAttribute<DescriptionAttribute>()?.Description ?? aiModel.ToString();

    try
    {
        var previousService = currentService;
        var httpClient = new HttpClient();
        currentService = provider switch
        {
            "OpenAI" => new ChatGptService(req.ApiKey, httpClient),
            "Anthropic" => new ClaudeService(req.ApiKey, httpClient),
            "Google" => new GeminiService(req.ApiKey, httpClient),
            "DeepSeek" => new DeepSeekService(req.ApiKey, httpClient),
            "xAI" => new GrokService(req.ApiKey, httpClient),
            "Perplexity" => new SonarService(req.ApiKey, httpClient),
            _ => throw new NotSupportedException($"Provider {provider} not supported")
        };
        currentService.ChangeModel(aiModel);
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
            try
            {
                var ragQuery = req.Message;
                string? rewrittenQuery = null;

                // Query Rewriting for multi-turn conversations
                var ragSettings = ragState.GetSettings();
                if (ragSettings.QueryRewriterEnabled && currentService != null)
                {
                    var messages = currentService.ActivateChat.Messages;
                    if (messages.Count > 0)
                    {
                        var history = messages
                            .Where(m => m.Role == ActorRole.User || m.Role == ActorRole.Assistant)
                            .Select(m => new ConversationTurn(
                                m.Role == ActorRole.User ? "user" : "assistant",
                                m.Content ?? string.Empty))
                            .ToList();

                        if (history.Count > 0)
                        {
                            AIService rewriterService = currentService;

                            // Use override model if configured
                            if (!string.IsNullOrWhiteSpace(ragSettings.RewriterModelOverride)
                                && !string.IsNullOrWhiteSpace(rewriterApiKey)
                                && Enum.TryParse<AIModel>(ragSettings.RewriterModelOverride, out var overrideModel))
                            {
                                var apiKey = rewriterApiKey;
                                if (!string.IsNullOrWhiteSpace(apiKey))
                                {
                                    var overrideProvider = GetProviderForModel(overrideModel);
                                    var overrideHttpClient = new HttpClient();
                                    rewriterService = overrideProvider switch
                                    {
                                        "OpenAI" => new ChatGptService(apiKey, overrideHttpClient),
                                        "Anthropic" => new ClaudeService(apiKey, overrideHttpClient),
                                        "Google" => new GeminiService(apiKey, overrideHttpClient),
                                        "DeepSeek" => new DeepSeekService(apiKey, overrideHttpClient),
                                        "xAI" => new GrokService(apiKey, overrideHttpClient),
                                        "Perplexity" => new SonarService(apiKey, overrideHttpClient),
                                        _ => currentService
                                    };
                                    rewriterService.ChangeModel(overrideModel);
                                }
                            }

                            rewriterModelName = rewriterService.Model;

                            var rewriter = new LlmQueryRewriter(rewriterService);
                            var rewritten = await rewriter.RewriteAsync(ragQuery, history, ctx.RequestAborted);
                            if (!string.IsNullOrWhiteSpace(rewritten) && rewritten != ragQuery)
                            {
                                rewrittenQuery = rewritten;
                                ragQuery = rewritten;
                            }
                        }
                    }
                }

                ragProcessed = await ragState.Store.QueryAsync(ragQuery, ctx.RequestAborted);

                // Preserve rewritten query info
                if (rewrittenQuery != null)
                {
                    ragProcessed.RewrittenQuery = rewrittenQuery;
                    ragProcessed.OriginalQuery = req.Message;
                }

                // The library sets AugmentedPrompt = OriginalQuery when no references are found,
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
                var ragErrorPayload = JsonSerializer.Serialize(new
                {
                    type = "rag_info",
                    error = ragEx.Message,
                    augmentedPrompt = (string?)null,
                    originalQuery = req.Message,
                    references = Array.Empty<object>()
                });
                await ctx.Response.WriteAsync($"data: {ragErrorPayload}\n\n", ctx.RequestAborted);
                await ctx.Response.Body.FlushAsync(ctx.RequestAborted);
            }
        }

        // Send RAG info event so the frontend can show per-message diagnostics
        if (ragProcessed != null)
        {
            var ragInfoPayload = JsonSerializer.Serialize(new
            {
                type = "rag_info",
                augmentedPrompt = ragProcessed.AugmentedPrompt,
                originalQuery = ragProcessed.OriginalQuery,
                rewrittenQuery = ragProcessed.RewrittenQuery,
                rewriterModel = rewriterModelName,
                diagnostics = new
                {
                    appliedNamespace = ragProcessed.Diagnostics.AppliedNamespace,
                    appliedTopK = ragProcessed.Diagnostics.AppliedTopK,
                    appliedMinScore = ragProcessed.Diagnostics.AppliedMinScore,
                    elapsedMs = ragProcessed.Diagnostics.ElapsedMs
                },
                references = ragProcessed.References.Select(r => new
                {
                    score = r.Score,
                    content = r.Record.Content,
                    metadata = r.Record.Metadata
                })
            });
            await ctx.Response.WriteAsync($"data: {ragInfoPayload}\n\n", ctx.RequestAborted);
            await ctx.Response.Body.FlushAsync(ctx.RequestAborted);
        }

        var messageContent = ragProcessed?.AugmentedPrompt ?? req.Message;
        var message = new Message(ActorRole.User, messageContent);
        if (ragProcessed != null)
        {
            message.Metadata = new Dictionary<string, object>
            {
                ["rag"] = true,
                ["rag_original_query"] = req.Message,
                ["rag_reference_count"] = ragProcessed.References.Count
            };
        }
        var options = new StreamOptions
        {
            IncludeReasoning = streamIncludeReasoning,
            IncludeMetadata = true,
            IncludeFunctionCalls = currentService.ShouldUseFunctions,
            TextOnly = false
        };

        await foreach (var sc in currentService.StreamAsync(message, options, ctx.RequestAborted))
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
                if (type != "error" && sc.Content == null) continue;
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
    if (req.ReasoningEnabled == true && req.ReasoningLevel != null && req.ReasoningType != null)
    {
        if (currentService is ChatGptService gpt)
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
        else if (currentService is ClaudeService claude)
        {
            if (int.TryParse(req.ReasoningLevel, out var budget))
                claude.ThinkingBudget = budget;
        }
        else if (currentService is GrokService grok)
        {
            if (Enum.TryParse<GrokReasoning>(req.ReasoningLevel, out var grokEffort))
                grok.ReasoningEffort = grokEffort;
        }
        else if (currentService is GeminiService gemini)
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
        if (currentService is ChatGptService gptOff)
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
        else if (currentService is ClaudeService claudeOff)
        {
            claudeOff.ThinkingBudget = -1;
        }
        else if (currentService is GrokService grokOff)
        {
            grokOff.ReasoningEffort = GrokReasoning.Off;
        }
        else if (currentService is GeminiService geminiOff)
        {
            geminiOff.ThinkingLevel = GeminiThinkingLevel.Auto;
            geminiOff.ThinkingBudget = -1;
        }
    }

    return Results.Ok(new { status = "updated" });
});

// ── GET /api/state ──────────────────────────────────────────────
app.MapGet("/api/state", () =>
{
    if (currentService == null)
        return Results.Ok(new { configured = false });

    var svc = currentService;

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

// ── GET /api/rag/pipeline-settings ─────────────────────────────
app.MapGet("/api/rag/pipeline-settings", () =>
{
    return Results.Ok(ragState.GetSettings());
});

// ── POST /api/rag/pipeline-settings ─────────────────────────────
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

    // Store rewriter API key separately
    if (req.RewriterApiKey != null)
        rewriterApiKey = string.IsNullOrWhiteSpace(req.RewriterApiKey) ? null : req.RewriterApiKey;

    ragState.UpdateSettings(settings);
    ragState.TryApplyQuerySettings(settings);
    return Results.Ok(settings);
});

// ── GET /api/rag/vector-store ────────────────────────────────────
app.MapGet("/api/rag/vector-store", () =>
{
    return Results.Ok(new
    {
        provider = ragVectorStoreProvider,
        connectionString = ragPgConnectionString,
        tableName = ragPgTableName,
        schemaName = ragPgSchemaName,
        dimension = ragPgDimension,
        ensureSchema = ragPgEnsureSchema,
        qdrantHost = ragQdrantHost,
        qdrantPort = ragQdrantPort,
        qdrantApiKey = (string?)null,
        qdrantUseTls = ragQdrantUseTls,
        qdrantDimension = ragQdrantDimension,
        qdrantCollectionName = ragQdrantCollectionName,
        pineconeIndexHost = ragPineconeIndexHost,
        pineconeApiKey = (string?)null,
        pineconeNamespace = ragPineconeNamespace
    });
});

// ── POST /api/rag/vector-store ──────────────────────────────────
app.MapPost("/api/rag/vector-store", async (VectorStoreConfigRequest req) =>
{
    var provider = (req.Provider ?? "inmemory").Trim().ToLowerInvariant();

    if (provider == "postgres")
    {
        if (string.IsNullOrWhiteSpace(req.ConnectionString))
            return Results.BadRequest(new { error = "ConnectionString is required for PostgreSQL." });

        ragPgConnectionString = req.ConnectionString.Trim();
        ragPgTableName = string.IsNullOrWhiteSpace(req.TableName) ? "vectors" : req.TableName.Trim();
        ragPgSchemaName = string.IsNullOrWhiteSpace(req.SchemaName) ? "public" : req.SchemaName.Trim();
        ragPgDimension = req.Dimension is > 0 ? req.Dimension.Value : 1536;
        ragPgEnsureSchema = req.EnsureSchema ?? true;

        // Save the OpenAI API key if provided
        if (!string.IsNullOrWhiteSpace(req.OpenAiApiKey))
            ragEmbeddingOpenAiKey = req.OpenAiApiKey.Trim();

        try
        {
            var oldStore = ragVectorStore;
            ragVectorStore = new PostgresStore(new PostgresOptions
            {
                ConnectionString = ragPgConnectionString,
                Dimension = ragPgDimension,
                TableName = ragPgTableName,
                SchemaName = ragPgSchemaName,
                EnsureSchema = ragPgEnsureSchema
            });
            ragVectorStoreProvider = "postgres";

            if (oldStore is IDisposable disposable)
                disposable.Dispose();

            // Auto-connect RAG pipeline for external DB (existing vectors are queryable immediately)
            string? autoConnectWarning = null;
            try
            {
                var settings = ragState.GetSettings();
                var embeddingKey = !string.IsNullOrWhiteSpace(ragEmbeddingOpenAiKey) ? ragEmbeddingOpenAiKey : null;
                var epKey = settings.EmbeddingProvider?.ToLowerInvariant() ?? "openai";

                // Fail fast when OpenAI is selected but no API key is available.
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
                            .UseStore(ragVectorStore)
                            .WithTopK(settings.TopK);

                        if (epKey == "ollama")
                        {
                            builder.UseEmbedding(new Mythosia.AI.Rag.Embeddings.OllamaEmbeddingProvider(
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

            return Results.Ok(new { provider = ragVectorStoreProvider, status = "connected", warning = autoConnectWarning, tableName = ragPgTableName, schemaName = ragPgSchemaName, dimension = ragPgDimension });
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { error = $"Failed to connect: {ex.Message}" });
        }
    }
    else if (provider == "qdrant")
    {
        // Strip scheme (https://, http://) and trailing slashes/paths from host
        var rawHost = (req.QdrantHost ?? "").Trim();
        if (rawHost.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            rawHost = rawHost.Substring("https://".Length);
        else if (rawHost.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            rawHost = rawHost.Substring("http://".Length);
        var slashIdx = rawHost.IndexOf('/');
        if (slashIdx >= 0) rawHost = rawHost.Substring(0, slashIdx);
        ragQdrantHost = string.IsNullOrWhiteSpace(rawHost) ? "localhost" : rawHost;
        ragQdrantPort = req.QdrantPort is > 0 ? req.QdrantPort.Value : 6334;
        ragQdrantApiKey = string.IsNullOrWhiteSpace(req.QdrantApiKey) ? null : req.QdrantApiKey.Trim();
        ragQdrantUseTls = req.QdrantUseTls ?? false;
        ragQdrantDimension = req.Dimension is > 0 ? req.Dimension.Value : 1536;
        ragQdrantCollectionName = string.IsNullOrWhiteSpace(req.QdrantCollectionName) ? "default" : req.QdrantCollectionName.Trim();

        if (!string.IsNullOrWhiteSpace(req.OpenAiApiKey))
            ragEmbeddingOpenAiKey = req.OpenAiApiKey.Trim();

        try
        {
            var oldStore = ragVectorStore;
            ragVectorStore = new QdrantStore(new QdrantOptions
            {
                Host = ragQdrantHost,
                Port = ragQdrantPort,
                ApiKey = ragQdrantApiKey,
                UseTls = ragQdrantUseTls,
                Dimension = ragQdrantDimension,
                CollectionName = ragQdrantCollectionName
            });
            ragVectorStoreProvider = "qdrant";

            if (oldStore is IDisposable disposable)
                disposable.Dispose();

            // Auto-connect RAG pipeline for external DB
            string? autoConnectWarning = null;
            try
            {
                var settings = ragState.GetSettings();
                var embeddingKey = !string.IsNullOrWhiteSpace(ragEmbeddingOpenAiKey) ? ragEmbeddingOpenAiKey : null;
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
                            .UseStore(ragVectorStore)
                            .WithTopK(settings.TopK)
                            .WithNamespace(ragQdrantCollectionName);

                        if (epKey == "ollama")
                        {
                            builder.UseEmbedding(new Mythosia.AI.Rag.Embeddings.OllamaEmbeddingProvider(
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

            return Results.Ok(new { provider = ragVectorStoreProvider, status = "connected", warning = autoConnectWarning, host = ragQdrantHost, port = ragQdrantPort, dimension = ragQdrantDimension, collectionName = ragQdrantCollectionName });
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { error = $"Failed to connect: {ex.Message}" });
        }
    }
    else if (provider == "pinecone")
    {
        ragPineconeIndexHost = (req.PineconeIndexHost ?? "").Trim();
        ragPineconeApiKey = string.IsNullOrWhiteSpace(req.PineconeApiKey) ? null : req.PineconeApiKey.Trim();
        ragPineconeNamespace = string.IsNullOrWhiteSpace(req.PineconeNamespace) ? "default" : req.PineconeNamespace.Trim();

        if (string.IsNullOrWhiteSpace(ragPineconeIndexHost))
            return Results.BadRequest(new { error = "PineconeIndexHost is required for Pinecone." });

        if (string.IsNullOrWhiteSpace(ragPineconeApiKey))
            return Results.BadRequest(new { error = "PineconeApiKey is required for Pinecone." });

        if (!string.IsNullOrWhiteSpace(req.OpenAiApiKey))
            ragEmbeddingOpenAiKey = req.OpenAiApiKey.Trim();

        try
        {
            var oldStore = ragVectorStore;
            ragVectorStore = new PineconeStore(new PineconeOptions
            {
                IndexHost = ragPineconeIndexHost,
                ApiKey = ragPineconeApiKey,
                DefaultNamespace = ragPineconeNamespace
            });
            ragVectorStoreProvider = "pinecone";

            if (oldStore is IDisposable disposable)
                disposable.Dispose();

            string? autoConnectWarning = null;
            try
            {
                var settings = ragState.GetSettings();
                var embeddingKey = !string.IsNullOrWhiteSpace(ragEmbeddingOpenAiKey) ? ragEmbeddingOpenAiKey : null;
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
                            .UseStore(ragVectorStore)
                            .WithTopK(settings.TopK)
                            .WithNamespace(ragPineconeNamespace);

                        if (epKey == "ollama")
                        {
                            builder.UseEmbedding(new Mythosia.AI.Rag.Embeddings.OllamaEmbeddingProvider(
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

            return Results.Ok(new { provider = ragVectorStoreProvider, status = "connected", warning = autoConnectWarning, indexHost = ragPineconeIndexHost, @namespace = ragPineconeNamespace });
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { error = $"Failed to connect: {ex.Message}" });
        }
    }
    else
    {
        var oldStore = ragVectorStore;
        ragVectorStore = new InMemoryVectorStore();
        ragVectorStoreProvider = "inmemory";

        if (oldStore is IDisposable disposable)
            disposable.Dispose();

        ragState.ClearStore();

        return Results.Ok(new { provider = ragVectorStoreProvider, status = "switched" });
    }
});

// ── GET /api/rag/status ────────────────────────────────────────
app.MapGet("/api/rag/status", () =>
{
    var settings = ragState.GetSettings();
    var hasIndex = ragState.HasStore || ragState.TryGetSnapshot(out _, out _);
    return Results.Ok(new
    {
        hasIndex,
        lastUpdated = ragState.LastUpdated,
        settings,
        vectorStoreProvider = ragVectorStoreProvider
    });
});

// ── POST /api/rag/reference ─────────────────────────────────────
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
        ragEmbeddingOpenAiKey = openAiApiKey.Trim();

    var documents = new List<RagDocument>();
    var chunks = new List<RagChunk>();
    var records = new List<VectorRecord>();

    var splitter = new TrackingTextSplitter(BuildTextSplitter(chunkerKey, chunkSize, chunkOverlap), chunks);
    var resolvedEmbeddingModel = string.IsNullOrWhiteSpace(embeddingModel)
        ? embeddingProviderKey == "ollama" ? "qwen3-embedding:4b" : "text-embedding-3-small"
        : embeddingModel;
    IEmbeddingProvider embeddingProvider = embeddingProviderKey?.Equals("ollama", StringComparison.OrdinalIgnoreCase) == true
        ? new Mythosia.AI.Rag.Embeddings.OllamaEmbeddingProvider(
            embeddingHttpClient,
            resolvedEmbeddingModel,
            embeddingDimensions,
            embeddingBaseUrl)
        : BuildOpenAiEmbeddingProvider(openAiApiKey, embeddingHttpClient, resolvedEmbeddingModel, embeddingDimensions);
    var trackingStore = new TrackingVectorStore(ragVectorStore, records);

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

            // Use collection name for external DBs
            if (ragVectorStoreProvider == "qdrant")
                builder.WithNamespace(ragQdrantCollectionName);
            else if (ragVectorStoreProvider == "pinecone")
                builder.WithNamespace(ragPineconeNamespace);

            if (minScore.HasValue)
            {
                builder.WithScoreThreshold(minScore.Value);
            }

            if (!string.IsNullOrWhiteSpace(promptTemplate))
            {
                builder.WithPromptTemplate(promptTemplate);
            }

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

// ── GET /api/rag/code-snippet ──────────────────────────────────
app.MapGet("/api/rag/code-snippet", () =>
{
    if (!ragState.TryGetSnapshot(out _, out var config))
        return Results.BadRequest(new { error = "Run Reference first to generate the code snippet." });

    var code = GenerateRagReferenceCodeSnippet(config!);
    return Results.Ok(new { code });
});

// ── GET /api/rag/reference-history ──────────────────────────────
app.MapGet("/api/rag/reference-history", () =>
{
    var history = ragState.GetHistory()
        .Select(entry => new
        {
            id = entry.Id,
            createdAt = entry.CreatedAt,
            sources = entry.Sources,
            summary = entry.Summary,
            config = entry.Config
        })
        .ToList();
    return Results.Ok(new { history });
});

// ── GET /api/rag/diagnose/health-check ──────────────────────────
app.MapGet("/api/rag/diagnose/health-check", async (CancellationToken ct) =>
{
    if (ragState.Store == null)
        return Results.BadRequest(new { error = "No RAG index. Run Document Reference first." });

    try
    {
        var session = ragState.Store.Diagnose();
        var result = await session.HealthCheckAsync(cancellationToken: ct);
        return Results.Ok(new
        {
            @namespace = result.Namespace,
            totalChunks = result.TotalChunks,
            hasWarnings = result.HasWarnings,
            items = result.Items.Select(i => new
            {
                status = i.Status.ToString().ToLowerInvariant(),
                category = i.Category,
                message = i.Message
            }),
            report = result.ToReport()
        });
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

// ── POST /api/rag/diagnose/why-missing ──────────────────────────
app.MapPost("/api/rag/diagnose/why-missing", async (WhyMissingRequest req, CancellationToken ct) =>
{
    if (ragState.Store == null)
        return Results.BadRequest(new { error = "No RAG index. Run Document Reference first." });

    if (string.IsNullOrWhiteSpace(req.Query) || string.IsNullOrWhiteSpace(req.ExpectedText))
        return Results.BadRequest(new { error = "query and expectedText are required." });

    try
    {
        var session = ragState.Store.Diagnose();
        var result = await session.WhyMissingAsync(req.Query, req.ExpectedText, cancellationToken: ct);
        return Results.Ok(new
        {
            query = result.Query,
            expectedText = result.ExpectedText,
            hasIssues = result.HasIssues,
            steps = result.Steps.Select(s => new
            {
                status = s.Status.ToString().ToLowerInvariant(),
                stepName = s.StepName,
                message = s.Message,
                suggestion = s.Suggestion
            }),
            suggestions = result.Suggestions,
            report = result.ToReport()
        });
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

// ── POST /api/rag/diagnose/query-scores ─────────────────────────
app.MapPost("/api/rag/diagnose/query-scores", async (QueryScoresRequest req, CancellationToken ct) =>
{
    if (ragState.Store == null)
        return Results.BadRequest(new { error = "No RAG index. Run Document Reference first." });

    if (string.IsNullOrWhiteSpace(req.Query))
        return Results.BadRequest(new { error = "query is required." });

    try
    {
        var diag = new RagDiagnostics(ragState.Store);
        var result = await diag.DiagnoseQueryAsync(req.Query, req.ExpectedText, cancellationToken: ct);
        return Results.Ok(new
        {
            query = req.Query,
            expectedText = req.ExpectedText,
            totalScored = result.AllScoredResults.Count,
            topK = result.TopK,
            minScore = result.MinScore,
            targetChunk = result.TargetChunkInfo != null ? new
            {
                rank = result.TargetChunkInfo.Rank,
                score = result.TargetChunkInfo.Score,
                isInTopK = result.TargetChunkInfo.IsInTopK,
                passesMinScore = result.TargetChunkInfo.PassesMinScore,
                preview = result.TargetChunkInfo.Preview,
                contentLength = result.TargetChunkInfo.Record.Content.Length
            } : (object?)null,
            results = result.AllScoredResults.Select(r => new
            {
                rank = r.Rank,
                score = r.Score,
                containsText = r.ContainsTarget,
                preview = r.Preview,
                content = r.Record.Content,
                contentLength = r.Record.Content.Length,
                id = r.Record.Id
            })
        });
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

// ── Fallback to index.html ──────────────────────────────────────
app.MapFallbackToFile("index.html");

app.Run();
