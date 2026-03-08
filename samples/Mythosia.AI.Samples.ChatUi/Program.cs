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
var ragEndpointState = new ChatUiRagEndpointState();
var embeddingHttpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };

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
                                && !string.IsNullOrWhiteSpace(ragEndpointState.RewriterApiKey)
                                && Enum.TryParse<AIModel>(ragSettings.RewriterModelOverride, out var overrideModel))
                            {
                                var apiKey = ragEndpointState.RewriterApiKey;
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

app.MapChatUiRagCoreEndpoints(ragState, ragEndpointState, embeddingHttpClient);

app.MapChatUiRagDiagnosticsEndpoints(ragState);

// ── Fallback to index.html ──────────────────────────────────────
app.MapFallbackToFile("index.html");

app.Run();
