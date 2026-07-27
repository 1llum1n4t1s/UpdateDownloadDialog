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
        Options = options ?? new UpdateDialogOptions();
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
    public async Task CheckAsync(bool manualCheck = false, CancellationToken cancellationToken = default)
    {
        // 再入防止は State ではなく専用フラグで持つ。State に依存させると
        // 「キャンセル後に State を Idle へ戻さないとガードが解けない」制約が生まれ、
        // 表示中のウィンドウが描画物ゼロの Idle に落ちる (= のっぺらぼう) 原因になる。
        if (_checkInFlight || State == UpdateState.Downloading)
            return;

        _checkInFlight = true;
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
            // State は Checking のまま維持する。ウィンドウ表示中のキャンセル
            // (外部トークン発火 / HttpClient タイムアウトの TaskCanceledException) で
            // 状態を巻き戻すと、Window が閉じ切るまでの間だけ空ダイアログが露出するため。
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

    /// <summary>
    /// 利用可能な更新をダウンロードし、完了後にアプリを再起動する。
    /// </summary>
    public Task DownloadAndApplyAsync()
    {
        if (State == UpdateState.Downloading)
            return Task.CompletedTask;

        // _updateInfo 未設定で Available 状態になっている異常系 (SetAvailable を経ず State を直接代入された等)。
        // 黙って no-op にするとダウンロードボタンが無反応に見えるため、ログに残して顕在化する。
        if (_updateInfo is null)
        {
            log.InfoFormat("DownloadAndApplyAsync was called without UpdateInfo. Reach Available via CheckAsync()/SetAvailable() first.");
            return Task.CompletedTask;
        }

        // CTS の所有権はこの後起動するダウンロードタスクにあり、破棄も finally で行う。
        // 走行中の Velopack がまだ token を保持している間に破棄すると ObjectDisposedException を
        // 誘発しうるため (CTS は関連操作の完了後に破棄するのが公式ガイダンス)、
        // Dispose() / CancelDownload() 側は Cancel だけに留める。
        var cts = new CancellationTokenSource();
        _downloadCts = cts;
        var token = cts.Token;
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

                _manager.ApplyUpdatesAndRestart(info);

                // ここに到達するのは再起動が走らなかった場合のみ (通常はプロセス終了で未到達)。
                // Apply が失敗して例外を投げたら下の catch に入るため Updated にはならず、
                // OnClosing で State=Failed から Failed が導出される。
                // 非 UI スレッドからの直接代入による race を避けるため UI スレッドで確定する。
                Dispatcher.UIThread.Post(() => FinalOutcome = UpdateOutcome.Updated);
            }
            catch (OperationCanceledException)
            {
                // ダウンロードのキャンセルは「更新フロー全体のキャンセル」ではなく Available へ戻すだけ。
                // FinalOutcome はここで確定させず OnClosing に委ねる
                // (Available のまま普通に閉じれば Closed を正しく返せる)。
                Dispatcher.UIThread.Post(() => State = UpdateState.Available);
            }
            catch (Exception ex)
            {
                Dispatcher.UIThread.Post(() => SetFailed(ex));
            }
            finally
            {
                // 参照落としと破棄を UI スレッドで直列化する。_downloadCts の読み書きは
                // CancelDownload() / Dispose() も UI スレッドから行うため、これで競合しない。
                Dispatcher.UIThread.Post(() =>
                {
                    if (ReferenceEquals(_downloadCts, cts))
                    {
                        _downloadCts = null;
                    }

                    cts.Dispose();
                });
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
        CancelDownload();
    }

    private readonly UpdateManager _manager;
    private UpdateInfo? _updateInfo;
    private bool _checkInFlight;
    private CancellationTokenSource? _downloadCts;
}
