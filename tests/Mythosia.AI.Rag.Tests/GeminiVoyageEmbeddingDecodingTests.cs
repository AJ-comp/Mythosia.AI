using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Mythosia.AI.Rag.Embeddings;

namespace Mythosia.AI.Rag.Tests;

[TestClass]
[TestCategory("Unit")]
public sealed class GeminiVoyageEmbeddingDecodingTests
{
    private const string Secret = "private-decoding-response-sentinel";
    private const string StrictEncodingName = "x-mythosia-test-strict-utf8";

    static GeminiVoyageEmbeddingDecodingTests()
        => Encoding.RegisterProvider(new StrictUtf8TestProvider());

    [TestMethod]
    [DataRow("Gemini", false)]
    [DataRow("Gemini", true)]
    [DataRow("Voyage", false)]
    [DataRow("Voyage", true)]
    public async Task ResponseDecodingFailure_OmitsRemoteTextAndDecoderException(string providerName, bool invalidUtf8)
    {
        using var handler = new DecodingFailureHandler(invalidUtf8);
        using var http = new HttpClient(handler);
        IEmbeddingProvider provider = providerName == "Gemini"
            ? new GeminiEmbeddingProvider("private-api-key-sentinel", http)
            : new VoyageContextualizedEmbeddingProvider("private-api-key-sentinel", http);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => provider.GetEmbeddingAsync("private-input-sentinel"));

        StringAssert.Contains(error.Message, providerName + " embeddings response is invalid");
        StringAssert.Contains(error.Message, "could not be decoded as text");
        Assert.IsFalse(error.ToString().Contains(Secret, StringComparison.Ordinal));
        Assert.IsFalse(error.ToString().Contains(StrictEncodingName, StringComparison.Ordinal));
        Assert.IsFalse(error.ToString().Contains("private-api-key-sentinel", StringComparison.Ordinal));
        Assert.IsFalse(error.ToString().Contains("private-input-sentinel", StringComparison.Ordinal));
        Assert.IsNull(error.InnerException);
        Assert.IsTrue(handler.Content.WasDisposed);
        Assert.AreEqual(1, handler.RequestCount);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => handler.Request!.Content!.ReadAsStringAsync());
    }

    // Register only a unique test encoding name so the malformed bytes exercise HttpContent's
    // real string decoder, without affecting ordinary UTF-8 responses in other tests.
    private sealed class StrictUtf8TestProvider : EncodingProvider
    {
        public override Encoding? GetEncoding(int codepage) => null;
        public override Encoding? GetEncoding(string name)
            => name == StrictEncodingName ? new UTF8Encoding(false, true) : null;
    }

    private sealed class DecodingFailureHandler : HttpMessageHandler
    {
        internal TrackingContent Content { get; }
        internal HttpRequestMessage? Request { get; private set; }
        internal int RequestCount { get; private set; }

        internal DecodingFailureHandler(bool invalidUtf8)
        {
            var bytes = Encoding.UTF8.GetBytes("{\"private\":\"" + Secret + "\"}");
            if (invalidUtf8) bytes = bytes.Concat(new byte[] { 0xff }).ToArray();
            Content = new TrackingContent(bytes);
            Content.Headers.ContentType = new MediaTypeHeaderValue("application/json")
            {
                CharSet = invalidUtf8 ? StrictEncodingName : Secret
            };
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            RequestCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = Content });
        }
    }

    private sealed class TrackingContent(byte[] bytes) : ByteArrayContent(bytes)
    {
        internal bool WasDisposed { get; private set; }
        protected override void Dispose(bool disposing) { WasDisposed = true; base.Dispose(disposing); }
    }
}
