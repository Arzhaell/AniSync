using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using AniSync.Core;

namespace AniSync.Detection;

/// <summary>Ce que l'extension navigateur envoie pour un onglet qui contient une vidéo.</summary>
public sealed class ExtensionReport
{
    public int TabId { get; set; }
    public string? Browser { get; set; }
    public string? Host { get; set; }
    public string? Title { get; set; }
    public string? MediaTitle { get; set; }
    public string? MediaArtist { get; set; }
    public List<string>? Series { get; set; }
    public int? Season { get; set; }
    public int? Episode { get; set; }
    public VideoState? Video { get; set; }
}

public sealed class VideoState
{
    public double CurrentTime { get; set; }
    public double Duration { get; set; }
    public bool Paused { get; set; }
    public bool Ended { get; set; }
}

/// <summary>
/// Petit serveur local (http://localhost:47814) qui reçoit les infos de l'extension navigateur.
/// Il exige l'en-tête X-AniSync-Extension : une page web ne peut pas l'envoyer (le navigateur demanderait
/// une autorisation CORS qu'on ne donne jamais), donc seule l'extension peut lui parler.
/// </summary>
public sealed class BrowserBridge : IDisposable
{
    public const int Port = 47814;
    const string AuthHeader = "X-AniSync-Extension";
    const long MaxBody = 64 * 1024;
    static readonly TimeSpan ReportTtl = TimeSpan.FromSeconds(8);
    static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    readonly HttpListener _listener = new();
    readonly ConcurrentDictionary<string, (ExtensionReport Report, DateTime At)> _reports = new();
    readonly int _port;

    public BrowserBridge(int port = Port)
    {
        _port = port;
    }

    /// <summary>Dernier message reçu de l'extension (UTC) et depuis quel navigateur.</summary>
    public DateTime? LastContact { get; private set; }
    public string? LastBrowser { get; private set; }

    /// <summary>Renvoyé à l'extension pour qu'elle affiche si l'écoute est active.</summary>
    public Func<bool> IsListening { get; set; } = () => true;

    public bool IsRunning => _listener.IsListening;

    public void Start()
    {
        try
        {
            _listener.Prefixes.Add($"http://localhost:{_port}/");
            _listener.Start();
            _ = Task.Run(AcceptLoopAsync);
            Log.Info($"Extension navigateur : écoute sur le port {_port}");
        }
        catch (Exception ex)
        {
            Log.Error($"Impossible d'ouvrir le port {_port} pour l'extension navigateur", ex);
        }
    }

    /// <summary>Les onglets qui ont une vidéo en ce moment (rapports de moins de quelques secondes).</summary>
    public List<ExtensionReport> ActiveReports()
    {
        var now = DateTime.UtcNow;
        foreach (var stale in _reports.Where(kv => now - kv.Value.At > ReportTtl).Select(kv => kv.Key).ToList())
            _reports.TryRemove(stale, out _);
        return _reports.Values.Select(v => v.Report).ToList();
    }

    async Task AcceptLoopAsync()
    {
        while (_listener.IsListening)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync();
            }
            catch
            {
                break; // arrêt
            }
            _ = Task.Run(() => Handle(context));
        }
    }

    void Handle(HttpListenerContext context)
    {
        var request = context.Request;
        var response = context.Response;
        try
        {
            if (request.Headers[AuthHeader] != "1")
            {
                Reply(response, 403, null);
                return;
            }

            var path = request.Url?.AbsolutePath ?? "";
            if (request.HttpMethod == "GET" && path == "/v1/ping")
            {
                Reply(response, 200, new { app = "AniSync", listening = IsListening() });
                return;
            }

            if (request.HttpMethod == "POST" && path == "/v1/playback")
            {
                if (request.ContentLength64 > MaxBody)
                {
                    Reply(response, 413, null);
                    return;
                }
                var report = JsonSerializer.Deserialize<ExtensionReport>(request.InputStream, Json);
                if (report is not null)
                {
                    _reports[$"{report.Browser}|{report.TabId}"] = (report, DateTime.UtcNow);
                    LastContact = DateTime.UtcNow;
                    LastBrowser = report.Browser;
                }
                Reply(response, 200, new { ok = true, listening = IsListening() });
                return;
            }

            Reply(response, 404, null);
        }
        catch (Exception ex)
        {
            Log.Error("Extension navigateur : requête invalide", ex);
            try { Reply(response, 400, null); } catch { /* connexion fermée */ }
        }
    }

    static void Reply(HttpListenerResponse response, int status, object? body)
    {
        response.StatusCode = status;
        if (body is not null)
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(body);
            response.ContentType = "application/json";
            response.ContentLength64 = bytes.Length;
            response.OutputStream.Write(bytes);
        }
        response.Close();
    }

    public void Dispose()
    {
        try { _listener.Stop(); _listener.Close(); } catch { /* déjà fermé */ }
    }
}

/// <summary>Transforme un rapport de l'extension en source de lecture pour le suivi.</summary>
public static class ExtensionCandidates
{
    const double MinDuration = 60;

    public static PlaybackCandidate? From(ExtensionReport report)
    {
        var video = report.Video;
        if (video is null || !(video.Duration >= MinDuration)) return null;

        // Infos structurées de la page (Crunchyroll...) : on n'a rien à deviner.
        ParsedEpisode? parsed = null;
        var series = (report.Series ?? [])
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (series.Count > 0 && report.Episode is > 0)
            parsed = new ParsedEpisode(series[0], report.Season is > 1 ? report.Season : null, report.Episode.Value) { AltTitles = series.Skip(1).ToList() };

        // Sinon : titres de la vidéo et de l'onglet, analysés comme d'habitude.
        var titles = new List<TitleOption>();
        if (report.MediaTitle is { Length: > 0 } mediaTitle)
        {
            titles.Add(new TitleOption(mediaTitle, ParseMode.Normal));
            if (report.MediaArtist is { Length: > 0 } artist) titles.Add(new TitleOption($"{artist} - {mediaTitle}", ParseMode.Normal));
        }
        if (report.Title is { Length: > 0 } title) titles.Add(new TitleOption(title, ParseMode.Normal));

        var site = string.IsNullOrWhiteSpace(report.Host) ? "Navigateur" : report.Host;
        var source = string.IsNullOrWhiteSpace(report.Browser) ? site : $"{site} · {report.Browser}";
        return new PlaybackCandidate(
            $"ext|{report.Browser}|{report.TabId}",
            source,
            titles,
            IsPlaying: !video.Paused && !video.Ended,
            TimeSpan.FromSeconds(video.Duration),
            parsed);
    }
}
