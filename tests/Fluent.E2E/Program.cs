// End-to-end tests for Fluent for Windows, run on a real Windows machine (GitHub Actions).
//
//   Fluent.E2E.exe <path to Fluent.exe> <output dir>
//
// Starts an in-process stand-in for Gemini (Live WebSocket + batch endpoint), launches the real Fluent.exe
// with --test-hooks and a fake microphone (a WAV played in real time), then drives it the way a person
// would: Notepad and Edge, the start/stop shortcut, the hold key, clicking the bubble, the capsule. It
// reads the target apps back through UI Automation (never through Fluent) and writes report.json,
// SUMMARY.md and screenshots. Exit code 1 when a required check fails.

using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO.Pipes;
using System.Net.WebSockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using FlaUI.UIA3;
using Fluent.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

static class E2E
{
    record Check(string Name, bool Pass, string Detail, bool Required);
    static readonly List<Check> Checks = [];
    static string Out = ".";
    static readonly UIA3Automation A = new();
    const string Words = "sounds good are you free for lunch tomorrow let's do twelve if that works";

    static void Record(string name, bool pass, string detail = "", bool required = true)
    {
        Checks.Add(new(name, pass, detail, required));
        Console.WriteLine($"{(pass ? "PASS" : required ? "FAIL" : "note")}  {name}  {detail}");
    }

    [DllImport("user32.dll")] static extern bool SetProcessDpiAwarenessContext(IntPtr v);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);

    [STAThread]
    static int Main(string[] args)
    {
        SetProcessDpiAwarenessContext(new IntPtr(-4));
        var exe = args[0];
        Out = Directory.CreateDirectory(args[1]).FullName;
        var data = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "fluent-e2e-" + Guid.NewGuid().ToString("N")[..8])).FullName;
        var mock = new Mock();
        var wav = Path.Combine(data, "speech.wav");
        File.WriteAllBytes(wav, Wav.Encode(Enumerable.Range(0, 16000 * 4)
            .Select(i => (short)(6000 * Math.Sin(2 * Math.PI * 220 * i / 16000.0) * (0.6 + 0.4 * Math.Sin(i / 1600.0)))).ToArray()));

        Process? fluent = null;
        try
        {
            fluent = Process.Start(new ProcessStartInfo(exe,
                $"--test-hooks --fake-mic \"{wav}\" --live-url {mock.LiveUrl} --batch-url {mock.BatchUrl} --data-dir \"{data}\"") { UseShellExecute = false });
            var up = WaitFor(() => Pipe("state") is not null, 20000);
            Record("Fluent starts and answers", up);
            if (!up) return Finish(1);
            Shot("01-first-run-window");
            Pipe("terms"); Pipe("setup"); Pipe("key AIzaTESTKEYNOTREAL0000"); Pipe("hide");

            NotepadTests(mock);
            EdgeTests();
            Pipe("quit");
            Record("Fluent quits cleanly", fluent.WaitForExit(8000), "", required: false);
        }
        catch (Exception e)
        {
            Record("no crash in the test driver", false, e.ToString());
        }
        finally
        {
            try { if (fluent is { HasExited: false }) fluent.Kill(); } catch { }
            foreach (var n in new[] { "notepad", "msedge" }) foreach (var p in Process.GetProcessesByName(n)) try { p.Kill(); } catch { }
            var log = Path.Combine(data, "fluent.log");
            if (File.Exists(log)) File.Copy(log, Path.Combine(Out, "fluent.log"), true);
            File.WriteAllText(Path.Combine(Out, "mock.json"), JsonSerializer.Serialize(mock.Sessions, new JsonSerializerOptions { WriteIndented = true }));
        }
        return Finish(Checks.Any(c => c.Required && !c.Pass) ? 1 : 0);
    }

    // Notepad: shortcut, bubble click, hold key, batch fallback, style, clipboard kept

    static void NotepadTests(Mock mock)
    {
        var np = Process.Start("notepad.exe");
        var win = WaitForWindow(w => w.Name.Contains("Notepad", StringComparison.OrdinalIgnoreCase), 15000);
        Record("Notepad opens", win is not null);
        if (win is null) return;
        win.SetForeground();
        var edit = WaitForValue(() => win.FindFirstDescendant(cf => cf.ByControlType(ControlType.Document))
                                      ?? win.FindFirstDescendant(cf => cf.ByControlType(ControlType.Edit)), 5000);
        Record("Notepad text area found", edit is not null, edit?.ControlType.ToString() ?? "");
        if (edit is null) return;
        edit.Focus();
        Thread.Sleep(1200);

        var st = State();
        Record("Fluent sees the Notepad text box", st?["fieldKind"]?.GetValue<string>() == "Editable",
            $"process={st?["fieldProcess"]} type={st?["fieldType"]} kind={st?["fieldKind"]}");
        var bubbleUp = WaitFor(() => State()?["bubbleVisible"]?.GetValue<bool>() == true, 4000);
        st = State();
        Record("bubble appears next to the text box", bubbleUp, $"bubble={st?["bubble"]?.ToJsonString()} field={st?["field"]?.ToJsonString()}");
        Shot("02-notepad-bubble");

        SetClipboard("ORIGINAL CLIPBOARD");

        // 1. Start/stop shortcut (Ctrl+Alt+Space).
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.ALT, VirtualKeyShort.SPACE);
        var recording = WaitFor(() => State()?["phase"]?.GetValue<string>() == "Recording", 3000);
        Thread.Sleep(600);
        st = State();
        Record("shortcut starts a dictation", recording, $"phase={st?["phase"]}");
        Record("capsule shows while recording", st?["capsuleVisible"]?.GetValue<bool>() == true, st?["capsule"]?.ToJsonString() ?? "");
        Record("bubble hides while the capsule owns the session", st?["bubbleVisible"]?.GetValue<bool>() == false, "", required: false);
        Shot("03-capsule-listening");
        Thread.Sleep(2600);
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.ALT, VirtualKeyShort.SPACE);
        var text1 = WaitForValue(() => { var t = ReadText(edit); return t.Contains("works") ? t : null; }, 10000);
        st = State();
        Shot("04-notepad-after-shortcut");
        Record("shortcut dictation lands in Notepad", text1?.Trim() == Words, $"text=\"{text1 ?? ReadText(edit)}\" route={st?["lastRoute"]} insert={st?["lastInsert"]}");
        Record("transcript came over Gemini Live (streamed)", st?["lastRoute"]?.GetValue<string>() == "live", $"latency={st?["latencyMs"]} ms");
        Record("latency after stop under 2 s", (st?["latencyMs"]?.GetValue<double>() ?? 99999) < 2000, $"{st?["latencyMs"]} ms");
        Thread.Sleep(900);
        Record("user's clipboard is put back", GetClipboard() == "ORIGINAL CLIPBOARD", $"clipboard=\"{GetClipboard()}\"");
        Record("Notepad kept the focus", ForegroundIs("notepad"), "");

        // 2. Click the bubble, talk, click again.
        WaitFor(() => State()?["bubbleVisible"]?.GetValue<bool>() == true, 4000);
        st = State();
        if (st?["bubble"] is JsonArray b)
        {
            var center = new Point(b[0]!.GetValue<int>() + b[2]!.GetValue<int>() / 2, b[1]!.GetValue<int>() + b[3]!.GetValue<int>() / 2);
            Mouse.Click(center);
            var rec = WaitFor(() => State()?["phase"]?.GetValue<string>() == "Recording", 3000);
            Record("bubble click starts a dictation", rec);
            Record("bubble click does not steal focus", ForegroundIs("notepad"), "");
            Thread.Sleep(2600);
            // The bubble hides while the capsule is up, so stop from the capsule's Stop button.
            var capsule = WaitForWindow(w => w.Name == "Fluent recording", 3000);
            var stopButton = capsule?.FindFirstDescendant(cf => cf.ByName("Stop and insert"));
            Record("capsule Stop button reachable", stopButton is not null);
            if (stopButton is not null) Mouse.Click(stopButton.GetClickablePoint());
            var text2 = WaitForValue(() => { var t = ReadText(edit); return t.Length > (text1?.Length ?? 0) + 10 ? t : null; }, 10000);
            Record("capsule Stop inserts, spaced after the earlier text", text2?.TrimEnd() == Words + " " + Words, $"text=\"{text2 ?? ReadText(edit)}\"");
            Record("Notepad still focused after the capsule click", ForegroundIs("notepad"), "");
        }
        else Record("bubble click starts a dictation", false, "no bubble");
        Shot("05-notepad-after-bubble");

        // 3. Hold Right Ctrl.
        var before = ReadText(edit);
        Keyboard.Press(VirtualKeyShort.RCONTROL);
        Thread.Sleep(2800);
        Keyboard.Release(VirtualKeyShort.RCONTROL);
        var text3 = WaitForValue(() => { var t = ReadText(edit); return t.Length > before.Length + 10 ? t : null; }, 10000);
        Record("holding Right Ctrl dictates and inserts on release", text3?.TrimEnd().EndsWith(Words + " " + Words) == true && text3.Length > before.Length,
            $"text=\"{text3 ?? ReadText(edit)}\"");

        // 4. Batch fallback + Style: the live socket is refused, the whole recording goes in one request.
        mock.RejectLive = true;
        Pipe("style other excited");
        edit.Focus();
        Thread.Sleep(400);
        SendCtrlEnd();
        before = ReadText(edit);
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.ALT, VirtualKeyShort.SPACE);
        Thread.Sleep(1800);
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.ALT, VirtualKeyShort.SPACE);
        var text4 = WaitForValue(() => { var t = ReadText(edit); return t.Contains("Batch") ? t : null; }, 12000);
        st = State();
        Record("rejected live socket falls back to the batch request", st?["lastRoute"]?.GetValue<string>() == "batch" && mock.BatchHits > 0,
            $"route={st?["lastRoute"]} batchHits={mock.BatchHits}");
        Record("Style applies (Other → Excited turns the full stop into !)", text4?.TrimEnd().EndsWith("Batch fallback works!") == true, $"text=\"{text4 ?? ReadText(edit)}\"");
        mock.RejectLive = false;
        Pipe("style other formal");

        // 5. Esc cancels: nothing is inserted.
        before = ReadText(edit);
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.ALT, VirtualKeyShort.SPACE);
        Thread.Sleep(1200);
        Keyboard.Type(VirtualKeyShort.ESCAPE);
        Thread.Sleep(2500);
        Record("Esc cancels without inserting", ReadText(edit) == before && State()?["phase"]?.GetValue<string>() == "Idle", $"phase={State()?["phase"]}");

        try { np?.Kill(); } catch { }
        foreach (var p in Process.GetProcessesByName("notepad")) try { p.Kill(); } catch { }
    }

    static void SendCtrlEnd() => Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.END);

    // Edge: textarea, input, password, contenteditable (web apps: Gmail, WhatsApp Web, ChatGPT, Claude…)

    static void EdgeTests()
    {
        var edge = new[] { @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe", @"C:\Program Files\Microsoft\Edge\Application\msedge.exe" }
            .FirstOrDefault(File.Exists);
        if (edge is null) { Record("Edge available", false, "not installed", required: false); return; }
        var page = Path.Combine(Path.GetTempPath(), "fluent-e2e.html");
        File.WriteAllText(page, """
            <!doctype html><html><head><title>Fluent test page</title><style>body{font:16px Segoe UI;margin:40px} textarea,input,div{display:block;width:520px;margin:14px 0;padding:8px;font:16px Segoe UI} textarea{height:90px} div{border:1px solid #999;min-height:60px}</style></head>
            <body><textarea id="t" aria-label="Message" placeholder="Message"></textarea><input id="i" aria-label="Subject" value="Re:"><input id="p" type="password" aria-label="Password"><div id="c" contenteditable="true" aria-label="Editor" role="textbox"></div></body></html>
            """);
        var profile = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "fluent-edge-" + Guid.NewGuid().ToString("N")[..6])).FullName;
        Process.Start(new ProcessStartInfo(edge, $"--user-data-dir=\"{profile}\" --no-first-run --no-default-browser-check --disable-features=msEdgeFRE,EdgeCollections --start-maximized \"file:///{page.Replace('\\', '/')}\"") { UseShellExecute = false });
        var win = WaitForWindow(w => w.Name.Contains("Fluent test page"), 25000);
        Record("Edge opens the test page", win is not null, "", required: false);
        if (win is null) return;
        win.SetForeground();
        Thread.Sleep(1500);

        void Field(string name, string say, string expected, bool required)
        {
            var el = WaitForValue(() => win.FindFirstDescendant(cf => cf.ByName(name)), 8000);
            if (el is null) { Record($"Edge: {name} found", false, "", required); return; }
            el.Click();
            Thread.Sleep(1200);
            var st = State();
            Record($"Edge {name}: Fluent sees an editable box", st?["fieldKind"]?.GetValue<string>() == "Editable",
                $"type={st?["fieldType"]} kind={st?["fieldKind"]} bubble={st?["bubbleVisible"]}", required);
            var r = Pipe("insert " + say);
            Thread.Sleep(400);
            var got = ReadText(el);
            Record($"Edge {name}: text inserted", got.Trim() == expected, $"insert={r} text=\"{got}\"", required);
        }

        Field("Message", "Hello from Fluent", "Hello from Fluent", required: true);
        Shot("06-edge-textarea");
        Field("Subject", "lunch tomorrow", "Re: lunch tomorrow", required: true);
        Field("Editor", "Typed into a rich editor", "Typed into a rich editor", required: false);

        var pw = win.FindFirstDescendant(cf => cf.ByName("Password"));
        if (pw is not null)
        {
            pw.Click();
            Thread.Sleep(1300);
            var st = State();
            Record("password field: no bubble", st?["bubbleVisible"]?.GetValue<bool>() == false, $"kind={st?["fieldKind"]}");
            var r = Pipe("insert secret words");
            Record("password field: Fluent refuses to type", r?["kind"]?.GetValue<string>() == "Failed", r?.ToJsonString() ?? "");
            Shot("07-edge-password");
        }
    }

    // helpers

    static string ReadText(AutomationElement el)
    {
        try
        {
            var v = el.Patterns.Value.PatternOrDefault?.Value.ValueOrDefault;
            if (v is not null) return v.Replace("\r\n", "\n").Replace("\r", "\n");
            var t = el.Patterns.Text.PatternOrDefault?.DocumentRange.GetText(-1);
            if (t is not null) return t.Replace("\r\n", "\n").Replace("\r", "\n");
            return el.Name ?? "";
        }
        catch { return ""; }
    }

    static bool ForegroundIs(string process)
    {
        GetWindowThreadProcessId(GetForegroundWindow(), out var pid);
        try { return Process.GetProcessById((int)pid).ProcessName.Contains(process, StringComparison.OrdinalIgnoreCase); } catch { return false; }
    }

    static Window? WaitForWindow(Func<Window, bool> match, int ms) =>
        WaitForValue(() => A.GetDesktop().FindAllChildren().Select(e => e.AsWindow()).FirstOrDefault(w => { try { return match(w); } catch { return false; } }), ms);

    static bool WaitFor(Func<bool> f, int ms)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < ms) { try { if (f()) return true; } catch { } Thread.Sleep(150); }
        return false;
    }

    static T? WaitForValue<T>(Func<T?> f, int ms) where T : class
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < ms) { try { if (f() is { } v) return v; } catch { } Thread.Sleep(150); }
        return null;
    }

    static JsonObject? State() => Pipe("state");

    static JsonObject? Pipe(string command)
    {
        try
        {
            using var p = new NamedPipeClientStream(".", "FluentTestHooks", PipeDirection.InOut);
            p.Connect(2000);
            var w = new StreamWriter(p, new UTF8Encoding(false)) { AutoFlush = true };
            w.WriteLine(command);
            var line = new StreamReader(p, Encoding.UTF8).ReadLine();
            return line is null ? null : JsonNode.Parse(line) as JsonObject;
        }
        catch { return null; }
    }

    static void SetClipboard(string s) { for (var i = 0; i < 5; i++) try { System.Windows.Forms.Clipboard.SetText(s); return; } catch { Thread.Sleep(100); } }
    static string? GetClipboard() { for (var i = 0; i < 5; i++) try { return System.Windows.Forms.Clipboard.GetText(); } catch { Thread.Sleep(100); } return null; }

    static void Shot(string name)
    {
        try
        {
            var b = System.Windows.Forms.Screen.PrimaryScreen!.Bounds;
            using var bmp = new Bitmap(b.Width, b.Height);
            using (var g = Graphics.FromImage(bmp)) g.CopyFromScreen(b.Location, Point.Empty, b.Size);
            bmp.Save(Path.Combine(Out, $"e2e-{name}.png"), ImageFormat.Png);
        }
        catch (Exception e) { Console.WriteLine("screenshot failed: " + e.Message); }
    }

    static int Finish(int code)
    {
        File.WriteAllText(Path.Combine(Out, "report.json"), JsonSerializer.Serialize(Checks, new JsonSerializerOptions { WriteIndented = true }));
        var sb = new StringBuilder();
        var failed = Checks.Count(c => c.Required && !c.Pass);
        sb.AppendLine($"## End-to-end on Windows: {Checks.Count(c => c.Pass)}/{Checks.Count} passed, {failed} required failure(s)");
        foreach (var c in Checks) sb.AppendLine($"- {(c.Pass ? "✅" : c.Required ? "❌" : "⚠️")} {c.Name}{(c.Detail.Length > 0 ? " — " + c.Detail.Replace("\n", " ") : "")}");
        File.WriteAllText(Path.Combine(Out, "E2E.md"), sb.ToString());
        Console.WriteLine(sb);
        return code;
    }
}

/// <summary>Gemini stand-in: Live WebSocket at /ws (setupComplete after 0.3 s, a final chunk per second of
/// audio, the rest plus turnComplete after audioStreamEnd) and the batch endpoint at /batch.</summary>
sealed class Mock
{
    static readonly string[] W = "sounds good are you free for lunch tomorrow let's do twelve if that works".Split(' ');
    public string LiveUrl { get; }
    public string BatchUrl { get; }
    public volatile bool RejectLive;
    public int BatchHits;
    public List<JsonObject> Sessions { get; } = [];

    public Mock()
    {
        var b = WebApplication.CreateSlimBuilder();
        b.Logging.ClearProviders();
        b.WebHost.UseUrls("http://127.0.0.1:0");
        var app = b.Build();
        app.UseWebSockets();
        app.Map("/batch", async ctx =>
        {
            Interlocked.Increment(ref BatchHits);
            using var r = new StreamReader(ctx.Request.Body);
            var body = await r.ReadToEndAsync();
            lock (Sessions) Sessions.Add(new JsonObject { ["batch"] = true, ["bytes"] = body.Length, ["key"] = ctx.Request.Headers["x-goog-api-key"].ToString().Length > 0 });
            await ctx.Response.WriteAsync("""{"steps":[{"content":[{"type":"text","text":"Batch fallback works."}]}]}""");
        });
        app.Map("/ws", async ctx =>
        {
            if (RejectLive) { ctx.Response.StatusCode = 403; await ctx.Response.WriteAsync("{\"error\":\"API key not valid\"}"); return; }
            using var ws = await ctx.WebSockets.AcceptWebSocketAsync();
            var rep = new JsonObject { ["live"] = true, ["order"] = new JsonArray() };
            lock (Sessions) Sessions.Add(rep);
            long bytes = 0, mark = 32000; int sent = 0;
            try
            {
                rep["setup"] = JsonNode.Parse(await Recv(ws) ?? "{}");
                await Task.Delay(300);
                await Send(ws, """{"setupComplete":{}}""");
                while (await Recv(ws) is { } raw)
                {
                    var rt = JsonNode.Parse(raw)?["realtimeInput"];
                    if (rt is null) continue;
                    if (rt["audio"] is { } a)
                    {
                        bytes += Convert.FromBase64String(a["data"]!.GetValue<string>()).Length;
                        if (bytes >= mark && sent < W.Length - 2) { mark += 32000; await Chunk(ws, string.Join(" ", W[sent..(sent + 2)])); sent += 2; }
                    }
                    foreach (var k in new[] { "activityStart", "activityEnd", "audioStreamEnd" })
                        if (rt[k] is not null) ((JsonArray)rep["order"]!).Add(k);
                    if (rt["audioStreamEnd"] is not null)
                    {
                        await Task.Delay(200);
                        await Chunk(ws, string.Join(" ", W[sent..]));
                        await Send(ws, """{"serverContent":{"turnComplete":true}}""");
                    }
                }
            }
            catch { }
            rep["audioBytes"] = bytes;
        });
        app.StartAsync().GetAwaiter().GetResult();
        var port = new Uri(app.Urls.First()).Port;
        LiveUrl = $"ws://127.0.0.1:{port}/ws";
        BatchUrl = $"http://127.0.0.1:{port}/batch";
    }

    static Task Chunk(WebSocket ws, string text) => Send(ws, new JsonObject { ["serverContent"] = new JsonObject { ["inputTranscription"] = new JsonObject { ["text"] = text } } }.ToJsonString());
    static Task Send(WebSocket ws, string s) => ws.SendAsync(Encoding.UTF8.GetBytes(s), WebSocketMessageType.Text, true, CancellationToken.None);

    static async Task<string?> Recv(WebSocket ws)
    {
        var buf = new byte[1 << 16];
        using var ms = new MemoryStream();
        while (true)
        {
            var r = await ws.ReceiveAsync(buf, CancellationToken.None);
            if (r.MessageType == WebSocketMessageType.Close) return null;
            ms.Write(buf, 0, r.Count);
            if (r.EndOfMessage) return Encoding.UTF8.GetString(ms.ToArray());
        }
    }
}
