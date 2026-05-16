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
    /// パーラレスコンストラクタ (Avalonia のデザイナ用)。実行時は呼ばない。
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
        var vm = new UpdateDialogViewModel(manager, options);
        var window = new UpdateDialogWindow(vm);

        // チェックは UI が見えている間に非同期で進める
        _ = vm.CheckAsync(manualCheck, cancellationToken).ContinueWith(t =>
        {
            if (t.IsFaulted && t.Exception?.InnerException is { } ex)
                Avalonia.Threading.Dispatcher.UIThread.Post(() => vm.SetFailed(ex));

            // 自動チェック + 最新版 + suppress が立ってる場合は無音で閉じる
            if (!manualCheck
                && vm.State == UpdateState.UpToDate
                && vm.Options.SuppressUpToDateOnAutoCheck)
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(window.Close);
            }
        }, TaskScheduler.Default);

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
                var init = options.InitialSize ?? new Size(500, 200);
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

    private readonly UpdateDialogViewModel _viewModel;
}
