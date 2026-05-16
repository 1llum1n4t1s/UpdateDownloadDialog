using System;
using Avalonia;
using Avalonia.Media;

namespace VelopackUpdateDialog;

/// <summary>
/// <see cref="UpdateDialogWindow"/> / <see cref="UpdateDialogView"/> をカスタマイズするためのオプション集。
/// 全プロパティに既定値があるため、ホスト側は差し替えたい項目だけ設定すれば良い。
/// </summary>
public sealed class UpdateDialogOptions
{
    // ---------------- ローカライゼーション ----------------

    /// <summary>表示文字列セット。null の場合は <see cref="DefaultStrings.Instance"/>。</summary>
    public IUpdateDialogStrings? Strings { get; set; }

    /// <summary>解決された文字列セット (null セーフ アクセサ)。</summary>
    public IUpdateDialogStrings ResolvedStrings => Strings ?? DefaultStrings.Instance;

    // ---------------- アイコン ----------------

    /// <summary>表示アイコンセット。null の場合は <see cref="DefaultIcons.Instance"/>。</summary>
    public IUpdateDialogIcons? Icons { get; set; }

    /// <summary>解決されたアイコンセット (null セーフ アクセサ)。</summary>
    public IUpdateDialogIcons ResolvedIcons => Icons ?? DefaultIcons.Instance;

    // ---------------- ウィンドウ chrome / サイズ ----------------

    /// <summary>ウィンドウのフレーム描画モード。既定 = <see cref="WindowChromeMode.Custom"/>。</summary>
    public WindowChromeMode ChromeMode { get; set; } = WindowChromeMode.Custom;

    /// <summary>リサイズ挙動。既定 = <see cref="WindowResizeMode.Fixed"/>。</summary>
    public WindowResizeMode ResizeMode { get; set; } = WindowResizeMode.Fixed;

    /// <summary>明示的な初期サイズ。null = ResizeMode に応じて自動 (Fixed なら SizeToContent、Resizable なら 500x200)。</summary>
    public Size? InitialSize { get; set; }

    /// <summary>リサイズ可能時の最小サイズ。</summary>
    public Size MinSize { get; set; } = UpdateDialogDefaults.MinSize;

    /// <summary>リサイズ可能時の最大サイズ。null = 無制限。</summary>
    public Size? MaxSize { get; set; }

    // ---------------- テーマ / 配色 ----------------

    /// <summary>アクセントカラー。Available 状態のバージョン バッジやプライマリ ボタンに反映。null = テーマの既定。</summary>
    public IBrush? AccentBrush { get; set; }

    // ---------------- 振る舞い ----------------

    /// <summary>「このバージョンを無視」ボタンを表示するか。既定 = true。</summary>
    public bool AllowIgnoreVersion { get; set; } = true;

    /// <summary>ダウンロード中にウィンドウを閉じることを許可するか。
    /// false にすると Close ボタン / Esc を抑止する。既定 = true (閉じる = キャンセル扱い)。</summary>
    public bool AllowCloseDuringDownload { get; set; } = true;

    /// <summary>最新版だった場合、自動チェック時は表示しない (true)。
    /// 手動チェック時に必ず結果を表示するなら呼び出し側で <c>manual: true</c> 指定。既定 = true。</summary>
    public bool SuppressUpToDateOnAutoCheck { get; set; } = true;

    /// <summary>「このバージョンを無視」でホスト側が永続化したタグ名を渡しておくと、
    /// 自動チェック時にそのタグの更新が見つかってもダイアログを一切表示しない。
    /// 手動チェック時は無視されず通常通り表示される（ユーザー主導の操作を妨げないため）。
    /// null / 空文字なら判定しない。例: <c>"v1.0.5"</c>。</summary>
    public string? IgnoredTagName { get; set; }

    // ---------------- コールバック / イベント ----------------

    /// <summary>ユーザーが「このバージョンを無視」を押した時に発火。
    /// ホスト側で Preferences 等への永続化を行う想定。</summary>
    public event Action<string>? VersionIgnored;

    /// <summary>例外発生時に発火。ホスト側のロガーへ流す想定。</summary>
    public event Action<Exception>? ErrorOccurred;

    internal void RaiseVersionIgnored(string tagName) => VersionIgnored?.Invoke(tagName);

    internal void RaiseErrorOccurred(Exception ex) => ErrorOccurred?.Invoke(ex);
}
