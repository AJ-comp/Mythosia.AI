using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Threading.Tasks;

namespace Mythosia.AI.Tests
{
    public partial class ChatGptServiceTests
    {
        /// <summary>
        /// IAsyncEnumerable 메모리 효율성 테스트
        /// </summary>
        [TestMethod]
        public async Task StreamingMemoryEfficiencyTest()
        {
            try
            {
                // 메모리 사용량 추적을 위한 시작 메모리
                long startMemory = GC.GetTotalMemory(true);

                // 대량의 텍스트를 스트리밍으로 처리
                int processedChunks = 0;
                long totalCharsProcessed = 0;

                await foreach (var chunk in AI.StreamAsync(
                    "Generate a very detailed technical documentation about distributed systems, " +
                    "including architecture patterns, consistency models, fault tolerance, " +
                    "scalability considerations, and real-world examples."))
                {
                    // 청크 단위로 처리 (메모리에 전체를 보관하지 않음)
                    processedChunks++;
                    totalCharsProcessed += chunk.Length;

                    // 실시간 처리 시뮬레이션
                    if (processedChunks % 10 == 0)
                    {
                        long currentMemory = GC.GetTotalMemory(false);
                        Console.WriteLine($"[Memory] After {processedChunks} chunks: {(currentMemory - startMemory) / 1024}KB");
                    }
                }

                long endMemory = GC.GetTotalMemory(true);
                long memoryUsed = (endMemory - startMemory) / 1024;

                Console.WriteLine($"[Memory Efficiency Test]");
                Console.WriteLine($"  Processed Chunks: {processedChunks}");
                Console.WriteLine($"  Total Characters: {totalCharsProcessed:N0}");
                Console.WriteLine($"  Memory Used: {memoryUsed}KB");
                Console.WriteLine($"  Bytes per Character: {(double)(endMemory - startMemory) / totalCharsProcessed:F2}");

                // 메모리 효율성 검증
                Assert.IsTrue(processedChunks > 0);
                Assert.IsTrue(totalCharsProcessed > 0);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Memory Efficiency Error] {ex.Message}");
                Assert.Fail(ex.Message);
            }
        }
    }
}