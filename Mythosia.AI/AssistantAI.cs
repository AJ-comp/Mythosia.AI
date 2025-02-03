using Azure;
//using Azure.AI.OpenAI.Assistants;
//using Azure.Search.Documents.Indexes;
//using Mythosia.Azure;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Mythosia.AI
{
    /*
    public class ProcessResult
    {
        public bool IsSuccess { get; private set; }
        public string Status { get; private set; } = string.Empty;
        public string Message { get; private set; } = string.Empty;

        public ProcessResult(bool isSuccess, string status, string message)
        {
            IsSuccess = isSuccess;
            Status = status;
            Message = message;
        }
    }

    public class AssistantAI
    {
        public AssistantsClient Client { get; private set; }
        public Lazy<Assistant> Assistant { get; private set; }
        public Lazy<AssistantThread> Thread { get; private set; }

        public SearchIndexClient SearchIndex { get; private set; }


        public AssistantAI(SecretFetcher endPointSecret, SecretFetcher keySecret, AssistantCreationOptions assistantCreationOptions)
        {
            var endPoint = new Uri(endPointSecret.GetKeyValueAsync().Result);
            var key = new AzureKeyCredential(keySecret.GetKeyValueAsync().Result);

            // 초기화 작업 수행
            Client = new AssistantsClient(endPoint, key);

            Assistant = new Lazy<Assistant>(() => Client.CreateAssistantAsync(assistantCreationOptions).Result.Value);
            Thread = new Lazy<AssistantThread>(() => Client.CreateThreadAsync().Result.Value);
        }


        public AssistantAI(string endpoint, string key, AssistantCreationOptions assistantCreationOptions)
        {
            // 초기화 작업 수행
            Client = new AssistantsClient(new Uri(endpoint), new AzureKeyCredential(key));

            Assistant = new Lazy<Assistant>(() => Client.CreateAssistantAsync(assistantCreationOptions).Result.Value);
            Thread = new Lazy<AssistantThread>(() => Client.CreateThreadAsync().Result.Value);
        }


        public async Task<ProcessResult> RunOnNewThreadAsync(string data)
        {
            await Client.DeleteThreadAsync(Thread.Value.Id);
            Thread = new Lazy<AssistantThread>(() => Client.CreateThreadAsync().Result.Value);

            return await RunAsync(data);
        }


        public async Task<ProcessResult> RunAsync(string data)
        {
            // Add a user question to the thread
            Response<ThreadMessage> messageResponse = await Client.CreateMessageAsync(
                Thread.Value.Id,
                MessageRole.User,
                data
            );

            // Run the thread
            Response<ThreadRun> runResponse = await Client.CreateRunAsync(
                Thread.Value.Id,
                new CreateRunOptions(Assistant.Value.Id)
            );
            ThreadRun run = runResponse.Value;

            // Wait for the assistant to respond
            do
            {
                await Task.Delay(TimeSpan.FromMilliseconds(500));
                runResponse = await Client.GetRunAsync(Thread.Value.Id, run.Id);
            } while (runResponse.Value.Status == RunStatus.Queued || runResponse.Value.Status == RunStatus.InProgress);

            // Get the messages in the thread once the run has completed
            return (runResponse.Value.Status == RunStatus.Completed)
                ? new ProcessResult(true, runResponse.Value.Status.ToString(), await GetFormattedMessagesAsync())
                : new ProcessResult(false, runResponse.Value.Status.ToString(), string.Empty);
        }


        private async Task<string> GetFormattedMessagesAsync()
        {
            // 비동기로 메시지 목록을 가져옴
            Response<PageableList<ThreadMessage>> afterRunMessagesResponse = await Client.GetMessagesAsync(Thread.Value.Id);
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
    }
    */
}
