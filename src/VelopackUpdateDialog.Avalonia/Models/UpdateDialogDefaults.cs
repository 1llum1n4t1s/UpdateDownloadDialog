using Avalonia;

namespace VelopackUpdateDialog;

/// <summary>
/// ライブラリ内で散在しがちなマジックナンバーを集約する内部定数。
/// 公開はしない (利用者は <see cref="UpdateDialogOptions"/> 経由で上書き可能)。
/// </summary>
internal static class UpdateDialogDefaults
{
    /// <summary>Resizable モードのデフォルト初期サイズ。</summary>
    public static readonly Size InitialSize = new(500, 200);

    /// <summary>Resizable モードのデフォルト最小サイズ。</summary>
    public static readonly Size MinSize = new(300, 120);

    /// <summary>macOS の Fixed モードで使う最小ウィンドウ幅。
    /// macOS は <c>SizeToContent</c> の measure がタイミング依存で、状態遷移や初回 measure で
    /// 幅を racy に狭く計算してコンテンツを横クリップすることがある。最小幅の床を設けると、
    /// 狭く出た値もこの床まで持ち上がり、全状態が同一幅に揃って横クリップが構造的に起きなくなる。
    /// 値はダウンロード状態の 500px プログレスバー + 左右余白 (16*2) + スラックから決定。</summary>
    public const double MacFixedMinWidth = 540;
}
