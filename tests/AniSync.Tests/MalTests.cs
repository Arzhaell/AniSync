using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using AniSync.AniList;
using AniSync.Core;
using AniSync.Mal;

namespace AniSync.Tests;

public class MalTests
{
    static readonly DateTime Today = new(2026, 10, 1);

    [Theory]
    [InlineData("watching", false, MediaListStatus.Current)]
    [InlineData("completed", false, MediaListStatus.Completed)]
    [InlineData("completed", true, MediaListStatus.Repeating)] // revisionnage façon MAL
    [InlineData("on_hold", false, MediaListStatus.Paused)]
    [InlineData("dropped", false, MediaListStatus.Dropped)]
    [InlineData("plan_to_watch", false, MediaListStatus.Planning)]
    public void Reads_mal_statuses(string mal, bool rewatching, string expected)
    {
        Assert.Equal(expected, MalMapping.FromMalStatus(mal, rewatching));
    }

    [Fact]
    public void Parses_the_users_list_entry()
    {
        var json = JsonNode.Parse("""
            {"status":"watching","score":0,"num_episodes_watched":8,"is_rewatching":false,
             "updated_at":"2026-09-30T20:00:00+00:00","start_date":"2026-09","num_times_rewatched":1}
            """);

        var entry = MalMapping.ParseEntry(52991, json)!;

        Assert.Equal(52991, entry.Id);
        Assert.Equal(MediaListStatus.Current, entry.Status);
        Assert.Equal(8, entry.Progress);
        Assert.Equal(1, entry.Repeat);
        Assert.Equal(new FuzzyDate(2026, 9, null), entry.StartedAt);   // date partielle acceptée par MAL
        Assert.Null(entry.CompletedAt);
    }

    [Fact]
    public void Anime_not_in_the_list_has_no_entry()
    {
        Assert.Null(MalMapping.ParseEntry(1, null));
        Assert.Null(MalMapping.ParseEntry(1, JsonNode.Parse("{}")));
    }

    [Fact]
    public void Adding_a_new_anime_sends_watching_progress_and_start_date()
    {
        var decision = SyncRules.Decide(null, 3, 12);

        var form = MalMapping.UpdateForm(decision, Today);

        Assert.Equal("watching", form["status"]);
        Assert.Equal("3", form["num_watched_episodes"]);
        Assert.Equal("false", form["is_rewatching"]);
        Assert.Equal("2026-10-01", form["start_date"]);
        Assert.False(form.ContainsKey("finish_date"));
    }

    [Fact]
    public void Finishing_the_last_episode_completes_with_finish_date()
    {
        var entry = new ListEntry(1, MediaListStatus.Current, 11, 0, new FuzzyDate(2026, 9, 1), null);

        var form = MalMapping.UpdateForm(SyncRules.Decide(entry, 12, 12), Today);

        Assert.Equal("completed", form["status"]);
        Assert.Equal("12", form["num_watched_episodes"]);
        Assert.Equal("2026-10-01", form["finish_date"]);
        Assert.False(form.ContainsKey("start_date"));
    }

    [Fact]
    public void Rewatch_progress_and_completion_follow_mal_conventions()
    {
        var rewatching = new ListEntry(1, MediaListStatus.Repeating, 3, 1, null, null);

        var progress = MalMapping.UpdateForm(SyncRules.Decide(rewatching, 5, 12), Today);
        Assert.Equal("completed", progress["status"]);   // sur MAL, un revisionnage reste « completed »
        Assert.Equal("true", progress["is_rewatching"]);

        var finished = MalMapping.UpdateForm(SyncRules.Decide(rewatching, 12, 12), Today);
        Assert.Equal("completed", finished["status"]);
        Assert.Equal("false", finished["is_rewatching"]);
        Assert.Equal("2", finished["num_times_rewatched"]);
    }

    [Fact]
    public void Undo_restores_previous_progress_and_status()
    {
        var form = MalMapping.RestoreForm(8, MediaListStatus.Paused, 0);

        Assert.Equal("on_hold", form["status"]);
        Assert.Equal("8", form["num_watched_episodes"]);
        Assert.Equal("0", form["num_times_rewatched"]);
    }

    [Fact]
    public void Pkce_verifier_follows_the_oauth_rules()
    {
        var a = MalAuth.NewCodeVerifier();
        var b = MalAuth.NewCodeVerifier();

        Assert.Matches(new Regex(@"^[A-Za-z0-9\-._~]{43,128}$"), a);
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void History_shows_which_site_was_updated()
    {
        var media = new AniMedia(154587, "Sousou no Frieren", ["Sousou no Frieren"], 28, "TV", 2023, null, null, [], null) { MalId = 52991 };
        var state = new ListState(52991, null, 28, "https://myanimelist.net/anime/52991");
        var decision = SyncRules.Decide(null, 2, 28);
        var saved = new ListEntry(52991, MediaListStatus.Current, 2, 0, null, null);

        var profile = new Profile { Service = ListSites.MyAnimeList, UserName = "Alice" };

        var item = HistoryItem.Applied(new ParsedEpisode("Frieren", null, 2), media, 2, decision, profile, state, saved);

        Assert.Equal(ListSites.MyAnimeList, item.Service);
        Assert.Equal(profile.Id, item.ProfileId);
        Assert.Equal(52991, item.SiteId);
        Assert.Equal(154587, item.MediaId);
        Assert.Equal("https://myanimelist.net/anime/52991", item.SiteUrl);
        Assert.EndsWith("· Alice · MyAnimeList", item.TimeText);
        Assert.True(item.WasCreated);
    }
}
