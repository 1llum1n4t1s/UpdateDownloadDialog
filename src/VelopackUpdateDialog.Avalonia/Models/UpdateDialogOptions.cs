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

    // ---------------- ウィンドウ chrome / サイズ ----------------

    /// <summary>ウィンドウのフレーム描画モード。既定 = <see cref="WindowChromeMode.Custom"/>。</summary>
    public WindowChromeMode ChromeMode { get; set; } = WindowChromeMode.Custom;

    /// <summary>リサイズ挙動。既定 = <see cref="WindowResizeMode.Fixed"/>。</summary>
    public WindowResizeMode ResizeMode { get; set; } = WindowResizeMode.Fixed;

    /// <summary>明示的な初期サイズ。null = ResizeMode に応じて自動 (Fixed なら SizeToContent、Resizable なら 540x200)。</summary>
    public Size? InitialSize { get; set; }

    /// <summary>リサイズ可能時の最小サイズ。幅を 540 未満にするとダウンロード進捗 UI が横クリップする可能性がある。</summary>
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
    /// 手動チェック時に必ず結果を表示するなら <see cref="UpdateDialogWindow.ShowAsync"/> で
    /// <c>manualCheck: true</c> を指定する。既定 = true。</summary>
    public bool SuppressUpToDateOnAutoCheck { get; set; } = true;

    /// <summary>「このバージョンを無視」でホスト側が永続化したタグ名を渡しておくと、
    /// 自動チェック時にそのタグの更新が見つかってもダイアログを一切表示しない。
    /// 手動チェック時は無視されず通常通り表示される（ユーザー主導の操作を妨げないため）。
    /// null / 空文字なら判定しない。例: <c>"v1.0.5"</c>。</summary>
    public string? IgnoredTagName { get; set; }

    // ---------------- コールバック / イベント ----------------

    /// <summary>ユーザーが「このバージョンを無視」を押した時に発火。
    /// ホスト側で Preferences 等への永続化を行う想定。
    /// <para>このオプションは 1 ダイアログ呼び出しにつき 1 インスタンスの利用を想定する。
    /// 同一インスタンスを使い回してハンドラを毎回登録すると、二重発火やハンドラがキャプチャした
    /// オブジェクト (UI 要素等) の購読者リークの原因になる。</para></summary>
    public event Action<string>? VersionIgnored;

    /// <summary>例外発生時に発火する、失敗に特化した通知。
    /// ログ全体は <see cref="LogEmitted"/> から受け取る。
    /// 使い回し時の注意は <see cref="VersionIgnored"/> と同じ。</summary>
    public event Action<Exception>? ErrorOccurred;

    /// <summary>
    /// 状態遷移、警告、失敗のログ項目をホストへ通知する。
    /// 本ライブラリ自身はログを出力しないため、必要に応じてホスト側のロガーやテレメトリへ転送する。
    /// 通知は状態変更などを行ったスレッドで同期実行されるため、時間のかかる転送はホスト側で非同期化する。
    /// 使い回し時の注意は <see cref="VersionIgnored"/> と同じ。
    /// </summary>
    public event Action<UpdateDialogLogEntry>? LogEmitted;

    internal void RaiseVersionIgnored(string tagName) => VersionIgnored?.Invoke(tagName);

    internal void RaiseErrorOccurred(Exception ex) => ErrorOccurred?.Invoke(ex);

    internal void RaiseLog(UpdateDialogLogEntry entry)
    {
        if (LogEmitted is not { } handlers)
            return;

        foreach (Action<UpdateDialogLogEntry> handler in handlers.GetInvocationList())
        {
            try
            {
                handler(entry);
            }
            catch (Exception)
            {
                // ログ転送先の障害で更新フローを失敗させない。
                // 本ライブラリは独自の代替出力を行わないため、ここでは通知を打ち切らず次の購読者へ進む。
            }
        }
    }
}
