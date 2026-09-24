using Mythosia.VectorDb;
using System;
using System.Threading.Tasks;

namespace Mythosia.AI.Rag
{
    /// <summary>
    /// Text and constraints for one retrieval operation. No embedding is created
    /// until the selected retriever requires it.
    /// </summary>
    public sealed class RagRetrievalRequest
    {
        /// <summary>The full semantic query, including any prior query rewriting.</summary>
        public string Query { get; }
        /// <summary>
        /// Optional keyword-query override. Null uses <see cref="Query"/> in the built-in
        /// text and hybrid retrievers. An empty override disables the text leg.
        /// </summary>
        public string? TextQuery { get; }
        /// <summary>Maximum number of candidates requested, before reranking.</summary>
        public int TopK { get; }
        /// <summary>A request-owned copy of the metadata and score constraints.</summary>
        public VectorFilter? Filter { get; }
        /// <summary>Optional progress callback for processing performed by the retriever.</summary>
        public Func<RagProgressStage, Task>? ProgressAsync { get; }

        /// <summary>Creates a validated request, snapshotting the filter conditions.</summary>
        public RagRetrievalRequest(string query, string? textQuery = null, int topK = 5,
            VectorFilter? filter = null, Func<RagProgressStage, Task>? progressAsync = null)
        {
            Query = query ?? throw new ArgumentNullException(nameof(query));
            if (topK <= 0) throw new ArgumentOutOfRangeException(nameof(topK));
            TextQuery = textQuery;
            TopK = topK;
            ProgressAsync = progressAsync;
            if (filter != null)
            {
                Filter = new VectorFilter { MinScore = filter.MinScore };
                CopyConditions(Filter, filter.Conditions);
            }
        }

        private static void CopyConditions(VectorFilter target,
            System.Collections.Generic.IReadOnlyList<FilterCondition> conditions)
        {
            foreach (var condition in conditions)
            {
                if (condition is FilterGroup group)
                {
                    if (group.Logic == FilterLogic.And) target.And(g => CopyConditions(g, group.Conditions));
                    else target.Or(g => CopyConditions(g, group.Conditions));
                }
                else if (condition is MetadataCondition value)
                {
                    switch (value.Operator)
                    {
                        case FilterOperator.Eq: target.Where(value.Key, value.Value!); break;
                        case FilterOperator.Ne: target.WhereNot(value.Key, value.Value!); break;
                        case FilterOperator.Gt: target.WhereGreaterThan(value.Key, value.Value!); break;
                        case FilterOperator.Gte: target.WhereGreaterThanOrEqual(value.Key, value.Value!); break;
                        case FilterOperator.Lt: target.WhereLessThan(value.Key, value.Value!); break;
                        case FilterOperator.Lte: target.WhereLessThanOrEqual(value.Key, value.Value!); break;
                        case FilterOperator.In: target.WhereIn(value.Key, CopyValues(value.Values)); break;
                        case FilterOperator.NotIn: target.WhereNotIn(value.Key, CopyValues(value.Values)); break;
                        case FilterOperator.Like: target.WhereLike(value.Key, value.Value!); break;
                        case FilterOperator.Exists: target.WhereExists(value.Key); break;
                        case FilterOperator.NotExists: target.WhereNotExists(value.Key); break;
                        default: throw new NotSupportedException("Unsupported metadata filter operator.");
                    }
                }
                else throw new NotSupportedException("Unsupported metadata filter condition.");
            }
        }

        private static string[] CopyValues(System.Collections.Generic.IReadOnlyList<string>? values)
        {
            if (values == null) return Array.Empty<string>();
            var copy = new string[values.Count];
            for (var i = 0; i < copy.Length; i++) copy[i] = values[i];
            return copy;
        }
    }
}
