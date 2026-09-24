using Mythosia.VectorDb.Pinecone;

namespace Mythosia.VectorDb.Tests.Pinecone;

[TestClass]
public class PineconeSearchCapabilitiesTests
{
    [TestMethod]
    public void DenseIndexAdapter_DoesNotAdvertiseSparseOnlyOrPortableRrfSupport()
    {
        using var client = new HttpClient(new MockHttpMessageHandler());
        using var store = new PineconeStore(new PineconeOptions
        {
            IndexHost = "https://test-index.svc.pinecone.io", ApiKey = "test"
        }, client);
        Assert.IsFalse((object)store is ITextSearchStore);
        Assert.IsFalse((object)store is IConfigurableHybridSearchStore);
    }
}
