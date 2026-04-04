# [To-Be] コンシューマーAPI改善

> **核心目標**: 外部からクリーンでエレガントに使用できること。モデル切り替えが1行であること。

## As-Is — 現在の不便さ

```csharp
// プロバイダーごとにサービス型を知る必要があり、HttpClientを直接管理する必要がある
var httpClient = new HttpClient();
var gpt = new OpenAIService("sk-...", httpClient);
var response = await gpt.GetCompletionAsync("hello");

// モデル切り替え？ → サービスを新規作成する必要がある
var httpClient2 = new HttpClient();
var claude = new AnthropicService("sk-ant-...", httpClient2);
```

## To-Be — 理想的なコンシューマー体験

### 1. 1行で登録

```csharp
services.AddMythosiaAI(o =>
{
    o.AddOpenAI("sk-...");
    o.AddAnthropic("sk-ant-...");
    o.AddGoogle("AIza...");
});
```

### 2. モデルベースの使用 — プロバイダーを知る必要なし

```csharp
public class ChatController(IAIServiceFactory ai)
{
    public async Task<string> Ask(string prompt)
    {
        // モデルを指定するだけで、プロバイダーは自動決定
        var service = ai.Create(AIModel.Gpt4oMini);
        return await service.GetCompletionAsync(prompt);
    }
}
```

### 3. モデル切り替えが1行

```csharp
// GPT → Claude 切り替え
var service = ai.Create(AIModel.Claude4Sonnet);

// 会話履歴をそのまま引き継ぎ
var service = ai.Create(AIModel.Claude4Sonnet).CopyFrom(previousService);
```

### 4. ストリーミングも同じパターン

```csharp
var service = ai.Create(AIModel.Gpt4oMini);

await foreach (var chunk in service.StreamAsync("explain quantum computing"))
{
    Console.Write(chunk);
}
```

## 設計原則

| 原則 | 説明 |
|------|------|
| **プロバイダー非依存** | コンシューマーは `AIModel` enumだけ知ればよい |
| **HttpClient透過** | `IHttpClientFactory`を内部で使用、コンシューマーに公開しない |
| **既存互換** | `new OpenAIService(key, httpClient)` 方式も引き継き動作 |
| **設定分離** | APIキーは登録時、モデル選択は使用時 |
