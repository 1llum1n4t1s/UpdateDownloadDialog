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
}
