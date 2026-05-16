using System;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace VelopackUpdateDialog;

/// <summary>
/// 更新ダイアログの中身を担う <see cref="UserControl"/>。
/// 単独で利用する場合: <c>UpdateDialogView</c> を任意のウィンドウに貼り付け、
/// <c>DataContext</c> に <see cref="UpdateDialogViewModel"/> をセットする。
/// </summary>
public partial class UpdateDialogView : UserControl
{
    /// <summary>ホスト Window が閉じることを要求したときに発火。</summary>
    public event EventHandler? CloseRequested;

    /// <summary>コンストラクタ。</summary>
    public UpdateDialogView()
    {
        InitializeComponent();
    }

    private async void OnDownloadAndInstall(object? sender, RoutedEventArgs e)
    {
        if (DataContext is UpdateDialogViewModel vm)
        {
            try
            {
                await vm.DownloadAndApplyAsync().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                vm.SetFailed(ex);
            }
        }
        e.Handled = true;
    }

    private void OnIgnoreVersion(object? sender, RoutedEventArgs e)
    {
        if (DataContext is UpdateDialogViewModel vm)
            vm.IgnoreCurrentAvailable();

        CloseRequested?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }

    private void OnClose(object? sender, RoutedEventArgs e)
    {
        CloseRequested?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }
}
