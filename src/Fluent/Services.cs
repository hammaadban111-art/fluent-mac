using System;
using System.IO;
using System.Media;
using System.Security.Cryptography;
using System.Text;
using Fluent.Core;
using Microsoft.Win32;

namespace Fluent;

/// <summary>The Gemini API key, encrypted with Windows DPAPI for the signed-in Windows user (only this
/// user on this PC can decrypt it). Never in settings.json, never logged.</summary>
public static class KeyStore
{
    public static string Dir { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Fluent");
    static string FilePath => Path.Combine(Dir, "gemini-key.bin");
    static readonly byte[] Entropy = "Fluent Gemini API key"u8.ToArray();

    public static string? Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return null;
            var plain = ProtectedData.Unprotect(File.ReadAllBytes(FilePath), Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plain);
        }
        catch { return null; }
    }

    public static bool Save(string key)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            var sealedKey = ProtectedData.Protect(Encoding.UTF8.GetBytes(key), Entropy, DataProtectionScope.CurrentUser);
            File.WriteAllBytes(FilePath, sealedKey);
            return true;
        }
        catch { return false; }
    }

    public static void Delete()
    {
        try { File.Delete(FilePath); } catch { }
    }
}

/// <summary>"Open Fluent when I sign in": the per-user Run key, no admin rights needed. The login launch
/// passes --background so Fluent starts in the tray instead of opening its window.</summary>
public static class LaunchAtLogin
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string Name = "Fluent";

    public static bool Enabled
    {
        get { try { return Registry.CurrentUser.OpenSubKey(RunKey)?.GetValue(Name) is string; } catch { return false; } }
    }

    /// <summary>Returns an error message when Windows refuses.</summary>
    public static string? Set(bool on)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey, true);
            if (on) key.SetValue(Name, $"\"{Environment.ProcessPath}\" --background");
            else key.DeleteValue(Name, false);
            return null;
        }
        catch (Exception e) { return e.Message; }
    }
}

/// <summary>Short start and stop sounds (Android: haptics; Mac: Tink and Pop), synthesised here so no
/// sound file needs a licence.</summary>
public static class Sounds
{
    static readonly Lazy<SoundPlayer> StartSound = new(() => Make(1568, 1976, 0.09, 0.22));
    static readonly Lazy<SoundPlayer> StopSound = new(() => Make(988, 740, 0.08, 0.2));

    public static void Start() => Play(StartSound);
    public static void Stop() => Play(StopSound);

    static void Play(Lazy<SoundPlayer> p)
    {
        try { p.Value.Play(); } catch { }
    }

    /// <summary>A soft two-note blip: a sine glide from <paramref name="f0"/> to <paramref name="f1"/> with a fast decay.</summary>
    static SoundPlayer Make(double f0, double f1, double seconds, double volume)
    {
        const int rate = 44100;
        var n = (int)(rate * seconds);
        var pcm = new short[n];
        double phase = 0;
        for (var i = 0; i < n; i++)
        {
            var t = (double)i / n;
            var f = f0 + (f1 - f0) * Math.Min(1, t * 2);
            phase += 2 * Math.PI * f / rate;
            var env = Math.Min(1, i / (rate * 0.004)) * Math.Exp(-5 * t);
            pcm[i] = (short)(Math.Sin(phase) * env * volume * short.MaxValue);
        }
        var p = new SoundPlayer(new MemoryStream(Wav.Encode(pcm, rate)));
        p.Load();
        return p;
    }
}
