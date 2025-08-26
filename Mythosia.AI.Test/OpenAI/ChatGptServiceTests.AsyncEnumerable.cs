using Microsoft.VisualStudio.TestTools.UnitTesting;
using Mythosia.AI.Extensions;
using Mythosia.AI.Models.Enums;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Mythosia.AI.Tests
{
    public partial class ChatGptServiceTests
    {
        /// <summary>
        /// IAsyncEnumerable 스트리밍과 이미지 분석 테스트
        /// </summary>
        [TestMethod]
        public async Task IAsyncEnumerableStreamingWithImageTest()
        {
            try
            {
                // Vision 지원 모델로 변경
                AI.ActivateChat.ChangeModel(AIModel.Gpt4oLatest);

                string fullResponse = "";
                int chunkCount = 0;
                var chunks = new List<string>();

                // IAsyncEnumerable로 이미지 분석 스트리밍
                await foreach (var chunk in AI
                    .BeginMessage()
                    .AddText("Describe this image in detail, mentioning colors, objects, and composition:")
                    .AddImage(TestImagePath)
                    .WithHighDetail()
                    .StreamAsync())
                {
                    fullResponse += chunk;
                    chunks.Add(chunk);
                    chunkCount++;
                    Console.Write(chunk);
                }

                Console.WriteLine($"\n[IAsyncEnumerable Image Stream] Chunks: {chunkCount}, Total: {fullResponse.Length}");
                Assert.IsTrue(chunkCount > 1, "Should receive multiple chunks");
                Assert.IsTrue(fullResponse.Length > 50, "Response should be detailed");

                // 청크 분석
                var avgChunkSize = chunks.Average(c => c.Length);
                Console.WriteLine($"[Chunk Analysis] Average chunk size: {avgChunkSize:F1} chars");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[IAsyncEnumerable Image Error] {ex.Message}");
                Assert.Fail(ex.Message);
            }
        }

        /// <summary>
        /// IAsyncEnumerable with cancellation 테스트
        /// </summary>
        [TestMethod]
        public async Task IAsyncEnumerableWithCancellationTest()
        {
            try
            {
                var cts = new CancellationTokenSource();
                string partialResponse = "";
                int receivedChunks = 0;
                const int maxChunks = 10;

                await foreach (var chunk in AI.StreamAsync(
                    "Write a very long story about artificial intelligence, including its history, current state, and future prospects.",
                    cts.Token))
                {
                    partialResponse += chunk;
                    receivedChunks++;
                    Console.Write(chunk);

                    // 일정 청크 수가 되면 취소
                    if (receivedChunks >= maxChunks)
                    {
                        cts.Cancel();
                        break;
                    }
                }

                Console.WriteLine($"\n[Cancelled After] {receivedChunks} chunks, {partialResponse.Length} chars");
                Assert.AreEqual(maxChunks, receivedChunks);
                Assert.IsTrue(partialResponse.Length > 0);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Cancellation Test Error] {ex.Message}");
                Assert.Fail(ex.Message);
            }
        }

        /// <summary>
        /// IAsyncEnumerable StreamOnceAsync 대화 기록 테스트
        /// </summary>
        [TestMethod]
        public async Task IAsyncEnumerableStreamOnceHistoryTest()
        {
            try
            {
                // 먼저 대화 시작
                await AI.GetCompletionAsync("Remember the number 12345");
                int messagesAfterFirst = AI.ActivateChat.Messages.Count;

                // StreamOnceAsync로 질문 (대화 기록에 추가되지 않음)
                string onceResponse = "";
                await foreach (var chunk in AI.StreamOnceAsync("What number did I mention?"))
                {
                    onceResponse += chunk;
                }

                int messagesAfterOnce = AI.ActivateChat.Messages.Count;
                Assert.AreEqual(messagesAfterFirst, messagesAfterOnce, "StreamOnceAsync should not add to history");

                // 일반 스트리밍으로 확인 (이전 대화 기억)
                string normalResponse = "";
                await foreach (var chunk in AI.StreamAsync("Do you still remember the number?"))
                {
                    normalResponse += chunk;
                }

                Console.WriteLine($"[Once Response] {onceResponse}");
                Console.WriteLine($"[Normal Response] {normalResponse}");

                // 정상 스트리밍은 대화 기록에 추가됨
                Assert.IsTrue(AI.ActivateChat.Messages.Count > messagesAfterOnce);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[StreamOnce History Test Error] {ex.Message}");
                Assert.Fail(ex.Message);
            }
        }
    }
}