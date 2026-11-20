using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using CoffeeMC.App.ViewModels;
using CoffeeMC.App.Views;

namespace CoffeeMC.App;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // Необработанная ошибка в async void-обработчике кнопки больше не закрывает лаунчер:
        // пишем в crash-лог и продолжаем работу
        Avalonia.Threading.Dispatcher.UIThread.UnhandledException += (_, e) =>
        {
            Program.WriteCrashLog(e.Exception);
            e.Handled = true;
        };

        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Program.WriteCrashLog(e.Exception);
            e.SetObserved();
        };

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainViewModel(),
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}