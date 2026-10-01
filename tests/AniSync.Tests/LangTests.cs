using AniSync.AniList;
using AniSync.Core;

namespace AniSync.Tests;

/// <summary>Les tests qui changent la langue de l'appli ne tournent pas en parallèle avec les autres.</summary>
[CollectionDefinition(nameof(LanguageCollection), DisableParallelization = true)]
public class LanguageCollection;

[Collection(nameof(LanguageCollection))]
public class LangTests
{
    [Theory]
    [InlineData("fr", true)]
    [InlineData("en", false)]
    public void Forced_language_wins_over_windows(string setting, bool french) =>
        Assert.Equal(french, L.Detect(setting));

    [Fact]
    public void Auto_follows_the_windows_language()
    {
        bool windowsIsFrench = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "fr";
        Assert.Equal(windowsIsFrench, L.Detect("auto"));
        Assert.Equal(windowsIsFrench, L.Detect(null));
    }

    [Fact]
    public void Texts_follow_the_chosen_language()
    {
        bool before = L.IsFrench;
        try
        {
            L.Use(false);
            Assert.Equal("Already up to date (ep. 12 on your list)", SyncRules.Decide(Entry(12), 8, 24).Reason);
            Assert.Equal("Watching", MediaListStatus.ToDisplay(MediaListStatus.Current));

            L.Use(true);
            Assert.Equal("Déjà à jour (ép. 12 sur ta liste)", SyncRules.Decide(Entry(12), 8, 24).Reason);
            Assert.Equal("En cours", MediaListStatus.ToDisplay(MediaListStatus.Current));
        }
        finally
        {
            L.Use(before);
        }
    }

    static ListEntry Entry(int progress) =>
        new(1, MediaListStatus.Current, progress, 0, new FuzzyDate(2026, 1, 1), null);
}
