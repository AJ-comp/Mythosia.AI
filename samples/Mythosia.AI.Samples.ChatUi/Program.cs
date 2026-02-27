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
using System.Reflection;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.UseStaticFiles();

// ── Shared state ────────────────────────────────────────────────
AIService? currentService = null;
string? currentProvider = null;
string? currentModelEnum = null;

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
        var message = new Message(ActorRole.User, req.Message);
        var options = new StreamOptions
        {
            IncludeReasoning = true,
            IncludeMetadata = false,
            IncludeFunctionCalls = false,
            TextOnly = false
        };

        await foreach (var sc in currentService.StreamAsync(message, options, ctx.RequestAborted))
        {
            string type = sc.Type switch
            {
                StreamingContentType.Reasoning => "reasoning",
                StreamingContentType.Text => "text",
                StreamingContentType.Error => "error",
                _ => null
            };
            if (type == null) continue;

            // For error types, fall back to metadata if Content is null
            var content = sc.Content
                ?? sc.Metadata?.GetValueOrDefault("error")?.ToString()
                ?? "(unknown error)";
            if (type != "error" && sc.Content == null) continue;

            var payload = JsonSerializer.Serialize(new { type, content });
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

// ── GET /api/code-snippet ────────────────────────────────────────
app.MapPost("/api/code-snippet", (CodeSnippetRequest req) =>
{
    if (currentService == null)
        return Results.BadRequest(new { error = "Service not configured" });

    var svc = currentService;
    var code = GenerateCodeSnippet(svc, currentProvider!, currentModelEnum!, req.UserMessage);
    return Results.Ok(new { code });
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

    // Send message
    var escapedMsg = EscapeSnippetString(userMessage ?? "Hello!");
    sb.AppendLine($"// 3. Send a message and stream the response");
    sb.AppendLine($"var message = new Message(ActorRole.User, \"{escapedMsg}\");");
    sb.AppendLine($"var options = new StreamOptions");
    sb.AppendLine($"{{");
    sb.AppendLine($"    IncludeReasoning = true,");
    sb.AppendLine($"    TextOnly = false");
    sb.AppendLine($"}};");
    sb.AppendLine();
    sb.AppendLine($"await foreach (var chunk in service.StreamAsync(message, options))");
    sb.AppendLine($"{{");
    sb.AppendLine($"    if (chunk.Type == StreamingContentType.Reasoning)");
    sb.AppendLine($"        Console.Write($\"[Thinking] {{chunk.Content}}\");");
    sb.AppendLine($"    else if (chunk.Type == StreamingContentType.Text)");
    sb.AppendLine($"        Console.Write(chunk.Content);");
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
