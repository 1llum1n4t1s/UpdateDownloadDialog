# DESIGN.md

この文書は、現在の実装から確認できる `VelopackUpdateDialog.Avalonia` の設計正本である。利用者向けの導入・使い方・設定は [README.md](README.md)、開発時のコマンドと変更規約は [AGENTS.md](AGENTS.md) を参照する。

## 目的と範囲

このリポジトリは、Avalonia 12 / `net10.0` のホストアプリへ Velopack の更新確認 UI と更新実行フローを組み込む NuGet パッケージを提供する。ホストは完成形の Window、任意 Window に埋め込む UserControl、状態機械だけを持つ ViewModel のいずれかを選べる。

パッケージが担当するのは、更新確認、状態表示、ダウンロード、適用と再起動の指示、最終結果の通知である。更新元と `UpdateManager` の構成、無視バージョンの永続化、エラーの外部送信、Velopack によるアプリのパッケージ化と署名はホスト側の責務である。

## 主要コンポーネントと境界

| コンポーネント | 責務 | 境界 |
|---|---|---|
| `UpdateDialogWindow` | `ShowAsync` の入口、自動・手動チェック時の表示判断、Window 設定、閉じる処理、最終結果の返却 | 更新ロジックは `UpdateDialogViewModel`、本文 UI は `UpdateDialogView` に委譲する |
| `UpdateDialogView` | 状態別 XAML、ダウンロード・無視・閉じる操作、`CloseRequested` の通知 | Window を直接所有せず、`DataContext` の ViewModel とイベントだけを使う |
| `UpdateDialogViewModel` | `UpdateState` 状態機械、Velopack 呼び出し、進捗、エラー、`UpdateOutcome` の確定 | Window の生成・表示とホスト固有の永続化を行わない |
| `UpdateDialogOptions` / `IUpdateDialogStrings` | 配色、文字列、Window 外観、無視・エラー通知などの公開カスタマイズ契約 | 永続化先やログ送信先は持たず、イベントでホストへ返す |
| `Models/` | 状態、結果、Window モード、内部既定値の型定義 | UI と Velopack の処理を持たない |
| `Themes/` / `AcrylicFallbackHelper` | 共通スタイルと Custom chrome の背景切替 | System chrome ではソリッド背景を使い、RDP・透明効果無効・acrylic 非許可時も不透明背景へフォールバックする |
| `samples/DemoApp` | 各状態、テーマ、Window モード、文字列差し替えの目視確認 | 配布パッケージには含まれない |
| `tests/VelopackUpdateDialog.Avalonia.RegressionTests` | ダウンロード完了・適用開始・Window close の順序と終了後 callback 抑止の回帰確認 | 実際の更新適用や UI 表示は行わず、internal のテスト境界から決定的に競合を再現する |

外部依存の境界は次のとおり。

- Velopack の `UpdateManager` が更新情報、ダウンロード、適用と再起動を担う。
- CommunityToolkit.Mvvm が状態通知を生成し、Avalonia の compiled binding が XAML へ反映する。
- SuperLightLogger は状態遷移と失敗を `Microsoft.Extensions.Logging` 抽象へ流す。ホストは `ErrorOccurred` と `UpdateDialogResult` でも失敗を観測できる。

## データフロー

### 手動チェック

1. ホストが `UpdateDialogWindow.ShowAsync(..., manualCheck: true)` を呼ぶ。
2. Window と ViewModel を生成し、Window の表示処理と並行して更新確認を開始する。更新確認タスクは追跡し、Window の寿命トークンと呼び出し元トークンを結合する。
3. ViewModel が `Checking` から `Available`、`UpToDate`、`Failed` のいずれかへ遷移する。
4. `UpdateDialogView` の compiled binding が対応パネルを表示する。
5. ユーザー操作と Window の閉じ処理から `FinalOutcome` を確定し、`UpdateDialogResult` を返す。

### 自動チェック

1. ホストが `ShowAsync(..., manualCheck: false)` を呼ぶ。
2. Window を生成する前に `CheckAsync` を完了させる。呼び出し元トークンのキャンセルは `WaitAsync` で待機へ即時反映する。
3. 最新版の抑止、無視タグ一致、失敗では Window を表示せず、それぞれ `UpToDate`、`Ignored`、`Failed` を返す。
4. 表示が必要な更新だけ Window を生成して `Available` 状態を提示する。

### ダウンロードと適用

1. `Available` 状態でダウンロード操作を受けると `Downloading` へ遷移し、バックグラウンドタスクを開始する。
2. Velopack の進捗 callback を `Dispatcher.UIThread.Post` で UI スレッドへ戻す。
3. ダウンロード完了時は、Window close と同じ同期 gate でキャンセルを再確認して適用開始を確定し、勝った側だけを進める。適用開始後は Window close を拒否する。
4. close または破棄が先なら以後の進捗・状態・結果・エラー callback を抑止し、`ShowAsync` はダウンロードタスクの完了を待ってから結果を返す。
5. ダウンロード用 `CancellationTokenSource` はタスク完了後、同期 gate 内の `finally` で参照を外して破棄する。

## 状態と結果

状態遷移の中核は次のとおり。

```text
Idle -> Checking
Checking -> Available | UpToDate | Failed
Available -> Downloading
Downloading -> 適用・再起動 | Available（キャンセル） | Failed
```

`Idle` は専用パネルを持たず、`IsPreparing` により `Checking` と同じ表示へフォールバックする。終了結果は状態とは別の `UpdateOutcome` で表し、更新、最新版、無視、キャンセル、失敗、単純なクローズをホストが区別できる。

## 重要な不変条件

- NuGet PackageId は `VelopackUpdateDialog.Avalonia`、AssemblyName・RootNamespace・公開 namespace は `VelopackUpdateDialog` とする。
- Window → View → ViewModel の依存方向を保ち、更新ロジックは ViewModel に集約する。
- すべての `UpdateState` は表示可能なパネルへ対応させる。派生表示プロパティを増やした場合は `OnStateChanged` から変更通知する。
- 更新確認の再入防止は `_checkInFlight` で行う。呼び出し元トークンによる確認キャンセル時は `FinalOutcome` を `Cancelled` にし、Window が閉じるまで `Checking` 表示を維持する。
- バックグラウンド処理から `DownloadProgress`、`State`、`FinalOutcome` を更新するときは UI スレッドへ戻す。ダウンロード CTS とタスクの参照は同期 gate 内だけで更新する。
- `CancelDownload` と `Dispose` は走行中 CTS のキャンセルだけを行い、破棄はダウンロードタスク側の `finally` に任せる。
- ダウンロード完了後の適用開始と Window close は同じ同期 gate で順序を確定する。close が先なら適用せず、適用開始が先なら close を拒否する。
- Window close または ViewModel の破棄後は、ダウンロード由来の進捗、状態、結果、`ErrorOccurred` を更新しない。`ShowAsync` は所有するダウンロードタスクを完了まで待つ。
- 更新確認の呼び出し元キャンセルは `Cancelled`、確認中の Window をユーザーが閉じた場合は `Closed` とする。Window を閉じる際は寿命トークンで待機を終了し、追跡タスクの完了後に結果を返すため、閉じた後に状態や `ErrorOccurred` を更新しない。
- Velopack 1.2.0 の `CheckForUpdatesAsync` は `CancellationToken` を受け取らないため、キャンセルできるのは待機だけであり、内部通信は完了まで継続し得る。切り離されたタスクは例外が未観測にならないよう完了まで観測し、結果を状態へ反映しない。
- 更新確認またはダウンロードの `OperationCanceledException` は、対応する呼び出し元トークンが要求済みの場合だけキャンセルとして扱う。HTTP タイムアウトなどトークン由来でない失敗は `Failed` とする。
- 自動チェックの失敗は Window を表示せず、`ErrorOccurred` と `UpdateDialogResult.Error` から観測可能にする。`SetFailed` による同一失敗のイベント通知は 1 回に保つ。
- `UpdateManager.IsInstalled == false` の開発実行は最新版として扱う。
- URL から `UpdateManager` を作る便利コンストラクタは絶対 HTTPS URL かつ `github.com` に限定する。その他の更新元は、ホストが構成した `UpdateManager` を注入する。
- Custom chrome は acrylic と不透明背景の両方を持ち、実行環境に応じて一方だけを表示する。System chrome は OS フレームとソリッド背景を使う。

## 採用済みの設計判断

| 判断 | 理由 | トレードオフ |
|---|---|---|
| Window / View / ViewModel の 3 レイヤーを公開 | ホストの UI 所有範囲に合わせて再利用単位を選べる | 公開 API と保守対象が増える |
| 状態機械を ViewModel に集約 | Window と View を薄くし、同じロジックを独自 UI からも利用できる | 状態追加時は派生 binding と XAML の同時更新が必要になる |
| 自動チェックを先行実行 | 更新が不要・無視・失敗のときに起動時ポップアップを出さない | fire-and-forget ではホストがイベントまたはログを設定しないと失敗を見落としやすい |
| ダウンロードを追跡可能なバックグラウンドタスクとして実行 | UI を応答可能なまま維持しつつ、Window 終了時に完了を join して遅延 callback を残さない | UI スレッドへの dispatch、同期 gate、CTS とタスクの所有権管理が必要になる |
| 永続化をイベント境界に限定 | ライブラリをホストの設定基盤や OS ストレージへ依存させない | 無視バージョンの保存はホストが実装する必要がある |
| PackageId と namespace を分離 | 将来の UI 派生パッケージ間で利用コードの namespace を共有できる | パッケージ名から namespace を推測できないため README で明示が必要になる |
| System / Custom chrome を選択可能にする | OS 標準操作と独自外観のどちらも利用できる | Window 設定と背景フォールバックの分岐が増える |

## ビルド・検証・配布の境界

`VelopackUpdateDialog.Avalonia.slnx` は配布ライブラリ、DemoApp、実行型回帰テストから成る。回帰テストでダウンロード・適用・終了の競合を確認し、Release build と pack に加え、DemoApp で各状態を目視確認する。

パッケージメタデータと製品バージョンは `Directory.Build.props` に集約され、pack 出力は `artifacts/` に置かれる。`release/**` ブランチへの push または手動実行で GitHub Actions が build、回帰テスト、pack を行い、NuGet.org Trusted Publishing で取得した短期資格情報だけを使って公開する。workflow は `artifacts/` の対象パッケージが1件でない場合、どのパッケージも送信せず異常終了する。
