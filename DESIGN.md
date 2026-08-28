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

外部依存の境界は次のとおり。

- Velopack の `UpdateManager` が更新情報、ダウンロード、適用と再起動を担う。
- CommunityToolkit.Mvvm が状態通知を生成し、Avalonia の compiled binding が XAML へ反映する。
- SuperLightLogger は状態遷移と失敗を `Microsoft.Extensions.Logging` 抽象へ流す。ホストは `ErrorOccurred` と `UpdateDialogResult` でも失敗を観測できる。

## データフロー

### 手動チェック

1. ホストが `UpdateDialogWindow.ShowAsync(..., manualCheck: true)` を呼ぶ。
2. Window と ViewModel を生成し、Window の表示処理と並行して `CheckAsync` を開始する。
3. ViewModel が `Checking` から `Available`、`UpToDate`、`Failed` のいずれかへ遷移する。
4. `UpdateDialogView` の compiled binding が対応パネルを表示する。
5. ユーザー操作と Window の閉じ処理から `FinalOutcome` を確定し、`UpdateDialogResult` を返す。

### 自動チェック

1. ホストが `ShowAsync(..., manualCheck: false)` を呼ぶ。
2. Window を生成する前に `CheckAsync` を完了させる。
3. 最新版の抑止、無視タグ一致、失敗では Window を表示せず、それぞれ `UpToDate`、`Ignored`、`Failed` を返す。
4. 表示が必要な更新だけ Window を生成して `Available` 状態を提示する。

### ダウンロードと適用

1. `Available` 状態でダウンロード操作を受けると `Downloading` へ遷移し、バックグラウンドタスクを開始する。
2. Velopack の進捗 callback を `Dispatcher.UIThread.Post` で UI スレッドへ戻す。
3. 成功時は `ApplyUpdatesAndRestart` を呼ぶ。キャンセル時は `Available` へ戻り、失敗時は `Failed` とエラー通知を設定する。
4. ダウンロード用 `CancellationTokenSource` はタスク完了後、UI スレッド上の `finally` で参照を外して破棄する。

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
- 更新確認の再入防止は `_checkInFlight` で行う。確認キャンセル時は `FinalOutcome` を `Cancelled` にし、Window が閉じるまで `Checking` 表示を維持する。
- バックグラウンド処理から `DownloadProgress`、`State`、`FinalOutcome`、ダウンロード CTS の参照を更新するときは UI スレッドへ戻す。
- `CancelDownload` と `Dispose` は走行中 CTS のキャンセルだけを行い、破棄はダウンロードタスク側の `finally` に任せる。
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
| ダウンロードをバックグラウンド実行 | UI を応答可能なまま維持する | UI スレッドへの dispatch と CTS の所有権管理が必要になる |
| 永続化をイベント境界に限定 | ライブラリをホストの設定基盤や OS ストレージへ依存させない | 無視バージョンの保存はホストが実装する必要がある |
| PackageId と namespace を分離 | 将来の UI 派生パッケージ間で利用コードの namespace を共有できる | パッケージ名から namespace を推測できないため README で明示が必要になる |
| System / Custom chrome を選択可能にする | OS 標準操作と独自外観のどちらも利用できる | Window 設定と背景フォールバックの分岐が増える |

## ビルド・検証・配布の境界

`VelopackUpdateDialog.Avalonia.slnx` は配布ライブラリと DemoApp から成る。自動テストプロジェクトはなく、Release build と pack に加え、DemoApp で各状態を目視確認する。

パッケージメタデータと製品バージョンは `Directory.Build.props` に集約され、pack 出力は `artifacts/` に置かれる。`release/**` ブランチへの push または手動実行で GitHub Actions が build、pack、`publish.ps1` による NuGet 公開を行う。公開スクリプトは `artifacts/` に複数バージョンが混在した場合に警告する。
