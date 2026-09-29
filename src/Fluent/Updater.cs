using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Windows.Threading;
using Fluent.Core;

namespace Fluent;

public enum UpdateStatus { Idle, Checking, UpToDate, Available, Downloading, Installing, Failed }

/// <summary>Settings → Updates and the "new version" notice. Reads the website's updates.json at start and
/// every few hours, says so once per new version (tray notification), and updates in place: download
/// the installer, check its SHA-256 against the manifest, run it silently (it closes Fluent, installs
/// over it and opens the new version).</summary>
public sealed class Updater : Observable
{
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(10) };
    static readonly TimeSpan Every = TimeSpan.FromHours(6);

    readonly AppModel model;
    readonly Action<string, string> notify;
    readonly Action quit;
    readonly DispatcherTimer timer = new() { Interval = Every };

    public Updater(AppModel model, Action<string, string> notify, Action quit)
    {
        this.model = model;
        this.notify = notify;
        this.quit = quit;
        timer.Tick += async (_, _) => await CheckAsync(announce: true);
    }

    UpdateStatus status;
    public UpdateStatus Status { get => status; private set { if (Set(ref status, value)) Raise(nameof(Summary)); } }
    public ReleaseInfo? Latest { get; private set; }
    double progress;
    public double Progress { get => progress; private set { if (Set(ref progress, value)) Raise(nameof(Summary)); } }
    public string? Error { get; private set; }

    public static string CurrentVersion
    {
        get
        {
            var v = typeof(Updater).Assembly.GetName().Version ?? new Version(1, 0);
            return v.Build > 0 ? $"{v.Major}.{v.Minor}.{v.Build}" : $"{v.Major}.{v.Minor}";
        }
    }

    public string Summary => Status switch
    {
        UpdateStatus.Checking => "Checking for updates…",
        UpdateStatus.UpToDate => $"Fluent is up to date ({CurrentVersion}).",
        UpdateStatus.Available => $"Fluent {Latest?.Version} is available. You have {CurrentVersion}.",
        UpdateStatus.Downloading => $"Downloading Fluent {Latest?.Version}… {Progress:P0}",
        UpdateStatus.Installing => $"Installing Fluent {Latest?.Version}. Fluent will restart.",
        UpdateStatus.Failed => Error ?? "Couldn't check for updates.",
        _ => $"You have Fluent {CurrentVersion}.",
    };

    /// <summary>First check shortly after start (not during startup), then every few hours.</summary>
    public void Start()
    {
        _ = Task.Delay(TimeSpan.FromSeconds(20)).ContinueWith(_ =>
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(async () => await CheckAsync(announce: true)));
        timer.Start();
    }

    public async Task CheckAsync(bool announce)
    {
        if (Status is UpdateStatus.Checking or UpdateStatus.Downloading or UpdateStatus.Installing) return;
        Status = UpdateStatus.Checking;
        try
        {
            var json = await Http.GetStringAsync(Constants.UpdatesUrl + "?t=" + DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            var latest = Updates.Parse(json, "windows");
            Latest = latest;
            if (latest is null) { Fail("The update list on the website couldn't be read."); return; }
            Status = Updates.IsNewer(latest.Version, CurrentVersion) ? UpdateStatus.Available : UpdateStatus.UpToDate;
            if (Status == UpdateStatus.Available && announce && model.Store.UpdateNotified != latest.Version)
            {
                model.Store.UpdateNotified = latest.Version;
                notify($"Fluent {latest.Version} is out", string.IsNullOrWhiteSpace(latest.Notes)
                    ? "Click to update from Settings → Updates." : latest.Notes);
            }
            Log.Write($"update check: latest {latest.Version}, have {CurrentVersion}");
        }
        catch (Exception e)
        {
            Log.Write("update check: " + e.Message);
            Fail("Couldn't reach the website. Check your connection.");
        }
    }

    public async Task UpdateAsync()
    {
        if (Latest is not { } r || Status is UpdateStatus.Downloading or UpdateStatus.Installing) return;
        Status = UpdateStatus.Downloading;
        Progress = 0;
        try
        {
            var dir = Path.Combine(Path.GetTempPath(), "Fluent-Update");
            Directory.CreateDirectory(dir);
            foreach (var old in Directory.GetFiles(dir)) try { File.Delete(old); } catch { }
            var file = Path.Combine(dir, $"Fluent-Setup-{r.Version}.exe");
            using (var resp = await Http.GetAsync(r.Url, HttpCompletionOption.ResponseHeadersRead))
            {
                resp.EnsureSuccessStatusCode();
                var total = resp.Content.Headers.ContentLength ?? r.Size;
                await using var input = await resp.Content.ReadAsStreamAsync();
                await using var output = File.Create(file);
                using var sha = SHA256.Create();
                var buf = new byte[81920];
                long read = 0;
                int n;
                while ((n = await input.ReadAsync(buf)) > 0)
                {
                    await output.WriteAsync(buf.AsMemory(0, n));
                    sha.TransformBlock(buf, 0, n, null, 0);
                    read += n;
                    if (total > 0) Progress = Math.Min(1, read / (double)total);
                }
                sha.TransformFinalBlock([], 0, 0);
                var hex = Convert.ToHexString(sha.Hash!).ToLowerInvariant();
                if (hex != r.Sha256)
                {
                    output.Close();
                    File.Delete(file);
                    Fail("The download didn't match its published checksum, so it wasn't installed. Try again.");
                    Log.Write("update: checksum mismatch");
                    return;
                }
            }
            Status = UpdateStatus.Installing;
            Log.Write($"update: running installer for {r.Version}");
            // Silent install for this user only: it closes Fluent, installs over it and opens the new one.
            Process.Start(new ProcessStartInfo(file, "/SILENT /SUPPRESSMSGBOXES /NORESTART /CURRENTUSER /CLOSEAPPLICATIONS /UPDATE=1") { UseShellExecute = true });
            await Task.Delay(1500);
            quit();
        }
        catch (Exception e)
        {
            Log.Write("update: " + e.Message);
            Fail("The update couldn't be downloaded. Try again.");
        }
    }

    void Fail(string message)
    {
        Error = message;
        Status = UpdateStatus.Failed;
        Raise(nameof(Summary));
    }
}
