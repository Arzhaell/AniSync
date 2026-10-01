using System.Net;
using System.Net.Http;
using System.Text;
using AniSync.Core;
using AniSync.Detection;

namespace AniSync.Tests;

public class ExtensionCandidatesTests
{
    static ExtensionReport Crunchyroll(bool paused = false) => new()
    {
        TabId = 7,
        Browser = "Opera",
        Host = "crunchyroll.com",
        Title = "Season 1 Magie ou pas, peu importe - Regardez sur Crunchyroll",
        Series = ["frieren beyond journeys end", "Frieren"],
        Season = 1,
        Episode = 2,
        Video = new VideoState { CurrentTime = 300, Duration = 1440, Paused = paused },
    };

    [Fact]
    public void Uses_structured_episode_info_from_the_page()
    {
        var c = ExtensionCandidates.From(Crunchyroll())!;

        Assert.NotNull(c.Parsed);
        Assert.Equal("frieren beyond journeys end", c.Parsed.Title);
        Assert.Equal(["Frieren"], c.Parsed.AltTitles);
        Assert.Null(c.Parsed.Season);
        Assert.Equal(2, c.Parsed.Episode);
        Assert.True(c.IsPlaying);
        Assert.Equal(TimeSpan.FromMinutes(24), c.Duration);
        Assert.Equal("crunchyroll.com · Opera", c.SourceName);
    }

    [Fact]
    public void Paused_video_is_not_playing()
    {
        Assert.False(ExtensionCandidates.From(Crunchyroll(paused: true))!.IsPlaying);
    }

    [Fact]
    public void Ignores_short_videos_like_ads()
    {
        var report = Crunchyroll();
        report.Video!.Duration = 30;
        Assert.Null(ExtensionCandidates.From(report));
    }

    [Fact]
    public void Falls_back_to_the_tab_title_without_structured_info()
    {
        var report = new ExtensionReport
        {
            TabId = 1,
            Browser = "Microsoft Edge",
            Host = "anime-sama.fr",
            Title = "Frieren - Episode 12 VOSTFR - Anime-Sama",
            Video = new VideoState { Duration = 1400 },
        };

        var c = ExtensionCandidates.From(report)!;
        var tracker = new WatchTracker();
        var (current, _) = tracker.Tick([c], new AppSettings(), DateTime.Now);

        Assert.Null(c.Parsed);
        Assert.Equal("Frieren", current!.Parsed.Title);
        Assert.Equal(12, current.Parsed.Episode);
    }

    [Theory]
    [InlineData("Opera", "opera")]
    [InlineData("Opera GX", "opera")]
    [InlineData("Microsoft Edge", "msedge")]
    [InlineData("Google Chrome", "chrome")]
    [InlineData("Brave", "brave")]
    public void Maps_browser_names_to_processes(string browser, string process)
    {
        Assert.Equal(process, KnownApps.ProcessForBrowser(browser));
    }
}

public class BrowserBridgeTests
{
    const string Body = """{"tabId":3,"browser":"Opera","host":"crunchyroll.com","series":["frieren beyond journeys end"],"episode":5,"video":{"currentTime":10,"duration":1440,"paused":false}}""";

    static HttpRequestMessage Post(int port, bool withHeader)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"http://localhost:{port}/v1/playback")
        {
            Content = new StringContent(Body, Encoding.UTF8, "application/json"),
        };
        if (withHeader) request.Headers.Add("X-AniSync-Extension", "1");
        return request;
    }

    [Fact]
    public async Task Accepts_reports_from_the_extension_only()
    {
        int port = 47900 + Random.Shared.Next(90);
        using var bridge = new BrowserBridge(port);
        bridge.Start();
        using var http = new HttpClient();

        // Une page web ne peut pas ajouter l'en-tête : refusé.
        var refused = await http.SendAsync(Post(port, withHeader: false));
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.Empty(bridge.ActiveReports());

        var accepted = await http.SendAsync(Post(port, withHeader: true));
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);

        var report = Assert.Single(bridge.ActiveReports());
        Assert.Equal(5, report.Episode);
        Assert.Equal("Opera", bridge.LastBrowser);
    }
}
