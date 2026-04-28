namespace Mythosia.AI.Rag
{
    public sealed class RagFilter
    {
        public int TopK { get; set; } = 5;

        public double? MinScore { get; set; }

        /// <summary>
        /// Returns a copy of this <see cref="RagFilter"/> with the same field values.
        /// </summary>
        public RagFilter Clone()
        {
            return new RagFilter
            {
                TopK = TopK,
                MinScore = MinScore
            };
        }
    }

    public sealed class RagRetrievalDerivation
    {
        public int TopKMultiplier { get; set; } = 3;

        public double MinScoreDivider { get; set; } = 3d;

        /// <summary>
        /// Returns a copy of this <see cref="RagRetrievalDerivation"/> with the same field values.
        /// </summary>
        public RagRetrievalDerivation Clone()
        {
            return new RagRetrievalDerivation
            {
                TopKMultiplier = TopKMultiplier,
                MinScoreDivider = MinScoreDivider
            };
        }
    }

    public sealed class RagRetrievalFilter
    {
        public int TopK { get; }

        public double? MinScore { get; }

        public RagRetrievalFilter(int topK, double? minScore)
        {
            TopK = topK;
            MinScore = minScore;
        }
    }
}
