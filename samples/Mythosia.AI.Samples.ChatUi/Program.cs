using Mythosia.AI.Extensions;
using Mythosia.AI.Models;
using Mythosia.AI.Models.Enums;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Models.Messages;
using Mythosia.AI.Models.Streaming;
using Mythosia.AI.Services.Anthropic;
using Mythosia.AI.Services.Base;
using Mythosia.AI.Services.DeepSeek;
using Mythosia.AI.Services.Google;
using Mythosia.AI.Services.OpenAI;
using Mythosia.AI.Services.Perplexity;
using Mythosia.AI.Services.xAI;
using System.ComponentModel;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.UseStaticFiles();

// ── Shared state ────────────────────────────────────────────────
AIService? currentService = null;
string? currentProvider = null;
string? currentModelEnum = null;
bool presetFunctionsEnabled = true; // Whether preset functions are registered

// ── Helper: build model catalogue ───────────────────────────────
static List<object> BuildModelCatalogue()
{
    var groups = new Dictionary<string, List<object>>();

    foreach (AIModel model in Enum.GetValues(typeof(AIModel)))
    {
        var provider = GetProviderForModel(model);
        var description = model.GetType()
            .GetField(model.ToString())!
            .GetCustomAttribute<DescriptionAttribute>()?.Description ?? model.ToString();

        if (!groups.ContainsKey(provider))
            groups[provider] = new List<object>();

        var reasoning = GetReasoningLevels(model);
        var maxOutputTokens = GetDefaultMaxOutputTokens(model);
        groups[provider].Add(new { name = model.ToString(), description, reasoning, maxOutputTokens });
    }

    return groups.Select(g => (object)new { provider = g.Key, models = g.Value }).ToList();
}

static object? GetReasoningLevels(AIModel model)
{
    var name = model.ToString();
    // OpenAI GPT-5
    if (name.StartsWith("Gpt5") && !name.StartsWith("Gpt5_1") && !name.StartsWith("Gpt5_2"))
        return new { type = "gpt5", levels = new[] { "Auto", "Minimal", "Low", "Medium", "High" } };
    // OpenAI GPT-5.1
    if (name.StartsWith("Gpt5_1"))
        return new { type = "gpt5_1", levels = new[] { "Auto", "None", "Low", "Medium", "High" } };
    // OpenAI GPT-5.2
    if (name.StartsWith("Gpt5_2"))
        return new { type = "gpt5_2", levels = new[] { "Auto", "None", "Low", "Medium", "High", "XHigh" } };
    // OpenAI o3
    if (name.StartsWith("o3") || name.StartsWith("O3"))
        return new { type = "o3", levels = new[] { "Low", "Medium", "High" } };
    // Claude (extended thinking)
    if (name.StartsWith("Claude"))
    {
        // Sonnet 4+, Opus 4+, Haiku 4.5+
        if (name.Contains("Sonnet4") || name.Contains("Opus4") || name.Contains("Haiku4_5"))
            return new { type = "claude", levels = new[] { "1024", "2048", "4096", "8192", "16384" } };
    }
    // xAI Grok reasoning models
    if (name.StartsWith("Grok"))
    {
        // grok-3-mini: supports reasoning_effort (Low/High), returns reasoning_content
        if (name.Contains("Grok3Mini"))
            return new { type = "grok", levels = new[] { "Low", "High" } };
        // grok-4, grok-4-1-fast: always reasoning, no controllable parameters, no visible reasoning
        if (name.Contains("Grok4"))
            return new { type = "grok_always", levels = Array.Empty<string>() };
    }
    return null;
}

static string GetProviderForModel(AIModel model)
{
    var name = model.ToString();
    if (name.StartsWith("Claude")) return "Anthropic";
    if (name.StartsWith("Gpt") || name.StartsWith("GPT") || name.StartsWith("o3")) return "OpenAI";
    if (name.StartsWith("Grok")) return "xAI";
    if (name.StartsWith("Gemini")) return "Google";
    if (name.StartsWith("DeepSeek")) return "DeepSeek";
    if (name.StartsWith("Perplexity")) return "Perplexity";
    return "Unknown";
}

static uint GetDefaultMaxOutputTokens(AIModel model)
{
    var desc = model.GetType()
        .GetField(model.ToString())!
        .GetCustomAttribute<DescriptionAttribute>()?.Description?.ToLower() ?? "";

    var provider = GetProviderForModel(model);
    return provider switch
    {
        "OpenAI" => desc switch
        {
            _ when desc.StartsWith("o3") => 100000,
            _ when desc.StartsWith("gpt-5") && desc.Contains("chat") => 16384,
            _ when desc.StartsWith("gpt-5") => 128000,
            _ when desc.StartsWith("gpt-4.1") => 32768,
            _ when desc.Contains("4o-mini") => 16384,
            _ when desc.Contains("4o") => 16384,
            _ when desc.Contains("vision") => 4096,
            _ => 16384
        },
        "Anthropic" => desc switch
        {
            _ when desc.Contains("opus-4-6") => 128000,
            _ when desc.Contains("sonnet-4-6") => 65536,
            _ when desc.Contains("opus-4-5") => 65536,
            _ when desc.Contains("sonnet-4-5") => 65536,
            _ when desc.Contains("haiku-4-5") => 65536,
            _ when desc.Contains("opus-4") => 32768,
            _ when desc.Contains("sonnet-4") => 16384,
            _ when desc.Contains("haiku-4") => 8192,
            _ => 8192
        },
        "Google" => 65536,
        "xAI" => 131072,
        "DeepSeek" => 8192,
        "Perplexity" => 8192,
        _ => 4096
    };
}

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

        var message = new Message(ActorRole.User, req.Message);
        var options = new StreamOptions
        {
            IncludeReasoning = true,
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
        }
        else if (currentService is ClaudeService claudeOff)
        {
            claudeOff.ThinkingBudget = -1;
        }
        else if (currentService is GrokService grokOff)
        {
            grokOff.ReasoningEffort = GrokReasoning.Off;
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

// ── Fallback to index.html ──────────────────────────────────────
app.MapFallbackToFile("index.html");

app.Run();

// ── Code Snippet Generator ───────────────────────────────────────
static string GenerateCodeSnippet(AIService svc, string provider, string modelEnum, string? userMessage)
{
    var serviceClass = provider switch
    {
        "OpenAI" => "ChatGptService",
        "Anthropic" => "ClaudeService",
        "Google" => "GeminiService",
        "DeepSeek" => "DeepSeekService",
        "xAI" => "GrokService",
        "Perplexity" => "SonarService",
        _ => "AIService"
    };

    var sb = new System.Text.StringBuilder();

    // Using statements
    sb.AppendLine("using Mythosia.AI.Extensions;");
    sb.AppendLine("using Mythosia.AI.Models;");
    sb.AppendLine("using Mythosia.AI.Models.Enums;");
    sb.AppendLine("using Mythosia.AI.Models.Messages;");
    sb.AppendLine("using Mythosia.AI.Models.Streaming;");
    sb.AppendLine($"using Mythosia.AI.Services.{(provider == "xAI" ? "xAI" : provider)};");
    sb.AppendLine();

    // Service creation
    sb.AppendLine($"// 1. Create the AI service");
    sb.AppendLine($"var httpClient = new HttpClient();");
    sb.AppendLine($"var service = new {serviceClass}(\"YOUR_API_KEY\", httpClient);");
    sb.AppendLine($"service.ChangeModel(AIModel.{modelEnum});");
    sb.AppendLine();

    // Settings
    sb.AppendLine($"// 2. Configure settings");
    if (!string.IsNullOrEmpty(svc.SystemMessage))
        sb.AppendLine($"service.SystemMessage = \"{EscapeSnippetString(svc.SystemMessage)}\";");
    sb.AppendLine($"service.Temperature = {svc.Temperature:F2}f;");
    sb.AppendLine($"service.TopP = {svc.TopP:F2}f;");
    sb.AppendLine($"service.MaxTokens = {svc.MaxTokens};");
    sb.AppendLine($"service.MaxMessageCount = {svc.MaxMessageCount};");
    if (svc.StatelessMode)
        sb.AppendLine($"service.StatelessMode = true;");

    // Reasoning settings
    if (svc is ChatGptService gpt)
    {
        if (modelEnum.StartsWith("Gpt5") && !modelEnum.StartsWith("Gpt5_1") && !modelEnum.StartsWith("Gpt5_2"))
        {
            if (gpt.Gpt5ReasoningSummary != null)
            {
                sb.AppendLine($"gpt.Gpt5ReasoningEffort = Gpt5Reasoning.{gpt.Gpt5ReasoningEffort};");
                sb.AppendLine($"gpt.Gpt5ReasoningSummary = ReasoningSummary.{gpt.Gpt5ReasoningSummary};");
            }
        }
        else if (modelEnum.StartsWith("Gpt5_1"))
        {
            if (gpt.Gpt5_1ReasoningSummary != null)
            {
                sb.AppendLine($"gpt.Gpt5_1ReasoningEffort = Gpt5_1Reasoning.{gpt.Gpt5_1ReasoningEffort};");
                sb.AppendLine($"gpt.Gpt5_1ReasoningSummary = ReasoningSummary.{gpt.Gpt5_1ReasoningSummary};");
            }
        }
        else if (modelEnum.StartsWith("Gpt5_2"))
        {
            if (gpt.Gpt5_2ReasoningSummary != null)
            {
                sb.AppendLine($"gpt.Gpt5_2ReasoningEffort = Gpt5_2Reasoning.{gpt.Gpt5_2ReasoningEffort};");
                sb.AppendLine($"gpt.Gpt5_2ReasoningSummary = ReasoningSummary.{gpt.Gpt5_2ReasoningSummary};");
            }
        }
    }
    else if (svc is ClaudeService claude && claude.ThinkingBudget > 0)
    {
        sb.AppendLine($"claude.ThinkingBudget = {claude.ThinkingBudget};");
    }

    sb.AppendLine();

    // Function Calling — if functions are registered
    var hasFunctions = svc.Functions.Count > 0;
    if (hasFunctions)
    {
        sb.AppendLine($"// 3. Register functions (Function Calling)");
        foreach (var fn in svc.Functions)
        {
            var props = fn.Parameters?.Properties;
            var required = fn.Parameters?.Required ?? new List<string>();
            if (props == null || props.Count == 0)
            {
                sb.AppendLine($"service.WithFunction(");
                sb.AppendLine($"    \"{fn.Name}\",");
                sb.AppendLine($"    \"{EscapeSnippetString(fn.Description ?? "")}\",");
                sb.AppendLine($"    args => {{ /* your logic here */ return \"result\"; }});");
            }
            else
            {
                var typeParams = string.Join(", ", props.Values.Select(p => JsonTypeToCSharp(p.Type)));
                sb.AppendLine($"service.WithFunction<{typeParams}>(");
                sb.AppendLine($"    \"{fn.Name}\",");
                sb.AppendLine($"    \"{EscapeSnippetString(fn.Description ?? "")}\",");
                foreach (var kvp in props)
                {
                    var isReq = required.Contains(kvp.Key);
                    sb.AppendLine($"    (\"{kvp.Key}\", \"{EscapeSnippetString(kvp.Value.Description ?? "")}\", {(isReq ? "true" : "false")}),");
                }
                var lambdaParams = string.Join(", ", props.Keys);
                sb.AppendLine($"    ({lambdaParams}) =>");
                sb.AppendLine($"    {{");
                sb.AppendLine($"        // Your function logic here");
                sb.AppendLine($"        return \"result\";");
                sb.AppendLine($"    }});");
            }
            sb.AppendLine();
        }
    }

    // Send message
    var stepNum = hasFunctions ? 4 : 3;
    var escapedMsg = EscapeSnippetString(userMessage ?? "Hello!");
    sb.AppendLine($"// {stepNum}. Send a message and stream the response");
    sb.AppendLine($"var message = new Message(ActorRole.User, \"{escapedMsg}\");");
    sb.AppendLine($"var options = new StreamOptions");
    sb.AppendLine($"{{");
    sb.AppendLine($"    IncludeReasoning = true,");
    if (hasFunctions)
        sb.AppendLine($"    IncludeFunctionCalls = true,");
    sb.AppendLine($"    TextOnly = false");
    sb.AppendLine($"}};");
    sb.AppendLine();
    sb.AppendLine($"await foreach (var chunk in service.StreamAsync(message, options))");
    sb.AppendLine($"{{");
    sb.AppendLine($"    switch (chunk.Type)");
    sb.AppendLine($"    {{");
    sb.AppendLine($"        case StreamingContentType.Reasoning:");
    sb.AppendLine($"            Console.Write($\"[Thinking] {{chunk.Content}}\");");
    sb.AppendLine($"            break;");
    sb.AppendLine($"        case StreamingContentType.Text:");
    sb.AppendLine($"            Console.Write(chunk.Content);");
    sb.AppendLine($"            break;");
    if (hasFunctions)
    {
        sb.AppendLine($"        case StreamingContentType.FunctionCall:");
        sb.AppendLine($"            var fnName = chunk.Metadata?[\"function_name\"];");
        sb.AppendLine($"            Console.WriteLine($\"\\n[Function Call] {{fnName}}\");");
        sb.AppendLine($"            break;");
        sb.AppendLine($"        case StreamingContentType.FunctionResult:");
        sb.AppendLine($"            var resultName = chunk.Metadata?[\"function_name\"];");
        sb.AppendLine($"            var result = chunk.Metadata?[\"result\"];");
        sb.AppendLine($"            Console.WriteLine($\"[Function Result] {{resultName}}: {{result}}\");");
        sb.AppendLine($"            break;");
    }
    sb.AppendLine($"    }}");
    sb.AppendLine($"}}");

    // Alternative: simple non-streaming
    sb.AppendLine();
    sb.AppendLine($"// Alternative: Non-streaming (simple)");
    sb.AppendLine($"// string response = await service.SendAsync(\"{escapedMsg}\");");
    sb.AppendLine($"// Console.WriteLine(response);");

    return sb.ToString();
}

static string EscapeSnippetString(string s)
{
    return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");
}

static string JsonTypeToCSharp(string jsonType) => jsonType?.ToLower() switch
{
    "string" => "string",
    "integer" => "int",
    "number" => "double",
    "boolean" => "bool",
    "array" => "string[]",
    _ => "string"
};

// ── Preset Function Registration ────────────────────────────────
static void RegisterPresetFunctions(AIService service)
{
    var fetchClient = new HttpClient();
    fetchClient.Timeout = TimeSpan.FromSeconds(15);
    fetchClient.DefaultRequestHeaders.Add("User-Agent", "Mythosia.AI-ChatUI/1.0");

    service.WithFunction<string, int>(
        "get_url_content",
        "Fetches the text content of a web page at the given URL. Returns the extracted text (HTML tags stripped). Use this when the user asks to read, summarize, or analyze a web page.",
        ("url", "The full URL to fetch (must start with http:// or https://)", true),
        ("max_length", "Maximum number of characters to return (default: 5000)", false),
        (url, maxLength) =>
        {
            try
            {
                if (string.IsNullOrWhiteSpace(url))
                    return "{\"error\": \"URL is required\"}";

                if (!url.StartsWith("http://") && !url.StartsWith("https://"))
                    url = "https://" + url;

                // Basic SSRF protection
                if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
                {
                    var host = uri.Host.ToLower();
                    if (host == "localhost" || host == "127.0.0.1" || host == "::1" || host.StartsWith("192.168.") || host.StartsWith("10.") || host.StartsWith("172."))
                        return "{\"error\": \"Access to local/private addresses is not allowed\"}";
                }

                var effectiveMax = maxLength > 0 ? maxLength : 5000;

                var response = fetchClient.GetAsync(url).GetAwaiter().GetResult();
                response.EnsureSuccessStatusCode();

                var html = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();

                // Strip HTML tags to get plain text
                var text = StripHtml(html);

                // Truncate
                if (text.Length > effectiveMax)
                    text = text.Substring(0, effectiveMax) + "\n\n[... truncated]";

                return JsonSerializer.Serialize(new { url, length = text.Length, content = text });
            }
            catch (HttpRequestException ex)
            {
                return JsonSerializer.Serialize(new { error = $"HTTP error: {ex.Message}", url });
            }
            catch (TaskCanceledException)
            {
                return JsonSerializer.Serialize(new { error = "Request timed out (15s)", url });
            }
            catch (Exception ex)
            {
                return JsonSerializer.Serialize(new { error = ex.Message, url });
            }
        });
}

static string StripHtml(string html)
{
    // Remove script and style blocks
    html = Regex.Replace(html, @"<script[^>]*>[\s\S]*?</script>", "", RegexOptions.IgnoreCase);
    html = Regex.Replace(html, @"<style[^>]*>[\s\S]*?</style>", "", RegexOptions.IgnoreCase);
    // Remove HTML tags
    html = Regex.Replace(html, @"<[^>]+>", " ");
    // Decode common HTML entities
    html = html.Replace("&nbsp;", " ").Replace("&amp;", "&").Replace("&lt;", "<").Replace("&gt;", ">").Replace("&quot;", "\"");
    // Collapse whitespace
    html = Regex.Replace(html, @"\s+", " ").Trim();
    return html;
}

// ── Request DTOs ────────────────────────────────────────────────
record ConfigureRequest(string? ApiKey, string? Model, string? SystemMessage);
record ChatRequest(string? Message);
record SettingsRequest(
    float? Temperature,
    float? TopP,
    int? MaxTokens,
    int? MaxMessageCount,
    float? FrequencyPenalty,
    float? PresencePenalty,
    bool? StatelessMode,
    string? SystemMessage,
    bool? ReasoningEnabled,
    string? ReasoningLevel,
    string? ReasoningType);
record CodeSnippetRequest(string? UserMessage);
record TogglePresetRequest(bool Enabled);
record SummaryPolicyRequest(bool Enabled, string? TriggerType, int Threshold, int KeepRecent);
