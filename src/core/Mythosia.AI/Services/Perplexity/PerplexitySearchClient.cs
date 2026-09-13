using Mythosia.AI.Exceptions;
using Mythosia.AI.Models.Perplexity;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Mythosia.AI.Services.Perplexity
{
    /// <summary>Calls Perplexity's independent Search API without generating a chat completion.</summary>
    /// <remarks>The caller owns the supplied HttpClient. Requests do not alter its base address or default headers.</remarks>
    public sealed class PerplexitySearchClient
    {
        private readonly string _apiKey;
        private readonly HttpClient _httpClient;

        public PerplexitySearchClient(string apiKey, HttpClient httpClient)
        {
            _apiKey = string.IsNullOrWhiteSpace(apiKey) ? throw new ArgumentException("An API key is required.", nameof(apiKey)) : apiKey;
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        }

        public Task<PerplexitySearchResponse> SearchAsync(string query, PerplexitySearchOptions? options = null,
            CancellationToken cancellationToken = default)
            => SearchCoreAsync(new[] { query }, false, options, cancellationToken);

        public Task<PerplexitySearchResponse> SearchAsync(IEnumerable<string> queries, PerplexitySearchOptions? options = null,
            CancellationToken cancellationToken = default)
            => SearchCoreAsync(queries, true, options, cancellationToken);

        private async Task<PerplexitySearchResponse> SearchCoreAsync(IEnumerable<string> queries, bool multiple,
            PerplexitySearchOptions? options, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (queries == null) throw new ArgumentNullException(nameof(queries));
            var inputs = queries.ToArray();
            if (inputs.Length < 1 || inputs.Length > 5 || inputs.Any(string.IsNullOrWhiteSpace))
                throw new ArgumentException("Provide one to five nonempty search queries.", nameof(queries));
            var body = BuildBody(inputs, multiple, options ?? new PerplexitySearchOptions());
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.perplexity.ai/search")
            {
                Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
            // Buffer under the cancellation token, so error-body reads cannot outlive cancellation.
            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken).ConfigureAwait(false);
            var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (!response.IsSuccessStatusCode)
                throw AIHttpErrorFactory.FromHttp((int)response.StatusCode, response.ReasonPhrase, json);
            return ParseResponse(json);
        }

        private static Dictionary<string, object> BuildBody(string[] queries, bool multiple, PerplexitySearchOptions options)
        {
            if (!Enum.IsDefined(typeof(PerplexitySearchType), options.SearchType)) throw new ArgumentOutOfRangeException(nameof(options.SearchType));
            var maximum = options.SearchType == PerplexitySearchType.People ? 50 : 20;
            if (options.MaxResults < 1 || options.MaxResults > maximum) throw new ArgumentOutOfRangeException(nameof(options.MaxResults));
            if (options.ContentSize.HasValue && !Enum.IsDefined(typeof(PerplexitySearchContentSize), options.ContentSize.Value))
                throw new ArgumentOutOfRangeException(nameof(options.ContentSize));
            if (options.SearchType == PerplexitySearchType.People && options.ContentSize.HasValue)
                throw new ArgumentException("ContentSize is not supported for people search; omit it to use the provider's default result content.", nameof(options));
            if (options.Recency.HasValue && !Enum.IsDefined(typeof(PerplexitySearchRecency), options.Recency.Value))
                throw new ArgumentOutOfRangeException(nameof(options.Recency));
            ValidateBudget(options.MaxTokens, nameof(options.MaxTokens));
            ValidateBudget(options.MaxTokensPerPage, nameof(options.MaxTokensPerPage));
            if (options.ContentSize.HasValue && (options.MaxTokens.HasValue || options.MaxTokensPerPage.HasValue))
                throw new ArgumentException("ContentSize cannot be combined with explicit token budgets.", nameof(options));
            var domains = options.DomainFilter?.ToArray() ?? throw new ArgumentException("DomainFilter cannot be null.", nameof(options));
            if (domains.Length > 20 || domains.Any(domain => string.IsNullOrWhiteSpace(domain) || domain.Length > 253 || domain == "-"))
                throw new ArgumentException("DomainFilter supports at most 20 nonempty domains or domain paths, each up to 253 characters.", nameof(options));
            if (domains.Any(domain => domain.StartsWith("-", StringComparison.Ordinal)) && domains.Any(domain => !domain.StartsWith("-", StringComparison.Ordinal)))
                throw new ArgumentException("Domain allowlists and denylists cannot be mixed.", nameof(options));
            var languages = options.LanguageFilter?.ToArray() ?? throw new ArgumentException("LanguageFilter cannot be null.", nameof(options));
            if (languages.Length > 20 || languages.Any(language => !IsTwoLetterCode(language)))
                throw new ArgumentException("LanguageFilter supports at most 20 two-letter language codes.", nameof(options));
            if (options.Country != null && !IsTwoLetterCode(options.Country)) throw new ArgumentException("Country must be a two-letter country code.", nameof(options));
            ValidateDates(options.PublishedAfter, options.PublishedBefore);
            ValidateDates(options.UpdatedAfter, options.UpdatedBefore);
            var body = new Dictionary<string, object>
            {
                ["query"] = multiple ? (object)queries : queries[0],
                ["max_results"] = options.MaxResults,
                ["search_type"] = options.SearchType.ToString().ToLowerInvariant()
            };
            if (options.Country != null) body["country"] = options.Country.ToUpperInvariant();
            if (domains.Length > 0) body["search_domain_filter"] = domains;
            if (languages.Length > 0) body["search_language_filter"] = languages.Select(language => language.ToLowerInvariant()).ToArray();
            AddDate(body, "search_after_date_filter", options.PublishedAfter);
            AddDate(body, "search_before_date_filter", options.PublishedBefore);
            AddDate(body, "last_updated_after_filter", options.UpdatedAfter);
            AddDate(body, "last_updated_before_filter", options.UpdatedBefore);
            if (options.Recency.HasValue) body["search_recency_filter"] = options.Recency.Value.ToString().ToLowerInvariant();
            if (options.ContentSize.HasValue) body["search_context_size"] = options.ContentSize.Value.ToString().ToLowerInvariant();
            if (options.MaxTokens.HasValue) body["max_tokens"] = options.MaxTokens.Value;
            if (options.MaxTokensPerPage.HasValue) body["max_tokens_per_page"] = options.MaxTokensPerPage.Value;
            return body;
        }

        private static void ValidateBudget(int? value, string name)
        {
            if (value.HasValue && (value.Value < 1 || value.Value > 1000000)) throw new ArgumentOutOfRangeException(name);
        }
        private static bool IsTwoLetterCode(string value) => value != null && value.Length == 2 && value.All(character =>
            (character >= 'a' && character <= 'z') || (character >= 'A' && character <= 'Z'));
        private static void ValidateDates(DateTime? after, DateTime? before)
        {
            if (after.HasValue && before.HasValue && after.Value.Date > before.Value.Date)
                throw new ArgumentException("The start date cannot follow the end date.");
        }
        private static void AddDate(Dictionary<string, object> body, string name, DateTime? value)
        {
            if (value.HasValue) body[name] = value.Value.ToString("MM/dd/yyyy", CultureInfo.InvariantCulture);
        }
        private static PerplexitySearchResponse ParseResponse(string json)
        {
            try
            {
                using var document = JsonDocument.Parse(json);
                var root = document.RootElement;
                if (!root.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array)
                    throw new JsonException("Search response must contain a results array.");
                var pages = new List<PerplexitySearchResult>();
                foreach (var page in results.EnumerateArray())
                {
                    var url = ReadString(page, "url", true)!;
                    if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed) || (parsed.Scheme != "https" && parsed.Scheme != "http"))
                        throw new JsonException("Search result URL must be an absolute HTTP or HTTPS URL.");
                    pages.Add(new PerplexitySearchResult
                    {
                        Rank = pages.Count + 1, Title = ReadString(page, "title", true)!, Url = url,
                        Snippet = ReadString(page, "snippet", true)!, Date = ReadString(page, "date", false),
                        LastUpdated = ReadString(page, "last_updated", false)
                    });
                }
                return new PerplexitySearchResponse
                {
                    Id = ReadString(root, "id", true)!, ServerTime = ReadString(root, "server_time", false), Results = pages.AsReadOnly()
                };
            }
            catch (Exception exception) when (exception is JsonException || exception is InvalidOperationException)
            {
                throw new AIServiceException("Perplexity Search returned an invalid response.", exception);
            }
        }
        private static string? ReadString(JsonElement element, string name, bool required)
        {
            if (!element.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
            {
                if (required) throw new JsonException("Search response is missing " + name + ".");
                return null;
            }
            return value.GetString();
        }
    }
}
