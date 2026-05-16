namespace VelopackUpdateDialog;

/// <summary>
/// ダイアログのリサイズ挙動。
/// </summary>
public enum WindowResizeMode
{
    /// <summary>固定サイズ。<c>SizeToContent=WidthAndHeight</c> + <c>CanResize=False</c>。デフォルト。</summary>
    Fixed,

    /// <summary>可変サイズ。<c>CanResize=True</c> + <c>MinSize</c>/<c>MaxSize</c> を尊重。</summary>
    Resizable,
}
