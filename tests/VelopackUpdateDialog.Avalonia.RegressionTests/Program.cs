using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Velopack;
using Velopack.Locators;
using Velopack.Sources;

namespace VelopackUpdateDialog;

internal static class Program
{
    private static readonly TimeSpan timeout = TimeSpan.FromSeconds(5);

    public static async Task Main()
    {
        await CloseBeforeApplyPreventsRestartAsync();
        await CloseDuringDownloadCanBeDisabledAsync();
        await PreviousCancellationDoesNotBypassClosePolicyAsync();
        await CloseWinsOverCheckCancellationAsync();
        await SuccessfulRecheckClearsPreviousFailureAsync();
        DirectAvailableStateClearsPreviousFailure();
        await ApplyBeforeCloseRejectsCloseAsync();
        await DeferredApplyCompletionSurvivesCloseAsync();
        await DeferredFailureSurvivesCloseAsync();
        await DeferredApplyFailureSurvivesCloseAsync();
        await PreviousSuccessCallbacksDoNotMutateNewDownloadAsync();
        await PreviousFailureCallbackDoesNotMutateNewDownloadAsync();
        await DisposalSuppressesLateCallbacksAsync();
        await DisposalSuppressesLateCheckCallbacksAsync();
        LoggingIsReturnedToHost();

        Console.WriteLine("Download/apply lifetime, terminal outcome, and host logging regression tests passed.");
    }

    private static void LoggingIsReturnedToHost()
    {
        var entries = new List<UpdateDialogLogEntry>();
        var errorCount = 0;
        var options = new UpdateDialogOptions();
        options.LogEmitted += _ => throw new InvalidOperationException("a log sink must not break the update flow");
        options.LogEmitted += entries.Add;
        options.ErrorOccurred += _ => errorCount++;

        using var viewModel = CreateViewModel(
            new ControlledUpdateManager(),
            _ => throw new InvalidOperationException("apply must not run"),
            options);

        viewModel.SetUpToDate();
        var failure = new InvalidOperationException("host-visible failure");
        viewModel.SetFailed(failure);
        viewModel.State = UpdateState.Available;
        var noOpTask = viewModel.DownloadAndApplyAsync();

        Assert(noOpTask.IsCompletedSuccessfully, "missing UpdateInfo must remain a completed no-op");
        Assert(entries.Exists(entry =>
                entry.Level == UpdateDialogLogLevel.Information
                && entry.Message == "State changed to UpToDate."),
            "state changes must be returned to the host as information logs");
        Assert(entries.Exists(entry =>
                entry.Level == UpdateDialogLogLevel.Warning
                && entry.Message.StartsWith("DownloadAndApplyAsync was called without UpdateInfo", StringComparison.Ordinal)),
            "invalid download requests must be returned to the host as warning logs");
        Assert(entries.Exists(entry =>
                entry.Level == UpdateDialogLogLevel.Error
                && ReferenceEquals(entry.Exception, failure)),
            "failures and their exception must be returned to the host as error logs");
        Assert(errorCount == 1, "existing ErrorOccurred compatibility notification must remain once per failure");
    }

    private static async Task CloseBeforeApplyPreventsRestartAsync()
    {
        var manager = new ControlledUpdateManager();
        var applyCount = 0;
        using var viewModel = CreateViewModel(manager, _ => Interlocked.Increment(ref applyCount));
        viewModel.SetAvailable(CreateUpdateInfo());

        var downloadTask = viewModel.DownloadAndApplyAsync();
        await manager.DownloadStarted.WaitAsync(timeout);

        Assert(viewModel.TryOnClosing(), "close should win before update application starts");
        manager.CompleteDownload();
        await downloadTask.WaitAsync(timeout);

        Assert(applyCount == 0, "close-winning race must not call ApplyUpdatesAndRestart");
        Assert(viewModel.FinalOutcome == UpdateOutcome.Cancelled, "close during download must return Cancelled");
    }

    private static async Task ApplyBeforeCloseRejectsCloseAsync()
    {
        var manager = new ControlledUpdateManager();
        using var applyEntered = new ManualResetEventSlim();
        using var releaseApply = new ManualResetEventSlim();
        var applyCount = 0;
        using var viewModel = CreateViewModel(
            manager,
            _ =>
            {
                Interlocked.Increment(ref applyCount);
                applyEntered.Set();
                Assert(releaseApply.Wait(timeout), "test did not release the simulated apply call");
            });
        viewModel.SetAvailable(CreateUpdateInfo());

        var downloadTask = viewModel.DownloadAndApplyAsync();
        await manager.DownloadStarted.WaitAsync(timeout);
        manager.CompleteDownload();
        Assert(applyEntered.Wait(timeout), "update application did not start");

        Assert(!viewModel.TryOnClosing(), "close must be rejected after update application starts");
        releaseApply.Set();
        await downloadTask.WaitAsync(timeout);

        Assert(applyCount == 1, "the accepted apply path must execute exactly once");
        Assert(viewModel.FinalOutcome == UpdateOutcome.Updated, "a returning apply delegate must produce Updated");
        Assert(viewModel.State == UpdateState.UpToDate, "a returning apply delegate must leave Downloading state");
        Assert(viewModel.TryOnClosing(), "close must be accepted after a returning apply delegate completes");
        Assert(viewModel.FinalOutcome == UpdateOutcome.Updated, "close after apply completion must preserve Updated");
    }

    private static async Task CloseDuringDownloadCanBeDisabledAsync()
    {
        var manager = new ControlledUpdateManager();
        var options = new UpdateDialogOptions { AllowCloseDuringDownload = false };
        using var viewModel = CreateViewModel(manager, _ => { }, options);
        viewModel.SetAvailable(CreateUpdateInfo());

        var downloadTask = viewModel.DownloadAndApplyAsync();
        await manager.DownloadStarted.WaitAsync(timeout);

        Assert(!viewModel.TryOnClosing(), "AllowCloseDuringDownload=false must reject close in the shared ViewModel gate");
        options.AllowCloseDuringDownload = true;
        Assert(viewModel.TryOnClosing(), "enabling close must let the shared ViewModel gate cancel the download");

        manager.CompleteDownload();
        await downloadTask.WaitAsync(timeout);
        Assert(viewModel.FinalOutcome == UpdateOutcome.Cancelled, "accepted close during download must remain Cancelled");
    }

    private static async Task DeferredApplyCompletionSurvivesCloseAsync()
    {
        var manager = new ControlledUpdateManager();
        var callbacks = new Queue<Action>();
        var options = new UpdateDialogOptions { AllowCloseDuringDownload = false };
        using var viewModel = CreateViewModel(manager, _ => { }, options, callbacks.Enqueue);
        viewModel.SetAvailable(CreateUpdateInfo());

        var downloadTask = viewModel.DownloadAndApplyAsync();
        await manager.DownloadStarted.WaitAsync(timeout);
        manager.CompleteDownload();
        await downloadTask.WaitAsync(timeout);

        Assert(callbacks.Count == 1, "returning apply must queue one UI state callback");
        Assert(viewModel.FinalOutcome == UpdateOutcome.Updated, "Updated must be fixed before the queued UI callback runs");
        Assert(viewModel.State == UpdateState.Downloading, "the deferred UI callback must own the visible state transition");
        Assert(viewModel.TryOnClosing(), "terminal Updated must allow close even when active downloads cannot be closed");

        callbacks.Dequeue()();
        Assert(viewModel.FinalOutcome == UpdateOutcome.Updated, "close before the queued callback must not overwrite Updated");
    }

    private static async Task PreviousCancellationDoesNotBypassClosePolicyAsync()
    {
        var manager = new ControlledUpdateManager();
        var options = new UpdateDialogOptions { AllowCloseDuringDownload = false };
        using var viewModel = CreateViewModel(manager, _ => { }, options);
        using var checkCts = new CancellationTokenSource();

        var checkTask = viewModel.CheckAsync(cancellationToken: checkCts.Token);
        await manager.CheckStarted.WaitAsync(timeout);
        checkCts.Cancel();
        await AssertCancelledAsync(checkTask, "caller cancellation must cancel the check wait");
        Assert(viewModel.FinalOutcome == UpdateOutcome.Cancelled, "cancelled check must expose Cancelled before reuse");
        manager.CompleteCheck(null);

        viewModel.SetAvailable(CreateUpdateInfo());
        var downloadTask = viewModel.DownloadAndApplyAsync();
        await manager.DownloadStarted.WaitAsync(timeout);

        Assert(!viewModel.TryOnClosing(), "a previous Cancelled outcome must not bypass the current download close policy");
        options.AllowCloseDuringDownload = true;
        Assert(viewModel.TryOnClosing(), "the current download must remain cancellable after the policy is enabled");

        manager.CompleteDownload();
        await downloadTask.WaitAsync(timeout);
        Assert(viewModel.FinalOutcome == UpdateOutcome.Cancelled, "closing the current download must produce Cancelled");
    }

    private static async Task CloseWinsOverCheckCancellationAsync()
    {
        var manager = new ControlledUpdateManager();
        using var viewModel = CreateViewModel(manager, _ => { });
        using var checkCts = new CancellationTokenSource();

        var checkTask = viewModel.CheckAsync(cancellationToken: checkCts.Token);
        await manager.CheckStarted.WaitAsync(timeout);
        Assert(viewModel.TryOnClosing(), "close while checking must be accepted");

        checkCts.Cancel();
        await AssertCancelledAsync(checkTask, "the caller cancellation must still finish the public check task");
        manager.CompleteCheck(null);

        Assert(viewModel.FinalOutcome == UpdateOutcome.Closed, "a late check cancellation must not overwrite a close-fixed outcome");
    }

    private static async Task SuccessfulRecheckClearsPreviousFailureAsync()
    {
        var manager = new ControlledUpdateManager();
        using var viewModel = CreateViewModel(manager, _ => { });
        var previousFailure = new InvalidOperationException("previous check failure");
        viewModel.SetFailed(previousFailure);

        var checkTask = viewModel.CheckAsync();
        await manager.CheckStarted.WaitAsync(timeout);
        Assert(viewModel.FinalOutcome == UpdateOutcome.Closed, "a new check must clear the previous terminal outcome");
        Assert(viewModel.FinalError is null, "a new check must clear the previous exception");
        Assert(viewModel.ErrorMessage is null, "a new check must clear the previous error message");

        manager.CompleteCheck(null);
        await checkTask.WaitAsync(timeout);

        Assert(viewModel.State == UpdateState.UpToDate, "a successful recheck without updates must become UpToDate");
        Assert(viewModel.TryOnClosing(), "close after a successful recheck must be accepted");
        Assert(viewModel.FinalOutcome == UpdateOutcome.UpToDate, "the current UpToDate result must replace the previous failure");
    }

    private static void DirectAvailableStateClearsPreviousFailure()
    {
        using var viewModel = CreateViewModel(new ControlledUpdateManager(), _ => { });
        viewModel.SetFailed(new InvalidOperationException("previous direct failure"));

        viewModel.SetAvailable(CreateUpdateInfo());

        Assert(viewModel.FinalOutcome == UpdateOutcome.Closed, "SetAvailable must clear the previous terminal outcome");
        Assert(viewModel.FinalError is null, "SetAvailable must clear the previous exception");
        Assert(viewModel.ErrorMessage is null, "SetAvailable must clear the previous error message");
        Assert(viewModel.TryOnClosing(), "close from the current Available state must be accepted");
        Assert(viewModel.FinalOutcome == UpdateOutcome.Closed, "Available close must not return the previous failure");
    }

    private static async Task DeferredFailureSurvivesCloseAsync()
    {
        var manager = new ControlledUpdateManager();
        var callbacks = new Queue<Action>();
        var options = new UpdateDialogOptions { AllowCloseDuringDownload = false };
        using var viewModel = CreateViewModel(
            manager,
            _ => throw new InvalidOperationException("apply must not run"),
            options,
            callbacks.Enqueue);
        viewModel.SetAvailable(CreateUpdateInfo());

        var failure = new InvalidOperationException("deferred download failure");
        var downloadTask = viewModel.DownloadAndApplyAsync();
        await manager.DownloadStarted.WaitAsync(timeout);
        manager.FailDownload(failure);
        await downloadTask.WaitAsync(timeout);

        Assert(callbacks.Count == 1, "download failure must queue one UI failure callback");
        Assert(viewModel.FinalOutcome == UpdateOutcome.Failed, "Failed must be fixed before the queued UI callback runs");
        Assert(ReferenceEquals(viewModel.FinalError, failure), "the deferred failure must be retained before UI dispatch");
        Assert(viewModel.TryOnClosing(), "terminal Failed must allow close even when active downloads cannot be closed");

        callbacks.Dequeue()();
        Assert(viewModel.FinalOutcome == UpdateOutcome.Failed, "close before the queued callback must not overwrite Failed");
        Assert(ReferenceEquals(viewModel.FinalError, failure), "close before the queued callback must preserve the failure");
    }

    private static async Task DeferredApplyFailureSurvivesCloseAsync()
    {
        var manager = new ControlledUpdateManager();
        var callbacks = new Queue<Action>();
        var options = new UpdateDialogOptions { AllowCloseDuringDownload = false };
        var failure = new InvalidOperationException("deferred apply failure");
        using var viewModel = CreateViewModel(
            manager,
            _ => throw failure,
            options,
            callbacks.Enqueue);
        viewModel.SetAvailable(CreateUpdateInfo());

        var downloadTask = viewModel.DownloadAndApplyAsync();
        await manager.DownloadStarted.WaitAsync(timeout);
        manager.CompleteDownload();
        await downloadTask.WaitAsync(timeout);

        Assert(callbacks.Count == 1, "apply failure must queue one UI failure callback");
        Assert(viewModel.FinalOutcome == UpdateOutcome.Failed, "apply failure must be fixed before the queued UI callback runs");
        Assert(ReferenceEquals(viewModel.FinalError, failure), "the apply failure must be retained before UI dispatch");
        Assert(viewModel.TryOnClosing(), "terminal apply failure must allow close even when active downloads cannot be closed");

        callbacks.Dequeue()();
        Assert(viewModel.FinalOutcome == UpdateOutcome.Failed, "close before the apply failure callback must preserve Failed");
        Assert(ReferenceEquals(viewModel.FinalError, failure), "close before the apply failure callback must preserve the exception");
    }

    private static async Task PreviousSuccessCallbacksDoNotMutateNewDownloadAsync()
    {
        var manager = new ControlledUpdateManager();
        var callbacks = new Queue<Action>();
        using var viewModel = CreateViewModel(manager, _ => { }, postToUi: callbacks.Enqueue);
        viewModel.SetAvailable(CreateUpdateInfo());

        var firstDownload = viewModel.DownloadAndApplyAsync();
        await manager.DownloadStarted.WaitAsync(timeout);
        manager.ReportProgress(42);
        manager.CompleteDownload();
        await firstDownload.WaitAsync(timeout);
        Assert(callbacks.Count == 2, "the first download must queue progress and success callbacks");

        manager.PrepareNextDownload();
        viewModel.SetAvailable(CreateUpdateInfo());
        callbacks.Dequeue()();
        Assert(viewModel.DownloadProgress == 0, "old progress must not leak into the new available operation");
        callbacks.Dequeue()();
        Assert(viewModel.State == UpdateState.Available, "old success must not replace the new available state");
        Assert(viewModel.FinalOutcome == UpdateOutcome.Closed, "old success must not replace the new operation outcome");

        var secondDownload = viewModel.DownloadAndApplyAsync();
        await manager.DownloadStarted.WaitAsync(timeout);
        Assert(viewModel.DownloadProgress == 0, "the new download must start from zero progress");
        Assert(viewModel.State == UpdateState.Downloading, "the new download must start in Downloading");

        Assert(viewModel.TryOnClosing(), "the second download must remain closable");
        manager.CompleteDownload();
        await secondDownload.WaitAsync(timeout);
    }

    private static async Task PreviousFailureCallbackDoesNotMutateNewDownloadAsync()
    {
        var manager = new ControlledUpdateManager();
        var callbacks = new Queue<Action>();
        using var viewModel = CreateViewModel(manager, _ => { }, postToUi: callbacks.Enqueue);
        viewModel.SetAvailable(CreateUpdateInfo());

        var previousFailure = new InvalidOperationException("previous download failure");
        var firstDownload = viewModel.DownloadAndApplyAsync();
        await manager.DownloadStarted.WaitAsync(timeout);
        manager.FailDownload(previousFailure);
        await firstDownload.WaitAsync(timeout);
        Assert(callbacks.Count == 1, "the first download failure must queue one callback");

        manager.PrepareNextDownload();
        viewModel.SetAvailable(CreateUpdateInfo());
        var secondDownload = viewModel.DownloadAndApplyAsync();
        await manager.DownloadStarted.WaitAsync(timeout);

        callbacks.Dequeue()();
        Assert(viewModel.State == UpdateState.Downloading, "old failure must not replace the new download state");
        Assert(viewModel.FinalOutcome == UpdateOutcome.Closed, "old failure must not replace the new download outcome");
        Assert(viewModel.FinalError is null, "old failure must not leak into the new download");
        Assert(viewModel.ErrorMessage is null, "old failure message must not leak into the new download");

        Assert(viewModel.TryOnClosing(), "the second download must remain closable after an old failure callback");
        manager.CompleteDownload();
        await secondDownload.WaitAsync(timeout);
    }

    private static async Task DisposalSuppressesLateCallbacksAsync()
    {
        var manager = new ControlledUpdateManager();
        var errorCount = 0;
        var logCount = 0;
        var options = new UpdateDialogOptions();
        options.ErrorOccurred += _ => Interlocked.Increment(ref errorCount);
        options.LogEmitted += _ => Interlocked.Increment(ref logCount);
        var viewModel = CreateViewModel(manager, _ => throw new InvalidOperationException("apply must not run"), options);
        viewModel.SetAvailable(CreateUpdateInfo());

        var downloadTask = viewModel.DownloadAndApplyAsync();
        await manager.DownloadStarted.WaitAsync(timeout);
        var trackedTask = viewModel.WaitForDownloadCompletionAsync();
        Assert(ReferenceEquals(downloadTask, trackedTask), "the ViewModel must expose its active download task for joining");
        var logCountBeforeDisposal = Volatile.Read(ref logCount);

        viewModel.Dispose();
        manager.FailDownload(new InvalidOperationException("late download failure"));
        await trackedTask.WaitAsync(timeout);

        Assert(errorCount == 0, "disposed ViewModel must suppress late ErrorOccurred callbacks");
        Assert(logCount == logCountBeforeDisposal, "disposed ViewModel must suppress late LogEmitted callbacks");
        Assert(viewModel.FinalError is null, "disposed ViewModel must not retain late failures");
        Assert(viewModel.State == UpdateState.Downloading, "disposed ViewModel must not mutate state from late callbacks");
    }

    private static async Task DisposalSuppressesLateCheckCallbacksAsync()
    {
        var manager = new ControlledUpdateManager();
        Assert(manager.IsInstalled, "controlled manager must represent an installed application");

        var logCount = 0;
        var errorCount = 0;
        var options = new UpdateDialogOptions();
        options.LogEmitted += _ => Interlocked.Increment(ref logCount);
        options.ErrorOccurred += _ => Interlocked.Increment(ref errorCount);
        var viewModel = CreateViewModel(manager, _ => throw new InvalidOperationException("apply must not run"), options);

        var checkTask = viewModel.CheckAsync();
        await manager.CheckStarted.WaitAsync(timeout);
        Assert(viewModel.State == UpdateState.Checking, "controlled check must enter Checking before disposal");
        var logCountBeforeDisposal = Volatile.Read(ref logCount);

        viewModel.Dispose();
        manager.CompleteCheck(CreateUpdateInfo());
        await checkTask.WaitAsync(timeout);

        Assert(logCount == logCountBeforeDisposal, "disposed ViewModel must suppress late check LogEmitted callbacks");
        Assert(errorCount == 0, "disposed ViewModel must suppress late check ErrorOccurred callbacks");
        Assert(viewModel.State == UpdateState.Checking, "disposed ViewModel must not apply a late check result");
        Assert(viewModel.AvailableTagName is null, "disposed ViewModel must not retain a late available version");
    }

    private static UpdateDialogViewModel CreateViewModel(
        UpdateManager manager,
        Action<UpdateInfo> apply,
        UpdateDialogOptions? options = null,
        Action<Action>? postToUi = null)
    {
        return new UpdateDialogViewModel(manager, options, apply, postToUi ?? (static callback => callback()));
    }

    private static async Task AssertCancelledAsync(Task task, string message)
    {
        try
        {
            await task.WaitAsync(timeout);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        throw new InvalidOperationException(message);
    }

    private static UpdateInfo CreateUpdateInfo()
    {
        return new UpdateInfo(
            new VelopackAsset
            {
                PackageId = "RegressionTests",
                Version = SemanticVersion.Parse("2.0.0"),
                FileName = "RegressionTests-2.0.0-full.nupkg",
            },
            false);
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed class ControlledUpdateManager : UpdateManager
    {
        private readonly TaskCompletionSource<UpdateInfo?> _checkCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _checkStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private TaskCompletionSource _downloadCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private TaskCompletionSource _downloadStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private Action<int>? _downloadProgress;

        public ControlledUpdateManager()
            : base(
                new SimpleWebSource("https://example.invalid"),
                null,
                new TestVelopackLocator("RegressionTests", "1.0.0", AppContext.BaseDirectory))
        {
        }

        public Task CheckStarted => _checkStarted.Task;

        public Task DownloadStarted => _downloadStarted.Task;

        public override async Task<UpdateInfo?> CheckForUpdatesAsync()
        {
            _checkStarted.TrySetResult();
            return await _checkCompletion.Task.ConfigureAwait(false);
        }

        public override async Task DownloadUpdatesAsync(
            UpdateInfo updates,
            Action<int>? progress = null,
            CancellationToken cancelToken = default)
        {
            _downloadStarted.TrySetResult();
            _downloadProgress = progress;
            await _downloadCompletion.Task.ConfigureAwait(false);
        }

        public void PrepareNextDownload()
        {
            if (!_downloadCompletion.Task.IsCompleted)
                throw new InvalidOperationException("the current controlled download has not completed");

            _downloadCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _downloadStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _downloadProgress = null;
        }

        public void ReportProgress(int progress)
        {
            _downloadProgress?.Invoke(progress);
        }

        public void CompleteDownload()
        {
            _downloadCompletion.TrySetResult();
        }

        public void CompleteCheck(UpdateInfo? updateInfo)
        {
            _checkCompletion.TrySetResult(updateInfo);
        }

        public void FailDownload(Exception exception)
        {
            _downloadCompletion.TrySetException(exception);
        }
    }
}
