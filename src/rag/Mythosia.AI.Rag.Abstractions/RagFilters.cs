namespace Mythosia.AI.Rag
{
    public sealed class RagFilter
    {
        public int TopK { get; set; } = 5;

        public double? MinScore { get; set; }
    }

    public sealed class RagRetrievalDerivation
    {
        public int TopKMultiplier { get; set; } = 3;

        public double MinScoreDivider { get; set; } = 3d;
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
