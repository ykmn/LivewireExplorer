using System.Windows;
using System.Windows.Threading;
using LivewireBrowser.App.Localization;
using LivewireBrowser.App.Views;
using LivewireBrowser.Core.Logging;
using LivewireBrowser.Core.Settings;

namespace LivewireBrowser.App;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        // One Load() covers both — applied before the first log line and before any window
        // (including the splash) so everything respects the saved language/log level from
        // the very first frame. MainViewModel loads its own AppSettings instance later;
        // this early read is just to get these two applied ASAP.
        var settings = AppSettings.Load();
        Log.MinLevel = settings.LogLevel;
        Loc.Apply(settings.Language);

        Log.Info("Application starting up");

        // Splash closing on its own (× button) must not tear down the whole app before
        // the main window exists — only switch to the normal "quit on main window close"
        // policy once that window is actually up.
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        var splash = new SplashWindow();
        splash.Show();
        splash.SetProgress(0.15, Loc.Get("Str_SplashStarting"));
        await Dispatcher.Yield(DispatcherPriority.Render);

        splash.SetProgress(0.5, Loc.Get("Str_SplashLoading"));
        var mainWindow = new MainWindow();
        await Dispatcher.Yield(DispatcherPriority.Render);

        splash.SetProgress(1.0, Loc.Get("Str_SplashReady"));
        await Task.Delay(300);

        MainWindow = mainWindow;
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        mainWindow.Show();
        splash.Close();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log.Info("Application exiting");
        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error("Unhandled UI exception", e.Exception);
    }

    private void OnAppDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        Log.Error("Unhandled background exception", e.ExceptionObject as Exception);
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Log.Error("Unobserved task exception", e.Exception);
        e.SetObserved();
    }
}
