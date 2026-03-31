using System;
using System.Collections.Generic;

namespace Mythosia.VectorDb
{
    /// <summary>
    /// Operator for a metadata filter condition.
    /// </summary>
    public enum FilterOperator
    {
        /// <summary>Exact match. Equivalent to SQL <c>=</c>.</summary>
        Eq,
        /// <summary>Not equal. Equivalent to SQL <c>!=</c>. Records without the key do not match.</summary>
        Ne,
        /// <summary>Greater than. Equivalent to SQL <c>&gt;</c>.</summary>
        Gt,
        /// <summary>Greater than or equal. Equivalent to SQL <c>&gt;=</c>.</summary>
        Gte,
        /// <summary>Less than. Equivalent to SQL <c>&lt;</c>.</summary>
        Lt,
        /// <summary>Less than or equal. Equivalent to SQL <c>&lt;=</c>.</summary>
        Lte,
        /// <summary>Value is in a set. Equivalent to SQL <c>IN (...)</c>.</summary>
        In,
        /// <summary>Value is not in a set. Equivalent to SQL <c>NOT IN (...)</c>. Records without the key do not match.</summary>
        NotIn,
        /// <summary>SQL LIKE pattern match.</summary>
        Like,
        /// <summary>Metadata key exists.</summary>
        Exists,
        /// <summary>Metadata key does not exist.</summary>
        NotExists,
    }

    /// <summary>Logical combinator for a <see cref="FilterGroup"/>.</summary>
    public enum FilterLogic { And, Or }

    /// <summary>Base class for all filter conditions.</summary>
    public abstract class FilterCondition { }

    /// <summary>
    /// A leaf condition that tests a single metadata key against a value or set of values.
    /// </summary>
    public sealed class MetadataCondition : FilterCondition
    {
        /// <summary>Metadata key to test.</summary>
        public string Key { get; }
        /// <summary>The comparison operator.</summary>
        public FilterOperator Operator { get; }
        /// <summary>Single value used by Eq, Ne, Gt, Gte, Lt, Lte, Like.</summary>
        public string? Value { get; }
        /// <summary>Value set used by In and NotIn.</summary>
        public IReadOnlyList<string>? Values { get; }

        internal MetadataCondition(string key, FilterOperator op, string? value, IReadOnlyList<string>? values)
        {
            Key = key;
            Operator = op;
            Value = value;
            Values = values;
        }
    }

    /// <summary>
    /// A composite condition that groups child conditions with AND or OR logic.
    /// </summary>
    public sealed class FilterGroup : FilterCondition
    {
        /// <summary>Whether child conditions are combined with AND or OR.</summary>
        public FilterLogic Logic { get; }
        /// <summary>Child conditions.</summary>
        public IReadOnlyList<FilterCondition> Conditions { get; }

        internal FilterGroup(FilterLogic logic, IReadOnlyList<FilterCondition> conditions)
        {
            Logic = logic;
            Conditions = conditions;
        }
    }

    /// <summary>
    /// Filter criteria for vector store queries and deletions.
    /// Top-level conditions are combined with AND logic.
    /// Supports fluent chaining — all methods return <c>this</c>.
    /// </summary>
    public class VectorFilter
    {
        /// <summary>Filter by namespace. Null means no namespace filter.</summary>
        [System.Obsolete("Namespace will be removed in a future major version. Use Where(\"namespace\", value) instead.")]
        public string? Namespace { get; set; }

        /// <summary>Filter by scope. Null means no scope filter.</summary>
        [System.Obsolete("Scope will be removed in a future major version. Use Where(\"scope\", value) instead.")]
        public string? Scope { get; set; }

        /// <summary>Minimum similarity score threshold. Results below this score are excluded.</summary>
        public double? MinScore { get; set; }

        private readonly List<FilterCondition> _conditions = new List<FilterCondition>();

        /// <summary>The metadata filter conditions. Top-level conditions are AND-combined.</summary>
        public IReadOnlyList<FilterCondition> Conditions => _conditions;

        public VectorFilter() { }

        // ── Equality ──────────────────────────────────────────────────────────

        /// <summary>Adds an exact-match condition (<c>key = value</c>).</summary>
        public VectorFilter Where(string key, string value)
        {
            _conditions.Add(new MetadataCondition(key, FilterOperator.Eq, value, null));
            return this;
        }

        /// <summary>Adds a not-equal condition (<c>key != value</c>). Records without the key do not match.</summary>
        public VectorFilter WhereNot(string key, string value)
        {
            _conditions.Add(new MetadataCondition(key, FilterOperator.Ne, value, null));
            return this;
        }

        // ── Set membership ────────────────────────────────────────────────────

        /// <summary>Adds an IN condition (<c>key IN (values)</c>).</summary>
        public VectorFilter WhereIn(string key, params string[] values)
        {
            _conditions.Add(new MetadataCondition(key, FilterOperator.In, null, values));
            return this;
        }

        /// <summary>Adds a NOT-IN condition (<c>key NOT IN (values)</c>). Records without the key do not match.</summary>
        public VectorFilter WhereNotIn(string key, params string[] values)
        {
            _conditions.Add(new MetadataCondition(key, FilterOperator.NotIn, null, values));
            return this;
        }

        // ── Range ─────────────────────────────────────────────────────────────

        /// <summary>Adds a greater-than condition (<c>key &gt; value</c>). String comparison for non-numeric keys.</summary>
        public VectorFilter WhereGreaterThan(string key, string value)
        {
            _conditions.Add(new MetadataCondition(key, FilterOperator.Gt, value, null));
            return this;
        }

        /// <summary>Adds a greater-than-or-equal condition (<c>key &gt;= value</c>). String comparison for non-numeric keys.</summary>
        public VectorFilter WhereGreaterThanOrEqual(string key, string value)
        {
            _conditions.Add(new MetadataCondition(key, FilterOperator.Gte, value, null));
            return this;
        }

        /// <summary>Adds a less-than condition (<c>key &lt; value</c>). String comparison for non-numeric keys.</summary>
        public VectorFilter WhereLessThan(string key, string value)
        {
            _conditions.Add(new MetadataCondition(key, FilterOperator.Lt, value, null));
            return this;
        }

        /// <summary>Adds a less-than-or-equal condition (<c>key &lt;= value</c>). String comparison for non-numeric keys.</summary>
        public VectorFilter WhereLessThanOrEqual(string key, string value)
        {
            _conditions.Add(new MetadataCondition(key, FilterOperator.Lte, value, null));
            return this;
        }

        // ── Pattern ───────────────────────────────────────────────────────────

        /// <summary>
        /// Adds a SQL LIKE pattern condition.
        /// Use <c>%</c> for any sequence of characters and <c>_</c> for any single character.
        /// </summary>
        public VectorFilter WhereLike(string key, string pattern)
        {
            _conditions.Add(new MetadataCondition(key, FilterOperator.Like, pattern, null));
            return this;
        }

        // ── Key existence ─────────────────────────────────────────────────────

        /// <summary>Adds a key-exists condition.</summary>
        public VectorFilter WhereExists(string key)
        {
            _conditions.Add(new MetadataCondition(key, FilterOperator.Exists, null, null));
            return this;
        }

        /// <summary>Adds a key-does-not-exist condition.</summary>
        public VectorFilter WhereNotExists(string key)
        {
            _conditions.Add(new MetadataCondition(key, FilterOperator.NotExists, null, null));
            return this;
        }

        // ── Logical grouping ──────────────────────────────────────────────────

        /// <summary>
        /// Adds an AND group. All conditions added to the inner filter must match.
        /// Useful for nesting AND inside an <see cref="Or"/> group.
        /// </summary>
        public VectorFilter And(Action<VectorFilter> configure)
        {
            var group = new VectorFilter();
            configure(group);
            if (group._conditions.Count > 0)
                _conditions.Add(new FilterGroup(FilterLogic.And, new List<FilterCondition>(group._conditions)));
            return this;
        }

        /// <summary>
        /// Adds an OR group. At least one condition added to the inner filter must match.
        /// </summary>
        public VectorFilter Or(Action<VectorFilter> configure)
        {
            var group = new VectorFilter();
            configure(group);
            if (group._conditions.Count > 0)
                _conditions.Add(new FilterGroup(FilterLogic.Or, new List<FilterCondition>(group._conditions)));
            return this;
        }

        // ── Fluent property setters ───────────────────────────────────────────

        /// <summary>Sets the namespace and returns <c>this</c> for chaining.</summary>
        [System.Obsolete("WithNamespace will be removed in a future major version. Use Where(\"namespace\", value) instead.")]
        public VectorFilter WithNamespace(string @namespace)
        {
            Namespace = @namespace;
            return this;
        }

        /// <summary>Sets the minimum similarity score threshold and returns <c>this</c> for chaining.</summary>
        public VectorFilter WithMinScore(double minScore)
        {
            MinScore = minScore;
            return this;
        }

        // ── Infrastructure ────────────────────────────────────────────────────

        /// <summary>
        /// Appends all conditions from <paramref name="other"/> into this filter.
        /// Used for filter merging (e.g., combining a pipeline-level store filter with a per-query filter).
        /// </summary>
        public VectorFilter AppendConditionsFrom(VectorFilter other)
        {
            foreach (var c in other._conditions)
                _conditions.Add(c);
            return this;
        }
    }
}
