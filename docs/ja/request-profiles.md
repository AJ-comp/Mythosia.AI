# AIRequestProfile

## これは何ですか？

`AIRequestProfile`は、生成パラメーター — Temperature、MaxTokens、ステートレスモード、関数呼び出し — を**単一リクエストに対してのみ**オーバーライドします。サービスのグローバル設定はそのまま維持されます。

## どんな問題を解決するのか？

クリエイティブな会話用に設定されたチャットボットがあるとします:

```csharp
var service = new OpenAIService(apiKey, http)
    .WithTemperature(0.8f)
    .WithMaxTokens(2048)
    .WithSystemMessage("あなたはクリエイティブな文章アシスタントです。");
```

RAGパイプラインでユーザーのクエリを低いTemperatureで、履歴なしで書き換える必要があります。`AIRequestProfile`**なし**ではこうなります:

```csharp
// ❌ AIRequestProfileなし — 手動での状態管理
var savedTemp = service.Temperature;
var savedMax = service.MaxTokens;
var savedStateless = service.StatelessMode;

service.Temperature = 0.1f;
service.MaxTokens = 256;
service.StatelessMode = true;

var rewritten = await service.GetCompletionAsync("このクエリを書き換えてください: ...");

// すべて復元 — 忘れやすく、スレッドセーフではない
service.Temperature = savedTemp;
service.MaxTokens = savedMax;
service.StatelessMode = savedStateless;
```

この方法は冗長で、エラーが起きやすく、**マルチスレッドシナリオで壊れます**（例：同時ユーザーを処理するウェブサーバー）。復元前に例外がスローされると、サービスが破損状態のまま残ります。

`AIRequestProfile`を**使えば**一行です:

```csharp
// ✅ AIRequestProfile使用 — クリーンで安全
var rewritten = await service.GetCompletionAsync("このクエリを書き換えてください: ...",
    new AIRequestProfile { Temperature = 0.1f, MaxTokens = 256, Stateless = true });
```

サービスのグローバル設定は一切触れません。クリーンアップも不要。スレッドセーフです。

## 利用可能なプロパティ

```csharp
var profile = new AIRequestProfile
{
    Temperature = 0.1f,       // Temperatureをオーバーライド
    MaxTokens = 256,          // 最大出力トークンをオーバーライド
    Stateless = true,         // このやり取りを会話履歴に追加しない
    DisableFunctions = true,  // このリクエストで関数呼び出しをスキップ
    DisableReasoning = true   // このリクエストで推論/思考過程をスキップ
};

var response = await service.GetCompletionAsync("プロンプト", profile);
```

すべてのプロパティはオプションです — オーバーライドしたいものだけ設定してください。設定しないものはサービスの現在値を使用します。

## 事前定義プロファイル

一般的なシナリオ向けに、プロパティを手動で設定する必要のない組み込みプロファイルが提供されています:

```csharp
// クエリ書き換え: 低Temperature、小さなトークン予算、ステートレス
var rewritten = await service.GetCompletionAsync(query, RequestProfiles.QueryRewrite);

// 要約: やや高いTemperature、適度なトークン
var summary = await service.GetCompletionAsync(text, RequestProfiles.Summarization);
```

## 実際の使用例

### RAGパイプラインでの内部クエリ書き換え

```csharp
// ユーザー向け会話用に設定されたメインサービス
var service = new OpenAIService(apiKey, http)
    .WithTemperature(0.7f)
    .WithMaxTokens(4096);

// 異なる設定でクエリを書き換え — サービスは変更されない
var betterQuery = await service.GetCompletionAsync(
    $"検索用に書き換えてください: {userQuery}",
    RequestProfiles.QueryRewrite);

// 通常の会話を続行 — まだTemperature 0.7、MaxTokens 4096のまま
var answer = await service.GetCompletionAsync(userQuery);
```

### 特定のステップで関数を無効化

```csharp
// サービスに関数が登録された状態
service.WithFunction("search_web", "ウェブ検索", ...);

// この1回の呼び出しだけ関数呼び出しをスキップ — 直接回答のみ
var directAnswer = await service.GetCompletionAsync(
    "2 + 2は何ですか？",
    new AIRequestProfile { DisableFunctions = true });
```

## AIRequestContextとの組み合わせ

最大限の制御のために両方を一緒に渡すことができます:

```csharp
var response = await service.GetCompletionAsync(
    prompt,
    profile: RequestProfiles.QueryRewrite,
    context: new AIRequestContext { SystemMessageSuffix = "\n簡潔に答えてください。" }
);
```

リクエストへのコンテンツ注入の詳細は[AIRequestContext](request-contexts.md)を参照してください。
