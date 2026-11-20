using Avalonia;
using System;
using System.IO;

namespace CoffeeMC.App;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        // WinExe не показывает исключения в консоли, поэтому при падении пишем их в crash.log
        AppDomain.CurrentDomain.UnhandledException += (_, e) => WriteCrashLog(e.ExceptionObject as Exception);

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            WriteCrashLog(ex);
            throw;
        }
    }

    internal static void WriteCrashLog(Exception? ex)
    {
        var text = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}]{Environment.NewLine}{ex}{Environment.NewLine}{Environment.NewLine}";

        foreach (var dir in new[]
                 {
                     AppContext.BaseDirectory,
                     Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
                 })
        {
            try
            {
                File.AppendAllText(Path.Combine(dir, "CoffeeMC-crash.log"), text);
            }
            catch
            {
                // не получилось записать в эту папку — пробуем следующую
            }
        }

        Console.Error.WriteLine(text);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
