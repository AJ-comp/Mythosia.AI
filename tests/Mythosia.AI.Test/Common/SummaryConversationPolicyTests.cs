using Mythosia.AI.Exceptions;
using Mythosia.AI.Models;
using Mythosia.AI.Models.Enums;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Models.Messages;
using Mythosia.AI.Services.Base;

namespace Mythosia.AI.Tests.Common;

/// <summary>
/// SummaryConversationPolicy 단위 테스트 (API 키 불필요, 프로바이더 무관)
/// MockAIService를 사용하여 요약 정책의 트리거, 메시지 분리, 시스템 메시지 주입,
/// StatelessMode 보호, 대화 히스토리 정리 등을 검증
/// </summary>
[TestClass]
public class SummaryConversationPolicyTests
{
    #region Mock AIService

    /// <summary>
    /// 테스트용 AIService 구현. GetCompletionAsync(Message)를 override하여
    /// 미리 정의된 응답을 순서대로 반환한다.
    /// </summary>
    private class MockAIService : AIService
    {
        private readonly Queue<string> _responses = new();
        public int CallCount { get; private set; }
        public List<string> ReceivedPrompts { get; } = new();
        public List<bool> WasStatelessOnCall { get; } = new();

        public MockAIService(params string[] responses)
            : base("fake-key", "https://localhost/", new HttpClient())
        {
            AddNewChat();
            foreach (var r in responses)
                _responses.Enqueue(r);
        }

        public override AIProvider Provider => AIProvider.OpenAI;

        public override Task<string> GetCompletionAsync(Message message)
        {
            CallCount++;
            ReceivedPrompts.Add(message.Content);
            WasStatelessOnCall.Add(StatelessMode);

            if (_responses.Count == 0)
                throw new AIServiceException("No more mock responses");

            var response = _responses.Dequeue();
            ActivateChat.Messages.Add(message);
            ActivateChat.Messages.Add(new Message(ActorRole.Assistant, response));
            return Task.FromResult(response);
        }

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

        public int StreamCallCount { get; private set; }

        public override async Task StreamCompletionAsync(Message message, Func<string, Task> messageReceivedAsync)
        {
            StreamCallCount++;

            if (!StatelessMode)
            {
                ActivateChat.Messages.Add(message);
            }

            if (_responses.Count == 0)
                throw new AIServiceException("No more mock responses");

            var response = _responses.Dequeue();

            // Simulate streaming by sending word-by-word
            foreach (var word in response.Split(' '))
            {
                await messageReceivedAsync(word + " ");
            }

            if (!StatelessMode)
            {
                ActivateChat.Messages.Add(new Message(ActorRole.Assistant, response));
            }
        }

        protected override HttpRequestMessage CreateFunctionMessageRequest()
            => new(HttpMethod.Post, "https://localhost/");

        protected override (string content, FunctionCall functionCall) ExtractFunctionCall(string response)
            => (response, null!);
    }

    #endregion

    #region Factory Method Tests

    [TestCategory("Unit")]
    [TestCategory("SummaryPolicy")]
    [TestMethod]
    public void ByToken_SetsCorrectProperties()
    {
        var policy = SummaryConversationPolicy.ByToken(triggerTokens: 3000, keepRecentTokens: 1000);

        Assert.AreEqual(3000u, policy.TriggerTokens);
        Assert.AreEqual(1000u, policy.KeepRecentTokens);
        Assert.IsNull(policy.TriggerCount);
        Assert.IsNull(policy.KeepRecentCount);
        Assert.IsNull(policy.CurrentSummary);
    }

    [TestCategory("Unit")]
    [TestCategory("SummaryPolicy")]
    [TestMethod]
    public void ByMessage_SetsCorrectProperties()
    {
        var policy = SummaryConversationPolicy.ByMessage(triggerCount: 20, keepRecentCount: 5);

        Assert.AreEqual(20u, policy.TriggerCount);
        Assert.AreEqual(5u, policy.KeepRecentCount);
        Assert.IsNull(policy.TriggerTokens);
        Assert.IsNull(policy.KeepRecentTokens);
    }

    [TestCategory("Unit")]
    [TestCategory("SummaryPolicy")]
    [TestMethod]
    public void ByBoth_SetsCorrectProperties()
    {
        var policy = SummaryConversationPolicy.ByBoth(triggerTokens: 3000, triggerCount: 20);

        Assert.AreEqual(3000u, policy.TriggerTokens);
        Assert.AreEqual(20u, policy.TriggerCount);
        Assert.AreEqual(1000u, policy.KeepRecentTokens, "Default keepRecentTokens = triggerTokens / 3");
        Assert.AreEqual(5u, policy.KeepRecentCount, "Default keepRecentCount = Max(3, triggerCount / 4)");
    }

    #endregion

    #region ShouldSummarize Tests

    [TestCategory("Unit")]
    [TestCategory("SummaryPolicy")]
    [TestMethod]
    public void ShouldSummarize_EmptyMessages_ReturnsFalse()
    {
        var policy = SummaryConversationPolicy.ByMessage(triggerCount: 5);
        var messages = new List<Message>();

        Assert.IsFalse(policy.ShouldSummarize(messages));
    }

    [TestCategory("Unit")]
    [TestCategory("SummaryPolicy")]
    [TestMethod]
    public void ShouldSummarize_BelowTriggerCount_ReturnsFalse()
    {
        var policy = SummaryConversationPolicy.ByMessage(triggerCount: 5);
        var messages = CreateMessages(5); // Exactly 5, not exceeding

        Assert.IsFalse(policy.ShouldSummarize(messages));
    }

    [TestCategory("Unit")]
    [TestCategory("SummaryPolicy")]
    [TestMethod]
    public void ShouldSummarize_ExceedsTriggerCount_ReturnsTrue()
    {
        var policy = SummaryConversationPolicy.ByMessage(triggerCount: 5);
        var messages = CreateMessages(6); // Exceeds 5

        Assert.IsTrue(policy.ShouldSummarize(messages));
    }

    [TestCategory("Unit")]
    [TestCategory("SummaryPolicy")]
    [TestMethod]
    public void ShouldSummarize_ByToken_ExceedsTokenLimit_ReturnsTrue()
    {
        var policy = SummaryConversationPolicy.ByToken(triggerTokens: 10);
        // Each message "msg0", "msg1", etc. is ~1 token (4 chars / 4 = 1)
        // Use long messages to exceed
        var messages = new List<Message>();
        for (int i = 0; i < 3; i++)
            messages.Add(new Message(ActorRole.User, new string('A', 50))); // ~12.5 tokens each

        Assert.IsTrue(policy.ShouldSummarize(messages));
    }

    [TestCategory("Unit")]
    [TestCategory("SummaryPolicy")]
    [TestMethod]
    public void ShouldSummarize_ByBoth_CountExceeds_ReturnsTrue()
    {
        var policy = SummaryConversationPolicy.ByBoth(triggerTokens: 100000, triggerCount: 3);
        var messages = CreateMessages(4); // Count exceeds 3, tokens don't exceed

        Assert.IsTrue(policy.ShouldSummarize(messages));
    }

    [TestCategory("Unit")]
    [TestCategory("SummaryPolicy")]
    [TestMethod]
    public void ShouldSummarize_ByBoth_NeitherExceeds_ReturnsFalse()
    {
        var policy = SummaryConversationPolicy.ByBoth(triggerTokens: 100000, triggerCount: 100);
        var messages = CreateMessages(3);

        Assert.IsFalse(policy.ShouldSummarize(messages));
    }

    #endregion

    #region GetMessagesToSummarize Tests

    [TestCategory("Unit")]
    [TestCategory("SummaryPolicy")]
    [TestMethod]
    public void GetMessagesToSummarize_KeepsRecentCount()
    {
        var policy = SummaryConversationPolicy.ByMessage(triggerCount: 5, keepRecentCount: 3);
        var messages = CreateMessages(8);

        var (toSummarize, keepFromIndex) = policy.GetMessagesToSummarize(messages);

        Assert.AreEqual(5, toSummarize.Count, "Should summarize first 5 messages");
        Assert.AreEqual(5, keepFromIndex, "Keep from index 5");
        Assert.AreEqual("msg0", toSummarize[0].Content);
        Assert.AreEqual("msg4", toSummarize[4].Content);
    }

    [TestCategory("Unit")]
    [TestCategory("SummaryPolicy")]
    [TestMethod]
    public void GetMessagesToSummarize_AllFitInKeep_ReturnsEmpty()
    {
        var policy = SummaryConversationPolicy.ByMessage(triggerCount: 10, keepRecentCount: 5);
        var messages = CreateMessages(5);

        var (toSummarize, keepFromIndex) = policy.GetMessagesToSummarize(messages);

        Assert.AreEqual(0, toSummarize.Count, "Nothing to summarize when all fit in keep window");
        Assert.AreEqual(0, keepFromIndex);
    }

    [TestCategory("Unit")]
    [TestCategory("SummaryPolicy")]
    [TestMethod]
    public void GetMessagesToSummarize_ByToken_KeepsRecentWithinTokenBudget()
    {
        // EstimateTokens = Content.Length / 4
        // 20 chars = 5 tokens, 40 chars = 10 tokens
        var policy = SummaryConversationPolicy.ByToken(triggerTokens: 10, keepRecentTokens: 12);

        var messages = new List<Message>
        {
            new Message(ActorRole.User, new string('A', 20)),       // 5 tokens
            new Message(ActorRole.Assistant, new string('B', 40)),   // 10 tokens
            new Message(ActorRole.User, new string('C', 20)),       // 5 tokens  — kept (budget 12)
            new Message(ActorRole.Assistant, new string('D', 20)),   // 5 tokens  — kept (budget 12)
        };
        // total = 25 tokens, exceeds 10
        // from the end: msg3(5) + msg2(5) = 10 ≤ 12 → keep 2
        //               msg3(5) + msg2(5) + msg1(10) = 20 > 12 → stop

        var (toSummarize, keepFromIndex) = policy.GetMessagesToSummarize(messages);

        Assert.AreEqual(2, toSummarize.Count, "First 2 messages should be summarized");
        Assert.AreEqual(2, keepFromIndex);
        Assert.IsTrue(toSummarize[0].Content.StartsWith("AAAA"));
        Assert.IsTrue(toSummarize[1].Content.StartsWith("BBBB"));
    }

    [TestCategory("Unit")]
    [TestCategory("SummaryPolicy")]
    [TestMethod]
    public void GetMessagesToSummarize_ByToken_AllFitInBudget_ReturnsEmpty()
    {
        // keepRecentTokens이 전체 토큰보다 큰 경우 → 요약 대상 없음
        var policy = SummaryConversationPolicy.ByToken(triggerTokens: 5, keepRecentTokens: 1000);
        var messages = new List<Message>
        {
            new Message(ActorRole.User, new string('A', 20)),   // 5 tokens
            new Message(ActorRole.User, new string('B', 20)),   // 5 tokens
        };
        // total 10 tokens, all fit within budget 1000

        var (toSummarize, keepFromIndex) = policy.GetMessagesToSummarize(messages);

        Assert.AreEqual(0, toSummarize.Count);
        Assert.AreEqual(0, keepFromIndex);
    }

    #endregion

    #region LoadSummary Tests

    [TestCategory("Unit")]
    [TestCategory("SummaryPolicy")]
    [TestMethod]
    public void LoadSummary_RestoresPreviousSummary()
    {
        var policy = SummaryConversationPolicy.ByMessage(triggerCount: 10);

        Assert.IsNull(policy.CurrentSummary);

        policy.LoadSummary("Previously saved summary content");

        Assert.AreEqual("Previously saved summary content", policy.CurrentSummary);
    }

    #endregion

    #region GetEffectiveSystemMessage Tests

    [TestCategory("Unit")]
    [TestCategory("SummaryPolicy")]
    [TestMethod]
    public void GetEffectiveSystemMessage_NullPolicy_ReturnsBaseSystemMessage()
    {
        var mock = new MockAIService();
        mock.SystemMessage = "You are a helpful assistant.";
        mock.ConversationPolicy = null;

        var effective = mock.GetEffectiveSystemMessage();

        Assert.AreEqual("You are a helpful assistant.", effective);
    }

    [TestCategory("Unit")]
    [TestCategory("SummaryPolicy")]
    [TestMethod]
    public void GetEffectiveSystemMessage_NoSummary_ReturnsBaseSystemMessage()
    {
        var mock = new MockAIService();
        mock.SystemMessage = "You are a helpful assistant.";
        mock.ConversationPolicy = SummaryConversationPolicy.ByMessage(triggerCount: 10);

        var effective = mock.GetEffectiveSystemMessage();

        Assert.AreEqual("You are a helpful assistant.", effective);
    }

    [TestCategory("Unit")]
    [TestCategory("SummaryPolicy")]
    [TestMethod]
    public void GetEffectiveSystemMessage_WithSummary_PrependsSummary()
    {
        var mock = new MockAIService();
        mock.SystemMessage = "You are a helpful assistant.";
        var policy = SummaryConversationPolicy.ByMessage(triggerCount: 10);
        policy.CurrentSummary = "User discussed project architecture.";
        mock.ConversationPolicy = policy;

        var effective = mock.GetEffectiveSystemMessage();

        Assert.IsTrue(effective.StartsWith("[Previous conversation summary]"),
            "Should start with summary prefix");
        Assert.IsTrue(effective.Contains("User discussed project architecture."),
            "Should contain the summary");
        Assert.IsTrue(effective.Contains("You are a helpful assistant."),
            "Should contain original system message");

        // Summary should come before the original system message
        var summaryIdx = effective.IndexOf("User discussed project architecture.");
        var baseIdx = effective.IndexOf("You are a helpful assistant.");
        Assert.IsTrue(summaryIdx < baseIdx, "Summary must precede base system message");
    }

    [TestCategory("Unit")]
    [TestCategory("SummaryPolicy")]
    [TestMethod]
    public void GetEffectiveSystemMessage_WithSummary_NoBaseMessage_ReturnsSummaryOnly()
    {
        var mock = new MockAIService();
        mock.SystemMessage = "";
        var policy = SummaryConversationPolicy.ByMessage(triggerCount: 10);
        policy.CurrentSummary = "Summary only";
        mock.ConversationPolicy = policy;

        var effective = mock.GetEffectiveSystemMessage();

        Assert.IsTrue(effective.Contains("[Previous conversation summary]"));
        Assert.IsTrue(effective.Contains("Summary only"));
        Assert.IsFalse(effective.Contains("\n\n\n"), "Should not have double-blank-line gap");
    }

    #endregion

    #region Integration Tests (ApplySummaryPolicyIfNeededAsync)

    [TestCategory("Unit")]
    [TestCategory("SummaryPolicy")]
    [TestMethod]
    public async Task NullPolicy_NoSummarization_NormalFlow()
    {
        // 정책 없이 메시지 제한 개수만큼 대화해도 요약 없이 정상 동작하는지 검증
        var responses = new string[20];
        for (int i = 0; i < responses.Length; i++)
            responses[i] = $"Response{i}";

        var mock = new MockAIService(responses);
        mock.ConversationPolicy = null;

        for (int i = 0; i < 20; i++)
        {
            var result = await mock.GetCompletionAsync($"Message{i}");
            Assert.AreEqual($"Response{i}", result);
        }

        Assert.AreEqual(20, mock.CallCount, "All 20 calls should go through without summarization");
        Assert.AreEqual(40, mock.ActivateChat.Messages.Count, "20 user + 20 assistant messages should remain");
    }

    [TestCategory("Unit")]
    [TestCategory("SummaryPolicy")]
    [TestMethod]
    public async Task BelowThreshold_NoSummarization()
    {
        var mock = new MockAIService("Response");
        mock.ConversationPolicy = SummaryConversationPolicy.ByMessage(triggerCount: 100);

        var result = await mock.GetCompletionAsync("Question");

        Assert.AreEqual("Response", result);
        Assert.AreEqual(1, mock.CallCount, "Should not trigger summarization");
        Assert.IsNull(mock.ConversationPolicy.CurrentSummary);
    }

    [TestCategory("Unit")]
    [TestCategory("SummaryPolicy")]
    [TestMethod]
    public async Task ExceedsThreshold_TriggersSummarization()
    {
        // Pre-fill 6 messages, set trigger at 5
        // First response = summary, second response = actual reply
        var mock = new MockAIService("Summary of old conversation", "Actual response");
        mock.ConversationPolicy = SummaryConversationPolicy.ByMessage(triggerCount: 5, keepRecentCount: 2);

        // Pre-fill messages to exceed threshold
        for (int i = 0; i < 6; i++)
        {
            var role = i % 2 == 0 ? ActorRole.User : ActorRole.Assistant;
            mock.ActivateChat.Messages.Add(new Message(role, $"msg{i}"));
        }

        // This should trigger summarization first, then process the actual prompt
        var result = await mock.GetCompletionAsync("New question");

        Assert.AreEqual("Actual response", result);
        Assert.AreEqual(2, mock.CallCount, "1 for summary + 1 for actual prompt");
        Assert.AreEqual("Summary of old conversation", mock.ConversationPolicy.CurrentSummary);
    }

    [TestCategory("Unit")]
    [TestCategory("SummaryPolicy")]
    [TestMethod]
    public async Task Summarization_UsesStatelessMode()
    {
        var mock = new MockAIService("Summary result", "Real response");
        mock.ConversationPolicy = SummaryConversationPolicy.ByMessage(triggerCount: 3, keepRecentCount: 1);

        // Pre-fill 4 messages
        for (int i = 0; i < 4; i++)
            mock.ActivateChat.Messages.Add(new Message(ActorRole.User, $"msg{i}"));

        await mock.GetCompletionAsync("prompt");

        // First call (summary) should have been in StatelessMode
        Assert.IsTrue(mock.WasStatelessOnCall[0], "Summary call must use StatelessMode=true");
        // Second call (actual) should NOT be in StatelessMode
        Assert.IsFalse(mock.WasStatelessOnCall[1], "Actual call must restore StatelessMode=false");
    }

    [TestCategory("Unit")]
    [TestCategory("SummaryPolicy")]
    [TestMethod]
    public async Task Summarization_RemovesOldMessages_KeepsRecent()
    {
        var mock = new MockAIService("Summary text", "Answer");
        mock.ConversationPolicy = SummaryConversationPolicy.ByMessage(triggerCount: 3, keepRecentCount: 2);

        // Pre-fill 5 messages
        for (int i = 0; i < 5; i++)
            mock.ActivateChat.Messages.Add(new Message(ActorRole.User, $"msg{i}"));

        var beforeCount = mock.ActivateChat.Messages.Count;
        Assert.AreEqual(5, beforeCount);

        await mock.GetCompletionAsync("final prompt");

        // After summarization, old messages removed. 
        // Kept: last 2 from original (msg3, msg4) + new messages from the actual call
        // The summary call uses StatelessMode so it doesn't add to main chat
        // The actual call adds "final prompt" + "Answer"
        var messages = mock.ActivateChat.Messages;

        // Verify the old messages (msg0, msg1, msg2) were removed
        Assert.IsFalse(messages.Any(m => m.Content == "msg0"), "msg0 should have been summarized and removed");
        Assert.IsFalse(messages.Any(m => m.Content == "msg1"), "msg1 should have been summarized and removed");
        Assert.IsFalse(messages.Any(m => m.Content == "msg2"), "msg2 should have been summarized and removed");

        // Verify recent messages were kept
        Assert.IsTrue(messages.Any(m => m.Content == "msg3"), "msg3 should be kept");
        Assert.IsTrue(messages.Any(m => m.Content == "msg4"), "msg4 should be kept");
    }

    [TestCategory("Unit")]
    [TestCategory("SummaryPolicy")]
    [TestMethod]
    public async Task Summarization_SummaryPromptContainsOldMessages()
    {
        var mock = new MockAIService("Summarized!", "Response");
        mock.ConversationPolicy = SummaryConversationPolicy.ByMessage(triggerCount: 2, keepRecentCount: 1);

        mock.ActivateChat.Messages.Add(new Message(ActorRole.User, "Tell me about cats"));
        mock.ActivateChat.Messages.Add(new Message(ActorRole.Assistant, "Cats are wonderful animals"));
        mock.ActivateChat.Messages.Add(new Message(ActorRole.User, "What about dogs?"));

        await mock.GetCompletionAsync("Continue");

        // First prompt should be the summary request containing old messages
        var summaryPrompt = mock.ReceivedPrompts[0];
        Assert.IsTrue(summaryPrompt.Contains("Tell me about cats"),
            "Summary prompt should contain old user message");
        Assert.IsTrue(summaryPrompt.Contains("Cats are wonderful animals"),
            "Summary prompt should contain old assistant message");
        Assert.IsTrue(summaryPrompt.Contains("[Conversation to summarize]"),
            "Summary prompt should have conversation header");
    }

    [TestCategory("Unit")]
    [TestCategory("SummaryPolicy")]
    [TestMethod]
    public async Task Summarization_IncrementalSummary_IncludesExistingSummary()
    {
        var mock = new MockAIService("Updated summary", "Response");
        var policy = SummaryConversationPolicy.ByMessage(triggerCount: 2, keepRecentCount: 1);
        policy.CurrentSummary = "Previous: user asked about cats.";
        mock.ConversationPolicy = policy;

        // Add enough messages to trigger again
        mock.ActivateChat.Messages.Add(new Message(ActorRole.User, "Now about dogs"));
        mock.ActivateChat.Messages.Add(new Message(ActorRole.Assistant, "Dogs are loyal"));
        mock.ActivateChat.Messages.Add(new Message(ActorRole.User, "And birds?"));

        await mock.GetCompletionAsync("Tell me more");

        var summaryPrompt = mock.ReceivedPrompts[0];
        Assert.IsTrue(summaryPrompt.Contains("[Existing summary]"),
            "Should contain existing summary header for incremental summarization");
        Assert.IsTrue(summaryPrompt.Contains("Previous: user asked about cats."),
            "Should include the existing summary text");
        Assert.IsTrue(summaryPrompt.Contains("[New messages to incorporate]"),
            "Should have new messages header");

        Assert.AreEqual("Updated summary", policy.CurrentSummary);
    }

    [TestCategory("Unit")]
    [TestCategory("SummaryPolicy")]
    [TestMethod]
    public async Task TokenBased_TriggersSummarization_RemovesOldMessages()
    {
        // EstimateTokens = Content.Length / 4
        // 각 메시지 40자 = 10 tokens, triggerTokens=25 → 3개(30 tokens) 시 초과
        var mock = new MockAIService("Token-based summary", "Final answer");
        mock.ConversationPolicy = SummaryConversationPolicy.ByToken(
            triggerTokens: 25,
            keepRecentTokens: 12  // 10 tokens 1개만 유지 가능
        );

        // Pre-fill 3 messages (30 tokens total, exceeds 25)
        mock.ActivateChat.Messages.Add(new Message(ActorRole.User, new string('A', 40)));       // 10 tokens
        mock.ActivateChat.Messages.Add(new Message(ActorRole.Assistant, new string('B', 40)));   // 10 tokens
        mock.ActivateChat.Messages.Add(new Message(ActorRole.User, new string('C', 40)));       // 10 tokens

        await mock.GetCompletionAsync("new question");

        Assert.AreEqual(2, mock.CallCount, "1 for summary + 1 for actual prompt");
        Assert.AreEqual("Token-based summary", mock.ConversationPolicy.CurrentSummary);

        // Old messages (A, B) should be removed, only C (within token budget) kept + new messages
        var messages = mock.ActivateChat.Messages;
        Assert.IsFalse(messages.Any(m => m.Content.StartsWith("AAAA")), "First message should be summarized and removed");
        Assert.IsFalse(messages.Any(m => m.Content.StartsWith("BBBB")), "Second message should be summarized and removed");
        Assert.IsTrue(messages.Any(m => m.Content.StartsWith("CCCC")), "Third message should be kept (within token budget)");
    }

    [TestCategory("Unit")]
    [TestCategory("SummaryPolicy")]
    [TestMethod]
    public async Task StatelessMode_SkipsSummarization()
    {
        var mock = new MockAIService("Direct response");
        mock.StatelessMode = true;
        mock.ConversationPolicy = SummaryConversationPolicy.ByMessage(triggerCount: 2, keepRecentCount: 1);

        // Pre-fill beyond threshold
        for (int i = 0; i < 5; i++)
            mock.ActivateChat.Messages.Add(new Message(ActorRole.User, $"msg{i}"));

        var result = await mock.GetCompletionAsync("question");

        Assert.AreEqual("Direct response", result);
        Assert.AreEqual(1, mock.CallCount, "Should not trigger summarization in StatelessMode");
        Assert.IsNull(mock.ConversationPolicy.CurrentSummary, "No summary should be created");
    }

    #endregion

    #region Serialization (Save/Restore) Tests

    [TestCategory("Unit")]
    [TestCategory("SummaryPolicy")]
    [TestMethod]
    public void SaveAndRestore_SummaryRoundTrip()
    {
        var policy = SummaryConversationPolicy.ByMessage(triggerCount: 10);
        policy.CurrentSummary = "Important context from previous session";

        // Save
        string saved = policy.CurrentSummary;

        // Create new policy and restore
        var restored = SummaryConversationPolicy.ByMessage(triggerCount: 10);
        restored.LoadSummary(saved);

        Assert.AreEqual("Important context from previous session", restored.CurrentSummary);
    }

    #endregion

    #region Helpers

    private static List<Message> CreateMessages(int count)
    {
        var messages = new List<Message>();
        for (int i = 0; i < count; i++)
        {
            var role = i % 2 == 0 ? ActorRole.User : ActorRole.Assistant;
            messages.Add(new Message(role, $"msg{i}"));
        }
        return messages;
    }

    #endregion

    #region Streaming + Summary Integration Tests

    [TestCategory("Unit")]
    [TestCategory("SummaryPolicy")]
    [TestMethod]
    public async Task Streaming_ExplicitSummaryCall_TriggersSummarization()
    {
        // ApplySummaryPolicyIfNeededAsync()를 StreamAsync 전에 명시적으로 호출하는 패턴 검증
        // (ChatUI에서 사용하는 패턴)
        var mock = new MockAIService("Summary of old conversation", "Streamed response");
        mock.ConversationPolicy = SummaryConversationPolicy.ByMessage(triggerCount: 5, keepRecentCount: 2);

        // Pre-fill 6 messages to exceed threshold
        for (int i = 0; i < 6; i++)
        {
            var role = i % 2 == 0 ? ActorRole.User : ActorRole.Assistant;
            mock.ActivateChat.Messages.Add(new Message(role, $"msg{i}"));
        }

        // Explicitly call summary (as done before StreamAsync in ChatUI)
        await mock.ApplySummaryPolicyIfNeededAsync();

        // Verify summary was created
        Assert.AreEqual("Summary of old conversation", mock.ConversationPolicy.CurrentSummary,
            "Summary should be created after explicit ApplySummaryPolicyIfNeededAsync call");

        // Verify old messages were removed
        Assert.IsFalse(mock.ActivateChat.Messages.Any(m => m.Content == "msg0"),
            "Old messages should be removed after summarization");
        Assert.IsFalse(mock.ActivateChat.Messages.Any(m => m.Content == "msg1"),
            "Old messages should be removed after summarization");

        // Verify recent messages were kept
        Assert.IsTrue(mock.ActivateChat.Messages.Any(m => m.Content == "msg4"),
            "Recent messages should be kept");
        Assert.IsTrue(mock.ActivateChat.Messages.Any(m => m.Content == "msg5"),
            "Recent messages should be kept");

        // Now stream — should work normally after summarization
        string streamed = "";
        await foreach (var chunk in mock.StreamAsync("New streaming question"))
        {
            streamed += chunk;
        }

        Assert.IsTrue(streamed.Contains("Streamed") && streamed.Contains("response"),
            $"Streaming should work after summarization. Got: {streamed}");
        Assert.AreEqual(1, mock.StreamCallCount, "StreamAsync should have been called once");
    }

    [TestCategory("Unit")]
    [TestCategory("SummaryPolicy")]
    [TestMethod]
    public async Task Streaming_BelowThreshold_NoSummarization()
    {
        // 임계값 미만일 때는 요약 없이 스트리밍 정상 동작
        var mock = new MockAIService("Streamed answer");
        mock.ConversationPolicy = SummaryConversationPolicy.ByMessage(triggerCount: 100, keepRecentCount: 5);

        // Pre-fill just a few messages (below threshold)
        for (int i = 0; i < 3; i++)
            mock.ActivateChat.Messages.Add(new Message(ActorRole.User, $"msg{i}"));

        // Explicitly call summary — should be a no-op
        await mock.ApplySummaryPolicyIfNeededAsync();

        Assert.IsNull(mock.ConversationPolicy.CurrentSummary,
            "No summary should be created when below threshold");
        Assert.AreEqual(3, mock.ActivateChat.Messages.Count,
            "No messages should be removed");

        // Stream should work normally
        string streamed = "";
        await foreach (var chunk in mock.StreamAsync("question"))
        {
            streamed += chunk;
        }

        Assert.IsTrue(streamed.Contains("Streamed"),
            $"Streaming should work normally. Got: {streamed}");
    }

    [TestCategory("Unit")]
    [TestCategory("SummaryPolicy")]
    [TestMethod]
    public async Task Streaming_EffectiveSystemMessage_IncludesSummary()
    {
        // 요약 후 GetEffectiveSystemMessage에 요약이 포함되는지 검증
        var mock = new MockAIService("Summary result", "Stream output");
        mock.SystemMessage = "You are a helpful assistant.";
        mock.ConversationPolicy = SummaryConversationPolicy.ByMessage(triggerCount: 3, keepRecentCount: 1);

        // Pre-fill 4 messages
        for (int i = 0; i < 4; i++)
            mock.ActivateChat.Messages.Add(new Message(ActorRole.User, $"msg{i}"));

        // Trigger summary before streaming
        await mock.ApplySummaryPolicyIfNeededAsync();

        // Verify effective system message includes summary
        var effective = mock.GetEffectiveSystemMessage();
        Assert.IsTrue(effective.Contains("[Previous conversation summary]"),
            "Effective system message should contain summary prefix");
        Assert.IsTrue(effective.Contains("Summary result"),
            "Effective system message should contain the summary content");
        Assert.IsTrue(effective.Contains("You are a helpful assistant."),
            "Effective system message should still contain original system message");

        // Stream should work with summary context
        string streamed = "";
        await foreach (var chunk in mock.StreamAsync("follow up"))
        {
            streamed += chunk;
        }
        Assert.IsTrue(streamed.Length > 0, "Streaming should produce output after summarization");
    }

    [TestCategory("Unit")]
    [TestCategory("SummaryPolicy")]
    [TestMethod]
    public void ByMessage_KeepRecentGreaterOrEqual_Throws()
    {
        try
        {
            SummaryConversationPolicy.ByMessage(triggerCount: 5, keepRecentCount: 5);
            Assert.Fail("Expected ArgumentException when keepRecentCount equals triggerCount");
        }
        catch (ArgumentException)
        {
        }

        try
        {
            SummaryConversationPolicy.ByMessage(triggerCount: 5, keepRecentCount: 10);
            Assert.Fail("Expected ArgumentException when keepRecentCount exceeds triggerCount");
        }
        catch (ArgumentException)
        {
        }
    }

    [TestCategory("Unit")]
    [TestCategory("SummaryPolicy")]
    [TestMethod]
    public void ByBoth_KeepRecentGreaterOrEqual_Throws()
    {
        try
        {
            SummaryConversationPolicy.ByBoth(triggerTokens: 100, triggerCount: 5, keepRecentCount: 5);
            Assert.Fail("Expected ArgumentException when keepRecentCount equals triggerCount");
        }
        catch (ArgumentException)
        {
        }

        try
        {
            SummaryConversationPolicy.ByBoth(triggerTokens: 100, triggerCount: 5, keepRecentCount: 10);
            Assert.Fail("Expected ArgumentException when keepRecentCount exceeds triggerCount");
        }
        catch (ArgumentException)
        {
        }
    }

    [TestCategory("Unit")]
    [TestCategory("SummaryPolicy")]
    [TestMethod]
    public async Task Streaming_StatelessMode_SkipsSummarization()
    {
        // StatelessMode에서는 ApplySummaryPolicyIfNeededAsync가 무시되는지 검증
        var mock = new MockAIService("Streamed response");
        mock.StatelessMode = true;
        mock.ConversationPolicy = SummaryConversationPolicy.ByMessage(triggerCount: 2, keepRecentCount: 1);

        // Pre-fill beyond threshold
        for (int i = 0; i < 5; i++)
            mock.ActivateChat.Messages.Add(new Message(ActorRole.User, $"msg{i}"));

        await mock.ApplySummaryPolicyIfNeededAsync();

        Assert.IsNull(mock.ConversationPolicy.CurrentSummary,
            "No summary should be created in StatelessMode");
        Assert.AreEqual(5, mock.ActivateChat.Messages.Count,
            "Messages should not be removed in StatelessMode");
    }

    #endregion
}
