using Avalonia;
using Velopack;

namespace DemoApp;

internal static class Program
{
    [System.STAThread]
    public static void Main(string[] args)
    {
        // VelopackLocator.Current を初期化するため、Avalonia 起動前に必ず呼ぶ
        VelopackApp.Build().Run();
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
