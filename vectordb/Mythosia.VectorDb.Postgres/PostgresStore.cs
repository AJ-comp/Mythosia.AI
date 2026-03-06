using Npgsql;
using NpgsqlTypes;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Mythosia.VectorDb.Postgres;

/// <summary>
/// PostgreSQL (pgvector) implementation of <see cref="IVectorStore"/>.
/// Uses a single shared table with a <c>namespace</c> column for logical isolation.
/// Embedding vectors are bound as pgvector-compatible string literals (<c>'[0.1,0.2,...]'::vector</c>).
/// </summary>
public class PostgresStore : IVectorStore, IDisposable
{
    private const string DefaultNamespace = "default";

    private readonly PostgresOptions _options;
    private readonly string _qualifiedTable;
    private readonly SemaphoreSlim _schemaLock = new SemaphoreSlim(1, 1);
    private volatile bool _schemaEnsured;

    /// <summary>
    /// Creates a new <see cref="PostgresStore"/>.
    /// </summary>
    /// <param name="options">Configuration options. Validated on construction.</param>
    public PostgresStore(PostgresOptions options)
    {
        options.Validate();
        _options = options;
        _qualifiedTable = $"\"{_options.SchemaName}\".\"{_options.TableName}\"";
    }

    #region IVectorStore — Upsert

    public async Task UpsertAsync(VectorRecord record, CancellationToken cancellationToken = default)
    {
        await EnsureSchemaIfNeededAsync(cancellationToken);

        using var conn = await OpenConnectionAsync(cancellationToken);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = BuildUpsertSql();
        AddUpsertParameters(cmd.Parameters, record);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task UpsertBatchAsync(IEnumerable<VectorRecord> records, CancellationToken cancellationToken = default)
    {
        await EnsureSchemaIfNeededAsync(cancellationToken);

        using var conn = await OpenConnectionAsync(cancellationToken);
        await using var tx = await conn.BeginTransactionAsync(cancellationToken);
        using var batch = new NpgsqlBatch(conn) { Transaction = tx };

        foreach (var record in records)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var cmd = new NpgsqlBatchCommand(BuildUpsertSql());
            AddUpsertParameters(cmd.Parameters, record);
            batch.BatchCommands.Add(cmd);
        }

        if (batch.BatchCommands.Count > 0)
            await batch.ExecuteNonQueryAsync(cancellationToken);

        await tx.CommitAsync(cancellationToken);
    }

    #endregion

    #region IVectorStore — Get / Delete

    public async Task<VectorRecord?> GetAsync(string id, VectorFilter? filter = null, CancellationToken cancellationToken = default)
    {
        await EnsureSchemaIfNeededAsync(cancellationToken);

        var ns = filter?.Namespace ?? DefaultNamespace;

        using var conn = await OpenConnectionAsync(cancellationToken);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
SELECT id, namespace, scope, content, metadata, embedding::text
FROM {_qualifiedTable}
WHERE namespace = @ns AND id = @id";
        cmd.Parameters.AddWithValue("@ns", ns);
        cmd.Parameters.AddWithValue("@id", id);

        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;

        return ReadRecord(reader);
    }

    public async Task DeleteAsync(string id, VectorFilter? filter = null, CancellationToken cancellationToken = default)
    {
        await EnsureSchemaIfNeededAsync(cancellationToken);

        var ns = filter?.Namespace ?? DefaultNamespace;

        using var conn = await OpenConnectionAsync(cancellationToken);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"DELETE FROM {_qualifiedTable} WHERE namespace = @ns AND id = @id";
        cmd.Parameters.AddWithValue("@ns", ns);
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task DeleteByFilterAsync(VectorFilter filter, CancellationToken cancellationToken = default)
    {
        await EnsureSchemaIfNeededAsync(cancellationToken);

        var ns = filter.Namespace ?? DefaultNamespace;
        var (whereClause, parameters) = BuildFilterWhere(filter, includeMinScore: false, scoreExpression: string.Empty);

        using var conn = await OpenConnectionAsync(cancellationToken);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"DELETE FROM {_qualifiedTable} WHERE namespace = @ns{whereClause}";
        cmd.Parameters.AddWithValue("@ns", ns);
        foreach (var p in parameters)
            cmd.Parameters.Add(p);

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    #endregion

    #region IVectorStore — Search

    public async Task<IReadOnlyList<VectorSearchResult>> SearchAsync(
        float[] queryVector,
        int topK = 5,
        VectorFilter? filter = null,
        CancellationToken cancellationToken = default)
        => await SearchAsync(queryVector, topK, filter, runtimeOptions: null, cancellationToken);

    /// <summary>
    /// Performs a similarity search with optional per-request runtime tuning.
    /// </summary>
    public async Task<IReadOnlyList<VectorSearchResult>> SearchAsync(
        float[] queryVector,
        int topK,
        VectorFilter? filter,
        VectorSearchRuntimeOptions? runtimeOptions,
        CancellationToken cancellationToken = default)
    {
        await EnsureSchemaIfNeededAsync(cancellationToken);

        var ns = filter?.Namespace ?? DefaultNamespace;
        var distanceOperator = GetDistanceOperator();
        var scoreExpression = GetScoreExpression();

        var (whereClause, filterParams) = filter != null
            ? BuildFilterWhere(filter, includeMinScore: true, scoreExpression)
            : ("", new List<NpgsqlParameter>());

        using var conn = await OpenConnectionAsync(cancellationToken);
        await using var tx = await conn.BeginTransactionAsync(cancellationToken);

        await ApplySearchRuntimeSettingsAsync(conn, tx, runtimeOptions, cancellationToken);

        var results = new List<VectorSearchResult>();

        // Block-scoped using for cmd/reader ensures they are fully disposed
        // before tx.CommitAsync(). Npgsql requires the DataReader to be closed
        // before any other command (including COMMIT) can execute on the connection.
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;

            cmd.CommandText = $@"
SELECT id, namespace, scope, content, metadata, embedding::text,
       {scoreExpression} AS score
FROM {_qualifiedTable}
WHERE namespace = @ns{whereClause}
ORDER BY embedding {distanceOperator} @q::vector
LIMIT @topK";

            cmd.Parameters.AddWithValue("@ns", ns);
            cmd.Parameters.AddWithValue("@q", VectorToString(queryVector));
            cmd.Parameters.AddWithValue("@topK", topK);

            foreach (var p in filterParams)
                cmd.Parameters.Add(p);

            using (var reader = await cmd.ExecuteReaderAsync(cancellationToken))
            {
                while (await reader.ReadAsync(cancellationToken))
                {
                    var record = ReadRecord(reader);
                    var score = reader.GetDouble(reader.GetOrdinal("score"));
                    results.Add(new VectorSearchResult(record, score));
                }
            }
        }

        await tx.CommitAsync(cancellationToken);

        return results;
    }

    #endregion

    #region Schema Management

    private async Task EnsureSchemaIfNeededAsync(CancellationToken cancellationToken)
    {
        if (_schemaEnsured)
            return;

        await _schemaLock.WaitAsync(cancellationToken);
        try
        {
            if (_schemaEnsured)
                return;

            if (_options.EnsureSchema)
            {
                await CreateSchemaAsync(cancellationToken);
            }
            else
            {
                await VerifySchemaExistsAsync(cancellationToken);
            }

            _schemaEnsured = true;
        }
        finally
        {
            _schemaLock.Release();
        }
    }

    private async Task CreateSchemaAsync(CancellationToken cancellationToken)
    {
        using var conn = await OpenConnectionAsync(cancellationToken);

        var sql = $@"
CREATE EXTENSION IF NOT EXISTS vector;

CREATE TABLE IF NOT EXISTS {_qualifiedTable} (
    namespace   text        NOT NULL,
    id          text        NOT NULL,
    scope       text        NULL,
    content     text        NULL,
    metadata    jsonb       NOT NULL DEFAULT '{{}}'::jsonb,
    embedding   vector({_options.Dimension}) NOT NULL,
    created_at  timestamptz NOT NULL DEFAULT now(),
    updated_at  timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (namespace, id)
);

CREATE INDEX IF NOT EXISTS idx_{_options.TableName}_metadata
    ON {_qualifiedTable} USING gin (metadata);

CREATE INDEX IF NOT EXISTS idx_{_options.TableName}_ns_scope
    ON {_qualifiedTable} (namespace, scope);
";
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync(cancellationToken);

        await TryCreateVectorIndexAsync(conn, cancellationToken);
    }

    private async Task TryCreateVectorIndexAsync(NpgsqlConnection conn, CancellationToken cancellationToken)
    {
        if (_options.Index is NoIndexOptions)
            return;

        try
        {
            using var cmd = conn.CreateCommand();
            var operatorClass = GetVectorOperatorClass();
            cmd.CommandText = _options.Index switch
            {
                HnswIndexOptions hnsw => $@"
CREATE INDEX IF NOT EXISTS idx_{_options.TableName}_embedding
    ON {_qualifiedTable} USING hnsw (embedding {operatorClass}) WITH (m = {hnsw.M}, ef_construction = {hnsw.EfConstruction})",
                IvfFlatIndexOptions ivfFlat => $@"
CREATE INDEX IF NOT EXISTS idx_{_options.TableName}_embedding
    ON {_qualifiedTable} USING ivfflat (embedding {operatorClass}) WITH (lists = {ivfFlat.Lists})",
                _ => throw new InvalidOperationException($"Unsupported index options type: {_options.Index.GetType().Name}")
            };

            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
        catch when (!_options.FailFastOnIndexCreationFailure)
        {
            // Non-fail-fast mode: allow startup to continue even if index creation fails.
        }
    }

    private async Task VerifySchemaExistsAsync(CancellationToken cancellationToken)
    {
        using var conn = await OpenConnectionAsync(cancellationToken);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT 1
FROM information_schema.tables
WHERE table_schema = @schema AND table_name = @table";
        cmd.Parameters.AddWithValue("@schema", _options.SchemaName);
        cmd.Parameters.AddWithValue("@table", _options.TableName);

        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        if (result == null)
        {
            throw new InvalidOperationException(
                $"Table \"{_options.SchemaName}\".\"{_options.TableName}\" does not exist. " +
                $"Create the table manually or set EnsureSchema = true for automatic provisioning.");
        }
    }

    #endregion

    #region Private Helpers

    private async Task<NpgsqlConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var conn = new NpgsqlConnection(_options.ConnectionString);
        await conn.OpenAsync(cancellationToken);
        return conn;
    }

    private string BuildUpsertSql()
    {
        return $@"
INSERT INTO {_qualifiedTable} (namespace, id, scope, content, metadata, embedding)
VALUES (@ns, @id, @scope, @content, @metadata, @embedding::vector)
ON CONFLICT (namespace, id) DO UPDATE SET
    scope      = EXCLUDED.scope,
    content    = EXCLUDED.content,
    metadata   = EXCLUDED.metadata,
    embedding  = EXCLUDED.embedding,
    updated_at = now()";
    }

    private static void AddUpsertParameters(NpgsqlParameterCollection parameters, VectorRecord record)
    {
        parameters.AddWithValue("@ns", record.Namespace ?? DefaultNamespace);
        parameters.AddWithValue("@id", record.Id);
        parameters.AddWithValue("@scope", (object?)record.Scope ?? DBNull.Value);
        parameters.AddWithValue("@content", (object?)record.Content ?? DBNull.Value);
        parameters.Add(CreateJsonbParameter("@metadata", record.Metadata));
        parameters.AddWithValue("@embedding", VectorToString(record.Vector));
    }

    private static (string whereClause, List<NpgsqlParameter> parameters) BuildFilterWhere(
        VectorFilter filter,
        bool includeMinScore,
        string scoreExpression)
    {
        var sb = new StringBuilder();
        var parameters = new List<NpgsqlParameter>();

        if (filter.Scope != null)
        {
            sb.Append(" AND scope = @scope_filter");
            parameters.Add(new NpgsqlParameter("@scope_filter", filter.Scope));
        }

        if (filter.MetadataMatch != null && filter.MetadataMatch.Count > 0)
        {
            var jsonb = JsonSerializer.Serialize(filter.MetadataMatch);
            sb.Append(" AND metadata @> @meta_filter");
            parameters.Add(CreateJsonbParameter("@meta_filter", jsonb));
        }

        if (includeMinScore && filter.MinScore.HasValue)
        {
            sb.Append($" AND ({scoreExpression}) >= @min_score");
            parameters.Add(new NpgsqlParameter("@min_score", filter.MinScore.Value));
        }

        return (sb.ToString(), parameters);
    }

    private static VectorRecord ReadRecord(NpgsqlDataReader reader)
    {
        var record = new VectorRecord
        {
            Id = reader.GetString(reader.GetOrdinal("id")),
            Namespace = reader.IsDBNull(reader.GetOrdinal("namespace"))
                ? null
                : reader.GetString(reader.GetOrdinal("namespace")),
            Content = reader.IsDBNull(reader.GetOrdinal("content"))
                ? string.Empty
                : reader.GetString(reader.GetOrdinal("content")),
            Scope = reader.IsDBNull(reader.GetOrdinal("scope"))
                ? null
                : reader.GetString(reader.GetOrdinal("scope"))
        };

        // Read embedding as text (cast in SQL: embedding::text → '[0.1,0.2,...]')
        var embeddingText = reader.GetString(reader.GetOrdinal("embedding"));
        record.Vector = ParseVectorString(embeddingText);

        // Read metadata jsonb
        var metadataJson = reader.IsDBNull(reader.GetOrdinal("metadata"))
            ? "{}"
            : reader.GetString(reader.GetOrdinal("metadata"));
        record.Metadata = JsonSerializer.Deserialize<Dictionary<string, string>>(metadataJson)
                          ?? new Dictionary<string, string>();

        return record;
    }

    /// <summary>
    /// Converts a float array to pgvector literal format: '[0.1,0.2,0.3]'.
    /// </summary>
    public static string VectorToString(float[] values)
    {
        var sb = new StringBuilder("[");
        for (int i = 0; i < values.Length; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append(values[i].ToString("G", CultureInfo.InvariantCulture));
        }
        sb.Append(']');
        return sb.ToString();
    }

    /// <summary>
    /// Parses pgvector text representation '[0.1,0.2,0.3]' back to float[].
    /// </summary>
    public static float[] ParseVectorString(string text)
    {
        if (string.IsNullOrEmpty(text))
            return Array.Empty<float>();

        // Remove surrounding brackets
        var trimmed = text.Trim();
        if (trimmed.StartsWith("[")) trimmed = trimmed.Substring(1);
        if (trimmed.EndsWith("]")) trimmed = trimmed.Substring(0, trimmed.Length - 1);

        if (string.IsNullOrWhiteSpace(trimmed))
            return Array.Empty<float>();

        var parts = trimmed.Split(',');
        var result = new float[parts.Length];
        for (int i = 0; i < parts.Length; i++)
        {
            result[i] = float.Parse(parts[i].Trim(), CultureInfo.InvariantCulture);
        }
        return result;
    }

    private static NpgsqlParameter CreateJsonbParameter(string name, object value)
    {
        string json = value is string s ? s : JsonSerializer.Serialize(value);
        return new NpgsqlParameter(name, NpgsqlDbType.Jsonb) { Value = json };
    }

    private async Task ApplySearchRuntimeSettingsAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        VectorSearchRuntimeOptions? runtimeOptions,
        CancellationToken cancellationToken)
    {
        var (profileIvfflatProbes, profileHnswEfSearch) = GetProfileDefaults(runtimeOptions?.Profile);

        if (_options.Index is NoIndexOptions)
            return;

        if (_options.Index is IvfFlatIndexOptions ivfFlat)
        {
            if (runtimeOptions is not null && runtimeOptions is not IvfFlatSearchRuntimeOptions)
                throw new ArgumentException("Use IvfFlatSearchRuntimeOptions when Index is IvfFlatIndexOptions.", nameof(runtimeOptions));

            var runtimeIvf = runtimeOptions as IvfFlatSearchRuntimeOptions;
            var probes = runtimeIvf?.Probes ?? profileIvfflatProbes ?? ivfFlat.Probes;

            // Block-scoped using ensures the command is fully disposed before returning,
            // preventing "A command is already in progress" errors from Npgsql.
            // SET LOCAL does not support parameterized queries ($1) in PostgreSQL.
            // The value is always a validated integer, so interpolation is safe.
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = $"SET LOCAL ivfflat.probes = {probes}";
                await cmd.ExecuteNonQueryAsync(cancellationToken);
            }
            return;
        }

        if (_options.Index is HnswIndexOptions hnsw)
        {
            if (runtimeOptions is not null && runtimeOptions is not HnswSearchRuntimeOptions)
                throw new ArgumentException("Use HnswSearchRuntimeOptions when Index is HnswIndexOptions.", nameof(runtimeOptions));

            var runtimeHnsw = runtimeOptions as HnswSearchRuntimeOptions;
            var efSearch = runtimeHnsw?.EfSearch ?? profileHnswEfSearch ?? hnsw.EfSearch;

            // Block-scoped using ensures the command is fully disposed before returning,
            // preventing "A command is already in progress" errors from Npgsql.
            // SET LOCAL does not support parameterized queries ($1) in PostgreSQL.
            // The value is always a validated integer, so interpolation is safe.
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = $"SET LOCAL hnsw.ef_search = {efSearch}";
                await cmd.ExecuteNonQueryAsync(cancellationToken);
            }
            return;
        }

        throw new InvalidOperationException($"Unsupported index options type: {_options.Index.GetType().Name}");
    }

    private static (int? ivfflatProbes, int? hnswEfSearch) GetProfileDefaults(SearchProfile? profile)
    {
        return profile switch
        {
            SearchProfile.Fast => (4, 16),
            SearchProfile.Balanced => (10, 40),
            SearchProfile.HighRecall => (32, 120),
            _ => (null, null)
        };
    }

    private string GetDistanceOperator()
    {
        return _options.DistanceStrategy switch
        {
            DistanceStrategy.Cosine => "<=>",
            DistanceStrategy.Euclidean => "<->",
            DistanceStrategy.InnerProduct => "<#>",
            _ => throw new InvalidOperationException($"Unsupported distance strategy: {_options.DistanceStrategy}")
        };
    }

    private string GetVectorOperatorClass()
    {
        return _options.DistanceStrategy switch
        {
            DistanceStrategy.Cosine => "vector_cosine_ops",
            DistanceStrategy.Euclidean => "vector_l2_ops",
            DistanceStrategy.InnerProduct => "vector_ip_ops",
            _ => throw new InvalidOperationException($"Unsupported distance strategy: {_options.DistanceStrategy}")
        };
    }

    private string GetScoreExpression()
    {
        return _options.DistanceStrategy switch
        {
            DistanceStrategy.Cosine => "1 - (embedding <=> @q::vector)",
            DistanceStrategy.Euclidean => "1 / (1 + (embedding <-> @q::vector))",
            DistanceStrategy.InnerProduct => "-(embedding <#> @q::vector)",
            _ => throw new InvalidOperationException($"Unsupported distance strategy: {_options.DistanceStrategy}")
        };
    }

    #endregion

    #region IDisposable

    public void Dispose()
    {
        _schemaLock.Dispose();
    }

    #endregion
}
