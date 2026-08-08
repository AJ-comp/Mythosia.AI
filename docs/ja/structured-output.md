# 構造化出力

## 構造化出力が必要な理由

LLMはデフォルトで自由形式のテキストを返します。アプリケーションがレスポンスを**プログラム的に処理**する必要がある場合 — データベースへの保存、別のAPIへの受け渡し、型付きUIでのレンダリング — テキストを自分でパースする必要があります。これはモデルが表現を変えると壊れる脆弱な正規表現や`string.Contains`チェックにつながります。

構造化出力は、モデルにC#型のスキーマに合ったJSONを返すよう指示することでこれを解決します。Mythosia.AIがスキーマ生成、プロンプト注入、デシリアライズを自動的に処理します — モデルが生成する可能性のある軽微なフォーマットエラーに対する**自動JSON修復**も含まれます。

### こんな場面で役立ちます

- 非構造化テキストからエンティティ、分類、構造化データを抽出
- AI生成コンテンツから型付きAPIレスポンスを構築
- 特定のデータ形状を期待する下流パイプラインにAI出力を供給
- モデルからの**信頼性の高い、機械可読な**出力が必要なあらゆるシナリオ

## 従来の方法の課題

モデルのレスポンスから天気データを抽出する必要があるとします。構造化出力**なし**では:

```csharp
// ❌ 構造化出力なし — 脆弱な手動パース
var text = await service.GetCompletionAsync("東京の天気はどうですか？");
// text = "東京の天気は晴れで、気温は22度です。"

// 自分でパースする必要があります...
var city = "東京"; // ハードコード？正規表現？
var tempMatch = Regex.Match(text, @"(\d+)度");
int temp = tempMatch.Success ? int.Parse(tempMatch.Groups[1].Value) : 0;
// モデルが"22度"ではなく"二十二度"と言ったら？ 💥
```

モデルが表現を変えるたびに壊れます。構造化出力を**使えば**:

```csharp
// ✅ 構造化出力使用 — 型安全、自動
public record WeatherResponse(string City, string Condition, int TemperatureC);

var result = await service.GetCompletionAsync<WeatherResponse>(
    "東京の天気はどうですか？");

Console.WriteLine(result.City);         // 東京
Console.WriteLine(result.Condition);    // 晴れ
Console.WriteLine(result.TemperatureC); // 22
```

モデルにC#型に合ったJSONを返すよう指示されます。Mythosia.AIが自動的にデシリアライズします。モデルがわずかに不正なJSON（カンマの欠落、末尾のテキスト）を生成しても、組み込みの**自動修復**がデシリアライズ前に修正します — 手動のエラー処理は不要です。

## 基本的な使い方

`GetCompletionAsync`に型パラメーターを渡します:

```csharp
public record WeatherResponse(string City, string Condition, int TemperatureC);

var result = await service.GetCompletionAsync<WeatherResponse>(
    "東京の天気はどうですか？");

Console.WriteLine(result.City);         // 東京
Console.WriteLine(result.Condition);    // 晴れ
Console.WriteLine(result.TemperatureC); // 22
```

## コレクション

コレクション型はラッパーDTOなしで直接使用できます:

```csharp
public record Entity(string Name, string Type);

var entities = await service.GetCompletionAsync<List<Entity>>(
    "このテキストからすべての人物と組織を抽出してください: ...");

foreach (var e in entities)
    Console.WriteLine($"{e.Type}: {e.Name}");
```

## ストリーミング + 構造化出力

リアルタイムでテキストをストリーミングしながら最終的なデシリアライズされたオブジェクトも取得します:

```csharp
var run = service.BeginStream("製品サマリーを生成してください").As<ProductDto>();

// リアルタイム出力
await foreach (var chunk in run.Stream())
    Console.Write(chunk);

// 最終パース結果
ProductDto product = await run.Result;
```

## 構造化出力ポリシー

モデルが構造化出力を生成する厳密さを制御します:

```csharp
using Mythosia.AI.Extensions;
using Mythosia.AI.Models;

// Strict: 自動修復を最大3回まで試行
service.WithStructuredOutputPolicy(StructuredOutputPolicy.Strict);

// NoRetry: 修復を再試行せず、最初の検証エラーを返す
service.WithStructuredOutputPolicy(StructuredOutputPolicy.NoRetry);
```
