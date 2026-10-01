using AniSync.AniList;
using AniSync.Core;

namespace AniSync.Tests;

public class SyncRulesTests
{
    static ListEntry Entry(string status, int progress, int repeat = 0, bool started = true) =>
        new(1, status, progress, repeat, started ? new FuzzyDate(2026, 1, 1) : new FuzzyDate(null, null, null), null);

    [Fact]
    public void Adds_anime_missing_from_the_list()
    {
        var d = SyncRules.Decide(null, 3, 12);

        Assert.Equal(SyncAction.Create, d.Action);
        Assert.Equal(3, d.Progress);
        Assert.Equal(MediaListStatus.Current, d.Status);
        Assert.True(d.SetStartedToday);
        Assert.False(d.SetCompletedToday);
    }

    [Fact]
    public void Adds_directly_as_completed_when_last_episode()
    {
        var d = SyncRules.Decide(null, 12, 12);

        Assert.Equal(SyncAction.Create, d.Action);
        Assert.Equal(MediaListStatus.Completed, d.Status);
        Assert.True(d.SetCompletedToday);
    }

    [Fact]
    public void Jumps_forward_from_8_to_12()
    {
        var d = SyncRules.Decide(Entry(MediaListStatus.Current, 8), 12, 24);

        Assert.Equal(SyncAction.Update, d.Action);
        Assert.Equal(12, d.Progress);
        Assert.Equal(MediaListStatus.Current, d.Status);
    }

    [Theory]
    [InlineData(12, 8)]
    [InlineData(12, 12)]
    public void Never_goes_backwards(int onAniList, int watched)
    {
        var d = SyncRules.Decide(Entry(MediaListStatus.Current, onAniList), watched, 24);

        Assert.Equal(SyncAction.None, d.Action);
    }

    [Fact]
    public void Does_not_touch_completed_anime_when_rewatching()
    {
        var d = SyncRules.Decide(Entry(MediaListStatus.Completed, 12), 5, 12);

        Assert.Equal(SyncAction.None, d.Action);
    }

    [Fact]
    public void Planning_becomes_current_with_start_date()
    {
        var d = SyncRules.Decide(Entry(MediaListStatus.Planning, 0, started: false), 1, 12);

        Assert.Equal(SyncAction.Update, d.Action);
        Assert.Equal(MediaListStatus.Current, d.Status);
        Assert.True(d.SetStartedToday);
    }

    [Fact]
    public void Paused_becomes_current_again()
    {
        var d = SyncRules.Decide(Entry(MediaListStatus.Paused, 4), 5, null);

        Assert.Equal(MediaListStatus.Current, d.Status);
        Assert.False(d.SetStartedToday);
    }

    [Fact]
    public void Finishing_the_last_episode_completes_the_anime()
    {
        var d = SyncRules.Decide(Entry(MediaListStatus.Current, 11), 12, 12);

        Assert.Equal(MediaListStatus.Completed, d.Status);
        Assert.True(d.SetCompletedToday);
    }

    [Fact]
    public void Finishing_a_rewatch_increments_repeat_count()
    {
        var d = SyncRules.Decide(Entry(MediaListStatus.Repeating, 3, repeat: 1), 12, 12);

        Assert.Equal(MediaListStatus.Completed, d.Status);
        Assert.Equal(2, d.Repeat);
    }

    [Fact]
    public void Ignores_episode_zero()
    {
        Assert.Equal(SyncAction.None, SyncRules.Decide(null, 0, 12).Action);
    }
}
