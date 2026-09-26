using System;
using System.Drawing;
using System.Windows.Forms;
using Fluent.Core;

namespace Fluent;

/// <summary>The system tray icon and its menu: Windows' counterpart of the Mac menu bar item.</summary>
public sealed class Tray : IDisposable
{
    readonly NotifyIcon icon;
    readonly AppModel model;
    readonly App app;

    public Tray(App app, AppModel model)
    {
        this.app = app;
        this.model = model;
        var stream = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/Fluent.ico"))!.Stream;
        icon = new NotifyIcon
        {
            Icon = new Icon(stream, SystemInformation.SmallIconSize),
            Text = "Fluent",
            Visible = true,
            ContextMenuStrip = new ContextMenuStrip(),
        };
        icon.ContextMenuStrip.Opening += (_, e) => { Build(); e.Cancel = false; };
        icon.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) app.ShowMain(null); };
        model.Dictation.PhaseChanged += () => icon.Text = model.Dictation.IsLive ? "Fluent — listening" : "Fluent";
        Build();
    }

    void Build()
    {
        var m = icon.ContextMenuStrip!;
        m.Items.Clear();
        var d = model.Dictation;
        m.Items.Add(new ToolStripMenuItem(Status()) { Enabled = false });
        m.Items.Add(new ToolStripSeparator());
        var dictate = new ToolStripMenuItem(d.IsLive ? "Stop and insert" : "Start dictation") { Enabled = model.TermsAccepted };
        dictate.Click += async (_, _) =>
        {
            if (d.IsLive) { d.Stop(); return; }
            d.Start(DictationSource.Tray, await FieldFinder.FrontmostAsync());
        };
        m.Items.Add(dictate);
        if (d.IsLive) m.Items.Add("Cancel dictation", null, (_, _) => d.Cancel());
        m.Items.Add(new ToolStripMenuItem($"Hold {model.HoldKey.Label()} · {model.ToggleShortcut.Label}") { Enabled = false });
        m.Items.Add(new ToolStripSeparator());
        if (model.Snoozed) m.Items.Add("Resume Fluent", null, (_, _) => model.Resume());
        else
        {
            var snooze = new ToolStripMenuItem("Snooze");
            foreach (var c in Enum.GetValues<SnoozeChoice>()) snooze.DropDownItems.Add(c.Label(), null, (_, _) => model.Snooze(c));
            m.Items.Add(snooze);
        }
        var bubble = new ToolStripMenuItem("Floating bubble") { Checked = model.BubbleEnabled };
        bubble.Click += (_, _) => model.BubbleEnabled = !model.BubbleEnabled;
        m.Items.Add(bubble);
        m.Items.Add(new ToolStripSeparator());
        m.Items.Add("Open Fluent…", null, (_, _) => app.ShowMain(null));
        m.Items.Add("Settings…", null, (_, _) => app.ShowMain(Tab.Settings));
        m.Items.Add(new ToolStripSeparator());
        m.Items.Add("Quit Fluent", null, (_, _) => app.Quit());
    }

    string Status() =>
        !model.TermsAccepted ? "Open Fluent to get started"
        : model.Mic == Recorder.MicState.BlockedByWindows ? "Microphone blocked in Windows settings"
        : model.Mic == Recorder.MicState.NoDevice ? "No microphone found"
        : !model.HasApiKey ? "Gemini API key needed"
        : model.Snoozed ? model.SnoozeLabel
        : "Fluent is ready";

    public void ShowBalloon(string title, string text)
    {
        icon.BalloonTipTitle = title;
        icon.BalloonTipText = text;
        icon.ShowBalloonTip(4000);
    }

    public void Dispose()
    {
        icon.Visible = false;
        icon.Dispose();
    }
}
