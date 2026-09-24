using System.Net;
using System.Text;
using System.Text.Json;
using Mythosia.AI.Models;
using Mythosia.AI.Models.Messages;
using Mythosia.AI.Services.OpenAI;
using Mythosia.VectorDb;

namespace Mythosia.AI.Rag.Tests;

[TestClass]
[TestCategory("Unit")]
public sealed class RagMultimodalCompletionTests
{
    private const string Question = "Explain the image and audio together";
    private const string DocumentText = "RETRIEVED_POLICY_81723";

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public async Task Completion_PreservesNonTextContentAndOriginalHistory(bool hasReferences, bool withOptions)
    {
        var service = new MockAIService();
        var retriever = new FixedRetriever(hasReferences);
        var rag = service.WithRag(b => b.UseRetriever(retriever));
        var firstText = new TextContent("Explain the image");
        var lastText = new TextContent("and audio together");
        var image = new ImageContent(new byte[] { 1, 2, 3 }, "image/png") { IsHighDetail = true };
        var imageUrl = new ImageContent("https://example.invalid/policy.png");
        var audio = new AudioContent(new byte[] { 4, 5, 6 }, "audio/wav");
        var custom = new CustomContent();
        var contents = new List<MessageContent> { firstText, image, imageUrl, lastText, audio, custom };
        var input = new Message(ActorRole.User, contents)
        {
            Timestamp = new DateTime(2026, 9, 23, 0, 0, 0, DateTimeKind.Utc),
            Metadata = new() { ["request_tag"] = "original" }
        };

        if (withOptions) await rag.GetCompletionAsync(input, new RagQueryOptions());
        else await rag.GetCompletionAsync(input);

        var sent = service.LastReceivedMessage!;
        Assert.IsNotNull(sent);
        Assert.AreEqual(Question, retriever.LastQuery, "Retrieval uses the message's text, not media placeholders.");
        Assert.AreEqual(input.Role, sent.Role);
        Assert.AreEqual(input.Timestamp, sent.Timestamp);
        Assert.AreEqual("original", sent.Metadata!["request_tag"]);
        var text = Assert.ContainsSingle(sent.Contents.OfType<TextContent>());
        StringAssert.Contains(text.Text, Question);
        Assert.AreEqual(hasReferences, text.Text.Contains(DocumentText, StringComparison.Ordinal));
        CollectionAssert.AreEqual(new MessageContent[] { image, imageUrl, audio, custom },
            sent.Contents.Where(content => content is not TextContent).ToArray());
        Assert.AreNotSame(contents, sent.Contents);
        Assert.AreNotSame(input.Metadata, sent.Metadata);
        Assert.AreSame(input, Assert.ContainsSingle(service.ActivateChat.Messages));
        Assert.AreSame(contents, input.Contents);
        Assert.AreEqual(Question, input.Content);
        Assert.AreEqual("Explain the image", firstText.Text);
        Assert.AreEqual("and audio together", lastText.Text);
        Assert.AreEqual(6, input.Contents.Count);
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public async Task Completion_ActualProviderRequestIncludesBothImages(bool hasReferences, bool withOptions)
    {
        using var handler = new CaptureHandler();
        using var client = new HttpClient(handler);
        var service = new OpenAIService("offline-media-test", AIModels.OpenAI.Gpt4o, client);
        var rag = service.WithRag(b => b.UseRetriever(new FixedRetriever(hasReferences)));
        var input = new Message(ActorRole.User, new List<MessageContent>
        {
            new TextContent(Question),
            new ImageContent("https://example.invalid/policy.png") { IsHighDetail = true },
            new ImageContent(new byte[] { 1, 2, 3 }, "image/png")
        });

        if (withOptions) await rag.GetCompletionAsync(input, new RagQueryOptions());
        else await rag.GetCompletionAsync(input);

        Assert.AreEqual(1, handler.Requests.Count);
        var user = handler.Requests[0].GetProperty("messages").EnumerateArray().Single(m => m.GetProperty("role").GetString() == "user");
        var content = user.GetProperty("content");
        Assert.AreEqual(JsonValueKind.Array, content.ValueKind);
        var parts = content.EnumerateArray().ToArray();
        Assert.AreEqual(3, parts.Length);
        StringAssert.Contains(parts[0].GetProperty("text").GetString()!, Question);
        Assert.AreEqual(hasReferences, parts[0].GetProperty("text").GetString()!.Contains(DocumentText, StringComparison.Ordinal));
        Assert.AreEqual("https://example.invalid/policy.png", parts[1].GetProperty("image_url").GetProperty("url").GetString());
        Assert.AreEqual("high", parts[1].GetProperty("image_url").GetProperty("detail").GetString());
        Assert.AreEqual("data:image/png;base64,AQID", parts[2].GetProperty("image_url").GetProperty("url").GetString());
        Assert.AreSame(input, service.ActivateChat.Messages[0]);
        Assert.AreEqual(Question, input.Content);
        Assert.AreEqual(2, input.Contents.OfType<ImageContent>().Count());
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Completion_SearchSkippedStillForwardsMedia(bool withOptions)
    {
        var service = new MockAIService();
        var retriever = new FixedRetriever(true);
        var rewriter = new PassRewriter();
        var rag = service.WithRag(b => b.UseRetriever(retriever).WithQueryRewriter(rewriter));
        var image = new ImageContent("https://example.invalid/photo.png");
        var input = new Message(ActorRole.User, new List<MessageContent> { new TextContent(Question), image });

        if (withOptions) await rag.GetCompletionAsync(input, new RagQueryOptions());
        else await rag.GetCompletionAsync(input);

        Assert.AreEqual(Question, rewriter.Query);
        Assert.IsNull(retriever.LastQuery);
        Assert.AreSame(image, Assert.ContainsSingle(service.LastReceivedMessage!.Contents.OfType<ImageContent>()));
        Assert.AreEqual(Question, Assert.ContainsSingle(service.LastReceivedMessage.Contents.OfType<TextContent>()).Text);
        Assert.AreSame(input, Assert.ContainsSingle(service.ActivateChat.Messages));
    }

    private sealed class FixedRetriever(bool hasReferences) : IRagRetriever
    {
        public string? LastQuery;
        public Task<IReadOnlyList<VectorSearchResult>> RetrieveAsync(RagRetrievalRequest request, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastQuery = request.Query;
            return Task.FromResult<IReadOnlyList<VectorSearchResult>>(hasReferences
                ? new[] { new VectorSearchResult(new VectorRecord("policy", new float[] { 1 }, DocumentText), 1) }
                : Array.Empty<VectorSearchResult>());
        }
    }

    private sealed class PassRewriter : IQueryRewriter
    {
        public string? Query;
        public Task<QueryRewriteResult> RewriteAsync(string query, IReadOnlyList<ConversationTurn>? conversationHistory, CancellationToken cancellationToken = default)
        { Query = query; return Task.FromResult(QueryRewriteResult.Pass(query)); }
    }

    private sealed class CustomContent : MessageContent
    {
        public override string Type => "custom";
        public override object ToRequestFormat(string provider) => new { type = Type };
        public override string GetDescription() => "custom media";
        public override uint EstimateTokens() => 1;
    }

    private sealed class CaptureHandler : HttpMessageHandler
    {
        public List<JsonElement> Requests { get; } = new();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            using var document = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            Requests.Add(document.RootElement.Clone());
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":\"answer\"},\"finish_reason\":\"stop\"}]}", Encoding.UTF8, "application/json")
            };
        }
    }
}
