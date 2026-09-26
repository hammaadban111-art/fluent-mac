using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using Fluent.Core;
using Fluent.Views;

namespace Fluent;

/// <summary>Options read from the command line. Normal launches pass nothing (or --background from
/// "Open Fluent when I sign in"); --demo, --test-hooks and --fake-mic are for the CI machines only.</summary>
public sealed class LaunchOptions
{
    public string? Demo, Theme, SnapDir, FakeMic, LiveUrl, BatchUrl, DataDir;
    public bool Background, TestHooks;

    public LaunchOptions(string[] args)
    {
        string? V(string name) { var i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
        Demo = V("--demo"); Theme = V("--theme"); SnapDir = V("--snap-dir");
        Background = args.Contains("--background");
        TestHooks = args.Contains("--test-hooks");
        if (TestHooks) { FakeMic = V("--fake-mic"); LiveUrl = V("--live-url"); BatchUrl = V("--batch-url"); DataDir = V("--data-dir"); }
    }
}

public partial class App : Application
{
    public AppModel? Model { get; private set; }
    public LaunchOptions Options { get; private set; } = new([]);
    MainWindow? window;
    OverlayController? overlays;
    HotkeyManager? hotkeys;
    Tray? tray;
    Mutex? single;
    EventWaitHandle? showSignal;

    public string? HotkeyProblem => hotkeys?.Problem;
    public OverlayController? Overlays => overlays;
    public MainWindow? MainWin => window;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Options = new LaunchOptions(e.Args);
        DispatcherUnhandledException += (_, ex) => { Log.Write("crash: " + ex.Exception); ex.Handled = true; };
        AppDomain.CurrentDomain.UnhandledException += (_, ex) => Log.Write("fatal: " + ex.ExceptionObject);

        // One Fluent per Windows user: a second launch just brings the first one's window forward.
        if (Options.Demo is null)
        {
            var user = Environment.UserName;
            single = new Mutex(true, $@"Local\Fluent.Running.{user}", out var first);
            showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, $@"Local\Fluent.Show.{user}");
            if (!first)
            {
                showSignal.Set();
                Shutdown();
                return;
            }
            var signal = showSignal;
            new Thread(() => { while (signal.WaitOne()) Dispatcher.BeginInvoke(() => ShowMain(null)); }) { IsBackground = true }.Start();
        }

        if (Options.TestHooks)
        {
            Recorder.FakeMicPath = Options.FakeMic;
            if (Options.LiveUrl is { } l) DictationController.LiveUrlOverride = new Uri(l);
            if (Options.BatchUrl is { } b) DictationController.BatchUrlOverride = new Uri(b);
            if (Options.DataDir is { } d) { KeyStore.Dir = d; Log.Path = Path.Combine(d, "fluent.log"); }
        }

        var dataDir = Options.TestHooks && Options.DataDir is { } dd ? dd : AppModel.DataDir(Options.Demo);
        var store = new JsonFileStore(Path.Combine(dataDir, "settings.json"));
        var model = Model = new AppModel(new Settings(store), dataDir, Options.Demo);
        model.LoadCollections();
        if (Options.Theme is { } theme) { model.ThemeId = theme; model.OverlayThemeId = Settings.MatchApp; }
        ThemeResources.Apply(Resources, model.Palette);
        model.ThemeChanged += () => ThemeResources.Apply(Resources, model.Palette);

        Log.Write($"Fluent {typeof(App).Assembly.GetName().Version} starting on {Environment.OSVersion}");
        overlays = new OverlayController(model);
        window = new MainWindow(model);

        if (Options.Demo is { } demo)
        {
            Demo.Run(this, demo, model, overlays, window);
            return;
        }

        overlays.Start();
        hotkeys = new HotkeyManager(model, overlays);
        hotkeys.Install();
        tray = new Tray(this, model);
        if (Options.TestHooks) TestHooks.Start(this, model, overlays);
        if (!Options.Background) window.Show();
        else if (!model.TermsAccepted || !model.Ready) window.Show();
    }

    public void ShowMain(Tab? tab) => window?.ShowTab(tab);

    public void Quit()
    {
        Model?.Dictation.Cancel();
        hotkeys?.Dispose();
        tray?.Dispose();
        if (window is not null) window.ReallyClose = true;
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        hotkeys?.Dispose();
        tray?.Dispose();
        try { single?.ReleaseMutex(); } catch { }
        base.OnExit(e);
    }
}
