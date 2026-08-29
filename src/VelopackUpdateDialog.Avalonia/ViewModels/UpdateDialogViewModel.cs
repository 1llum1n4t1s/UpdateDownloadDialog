using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using SuperLightLogger;
using Velopack;
using Velopack.Sources;

namespace VelopackUpdateDialog;

/// <summary>
/// 更新ダイアログの状態機械と Velopack 連携を司る ViewModel。
/// Window / UserControl いずれの提供レイヤーからも DataContext として再利用される。
/// </summary>
public sealed partial class UpdateDialogViewModel : ObservableObject, IDisposable
{
    private static readonly ILog log = LogManager.GetLogger(typeof(UpdateDialogViewModel));

    /// <summary>
    /// 既存の <see cref="UpdateManager"/> を持ち込んで初期化する。
    /// Velopack の初期化はホスト側で行う前提（GithubSource の URL 等が外部依存になりがちなため）。
    /// </summary>
    public UpdateDialogViewModel(UpdateManager manager, UpdateDialogOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(manager);
        _manager = manager;
        _applyUpdatesAndRestart = info => manager.ApplyUpdatesAndRestart(info);
        _postToUi = callback => Dispatcher.UIThread.Post(callback);
        Options = options ?? new UpdateDialogOptions();
    }

    internal UpdateDialogViewModel(
        UpdateManager manager,
        UpdateDialogOptions? options,
        Action<UpdateInfo> applyUpdatesAndRestart,
        Action<Action> postToUi)
        : this(manager, options)
    {
        ArgumentNullException.ThrowIfNull(applyUpdatesAndRestart);
        ArgumentNullException.ThrowIfNull(postToUi);
        _applyUpdatesAndRestart = applyUpdatesAndRestart;
        _postToUi = postToUi;
    }

    /// <summary>
    /// GitHub リポジトリ URL から <see cref="GithubSource"/> + <see cref="UpdateManager"/> を内部で組み立てる便利コンストラクタ。
    /// <para>
    /// セキュリティのため URL は <c>https://github.com/...</c> に限定する。GitHub Enterprise や
    /// ユーザー入力経由で動的に URL を組む場合は、こちらではなく
    /// <see cref="UpdateDialogViewModel(UpdateManager, UpdateDialogOptions?)"/> を使ってホスト側で
    /// <see cref="UpdateManager"/> を構築すること。
    /// </para>
    /// </summary>
    /// <param name="githubRepoUrl">例: <c>https://github.com/owner/repo</c></param>
    /// <param name="accessToken">プライベートリポジトリ用トークン (省略可)。</param>
    /// <param name="prerelease">プレリリースも検索対象にするか。</param>
    /// <param name="options">表示・振る舞いオプション。</param>
    /// <exception cref="ArgumentException"><paramref name="githubRepoUrl"/> が absolute URI でない、または https / github.com 以外。</exception>
    public UpdateDialogViewModel(string githubRepoUrl, string? accessToken = null, bool prerelease = false, UpdateDialogOptions? options = null)
        : this(BuildGithubManager(githubRepoUrl, accessToken, prerelease), options)
    {
    }

    private static UpdateManager BuildGithubManager(string githubRepoUrl, string? accessToken, bool prerelease)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(githubRepoUrl);
        if (!Uri.TryCreate(githubRepoUrl, UriKind.Absolute, out var uri))
            throw new ArgumentException("githubRepoUrl must be an absolute URI.", nameof(githubRepoUrl));
        if (uri.Scheme != Uri.UriSchemeHttps)
            throw new ArgumentException("githubRepoUrl must use https scheme.", nameof(githubRepoUrl));
        if (!uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException(
                "githubRepoUrl host must be github.com. For GitHub Enterprise, use the UpdateManager-injection constructor.",
                nameof(githubRepoUrl));
        return new UpdateManager(new GithubSource(githubRepoUrl, accessToken ?? string.Empty, prerelease));
    }

    // ---------------- 公開プロパティ ----------------

    /// <summary>表示・振る舞いオプション (読み取り専用)。</summary>
    public UpdateDialogOptions Options { get; }

    /// <summary>解決された文字列セット。XAML バインディング用。</summary>
    public IUpdateDialogStrings Strings => Options.ResolvedStrings;

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

    /// <summary>
    /// 進捗表示 (確認中スピナー) を出す状態。<see cref="UpdateState.Idle"/> と
    /// <see cref="UpdateState.Checking"/> の両方を含む。
    /// <para>
    /// Idle をここに含めるのは「描画するものが 1 つも無い状態」を作らないため。
    /// Idle 用の専用 UI を持たせずに空ウィンドウを防ぐ安全網であり、XAML の確認中パネルは
    /// <see cref="IsChecking"/> ではなくこちらにバインドする。
    /// </para>
    /// </summary>
    public bool IsPreparing => State is UpdateState.Idle or UpdateState.Checking;

    /// <summary>State == Available</summary>
    public bool IsAvailable => State == UpdateState.Available;

    /// <summary>State == UpToDate</summary>
    public bool IsUpToDate => State == UpdateState.UpToDate;

    /// <summary>State == Downloading</summary>
    public bool IsDownloading => State == UpdateState.Downloading;

    /// <summary>State == Failed</summary>
    public bool IsFailed => State == UpdateState.Failed;

    partial void OnStateChanged(UpdateState value)
    {
        log.InfoFormat("State changed to {0}", value);
        OnPropertyChanged(nameof(IsChecking));
        OnPropertyChanged(nameof(IsPreparing));
        OnPropertyChanged(nameof(IsAvailable));
        OnPropertyChanged(nameof(IsUpToDate));
        OnPropertyChanged(nameof(IsDownloading));
        OnPropertyChanged(nameof(IsFailed));
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
        if (IsDisposed())
            return;

        FinalError = ex;
        ErrorMessage = ex.InnerException?.Message ?? ex.Message;
        State = UpdateState.Failed;
        log.Error("Update flow failed", ex);
        Options.RaiseErrorOccurred(ex);
    }

    /// <summary>
    /// GitHub Release を確認し、結果に応じて Available / UpToDate / Failed へ遷移する。
    /// </summary>
    /// <param name="manualCheck">true ならユーザー主導の手動チェック (最新であってもダイアログを残す)。</param>
    /// <param name="cancellationToken">チェックをキャンセルする際のトークン。</param>
    public Task CheckAsync(bool manualCheck = false, CancellationToken cancellationToken = default)
    {
        return CheckCoreAsync(cancellationToken, CancellationToken.None);
    }

    internal Task CheckForWindowAsync(
        CancellationToken cancellationToken,
        CancellationToken windowLifetimeToken)
    {
        return CheckCoreAsync(cancellationToken, windowLifetimeToken);
    }

    private async Task CheckCoreAsync(
        CancellationToken cancellationToken,
        CancellationToken windowLifetimeToken)
    {
        // 再入防止は State ではなく専用フラグで持つ。State に依存させると
        // 「キャンセル後に State を Idle へ戻さないとガードが解けない」制約が生まれ、
        // 表示中のウィンドウが描画物ゼロの Idle に落ちる (= のっぺらぼう) 原因になる。
        if (_checkInFlight || State == UpdateState.Downloading)
            return;

        _checkInFlight = true;
        State = UpdateState.Checking;

        using var linkedCts = windowLifetimeToken.CanBeCanceled
            ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, windowLifetimeToken)
            : null;
        var operationToken = linkedCts?.Token ?? cancellationToken;
        Task<UpdateInfo?>? checkTask = null;

        try
        {
            // 開発環境 (未インストール) では「最新」扱いにする
            if (!_manager.IsInstalled)
            {
                SetUpToDate();
                return;
            }

            operationToken.ThrowIfCancellationRequested();
            checkTask = _manager.CheckForUpdatesAsync();
            var info = await checkTask.WaitAsync(operationToken).ConfigureAwait(true);
            operationToken.ThrowIfCancellationRequested();

            if (info is null)
            {
                SetUpToDate();
                return;
            }

            SetAvailable(info);
        }
        catch (OperationCanceledException) when (operationToken.IsCancellationRequested)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                FinalOutcome = UpdateOutcome.Cancelled;
            }

            // Velopack 1.2.0 の更新確認 API は CancellationToken を受け取らない。
            // WaitAsync で呼び出し側の待機だけを終了し、残った通信タスクは例外が
            // 未観測にならないよう完了まで観測する。キャンセル後の結果は UI へ反映しない。
            if (checkTask is { IsCompleted: false })
            {
                _ = ObserveDetachedCheckAsync(checkTask);
            }

            // State は Checking のまま維持する。状態を巻き戻すと、Window が
            // 閉じ切るまでの間だけ空ダイアログが露出するため。
            // 再入ガードは finally の _checkInFlight 解除で解ける。
            throw;
        }
        catch (Exception ex)
        {
            // SetFailed が ErrorOccurred を発火するため、ここでは追加 raise しない
            SetFailed(ex);
        }
        finally
        {
            _checkInFlight = false;
        }
    }

    private static async Task ObserveDetachedCheckAsync(Task checkTask)
    {
        try
        {
            await checkTask.ConfigureAwait(false);
        }
        catch (Exception)
        {
            // 呼び出し側では既にキャンセル済み。遅延完了した例外を観測するだけに留める。
        }
    }

    /// <summary>
    /// 利用可能な更新をダウンロードし、完了後にアプリを再起動する。
    /// </summary>
    public Task DownloadAndApplyAsync()
    {
        lock (_downloadGate)
        {
            if (_disposed || _suppressDownloadCallbacks)
                return Task.CompletedTask;

            if (_downloadTask is { IsCompleted: false })
                return _downloadTask;

            if (State == UpdateState.Downloading)
                return Task.CompletedTask;

            // _updateInfo 未設定で Available 状態になっている異常系 (SetAvailable を経ず State を直接代入された等)。
            // 黙って no-op にするとダウンロードボタンが無反応に見えるため、ログに残して顕在化する。
            if (_updateInfo is null)
            {
                log.InfoFormat("DownloadAndApplyAsync was called without UpdateInfo. Reach Available via CheckAsync()/SetAvailable() first.");
                return Task.CompletedTask;
            }

            // 起動準備と Task の公開を close / Dispose と同じ gate 内で完了させる。
            // CTS の破棄は Velopack が token を使い終えた後のタスク側 finally が担う。
            var info = _updateInfo;
            var cts = new CancellationTokenSource();
            _downloadCts = cts;
            State = UpdateState.Downloading;
            DownloadProgress = 0;
            _downloadTask = Task.Run(() => DownloadAndApplyCoreAsync(info, cts));
            return _downloadTask;
        }
    }

    private async Task DownloadAndApplyCoreAsync(UpdateInfo info, CancellationTokenSource cts)
    {
        var token = cts.Token;
        try
        {
            await _manager.DownloadUpdatesAsync(
                info,
                p => PostDownloadCallback(() => DownloadProgress = p),
                cancelToken: token).ConfigureAwait(false);

            BeginApplyOrThrow(token);
            _applyUpdatesAndRestart(info);

            // ここに到達するのは再起動が走らなかった場合のみ (通常はプロセス終了で未到達)。
            // Apply が失敗して例外を投げたら下の catch に入るため Updated にはならない。
            PostDownloadCallback(() => FinalOutcome = UpdateOutcome.Updated);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // ダウンロードのキャンセルは「更新フロー全体のキャンセル」ではなく Available へ戻すだけ。
            // close / Dispose が先なら callback 自体を抑止する。
            PostDownloadCallback(() => State = UpdateState.Available);
        }
        catch (Exception ex)
        {
            PostDownloadCallback(() => SetFailed(ex));
        }
        finally
        {
            lock (_downloadGate)
            {
                _isApplyingUpdate = false;
                if (ReferenceEquals(_downloadCts, cts))
                {
                    _downloadCts = null;
                }

                cts.Dispose();
            }
        }
    }

    internal Task WaitForDownloadCompletionAsync()
    {
        lock (_downloadGate)
        {
            return _downloadTask ?? Task.CompletedTask;
        }
    }

    internal bool TryOnClosing()
    {
        var wasDownloading = false;
        lock (_downloadGate)
        {
            if (_isApplyingUpdate)
                return false;

            _suppressDownloadCallbacks = true;
            wasDownloading = State == UpdateState.Downloading;
            if (wasDownloading)
            {
                _downloadCts?.Cancel();
            }
        }

        if (wasDownloading)
        {
            FinalOutcome = UpdateOutcome.Cancelled;
            return true;
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

        return true;
    }

    private void BeginApplyOrThrow(CancellationToken token)
    {
        lock (_downloadGate)
        {
            token.ThrowIfCancellationRequested();
            if (_suppressDownloadCallbacks)
                throw new OperationCanceledException(token);

            _isApplyingUpdate = true;
        }
    }

    private void PostDownloadCallback(Action callback)
    {
        _postToUi(() =>
        {
            lock (_downloadGate)
            {
                if (_disposed || _suppressDownloadCallbacks)
                    return;
            }

            callback();
        });
    }

    private bool IsDisposed()
    {
        lock (_downloadGate)
        {
            return _disposed;
        }
    }

    /// <summary>ダウンロードをキャンセル。</summary>
    public void CancelDownload()
    {
        lock (_downloadGate)
        {
            _downloadCts?.Cancel();
        }
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
        _ = TryOnClosing();
    }

    /// <summary>
    /// 進行中のダウンロードをキャンセルする。
    /// Window 経由で使う場合は <see cref="UpdateDialogWindow"/> が Closed 時に呼ぶ。
    /// ViewModel を直接使うホストは破棄時に呼ぶこと。
    /// <para>
    /// <see cref="CancellationTokenSource"/> 自体の解放はダウンロードタスク側が完了時に行う。
    /// まだ Velopack が token を保持している間に解放すると
    /// <see cref="ObjectDisposedException"/> を誘発しうるため。
    /// </para>
    /// </summary>
    public void Dispose()
    {
        lock (_downloadGate)
        {
            _disposed = true;
            _suppressDownloadCallbacks = true;
            _downloadCts?.Cancel();
        }
    }

    private readonly object _downloadGate = new();
    private readonly UpdateManager _manager;
    private readonly Action<UpdateInfo> _applyUpdatesAndRestart;
    private readonly Action<Action> _postToUi;
    private UpdateInfo? _updateInfo;
    private bool _checkInFlight;
    private CancellationTokenSource? _downloadCts;
    private Task? _downloadTask;
    private bool _isApplyingUpdate;
    private bool _suppressDownloadCallbacks;
    private bool _disposed;
}
