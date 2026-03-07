using Mythosia.VectorDb;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Mythosia.VectorDb.Pinecone
{
    /// <summary>
    /// Pinecone implementation of <see cref="IVectorStore"/>.
    ///
    /// Mapping model:
    /// - Collection (physical) -> Pinecone index (via <see cref="PineconeOptions.IndexHost"/>)
    /// - Namespace (1st-tier logical) -> Pinecone namespace
    /// - Scope (2nd-tier logical) -> reserved metadata key <c>_scope</c>
    /// </summary>
    public class PineconeStore : IVectorStore, IDisposable
    {
        private readonly PineconeOptions _options;
        private readonly HttpClient _httpClient;
        private readonly bool _ownsHttpClient;

        private const string MetadataKeyContent = "_content";
        private const string MetadataKeyScope = "_scope";

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        private static readonly JsonSerializerOptions RequestJsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        /// <summary>
        /// Creates a new <see cref="PineconeStore"/> that owns its <see cref="HttpClient"/>.
        /// </summary>
        public PineconeStore(PineconeOptions options)
        {
            options.Validate();
            _options = options;

            _httpClient = new HttpClient
            {
                BaseAddress = NormalizeIndexHost(options.IndexHost),
                Timeout = TimeSpan.FromSeconds(options.RequestTimeoutSeconds)
            };
            _ownsHttpClient = true;
        }

        /// <summary>
        /// Creates a new <see cref="PineconeStore"/> using an externally managed <see cref="HttpClient"/>.
        /// </summary>
        public PineconeStore(PineconeOptions options, HttpClient httpClient)
        {
            options.Validate();
            _options = options;
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));

            if (_httpClient.BaseAddress == null)
                _httpClient.BaseAddress = NormalizeIndexHost(options.IndexHost);

            _ownsHttpClient = false;
        }

        #region IVectorStore - Upsert

        public async Task UpsertAsync(VectorRecord record, CancellationToken cancellationToken = default)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            await UpsertBatchAsync(new[] { record }, cancellationToken);
        }

        public async Task UpsertBatchAsync(IEnumerable<VectorRecord> records, CancellationToken cancellationToken = default)
        {
            if (records == null) throw new ArgumentNullException(nameof(records));

            var materialized = records.ToList();
            if (materialized.Count == 0)
                return;

            foreach (var nsGroup in materialized.GroupBy(r => ResolveNamespace(r.Namespace), StringComparer.Ordinal))
            {
                var vectors = nsGroup.Select(ToUpsertVector).ToList();

                for (var i = 0; i < vectors.Count; i += _options.UpsertBatchSize)
                {
                    var batch = vectors.Skip(i).Take(_options.UpsertBatchSize).ToList();
                    var request = new UpsertRequest
                    {
                        Namespace = nsGroup.Key,
                        Vectors = batch
                    };

                    await SendNoContentAsync(HttpMethod.Post, "vectors/upsert", request, cancellationToken);
                }
            }
        }

        #endregion

        #region IVectorStore - Get / Delete

        public async Task<VectorRecord?> GetAsync(string id, VectorFilter? filter = null, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("id must not be empty.", nameof(id));

            var ns = ResolveNamespace(filter?.Namespace);
            var path = BuildFetchPath(id, ns);
            var response = await SendAsync<FetchResponse>(HttpMethod.Get, path, null, cancellationToken);

            if (response.Vectors == null || !response.Vectors.TryGetValue(id, out var vector))
                return null;

            var record = ToVectorRecord(vector, ns);
            if (filter != null && !MatchesFilter(record, filter))
                return null;

            return record;
        }

        public async Task DeleteAsync(string id, VectorFilter? filter = null, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("id must not be empty.", nameof(id));

            // When additional filters are present, ensure the target matches first.
            if (filter != null && (filter.Scope != null || filter.MetadataMatch != null))
            {
                var existing = await GetAsync(id, filter, cancellationToken);
                if (existing == null)
                    return;
            }

            var request = new DeleteRequest
            {
                Namespace = ResolveNamespace(filter?.Namespace),
                Ids = new List<string> { id }
            };

            await SendNoContentAsync(HttpMethod.Post, "vectors/delete", request, cancellationToken);
        }

        public async Task DeleteByFilterAsync(VectorFilter filter, CancellationToken cancellationToken = default)
        {
            if (filter == null) throw new ArgumentNullException(nameof(filter));

            var metadataFilter = BuildMetadataFilter(filter);
            var request = new DeleteRequest
            {
                Namespace = ResolveNamespace(filter.Namespace)
            };

            if (metadataFilter == null)
            {
                request.DeleteAll = true;
            }
            else
            {
                request.Filter = metadataFilter;
            }

            await SendNoContentAsync(HttpMethod.Post, "vectors/delete", request, cancellationToken);
        }

        #endregion

        #region IVectorStore - Search

        public async Task<IReadOnlyList<VectorSearchResult>> SearchAsync(
            float[] queryVector,
            int topK = 5,
            VectorFilter? filter = null,
            CancellationToken cancellationToken = default)
        {
            if (queryVector == null) throw new ArgumentNullException(nameof(queryVector));
            if (topK <= 0) throw new ArgumentOutOfRangeException(nameof(topK), "topK must be greater than 0.");

            var request = new QueryRequest
            {
                Namespace = ResolveNamespace(filter?.Namespace),
                Vector = queryVector,
                TopK = topK,
                IncludeMetadata = true,
                IncludeValues = true,
                Filter = BuildMetadataFilter(filter)
            };

            var response = await SendAsync<QueryResponse>(HttpMethod.Post, "query", request, cancellationToken);
            var matches = response.Matches ?? new List<QueryMatch>();

            var minScore = filter?.MinScore;
            var results = new List<VectorSearchResult>(matches.Count);

            foreach (var match in matches)
            {
                if (minScore.HasValue && match.Score < minScore.Value)
                    continue;

                var record = ToVectorRecord(match, request.Namespace);
                results.Add(new VectorSearchResult(record, match.Score));
            }

            return results;
        }

        #endregion

        #region Helpers - HTTP

        private async Task SendNoContentAsync(
            HttpMethod method,
            string path,
            object payload,
            CancellationToken cancellationToken)
        {
            await SendAsync<object>(method, path, payload, cancellationToken);
        }

        private async Task<TResponse> SendAsync<TResponse>(
            HttpMethod method,
            string path,
            object? payload,
            CancellationToken cancellationToken)
        {
            using var request = new HttpRequestMessage(method, path);
            request.Headers.TryAddWithoutValidation("Api-Key", _options.ApiKey);
            request.Headers.TryAddWithoutValidation("Accept", "application/json");

            if (payload != null)
            {
                var json = JsonSerializer.Serialize(payload, RequestJsonOptions);
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            }

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"Pinecone request failed ({(int)response.StatusCode}): {body}");

            if (typeof(TResponse) == typeof(object) || string.IsNullOrWhiteSpace(body))
                return (TResponse)(object)new object();

            var result = JsonSerializer.Deserialize<TResponse>(body, JsonOptions);
            if (result == null)
                throw new InvalidOperationException("Failed to deserialize Pinecone response.");

            return result;
        }

        private static Uri NormalizeIndexHost(string indexHost)
        {
            var host = indexHost.Trim();
            if (!host.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !host.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                host = "https://" + host;
            }

            if (!host.EndsWith("/", StringComparison.Ordinal))
                host += "/";

            return new Uri(host, UriKind.Absolute);
        }

        private static string BuildFetchPath(string id, string? @namespace)
        {
            var path = new StringBuilder("vectors/fetch?ids=")
                .Append(Uri.EscapeDataString(id));

            if (!string.IsNullOrWhiteSpace(@namespace))
            {
                path.Append("&namespace=")
                    .Append(Uri.EscapeDataString(@namespace));
            }

            return path.ToString();
        }

        #endregion

        #region Helpers - Mapping

        private string? ResolveNamespace(string? explicitNamespace)
        {
            return explicitNamespace ?? _options.DefaultNamespace;
        }

        private static UpsertVector ToUpsertVector(VectorRecord record)
        {
            if (string.IsNullOrWhiteSpace(record.Id))
                throw new ArgumentException("VectorRecord.Id must not be empty.", nameof(record));

            var metadata = new Dictionary<string, string>(StringComparer.Ordinal);

            if (record.Metadata != null)
            {
                foreach (var kvp in record.Metadata)
                {
                    if (string.IsNullOrWhiteSpace(kvp.Key))
                        continue;

                    metadata[kvp.Key] = kvp.Value ?? string.Empty;
                }
            }

            metadata[MetadataKeyContent] = record.Content ?? string.Empty;
            if (record.Scope != null)
                metadata[MetadataKeyScope] = record.Scope;

            return new UpsertVector
            {
                Id = record.Id,
                Values = record.Vector ?? Array.Empty<float>(),
                Metadata = metadata
            };
        }

        private static VectorRecord ToVectorRecord(QueryMatch match, string? @namespace)
        {
            var content = string.Empty;
            string? scope = null;
            var metadata = new Dictionary<string, string>(StringComparer.Ordinal);

            if (match.Metadata != null)
                ParseMetadata(match.Metadata, ref content, ref scope, metadata);

            return new VectorRecord
            {
                Id = match.Id ?? string.Empty,
                Vector = match.Values ?? Array.Empty<float>(),
                Content = content,
                Namespace = @namespace,
                Scope = scope,
                Metadata = metadata
            };
        }

        private static VectorRecord ToVectorRecord(FetchVector vector, string? @namespace)
        {
            var content = string.Empty;
            string? scope = null;
            var metadata = new Dictionary<string, string>(StringComparer.Ordinal);

            if (vector.Metadata != null)
                ParseMetadata(vector.Metadata, ref content, ref scope, metadata);

            return new VectorRecord
            {
                Id = vector.Id ?? string.Empty,
                Vector = vector.Values ?? Array.Empty<float>(),
                Content = content,
                Namespace = @namespace,
                Scope = scope,
                Metadata = metadata
            };
        }

        private static void ParseMetadata(
            Dictionary<string, JsonElement> source,
            ref string content,
            ref string? scope,
            Dictionary<string, string> metadata)
        {
            foreach (var kvp in source)
            {
                var value = JsonElementToString(kvp.Value);

                if (kvp.Key == MetadataKeyContent)
                {
                    content = value;
                    continue;
                }

                if (kvp.Key == MetadataKeyScope)
                {
                    scope = value;
                    continue;
                }

                metadata[kvp.Key] = value;
            }
        }

        private static string JsonElementToString(JsonElement element)
        {
            return element.ValueKind switch
            {
                JsonValueKind.String => element.GetString() ?? string.Empty,
                JsonValueKind.Number => element.GetRawText(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                JsonValueKind.Null => string.Empty,
                _ => element.GetRawText()
            };
        }

        #endregion

        #region Helpers - Filtering

        private static bool MatchesFilter(VectorRecord record, VectorFilter filter)
        {
            if (filter.Scope != null && !string.Equals(record.Scope, filter.Scope, StringComparison.Ordinal))
                return false;

            if (filter.MetadataMatch != null)
            {
                foreach (var kvp in filter.MetadataMatch)
                {
                    if (!record.Metadata.TryGetValue(kvp.Key, out var value) ||
                        !string.Equals(value, kvp.Value, StringComparison.Ordinal))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private static object? BuildMetadataFilter(VectorFilter? filter)
        {
            if (filter == null)
                return null;

            var conditions = new List<Dictionary<string, object>>();

            if (filter.Scope != null)
            {
                conditions.Add(new Dictionary<string, object>
                {
                    [MetadataKeyScope] = new Dictionary<string, object>
                    {
                        ["$eq"] = filter.Scope
                    }
                });
            }

            if (filter.MetadataMatch != null)
            {
                foreach (var kvp in filter.MetadataMatch)
                {
                    conditions.Add(new Dictionary<string, object>
                    {
                        [kvp.Key] = new Dictionary<string, object>
                        {
                            ["$eq"] = kvp.Value
                        }
                    });
                }
            }

            if (conditions.Count == 0)
                return null;

            if (conditions.Count == 1)
                return conditions[0];

            return new Dictionary<string, object>
            {
                ["$and"] = conditions
            };
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            if (_ownsHttpClient)
                _httpClient.Dispose();
        }

        #endregion

        #region DTOs

        private sealed class UpsertRequest
        {
            public string? Namespace { get; set; }
            public List<UpsertVector> Vectors { get; set; } = new List<UpsertVector>();
        }

        private sealed class UpsertVector
        {
            public string Id { get; set; } = string.Empty;
            public float[] Values { get; set; } = Array.Empty<float>();
            public Dictionary<string, string> Metadata { get; set; } = new Dictionary<string, string>(StringComparer.Ordinal);
        }

        private sealed class QueryRequest
        {
            public string? Namespace { get; set; }
            public float[] Vector { get; set; } = Array.Empty<float>();
            public int TopK { get; set; }
            public bool IncludeMetadata { get; set; }
            public bool IncludeValues { get; set; }
            public object? Filter { get; set; }
        }

        private sealed class QueryResponse
        {
            public List<QueryMatch>? Matches { get; set; }
        }

        private sealed class QueryMatch
        {
            public string? Id { get; set; }
            public double Score { get; set; }
            public float[]? Values { get; set; }
            public Dictionary<string, JsonElement>? Metadata { get; set; }
        }

        private sealed class FetchResponse
        {
            public Dictionary<string, FetchVector>? Vectors { get; set; }
        }

        private sealed class FetchVector
        {
            public string? Id { get; set; }
            public float[]? Values { get; set; }
            public Dictionary<string, JsonElement>? Metadata { get; set; }
        }

        private sealed class DeleteRequest
        {
            public string? Namespace { get; set; }
            public List<string>? Ids { get; set; }
            public bool? DeleteAll { get; set; }
            public object? Filter { get; set; }
        }

        #endregion
    }
}
