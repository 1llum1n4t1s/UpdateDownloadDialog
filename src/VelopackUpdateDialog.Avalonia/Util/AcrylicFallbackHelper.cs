using System;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Microsoft.Win32;

namespace VelopackUpdateDialog;

/// <summary>
/// アクリルブラーが実際には効かない環境 (Windows の透過効果 OFF・リモートデスクトップ・
/// プラットフォーム非対応) を検出する。アクリルの背後は不透明黒で塗られるため、特にライト
/// テーマでは薄いウィンドウ背景色が黒と混ざってくすんだ灰色に見えてしまう。そうした環境では
/// <c>ExperimentalAcrylicBorder</c> を隠し、テーマ色のソリッド背景へフォールバックする判断に使う。
/// <para>Windows は透過効果 OFF でも <c>ActualTransparencyLevel</c> を AcrylicBlur と報告するため、
/// 透過レベルだけでなく OS 側の状態 (リモートセッション判定・レジストリ) も併せて見る。</para>
/// </summary>
internal static class AcrylicFallbackHelper
{
    private const int SmRemoteSession = 0x1000;

    /// <summary>このウィンドウでアクリルをやめてソリッド背景にフォールバックすべきか。</summary>
    public static bool ShouldUseOpaqueBackground(Window window)
    {
        var acrylicGranted = window.ActualTransparencyLevel == WindowTransparencyLevel.AcrylicBlur;
        return ShouldUseOpaqueBackgroundCore(
            acrylicGranted,
            OperatingSystem.IsWindows(),
            OperatingSystem.IsWindows() && IsRemoteSession(),
            !OperatingSystem.IsWindows() || IsTransparencyEnabled());
    }

    /// <summary>判定ロジック本体 (副作用なしの純粋関数として分離)。</summary>
    internal static bool ShouldUseOpaqueBackgroundCore(
        bool acrylicGranted, bool isWindows, bool isRemoteSession, bool transparencyEnabled)
    {
        if (!acrylicGranted)
        {
            return true;
        }

        // Windows は透過効果 OFF / RDP でも acrylicGranted=true を返すので OS 状態で補正する
        return isWindows && (isRemoteSession || !transparencyEnabled);
    }

    private static bool IsRemoteSession()
    {
        try
        {
            return GetSystemMetrics(SmRemoteSession) != 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static bool IsTransparencyEnabled()
    {
        if (!OperatingSystem.IsWindows())
        {
            return true;
        }

        try
        {
            var value = Registry.GetValue(
                @"HKEY_CURRENT_USER\SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                "EnableTransparency",
                1);
            // 値が無い / 想定外の型なら透過有効とみなして従来動作 (アクリル) を維持
            return value is not int enabled || enabled != 0;
        }
        catch (Exception)
        {
            return true;
        }
    }

    // LibraryImport (.NET 7+ のソース生成 P/Invoke) は AllowUnsafeBlocks=true を要求する (SYSLIB1062)。
    // blittable な int 1 つのためにライブラリ全体の unsafe を開ける方が損なので DllImport を維持する。
    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);
}
