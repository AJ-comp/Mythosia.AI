namespace Mythosia.VectorDb.Postgres
{
    /// <summary>
    /// Runtime search profile presets for recall/latency tradeoffs.
    /// </summary>
    public enum SearchProfile
    {
        Fast,
        Balanced,
        HighRecall
    }
}
