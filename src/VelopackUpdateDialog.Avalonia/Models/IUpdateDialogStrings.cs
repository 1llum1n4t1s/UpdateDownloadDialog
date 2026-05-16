namespace VelopackUpdateDialog;

/// <summary>
/// ダイアログで使う文字列を差し替えるための拡張点。
/// ホストアプリの i18n システムに合わせて実装し、<see cref="UpdateDialogOptions.Strings"/> に差し込む。
/// </summary>
public interface IUpdateDialogStrings
{
    /// <summary>ウィンドウ タイトル。例: <c>Self Update</c>。</summary>
    string Title { get; }

    /// <summary>新バージョンを発見した時のヘッダー文。例: <c>New version available!</c></summary>
    string AvailableHeader { get; }

    /// <summary>ダウンロード &amp; インストール ボタン。例: <c>Download and install</c></summary>
    string DownloadAndInstall { get; }

    /// <summary>このバージョンを無視 ボタン。例: <c>Ignore this version</c></summary>
    string IgnoreThisVersion { get; }

    /// <summary>最新版である旨。例: <c>You're using the latest version.</c></summary>
    string UpToDateMessage { get; }

    /// <summary>失敗時のヘッダー文。例: <c>Self update failed</c></summary>
    string ErrorHeader { get; }

    /// <summary>閉じる ボタン。例: <c>Close</c></summary>
    string Close { get; }

    /// <summary>確認中のメッセージ。例: <c>Checking for updates...</c></summary>
    string CheckingMessage { get; }
}
