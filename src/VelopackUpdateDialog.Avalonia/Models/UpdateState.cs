namespace VelopackUpdateDialog;

/// <summary>
/// ダイアログ ViewModel が取り得る状態。
/// </summary>
public enum UpdateState
{
    /// <summary>未チェック / 初期状態。</summary>
    Idle,

    /// <summary>GitHub Release を確認中。</summary>
    Checking,

    /// <summary>新バージョンが利用可能。<c>VelopackUpdate</c> 情報を保持。</summary>
    Available,

    /// <summary>現在のバージョンが最新。</summary>
    UpToDate,

    /// <summary>更新パッケージをダウンロード中。</summary>
    Downloading,

    /// <summary>チェック / ダウンロードでエラー発生。<c>ErrorMessage</c> を参照。</summary>
    Failed,
}
