using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using Avalonia.Media;
using System;
using System.Linq;
using System.ComponentModel;
using Avalonia.Markup.Xaml;
using WinButler.Services;
using WinButler.ViewModels;
using WinButler.Views;

namespace WinButler;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            try
            {
                var vm = new MainWindowViewModel();

                // Restore the saved target drive (never dry-run — that resets to ON every
                // launch). Only reached under the classic-desktop lifetime, so headless tests
                // never touch the real settings file.
                SettingsStore.Load(vm.Settings);

                vm.Settings.PropertyChanged += (_, e) =>
                {
                    // Persist the one preference a user expects to survive a restart (never dry-run,
                    // which resets to ON every launch).
                    if (e.PropertyName == nameof(AppSettings.TargetDrive))
                        SettingsStore.Save(vm.Settings);
                };

                desktop.MainWindow = new MainWindow
                {
                    DataContext = vm,
                };
            }
            catch (Exception ex)
            {
                // Startup must never die silently: show what went wrong instead of exiting.
                Log.Error("startup", "Failed to construct the main window.", ex);
                desktop.MainWindow = CreateStartupErrorWindow(ex);
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static Window CreateStartupErrorWindow(Exception ex) => new()
    {
        Title = "WinButler — startup error",
        Width = 560,
        Height = 320,
        Content = new TextBlock
        {
            Margin = new Thickness(24),
            TextWrapping = TextWrapping.Wrap,
            Text = "WinButler could not start.\n\n" + ex.Message +
                   "\n\nDetails were written to %APPDATA%\\WinButler\\logs\\winbutler.log.",
        },
    };
}
