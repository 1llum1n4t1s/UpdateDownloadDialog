using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Velopack;
using Velopack.Sources;

namespace VelopackUpdateDialog;

/// <summary>
/// そのまま <c>ShowDialog()</c> できる完成形ウィンドウ。
/// 静的便利メソッド <see cref="ShowAsync(Window, UpdateManager, UpdateDialogOptions?, bool, CancellationToken)"/> で
/// 1 行呼び出しが可能。
/// </summary>
public partial class UpdateDialogWindow : Window
{
    /// <summary>
    /// ViewModel をそのまま受け取って初期化する。
    /// </summary>
    public UpdateDialogWindow(UpdateDialogViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        InitializeComponent();
        DataContext = viewModel;
        _viewModel = viewModel;

        ApplyOptions(viewModel.Options);

        // View からの閉じる要求を受けて Close する
        DialogBody.CloseRequested += (_, _) => Close();
    }

    /// <summary>
    /// パラメタレスコンストラクタ (Avalonia のデザイナ用)。実行時は呼ばない。
    /// </summary>
    public UpdateDialogWindow() : this(CreateDesignViewModel())
    {
    }

    private static UpdateDialogViewModel CreateDesignViewModel()
    {
        // デザイナでも DataContext を解決するためのダミー。
        // Velopack の UpdateManager にはダミー source を渡す。
        return new UpdateDialogViewModel(new UpdateManager(new SimpleFileSource(new System.IO.DirectoryInfo(System.IO.Path.GetTempPath()))));
    }

    /// <summary>
    /// 1 行呼び出しの便利メソッド。チェック → 結果に応じて自動で UI を出し、
    /// 最終 outcome を <see cref="UpdateDialogResult"/> として返す。
    /// <para>
    /// 手動チェック (<paramref name="manualCheck"/> = true) 時はチェック進捗を見せたいので
    /// 即ウィンドウを表示し、バックグラウンドでチェック → 結果に応じて Available/UpToDate/Failed 状態へ遷移。
    /// 自動チェック時はチェック完了まで何も表示せず、以下の場合のみウィンドウを開く:
    /// </para>
    /// <list type="bullet">
    ///   <item>更新が利用可能 (<see cref="UpdateState.Available"/>) かつ
    ///         <see cref="UpdateDialogOptions.IgnoredTagName"/> と一致しない</item>
    /// </list>
    /// <para>
    /// 自動チェックで最新版 / 無視タグ / 失敗だった場合はウィンドウを開かず、戻り値で結果を通知する。
    /// </para>
    /// </summary>
    /// <param name="owner">親ウィンドウ。null の場合は <see cref="Window.Show(Window)"/> ではなく <see cref="Window.Show()"/> で表示。</param>
    /// <param name="manager">既存の <see cref="UpdateManager"/>。</param>
    /// <param name="options">表示・振る舞いオプション。</param>
    /// <param name="manualCheck">手動チェックなら true (最新版でも結果ダイアログを残す)。</param>
    /// <param name="cancellationToken">チェック処理用キャンセル トークン。</param>
    public static async Task<UpdateDialogResult> ShowAsync(
        Window? owner,
        UpdateManager manager,
        UpdateDialogOptions? options = null,
        bool manualCheck = false,
        CancellationToken cancellationToken = default)
    {
        var resolvedOptions = options ?? new UpdateDialogOptions();
        var vm = new UpdateDialogViewModel(manager, resolvedOptions);

        // 自動チェック時はウィンドウを開かず先にチェック。
        // 「最新」「無視タグ一致」「失敗」のどれかなら何も表示せず戻る。
        if (!manualCheck)
        {
            try
            {
                await vm.CheckAsync(manualCheck: false, cancellationToken).ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
                return new UpdateDialogResult(UpdateOutcome.Cancelled);
            }

            switch (vm.State)
            {
                case UpdateState.UpToDate when resolvedOptions.SuppressUpToDateOnAutoCheck:
                    return new UpdateDialogResult(UpdateOutcome.UpToDate);

                case UpdateState.Available
                    when !string.IsNullOrEmpty(resolvedOptions.IgnoredTagName)
                         && IsSameVersionTag(vm.AvailableTagName, resolvedOptions.IgnoredTagName):
                    return new UpdateDialogResult(UpdateOutcome.Ignored);

                case UpdateState.Failed:
                    // 自動チェックの失敗は ErrorOccurred 経由でホストに通知済み。ダイアログは出さない。
                    return new UpdateDialogResult(UpdateOutcome.Failed, vm.FinalError);
            }
        }

        var window = new UpdateDialogWindow(vm);

        // 手動チェック時のみ、ウィンドウ表示と並行してチェックを開始する
        if (manualCheck)
        {
            _ = vm.CheckAsync(manualCheck: true, cancellationToken).ContinueWith(t =>
            {
                if (t.IsFaulted && t.Exception?.InnerException is { } ex)
                {
                    Avalonia.Threading.Dispatcher.UIThread.Post(() => vm.SetFailed(ex));
                }
                else if (t.IsCanceled)
                {
                    // cancellationToken 発火時に Window が「チェック中」表示で固まらないよう Idle に戻す
                    Avalonia.Threading.Dispatcher.UIThread.Post(() => vm.SetUpToDate());
                }
            }, TaskScheduler.Default);
        }

        if (owner is not null)
            await window.ShowDialog(owner).ConfigureAwait(true);
        else
            await ShowStandaloneAsync(window).ConfigureAwait(true);

        return new UpdateDialogResult(vm.FinalOutcome, vm.FinalError);
    }

    private static Task ShowStandaloneAsync(Window window)
    {
        var tcs = new TaskCompletionSource<object?>();
        EventHandler? handler = null;
        handler = (_, _) =>
        {
            window.Closed -= handler;
            tcs.TrySetResult(null);
        };
        window.Closed += handler;
        window.Show();
        return tcs.Task;
    }

    /// <inheritdoc />
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        // ダウンロード中はキャンセル不可オプション
        if (!_viewModel.Options.AllowCloseDuringDownload
            && _viewModel.State == UpdateState.Downloading)
        {
            e.Cancel = true;
            return;
        }

        _viewModel.OnClosing();
        base.OnClosing(e);
    }

    private void ApplyOptions(UpdateDialogOptions options)
    {
        // chrome
        switch (options.ChromeMode)
        {
            case WindowChromeMode.System:
                ExtendClientAreaToDecorationsHint = false;
                WindowDecorations = WindowDecorations.Full;
                CustomTitleBar.IsVisible = false;
                break;

            case WindowChromeMode.Custom:
                ExtendClientAreaToDecorationsHint = true;
                ExtendClientAreaTitleBarHeightHint = -1;
                WindowDecorations = WindowDecorations.BorderOnly;
                CustomTitleBar.IsVisible = true;
                break;
        }

        // サイズ
        switch (options.ResizeMode)
        {
            case WindowResizeMode.Fixed:
                SizeToContent = SizeToContent.WidthAndHeight;
                CanResize = false;
                if (options.InitialSize is { } fixedSize)
                {
                    SizeToContent = SizeToContent.Manual;
                    Width = fixedSize.Width;
                    Height = fixedSize.Height;
                }
                break;

            case WindowResizeMode.Resizable:
                SizeToContent = SizeToContent.Manual;
                CanResize = true;
                var init = options.InitialSize ?? UpdateDialogDefaults.InitialSize;
                Width = init.Width;
                Height = init.Height;
                MinWidth = options.MinSize.Width;
                MinHeight = options.MinSize.Height;
                if (options.MaxSize is { } max)
                {
                    MaxWidth = max.Width;
                    MaxHeight = max.Height;
                }
                break;
        }
    }

    private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }

    private void OnCloseClicked(object? sender, RoutedEventArgs e)
    {
        Close();
        e.Handled = true;
    }

    // バージョンタグ比較。先頭 v/V プレフィックスと前後空白を正規化して
    // ホストが "v1.0.5" / "V1.0.5" / "1.0.5" のどれで永続化しても一致するようにする。
    private static bool IsSameVersionTag(string? available, string? ignored)
    {
        if (string.IsNullOrEmpty(available) || string.IsNullOrEmpty(ignored))
            return false;

        var a = available.AsSpan().Trim();
        var b = ignored.AsSpan().Trim();
        if (a.Length > 0 && (a[0] == 'v' || a[0] == 'V')) a = a[1..];
        if (b.Length > 0 && (b[0] == 'v' || b[0] == 'V')) b = b[1..];
        return a.SequenceEqual(b);
    }

    private readonly UpdateDialogViewModel _viewModel;
}
