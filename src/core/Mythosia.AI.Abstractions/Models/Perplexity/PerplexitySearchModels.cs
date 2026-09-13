using System;
using System.Collections.Generic;

namespace Mythosia.AI.Models.Perplexity
{
    public enum PerplexitySearchType { Web, People }
    public enum PerplexitySearchContentSize { Low, Medium, High }
    public enum PerplexitySearchRecency { Hour, Day, Week, Month, Year }

    /// <summary>Options for the independent Perplexity Search API, which returns ranked pages rather than an LLM answer.</summary>
    public sealed class PerplexitySearchOptions
    {
        public int MaxResults { get; set; } = 10;
        public PerplexitySearchType SearchType { get; set; } = PerplexitySearchType.Web;
        public string? Country { get; set; }
        /// <summary>Up to 20 domains or domain paths. Prefix every entry with '-' to exclude them; allow and deny modes cannot be mixed.</summary>
        public IReadOnlyList<string> DomainFilter { get; set; } = Array.Empty<string>();
        public IReadOnlyList<string> LanguageFilter { get; set; } = Array.Empty<string>();
        public DateTime? PublishedAfter { get; set; }
        public DateTime? PublishedBefore { get; set; }
        public DateTime? UpdatedAfter { get; set; }
        public DateTime? UpdatedBefore { get; set; }
        public PerplexitySearchRecency? Recency { get; set; }
        /// <summary>Web search only. Omit to use provider defaults or explicit token budgets. Cannot be combined with either budget property.</summary>
        public PerplexitySearchContentSize? ContentSize { get; set; }
        public int? MaxTokens { get; set; }
        public int? MaxTokensPerPage { get; set; }
    }

    public sealed class PerplexitySearchResponse
    {
        public string Id { get; set; } = string.Empty;
        public string? ServerTime { get; set; }
        public IReadOnlyList<PerplexitySearchResult> Results { get; set; } = Array.Empty<PerplexitySearchResult>();
    }

    public sealed class PerplexitySearchResult
    {
        /// <summary>One-based position in the provider's ranked results; this is not a relevance score.</summary>
        public int Rank { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public string Snippet { get; set; } = string.Empty;
        /// <summary>Provider-supplied publication date, if available.</summary>
        public string? Date { get; set; }
        public string? LastUpdated { get; set; }
    }
}
