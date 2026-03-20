namespace Mythosia.VectorDb.Postgres;

/// <summary>
/// Determines how the keyword/text leg of hybrid search operates.
/// </summary>
public enum TextSearchMode
{
    /// <summary>
    /// Uses PostgreSQL <c>tsvector / tsquery</c> full-text search.
    /// Works well for languages with good PostgreSQL text search configurations
    /// (e.g. European languages).
    /// </summary>
    TsVector,

    /// <summary>
    /// Uses <c>pg_trgm</c> trigram-based <c>word_similarity</c> matching.
    /// Better for CJK languages (Korean, Japanese, Chinese) and agglutinative languages
    /// where PostgreSQL lacks built-in morphological analysis.
    /// Requires the <c>pg_trgm</c> extension (standard PostgreSQL contrib module).
    /// </summary>
    Trigram
}
