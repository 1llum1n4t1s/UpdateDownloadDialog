using Avalonia.Media;

namespace VelopackUpdateDialog;

/// <summary>
/// ダイアログで使うベクタアイコン (Path Data) を差し替えるための拡張点。
/// ホストアプリ独自のアイコンセットと統一したい場合に実装する。
/// </summary>
public interface IUpdateDialogIcons
{
    /// <summary>タイトルバーに使う「ソフトウェア更新」アイコン。</summary>
    Geometry SoftwareUpdate { get; }

    /// <summary>情報表示用の「インフォ」アイコン。</summary>
    Geometry Info { get; }

    /// <summary>ダウンロード ボタン上の「下向き矢印」風アイコン。</summary>
    Geometry Download { get; }

    /// <summary>無視 ボタン上の「× / 禁止」アイコン。</summary>
    Geometry Ignore { get; }

    /// <summary>失敗時の「エラー」アイコン。</summary>
    Geometry Error { get; }
}
