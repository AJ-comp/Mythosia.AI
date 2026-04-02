# クイックスタート

## インストール

コアパッケージをインストールします:

```bash
dotnet add package Mythosia.AI
```

LINQオペレーター（例: `ToListAsync`）を使ったストリーミングが必要な場合は追加でインストールします:

```bash
dotnet add package System.Linq.Async
```

## 最初の補完リクエスト

プロバイダーを選択し、APIキーと`HttpClient`でサービスインスタンスを作成します:

```csharp
using Mythosia.AI;

var http = new HttpClient();

// OpenAI
var service = new OpenAIService("your-openai-api-key", http);

// Anthropic
// var service = new AnthropicService("your-anthropic-api-key", http);

// Google
// var service = new GoogleAIService("your-google-api-key", http);
```

`GetCompletionAsync`を呼び出します:

```csharp
var response = await service.GetCompletionAsync("こんにちは！");
Console.WriteLine(response);
```

## モデルの選択

各サービスはデフォルトモデルを使用しますが、明示的に指定することもできます:

```csharp
var service = new OpenAIService("your-api-key", http)
{
    Model = AIModels.OpenAI.Gpt4_1
};
```

利用可能なすべてのモデル定数については[APIリファレンス](../../api/Mythosia.AI.Models.AIModels.yml)を参照してください。

## 次のステップ

- [基本的な補完](completions.md) — システムプロンプト、会話履歴、マルチモーダル
- [ストリーミング](streaming.md) — トークン単位の出力と推論ストリーミング
- [関数呼び出し](function-calling.md) — モデルにコードを呼び出させる
- [構造化出力](structured-output.md) — レスポンスをC#型にデシリアライズ
