namespace VelopackUpdateDialog;

/// <summary>
/// ウィンドウのフレーム描画モード。
/// </summary>
public enum WindowChromeMode
{
    /// <summary>OS 標準フレームを使う。タイトルバー / 最小化・閉じるボタンは OS 提供。</summary>
    System,

    /// <summary>独自のタイトルバーを描画する borderless 風モード。</summary>
    Custom,
}
