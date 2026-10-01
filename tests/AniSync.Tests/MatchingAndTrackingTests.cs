using AniSync.Core;
using AniSync.Detection;

namespace AniSync.Tests;

public class TitleMatcherTests
{
    static double Sim(string a, string b) => TitleMatcher.Similarity(TitleParser.Normalize(a), TitleParser.Normalize(b));

    [Theory]
    [InlineData("Frieren", "Sousou no Frieren")]
    [InlineData("Spy x Family", "SPY×FAMILY")]
    [InlineData("Frieren: Beyond Journey's End", "Frieren: Beyond Journey’s End")]
    [InlineData("L'Attaque des Titans", "L'attaque des titans")]
    public void Accepts_matching_titles(string detected, string anilist)
    {
        Assert.True(Sim(detected, anilist) >= TitleMatcher.AcceptThreshold);
    }

    [Theory]
    [InlineData("The Boys", "The Boy and the Heron")]
    [InlineData("The Boys", "The Boys Presents: Diabolical")]
    [InlineData("One Piece", "One Punch Man")]
    public void Rejects_different_titles(string detected, string anilist)
    {
        Assert.True(Sim(detected, anilist) < TitleMatcher.AcceptThreshold);
    }

    [Theory]
    [InlineData("spy x family season 2", 2, true)]
    [InlineData("shingeki no kyojin 3", 3, true)]
    [InlineData("mushoku tensei ii", 2, true)]
    [InlineData("sousou no frieren 2nd season", 2, true)]
    [InlineData("sousou no frieren", 2, false)]
    public void Detects_season_markers(string norm, int season, bool expected)
    {
        Assert.Equal(expected, TitleMatcher.HasSeasonMarker(norm, season));
    }

    [Theory]
    [InlineData("Jujutsu Kaizen", null, "Jujutsu Kaisen")]                     // faute de frappe
    [InlineData("Frieren le voyage continue", "frieren", "Sousou no Frieren")] // trouvé par le début du titre
    [InlineData("Demon Slayer Le Train de l'infini", "demon slayer", "Demon Slayer: Kimetsu no Yaiba Mugen Train Arc")]
    public void Suggests_titles_that_are_close_but_not_certain(string detected, string? query, string anilist)
    {
        double score = TitleMatcher.SuggestionScore(TitleParser.Normalize(detected), [anilist], query);

        Assert.True(score >= TitleMatcher.SuggestThreshold, $"score {score:0.00}");
    }

    [Fact]
    public void A_match_on_a_small_part_of_the_title_never_looks_certain()
    {
        // « frieren » seul correspond parfaitement à un synonyme, mais ce n'est qu'un quart du titre détecté.
        double score = TitleMatcher.SuggestionScore(TitleParser.Normalize("Frieren le voyage continue"), ["Frieren"], "frieren");

        Assert.InRange(score, TitleMatcher.SuggestThreshold, TitleMatcher.AcceptThreshold);
    }

    [Theory]
    [InlineData("Les Simpson", "Shin-chan")]
    [InlineData("The Boys", "Dragon Ball Z")]
    public void Does_not_suggest_unrelated_titles(string detected, string anilist)
    {
        Assert.True(TitleMatcher.SuggestionScore(TitleParser.Normalize(detected), [anilist]) < TitleMatcher.SuggestThreshold);
    }

    [Fact]
    public void Widens_the_search_with_shorter_queries()
    {
        var queries = TitleMatcher.RelaxedQueries("Demon Slayer: Le Train de l'infini").ToList();

        Assert.Equal(["Demon Slayer", "Demon Slayer: Le", "demon"], queries);
    }

    [Fact]
    public void Strips_season_markers()
    {
        Assert.Equal("spy x family", TitleMatcher.StripSeason("spy x family season 2"));
        Assert.Equal("sousou no frieren", TitleMatcher.StripSeason("sousou no frieren 2nd season"));
    }
}

public class WatchTrackerTests
{
    static PlaybackCandidate Candidate(string title, bool playing, TimeSpan? duration = null) =>
        new("smtc|test|" + title, "Test", [new TitleOption(title, ParseMode.Normal)], playing, duration);

    /// <summary>Simule n secondes de lecture (ou de pause) à raison d'un tick par seconde.</summary>
    static List<EpisodeSnapshot> Run(WatchTracker tracker, AppSettings settings, PlaybackCandidate c, int seconds, ref DateTime now)
    {
        var completed = new List<EpisodeSnapshot>();
        for (int i = 0; i < seconds; i++)
        {
            now = now.AddSeconds(1);
            completed.AddRange(tracker.Tick([c], settings, now).Completed);
        }
        return completed;
    }

    [Fact]
    public void Counts_only_after_20_minutes_of_real_playback()
    {
        var tracker = new WatchTracker();
        var settings = new AppSettings { MinWatchMinutes = 20 };
        var now = new DateTime(2026, 9, 29, 20, 0, 0);
        var playing = Candidate("Frieren - Episode 3", playing: true, TimeSpan.FromMinutes(24));
        var paused = playing with { IsPlaying = false };

        Assert.Empty(Run(tracker, settings, playing, 600, ref now));      // 10 min de lecture
        Assert.Empty(Run(tracker, settings, paused, 3600, ref now));      // 1 h de pause : ne compte pas
        Assert.Empty(Run(tracker, settings, playing, 590, ref now));      // 19 min 50
        var done = Run(tracker, settings, playing, 30, ref now);          // passe les 20 min

        var ep = Assert.Single(done);
        Assert.Equal(3, ep.Parsed.Episode);
        Assert.Empty(Run(tracker, settings, playing, 300, ref now));      // jamais compté deux fois
    }

    [Fact]
    public void Short_episodes_need_85_percent_of_their_duration()
    {
        var tracker = new WatchTracker();
        var settings = new AppSettings { MinWatchMinutes = 20 };
        var now = new DateTime(2026, 9, 29, 20, 0, 0);
        var shortEp = Candidate("Chiikawa - Episode 40", playing: true, TimeSpan.FromMinutes(2));

        Assert.Empty(Run(tracker, settings, shortEp, 100, ref now));
        Assert.Single(Run(tracker, settings, shortEp, 5, ref now));       // 85 % de 2 min = 102 s
    }

    [Fact]
    public void Ignores_reaction_videos()
    {
        var tracker = new WatchTracker();
        var settings = new AppSettings { MinWatchMinutes = 1 };
        var now = new DateTime(2026, 9, 29, 20, 0, 0);
        var reaction = Candidate("One Piece Episode 1100 REACTION", playing: true);

        Assert.Empty(Run(tracker, settings, reaction, 120, ref now));
    }
}
