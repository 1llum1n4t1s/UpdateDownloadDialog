using Avalonia.Media;

namespace VelopackUpdateDialog;

/// <summary>
/// Material Design Icons から抜粋した PathGeometry のデフォルトセット。
/// シングルトン化されているのでリソースを浪費しない。
/// </summary>
public sealed class DefaultIcons : IUpdateDialogIcons
{
    /// <summary>シングルトン インスタンス。</summary>
    public static readonly DefaultIcons Instance = new();

    private static readonly Geometry s_softwareUpdate = Geometry.Parse(
        "M12 1L3 5v6c0 5.55 3.84 10.74 9 12 5.16-1.26 9-6.45 9-12V5l-9-4zM12 7l4 4h-3v4h-2v-4H8l4-4z");

    private static readonly Geometry s_info = Geometry.Parse(
        "M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm1 15h-2v-6h2v6zm0-8h-2V7h2v2z");

    private static readonly Geometry s_download = Geometry.Parse(
        "M19 9h-4V3H9v6H5l7 7 7-7zM5 18v2h14v-2H5z");

    private static readonly Geometry s_ignore = Geometry.Parse(
        "M12 2C6.47 2 2 6.47 2 12s4.47 10 10 10 10-4.47 10-10S17.53 2 12 2zm5 13.59L15.59 17 12 13.41 8.41 17 7 15.59 10.59 12 7 8.41 8.41 7 12 10.59 15.59 7 17 8.41 13.41 12 17 15.59z");

    private static readonly Geometry s_error = Geometry.Parse(
        "M12 2C6.47 2 2 6.47 2 12s4.47 10 10 10 10-4.47 10-10S17.53 2 12 2zm1 15h-2v-2h2v2zm0-4h-2V7h2v6z");

    /// <inheritdoc />
    public Geometry SoftwareUpdate => s_softwareUpdate;

    /// <inheritdoc />
    public Geometry Info => s_info;

    /// <inheritdoc />
    public Geometry Download => s_download;

    /// <inheritdoc />
    public Geometry Ignore => s_ignore;

    /// <inheritdoc />
    public Geometry Error => s_error;
}
