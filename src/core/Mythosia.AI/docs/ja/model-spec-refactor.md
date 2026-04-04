# [To-Be] モデルメタデータ（ModelSpec）リファクタリング提案

## 背景

- 現在 `AIService.Model` は `string` で保持されています。
- 各サービスが文字列比較でモデルの機能やトークン制限を判断しています。
- モデル追加は迅速ですが、ロジックが分散しメンテナンスコストが増加しています。

## 目標

- **柔軟性**: 未登録/カスタムモデル文字列をそのまま受容
- **集中化**: 既知モデルのメタデータを一箇所で管理
- **互換性**: 既存API/動作への破壊的変更を最小化

## 提案構造（ハイブリッド）

- `Model` は **string のまま維持**
- **ModelSpec（メタデータ）** をオプションとして導入
- 既知モデルのみ `ModelSpec` にresolve
- 不明な文字列は **既存ロジックのまま** 処理

### ModelSpec フィールド例（草案）

- ModelId (string)
- Provider
- MaxOutputTokens（または MaxTokensLimit）
- Capabilities
  - Vision対応
  - Reasoning/Thinking対応
  - Function Calling対応
- オプションのデフォルト値: DefaultMaxTokens 等

> 注: プロバイダー固有設定（ReasoningEffort, ThinkingBudget等）は **サービスクラスに維持** します。

## 解決ルール

1. `Model` 設定時、**ModelSpecRegistry** で既知モデルかを試行
2. **resolved** → ModelSpecベースでcapability/limitを適用
3. **not resolved** → 既存のサービス別文字列パースロジックを使用
4. 不明な文字列でも正常に動作すること

## 期待効果

- 既知モデルのメタデータ集中化 → **保守性向上**
- カスタムモデル対応の **柔軟性維持**
- 段階的マイグレーションが可能

## マイグレーション草案

1. `ModelSpec`/`ModelSpecRegistry` スケルトン導入
2. 既知モデルを段階的に登録
3. サービス別パースロジックに **Spec優先 + フォールバック** を追加
4. テスト強化（既知モデルメタ、不明文字列パス）

## 検討事項

- 不明モデルに対するユーザー定義メタデータ提供方式（オプション）
- エイリアス（別名）モデルの処理ポリシー
- Specに含める最小メタデータ範囲
