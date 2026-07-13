using Mythosia.AI.Models;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Models.Messages;
using Mythosia.AI.Services.Base;
using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace Mythosia.AI.Tests.Common;

/// <summary>
/// GetLatestMessages 의 메시지 창(sliding window) 동작 검증 (API 키 불필요, 프로바이더 무관).
/// 핵심: 에이전틱 도구 라운드가 메시지를 불려 창이 잘려도, 현재 실행을 시작한 user 메시지는
/// 반드시 살아남아야 한다 — user 턴이 전혀 없는 요청은 일부 OpenAI 호환 서버(vLLM/Qwen:
/// "No user query found in messages")가 400 으로 거부한다.
/// </summary>
[TestClass]
public class MessageWindowTests
{
    #region Probe service (창 동작만 검사 — 네트워크 없음)

    private class WindowProbeService : AIService
    {
        public WindowProbeService()
            : base("fake-key", "https://localhost/", new HttpClient())
        {
            AddNewChat();
        }

        public override string Provider => nameof(AIProvider.OpenAI);

        public System.Collections.Generic.List<Message> Window()
            => GetLatestMessages().ToList();

        public override Task<string> GetCompletionAsync(Message message)
            => Task.FromResult(string.Empty);

        protected override HttpRequestMessage CreateMessageRequest()
            => new(HttpMethod.Post, "https://localhost/");

        protected override string ExtractResponseContent(string responseContent)
            => responseContent;

        protected override string StreamParseJson(string jsonData)
            => jsonData;

        public override Task<uint> GetInputTokenCountAsync()
            => Task.FromResult(0u);

        public override Task<uint> GetInputTokenCountAsync(string prompt)
            => Task.FromResult(0u);

        public override Task<byte[]> GenerateImageAsync(string prompt, string size = "1024x1024")
            => Task.FromResult(Array.Empty<byte>());

        public override Task<string> GenerateImageUrlAsync(string prompt, string size = "1024x1024")
            => Task.FromResult(string.Empty);

        public override Task StreamCompletionAsync(Message message, Func<string, Task> messageReceivedAsync)
            => Task.CompletedTask;

        protected override HttpRequestMessage CreateFunctionMessageRequest()
            => new(HttpMethod.Post, "https://localhost/");

        protected override (string content, FunctionCall functionCall) ExtractFunctionCall(string response)
            => (response, null!);
    }

    #endregion

#pragma warning disable CS0618 // MaxMessageCount — deprecated window, 제거 전까지 동작 보증 테스트

    [TestMethod]
    public void Window_DropsUserMessage_ReanchorsMostRecentUser()
    {
        var svc = new WindowProbeService { MaxMessageCount = 10 };

        // 에이전틱 시나리오: user 질문 1개 + 도구 라운드가 만든 assistant/function 메시지 12개
        svc.ActivateChat.Messages.Add(new Message(ActorRole.User, "이것을 표로 만들어줘"));
        for (var i = 0; i < 6; i++)
        {
            svc.ActivateChat.Messages.Add(new Message(ActorRole.Assistant, $"tool call {i}"));
            svc.ActivateChat.Messages.Add(new Message(ActorRole.Function, $"tool result {i}"));
        }

        var window = svc.Window();

        // 창(10) 밖으로 밀렸어야 할 user 질문이 앵커로 재삽입되어 원문 그대로 살아있어야 한다.
        Assert.IsTrue(window.Any(m => m.Role == ActorRole.User), "window must contain a user message");
        Assert.AreEqual(ActorRole.User, window[0].Role);
        Assert.AreEqual("이것을 표로 만들어줘", window[0].Content);
        // 앵커 1개가 추가되므로 창 크기는 MaxMessageCount + 1.
        Assert.AreEqual(11, window.Count);
    }

    [TestMethod]
    public void Window_ContainsUserMessage_NoAnchorInserted()
    {
        var svc = new WindowProbeService { MaxMessageCount = 10 };

        // user 메시지가 창 안에 남는 일반 대화 — 재앵커 없이 기존 동작 그대로.
        for (var i = 0; i < 8; i++)
        {
            svc.ActivateChat.Messages.Add(new Message(ActorRole.User, $"question {i}"));
            svc.ActivateChat.Messages.Add(new Message(ActorRole.Assistant, $"answer {i}"));
        }

        var window = svc.Window();

        Assert.AreEqual(10, window.Count);
        Assert.AreEqual("question 3", window.First(m => m.Role == ActorRole.User).Content);
    }

    [TestMethod]
    public void Window_MultipleUsersDropped_AnchorsMostRecentOne()
    {
        var svc = new WindowProbeService { MaxMessageCount = 4 };

        svc.ActivateChat.Messages.Add(new Message(ActorRole.User, "old question"));
        svc.ActivateChat.Messages.Add(new Message(ActorRole.Assistant, "old answer"));
        svc.ActivateChat.Messages.Add(new Message(ActorRole.User, "current question"));
        for (var i = 0; i < 5; i++)
            svc.ActivateChat.Messages.Add(new Message(ActorRole.Assistant, $"tool {i}"));

        var window = svc.Window();

        // 잘려나간 user 중 '가장 최근'(현재 실행의 질문)이 앵커여야 한다.
        Assert.AreEqual("current question", window[0].Content);
        Assert.AreEqual(1, window.Count(m => m.Role == ActorRole.User));
    }

    [TestMethod]
    public void Window_NoUserAnywhere_LeavesWindowUnchanged()
    {
        var svc = new WindowProbeService { MaxMessageCount = 3 };

        for (var i = 0; i < 5; i++)
            svc.ActivateChat.Messages.Add(new Message(ActorRole.Assistant, $"assistant {i}"));

        var window = svc.Window();

        Assert.AreEqual(3, window.Count);
        Assert.IsFalse(window.Any(m => m.Role == ActorRole.User));
    }

    [TestMethod]
    public void Window_NotTruncated_Unchanged()
    {
        var svc = new WindowProbeService { MaxMessageCount = 20 };

        svc.ActivateChat.Messages.Add(new Message(ActorRole.User, "hello"));
        svc.ActivateChat.Messages.Add(new Message(ActorRole.Assistant, "hi"));

        var window = svc.Window();

        Assert.AreEqual(2, window.Count);
        Assert.AreEqual("hello", window[0].Content);
    }

#pragma warning restore CS0618
}
