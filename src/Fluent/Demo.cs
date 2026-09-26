using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Fluent.Views;

namespace Fluent;

/// <summary><c>--demo &lt;screen&gt; [--theme id] [--snap-dir dir]</c>: CI opens one screen of the real app and
/// saves a picture of it, so every screen is checked for rendering in every theme. Settings live in a
/// throwaway folder, so a demo never touches a real setup.
///
/// Screens: terms, onboarding, dictate, history, style, settings, capsule-listening, capsule-writing,
/// capsule-inserted, capsule-error, bubble.</summary>
static class Demo
{
    public static void Run(App app, string screen, AppModel model, OverlayController overlays, MainWindow window)
    {
        AmbientBackground.FixedTime = 3.0;
        switch (screen)
        {
            case "terms": break;
            case "onboarding": model.AcceptTerms(); break;
            default:
                model.AcceptTerms();
                model.SetupComplete = true;
                model.PretendReady();
                break;
        }
        switch (screen)
        {
            case "history": model.HistoryEnabled = true; model.SeedDemoHistory(); model.Tab = Tab.History; break;
            case "dictate": model.HistoryEnabled = true; model.SeedDemoHistory(); model.Tab = Tab.Dictate; break;
            case "style": model.Tab = Tab.Style; break;
            case "settings": model.Tab = Tab.Settings; break;
        }

        if (screen.StartsWith("capsule-"))
        {
            overlays.Pinned = true;
            var (phase, message) = screen switch
            {
                "capsule-writing" => (DictationPhase.Transcribing, ""),
                "capsule-inserted" => (DictationPhase.Done, "Inserted"),
                "capsule-error" => (DictationPhase.Error, Core.TranscriptionError.NoApiKey.UserMessage),
                _ => (DictationPhase.Recording, ""),
            };
            model.Dictation.ShowDemo(phase, message);
            overlays.ShowCapsule();
            SnapLater(screen, overlays.Capsule);
            return;
        }
        if (screen == "bubble")
        {
            overlays.Pinned = true;
            overlays.ShowBubble(null, (600, 400));
            SnapLater(screen, overlays.Bubble);
            return;
        }
        window.Show();
        SnapLater(screen, window);
    }

    /// <summary>Waits for layout and animation to settle, saves app-&lt;screen&gt;.png of the window itself (drawn
    /// at 2x, so the pictures stay sharp in videos) and writes &lt;screen&gt;.ready so the CI script can take its
    /// own full-screen capture too.</summary>
    static void SnapLater(string screen, Window w)
    {
        var dir = (Application.Current as App)?.Options.SnapDir;
        var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
        t.Tick += (_, _) =>
        {
            t.Stop();
            if (dir is null) return;
            try
            {
                Directory.CreateDirectory(dir);
                if (w.Content is FrameworkElement root && root.ActualWidth > 0)
                {
                    var scale = 2.0;
                    var bmp = new RenderTargetBitmap((int)(root.ActualWidth * scale), (int)(root.ActualHeight * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
                    var dv = new DrawingVisual();
                    using (var dc = dv.RenderOpen())
                    {
                        if (w is not OverlayWindow) dc.DrawRectangle((Brush)Application.Current.FindResource("FBg"), null, new Rect(0, 0, root.ActualWidth, root.ActualHeight));
                        dc.DrawRectangle(new VisualBrush(root), null, new Rect(0, 0, root.ActualWidth, root.ActualHeight));
                    }
                    bmp.Render(dv);
                    var enc = new PngBitmapEncoder();
                    enc.Frames.Add(BitmapFrame.Create(bmp));
                    using var f = File.Create(Path.Combine(dir, $"app-{screen}.png"));
                    enc.Save(f);
                }
                File.WriteAllText(Path.Combine(dir, $"{screen}.ready"), screen);
            }
            catch (Exception e)
            {
                File.WriteAllText(Path.Combine(dir, $"{screen}.error"), e.ToString());
            }
        };
        t.Start();
    }
}
