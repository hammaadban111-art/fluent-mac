using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Windows;
using Fluent.Core;

namespace Fluent;

/// <summary>Only with <c>--test-hooks</c> (the CI machines): a local named pipe that reports Fluent's state
/// and drives it, so the end-to-end tests can check the bubble, the capsule and insertion into real apps
/// without a microphone. One command per connection, one JSON line back.
///
///   state                       → phase, label, bubble and field rectangles, last text, route, insert
///   insert &lt;text&gt;               → runs the real TextInserter on the focused field
///   theme &lt;id&gt; | style &lt;cat&gt; &lt;style&gt; | history on|off | terms | setup | key &lt;k&gt;</summary>
static class TestHooks
{
    public const string PipeName = "FluentTestHooks";

    public static void Start(App app, AppModel model, OverlayController overlays)
    {
        Task.Run(async () =>
        {
            while (true)
            {
                try
                {
                    await using var pipe = new NamedPipeServerStream(PipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                    await pipe.WaitForConnectionAsync();
                    using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
                    var line = await reader.ReadLineAsync() ?? "";
                    var reply = await app.Dispatcher.InvokeAsync(() => HandleAsync(line, model, overlays)).Task.Unwrap();
                    var bytes = Encoding.UTF8.GetBytes(reply + "\n");
                    await pipe.WriteAsync(bytes);
                    await pipe.FlushAsync();
                    pipe.WaitForPipeDrain();
                }
                catch (Exception e) { Log.Write("test hook: " + e.Message); await Task.Delay(200); }
            }
        });
    }

    static async Task<string> HandleAsync(string line, AppModel model, OverlayController overlays)
    {
        var sp = line.IndexOf(' ');
        var cmd = sp < 0 ? line.Trim() : line[..sp];
        var arg = sp < 0 ? "" : line[(sp + 1)..];
        var d = model.Dictation;
        switch (cmd)
        {
            case "state":
            {
                var field = await FieldFinder.FrontmostAsync();
                var o = new JsonObject
                {
                    ["phase"] = d.Phase.ToString(),
                    ["label"] = d.Label,
                    ["lastText"] = d.LastText,
                    ["lastRoute"] = d.LastRoute,
                    ["latencyMs"] = d.LastLatencyMs,
                    ["lastInsert"] = d.LastInsert,
                    ["lastError"] = d.LastError?.Kind.ToString(),
                    ["capsuleVisible"] = overlays.CapsuleVisible && overlays.Capsule.IsVisible,
                    ["bubbleVisible"] = overlays.Bubble.IsVisible && overlays.Bubble.Opacity > 0,
                    ["fieldProcess"] = field?.Process,
                    ["fieldKind"] = field?.Kind.ToString(),
                    ["fieldType"] = field?.Traits.ControlType,
                    ["fieldCategory"] = field?.Category.ToString(),
                };
                if (overlays.Bubble.IsVisible) { var r = overlays.Bubble.PixelRect(); o["bubble"] = new JsonArray(r.X, r.Y, r.W, r.H); }
                if (overlays.Capsule.IsVisible) { var r = overlays.Capsule.PixelRect(); o["capsule"] = new JsonArray(r.X, r.Y, r.W, r.H); }
                if (field is not null) o["field"] = new JsonArray(field.Frame.X, field.Frame.Y, field.Frame.Width, field.Frame.Height);
                return o.ToJsonString();
            }
            case "insert":
            {
                var outcome = await TextInserter.InsertAsync(arg, null);
                return new JsonObject { ["kind"] = outcome.Kind.ToString(), ["detail"] = outcome.Detail }.ToJsonString();
            }
            case "theme": model.ThemeId = arg; return Ok();
            case "style":
            {
                var parts = arg.Split(' ');
                model.SetStyle(Enum.Parse<WritingStyle>(parts[1], true), Enum.Parse<StyleCategory>(parts[0], true));
                return Ok();
            }
            case "history": model.HistoryEnabled = arg == "on"; return Ok();
            case "terms": model.AcceptTerms(); return Ok();
            case "setup": model.SetupComplete = true; return Ok();
            case "key": model.SaveApiKey(arg); return Ok();
            case "hide": (Application.Current as App)?.MainWin?.Hide(); return Ok();
            case "quit": (Application.Current as App)?.Quit(); return Ok();
            default: return new JsonObject { ["error"] = "unknown command " + cmd }.ToJsonString();
        }
    }

    static string Ok() => "{\"ok\":true}";
}
