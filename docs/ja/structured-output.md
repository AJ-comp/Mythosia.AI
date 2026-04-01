# 構造化出力

構造化出力はモデルのレスポンスをC#型に直接デシリアライズします。Mythosia.AIには自動JSON修復が内蔵されており、モデルの軽微なフォーマットエラーを透過的に処理します。

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
using Mythosia.AI.Models;

// デフォルト: スキーマに合ったJSONを返すよう要求
service.StructuredOutputPolicy = StructuredOutputPolicy.Strict;

// 緩和: モデルにより多くの自由を与え、自動修復に依存
service.StructuredOutputPolicy = StructuredOutputPolicy.Lenient;
```
