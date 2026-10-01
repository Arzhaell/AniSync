using AniSync.Core;

namespace AniSync.Tests;

public class TitleParserTests
{
    [Theory]
    [InlineData("Frieren - Episode 12 vostfr - Anime-Sama", ParseMode.Normal, "Frieren", null, 12)]
    [InlineData("Sousou no Frieren Saison 2 Épisode 3 VOSTFR", ParseMode.Normal, "Sousou no Frieren", 2, 3)]
    [InlineData("[SubsPlease] Sousou no Frieren - 12 (1080p) [ABCD1234].mkv - VLC media player", ParseMode.Loose, "Sousou no Frieren", null, 12)]
    [InlineData("The Boys 2019 S04E01 FRENCH SubForced 720p WEB H264", ParseMode.Normal, "The Boys", 4, 1)]
    [InlineData("Jujutsu.Kaisen.S02E05.1080p.WEB.mkv", ParseMode.Loose, "Jujutsu Kaisen", 2, 5)]
    [InlineData("One Piece Episode 1100 VOSTFR", ParseMode.Normal, "One Piece", null, 1100)]
    [InlineData("Watch Frieren: Beyond Journey's End Episode 12", ParseMode.Normal, "Frieren: Beyond Journey's End", null, 12)]
    [InlineData("Mushoku Tensei II Ep 05", ParseMode.Normal, "Mushoku Tensei II", null, 5)]
    [InlineData("Spy x Family 2nd Season - 07", ParseMode.Normal, "Spy x Family", 2, 7)]
    [InlineData("Re:Zero kara Hajimeru Isekai Seikatsu 3rd Season Episode 2", ParseMode.Normal, "Re:Zero kara Hajimeru Isekai Seikatsu", 3, 2)]
    [InlineData("Mob Psycho 100 III - Episode 4", ParseMode.Normal, "Mob Psycho 100 III", null, 4)]
    [InlineData("Voir Dr. Stone épisode 5 en streaming VOSTFR", ParseMode.Normal, "Dr. Stone", null, 5)]
    [InlineData("Anime-Sama | Kaiju No. 8 Episode 3", ParseMode.Normal, "Kaiju No. 8", null, 3)]
    [InlineData("Hitori no Shita - E05 - Le titre", ParseMode.Normal, "Hitori no Shita", null, 5)]
    [InlineData("Frieren – Épisode 12 et 1 page supplémentaire - Personnel – Microsoft​ Edge", ParseMode.Strict, "Frieren", null, 12)]
    [InlineData("Frieren 12.mkv - VLC media player", ParseMode.Loose, "Frieren", null, 12)]
    [InlineData("Blue Lock - Épisode 3 - Saison 2 - VOSTFR", ParseMode.Normal, "Blue Lock", 2, 3)]
    [InlineData("[Oshi No Ko] Season 2 Episode 3", ParseMode.Normal, "Oshi No Ko", 2, 3)]
    [InlineData("[Oshi no Ko] Épisode 11 VOSTFR", ParseMode.Strict, "Oshi no Ko", null, 11)]
    [InlineData("Frieren: Beyond Journey’s End Episode 12", ParseMode.Normal, "Frieren: Beyond Journey’s End", null, 12)]
    [InlineData("Re:ZERO -Starting Life in Another World- Season 3 Episode 2", ParseMode.Normal, "Re:ZERO -Starting Life in Another World", 3, 2)]
    public void Recognizes_episode(string raw, ParseMode mode, string title, int? season, int episode)
    {
        var parsed = TitleParser.Parse(raw, mode);

        Assert.NotNull(parsed);
        Assert.Equal(title, parsed.Title);
        Assert.Equal(season, parsed.Season);
        Assert.Equal(episode, parsed.Episode);
    }

    [Theory]
    [InlineData("Noms pour application anime et 1 page supplémentaire - Personnel – Microsoft​ Edge", ParseMode.Strict)]
    [InlineData("#jeux | UWU MAKI - Discord", ParseMode.Normal)]
    [InlineData("Frieren Épisode 12.5 - Récap", ParseMode.Normal)]
    [InlineData("Top 10 anime 2024 - YouTube", ParseMode.Normal)]
    [InlineData("Lofi Girl - 24/7 radio", ParseMode.Normal)]
    [InlineData("Kaiju No. 8 - 03", ParseMode.Strict)]
    [InlineData("Paramètres", ParseMode.Loose)]
    [InlineData("Episode 5", ParseMode.Normal)]
    [InlineData("Season 1 Magie ou pas, peu importe - Regardez sur Crunchyroll", ParseMode.Normal)]
    [InlineData("", ParseMode.Loose)]
    public void Ignores_non_episodes(string raw, ParseMode mode)
    {
        Assert.Null(TitleParser.Parse(raw, mode));
    }

    [Fact]
    public void Same_series_has_same_key_whatever_the_episode_or_formatting()
    {
        var a = TitleParser.Parse("Frieren - Episode 12 VOSTFR")!;
        var b = TitleParser.Parse("FRIEREN épisode 13")!;

        Assert.Equal(a.SeriesKey, b.SeriesKey);
        Assert.NotEqual(a.EpisodeKey, b.EpisodeKey);
    }

    [Theory]
    [InlineData("Je veux t'aimer jusqu'à ta mort", "Je veux t'aimer jusqu'a ta mort")]
    [InlineData("Moi, quand je me réincarne en Slime", "Moi, quand je me reincarne en Slime")]
    [InlineData("Le Cœur de Thomas", "Le Coeur de Thomas")]
    [InlineData("Frieren", "Frieren")]
    [InlineData("きみが死ぬまで恋をしたい", "きみが死ぬまで恋をしたい")]
    [InlineData("ダンダダン", "ダンダダン")]
    public void Removes_accents_from_latin_letters_only(string input, string expected)
    {
        Assert.Equal(expected, TitleParser.RemoveLatinAccents(input));
    }

    [Fact]
    public void Season_is_part_of_the_series_key()
    {
        var s1 = TitleParser.Parse("Blue Lock Episode 3")!;
        var s2 = TitleParser.Parse("Blue Lock Saison 2 Episode 3")!;

        Assert.NotEqual(s1.SeriesKey, s2.SeriesKey);
    }
}
