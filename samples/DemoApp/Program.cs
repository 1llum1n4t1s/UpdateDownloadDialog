using Avalonia;
using Microsoft.Extensions.Logging;
using Velopack;

namespace DemoApp;

internal static class Program
{
    [System.STAThread]
    public static void Main(string[] args)
    {
        // VelopackLocator.Current を初期化するため、Avalonia 起動前に必ず呼ぶ
        VelopackApp.Build().Run();

        // VelopackUpdateDialog 内部の SuperLightLogger ログを拾うための設定。
        // これを呼ばないと NullLoggerFactory になりライブラリのログが一切出力されない。
        // 本番ホストアプリも同様にロガーを設定するとデバッグしやすい。
        SuperLightLogger.LogManager.Configure(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
        });

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
