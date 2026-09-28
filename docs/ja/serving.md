# 実行中のモデルサーバーを管理する

モデル選択画面や運用ツールでは、プロンプトを送る前にサーバーの正常性、利用可能なモデル、ロード状態を確認する必要があります。Serving パッケージは Ollama・llama.cpp・vLLM の確認を共通インターフェイスにまとめ、ランタイム固有の操作は明示的に実行できるようにします。

モデル選択画面への一覧表示、サーバーへの接続可否の確認、対応ランタイムでのモデルの常駐管理、エンジンのメトリック取得に使えます。ランタイムを変更しても、アプリケーションの共通確認コードを維持できます。

これらのクライアントは既存の HTTP サーバーに接続します。エンジンのインストールやホスティング、GPU のレンタル、チャット、埋め込み生成は別のコンポーネントが担当します。チャットは vLLM 向けの `QwenService` など適切な AI サービスを引き続き使い、RAG の埋め込みプロバイダーも独立しています。機能確認でモデルが自動ロードされることはありません。SGLang は未実装です。

## パッケージを選ぶ

| パッケージ | バージョン | 用途 |
| --- | --- | --- |
| `Mythosia.AI.Serving.Abstractions` | 1.0.0 | アプリケーションや独自の管理アダプターで使う共通契約。パッケージ依存関係はありません。 |
| `Mythosia.AI.Serving.Ollama` | 1.0.0 | Ollama の確認、モデルのダウンロード、明示的なプリロードとアンロード。 |
| `Mythosia.AI.Serving.LlamaCpp` | 1.0.0 | llama.cpp の確認、メトリック取得、Router モードでのモデル管理。 |
| `Mythosia.AI.Serving.Vllm` | 1.1.0 | 共通 API または既存の vLLM 固有 API による vLLM の確認とメトリック取得。 |

4 つのパッケージはいずれも .NET Standard 2.1 を対象とします。利用するアダプターをインストールすると、抽象化パッケージも自動的に追加されます。アダプターは共通契約と Newtonsoft.Json に依存し、コア AI パッケージや RAG パッケージからは独立しています。

## サーバーの状態を変更せずに調べる

使用するランタイムの具象パッケージをインストールします。以下は Ollama の例です。他のサーバーには対応する名前空間の `VllmServer` または `LlamaCppServer` を選びます。確認には読み取り専用リクエストを使い、ロード・生成・ダウンロード命令は送りません。

```bash
dotnet add package Mythosia.AI.Serving.Ollama --version 1.0.0
```

```csharp
using System;
using System.Net.Http;
using System.Threading;
using Mythosia.AI.Serving;
using Mythosia.AI.Serving.Ollama;

using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
using var cancellation = new CancellationTokenSource();
IModelServer server = new OllamaServer("http://localhost:11434", http,
    apiKey: Environment.GetEnvironmentVariable("MODEL_SERVER_API_KEY"));

var health = await server.GetHealthAsync(cancellation.Token);
var info = await server.GetInfoAsync(cancellation.Token);
var capabilities = await server.GetCapabilitiesAsync(cancellation.Token);
var models = await server.GetModelsAsync(cancellation.Token);

foreach (var model in models)
    Console.WriteLine($"{model.Id}: {model.InstallationState} / {model.LoadState}");
```

エンドポイントにはサーバーのルートを指定し、必要ならリバースプロキシのパスプレフィックスを含めます。API キーは省略可能で、リクエストごとに Bearer 資格情報として送信されます。クライアントは `HttpClient.DefaultRequestHeaders` を変更せず、渡された `HttpClient` を破棄しません。アプリケーションのライフサイクルに合わせて再利用・破棄してください。この Ollama の例では、タイムアウトはストリーミング応答の本文にも適用されるため、モデルのダウンロードに十分な時間を設定します。

## 共通契約と任意の契約

| 契約 | 目的 |
| --- | --- |
| `IModelServer` | サーバー情報、正常性、モデルと観測した機能対応。 |
| `IModelLifecycle` | 明示的なロードとアンロード。任意機能です。 |
| `IModelDownloader` | 進捗を伴う明示的なダウンロード。任意機能です。 |
| `IModelMetricsProvider` | ラベルを保持したメトリック標本。任意機能です。 |

インターフェイスの実装はクライアントに操作があることを示し、`ServingCapabilities` は接続先で確認できた対応状況を示します。`Supported` は全モデルへの権限や成功の保証ではありません。`Unsupported` は観測したモードやエンドポイントで利用できないこと、`Unknown` は認証・接続失敗などで根拠が不足していることを表します。不明を未対応として扱わないでください。

`InstallationState` と `LoadState` は別の状態です。`Unknown` は不在やアンロード済みを意味しません。未報告の `SizeBytes`・`MemoryBytes`・`ContextLength` はゼロではなく `null` です。管理 API が正常でも、個々のモデルが推論可能とは限りません。

## ランタイムごとの違い

| 操作 | Ollama | llama.cpp 単一モデル | llama.cpp Router | vLLM |
| --- | --- | --- | --- | --- |
| 情報、正常性、モデル一覧 | 対応 | 対応 | 対応 | 対応 |
| 明示的なロード / アンロード | 空の生成リクエストで対応 | 非対応 | Router モードの確認後に対応 | このクライアントでは非対応 |
| モデルのダウンロード | 進捗ストリーム付きで対応 | 非対応 | 明示的操作。ダウンロードエンドポイントと SSE イベントが必要 | このクライアントでは非対応 |
| メトリック | 未実装 | 有効化されている場合にサーバーメトリックを取得 | 具象型のモデル別オーバーロード。モデルはロード済みである必要あり | 利用可能な場合にサーバーメトリックを取得 |

この表はクライアントが提供する操作を示しており、あらゆるサーバーバージョン、権限設定、モデルでの対応を保証するものではありません。接続先の機能対応を確認し、操作の失敗を処理してください。

**Ollama:** `/api/tags` は登録モデル、`/api/ps` は現在の実行モデルを返します。リモートモデルはローカルの重みなしで登録でき、ローカル実行情報がなければロード状態は不明のままです。プリロードは空の `/api/generate` リクエストとサーバー既定の keep-alive を使います。埋め込み専用モデルを別 API に自動転送しません。アンロードは `keep_alive: 0` を使い、ファイルは削除しません。メトリックは未実装です。

**llama.cpp:** ライフサイクルやダウンロード命令には、`/props` による明示的なルーターモードの確認が必要です。単一モデルモードはこれらの命令に非対応で、観測されたスリープ状態を保持します。ルーターのダウンロードは `/models/sse` を購読後に `POST /models` を送り、対象モデルの `download_finished` イベントだけを成功とします。SSE の利用可能性だけではダウンロード対応は不明です。サーバー全体のメトリックは単一モデル用です。ルーターでは具象型の `GetMetricsAsync(modelId, token)` を使い、`autoload=false` により確認中のロードを防ぎます。

**vLLM:** 提供エイリアスと任意の `root` フィールドは保持しますが、共通のインストール・ロード状態は不明です。モデルとメトリックは実際の応答で確認し、ライフサイクルとダウンロードは非対応です。既存の `VllmServer` メソッドと DTO は具象クライアントで引き続き利用でき、共通の正常性・モデル・メトリック操作は明示的インターフェイスで提供します。


## 管理操作を明示的に実行する

ダウンロードや常駐状態の変更はネットワーク、ディスク、デバイスメモリを消費します。アプリケーションに必要なときに実行してください。次のコードは Ollama の例の続きで、小さなモデルをダウンロードして一時的にロードし、状態を確認します。Ollama のタグや llama.cpp の量子化タグを含め、正確なサーバーモデル ID を使います。

```csharp
if (server is IModelDownloader downloader &&
    capabilities.ModelDownloading == ServingFeatureSupport.Supported)
{
    var progress = new Progress<ModelDownloadProgress>(p =>
        Console.WriteLine($"{p.ModelId}: {p.Stage} {p.CompletedBytes}/{p.TotalBytes}"));
    await downloader.DownloadModelAsync("qwen2.5:0.5b", progress, cancellation.Token);
}

if (server is IModelLifecycle lifecycle &&
    capabilities.ModelLoading == ServingFeatureSupport.Supported &&
    capabilities.ModelUnloading == ServingFeatureSupport.Supported)
{
    try
    {
        await lifecycle.LoadModelAsync("qwen2.5:0.5b", cancellation.Token);
        var afterLoad = await server.GetModelsAsync(cancellation.Token);
        foreach (var model in afterLoad)
            Console.WriteLine($"{model.Id}: {model.LoadState}");
    }
    finally
    {
        using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await lifecycle.UnloadModelAsync("qwen2.5:0.5b", cleanup.Token);
    }
}
```

この例は専用のテストモデルを使い、終了後にアンロードします。本番アプリケーションではモデルを解放するタイミングを判断し、他のリクエストがまだ使用中のモデルをアンロードしないでください。クリーンアップには独自の制限時間があり、サーバーに接続できない場合は失敗することがあります。

進捗は個々の成果物や段階を表します。バイト数の `null` はゼロでもモデル全体の進捗率でもありません。ロード成功は命令の受理を示し、準備完了や無期限の常駐を保証しません。準備状態が重要な場合は待機時間を制限して `LoadState` を確認します。llama.cpp Router のダウンロード手順とバージョン制約は具象パッケージのガイドを参照してください。明示的に要求された操作は、サーバー設定の確認後であれば対応状況が `Unknown` でも試行できますが、機能確認だけで開始することはありません。

## キャンセルとエラー

確認と命令にはキャンセルトークンを渡します。キャンセルはこのクライアントの HTTP 処理と待機を止めますが、リモート処理の中止、ロールバック、取得済みレイヤーの削除は保証しません。処理時間に合わせて渡す `HttpClient` を設定してください。クライアントはその所有権を取得しません。

モデルやエンジンの比較ではメトリックのラベルを保持します。欠落値はゼロではなく、値には `NaN` や無限大も含まれます。共通エラー型は `ServingException` で、共通管理エラーには生の応答本文や資格情報を含めません。既存の vLLM 固有呼び出しは従来のエラー詳細を維持します。

`GetHealthAsync` はエンドポイントの失敗を正常性の状態に分類しますが、呼び出し元によるキャンセルは引き続き伝播します。他の操作は `ServingException` を、既知の非対応 llama.cpp モードは `NotSupportedException` をスローする場合があります。タイムアウトやリクエスト失敗はリモート操作のロールバックを証明しません。ダウンロードメソッドが正常終了するのは、ランタイムが完了を報告した後だけです。Ollama では最終成功応答とその後の EOF、llama.cpp Router では一致する `download_finished` イベントが必要です。

## 検証済みの範囲

オフラインテストは制御された成功応答、不正な応答、エラー、キャンセルを検証します。別途行った実サーバー検証では、NVIDIA A40 1 基、小規模な公開 Qwen モデル、次のエンジンビルドを使用しました。

| ランタイム | 検証モデル | 検証した管理操作 |
| --- | --- | --- |
| Ollama 0.34.4 | `qwen2.5:0.5b` | 機能確認、新規ダウンロード、ロード・アンロード、機密情報を除いたモデル不在エラー、事前キャンセル、部分的なダウンロード進捗後のキャンセル。 |
| llama.cpp b11146, Router | `Qwen/Qwen2.5-0.5B-Instruct-GGUF:Q4_K_M` | 機能確認、ダウンロードイベント、ロード・アンロード、自動ロードしないモデル別メトリック、エラー、ダウンロードのキャンセル。 |
| llama.cpp b11146、単一モデル | 同じ GGUF モデル | 機能確認、サーバーメトリック、キャンセル、Router のライフサイクル命令の明示的な拒否。 |
| vLLM 0.30.0 | `Qwen/Qwen2.5-0.5B-Instruct` | 機能確認、サーバーメトリック、事前キャンセル。 |

ネイティブ HTTP による短い推論リクエストも、4 構成すべてで生成テキストを返しました。これはエンジンの動作確認であり、AI サービスのチャットアダプター、モデル品質、スループット、すべてのエンジンビルドとの互換性の検証ではありません。上記は検証した構成であり、最小対応バージョンではありません。ダウンロードのキャンセル確認には別の大きなテストモデルを使い、リモート処理のロールバックは確認していません。最初の Ollama ダウンロードは失敗しましたが、再試行とモデル削除後の新規ダウンロードは成功しました。最初の失敗の正確な原因は特定していません。

配備先のエンドポイントは、[明示的に有効化する実サーバー検証ガイド](https://github.com/AJ-comp/Mythosia.AI/blob/main/tests/Mythosia.AI.Serving.Live/README.md)に従って確認してください。このガイドでは、リポジトリに含まれる管理テストランナーと、検証時に追加で行った推論・キャンセルの確認を区別しています。詳細な実行レポートは公開ドキュメントには含めません。

## パッケージ別ガイド

- [Mythosia.AI.Serving.Abstractions](../../src/serving/Mythosia.AI.Serving.Abstractions/README.md) — 共通管理契約と不変のサーバー・モデル・機能スナップショット。
- [Mythosia.AI.Serving.Ollama](../../src/serving/Mythosia.AI.Serving.Ollama/README.md) — Ollama のモデル一覧と正常性、明示的なプリロード・アンロードとストリーミング取得。
- [Mythosia.AI.Serving.LlamaCpp](../../src/serving/Mythosia.AI.Serving.LlamaCpp/README.md) — llama.cpp の確認、ルーター確認付き管理・取得、自動ロードなしのメトリック。
- [Mythosia.AI.Serving.Vllm](../../src/serving/Mythosia.AI.Serving.Vllm/README.md) — vLLM のモデルカード、正常性、バージョン、ラベル付きメトリック。従来の具象 API を維持。
