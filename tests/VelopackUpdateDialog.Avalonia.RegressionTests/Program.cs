using System;
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
        await ApplyBeforeCloseRejectsCloseAsync();
        await DisposalSuppressesLateCallbacksAsync();

        Console.WriteLine("Download/apply lifetime regression tests passed.");
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
    }

    private static async Task DisposalSuppressesLateCallbacksAsync()
    {
        var manager = new ControlledUpdateManager();
        var errorCount = 0;
        var options = new UpdateDialogOptions();
        options.ErrorOccurred += _ => Interlocked.Increment(ref errorCount);
        var viewModel = CreateViewModel(manager, _ => throw new InvalidOperationException("apply must not run"), options);
        viewModel.SetAvailable(CreateUpdateInfo());

        var downloadTask = viewModel.DownloadAndApplyAsync();
        await manager.DownloadStarted.WaitAsync(timeout);
        var trackedTask = viewModel.WaitForDownloadCompletionAsync();
        Assert(ReferenceEquals(downloadTask, trackedTask), "the ViewModel must expose its active download task for joining");

        viewModel.Dispose();
        manager.FailDownload(new InvalidOperationException("late download failure"));
        await trackedTask.WaitAsync(timeout);

        Assert(errorCount == 0, "disposed ViewModel must suppress late ErrorOccurred callbacks");
        Assert(viewModel.FinalError is null, "disposed ViewModel must not retain late failures");
        Assert(viewModel.State == UpdateState.Downloading, "disposed ViewModel must not mutate state from late callbacks");
    }

    private static UpdateDialogViewModel CreateViewModel(
        UpdateManager manager,
        Action<UpdateInfo> apply,
        UpdateDialogOptions? options = null)
    {
        return new UpdateDialogViewModel(manager, options, apply, callback => callback());
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
        private readonly TaskCompletionSource _downloadCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _downloadStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ControlledUpdateManager()
            : base(
                new SimpleWebSource("https://example.invalid"),
                null,
                new TestVelopackLocator("RegressionTests", "1.0.0", AppContext.BaseDirectory))
        {
        }

        public Task DownloadStarted => _downloadStarted.Task;

        public override async Task DownloadUpdatesAsync(
            UpdateInfo updates,
            Action<int>? progress = null,
            CancellationToken cancelToken = default)
        {
            _downloadStarted.TrySetResult();
            await _downloadCompletion.Task.ConfigureAwait(false);
        }

        public void CompleteDownload()
        {
            _downloadCompletion.TrySetResult();
        }

        public void FailDownload(Exception exception)
        {
            _downloadCompletion.TrySetException(exception);
        }
    }
}
