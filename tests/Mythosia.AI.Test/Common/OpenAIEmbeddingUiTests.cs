using Mythosia.AI.Rag.Embeddings;
using Mythosia.AI.Samples.ChatUi;

namespace Mythosia.AI.Tests.Common;

[TestClass]
[TestCategory("Unit")]
public sealed class OpenAIEmbeddingUiTests
{
    [TestMethod]
    [DataRow("text-embedding-ada-002", 1536)]
    [DataRow("text-embedding-3-small", 512)]
    [DataRow("text-embedding-3-large", 3072)]
    public void SubmittedSettings_PreserveModelDimensions(string model, int dimensions)
    {
        using var http = new HttpClient();
        var provider = ChatUiUtilityHelpers.BuildRagEmbeddingProvider(
            "openai", "test-key", null, http, model, dimensions, "");

        Assert.IsInstanceOfType<OpenAIEmbeddingProvider>(provider);
        Assert.AreEqual(dimensions, provider.Dimensions);
    }

    [TestMethod]
    [DataRow(512)]
    [DataRow(3072)]
    public void AdaIncompatibleDatabaseDimensions_AreRejectedByServerFactory(int dimensions)
    {
        using var http = new HttpClient();
        var error = Assert.Throws<ArgumentOutOfRangeException>(() =>
            ChatUiUtilityHelpers.BuildRagEmbeddingProvider(
                "openai", "test-key", null, http, "text-embedding-ada-002", dimensions, ""));

        Assert.AreEqual("dimensions", error.ParamName);
        StringAssert.Contains(error.Message, "1536");
    }
}
