# VelopackUpdateDialog.Avalonia

Avalonia 12 で動く **Velopack 自動更新ダイアログ** の再利用可能パッケージ。

`Window` / `UserControl` / `ViewModel` の 3 段提供で、ホストアプリの設計に合わせて柔軟に組み込める。

## インストール

```bash
dotnet add package VelopackUpdateDialog.Avalonia
```

依存: `Avalonia 12.0.3+`, `CommunityToolkit.Mvvm 8.4.2+`, `Velopack 0.0.1298+`, TFM `net10.0`。

> 📦 **PackageId と namespace について**: NuGet パッケージ名は `VelopackUpdateDialog.Avalonia` ですが、C# namespace は `VelopackUpdateDialog`（`.Avalonia` 接尾辞なし）です。将来の WPF/WinForms 派生パッケージとの namespace 共有を見越した設計です。

## 最短の使い方

```csharp
using Velopack;
using Velopack.Sources;
using VelopackUpdateDialog;

var mgr = new UpdateManager(new GithubSource("https://github.com/owner/repo", string.Empty, false));
await UpdateDialogWindow.ShowAsync(parentWindow, mgr);
```

## オプション指定

```csharp
// 例: MyJapaneseStrings は IUpdateDialogStrings を実装するユーザー定義クラス。
//     最小実装は samples/DemoApp/MainWindow.axaml.cs の JapaneseStrings を参照。
// 例: MyIcons は IUpdateDialogIcons を実装するユーザー定義クラス。

var options = new UpdateDialogOptions
{
    // 表示文字列を差し替え（日本語 etc.）
    Strings = new MyJapaneseStrings(),

    // アイコンセットを差し替え
    Icons = MyIcons.Instance,

    // 大昔の SelfUpdate 風: ウィンドウ固定サイズ（デフォルト）
    ResizeMode = WindowResizeMode.Fixed,

    // 可変ウィンドウにする場合
    // ResizeMode = WindowResizeMode.Resizable,
    // InitialSize = new Size(600, 240),
    // MinSize = new Size(400, 160),

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

// エラーが起きた時のロギング
options.ErrorOccurred += ex => Logger.LogException("更新失敗", ex);

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
    <upd:UpdateDialogView DataContext="{Binding UpdateVm}"/>
</Window>
```

```csharp
var vm = new UpdateDialogViewModel(updateManager, options);
MyWindow.DataContext = vm;
await vm.CheckAsync(manualCheck: true);
```

### 3. `UpdateDialogViewModel` — 完全自前 UI

状態機械と Velopack 呼び出しロジックだけを再利用し、UI は完全自前で組む場合。

```csharp
var vm = new UpdateDialogViewModel(updateManager);
vm.PropertyChanged += (_, e) =>
{
    if (e.PropertyName == nameof(vm.State))
    {
        // 自前の UI を更新
    }
};
await vm.CheckAsync(manualCheck: true);
```

## カスタマイズ拡張点

| 拡張点 | インターフェース | 差し替え方法 |
|---|---|---|
| 文字列 (タイトル / ボタン / メッセージ) | `IUpdateDialogStrings` | `UpdateDialogOptions.Strings` |
| アイコン (Geometry 5 種) | `IUpdateDialogIcons` | `UpdateDialogOptions.Icons` |
| 配色 (アクセント) | `IBrush` | `UpdateDialogOptions.AccentBrush` |
| テーマ全体 (Light/Dark) | `ThemeVariant` | ホストアプリ側 `Application.RequestedThemeVariant` |
| 無視永続化 | `event Action<string>` | `UpdateDialogOptions.VersionIgnored` |
| エラー通知 | `event Action<Exception>` | `UpdateDialogOptions.ErrorOccurred` |

## 動作

| 状態 | 表示 |
|---|---|
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

## License

MIT
