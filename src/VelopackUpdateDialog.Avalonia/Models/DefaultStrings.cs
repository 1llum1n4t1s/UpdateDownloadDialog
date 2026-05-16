namespace VelopackUpdateDialog;

/// <summary>
/// 英語のデフォルト文字列セット。
/// 差し替えたい場合は <see cref="IUpdateDialogStrings"/> を実装して
/// <see cref="UpdateDialogOptions.Strings"/> に渡す。
/// </summary>
public sealed class DefaultStrings : IUpdateDialogStrings
{
    /// <summary>シングルトン インスタンス。</summary>
    public static readonly DefaultStrings Instance = new();

    /// <inheritdoc />
    public string Title => "Self Update";

    /// <inheritdoc />
    public string AvailableHeader => "New version available!";

    /// <inheritdoc />
    public string DownloadAndInstall => "Download and install";

    /// <inheritdoc />
    public string IgnoreThisVersion => "Ignore this version";

    /// <inheritdoc />
    public string UpToDateMessage => "You're using the latest version.";

    /// <inheritdoc />
    public string ErrorHeader => "Self update failed";

    /// <inheritdoc />
    public string Close => "Close";

    /// <inheritdoc />
    public string CheckingMessage => "Checking for updates...";
}
