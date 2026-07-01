using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
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
            var vm = new MainWindowViewModel();

            ThemeService.Apply(vm.Settings.Accent);
            vm.Settings.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(AppSettings.Accent))
                    ThemeService.Apply(vm.Settings.Accent);
            };

            desktop.MainWindow = new MainWindow
            {
                DataContext = vm,
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
