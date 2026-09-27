// Screenshots for the "How to install Fluent for Windows" video, taken on a real Windows machine:
//
//   Fluent.E2E.exe --capture <output dir>
//
// Goes through exactly what a new user does: the live website in Edge, the download, Windows'
// SmartScreen warning, the installer, Fluent's first run (terms, microphone, key) and a first
// dictation into Notepad. Every stage saves a full-screen PNG and a line in capture.log; stages that
// don't appear on this machine are logged and skipped, never faked.

using System.Diagnostics;
using System.Runtime.InteropServices;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;

static partial class E2E
{
    static StreamWriter? capLog;
    static void Note(string s) { Console.WriteLine(s); capLog?.WriteLine($"{DateTime.Now:HH:mm:ss} {s}"); capLog?.Flush(); }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public short dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
        public int dmFields, dmPositionX, dmPositionY, dmDisplayOrientation, dmDisplayFixedOutput;
        public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public short dmLogPixels;
        public int dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
        public int dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
    }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool EnumDisplaySettings(string? dev, int mode, ref DEVMODE dm);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int ChangeDisplaySettings(ref DEVMODE dm, int flags);

    static void TryResolution(int w, int h)
    {
        var dm = new DEVMODE { dmSize = (short)Marshal.SizeOf<DEVMODE>() };
        EnumDisplaySettings(null, -1, ref dm);
        dm.dmPelsWidth = w; dm.dmPelsHeight = h; dm.dmFields = 0x80000 | 0x100000;
        var r = ChangeDisplaySettings(ref dm, 0);
        Note($"resolution {w}x{h}: result {r}; screen now {System.Windows.Forms.Screen.PrimaryScreen!.Bounds.Size}");
    }

    static int RunCapture(string[] args)
    {
        try { return Capture(args); }
        finally
        {
            foreach (var n in new[] { "msedge", "notepad", "Fluent", "Fluent-Setup-1.0.0" })
                foreach (var pr in Process.GetProcessesByName(n)) try { pr.Kill(); } catch { }
            capLog?.Dispose();
        }
    }

    static int Capture(string[] args)
    {
        Out = Directory.CreateDirectory(args[0]).FullName;
        capLog = new StreamWriter(Path.Combine(Out, "capture.log"));
        TryResolution(1920, 1080);
        Thread.Sleep(1500);

        Mock.W = "Hey, are you free for lunch tomorrow? Let's do 12 if that works.".Split(' ');
        var mock = new Mock();
        var dl = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        foreach (var f in Directory.GetFiles(dl, "Fluent-Setup*")) File.Delete(f);

        // 1. The website in Edge.
        var edgeExe = new[] { @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe", @"C:\Program Files\Microsoft\Edge\Application\msedge.exe" }.First(File.Exists);
        var profile = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "edge-cap")).FullName;
        Process.Start(new ProcessStartInfo(edgeExe, $"--user-data-dir=\"{profile}\" --no-first-run --no-default-browser-check --start-maximized " +
            "--disable-features=msEdgeFRE,EdgeCollections,msUndersideButton,msHubApps https://fluent-voice-v2.vercel.app/") { UseShellExecute = true });
        var edge = WaitForWindow(w => w.Name.Contains("Fluent") && w.Name.Contains("Edge"), 40000);
        Note($"edge window: {edge?.Name}");
        if (edge is null) return 1;
        edge.SetForeground();
        Thread.Sleep(4000);
        Shot("01-site-top");

        var link = WaitForValue(() => edge.FindFirstDescendant(cf => cf.ByName("Download Fluent for Windows Installer · 68 MB"))
                                    ?? edge.FindAllDescendants(cf => cf.ByControlType(ControlType.Hyperlink))
                                           .FirstOrDefault(e => (e.Name ?? "").StartsWith("Download Fluent for Windows")), 8000);
        Note($"hero windows link: {link?.Name}");
        if (link is null) return 1;
        try { link.Patterns.ScrollItem.PatternOrDefault?.ScrollIntoView(); } catch { }
        Thread.Sleep(1200);
        var p = link.GetClickablePoint();
        Mouse.MoveTo(p); Thread.Sleep(700);
        Shot("02-site-hover");
        Mouse.Click(p);
        Note("clicked download");

        // 2. Edge's download list. Edge may first ask whether to keep an uncommon file.
        Thread.Sleep(2500);
        Shot("03-edge-downloading");
        // Edge holds a new, rarely downloaded .exe back ("isn't commonly downloaded"): the real way through is
        // hover the item, "More actions" (…), Keep, then "Show more" and "Keep anyway" in the dialog.
        var warn = WaitForValue(() => edge.FindFirstDescendant(cf => cf.ByName("Downloads"))?.FindAllDescendants()
            .FirstOrDefault(e => (e.Name ?? "").Contains("isn't commonly downloaded")), 20000);
        Note($"edge warning: {warn?.Name}");
        if (warn is not null)
        {
            Shot("03b-edge-warning");
            Mouse.MoveTo(warn.GetClickablePoint()); Thread.Sleep(800);
            var more = WaitForValue(() => edge.FindFirstDescendant(cf => cf.ByName("More actions")), 4000);
            Note($"more actions: {more is not null}");
            if (more is not null)
            {
                Mouse.MoveTo(more.GetClickablePoint()); Thread.Sleep(500);
                Shot("03c-edge-more-hover");
                more.Click(); Thread.Sleep(900);
                Shot("03d-edge-menu");
                var keep = WaitForValue(() => edge.FindFirstDescendant(cf => cf.ByName("Keep").And(cf.ByControlType(ControlType.MenuItem)))
                                              ?? edge.FindFirstDescendant(cf => cf.ByName("Keep")), 4000);
                Note($"keep: {keep is not null}");
                if (keep is not null)
                {
                    Mouse.MoveTo(keep.GetClickablePoint()); Thread.Sleep(400); Shot("03e-edge-keep-hover");
                    keep.Click(); Thread.Sleep(1500);
                    Shot("03f-edge-keep-dialog");
                    // "Keep anyway" sits under the arrow on the dialog's Delete button.
                    var del = edge.FindAllDescendants(cf => cf.ByControlType(ControlType.Button))
                        .Where(e => (e.Name ?? "").StartsWith("Delete") && !e.BoundingRectangle.IsEmpty)
                        .OrderByDescending(e => e.BoundingRectangle.Bottom).FirstOrDefault();
                    Note($"dialog delete button: {del?.Name} {del?.BoundingRectangle}");
                    if (del is not null)
                    {
                        var r = del.BoundingRectangle;
                        var arrow = new System.Drawing.Point(r.Right - 16, r.Top + r.Height / 2);
                        Mouse.MoveTo(arrow); Thread.Sleep(400); Shot("03g-edge-arrow-hover");
                        Mouse.Click(arrow); Thread.Sleep(900); Shot("03g2-edge-arrow-menu");
                    }
                    var anyway = WaitForValue(() => edge.FindFirstDescendant(cf => cf.ByName("Keep anyway")), 4000);
                    Note($"keep anyway: {anyway is not null}");
                    if (anyway is not null) { Mouse.MoveTo(anyway.GetClickablePoint()); Thread.Sleep(400); Shot("03h-edge-keep-anyway-hover"); anyway.Click(); }
                }
            }
        }
        var sw = Stopwatch.StartNew();
        string? file = null;
        while (sw.ElapsedMilliseconds < 60000)
        {
            file = Directory.GetFiles(dl, "Fluent-Setup-1.0.0.exe").FirstOrDefault();
            if (file is not null && new FileInfo(file).Length > 60_000_000 && !Directory.GetFiles(dl, "*.crdownload").Any())
            {
                var len = new FileInfo(file).Length; Thread.Sleep(700);
                if (new FileInfo(file).Length == len) break;
            }
            Thread.Sleep(500);
            file = null;
        }
        Note($"downloaded: {file}");
        Thread.Sleep(1500);
        Shot("04-edge-downloaded");
        if (file is null) return 1;

        // 3. Open it from Edge's download list, like a user would.
        var open = edge.FindFirstDescendant(cf => cf.ByName("Open file"));
        Note($"'Open file' in Edge: {open is not null}");
        if (open is not null) { Mouse.MoveTo(open.GetClickablePoint()); Thread.Sleep(500); Shot("05-edge-open-file"); open.Click(); }
        else Process.Start(new ProcessStartInfo(file) { UseShellExecute = true });

        // 4. SmartScreen, if Windows shows it here.
        var ss = WaitForWindow(w => w.Name.Contains("Windows protected your PC") || w.Name.Contains("Microsoft Defender SmartScreen")
                                    || (w.FindFirstDescendant(cf => cf.ByName("Windows protected your PC")) is not null), 12000);
        Note($"smartscreen: {ss?.Name}");
        if (ss is not null)
        {
            Thread.Sleep(800);
            Shot("06-smartscreen");
            var more = ss.FindFirstDescendant(cf => cf.ByName("More info"));
            if (more is not null) { Mouse.MoveTo(more.GetClickablePoint()); Thread.Sleep(400); Shot("06b-smartscreen-hover"); more.Click(); Thread.Sleep(1000); Shot("07-smartscreen-more"); }
            var run = WaitForValue(() => ss.FindFirstDescendant(cf => cf.ByName("Run anyway")), 4000);
            if (run is not null) { Mouse.MoveTo(run.GetClickablePoint()); Thread.Sleep(400); Shot("07b-run-anyway-hover"); run.Click(); }
        }

        // 5. The installer. (Older builds first asked "install for me only / all users".)
        var mode = WaitForWindow(w => w.Name.Contains("Select Setup Install Mode"), 5000);
        mode?.FindFirstDescendant(cf => cf.ByName("Install for me only (recommended)"))?.Click();
        var setup = WaitForWindow(w => w.Name.Contains("Setup - Fluent"), 30000);
        Note($"installer: {setup?.Name}");
        if (setup is null)
        {
            Shot("08-no-installer");
            return 1;
        }
        setup.SetForeground();
        var pages = new[] { ("10-setup-tasks", "Next") };
        foreach (var (name, button) in pages)
        {
            Thread.Sleep(1200);
            var b = WaitForValue(() => setup.FindFirstDescendant(cf => cf.ByName(button).And(cf.ByControlType(ControlType.Button))), 8000);
            if (b is null) { Note($"no {button} on {name}"); Shot(name + "-missing"); continue; }
            Mouse.MoveTo(b.GetClickablePoint()); Thread.Sleep(400);
            Shot(name);
            b.Click();
        }
        var finish = WaitForValue(() => setup.FindFirstDescendant(cf => cf.ByName("Finish").And(cf.ByControlType(ControlType.Button))), 60000);
        Thread.Sleep(800);
        if (finish is not null) { Mouse.MoveTo(finish.GetClickablePoint()); Thread.Sleep(400); Shot("12-setup-finish"); finish.Click(); }
        Note($"finish: {finish is not null}");

        // 6. Fluent's first run, as the installer opened it.
        var fluentWin = WaitForWindow(w => w.Name == "Fluent", 20000);
        Note($"fluent window after install: {fluentWin is not null}");
        Thread.Sleep(2500);
        Shot("13-fluent-first-run");
        // The build machine has no microphone. Reopen Fluent with a stand-in microphone and Gemini so the
        // rest of the setup and the first dictation show what a real PC shows.
        foreach (var pr in Process.GetProcessesByName("Fluent")) try { pr.Kill(); pr.WaitForExit(5000); } catch { }
        var exe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Fluent", "Fluent.exe");
        var wav = Path.Combine(Path.GetTempPath(), "speech.wav");
        File.WriteAllBytes(wav, Fluent.Core.Wav.Encode(Enumerable.Range(0, 16000 * 5)
            .Select(i => (short)(7000 * Math.Sin(2 * Math.PI * 180 * i / 16000.0) * Math.Abs(Math.Sin(i / 2400.0)))).ToArray()));
        Process.Start(new ProcessStartInfo(exe, $"--test-hooks --fake-mic \"{wav}\" --live-url {mock.LiveUrl} --batch-url {mock.BatchUrl}") { UseShellExecute = false });
        fluentWin = WaitForWindow(w => w.Name == "Fluent", 20000);
        if (fluentWin is null) return 1;
        fluentWin.SetForeground();
        Thread.Sleep(2500);
        Shot("14-terms");
        var agree = fluentWin.FindFirstDescendant(cf => cf.ByName("I agree"));
        if (agree is not null) { Mouse.MoveTo(agree.GetClickablePoint()); Thread.Sleep(300); agree.Click(); Thread.Sleep(600); Shot("15-terms-checked"); }
        var cont = fluentWin.FindFirstDescendant(cf => cf.ByName("Continue"));
        if (cont is not null) { Mouse.MoveTo(cont.GetClickablePoint()); Thread.Sleep(300); cont.Click(); }
        Thread.Sleep(1500);
        Shot("16-setup");
        var box = WaitForValue(() => fluentWin.FindFirstDescendant(cf => cf.ByName("Gemini API key")), 5000);
        if (box is not null)
        {
            box.Click();
            Thread.Sleep(400);
            Note($"key box: type={box.ControlType} class={box.ClassName} enabled={box.IsEnabled} focusable={box.Properties.IsKeyboardFocusable.ValueOrDefault} rect={box.BoundingRectangle} fw={fluentWin.BoundingRectangle}");
            var hit = A.FromPoint(box.GetClickablePoint());
            Note($"element under the click point: {hit?.ControlType} class={hit?.ClassName} name='{hit?.Name}'");
            Shot("16b-key-clicked");
            try { box.Focus(); } catch (Exception e) { Note("focus() threw " + e.Message); }
            Thread.Sleep(400);
            Note($"after UIA SetFocus: {box.Properties.HasKeyboardFocus.ValueOrDefault}");
            var focused = A.FocusedElement();
            Note($"key box focused: {box.Properties.HasKeyboardFocus.ValueOrDefault}; focused element: {focused?.ControlType} '{focused?.Name}'");
            // Paste, the way people add a key they copied from AI Studio.
            SetClipboard("AIzaSyDemoKeyNotReal0000000000000000");
            Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_V);
            Thread.Sleep(600);
            var save = fluentWin.FindFirstDescendant(cf => cf.ByName("Save key"));
            Note($"after paste: Save key enabled = {save?.IsEnabled}");
            if (save is { IsEnabled: false })
            {
                Keyboard.Type("AIzaSyDemoKeyNotReal0000000000000000");
                Thread.Sleep(600);
                Note($"after typing: Save key enabled = {save?.IsEnabled}");
            }
            Shot("17-setup-key-typed");
            if (save is not null) { Mouse.MoveTo(save.GetClickablePoint()); Thread.Sleep(300); save.Click(); }
            Thread.Sleep(1200);
            Shot("18-setup-done");
        }
        var fin = fluentWin.FindFirstDescendant(cf => cf.ByName("Finish setup"));
        if (fin is not null) { Mouse.MoveTo(fin.GetClickablePoint()); Thread.Sleep(300); fin.Click(); }
        Thread.Sleep(2000);
        Shot("19-dictate-home");
        Pipe("hide");

        // 7. First dictation in Notepad: bubble, capsule, text.
        Process.Start("notepad.exe");
        var np = WaitForWindow(w => w.Name.Contains("Notepad"), 15000);
        if (np is null) return 1;
        np.SetForeground();
        var edit = WaitForValue(() => np.FindFirstDescendant(cf => cf.ByControlType(ControlType.Document)) ?? np.FindFirstDescendant(cf => cf.ByControlType(ControlType.Edit)), 5000);
        edit?.Focus();
        WaitFor(() => State()?["bubbleVisible"]?.GetValue<bool>() == true, 6000);
        Thread.Sleep(800);
        var st = State();
        Note($"bubble: {st?["bubble"]?.ToJsonString()}");
        Shot("20-notepad-bubble");
        if (st?["bubble"] is System.Text.Json.Nodes.JsonArray b2)
        {
            var c = new System.Drawing.Point(b2[0]!.GetValue<int>() + b2[2]!.GetValue<int>() / 2, b2[1]!.GetValue<int>() + b2[3]!.GetValue<int>() / 2);
            Mouse.MoveTo(c); Thread.Sleep(500);
            Shot("21-bubble-hover");
            Mouse.Click(c);
            Thread.Sleep(1500);
            Shot("22-capsule-listening");
            Thread.Sleep(2000);
            Shot("23-capsule-listening-2");
            var cap = WaitForWindow(w => w.Name == "Fluent recording", 3000);
            var stop = cap?.FindFirstDescendant(cf => cf.ByName("Stop and insert"));
            if (stop is not null)
            {
                Mouse.MoveTo(stop.GetClickablePoint()); Thread.Sleep(400);
                Shot("24-capsule-stop-hover");
                stop.Click();
                Thread.Sleep(150);
                Shot("25-capsule-writing");
            }
            WaitFor(() => edit is not null && ReadText(edit).Contains("works"), 8000);
            Thread.Sleep(200);
            Shot("26-notepad-inserted");
            Note($"notepad text: {(edit is null ? "" : ReadText(edit))}");
        }
        Pipe("quit");
        Note("capture done");
        return 0;
    }
}
