using Mythosia.AI.Models.Enums;
using Mythosia.AI.Services.Base;
using Mythosia.AI.Services.OpenAI;
using Mythosia.Azure;

namespace Mythosia.AI.Tests.OpenAI;

/// <summary>
/// OpenAI 서비스 생성 팩토리. API 키를 한 번만 로드하여 공유.
/// </summary>
public static class ChatGptServiceFactory
{
    private static string? _apiKey;
    private static readonly SemaphoreSlim _semaphore = new(1, 1);

    public static AIService Create(AIModel model)
    {
        EnsureApiKey().GetAwaiter().GetResult();
        var service = new ChatGptService(_apiKey!, new HttpClient());
        service.ChangeModel(model);
        service.ActivateChat.SystemMessage = "You are a helpful assistant for testing purposes.";
        return service;
    }

    private static async Task EnsureApiKey()
    {
        if (_apiKey != null) return;
        await _semaphore.WaitAsync();
        try
        {
            if (_apiKey != null) return;
            var fetcher = new SecretFetcher(
                "https://mythosia-key-vault.vault.azure.net/",
                "momedit-openai-secret");
            _apiKey = await fetcher.GetKeyValueAsync();
        }
        finally { _semaphore.Release(); }
    }
}
