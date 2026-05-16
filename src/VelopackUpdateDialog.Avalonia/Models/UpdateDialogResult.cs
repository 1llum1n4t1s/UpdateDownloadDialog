using System;

namespace VelopackUpdateDialog;

/// <summary>
/// ダイアログが閉じた時点の結果。<c>UpdateDialogWindow.ShowAsync</c> の戻り値。
/// </summary>
public sealed record UpdateDialogResult(UpdateOutcome Outcome, Exception? Error = null);

/// <summary>
/// ダイアログ終了時の最終的な分岐。
/// </summary>
public enum UpdateOutcome
{
    /// <summary>新バージョンが見つかってダウンロード完了→再起動指示まで進んだ。<br/>
    /// 実際にはこの outcome を返す前にアプリは再起動されるため、利用側が観測する機会は通常ない。</summary>
    Updated,

    /// <summary>更新は不要だった（最新版）。</summary>
    UpToDate,

    /// <summary>ユーザーが「このバージョンを無視」を選んだ。</summary>
    Ignored,

    /// <summary>ダウンロード中にユーザーが閉じる/キャンセルした。</summary>
    Cancelled,

    /// <summary>チェック / ダウンロード中にエラーが発生した。<c>Error</c> を参照。</summary>
    Failed,

    /// <summary>ユーザーが状態に関係なくウィンドウを閉じた。</summary>
    Closed,
}
