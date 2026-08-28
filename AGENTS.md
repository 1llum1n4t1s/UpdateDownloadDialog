# AGENTS.md

This file provides guidance to Codex when working in this repository.

## このリポジトリの概要

Avalonia 12 上で動く **Velopack 自動更新ダイアログ** の再利用可能 NuGet パッケージ。利用者向けドキュメント（インストール・使い方・オプション一覧）は [README.md](README.md)、現在の構造・責務・不変条件は [DESIGN.md](DESIGN.md) を正とする。本ファイルは開発者・Codex などのコーディングエージェント向けに、必須コマンドと変更時の規約を記す。

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

- **.NET SDK の選択基準は [global.json](global.json) の `10.0.201` で、`latestFeature` ロールフォワードを許可**する。TFM は `net10.0`。
- **テストプロジェクトは存在しない**。動作確認は DemoApp の目視（`OnShowAvailable` 等のボタンで各 `UpdateState` を再現）で行う。
- `TreatWarningsAsErrors=true` + `EnforceCodeStyleInBuild=true`（[Directory.Build.props](Directory.Build.props)）。**警告・コードスタイル違反はビルドエラーになる**。[.editorconfig](.editorconfig) のスタイル（file-scoped namespace / using は namespace 外 / `var` は型が自明なときのみ / private フィールドは `_camelCase` / 中括弧必須）を守らないと CI が落ちる。

## 最重要の非自明ポイント

### PackageId と namespace がずれている
- NuGet **PackageId** = `VelopackUpdateDialog.Avalonia`
- C# **RootNamespace / AssemblyName / 全 namespace** = `VelopackUpdateDialog`（`.Avalonia` 接尾辞なし）

将来の WPF/WinForms 派生との namespace 共有を見越した意図的な設計。新規ファイルの namespace は必ず `VelopackUpdateDialog` にする。XAML 側は `xmlns:upd="using:VelopackUpdateDialog"`。

### バージョン番号は触らない
`<Version>` は [Directory.Build.props](Directory.Build.props) で一元管理。コード修正のついでに勝手に上げない。バージョン更新が必要なら `/vava` ワークフローを提案する（グローバル AGENTS.md の方針）。

## 設計変更時の規約

実装着手前に [DESIGN.md](DESIGN.md) の該当節を読み、責務の境界と不変条件を維持する。公開レイヤー、状態遷移、自動・手動チェック、スレッド・キャンセル所有権、Window 外観、ホストとのイベント境界を変更した場合は、実装と同じ変更内で DESIGN.md を現在形へ更新する。

- `UpdateState` または派生表示プロパティを変更するときは、`OnStateChanged` の通知と `UpdateDialogView.axaml` の表示パネルを同時に照合する。
- 非同期更新処理を変更するときは、UI スレッドへの dispatch とダウンロード CTS の所有・破棄位置を照合する。
- 公開 API や利用方法を変更したときは、利用者向けの [README.md](README.md) も同時に更新する。

## CI / リリース

[.github/workflows/publish.yml](.github/workflows/publish.yml): `release/**` ブランチへの push（または手動 dispatch）で build → pack → `publish.ps1` で NuGet 公開。`release/x.y.z` ブランチは `/vava` が作成する。GitHub Actions は SHA pin、権限は `contents: read` 最小化。`publish.ps1` は artifacts/ に複数バージョンの nupkg が混在すると警告して誤公開を防ぐ。

## ディレクトリ早見

| パス | 役割 |
|---|---|
| `src/VelopackUpdateDialog.Avalonia/` | ライブラリ本体（`Models/` `ViewModels/` `Controls/` `Windows/` `Themes/` `Util/`） |
| `samples/DemoApp/` | 各状態を再現する目視確認用 Avalonia アプリ |
| `DESIGN.md` | 現在の構造、責務、データフロー、不変条件、設計判断の正本 |
| `Directory.Build.props` | バージョン・パッケージメタデータ・共通ビルド設定の一元管理 |
| `artifacts/` | `dotnet pack` 出力先 |
| `icon/` | パッケージアイコン（存在時のみ条件付きで同梱） |
