namespace VelopackUpdateDialog;

/// <summary>ホストへ通知する更新処理ログの重要度。</summary>
public enum UpdateDialogLogLevel
{
    /// <summary>通常の状態遷移や処理状況。</summary>
    Information,

    /// <summary>処理を継続できるものの、ホスト側で確認すべき状態。</summary>
    Warning,

    /// <summary>更新処理を失敗させたエラー。</summary>
    Error,
}
