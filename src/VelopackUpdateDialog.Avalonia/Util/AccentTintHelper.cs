using System;
using Avalonia;
using Avalonia.Media;

namespace VelopackUpdateDialog;

/// <summary>
/// OS のアクセントカラーをウィンドウ背景にごく薄く上乗せするための tint ブラシを生成する。
/// ライトテーマ + 青アクセントなら背景がごく薄い水色になる、という趣旨。背景レイヤー
/// (アクリル / ソリッド フォールバック双方) の上・コンテンツの下に敷いて使う。
/// </summary>
internal static class AccentTintHelper
{
    /// <summary>上乗せの濃さ (α 0x18 ≈ 9%。「凄く薄い」が知覚できる程度)。</summary>
    private const byte TintAlpha = 0x18;

    /// <summary>
    /// 現在の OS アクセントカラーから tint ブラシを作る。取得できなければ null
    /// (この場合は上乗せなしとして扱う)。
    /// </summary>
    public static IBrush? TryCreateTintBrush()
    {
        try
        {
            var colors = Application.Current?.PlatformSettings?.GetColorValues();
            if (colors is not { } c)
            {
                return null;
            }

            var accent = c.AccentColor1;
            return new SolidColorBrush(Color.FromArgb(TintAlpha, accent.R, accent.G, accent.B));
        }
        catch (Exception)
        {
            return null;
        }
    }
}
