using System;

namespace Mythosia.VectorDb.Postgres;

/// <summary>
/// Configuration options for <see cref="PostgresStore"/>.
/// </summary>
public class PostgresOptions
{
    /// <summary>
    /// PostgreSQL connection string. Required.
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// Embedding vector dimension. Must match the dimension used by the embedding provider.
    /// Required (must be greater than 0).
    /// </summary>
    public int Dimension { get; set; }

    /// <summary>
    /// Database schema name. Default: "public".
    /// </summary>
    public string SchemaName { get; set; } = "public";

    /// <summary>
    /// Table name for vector storage. Default: "vectors".
    /// </summary>
    public string TableName { get; set; } = "vectors";

    /// <summary>
    /// When true, automatically creates the pgvector extension, table, and indexes
    /// if they do not exist. Recommended for development/testing only.
    /// When false (default), the schema must already exist or an exception is thrown.
    /// </summary>
    public bool EnsureSchema { get; set; } = false;

    /// <summary>
    /// Distance function for similarity search. Default: <see cref="DistanceStrategy.Cosine"/>.
    /// </summary>
    public DistanceStrategy DistanceStrategy { get; set; } = DistanceStrategy.Cosine;

    /// <summary>
    /// Vector index settings. Default: <see cref="HnswIndexOptions"/>.
    /// </summary>
    public VectorIndexOptions Index { get; set; } = new HnswIndexOptions();

    /// <summary>
    /// When true (default), index creation failures throw and fail fast.
    /// When false, index creation failures are downgraded to warnings and startup continues.
    /// </summary>
    public bool FailFastOnIndexCreationFailure { get; set; } = true;

    /// <summary>
    /// Validates the options and throws <see cref="ArgumentException"/> if invalid.
    /// </summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString))
            throw new ArgumentException("ConnectionString must not be empty.", nameof(ConnectionString));

        if (Dimension <= 0)
            throw new ArgumentException("Dimension must be greater than 0.", nameof(Dimension));

        if (string.IsNullOrWhiteSpace(TableName))
            throw new ArgumentException("TableName must not be empty.", nameof(TableName));

        if (string.IsNullOrWhiteSpace(SchemaName))
            throw new ArgumentException("SchemaName must not be empty.", nameof(SchemaName));

        if (!Enum.IsDefined(typeof(DistanceStrategy), DistanceStrategy))
            throw new ArgumentException("DistanceStrategy is invalid.", nameof(DistanceStrategy));

        if (Index is null)
            throw new ArgumentException("Index must not be null.", nameof(Index));

        Index.Validate();

        ValidateIdentifier(SchemaName, nameof(SchemaName));
        ValidateIdentifier(TableName, nameof(TableName));
    }

    /// <summary>
    /// Ensures an identifier contains only safe characters (letters, digits, underscores)
    /// to prevent SQL injection via schema/table names.
    /// </summary>
    private static void ValidateIdentifier(string value, string paramName)
    {
        foreach (var c in value)
        {
            if (!char.IsLetterOrDigit(c) && c != '_')
                throw new ArgumentException(
                    $"Identifier '{value}' contains invalid character '{c}'. Only letters, digits, and underscores are allowed.",
                    paramName);
        }
    }
}
