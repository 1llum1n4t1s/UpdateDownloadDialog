using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Velopack;
using Velopack.Sources;

namespace VelopackUpdateDialog;

/// <summary>
/// 更新ダイアログの状態機械と Velopack 連携を司る ViewModel。
/// Window / UserControl いずれの提供レイヤーからも DataContext として再利用される。
/// </summary>
public sealed partial class UpdateDialogViewModel : ObservableObject
{
    /// <summary>
    /// 既存の <see cref="UpdateManager"/> を持ち込んで初期化する。
    /// Velopack の初期化はホスト側で行う前提（GithubSource の URL 等が外部依存になりがちなため）。
    /// </summary>
    public UpdateDialogViewModel(UpdateManager manager, UpdateDialogOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(manager);
        _manager = manager;
        Options = options ?? UpdateDialogOptions.Default;
    }

    /// <summary>
    /// GitHub リポジトリ URL から <see cref="GithubSource"/> + <see cref="UpdateManager"/> を内部で組み立てる便利コンストラクタ。
    /// </summary>
    /// <param name="githubRepoUrl">例: <c>https://github.com/owner/repo</c></param>
    /// <param name="accessToken">プライベートリポジトリ用トークン (省略可)。</param>
    /// <param name="prerelease">プレリリースも検索対象にするか。</param>
    /// <param name="options">表示・振る舞いオプション。</param>
    public UpdateDialogViewModel(string githubRepoUrl, string? accessToken = null, bool prerelease = false, UpdateDialogOptions? options = null)
        : this(new UpdateManager(new GithubSource(githubRepoUrl, accessToken ?? string.Empty, prerelease)), options)
    {
    }

    // ---------------- 公開プロパティ ----------------

    /// <summary>表示・振る舞いオプション (読み取り専用)。</summary>
    public UpdateDialogOptions Options { get; }

    /// <summary>解決された文字列セット。XAML バインディング用。</summary>
    public IUpdateDialogStrings Strings => Options.ResolvedStrings;

    /// <summary>解決されたアイコンセット。XAML バインディング用。</summary>
    public IUpdateDialogIcons Icons => Options.ResolvedIcons;

    /// <summary>アクセントカラー。null の場合はテーマ既定。</summary>
    public IBrush? AccentBrush => Options.AccentBrush;

    /// <summary>「無視」ボタンの表示可否。</summary>
    public bool AllowIgnoreVersion => Options.AllowIgnoreVersion;

    /// <summary>現在の状態。</summary>
    [ObservableProperty]
    public partial UpdateState State { get; set; } = UpdateState.Idle;

    /// <summary>新バージョンが見つかった時のタグ名 (例: <c>v1.0.5</c>)。</summary>
    [ObservableProperty]
    public partial string? AvailableTagName { get; set; }

    /// <summary>ダウンロード進捗 (0-100)。</summary>
    [ObservableProperty]
    public partial int DownloadProgress { get; set; }

    /// <summary>失敗時のエラーメッセージ。</summary>
    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    /// <summary>このダイアログが最終的に確定した outcome。Window 側がこれを見て ShowAsync の戻り値を組み立てる。</summary>
    public UpdateOutcome FinalOutcome { get; private set; } = UpdateOutcome.Closed;

    /// <summary>最終的に保持された例外 (Failed 時)。</summary>
    public Exception? FinalError { get; private set; }

    // ---------------- 派生 (XAML 用) ----------------

    /// <summary>State == Checking</summary>
    public bool IsChecking => State == UpdateState.Checking;

    /// <summary>State == Available</summary>
    public bool IsAvailable => State == UpdateState.Available;

    /// <summary>State == UpToDate</summary>
    public bool IsUpToDate => State == UpdateState.UpToDate;

    /// <summary>State == Downloading</summary>
    public bool IsDownloading => State == UpdateState.Downloading;

    /// <summary>State == Failed</summary>
    public bool IsFailed => State == UpdateState.Failed;

    /// <summary>Available または Failed (≒ ボタン領域を出す状態)</summary>
    public bool ShowActionButtons => IsAvailable || IsUpToDate || IsFailed;

    partial void OnStateChanged(UpdateState value)
    {
        OnPropertyChanged(nameof(IsChecking));
        OnPropertyChanged(nameof(IsAvailable));
        OnPropertyChanged(nameof(IsUpToDate));
        OnPropertyChanged(nameof(IsDownloading));
        OnPropertyChanged(nameof(IsFailed));
        OnPropertyChanged(nameof(ShowActionButtons));
    }

    // ---------------- 状態遷移 ----------------

    /// <summary>
    /// 利用可能更新を直接セット (Velopack の <see cref="UpdateInfo"/> をホスト側で取得済みの場合)。
    /// </summary>
    public void SetAvailable(UpdateInfo updateInfo)
    {
        ArgumentNullException.ThrowIfNull(updateInfo);
        _updateInfo = updateInfo;
        AvailableTagName = $"v{updateInfo.TargetFullRelease.Version}";
        State = UpdateState.Available;
    }

    /// <summary>状態を「最新」にセット。</summary>
    public void SetUpToDate()
    {
        _updateInfo = null;
        State = UpdateState.UpToDate;
    }

    /// <summary>状態を「失敗」にセット。</summary>
    public void SetFailed(Exception ex)
    {
        ArgumentNullException.ThrowIfNull(ex);
        FinalError = ex;
        ErrorMessage = ex.InnerException?.Message ?? ex.Message;
        State = UpdateState.Failed;
        Options.RaiseErrorOccurred(ex);
    }

    /// <summary>
    /// GitHub Release を確認し、結果に応じて Available / UpToDate / Failed へ遷移する。
    /// </summary>
    /// <param name="manualCheck">true ならユーザー主導の手動チェック (最新であってもダイアログを残す)。</param>
    /// <param name="cancellationToken">チェックをキャンセルする際のトークン。</param>
    public async Task CheckAsync(bool manualCheck = false, CancellationToken cancellationToken = default)
    {
        if (State is UpdateState.Checking or UpdateState.Downloading)
            return;

        State = UpdateState.Checking;

        try
        {
            // 開発環境 (未インストール) では「最新」扱いにする
            if (!_manager.IsInstalled)
            {
                SetUpToDate();
                return;
            }

            cancellationToken.ThrowIfCancellationRequested();
            var info = await _manager.CheckForUpdatesAsync().ConfigureAwait(true);
            cancellationToken.ThrowIfCancellationRequested();

            if (info is null)
            {
                SetUpToDate();
                return;
            }

            SetAvailable(info);
        }
        catch (OperationCanceledException)
        {
            FinalOutcome = UpdateOutcome.Cancelled;
            throw;
        }
        catch (Exception ex)
        {
            // SetFailed が ErrorOccurred を発火するため、ここでは追加 raise しない
            SetFailed(ex);
        }
    }

    /// <summary>
    /// 利用可能な更新をダウンロードし、完了後にアプリを再起動する。
    /// </summary>
    public Task DownloadAndApplyAsync()
    {
        if (_updateInfo is null || State == UpdateState.Downloading)
            return Task.CompletedTask;

        _downloadCts?.Dispose();
        _downloadCts = new CancellationTokenSource();
        var token = _downloadCts.Token;
        var info = _updateInfo;

        State = UpdateState.Downloading;
        DownloadProgress = 0;

        return Task.Run(async () =>
        {
            try
            {
                await _manager.DownloadUpdatesAsync(
                    info,
                    p => Dispatcher.UIThread.Post(() => DownloadProgress = p),
                    cancelToken: token).ConfigureAwait(false);

                // ApplyUpdatesAndRestart は UI スレッドでなくてもよいが、Velopack 内で
                // プロセス再起動が走るため、ここで戻り値は事実上観測不能。
                FinalOutcome = UpdateOutcome.Updated;
                _manager.ApplyUpdatesAndRestart(info);
            }
            catch (OperationCanceledException)
            {
                FinalOutcome = UpdateOutcome.Cancelled;
                Dispatcher.UIThread.Post(() => State = UpdateState.Available);
            }
            catch (Exception ex)
            {
                Dispatcher.UIThread.Post(() => SetFailed(ex));
            }
        });
    }

    /// <summary>ダウンロードをキャンセル。</summary>
    public void CancelDownload()
    {
        _downloadCts?.Cancel();
    }

    /// <summary>
    /// 「このバージョンを無視」をユーザーが選んだ際に呼ぶ。
    /// ホスト側は <see cref="UpdateDialogOptions.VersionIgnored"/> を購読して永続化する。
    /// </summary>
    public void IgnoreCurrentAvailable()
    {
        if (AvailableTagName is { Length: > 0 } tag)
        {
            FinalOutcome = UpdateOutcome.Ignored;
            Options.RaiseVersionIgnored(tag);
        }
    }

    /// <summary>
    /// Window 側で <c>Closing</c> 時に呼ぶ。ダウンロード中ならキャンセルし、FinalOutcome を確定する。
    /// </summary>
    public void OnClosing()
    {
        if (State == UpdateState.Downloading)
        {
            CancelDownload();
            FinalOutcome = UpdateOutcome.Cancelled;
            return;
        }

        if (FinalOutcome == UpdateOutcome.Closed)
        {
            FinalOutcome = State switch
            {
                UpdateState.UpToDate => UpdateOutcome.UpToDate,
                UpdateState.Failed => UpdateOutcome.Failed,
                _ => UpdateOutcome.Closed,
            };
        }
    }

    private readonly UpdateManager _manager;
    private UpdateInfo? _updateInfo;
    private CancellationTokenSource? _downloadCts;
}
