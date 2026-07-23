# 会話要約メカニズム

## 概要

長い会話におけるトークンコストとコンテキストウィンドウ制限を管理するための自動要約メカニズムです。
`SummaryConversationPolicy`がトリガー条件を検出すると、古いメッセージを要約テキストに圧縮し、最近のメッセージのみを保持します。

## 核心設計原則

1. **要約タイミング**: トリガーによる要約は、関数呼び出しチェーン（ラウンド）の途中ではなく全ラウンド完了後にのみ実行。例外はコンテキスト超過リカバリのみで、そこでも現在の質問と先行ラウンドの結果は保持される（[コンテキスト超過リカバリ](#コンテキスト超過リカバリトリガー要約とは別の経路)参照）
2. **要約ポリシーはAPI制約を知らない**: `GetMessagesToSummarize`はルール通りにトリミングするのみ。User-firstなどのAPI制約は各プロバイダーが処理
3. **原本不変**: 要約テキストは`CurrentSummary`に保存され、システムプロンプトに注入。APIリクエスト用メッセージリストはコピー

## トリガー条件

```csharp
// メッセージ数ベース
var policy = SummaryConversationPolicy.ByMessage(triggerCount: 10, keepRecentCount: 4);

// トークン数ベース（API返却の実トークン使用）
var policy = SummaryConversationPolicy.ByToken(triggerTokens: 8000, keepRecentTokens: 2000);

// 両方（OR条件）
var policy = SummaryConversationPolicy.ByBoth(triggerTokens: 8000, triggerCount: 20, ...);
```

トークンベースのトリガーは、APIが返す公式`InputTokens`値（`LastKnownInputTokens`）を優先使用します。
実際の値がない場合のみ、ローカル推定値（`EstimateTokens`）にフォールバックします。

## 全体フロー（関数呼び出し含む）

### 設定

```
triggerCount=3, keepRecentCount=2
関数: get_user_id, get_user_details
```

### ステップ1: ユーザー質問

```
StreamAsync(User("john_doeの情報を教えて"))

ActivateChat.Messages:
  [0] User: "john_doeの情報を教えて"
```

### ステップ2: Round 0 — 最初の関数呼び出し

LLMが`get_user_id("john_doe")`の呼び出しを決定。実行後:

```
ActivateChat.Messages:
  [0] User: "john_doeの情報を教えて"
  [1] Assistant: function_call(get_user_id)
  [2] Function: "user_123"
```

`hasFunctionResult = true` → 次のラウンドへ

### ステップ3: Round 1 — 2番目の関数呼び出し

LLMが`get_user_details("user_123")`を呼び出し。実行後:

```
ActivateChat.Messages:
  [0] User: "john_doeの情報を教えて"
  [1] Assistant: function_call(get_user_id)
  [2] Function: "user_123"
  [3] Assistant: function_call(get_user_details)
  [4] Function: "{id: user_123, name: Test User, email: test@example.com}"
```

`hasFunctionResult = true` → 次のラウンドへ

### ステップ4: Round 2 — 最終テキスト応答

LLMが関数結果を統合してテキスト応答を生成:

```
ActivateChat.Messages:
  [0] User: "john_doeの情報を教えて"
  [1] Assistant: function_call(get_user_id)
  [2] Function: "user_123"
  [3] Assistant: function_call(get_user_details)
  [4] Function: "{id: user_123, ...}"
  [5] Assistant: "john_doeの情報は以下の通りです..."
```

`hasFunctionResult = false` → 全ラウンド完了

### ステップ5: ストリーミング終了後の要約実行

```
ShouldSummarize: 6メッセージ > triggerCount(3) → トリガー!

GetMessagesToSummarize:
  keepFromIndex = 6 - 2 = 4
  要約対象: [0]~[3] (User, Asst(FC), Func, Asst(FC))
  保持対象: [4]~[5] (Function, Assistant)
```

要約生成とメッセージ削除後:

```
CurrentSummary = "ユーザーがjohn_doeの情報を要求。get_user_id→user_123、get_user_detailsで詳細を照会"

ActivateChat.Messages:
  [0] Function: "{id: user_123, ...}"
  [1] Assistant: "john_doeの情報は以下の通りです..."

SystemMessage:
  "You are a helpful assistant.

  [Previous conversation summary]
  ユーザーがjohn_doeの情報を要求。get_user_id→user_123、get_user_detailsで詳細を照会"
```

### ステップ6: 次のユーザー質問

```
StreamAsync(User("メールアドレスも教えて"))

ActivateChat.Messages:
  [0] Function: "{id: user_123, ...}"
  [1] Assistant: "john_doeの情報は以下の通りです..."
  [2] User: "メールアドレスも教えて"
```

APIリクエスト構築時に`EnsureUserFirstMessage`が適用:

```
messages[0] = Function → Userではない → 合成User挿入

APIに送信されるメッセージ:
  [0] User: "(Continuing from previous conversation context)"  ← 合成
  [1] Function: "{id: user_123, ...}"
  [2] Assistant: "john_doeの情報は以下の通りです..."
  [3] User: "メールアドレスも教えて"
```

## User-First制約の処理

一部のAPI（Gemini、Claude）はメッセージ配列がUserロールで始まることを要求します。
要約トリミング後、最初のメッセージがAssistant/Functionになる可能性があるため、各プロバイダーのリクエストビルダーで処理します。

```csharp
// AIService.cs
protected static void EnsureUserFirstMessage(List<Message> messages)
{
    if (messages.Count == 0) return;
    if (messages[0].Role == ActorRole.User) return;
    messages.Insert(0, new Message(ActorRole.User,
        "(Continuing from previous conversation context)"));
}
```

- **適用対象**: Gemini、Claude（リクエストビルダー4箇所）
- **非適用**: OpenAI、Grok、DeepSeek、Sonar、Qwen（User-first制約なし）
- **原本不変**: `GetLatestMessages().ToList()`で作成したコピーにのみ適用

## 要約がラウンド完了後に実行される理由

```
X ラウンド途中の要約:
  Round 0: FC呼び出し → 結果保存
  Round 1: [ここで要約実行] → FC結果が削除！ → LLMがコンテキストを失う

O 完了後の要約:
  Round 0: FC呼び出し → 結果保存
  Round 1: FC呼び出し → 結果保存
  Round 2: LLMが全FC結果でテキスト生成（完了）
  [ここで要約実行] → 次のターンの準備、現在の応答に影響なし
```

## コンテキスト超過リカバリ：トリガー要約とは別の経路

トリガー要約は「次のターンのための片付け」なので、ラウンド途中で実行する理由がない。
しかし**ラウンド3でサーバーが「コンテキストを超過した」と拒否**した場合は事情が違う。
ターン終了まで待つことができない — 今縮めなければ、このターンは失敗で終わるからだ。

そのためリカバリ圧縮はラウンド内で実行される。上の X が懸念する「FC結果の削除」は、
切り取り位置を**最後のUserメッセージ**までしか手前に寄せないクランプで防いでいる。

```
Round 0: FC呼び出し → 結果保存
Round 1: FC呼び出し → 結果保存
Round 2: [サーバーが400で拒否]
         → 現在の質問より「前」の古い会話だけを要約して畳む
         → 現在の質問と Round 0・1 のFC結果はそのまま維持
         → Round 2 のみ再実行（Round 0・1 は再実行しない = ツールの二重実行なし）
```

畳む古い会話がない場合は、**要約リクエストすら送らずに**即座に諦める。
無理に削除しても要求サイズは縮まらず、履歴だけを失うからだ。停止装置は3つ：

| 理由 | 意味 |
|---|---|
| `nothing-to-cut` | 現在の質問より前に切るものがない |
| `window-clipped` | 切る区間がすでに `MaxMessageCount` ウィンドウの外 — 削除しても要求が縮まらない |
| `retries-exhausted` | `ContextRecoveryMaxRetries` を使い切った |

いずれの場合も**要約呼び出しも削除も行わず**、元のエラーをそのまま伝播する。

> **非ストリーミングは異なる。** 再試行がプロバイダーのラウンドループを0から回し直すため、
> すでに実行済みのツールがあれば二度実行されてしまう。そのため該当する場合は
> `tool-side-effects` を理由にリカバリせず停止する。ラウンド単位の再実行はストリーミング経路のみ。
