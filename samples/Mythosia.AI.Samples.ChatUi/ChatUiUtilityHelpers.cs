using Mythosia.AI.Extensions;
using Mythosia.AI.Loaders;
using Mythosia.AI.Loaders.Office.Excel;
using Mythosia.AI.Loaders.Office.PowerPoint;
using Mythosia.AI.Loaders.Office.Word;
using Mythosia.AI.Loaders.Pdf;
using Mythosia.AI.Rag;
using Mythosia.AI.Rag.Embeddings;
using Mythosia.AI.Rag.Loaders;
using Mythosia.AI.Rag.Splitters;
using Mythosia.AI.Services.Base;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Mythosia.AI.Samples.ChatUi
{
    internal static class ChatUiUtilityHelpers
    {
        public static string GenerateCodeSnippet(AIService svc, string provider, string modelEnum, string? userMessage)
        {
            var serviceClass = provider switch
            {
                "OpenAI" => "ChatGptService",
                "Anthropic" => "ClaudeService",
                "Google" => "GeminiService",
                "DeepSeek" => "DeepSeekService",
                "xAI" => "GrokService",
                "Perplexity" => "SonarService",
                _ => "ChatGptService"
            };

            var escapedApiKey = "YOUR_API_KEY";
            var escapedMsg = EscapeSnippetString(userMessage ?? "Hello!");
            var escapedSystem = EscapeSnippetString(svc.SystemMessage ?? "");

            var sb = new StringBuilder();
            sb.AppendLine("using Mythosia.AI;");
            sb.AppendLine("using Mythosia.AI.Services.OpenAI;");
            sb.AppendLine("using Mythosia.AI.Services.Anthropic;");
            sb.AppendLine("using Mythosia.AI.Services.Google;");
            sb.AppendLine("using Mythosia.AI.Services.DeepSeek;");
            sb.AppendLine("using Mythosia.AI.Services.xAI;");
            sb.AppendLine("using Mythosia.AI.Services.Perplexity;");
            sb.AppendLine("using Mythosia.AI.Models;");
            sb.AppendLine("using Mythosia.AI.Models.Messages;");
            sb.AppendLine("using Mythosia.AI.Models.Streaming;");
            sb.AppendLine("using System.Net.Http;");
            sb.AppendLine();
            sb.AppendLine($"var httpClient = new HttpClient();");
            sb.AppendLine($"var service = new {serviceClass}(\"{escapedApiKey}\", httpClient);");
            var modelValue = ChatUiModelHelpers.FindModelValueByName(modelEnum) ?? modelEnum;
            sb.AppendLine($"service.ChangeModel(\"{EscapeSnippetString(modelValue)}\");");

            if (!string.IsNullOrWhiteSpace(escapedSystem))
                sb.AppendLine($"service.SystemMessage = \"{escapedSystem}\";");

            sb.AppendLine();

            var hasFunctions = svc.Functions.Count > 0;
            if (hasFunctions)
            {
                sb.AppendLine($"// Function calling settings");
                sb.AppendLine($"service.EnableFunctions = {svc.EnableFunctions.ToString().ToLower()};");
                sb.AppendLine($"service.FunctionCallMode = FunctionCallMode.{svc.FunctionCallMode};");
                if (!string.IsNullOrWhiteSpace(svc.ForceFunctionName))
                    sb.AppendLine($"service.ForceFunctionName = \"{EscapeSnippetString(svc.ForceFunctionName)}\";");
                sb.AppendLine();
            }

            sb.AppendLine($"// Generation settings");
            sb.AppendLine($"service.Temperature = {svc.Temperature}f;");
            sb.AppendLine($"service.TopP = {svc.TopP}f;");
            sb.AppendLine($"service.MaxTokens = {svc.MaxTokens};");
            sb.AppendLine($"service.MaxMessageCount = {svc.MaxMessageCount};");
            sb.AppendLine($"service.StatelessMode = {svc.StatelessMode.ToString().ToLower()};");
            sb.AppendLine();

            sb.AppendLine($"// Streaming with reasoning/function chunks");
            sb.AppendLine($"var message = new Message(ActorRole.User, \"{escapedMsg}\");");
            sb.AppendLine($"var options = new StreamOptions");
            sb.AppendLine($"{{");
            sb.AppendLine($"    IncludeReasoning = true,");
            sb.AppendLine($"    IncludeMetadata = true,");
            sb.AppendLine($"    IncludeFunctionCalls = {svc.ShouldUseFunctions.ToString().ToLower()},");
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
            sb.AppendLine();
            sb.AppendLine($"// Alternative: Non-streaming (simple)");
            sb.AppendLine($"// string response = await service.SendAsync(\"{escapedMsg}\");");
            sb.AppendLine($"// Console.WriteLine(response);");

            return sb.ToString();
        }

        public static string GenerateRagReferenceCodeSnippet(RagReferenceConfig config)
        {
            var sb = new StringBuilder();

            sb.AppendLine("using Mythosia.AI.Rag;");
            sb.AppendLine("using Mythosia.AI.Rag.Embeddings;");
            sb.AppendLine("using Mythosia.AI.Rag.Splitters;");
            sb.AppendLine("using Mythosia.AI.Services.OpenAI;");
            sb.AppendLine("using System.Net.Http;");
            sb.AppendLine();
            sb.AppendLine("// 1. Create your AI service and enable RAG (extension method)");
            sb.AppendLine("var service = new ChatGptService(\"YOUR_API_KEY\", new HttpClient())");
            sb.AppendLine("    .WithRag(rag => rag");

            if (config.Sources.Count == 0)
            {
                sb.AppendLine("        // .AddDocument(\"manual.pdf\")");
            }
            else
            {
                foreach (var source in config.Sources)
                    sb.AppendLine($"        .AddDocument(\"{EscapeSnippetString(source)}\")");
            }

            sb.AppendLine($"        .WithTextSplitter({BuildRagTextSplitterSnippet(config)})");

            switch (string.IsNullOrWhiteSpace(config.EmbeddingProvider) ? null : config.EmbeddingProvider.Trim().ToLowerInvariant())
            {
                case "ollama":
                    sb.AppendLine($"        .UseEmbedding(new OllamaEmbeddingProvider(new HttpClient(), model: \"{EscapeSnippetString(config.EmbeddingModel)}\", dimensions: {config.EmbeddingDimensions}, baseUrl: \"{EscapeSnippetString(config.EmbeddingBaseUrl)}\"))");
                    break;
                case "vllm":
                    sb.AppendLine($"        .UseEmbedding(new VllmEmbeddingProvider(new HttpClient(), model: \"{EscapeSnippetString(config.EmbeddingModel)}\", dimensions: {config.EmbeddingDimensions}, baseUrl: \"{EscapeSnippetString(config.EmbeddingBaseUrl)}\"))");
                    break;
                case "openai":
                    sb.AppendLine($"        .UseOpenAIEmbedding(\"YOUR_OPENAI_API_KEY\", model: \"{EscapeSnippetString(config.EmbeddingModel)}\", dimensions: {config.EmbeddingDimensions})");
                    break;
                default:
                    throw new InvalidOperationException("Embedding provider is required to generate the code snippet.");
            }

            sb.AppendLine("        .UseInMemoryStore()" );
            sb.AppendLine("    );");
            sb.AppendLine();
            sb.AppendLine("// 2. Ask questions");
            sb.AppendLine("// var answer = await service.GetCompletionAsync(\"문서 기준으로 요약해줘\");");

            return sb.ToString();
        }

        public static string EscapeSnippetString(string s)
            => s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");

        public static string JsonTypeToCSharp(string jsonType) => jsonType?.ToLower() switch
        {
            "string" => "string",
            "integer" => "int",
            "number" => "double",
            "boolean" => "bool",
            "array" => "string[]",
            _ => "string"
        };

        public static int ParsePositiveInt(string? value, int fallback)
            => int.TryParse(value, out var parsed) && parsed > 0 ? parsed : fallback;

        public static int? ParseOptionalNonNegativeInt(string? value)
            => int.TryParse(value, out var parsed) && parsed >= 0 ? parsed : null;

        public static string NormalizeRagKey(string? value, string fallback)
            => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim().ToLowerInvariant();

        public static string BuildRagTextSplitterSnippet(RagReferenceConfig config)
        {
            var chunker = NormalizeRagKey(config.Chunker, "character");
            return chunker switch
            {
                "token" => $"new TokenTextSplitter({config.ChunkSize}, {config.ChunkOverlap})",
                "recursive" => $"new RecursiveTextSplitter({config.ChunkSize}, {config.ChunkOverlap})",
                "markdown" => "new MarkdownTextSplitter()",
                _ => $"new CharacterTextSplitter({config.ChunkSize}, {config.ChunkOverlap})"
            };
        }

        public static IEmbeddingProvider BuildOpenAiEmbeddingProvider(string? apiKey, HttpClient httpClient, string model, int dimensions)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new InvalidOperationException("OpenAI API key is required.");

            return new OpenAIEmbeddingProvider(apiKey, httpClient, model, dimensions);
        }

        public static double? ParseOptionalDouble(string? value)
            => double.TryParse(value, out var parsed) ? parsed : null;

        public static ITextSplitter BuildTextSplitter(string? chunkerKey, int chunkSize, int chunkOverlap)
        {
            var normalized = chunkerKey?.Trim().ToLowerInvariant();
            return normalized switch
            {
                "token" => new TokenTextSplitter(chunkSize, chunkOverlap),
                "recursive" => new RecursiveTextSplitter(chunkSize, chunkOverlap),
                "markdown" => new MarkdownTextSplitter(),
                _ => new CharacterTextSplitter(chunkSize, chunkOverlap)
            };
        }

        public static IDocumentLoader CreateLoaderForExtension(string extension)
        {
            if (string.IsNullOrWhiteSpace(extension))
                return new PlainTextDocumentLoader();

            var normalized = extension.Trim();
            if (!normalized.StartsWith(".", StringComparison.Ordinal))
                normalized = "." + normalized;

            return normalized.ToLowerInvariant() switch
            {
                ".docx" => new WordDocumentLoader(),
                ".xlsx" => new ExcelDocumentLoader(),
                ".pptx" => new PowerPointDocumentLoader(),
                ".pdf" => new PdfDocumentLoader(),
                _ => new PlainTextDocumentLoader()
            };
        }

        public static void RegisterPresetFunctions(AIService service)
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

        /// <summary>
        /// Translates raw RAG/vector-store exceptions into user-friendly messages.
        /// Returns null if no special translation applies.
        /// </summary>
        public static string HumanizeRagError(string rawMessage, int configuredDimension = 0)
        {
            if (string.IsNullOrWhiteSpace(rawMessage))
                return rawMessage;

            // Qdrant dimension mismatch: "expected dim: 2560, got 1536"
            var dimMatch = Regex.Match(rawMessage, @"expected dim:\s*(\d+),\s*got\s*(\d+)", RegexOptions.IgnoreCase);
            if (dimMatch.Success)
            {
                var expected = dimMatch.Groups[1].Value;
                var actual = dimMatch.Groups[2].Value;
                return $"Vector dimension mismatch — the collection expects {expected}-dim vectors "
                    + $"but the current embedding model produces {actual}-dim vectors. "
                    + $"Either change the embedding model/dimensions to {expected}, "
                    + $"or recreate the collection with {actual} dimensions.";
            }

            // pgvector dimension mismatch: "expected 2560 dimensions, not 1536"
            var pgDimMatch = Regex.Match(rawMessage, @"expected\s+(\d+)\s+dimensions?,\s*not\s+(\d+)", RegexOptions.IgnoreCase);
            if (pgDimMatch.Success)
            {
                var expected = pgDimMatch.Groups[1].Value;
                var actual = pgDimMatch.Groups[2].Value;
                return $"Vector dimension mismatch — the table expects {expected}-dim vectors "
                    + $"but the current embedding model produces {actual}-dim vectors. "
                    + $"Either change the embedding model/dimensions to {expected}, "
                    + $"or recreate the table with {actual} dimensions.";
            }

            return rawMessage;
        }

        public static string StripHtml(string html)
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
    }
}
