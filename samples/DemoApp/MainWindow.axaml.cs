using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Velopack;
using Velopack.Sources;
using VelopackUpdateDialog;

namespace DemoApp;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // インライン プレビュー用 VM
        var inlineVm = CreateVm();
        inlineVm.AvailableTagName = "v9.9.9";
        inlineVm.State = UpdateState.Available;
        InlineView.DataContext = inlineVm;
    }

    // ---------------- ヘルパー ----------------

    private static UpdateDialogViewModel CreateVm(UpdateDialogOptions? options = null)
    {
        // SimpleFileSource は実際には更新を見つけないので、UpdateManager は薄いラッパー。
        // 状態は手動で書き換えてダイアログを表示する。
        var dir = new DirectoryInfo(Path.GetTempPath());
        var mgr = new UpdateManager(new SimpleFileSource(dir));
        return new UpdateDialogViewModel(mgr, options);
    }

    private async void ShowWithState(UpdateState state, Action<UpdateDialogViewModel>? extra = null, UpdateDialogOptions? options = null)
    {
        var vm = CreateVm(options);
        extra?.Invoke(vm);
        vm.State = state;

        var window = new UpdateDialogWindow(vm);
        await window.ShowDialog(this);

        ResultLabel.Text = $"Outcome: {vm.FinalOutcome}";
    }

    // ---------------- イベントハンドラ ----------------

    private void OnThemeChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (Application.Current is not { } app || sender is not ComboBox combo)
            return;

        app.RequestedThemeVariant = combo.SelectedIndex switch
        {
            1 => ThemeVariant.Light,
            2 => ThemeVariant.Dark,
            _ => ThemeVariant.Default,
        };
    }

    private void OnShowAvailable(object? sender, RoutedEventArgs e)
    {
        ShowWithState(UpdateState.Available, vm => vm.AvailableTagName = "v1.2.3");
    }

    private void OnShowUpToDate(object? sender, RoutedEventArgs e)
    {
        ShowWithState(UpdateState.UpToDate);
    }

    private void OnShowFailed(object? sender, RoutedEventArgs e)
    {
        ShowWithState(UpdateState.Failed, vm => vm.SetFailed(new InvalidOperationException("Network unreachable (demo)")));
    }

    private void OnShowChecking(object? sender, RoutedEventArgs e)
    {
        ShowWithState(UpdateState.Checking);
    }

    // Idle でも空ウィンドウ (のっぺらぼう) にならず、確認中表示へフォールバックすることの回帰確認用。
    private void OnShowIdle(object? sender, RoutedEventArgs e)
    {
        ShowWithState(UpdateState.Idle);
    }

    // 確認中のキャンセル経路の目視確認。ライブラリ内部では
    // 「State は Checking のまま維持 → FinalOutcome=Cancelled → ウィンドウを閉じる」と遷移するので、
    // 閉じ切るまでの間に空ダイアログが露出しないことをここで確認する。
    //
    // ShowAsync + CancellationToken で本物のキャンセルを再現できないのは、DemoApp の UpdateManager が
    // 未インストール状態 (IsInstalled=false) で、CheckAsync が token を見る前に UpToDate へ抜けるため。
    // 見え方は同じなので、Checking 表示のまま自動クローズさせて代用する。
    private async void OnShowCancelled(object? sender, RoutedEventArgs e)
    {
        var vm = CreateVm();
        vm.State = UpdateState.Checking;

        var window = new UpdateDialogWindow(vm);
        _ = CloseAfterDelayAsync(window);
        await window.ShowDialog(this);

        ResultLabel.Text = $"Outcome: {vm.FinalOutcome} (キャンセル相当)";
    }

    private static async Task CloseAfterDelayAsync(Window window)
    {
        await Task.Delay(TimeSpan.FromSeconds(1.5));
        window.Close();
    }

    private void OnShowDownloading(object? sender, RoutedEventArgs e)
    {
        ShowWithState(UpdateState.Downloading, vm =>
        {
            vm.AvailableTagName = "v1.2.3";
            vm.DownloadProgress = 35;
        });
    }

    private void OnShowResizableSystem(object? sender, RoutedEventArgs e)
    {
        var options = new UpdateDialogOptions
        {
            ResizeMode = WindowResizeMode.Resizable,
            ChromeMode = WindowChromeMode.System,
            InitialSize = new Size(600, 240),
            MinSize = new Size(400, 160),
        };
        ShowWithState(UpdateState.Available, vm => vm.AvailableTagName = "v2.0.0", options);
    }

    private void OnShowJapanese(object? sender, RoutedEventArgs e)
    {
        var options = new UpdateDialogOptions
        {
            Strings = new JapaneseStrings(),
        };
        options.VersionIgnored += tag => ResultLabel.Text = $"無視されたバージョン: {tag}";
        ShowWithState(UpdateState.Available, vm => vm.AvailableTagName = "v1.0.77", options);
    }
}

/// <summary>日本語ローカライズの例。</summary>
file sealed class JapaneseStrings : IUpdateDialogStrings
{
    public string Title => "アップデート";
    public string AvailableHeader => "新しいバージョンがあります！";
    public string DownloadAndInstall => "ダウンロードしてインストール";
    public string IgnoreThisVersion => "このバージョンを無視";
    public string UpToDateMessage => "最新版を使用中です。";
    public string ErrorHeader => "アップデート失敗";
    public string Close => "閉じる";
    public string CheckingMessage => "更新を確認中…";
}
