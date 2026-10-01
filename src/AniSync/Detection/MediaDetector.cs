using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using AniSync.Core;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using Windows.Media.Control;

namespace AniSync.Detection;

public sealed record TitleOption(string Text, ParseMode Mode);

/// <summary>
/// Quelque chose qui est (ou était) en lecture sur le PC.
/// <see cref="Parsed"/> est déjà connu quand la source le donne directement (extension navigateur).
/// </summary>
public sealed record PlaybackCandidate(
    string SourceKey,
    string SourceName,
    IReadOnlyList<TitleOption> Titles,
    bool IsPlaying,
    TimeSpan? Duration,
    ParsedEpisode? Parsed = null);

/// <summary>
/// Regarde ce qui se lit sur le PC :
/// 1) les sessions média Windows (navigateurs, lecteur multimédia...) : titre, lecture/pause, durée ;
/// 2) pour les lecteurs qui n'en publient pas (VLC, MPC...) : le titre de la fenêtre + le son réellement émis.
/// </summary>
public sealed class MediaDetector
{
    static readonly TimeSpan AudioGrace = TimeSpan.FromSeconds(8);
    static readonly Regex Domain = new(@"^[\w-]+(\.[\w-]+)+$");

    GlobalSystemMediaTransportControlsSessionManager? _smtc;
    MMDeviceEnumerator? _audio;
    readonly BrowserBridge? _bridge;
    readonly Dictionary<string, DateTime> _lastAudio = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<uint, (string? Name, DateTime At)> _processNames = new();
    readonly HashSet<string> _loggedErrors = new();

    public MediaDetector(BrowserBridge? bridge = null)
    {
        _bridge = bridge;
    }

    public async Task<List<PlaybackCandidate>> PollAsync(AppSettings settings)
    {
        var now = DateTime.UtcNow;
        UpdateAudioActivity(now);

        var windows = NativeWindows.GetVisibleWindows()
            .Select(w => (Process: ProcessName(w.Pid), w.Title))
            .Where(w => w.Process is not null)
            .Select(w => (Process: w.Process!, w.Title))
            .ToList();

        var result = new List<PlaybackCandidate>();
        var covered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // L'extension voit la page elle-même : pour ce navigateur, elle remplace la détection par titre.
        var extensionBrowsers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var report in _bridge?.ActiveReports() ?? [])
        {
            if (ExtensionCandidates.From(report) is not { } candidate) continue;
            result.Add(candidate);
            if (KnownApps.ProcessForBrowser(report.Browser) is { } browser) extensionBrowsers.Add(browser);
        }
        covered.UnionWith(extensionBrowsers);

        try
        {
            _smtc ??= await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            foreach (var session in _smtc.GetSessions())
            {
                try
                {
                    var candidate = await ReadSessionAsync(session, windows, settings, covered);
                    if (candidate is not null && !extensionBrowsers.Contains(KnownApps.ProcessFromAumid(session.SourceAppUserModelId ?? "") ?? ""))
                        result.Add(candidate);
                }
                catch (Exception ex)
                {
                    LogOnce("smtc-session", ex);
                }
            }
        }
        catch (Exception ex)
        {
            LogOnce("smtc", ex);
            _smtc = null;
        }

        // Lecteurs / navigateurs absents des sessions média Windows.
        foreach (var group in windows
                     .Where(w => KnownApps.IsTracked(w.Process) && !covered.Contains(w.Process) && !settings.IsAppIgnored(w.Process))
                     .GroupBy(w => w.Process, StringComparer.OrdinalIgnoreCase))
        {
            bool playing = _lastAudio.TryGetValue(group.Key, out var t) && now - t < AudioGrace;
            var mode = KnownApps.IsBrowser(group.Key) ? ParseMode.Strict : ParseMode.Loose;
            result.Add(new PlaybackCandidate(
                $"win|{group.Key}",
                KnownApps.FriendlyName(group.Key),
                group.Select(w => new TitleOption(w.Title, mode)).ToList(),
                playing,
                null));
        }

        return result;
    }

    static async Task<PlaybackCandidate?> ReadSessionAsync(
        GlobalSystemMediaTransportControlsSession session,
        List<(string Process, string Title)> windows,
        AppSettings settings,
        HashSet<string> covered)
    {
        var aumid = session.SourceAppUserModelId ?? "";
        var process = KnownApps.ProcessFromAumid(aumid);
        if (process is not null) covered.Add(process);
        if (settings.IsAppIgnored(aumid)) return null;

        var props = await session.TryGetMediaPropertiesAsync();
        var playback = session.GetPlaybackInfo();
        var timeline = session.GetTimelineProperties();

        bool isBrowser = process is not null && KnownApps.IsBrowser(process);
        var mode = isBrowser ? ParseMode.Normal : ParseMode.Loose;
        string title = props?.Title ?? "";
        var options = new List<TitleOption>();

        if (title.Length > 0) options.Add(new TitleOption(title, mode));
        if (props?.Artist is { Length: > 0 } artist && !Domain.IsMatch(artist) && title.Length > 0)
            options.Add(new TitleOption($"{artist} - {title}", mode));
        if (props?.AlbumTitle is { Length: > 0 } album && title.Length > 0)
            options.Add(new TitleOption($"{album} - {title}", mode));

        // La vidéo est souvent dans un lecteur intégré au titre peu parlant : le titre de l'onglet aide.
        if (isBrowser)
            options.AddRange(windows
                .Where(w => w.Process.Equals(process, StringComparison.OrdinalIgnoreCase))
                .Select(w => new TitleOption(w.Title, ParseMode.Strict)));

        TimeSpan? duration = timeline.EndTime > timeline.StartTime && timeline.EndTime > TimeSpan.Zero
            ? timeline.EndTime - timeline.StartTime
            : null;

        bool playing = playback?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
        return new PlaybackCandidate($"smtc|{aumid}|{title}", KnownApps.FriendlyName(process ?? aumid), options, playing, duration);
    }

    void UpdateAudioActivity(DateTime now)
    {
        try
        {
            _audio ??= new MMDeviceEnumerator();
            foreach (var device in _audio.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
            {
                using (device)
                {
                    var manager = device.AudioSessionManager;
                    manager.RefreshSessions();
                    var sessions = manager.Sessions;
                    for (int i = 0; i < sessions.Count; i++)
                    {
                        using var session = sessions[i];
                        if (session.State != AudioSessionState.AudioSessionStateActive) continue;
                        if (session.AudioMeterInformation.MasterPeakValue < 0.0005f) continue;
                        if (ProcessName(session.GetProcessID) is { } name) _lastAudio[name] = now;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            LogOnce("audio", ex);
        }
    }

    string? ProcessName(uint pid)
    {
        if (pid == 0) return null;
        var now = DateTime.UtcNow;
        if (_processNames.TryGetValue(pid, out var cached) && now - cached.At < TimeSpan.FromMinutes(1)) return cached.Name;
        string? name = null;
        try
        {
            using var p = Process.GetProcessById((int)pid);
            name = p.ProcessName;
        }
        catch
        {
            // processus terminé
        }
        _processNames[pid] = (name, now);
        if (_processNames.Count > 2000) _processNames.Clear();
        return name;
    }

    void LogOnce(string key, Exception ex)
    {
        if (_loggedErrors.Add(key)) Log.Error($"Détection ({key})", ex);
    }
}

public static class KnownApps
{
    static readonly HashSet<string> Browsers = new(StringComparer.OrdinalIgnoreCase)
    {
        "msedge", "chrome", "opera", "opera_gx", "brave", "vivaldi", "firefox", "librewolf", "waterfox", "floorp",
        "zen", "arc", "chromium", "yandex", "thorium",
    };

    static readonly HashSet<string> Players = new(StringComparer.OrdinalIgnoreCase)
    {
        "vlc", "mpc-hc", "mpc-hc64", "mpc-be", "mpc-be64", "mpv", "mpvnet", "PotPlayer", "PotPlayer64", "PotPlayerMini",
        "PotPlayerMini64", "KMPlayer", "KMPlayer64X", "GOM", "smplayer", "wmplayer", "stremio", "mpc-qt",
        "Microsoft.Media.Player", "Video.UI",
    };

    static readonly Dictionary<string, string> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        ["msedge"] = "Edge", ["chrome"] = "Chrome", ["opera"] = "Opera", ["opera_gx"] = "Opera GX", ["brave"] = "Brave",
        ["vivaldi"] = "Vivaldi", ["firefox"] = "Firefox", ["vlc"] = "VLC", ["mpc-hc"] = "MPC-HC", ["mpc-hc64"] = "MPC-HC",
        ["mpc-be"] = "MPC-BE", ["mpc-be64"] = "MPC-BE", ["mpv"] = "mpv", ["mpvnet"] = "mpv.net", ["PotPlayer"] = "PotPlayer",
        ["PotPlayer64"] = "PotPlayer", ["PotPlayerMini"] = "PotPlayer", ["PotPlayerMini64"] = "PotPlayer", ["stremio"] = "Stremio",
        ["Microsoft.Media.Player"] = L.T("Lecteur multimédia", "Media Player"), ["Video.UI"] = L.T("Films et TV", "Movies & TV"), ["wmplayer"] = "Windows Media Player",
    };

    public static bool IsBrowser(string process) => Browsers.Contains(process);

    /// <summary>Nom de navigateur donné par l'extension (« Opera », « Microsoft Edge »...) → processus.</summary>
    public static string? ProcessForBrowser(string? browser)
    {
        var b = browser?.ToLowerInvariant() ?? "";
        if (b.Contains("edge")) return "msedge";
        if (b.Contains("opera")) return "opera";
        if (b.Contains("brave")) return "brave";
        if (b.Contains("vivaldi")) return "vivaldi";
        if (b.Contains("chrome")) return "chrome";
        return null;
    }
    public static bool IsTracked(string process) => Browsers.Contains(process) || Players.Contains(process);

    /// <summary>Retrouve le processus derrière l'identifiant d'appli d'une session média Windows.</summary>
    public static string? ProcessFromAumid(string aumid)
    {
        var a = aumid.ToLowerInvariant();
        if (a.Length == 0) return null;
        if (a.Contains("msedge") || a.Contains("microsoftedge")) return "msedge";
        if (a.Contains("opera")) return "opera";
        if (a.Contains("chrome")) return "chrome";
        if (a.Contains("brave")) return "brave";
        if (a.Contains("vivaldi")) return "vivaldi";
        if (a.Contains("firefox") || a == "308046b0af4a39cb") return "firefox";
        if (a.EndsWith(".exe")) return Path.GetFileNameWithoutExtension(a);
        return Browsers.Concat(Players).FirstOrDefault(p => a.Contains(p.ToLowerInvariant()));
    }

    public static string FriendlyName(string processOrAumid)
    {
        if (Names.TryGetValue(processOrAumid, out var name)) return name;
        var s = processOrAumid;
        if (s.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) s = s[..^4];
        int bang = s.IndexOf('!');
        if (bang > 0) s = s[..bang];
        int underscore = s.IndexOf('_');
        if (underscore > 0) s = s[..underscore];
        return s;
    }
}

internal static class NativeWindows
{
    delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll")]
    static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int GetWindowTextW(IntPtr hWnd, StringBuilder text, int maxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int GetWindowTextLengthW(IntPtr hWnd);

    [DllImport("user32.dll")]
    static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    public static List<(uint Pid, string Title)> GetVisibleWindows()
    {
        var windows = new List<(uint, string)>();
        EnumWindows((hWnd, _) =>
        {
            if (!IsWindowVisible(hWnd)) return true;
            int length = GetWindowTextLengthW(hWnd);
            if (length <= 0) return true;
            var sb = new StringBuilder(length + 1);
            GetWindowTextW(hWnd, sb, sb.Capacity);
            GetWindowThreadProcessId(hWnd, out uint pid);
            windows.Add((pid, sb.ToString()));
            return true;
        }, IntPtr.Zero);
        return windows;
    }
}
