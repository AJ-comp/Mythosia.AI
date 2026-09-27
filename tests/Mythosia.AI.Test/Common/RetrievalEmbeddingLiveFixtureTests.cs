using Mythosia.AI.Rag.Splitters;
using System.Text.RegularExpressions;

namespace Mythosia.AI.Tests;

[TestClass]
[TestCategory("Unit")]
public sealed class RetrievalEmbeddingLiveFixtureTests
{
    [TestMethod]
    [DataRow(".txt")]
    [DataRow(".md")]
    [DataRow(".pdf")]
    public async Task SyntheticFixtureUsesProductionExtractionAndFitsOneChunk(string extension)
    {
        const string text = "Customers may request a refund within fourteen days after purchase. Unused items require the original receipt.";
        using var fixture = new RetrievalEmbeddingLiveFixture();
        var document = await fixture.LoadAsync(extension, "Synthetic refund policy", text, CancellationToken.None);
        StringAssert.Contains(Regex.Replace(document.Content, @"\s+", " "), text);
        Assert.HasCount(1, new CharacterTextSplitter(2048, 0).Split(document));
        Assert.AreEqual("Synthetic refund policy", document.Metadata["title"]);
    }
}
