using Avalonia;

namespace VelopackUpdateDialog;

/// <summary>
/// ライブラリ内で散在しがちなマジックナンバーを集約する内部定数。
/// 公開はしない (利用者は <see cref="UpdateDialogOptions"/> 経由で上書き可能)。
/// </summary>
internal static class UpdateDialogDefaults
{
    /// <summary>500px のダウンロード進捗バーと左右余白を収める最小ウィンドウ幅。</summary>
    public const double ContentMinWidth = 540;

    /// <summary>Resizable モードのデフォルト初期サイズ。</summary>
    public static readonly Size InitialSize = new(ContentMinWidth, 200);

    /// <summary>Resizable モードのデフォルト最小サイズ。</summary>
    public static readonly Size MinSize = new(ContentMinWidth, 120);
}
