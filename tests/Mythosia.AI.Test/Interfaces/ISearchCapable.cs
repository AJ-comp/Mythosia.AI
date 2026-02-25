namespace Mythosia.AI.Tests.Interfaces;

public interface ISearchCapable
{
    Task<SearchResponse> GetCompletionWithSearchAsync(string prompt, string[]? domains = null, string recencyFilter = "month");
}

public class SearchResponse
{
    public string Content { get; set; } = string.Empty;
    public List<Citation> Citations { get; set; } = new List<Citation>();
}

public class Citation
{
    public string Url { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Snippet { get; set; } = string.Empty;
}