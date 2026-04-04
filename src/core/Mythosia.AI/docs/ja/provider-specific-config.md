# プロバイダー固有設定アーキテクチャ

## 原則

| 設定タイプ | 配置場所 | 例 |
|------------|----------|-----|
| **共通設定** | `ChatBlock` | Temperature, TopP, MaxTokens, FrequencyPenalty 等 |
| **プロバイダー固有** | 各サービスクラス | ThinkingBudget (Gemini), ReasoningEffort (GPT) 等 |

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
