using Qdrant.Client;
using Qdrant.Client.Grpc;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Mythosia.VectorDb.Qdrant
{
    public sealed class QdrantVectorStoreMigratorFactory : IDesignTimeVectorStoreMigratorFactory
    {
        public string ProviderName => "qdrant";

        public IVectorStoreMigrator CreateMigrator(VectorStoreMigrationConnection connection)
        {
            if (connection == null)
                throw new ArgumentNullException(nameof(connection));

            if (string.IsNullOrWhiteSpace(connection.Endpoint))
                throw new ArgumentException("Migration endpoint must not be empty.", nameof(connection));

            if (!connection.Properties.TryGetValue("source", out var source) || string.IsNullOrWhiteSpace(source))
                throw new ArgumentException("Qdrant migration requires a 'source' property.", nameof(connection));

            var normalized = connection.Endpoint.Contains("://", StringComparison.Ordinal)
                ? new Uri(connection.Endpoint)
                : new Uri($"http://{connection.Endpoint}");

            var port = normalized.IsDefaultPort ? 6334 : normalized.Port;

            var options = new QdrantOptions
            {
                Host = normalized.Host,
                Port = port,
                UseTls = string.Equals(normalized.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase),
                ApiKey = string.IsNullOrWhiteSpace(connection.ApiKey) ? null : connection.ApiKey,
                CollectionName = source,
                Dimension = 1
            };

            return new QdrantVectorStoreMigrator(options);
        }
    }

    public sealed class QdrantVectorStoreMigrator : IVectorStoreMigrator, IDisposable
    {
        private const string DefaultTargetSuffix = "_migrate";
        private const string DefaultCopyTargetSuffix = "_copy";
        private const string LegacySchemaKind = "dense";
        private const int BatchSize = 128;

        private readonly QdrantOptions _options;
        private readonly QdrantClient _client;
        private readonly bool _ownsClient;

        public QdrantVectorStoreMigrator(QdrantOptions options)
        {
            if (options == null)
                throw new ArgumentNullException(nameof(options));

            options.Validate();
            _options = CloneOptions(options);
            _client = new QdrantClient(options.Host, options.Port, options.UseTls, options.ApiKey);
            _ownsClient = true;
        }

        public QdrantVectorStoreMigrator(QdrantOptions options, QdrantClient client)
        {
            if (options == null)
                throw new ArgumentNullException(nameof(options));

            options.Validate();
            _options = CloneOptions(options);
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _ownsClient = false;
        }

        public string ProviderName => "qdrant";

        public async Task<VectorStoreMigrationPlan> PlanAsync(VectorStoreMigrationRequest request, CancellationToken cancellationToken = default)
        {
            ValidateRequest(request);

            var source = request.Source.Trim();
            var target = await ResolveTargetNameAsync(request, source, cancellationToken);
            var info = await _client.GetCollectionInfoAsync(source, cancellationToken: cancellationToken);
            var schema = await ResolveSchemaAsync(source, info, cancellationToken);

            return new VectorStoreMigrationPlan
            {
                ProviderName = ProviderName,
                Source = source,
                Target = target,
                SchemaVersion = schema.version,
                SchemaKind = schema.kind,
                TargetSchemaVersion = QdrantHelpers.CurrentSchemaVersion,
                TargetSchemaKind = QdrantHelpers.CurrentSchemaKind,
                MigrationRequired = schema.version < QdrantHelpers.CurrentSchemaVersion || !string.Equals(schema.kind, QdrantHelpers.CurrentSchemaKind, StringComparison.Ordinal),
                ReplaceOnSuccess = request.ReplaceOnSuccess
            };
        }

        public async Task<VectorStoreMigrationResult> MigrateAsync(
            VectorStoreMigrationRequest request,
            IProgress<VectorStoreMigrationProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var plan = await PlanAsync(request, cancellationToken);
            if (!plan.MigrationRequired)
            {
                return new VectorStoreMigrationResult
                {
                    ProviderName = ProviderName,
                    Source = plan.Source,
                    Target = plan.Source,
                    SchemaVersion = plan.SchemaVersion,
                    SchemaKind = plan.SchemaKind,
                    TotalRecords = 0,
                    MigratedRecords = 0,
                    ReplacedSource = false
                };
            }

            if (string.Equals(plan.Source, plan.Target, StringComparison.Ordinal))
                throw new InvalidOperationException("Target collection must differ from source collection for Qdrant migration.");

            var sourceInfo = await _client.GetCollectionInfoAsync(plan.Source, cancellationToken: cancellationToken);
            progress?.Report(CreateProgress("planning", 0, null, $"Planning migration from '{plan.Source}' to '{plan.Target}'."));
            await EnsureTargetCollectionDoesNotExistAsync(plan.Target, cancellationToken);
            await CreateHybridTargetCollectionAsync(plan.Target, sourceInfo, cancellationToken);
            await QdrantHelpers.CreatePayloadIndexesAsync(_client, plan.Target, _options, cancellationToken);
            var denseDimension = (int)ResolveDenseVectorParams(sourceInfo).Size;
            await WriteSchemaMarkerAsync(plan.Target, denseDimension, cancellationToken);
            progress?.Report(CreateProgress("provisioning", 0, null, $"Created target collection '{plan.Target}' with schema {QdrantHelpers.CurrentSchemaKind}:{QdrantHelpers.CurrentSchemaVersion}."));

            var migrated = await CopyCollectionAsync(
                plan.Source,
                plan.Target,
                sourceInfo,
                progress,
                cancellationToken);

            var hasSchemaMarker = string.Equals(plan.SchemaKind, QdrantHelpers.CurrentSchemaKind, StringComparison.Ordinal);
            var totalRecords = Math.Max(0, (long)sourceInfo.PointsCount - (hasSchemaMarker ? 1L : 0L));
            var replacedSource = false;
            var resultTarget = plan.Target;

            if (request.ReplaceOnSuccess)
            {
                progress?.Report(CreateProgress("cleanup", migrated, totalRecords, $"Replacing source collection '{plan.Source}' with migrated collection '{plan.Target}'."));
                await _client.DeleteCollectionAsync(plan.Source, cancellationToken: cancellationToken);
                await CreateHybridTargetCollectionAsync(plan.Source, sourceInfo, cancellationToken);
                await QdrantHelpers.CreatePayloadIndexesAsync(_client, plan.Source, _options, cancellationToken);
                await WriteSchemaMarkerAsync(plan.Source, denseDimension, cancellationToken);

                await CopyCollectionAsync(
                    plan.Target,
                    plan.Source,
                    null,
                    progress,
                    cancellationToken,
                    "finalizing",
                    $"Promoted migrated records into source collection '{plan.Source}'.");

                await _client.DeleteCollectionAsync(plan.Target, cancellationToken: cancellationToken);
                replacedSource = true;
                resultTarget = plan.Source;
            }

            progress?.Report(
                request.ReplaceOnSuccess
                    ? CreateProgress("completed", migrated, totalRecords, $"Migration completed for collection '{resultTarget}'.")
                    : CreateProgress("completed", migrated, totalRecords, $"Migration completed. Old collection: '{plan.Source}'. New migrated collection: '{resultTarget}'."));

            return new VectorStoreMigrationResult
            {
                ProviderName = ProviderName,
                Source = plan.Source,
                Target = resultTarget,
                SchemaVersion = QdrantHelpers.CurrentSchemaVersion,
                SchemaKind = QdrantHelpers.CurrentSchemaKind,
                TotalRecords = totalRecords,
                MigratedRecords = migrated,
                ReplacedSource = replacedSource
            };
        }

        private async Task<long> CopyCollectionAsync(
            string sourceCollection,
            string targetCollection,
            CollectionInfo? sourceInfo,
            IProgress<VectorStoreMigrationProgress>? progress,
            CancellationToken cancellationToken,
            string progressStage = "copying",
            string? progressMessage = null)
        {
            PointId? offset = null;
            long migrated = 0;
            long? totalRecords = null;

            while (true)
            {
                var points = await _client.ScrollAsync(
                    sourceCollection,
                    filter: null,
                    limit: (uint)BatchSize,
                    offset: offset,
                    payloadSelector: new WithPayloadSelector { Enable = true },
                    vectorsSelector: new WithVectorsSelector { Enable = true },
                    cancellationToken: cancellationToken);

                if (points.Result.Count == 0)
                    break;

                if (sourceInfo != null)
                    totalRecords ??= Math.Max(0, (long)sourceInfo.PointsCount);

                var batch = points.Result
                    .Where(p => !QdrantHelpers.IsSchemaMarker(p))
                    .Select(QdrantHelpers.ToVectorRecord)
                    .Select(r => QdrantHelpers.ToPointStruct(r))
                    .ToList();

                if (batch.Count > 0)
                {
                    await _client.UpsertAsync(targetCollection, batch, cancellationToken: cancellationToken);
                    migrated += batch.Count;
                    progress?.Report(new VectorStoreMigrationProgress
                    {
                        Stage = progressStage,
                        ProcessedRecords = migrated,
                        TotalRecords = totalRecords,
                        Message = progressMessage ?? $"Migrated {migrated} records to '{targetCollection}'."
                    });
                }

                offset = points.NextPageOffset;
                if (offset == null)
                    break;
            }

            return migrated;
        }

        public async Task<VectorStoreMigrationResult> CopyAsync(
            string source,
            string? target = null,
            IProgress<VectorStoreMigrationProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(source))
                throw new ArgumentException("Copy source must not be empty.", nameof(source));

            var trimmedSource = source.Trim();
            var resolvedTarget = await ResolveDefaultTargetNameAsync(trimmedSource, target, DefaultCopyTargetSuffix, cancellationToken);

            if (string.Equals(trimmedSource, resolvedTarget, StringComparison.Ordinal))
                throw new InvalidOperationException("Target collection must differ from source collection for Qdrant copy.");

            var sourceInfo = await _client.GetCollectionInfoAsync(trimmedSource, cancellationToken: cancellationToken);
            var schema = await ResolveSchemaAsync(trimmedSource, sourceInfo, cancellationToken);

            progress?.Report(CreateProgress("planning", 0, null, $"Planning copy from '{trimmedSource}' to '{resolvedTarget}'."));
            await EnsureTargetCollectionDoesNotExistAsync(resolvedTarget, cancellationToken);
            await CreateCollectionLikeSourceAsync(resolvedTarget, sourceInfo, cancellationToken);
            await QdrantHelpers.CreatePayloadIndexesAsync(_client, resolvedTarget, _options, cancellationToken);
            progress?.Report(CreateProgress("provisioning", 0, null, $"Created target collection '{resolvedTarget}' with source schema {schema.kind}:{schema.version}."));

            var copied = await CopyCollectionRawAsync(trimmedSource, resolvedTarget, sourceInfo, progress, cancellationToken);
            var totalRecords = Math.Max(0, (long)sourceInfo.PointsCount);

            progress?.Report(CreateProgress("completed", copied, totalRecords, $"Copy completed. Source collection: '{trimmedSource}'. New copied collection: '{resolvedTarget}'."));

            return new VectorStoreMigrationResult
            {
                ProviderName = ProviderName,
                Source = trimmedSource,
                Target = resolvedTarget,
                SchemaVersion = schema.version,
                SchemaKind = schema.kind,
                TotalRecords = totalRecords,
                MigratedRecords = copied,
                ReplacedSource = false
            };
        }

        public void Dispose()
        {
            if (_ownsClient)
                _client.Dispose();
        }

        private static void ValidateRequest(VectorStoreMigrationRequest request)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            if (string.IsNullOrWhiteSpace(request.Source))
                throw new ArgumentException("Migration source must not be empty.", nameof(request));
        }

        private async Task<string> ResolveTargetNameAsync(VectorStoreMigrationRequest request, string source, CancellationToken cancellationToken)
        {
            return await ResolveDefaultTargetNameAsync(source, request.Target, DefaultTargetSuffix, cancellationToken);
        }

        private async Task<string> ResolveDefaultTargetNameAsync(string source, string? explicitTarget, string suffix, CancellationToken cancellationToken)
        {
            if (!string.IsNullOrWhiteSpace(explicitTarget))
                return explicitTarget!.Trim();

            var baseTarget = source + suffix;
            if (!await _client.CollectionExistsAsync(baseTarget, cancellationToken))
                return baseTarget;

            for (var i = 2; i < int.MaxValue; i++)
            {
                var candidate = baseTarget + i.ToString(System.Globalization.CultureInfo.InvariantCulture);
                if (!await _client.CollectionExistsAsync(candidate, cancellationToken))
                    return candidate;
            }

            throw new InvalidOperationException($"Could not determine an available migration target name for source collection '{source}'.");
        }

        private async Task EnsureTargetCollectionDoesNotExistAsync(string target, CancellationToken cancellationToken)
        {
            if (await _client.CollectionExistsAsync(target, cancellationToken))
                throw new InvalidOperationException($"Target collection '{target}' already exists.");
        }

        private async Task CreateHybridTargetCollectionAsync(string target, CollectionInfo sourceInfo, CancellationToken cancellationToken)
        {
            var sourceDense = ResolveDenseVectorParams(sourceInfo);

            var denseConfig = new VectorParamsMap();
            denseConfig.Map.Add(
                QdrantOptions.DenseVectorName,
                new VectorParams
                {
                    Size = sourceDense.Size,
                    Distance = sourceDense.Distance
                });

            var sparseConfig = new SparseVectorConfig();
            sparseConfig.Map.Add(QdrantOptions.SparseVectorName, new SparseVectorParams());

            await _client.CreateCollectionAsync(
                target,
                denseConfig,
                sparseVectorsConfig: sparseConfig,
                cancellationToken: cancellationToken);
        }

        private async Task CreateCollectionLikeSourceAsync(string target, CollectionInfo sourceInfo, CancellationToken cancellationToken)
        {
            if (sourceInfo.Config?.Params?.VectorsConfig?.Params != null)
            {
                await _client.CreateCollectionAsync(
                    target,
                    sourceInfo.Config.Params.VectorsConfig.Params,
                    cancellationToken: cancellationToken);
                return;
            }

            if (sourceInfo.Config?.Params?.VectorsConfig?.ParamsMap?.Map != null)
            {
                var denseConfig = new VectorParamsMap();
                foreach (var kvp in sourceInfo.Config.Params.VectorsConfig.ParamsMap.Map)
                    denseConfig.Map.Add(kvp.Key, kvp.Value);

                SparseVectorConfig? sparseConfig = null;
                var sourceSparse = sourceInfo.Config.Params.SparseVectorsConfig;
                if (sourceSparse?.Map != null && sourceSparse.Map.Count > 0)
                {
                    sparseConfig = new SparseVectorConfig();
                    foreach (var kvp in sourceSparse.Map)
                        sparseConfig.Map.Add(kvp.Key, kvp.Value);
                }

                await _client.CreateCollectionAsync(
                    target,
                    denseConfig,
                    sparseVectorsConfig: sparseConfig,
                    cancellationToken: cancellationToken);
                return;
            }

            throw new InvalidOperationException("Could not resolve source collection configuration for copy.");
        }

        private static VectorParams ResolveDenseVectorParams(CollectionInfo info)
        {
            if (info.Config == null)
                throw new InvalidOperationException("Could not resolve source collection configuration.");

            if (info.Config.Params?.VectorsConfig?.Params != null)
                return info.Config.Params.VectorsConfig.Params;

            if (info.Config.Params?.VectorsConfig?.ParamsMap?.Map != null)
            {
                if (info.Config.Params.VectorsConfig.ParamsMap.Map.TryGetValue(QdrantOptions.DenseVectorName, out var named))
                    return named;

                var first = info.Config.Params.VectorsConfig.ParamsMap.Map.FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(first.Key) && first.Value != null)
                    return first.Value;
            }

            throw new InvalidOperationException("Could not resolve dense vector parameters from the source collection.");
        }

        private async Task<(int version, string kind)> ResolveSchemaAsync(string collectionName, CollectionInfo info, CancellationToken cancellationToken)
        {
            var resolved = await TryResolveSchemaMarkerAsync(collectionName, cancellationToken);
            if (resolved != null)
                return resolved.Value;

            return (1, LegacySchemaKind);
        }

        private async Task<(int version, string kind)?> TryResolveSchemaMarkerAsync(string collectionName, CancellationToken cancellationToken)
        {
            var points = await _client.RetrieveAsync(
                collectionName,
                new PointId[] { QdrantHelpers.CreatePointId(QdrantHelpers.SchemaMarkerId) },
                withPayload: true,
                withVectors: false,
                cancellationToken: cancellationToken);

            if (points.Count == 0)
                return null;

            var marker = points[0];
            if (!marker.Payload.TryGetValue(QdrantHelpers.PayloadKeySchemaVersion, out var versionValue)
                || !marker.Payload.TryGetValue(QdrantHelpers.PayloadKeySchemaKind, out var kindValue))
                return null;

            if (!int.TryParse(versionValue.StringValue, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var version))
                return null;

            var kind = kindValue.StringValue;
            if (string.IsNullOrWhiteSpace(kind))
                return null;

            return (version, kind);
        }

        private async Task<long> CopyCollectionRawAsync(
            string sourceCollection,
            string targetCollection,
            CollectionInfo? sourceInfo,
            IProgress<VectorStoreMigrationProgress>? progress,
            CancellationToken cancellationToken,
            string progressStage = "copying",
            string? progressMessage = null)
        {
            PointId? offset = null;
            long copied = 0;
            long? totalRecords = null;

            while (true)
            {
                var points = await _client.ScrollAsync(
                    sourceCollection,
                    filter: null,
                    limit: (uint)BatchSize,
                    offset: offset,
                    payloadSelector: new WithPayloadSelector { Enable = true },
                    vectorsSelector: new WithVectorsSelector { Enable = true },
                    cancellationToken: cancellationToken);

                if (points.Result.Count == 0)
                    break;

                if (sourceInfo != null)
                    totalRecords ??= Math.Max(0, (long)sourceInfo.PointsCount);

                var batch = points.Result
                    .Select(ToRawPointStruct)
                    .ToList();

                if (batch.Count > 0)
                {
                    await _client.UpsertAsync(targetCollection, batch, cancellationToken: cancellationToken);
                    copied += batch.Count;
                    progress?.Report(new VectorStoreMigrationProgress
                    {
                        Stage = progressStage,
                        ProcessedRecords = copied,
                        TotalRecords = totalRecords,
                        Message = progressMessage ?? $"Copied {copied} records to '{targetCollection}'."
                    });
                }

                offset = points.NextPageOffset;
                if (offset == null)
                    break;
            }

            return copied;
        }

        private static PointStruct ToRawPointStruct(RetrievedPoint point)
        {
            var copy = new PointStruct
            {
                Id = point.Id,
                Vectors = ToVectors(point.Vectors)
            };

            foreach (var kvp in point.Payload)
                copy.Payload[kvp.Key] = kvp.Value;

            return copy;
        }

        private static Vectors ToVectors(VectorsOutput? vectors)
        {
            var dense = vectors?.Vector?.GetDenseVector();
            if (dense?.Data != null)
            {
                return dense.Data.ToArray();
            }

            var named = new NamedVectors();
            if (vectors?.Vectors?.Vectors != null)
            {
                foreach (var kvp in vectors.Vectors.Vectors)
                {
                    var vector = new Vector();

                    if (kvp.Value.Dense?.Data != null)
                    {
                        var denseProto = new DenseVector();
                        denseProto.Data.AddRange(kvp.Value.Dense.Data);
                        vector.Dense = denseProto;
                    }

                    if (kvp.Value.Sparse != null)
                    {
                        var sparseProto = new SparseVector();
                        sparseProto.Indices.AddRange(kvp.Value.Sparse.Indices);
                        sparseProto.Values.AddRange(kvp.Value.Sparse.Values);
                        vector.Sparse = sparseProto;
                    }

                    named.Vectors.Add(kvp.Key, vector);
                }
            }

            return new Vectors { Vectors_ = named };
        }

        private async Task WriteSchemaMarkerAsync(string collectionName, int denseDimension, CancellationToken cancellationToken)
        {
            var marker = QdrantHelpers.CreateSchemaMarkerPoint(denseDimension);
            await _client.UpsertAsync(collectionName, new[] { marker }, cancellationToken: cancellationToken);
        }

        private static VectorStoreMigrationProgress CreateProgress(string stage, long processedRecords, long? totalRecords, string message)
        {
            return new VectorStoreMigrationProgress
            {
                Stage = stage,
                ProcessedRecords = processedRecords,
                TotalRecords = totalRecords,
                Message = message
            };
        }

        private static QdrantOptions CloneOptions(QdrantOptions options)
        {
            return new QdrantOptions
            {
                Host = options.Host,
                Port = options.Port,
                UseTls = options.UseTls,
                ApiKey = options.ApiKey,
                CollectionName = options.CollectionName,
                Dimension = options.Dimension,
                DistanceStrategy = options.DistanceStrategy,
                AutoCreateCollection = options.AutoCreateCollection,
                HybridFusionStrategy = options.HybridFusionStrategy,
                AdditionalPayloadIndexes = options.AdditionalPayloadIndexes?.ToList() ?? new List<QdrantIndexOption>()
            };
        }
    }
}
