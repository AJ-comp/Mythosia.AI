using Microsoft.VisualStudio.TestTools.UnitTesting;
using Mythosia.AI;
using Mythosia.Azure;
using System;
using System.Collections.Generic;
using System.Text;

namespace Mythosia.AI.Tests
{
    [TestClass()]
    public class ClaudeServiceTests
    {
        [TestMethod()]
        public async Task ClaudeServiceTest()
        {
            try
            {
                SecretFetcher secretFetcher = new SecretFetcher("https://mythosia-key-vault.vault.azure.net/", "momedit-antropic-secret");
                ClaudeService claudeService = new ClaudeService(await secretFetcher.GetKeyValueAsync(), new HttpClient());

                // 질문 준비
                string prompt = "안녕하세요, Claude. 인공지능의 발전이 인류에게 미칠 수 있는 긍정적인 영향에 대해 설명해 주시겠습니까?";

                // Claude 3.5 Sonnet 모델을 사용하여 질의 및 응답 받기
                string response = await claudeService.GetCompletionAsync(prompt);
                await claudeService.StreamCompletionAsync(prompt, (message) => { Console.WriteLine(message); });

                // 응답 출력
                Console.WriteLine("Claude 3.5 Sonnet의 응답:");
                Console.WriteLine(response);
            }
            catch (ArgumentException aex)
            {
                Console.WriteLine("모델 선택 오류: " + aex.Message);
                Assert.Fail();
            }
            catch (Exception ex)
            {
                Console.WriteLine("API 요청 오류: " + ex.Message);
                Assert.Fail();
            }
        }
    }
}