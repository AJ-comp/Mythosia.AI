using Azure;
//using Azure.AI.OpenAI.Assistants;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Mythosia.AI
{
    public static class AssistantsClientHelper
    {

        /*
        public static async Task<string> RunAsync(this AssistantsClient client, Assistant assistant, string data, string history)
        {
            // 스레드 생성
            var thread = client.CreateThreadAsync().Result.Value;


        }
        */

        /*
        public static async Task<string> RunAsync(this AssistantsClient client, Assistant assistant, AssistantThread thread, string data)
        {
            // Add a user question to the thread
            Response<ThreadMessage> messageResponse = await client.CreateMessageAsync(
                thread.Id,
                MessageRole.User,
                data
            );

            // Run the thread
            Response<ThreadRun> runResponse = await client.CreateRunAsync(
                thread.Id,
                new CreateRunOptions(assistant.Id)
            );
            ThreadRun run = runResponse.Value;

            // Wait for the assistant to respond
            do
            {
                await Task.Delay(TimeSpan.FromMilliseconds(500));
                runResponse = await client.GetRunAsync(thread.Id, run.Id);
            } while (runResponse.Value.Status == RunStatus.Queued || runResponse.Value.Status == RunStatus.InProgress);


            // Get the messages in the thread once the run has completed
            return (runResponse.Value.Status == RunStatus.Completed) ? await GetFormattedMessagesAsync(client, thread.Id)
                                                                                                    : $"Run status is {runResponse.Value.Status}, unable to fetch messages.";
        }


        public static async Task ResetThreadIfMessageCountAsync(this AssistantsClient client, AssistantThread thread, ushort count)
        {
            // 현재 쓰레드의 메시지 수 확인
            var messagesResponse = await client.GetMessagesAsync(thread.Id);
            int messageCount = messagesResponse.Value.Data.Count;

            if (messageCount >= count)
            {
                // 현재 쓰레드 삭제
                await client.DeleteThreadAsync(thread.Id);

                // 새 스레드 생성
                thread = (await client.CreateThreadAsync()).Value;
            }
        }


        private static async Task<string> GetFormattedMessagesAsync(AssistantsClient client, string threadId)
        {
            // 비동기로 메시지 목록을 가져옴
            Response<PageableList<ThreadMessage>> afterRunMessagesResponse = await client.GetMessagesAsync(threadId);
            IReadOnlyList<ThreadMessage> messages = afterRunMessagesResponse.Value.Data;

            // StringBuilder를 사용하여 결과를 누적
            StringBuilder resultBuilder = new StringBuilder();

            // 최신 메시지부터 오래된 메시지 순서대로 처리
            foreach (ThreadMessage threadMessage in messages)
            {
                if (threadMessage.Role == MessageRole.User) break;

                // 각 메시지의 생성 시간과 역할을 추가
                //                resultBuilder.Append($"{threadMessage.CreatedAt:yyyy-MM-dd HH:mm:ss} - {threadMessage.Role,10}: ");

                // 메시지의 콘텐츠를 처리
                foreach (MessageContent contentItem in threadMessage.ContentItems)
                {
                    if (contentItem is MessageTextContent textItem)
                    {
                        resultBuilder.Append(textItem.Text);
                    }
                    else if (contentItem is MessageImageFileContent imageFileItem)
                    {
                        resultBuilder.Append($"<image from ID: {imageFileItem.FileId}>");
                    }
                }

                // 각 메시지 끝에 줄바꿈 추가
                resultBuilder.AppendLine();
            }

            // 결과 문자열을 반환
            return resultBuilder.ToString();
        }
        */

    }



    /*
    internal class AzureOpenAIClient
    {
        private HttpClient _httpClient;

        public string EndPoint { get; private set; } = string.Empty;
        public string APIKey { get; private set; } = string.Empty;

        public AzureOpenAIClient(string endPoint, string apiKey)
        {
            EndPoint = endPoint;

            APIKey = apiKey;

            _httpClient = new HttpClient();
            _httpClient.BaseAddress = new Uri(endPoint);
            _httpClient.DefaultRequestHeaders.Add("api-key", apiKey);
        }


        async Task<string> UploadFile(string dataSetFullPath, string purpose)
        {
            var dataset = Path.GetFileName(dataSetFullPath);
            using var fs = File.OpenRead(dataSetFullPath);

            var fileContent = new StreamContent(fs);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            fileContent.Headers.ContentDisposition = new ContentDispositionHeaderValue("form-data")
            {
                Name = "file",
                FileName = dataset
            };

            using var formData = new MultipartFormDataContent();
            formData.Add(new StringContent(purpose), "purpose");
            formData.Add(fileContent);

            var response = await _httpClient.PostAsync("openai/files?api-version=2023-10-01-preview", formData);
            if (response.IsSuccessStatusCode)
            {
                var data = await response.Content.ReadFromJsonAsync<FileUploadResponse>();
                return data.id;
            }

            return string.Empty;
        }


        private async Task<T> ReadFromJsonAsync<T>(HttpContent content)
        {
            // JSON 문자열 읽기
            var jsonString = await content.ReadAsStringAsync();

            // JSON 문자열을 T 타입으로 역직렬화
//            jsonString.json
            return JsonSerializer.Deserialize<T>(jsonString);
        }
    }
    */
}
