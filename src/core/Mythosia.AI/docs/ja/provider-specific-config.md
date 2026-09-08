# プロバイダー固有設定アーキテクチャ

> GPT-6 Astra、`AllowAsync`、`StartRunAsync`、共通の推論・検索 API は `Mythosia.AI` 7.1.0 から利用でき、共通型は `Mythosia.AI.Abstractions` 3.1.0 に含まれます。

## 原則

アプリケーションは[共通の推論・検索 API](https://github.com/AJ-comp/Mythosia.AI/blob/main/docs/ja/reasoning-and-search.md)で、作業に必要な推論量とホスト型検索を指定できます。`AIRequestFeatures` は論理的なリクエストごとにコピーされ、プロバイダーのアダプターが検証・変換します。プロバイダー固有の既定値はサービスに残り、キャッシュを維持する変更の状態は追跡中の会話に保存されます。`AICitation` はストリームの購読とは独立して出典を保持します。カスタムサービスは任意の `IAIRequestFeatureService` で対応し、`IAIService` に必須メンバーは追加されません。

| 設定タイプ | 配置場所 | 例 |
|------------|----------|-----|
| **共通設定** | `ChatBlock` | Temperature, TopP, MaxTokens, FrequencyPenalty 等 |
| **プロバイダー固有** | 各サービスクラス | ThinkingBudget (Gemini), ReasoningEffort (GPT) 等 |
| **関数ごとの実行許可** | `FunctionDefinition` | `AllowAsync`（既定値 `false`） |

`AllowAsync` は呼び出し側が選ぶ許可であり、モデルと API の対応状況はサービスが内部で判断します。`FunctionBuilder.WithAsync()` と `[AiFunction("lookup", "データを取得", AllowAsync = true)]` でも同じ許可を有効にできます。GPT-6 Astra では Responses で使用し、未対応のモデルでは API オプションを省略して同じハンドラーの結果を待ちます。設定した許可の値は変更しません。

## 現在の実装: サービスレベル

プロバイダー固有設定は各サービスクラスのプロパティとして管理します。

```csharp
// 共通設定 → ChatBlock
geminiService.ActivateChat.Temperature = 0.7f;
geminiService.ActivateChat.MaxTokens = 4096;

// プロバイダー固有設定 → サービス
geminiService.ThinkingBudget = 1024;
```

### メリット
- ChatBlockがプロバイダーに対して完全に無関心（クリーンな分離）
- OOP原則に適合（サービスが自身の固有設定を管理）
- サービスインスタンス1つに固有設定1つ → シンプルな構造

### デメリット
- 1つのサービス内の複数ChatBlockに同一の固有設定が適用される

## ChatBlockレベルへの移行が必要な場合

今後 **ChatBlockごとに固有設定を独立して維持する要件** が発生した場合、ChatBlock内にLazy初期化のコンフィグクラスを追加する方式でマイグレーションします。

```csharp
// 例（現在は未実装）
public class ChatBlock
{
    private GeminiConfig _gemini;
    public GeminiConfig Gemini => _gemini ??= new GeminiConfig();
}

// 使用
chatBlock.Gemini.ThinkingBudget = 1024;
```

### この方式が必要なシナリオ
- 1つのサービスインスタンスでChatBlock AとBが異なるThinkingBudgetを使用する必要がある場合
- 実際にはこのケースは非常に稀なため、現在はサービスレベルを維持

## 決定ログ

- **2026-02-12**: 最初 Option B（ChatBlockレベル）で実装後、サービスレベルにロールバック。固有設定はサービスに置くのが自然と判断。

## 実行の制御が必要になる場面

時間のかかる処理では、進行状況を表示したり、ユーザーが途中で条件を変更できるようにしたりする必要があります。`StartRunAsync` が返す `AIRun` でその処理を制御し、実行中の追加指示に対応するかどうかはプロバイダーが決定します。モデル設定はサービスで開始前に構成し、追加指示の前に `run.CanSteer` を確認します。利用場面、例、キャンセル、互換性は [Run の利用ガイド](../../../../../docs/ja/execution-api-transition.md)を参照してください。
