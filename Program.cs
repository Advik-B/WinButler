using Avalonia;
using System;
using System.Threading.Tasks;
using Optris.Icons.Avalonia;
using Optris.Icons.Avalonia.MaterialDesign;
using WinButler.Services;

namespace WinButler;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        // Last-resort backstops: RunGuardedAsync in the ViewModels is the primary defense;
        // these only exist so a crash still leaves a diagnosable trace in the log.
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Log.Error("fatal", "Unhandled exception (AppDomain).", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log.Error("fatal", "Unobserved task exception.", e.Exception);
            e.SetObserved();
        };

        try
        {
            Log.Info("app", "Session start.");
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            Log.Info("app", "Session end.");
        }
        catch (Exception ex)
        {
            // Log, then rethrow so Windows Error Reporting still sees the crash.
            Log.Error("fatal", "Top-level crash.", ex);
            throw;
        }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
    {
        // Material Design vector icons (mdi-*), rendered via <i:Icon/> — replaces the old emoji glyphs.
        IconProvider.Current.Register<MaterialDesignIconProvider>();
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
    }
}
