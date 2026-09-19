using System;

namespace VelopackUpdateDialog;

/// <summary>
/// 更新ダイアログからホストへ通知するログ項目。
/// 本ライブラリ自身はログを出力せず、ホストが任意のロガーやテレメトリへ転送する。
/// </summary>
/// <param name="Level">ログの重要度。</param>
/// <param name="Message">ログメッセージ。</param>
/// <param name="Exception">関連する例外。例外を伴わない項目では null。</param>
public sealed record UpdateDialogLogEntry(
    UpdateDialogLogLevel Level,
    string Message,
    Exception? Exception = null);
