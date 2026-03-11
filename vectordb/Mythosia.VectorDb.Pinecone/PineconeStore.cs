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
        private readonly SemaphoreSlim _indexLock = new SemaphoreSlim(1, 1);
        private volatile bool _indexEnsured;

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
                Timeout = TimeSpan.FromSeconds(options.RequestTimeoutSeconds)
            };

            if (!string.IsNullOrWhiteSpace(options.IndexHost))
                _httpClient.BaseAddress = NormalizeIndexHost(options.IndexHost);

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

            if (_httpClient.BaseAddress == null && !string.IsNullOrWhiteSpace(options.IndexHost))
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

            await EnsureIndexAsync(cancellationToken);

            var materialized = records.ToList();
            if (materialized.Count == 0)
                return;

            foreach (var nsGroup in materialized.GroupBy(r => ResolveNamespace(r.Namespace), StringComparer.Ordinal))
            {
                var vectors = nsGroup.Select(r => ToUpsertVector(r)).ToList();

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

            await EnsureIndexAsync(cancellationToken);

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

            await EnsureIndexAsync(cancellationToken);

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

            await EnsureIndexAsync(cancellationToken);

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

            await EnsureIndexAsync(cancellationToken);

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

        #region IVectorStore - Hybrid Search

        /// <summary>
        /// Performs a native hybrid search using Pinecone's server-side dense + sparse fusion.
        /// Dense vector search and sparse (BM25-based) vector search are combined by Pinecone internally.
        /// Requires the index to use <c>dotproduct</c> metric.
        /// </summary>
        public async Task<IReadOnlyList<VectorSearchResult>> HybridSearchAsync(
            float[] denseVector,
            string query,
            int topK,
            VectorFilter? filter = null,
            CancellationToken cancellationToken = default)
        {
            if (denseVector == null) throw new ArgumentNullException(nameof(denseVector));
            if (string.IsNullOrWhiteSpace(query)) throw new ArgumentException("query must not be empty.", nameof(query));
            if (topK <= 0) throw new ArgumentOutOfRangeException(nameof(topK), "topK must be greater than 0.");

            await EnsureIndexAsync(cancellationToken);

            // Build sparse vector from query using BM25 tokenizer
            var (sparseIndices, sparseValues) = PineconeHelpers.BuildSparseVector(query);

            var request = new QueryRequest
            {
                Namespace = ResolveNamespace(filter?.Namespace),
                Vector = denseVector,
                TopK = topK,
                IncludeMetadata = true,
                IncludeValues = true,
                Filter = BuildMetadataFilter(filter)
            };

            if (sparseIndices.Length > 0)
            {
                request.SparseVector = new SparseValuesDto
                {
                    Indices = sparseIndices,
                    Values = sparseValues
                };
            }

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

        #region Helpers - Index Management

        private async Task EnsureIndexAsync(CancellationToken cancellationToken)
        {
            if (_indexEnsured)
                return;

            if (!_options.AutoCreateIndex)
            {
                _indexEnsured = true;
                return;
            }

            await _indexLock.WaitAsync(cancellationToken);
            try
            {
                if (_indexEnsured)
                    return;

                var host = await GetOrCreateIndexHostAsync(cancellationToken);

                if (_httpClient.BaseAddress == null)
                    _httpClient.BaseAddress = NormalizeIndexHost(host);

                _indexEnsured = true;
            }
            finally
            {
                _indexLock.Release();
            }
        }

        private async Task<string> GetOrCreateIndexHostAsync(CancellationToken cancellationToken)
        {
            var controlPlaneBase = _options.ControlPlaneHost.TrimEnd('/');
            var indexName = _options.IndexName!;
            var describeUrl = $"{controlPlaneBase}/indexes/{Uri.EscapeDataString(indexName)}";

            // Try to describe existing index
            var existing = await SendControlPlaneAsync<DescribeIndexResponse>(
                HttpMethod.Get, describeUrl, null, cancellationToken, throwOnNotFound: false);

            if (existing != null && !string.IsNullOrWhiteSpace(existing.Host))
            {
                if (existing.Status?.Ready == true)
                    return existing.Host!;

                // Index exists but not ready yet — poll
                return await PollUntilReadyAsync(describeUrl, cancellationToken);
            }

            // Create index with dotproduct metric (required for hybrid search)
            var createPayload = new CreateIndexRequest
            {
                Name = indexName,
                Dimension = _options.Dimension,
                Metric = "dotproduct",
                Spec = new IndexSpec
                {
                    Serverless = new ServerlessSpec
                    {
                        Cloud = _options.Cloud!,
                        Region = _options.Region!
                    }
                }
            };

            await SendControlPlaneAsync<object>(
                HttpMethod.Post, $"{controlPlaneBase}/indexes", createPayload, cancellationToken);

            return await PollUntilReadyAsync(describeUrl, cancellationToken);
        }

        private async Task<string> PollUntilReadyAsync(string describeUrl, CancellationToken cancellationToken)
        {
            for (int attempt = 0; attempt < 60; attempt++)
            {
                await Task.Delay(2000, cancellationToken);

                var poll = await SendControlPlaneAsync<DescribeIndexResponse>(
                    HttpMethod.Get, describeUrl, null, cancellationToken);

                if (poll != null &&
                    !string.IsNullOrWhiteSpace(poll.Host) &&
                    poll.Status?.Ready == true)
                {
                    return poll.Host!;
                }
            }

            throw new InvalidOperationException(
                $"Pinecone index '{_options.IndexName}' did not become ready within 2 minutes.");
        }

        private async Task<TResponse?> SendControlPlaneAsync<TResponse>(
            HttpMethod method,
            string absoluteUrl,
            object? payload,
            CancellationToken cancellationToken,
            bool throwOnNotFound = true) where TResponse : class
        {
            using var request = new HttpRequestMessage(method, new Uri(absoluteUrl));
            request.Headers.TryAddWithoutValidation("Api-Key", _options.ApiKey);
            request.Headers.TryAddWithoutValidation("Accept", "application/json");

            if (payload != null)
            {
                var json = JsonSerializer.Serialize(payload, RequestJsonOptions);
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            }

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync();

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound && !throwOnNotFound)
                return null;

            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException(
                    $"Pinecone control plane request failed ({(int)response.StatusCode}): {body}");

            if (string.IsNullOrWhiteSpace(body))
                return null;

            return JsonSerializer.Deserialize<TResponse>(body, JsonOptions);
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
                throw new InvalidOperationException(
                    $"Pinecone request failed ({(int)response.StatusCode}): {body}. " +
                    "If you are using hybrid search (sparse vectors), ensure the index metric is 'dotproduct'.");

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

            var upsertVector = new UpsertVector
            {
                Id = record.Id,
                Values = record.Vector ?? Array.Empty<float>(),
                Metadata = metadata
            };

            if (!string.IsNullOrEmpty(record.Content))
            {
                var (indices, values) = PineconeHelpers.BuildSparseVector(record.Content);
                if (indices.Length > 0)
                {
                    upsertVector.SparseValues = new SparseValuesDto
                    {
                        Indices = indices,
                        Values = values
                    };
                }
            }

            return upsertVector;
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
            _indexLock.Dispose();

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
            public SparseValuesDto? SparseValues { get; set; }
            public Dictionary<string, string> Metadata { get; set; } = new Dictionary<string, string>(StringComparer.Ordinal);
        }

        private sealed class SparseValuesDto
        {
            public uint[] Indices { get; set; } = Array.Empty<uint>();
            public float[] Values { get; set; } = Array.Empty<float>();
        }

        private sealed class QueryRequest
        {
            public string? Namespace { get; set; }
            public float[] Vector { get; set; } = Array.Empty<float>();
            public SparseValuesDto? SparseVector { get; set; }
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

        // Control Plane DTOs

        private sealed class CreateIndexRequest
        {
            public string Name { get; set; } = string.Empty;
            public int Dimension { get; set; }
            public string Metric { get; set; } = "dotproduct";
            public IndexSpec? Spec { get; set; }
        }

        private sealed class IndexSpec
        {
            public ServerlessSpec? Serverless { get; set; }
        }

        private sealed class ServerlessSpec
        {
            public string Cloud { get; set; } = string.Empty;
            public string Region { get; set; } = string.Empty;
        }

        private sealed class DescribeIndexResponse
        {
            public string? Host { get; set; }
            public IndexStatus? Status { get; set; }
        }

        private sealed class IndexStatus
        {
            public bool Ready { get; set; }
        }

        #endregion
    }
}
