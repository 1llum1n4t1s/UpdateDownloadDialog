# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## このリポジトリの概要

Avalonia 12 上で動く **Velopack 自動更新ダイアログ** の再利用可能 NuGet パッケージ。利用者向けドキュメント（インストール・使い方・オプション一覧）は [README.md](README.md) を正とする。本ファイルは開発者（Claude）向けに、リポジトリ構造とビルド・設計上の非自明な約束事を記す。

## ビルド / 実行コマンド

```powershell
# ソリューション全体ビルド（ライブラリ + DemoApp）
dotnet build VelopackUpdateDialog.Avalonia.slnx -c Release

# DemoApp を起動して各状態のダイアログを目視確認（テストプロジェクトは無いのでこれが動作確認手段）
dotnet run --project samples/DemoApp/DemoApp.csproj

# NuGet パッケージ生成（artifacts/ に .nupkg + .snupkg が出力される）
dotnet pack src/VelopackUpdateDialog.Avalonia/VelopackUpdateDialog.Avalonia.csproj -c Release

# NuGet へ公開（NUGET_API_KEY 環境変数が必須。artifacts/ 内の全 nupkg を push）
./publish.ps1
```

- **.NET SDK は `10.0.201`（`latestFeature` ロールフォワード）に [global.json](global.json) で固定**。TFM は `net10.0`。
- **テストプロジェクトは存在しない**。動作確認は DemoApp の目視（`OnShowAvailable` 等のボタンで各 `UpdateState` を再現）で行う。
- `TreatWarningsAsErrors=true` + `EnforceCodeStyleInBuild=true`（[Directory.Build.props](Directory.Build.props)）。**警告・コードスタイル違反はビルドエラーになる**。[.editorconfig](.editorconfig) のスタイル（file-scoped namespace / using は namespace 外 / `var` は型が自明なときのみ / private フィールドは `_camelCase` / 中括弧必須）を守らないと CI が落ちる。

## 最重要の非自明ポイント

### PackageId と namespace がずれている
- NuGet **PackageId** = `VelopackUpdateDialog.Avalonia`
- C# **RootNamespace / AssemblyName / 全 namespace** = `VelopackUpdateDialog`（`.Avalonia` 接尾辞なし）

将来の WPF/WinForms 派生との namespace 共有を見越した意図的な設計。新規ファイルの namespace は必ず `VelopackUpdateDialog` にする。XAML 側は `xmlns:upd="using:VelopackUpdateDialog"`。

### バージョン番号は触らない
`<Version>` は [Directory.Build.props](Directory.Build.props) で一元管理。コード修正のついでに勝手に上げない。バージョン更新が必要なら `/vava` ワークフローを提案する（グローバル CLAUDE.md の方針）。

## アーキテクチャ（big picture）

ライブラリは `src/VelopackUpdateDialog.Avalonia/` の単一プロジェクト。**3 レイヤー提供**で、ホストアプリの設計に合わせて段階的に組み込める構造になっている:

1. **`UpdateDialogWindow`**（[Windows/](src/VelopackUpdateDialog.Avalonia/Windows/)）— 完成形ウィンドウ。静的 `ShowAsync(owner, manager, options, manualCheck)` 1 行で呼べる。チェック → UI 表示 → outcome 確定までを内包。
2. **`UpdateDialogView : UserControl`**（[Controls/](src/VelopackUpdateDialog.Avalonia/Controls/)）— ダイアログ中身だけ。任意の Window に貼り、`DataContext` に ViewModel をセット。`CloseRequested` イベントでホストへ閉じ要求を伝える。
3. **`UpdateDialogViewModel`**（[ViewModels/](src/VelopackUpdateDialog.Avalonia/ViewModels/)）— 状態機械 + Velopack 呼び出しの本体。UI 完全自前のホストはこれだけ再利用する。

上 2 つは下のレイヤーを内部で利用する（Window → View → ViewModel）。**ロジックは全て ViewModel に集約**されており、Window/View は薄いアダプタ。

### 状態機械（ViewModel の中核）
- `UpdateState`（[Models/UpdateState.cs](src/VelopackUpdateDialog.Avalonia/Models/UpdateState.cs)）: `Idle → Checking → {Available | UpToDate | Failed}`、`Available → Downloading → (再起動 or 戻る)`。`[ObservableProperty]` の `State` 変更時に `OnStateChanged` が派生 bool（`IsChecking` 等、XAML バインド用）を一括 raise する。
- `UpdateOutcome`（[Models/UpdateDialogResult.cs](src/VelopackUpdateDialog.Avalonia/Models/UpdateDialogResult.cs)）: ダイアログ終了時の最終分岐（`Updated / UpToDate / Ignored / Cancelled / Failed / Closed`）。`FinalOutcome` は `OnClosing()` で `State` から導出される。`ShowAsync` の戻り値 `UpdateDialogResult` に包まれる。

### `manualCheck` による表示分岐（設計の肝）
`ShowAsync` の `manualCheck` で挙動が大きく変わる。詳細表は README の「`manualCheck` の挙動差」を参照:
- **手動チェック (`true`)**: 即ウィンドウ表示 → バックグラウンドでチェック → 状態に応じて遷移。最新版でも結果を見せる。
- **自動チェック (`false`)**: ウィンドウを開かずに先にチェックし、`UpToDate`（`SuppressUpToDateOnAutoCheck` 既定 true）/ `IgnoredTagName` 一致 / `Failed` のときは **UI を一切出さず** outcome だけ返す。起動時サイレントチェック向け。

### スレッドモデル（壊しやすい箇所）
ダウンロードは `Task.Run` のバックグラウンドで走り、進捗・状態・`FinalOutcome` の更新は必ず `Dispatcher.UIThread.Post` 経由で UI スレッドに戻す（`UpdateDialogViewModel.DownloadAndApplyAsync`）。非 UI スレッドからの直接代入は race を生むため避ける。`OperationCanceledException` のキャンセル後は `State` を `Idle` に戻す（戻さないと次回 `CheckAsync` 冒頭のガードで永久 return する）。

### カスタマイズ拡張点
[Models/UpdateDialogOptions.cs](src/VelopackUpdateDialog.Avalonia/Models/UpdateDialogOptions.cs) が全オプションの集約。文字列差し替えは `IUpdateDialogStrings`（既定 `DefaultStrings.Instance`）、配色は `AccentBrush`、永続化フックは `VersionIgnored` / `ErrorOccurred` イベント。ウィンドウ外観は `WindowChromeMode`（System=OS フレーム / Custom=独自タイトルバー + アクリル）と `WindowResizeMode`。`ApplyOptions`（Window 側）がこれらを実 Window プロパティへ反映する。

### Velopack 連携の前提
- `UpdateManager.IsInstalled == false`（`vpk pack` を経ない開発実行）なら `CheckAsync` は常に `UpToDate` を返す。
- 便利コンストラクタ `UpdateDialogViewModel(string githubRepoUrl, ...)` は **URL を `https://` + `github.com` ホストに限定**（セキュリティ）。GitHub Enterprise 等は `UpdateManager` を渡すコンストラクタを使う。
- ログは [SuperLightLogger](https://www.nuget.org/packages/SuperLightLogger/)（`Microsoft.Extensions.Logging` 抽象）。失敗時は `ErrorOccurred` イベントが 1 回発火する。

## CI / リリース

[.github/workflows/publish.yml](.github/workflows/publish.yml): `release/**` ブランチへの push（または手動 dispatch）で build → pack → `publish.ps1` で NuGet 公開。`release/x.y.z` ブランチは `/vava` が作成する。GitHub Actions は SHA pin、権限は `contents: read` 最小化。`publish.ps1` は artifacts/ に複数バージョンの nupkg が混在すると警告して誤公開を防ぐ。

## ディレクトリ早見

| パス | 役割 |
|---|---|
| `src/VelopackUpdateDialog.Avalonia/` | ライブラリ本体（`Models/` `ViewModels/` `Controls/` `Windows/` `Themes/`） |
| `samples/DemoApp/` | 各状態を再現する目視確認用 Avalonia アプリ |
| `Directory.Build.props` | バージョン・パッケージメタデータ・共通ビルド設定の一元管理 |
| `artifacts/` | `dotnet pack` 出力先 |
| `icon/` | パッケージアイコン（存在時のみ条件付きで同梱） |
