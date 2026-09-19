# VelopackUpdateDialog.Avalonia

Avalonia 12 で動く **Velopack 自動更新ダイアログ** の再利用可能パッケージ。

`Window` / `UserControl` / `ViewModel` の 3 段提供で、ホストアプリの設計に合わせて柔軟に組み込める。

## インストール

```bash
dotnet add package VelopackUpdateDialog.Avalonia
```

依存: `Avalonia 12.1.2+`, `CommunityToolkit.Mvvm 8.4.2+`, `Velopack 1.2.0+`, TFM `net10.0`。

> 📦 **PackageId と namespace について**: NuGet パッケージ名は `VelopackUpdateDialog.Avalonia` ですが、C# namespace は `VelopackUpdateDialog`（`.Avalonia` 接尾辞なし）です。将来の WPF/WinForms 派生パッケージとの namespace 共有を見越した設計です。

## 最短の使い方

```csharp
using Velopack;
using Velopack.Sources;
using VelopackUpdateDialog;

var mgr = new UpdateManager(new GithubSource("https://github.com/owner/repo", string.Empty, false));
await UpdateDialogWindow.ShowAsync(parentWindow, mgr, manualCheck: true);
```

起動時にサイレントに自動確認する場合は `manualCheck: false`（既定値）を使う。最新版、無視対象、確認失敗では Window を表示せず結果だけを返す。

## オプション指定

```csharp
using Avalonia;
using Avalonia.Media;
using Microsoft.Extensions.Logging;

// 例: MyJapaneseStrings は IUpdateDialogStrings を実装するユーザー定義クラス。
//     最小実装は samples/DemoApp/MainWindow.axaml.cs の JapaneseStrings を参照。

var options = new UpdateDialogOptions
{
    // 表示文字列を差し替え（日本語 etc.）
    Strings = new MyJapaneseStrings(),

    // 大昔の SelfUpdate 風: ウィンドウ固定サイズ（デフォルト）
    ResizeMode = WindowResizeMode.Fixed,

    // 可変ウィンドウにする場合
    // ResizeMode = WindowResizeMode.Resizable,
    // InitialSize = new Size(600, 240),
    // MinSize = new Size(540, 160), // ダウンロード進捗 UI を収める推奨下限
    // MaxSize = new Size(900, 600), // null なら上限なし

    ChromeMode = WindowChromeMode.Custom,  // OS フレームを使うなら System
    AccentBrush = Brushes.DodgerBlue,
    AllowIgnoreVersion = true,
    AllowCloseDuringDownload = true,
    SuppressUpToDateOnAutoCheck = true,

    // 自動チェック時はこのタグの更新を無視 (ホスト側で保存した IgnoreUpdateTag を渡す)
    IgnoredTagName = Preferences.IgnoreUpdateTag,
};

// 「このバージョンを無視」を押された時の永続化処理
options.VersionIgnored += tag => Preferences.IgnoreUpdateTag = tag;

// エラー発生時にログ以外の個別処理も必要な場合
options.ErrorOccurred += ex => HandleUpdateFailure(ex);

// 状態遷移、警告、失敗をホスト側の任意のロガーへ転送
options.LogEmitted += entry =>
{
    var level = entry.Level switch
    {
        UpdateDialogLogLevel.Information => LogLevel.Information,
        UpdateDialogLogLevel.Warning => LogLevel.Warning,
        UpdateDialogLogLevel.Error => LogLevel.Error,
        _ => LogLevel.None,
    };
    logger.Log(level, entry.Exception, "{Message}", entry.Message);
};

var result = await UpdateDialogWindow.ShowAsync(parentWindow, mgr, options, manualCheck: true);

switch (result.Outcome)
{
    case UpdateOutcome.Updated:    /* 再起動指示済み */ break;
    case UpdateOutcome.UpToDate:   /* 最新版 */ break;
    case UpdateOutcome.Ignored:    /* ユーザーが無視を選択 */ break;
    case UpdateOutcome.Cancelled:  /* ダウンロード中断 */ break;
    case UpdateOutcome.Failed:     /* result.Error 参照 */ break;
    case UpdateOutcome.Closed:     /* 単純に閉じられた */ break;
}
```

## レイヤー別の提供

### 1. `UpdateDialogWindow` — そのまま `ShowAsync`

完成形ウィンドウ。タイトルバー込み。

### 2. `UpdateDialogView : UserControl` — 任意 Window に貼り付け

```xml
<Window xmlns:upd="using:VelopackUpdateDialog">
    <upd:UpdateDialogView x:Name="UpdateView"/>
</Window>
```

```csharp
var vm = new UpdateDialogViewModel(updateManager, options);
UpdateView.DataContext = vm;
UpdateView.CloseRequested += (_, _) => Close();

// ダウンロード完了と適用開始の競合を ViewModel と同じ gate で確定。
Closing += (_, e) => e.Cancel = !vm.TryOnClosing();

// close が先に確定した場合はダウンロード・適用処理の終了を待ってから破棄。
Closed += async (_, _) =>
{
    await vm.WaitForDownloadCompletionAsync();
    vm.Dispose();
};
await vm.CheckAsync();
```

`UpdateDialogView` は Window を所有せず、無視・閉じる操作で `CloseRequested` を通知する。任意 Window へ埋め込む場合は、ホストがこの通知、Window の閉じる方針、ダウンロード・適用処理の完了待ち、ViewModel の `Dispose` を所有する。`TryOnClosing()` が false を返した場合は、適用開始済みまたは `AllowCloseDuringDownload=false` のダウンロード中なので close を取り消す。

### 3. `UpdateDialogViewModel` — 完全自前 UI

状態機械と Velopack 呼び出しロジックだけを再利用し、UI は完全自前で組む場合。

```csharp
using var vm = new UpdateDialogViewModel(updateManager);
// GitHub.com なら new UpdateDialogViewModel("https://github.com/owner/repo") も利用可。
vm.PropertyChanged += (_, e) =>
{
    if (e.PropertyName == nameof(vm.State))
    {
        // 自前の UI を更新
    }
};
await vm.CheckAsync();
```

`manualCheck` による Window 表示の切り替えは `UpdateDialogWindow.ShowAsync` が担当する。ViewModel を直接使う場合は、ホストが表示と寿命を管理する。

## カスタマイズ拡張点

| 拡張点 | インターフェース | 差し替え方法 |
|---|---|---|
| 文字列 (タイトル / ボタン / メッセージ) | `IUpdateDialogStrings` | `UpdateDialogOptions.Strings` |
| 配色 (アクセント) | `IBrush` | `UpdateDialogOptions.AccentBrush` |
| テーマ全体 (Light/Dark) | `ThemeVariant` | ホストアプリ側 `Application.RequestedThemeVariant` |
| 無視永続化 | `event Action<string>` | `UpdateDialogOptions.VersionIgnored` |
| エラー通知 | `event Action<Exception>` | `UpdateDialogOptions.ErrorOccurred` |
| ログ通知 | `event Action<UpdateDialogLogEntry>` | `UpdateDialogOptions.LogEmitted` |

## 動作

| 状態 | 表示 |
|---|---|
| `Idle` | 初期状態。空表示を避けるため `Checking` と同じスピナーを表示 |
| `Checking` | 不定進捗バー + "Checking for updates..." |
| `Available` | バージョン バッジ + 「ダウンロードしてインストール」/「このバージョンを無視」 |
| `Downloading` | 進捗バー (0-100) |
| `UpToDate` | 「最新版です」+ Close |
| `Failed` | エラーメッセージ + Close（`ErrorOccurred` イベントで `Exception` がホストへ 1 回通知される） |

ダウンロード完了後、Velopack の `ApplyUpdatesAndRestart` を自動呼び出し。

### `manualCheck` の挙動差

| | 手動チェック (`manualCheck: true`) | 自動チェック (`manualCheck: false`) |
|---|---|---|
| Window 表示 | 即表示（`Checking` 状態でスピナー） | チェック完了まで表示しない |
| UpToDate | 「最新版です」を表示 | `SuppressUpToDateOnAutoCheck` (既定 true) なら何も表示せず `UpdateOutcome.UpToDate` を返す |
| Available | バッジ + ボタン表示 | `IgnoredTagName` と一致すれば表示せず `UpdateOutcome.Ignored` を返す。それ以外は表示 |
| Failed | エラー詳細を表示 | 表示せず `UpdateOutcome.Failed` を返す（`ErrorOccurred` 経由でホスト通知） |

これにより自動チェックは「無関係な時は一切ポップアップを出さない」挙動になり、起動時のサイレントチェックに適する。

## 事前条件

- ホストアプリは **Velopack でパッケージ化** (`vpk pack`) されている必要がある。`UpdateManager.IsInstalled` が `false` の場合 (= `vpk pack` を経ていない開発実行など)、通常の更新確認は最新版として扱う（`ShowAsync` は `UpdateOutcome.UpToDate`、ViewModel 直接利用では `State = UpdateState.UpToDate`）
- ホストアプリの `Program.Main` 冒頭で `VelopackApp.Build().Run()` を呼ぶこと (Velopack 公式の事前要件)
- TFM `net10.0` 以上

## ロギング

本ライブラリ自身はコンソール、ファイル、デバッガーなどへログを出力せず、ロガー実装にも依存しない。状態遷移、警告、失敗は `UpdateDialogOptions.LogEmitted` から `UpdateDialogLogEntry` としてホストへ通知する。ホストは必要な項目だけを任意のロガーやテレメトリへ転送できる。

```csharp
using Microsoft.Extensions.Logging;

options.LogEmitted += entry =>
{
    var level = entry.Level switch
    {
        UpdateDialogLogLevel.Information => LogLevel.Information,
        UpdateDialogLogLevel.Warning => LogLevel.Warning,
        UpdateDialogLogLevel.Error => LogLevel.Error,
        _ => LogLevel.None,
    };

    logger.Log(level, entry.Exception, "{Message}", entry.Message);
};
```

`LogEmitted` を購読しなければログ項目は破棄される。通知は状態変更などを行ったスレッドで同期実行されるため、時間のかかる転送はホスト側で非同期化する。購読ハンドラが例外を投げても更新フローと他の購読者には影響しない。失敗時は後方互換の `Options.ErrorOccurred` からも同じ `Exception` が 1 回通知される。

> ⚠️ **起動時の自動チェックを fire-and-forget (`_ = ShowAsync(...)`) で呼ぶ場合**、戻り値の `UpdateDialogResult` を見ないため、失敗を観測するには `Options.ErrorOccurred` または `Options.LogEmitted` を購読する。プロキシ遮断・TLS 失敗などを静かに見逃さないよう、自動チェック時は最低でもどちらかを購読することを推奨する。

## Troubleshooting

| 症状 | 確認ポイント |
|---|---|
| `ShowAsync` を呼んでも何も起きない | `manualCheck: true` で呼んでいるか / `SuppressUpToDateOnAutoCheck` が既定 (true) でないか / `IgnoredTagName` が現バージョンと一致していないか |
| 「最新版です」と出るが新版あるはず | ホストアプリが `vpk pack` 経由でインストール済みか (`UpdateManager.IsInstalled == true`) / `GithubSource` の URL / `prerelease` フラグ |
| ダウンロードが 0% から進まない | `Options.LogEmitted` または `Options.ErrorOccurred` を購読して原因取得 / プロキシ・TLS 1.2+ / `accessToken` の有効性 |
| 「このバージョンを無視」しても翌起動で再表示 | ホスト側で保存している `IgnoredTagName` を `Options.IgnoredTagName` に渡しているか (`v` プレフィックスと前後空白は正規化される) |
| `ArgumentException: githubRepoUrl host must be github.com` | 便利コンストラクタは `https://github.com/...` のみ受け付ける。GitHub Enterprise や独自ホストは `UpdateDialogViewModel(UpdateManager, ...)` を使う |

## Security Considerations

- **コード署名**: Velopack の `vpk pack --signParams` (Windows: SignTool) / `--signEntitlements` (macOS: codesign) で配布パッケージに必ず署名を施すこと。未署名運用は、GitHub Release への push 権限を握った攻撃者が任意コード実行を仕込める経路になる
- **GitHub Repository 設定**: Release への push 権限は branch protection + required reviews で絞ること
- **アクセストークン**: 平文設定ファイルへの保存は避け、Windows Credential Manager / macOS Keychain / DPAPI などで保護する。`accessToken` は `GithubSource` 経由で `Authorization` ヘッダに送信される
- **エラーログ**: `Options.LogEmitted` または `Options.ErrorOccurred` で受け取った `Exception` を外部テレメトリ (Sentry / Application Insights 等) に送る場合、本ライブラリは生のメッセージを sanitize しない。ホスト側で転送時に PII / token を redact すること

詳細は [Velopack 公式ドキュメント](https://docs.velopack.io/packaging/signing) も参照。

## License

MIT
