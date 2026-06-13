using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform;
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
        AttachBackdrop(viewModel.Options.ApplyAccentTint);

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
                    // cancellationToken 発火時はキャンセルなので「最新版です」と誤表示せず、ウィンドウを閉じる。
                    // FinalOutcome は CheckAsync 内で既に Cancelled に確定済み (表示と戻り値が整合する)。
                    Avalonia.Threading.Dispatcher.UIThread.Post(() => window.Close());
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

    /// <inheritdoc />
    protected override void OnClosed(EventArgs e)
    {
        // ウィンドウが閉じきった後に ViewModel の CancellationTokenSource を解放する。
        // この時点で OnClosing 経由でダウンロードはキャンセル済み (AllowCloseDuringDownload=false なら閉じない)。
        _viewModel.Dispose();
        base.OnClosed(e);
    }

    private void ApplyOptions(UpdateDialogOptions options)
    {
        // chrome
        switch (options.ChromeMode)
        {
            case WindowChromeMode.System:
                // OS タイトルバーとアクリル背景は視覚的に両立しないので、
                // System モード時はアクリルを Off にしてソリッド背景に戻す。
                ExtendClientAreaToDecorationsHint = false;
                WindowDecorations = WindowDecorations.Full;
                CustomTitleBar.IsVisible = false;
                TransparencyLevelHint = new[] { Avalonia.Controls.WindowTransparencyLevel.None };
                AcrylicBackdrop.IsVisible = false;
                SolidBackdrop.IsVisible = true;
                break;

            case WindowChromeMode.Custom:
                ExtendClientAreaToDecorationsHint = true;
                ExtendClientAreaTitleBarHeightHint = -1;
                WindowDecorations = WindowDecorations.BorderOnly;
                CustomTitleBar.IsVisible = true;
                // XAML 側のアクリル背景をそのまま使う (TransparencyLevelHint は XAML で指定済み)
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
                else if (OperatingSystem.IsMacOS())
                {
                    // macOS は SizeToContent の measure がタイミング依存で、状態遷移
                    // (Checking→Available 等) や初回 measure で幅を racy に狭く取り違えて
                    // 横クリップを間欠的に起こす。最小幅の床を入れると、狭く出た値も
                    // この床まで持ち上がって全状態が同一幅に揃い、遷移時の再サイズ
                    // (= レースの発火点) が消える。高さは従来どおりコンテンツに追従させる。
                    MinWidth = UpdateDialogDefaults.MacFixedMinWidth;
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

    // 背景レイヤー (アクリル / ソリッド / アクセント上乗せ) の制御を取り付ける。
    // 透過レベル変化・Activated (設定アプリで透過を切り替えて戻ってきたケース)・
    // OS アクセント変更に追従して再評価する。
    private void AttachBackdrop(bool applyAccentTint)
    {
        _applyAccentTint = applyAccentTint;

        Opened += (_, _) => UpdateBackdrop();
        Activated += (_, _) => UpdateBackdrop();
        PropertyChanged += (_, e) =>
        {
            if (e.Property == TopLevel.ActualTransparencyLevelProperty)
            {
                UpdateBackdrop();
            }
        };

        if (!applyAccentTint)
        {
            return;
        }

        // ダイアログは短寿命なので、long-lived な PlatformSettings への購読は Closed で必ず解除する
        var platformSettings = Application.Current?.PlatformSettings;
        if (platformSettings is null)
        {
            return;
        }

        EventHandler<PlatformColorValues> onColorsChanged = (_, _) => UpdateAccentTint();
        platformSettings.ColorValuesChanged += onColorsChanged;
        Closed += (_, _) => platformSettings.ColorValuesChanged -= onColorsChanged;
    }

    // アクリルが実際に効かない環境 (RDP / 透過 OFF / 非対応) ではテーマ色のソリッド背景へ
    // フォールバックする。アクリルの背後は不透明黒で、特にライトテーマでは薄い背景色が
    // くすんだ灰色に見えてしまうため。System chrome は ApplyOptions で既にソリッド化済みだが、
    // ここで評価しても結果は一致するので一本化する。
    private void UpdateBackdrop()
    {
        var useOpaque = AcrylicFallbackHelper.ShouldUseOpaqueBackground(this);
        AcrylicBackdrop.IsVisible = !useOpaque;
        SolidBackdrop.IsVisible = useOpaque;
        UpdateAccentTint();
    }

    private void UpdateAccentTint()
    {
        if (!_applyAccentTint)
        {
            AccentTint.IsVisible = false;
            return;
        }

        var brush = AccentTintHelper.TryCreateTintBrush();
        AccentTint.Background = brush;
        AccentTint.IsVisible = brush is not null;
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
    private bool _applyAccentTint;
}
