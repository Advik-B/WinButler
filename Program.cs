using Avalonia;
using System;
using System.Threading.Tasks;
using Optris.Icons.Avalonia;
using Optris.Icons.Avalonia.MaterialDesign;
using Velopack;
using WinButler.Services;

namespace WinButler;

sealed class Program
{
    // Avalonia is not initialized until StartWithClassicDesktopLifetime runs — nothing before
    // that call may touch Avalonia APIs or rely on a SynchronizationContext.
    [STAThread]
    public static void Main(string[] args)
    {
        // Must run before anything else: on Velopack's install/update/uninstall hook launches
        // it does its work and exits the process; on a normal launch it's a no-op.
        VelopackApp.Build().Run();

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

    // Also invoked by the visual designer/previewer — keep it public and parameterless.
    public static AppBuilder BuildAvaloniaApp()
    {
        // Material Design vector icons (mdi-*), rendered in XAML via <i:Icon/>.
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
