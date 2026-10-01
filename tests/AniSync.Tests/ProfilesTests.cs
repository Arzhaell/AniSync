using AniSync.AniList;
using AniSync.Core;

namespace AniSync.Tests;

public class ProfilesTests
{
    [Fact]
    public void Old_single_accounts_become_profiles_and_keep_the_chosen_site_active()
    {
        var settings = new AppSettings
        {
            ClientId = "123",
            EncryptedToken = Secrets.Encrypt("anilist-token"),
            MalClientId = "abc",
            MalEncryptedTokens = Secrets.Encrypt("{}"),
            Service = ListSites.MyAnimeList,
        };

        Assert.True(settings.MigrateLegacyAccounts());

        Assert.Equal(2, settings.Profiles.Count);
        Assert.Equal(ListSites.MyAnimeList, settings.ActiveProfile!.Service);
        Assert.Equal("abc", settings.ActiveProfile.ClientId);
        var aniList = settings.Profiles.Single(p => p.Service == ListSites.AniList);
        Assert.Equal("anilist-token", Secrets.Decrypt(aniList.EncryptedToken));
        Assert.Null(settings.EncryptedToken);       // l'ancien format est vidé
        Assert.Null(settings.MalEncryptedTokens);
        Assert.False(settings.MigrateLegacyAccounts()); // et la conversion ne se refait pas
    }

    [Fact]
    public void Reconnecting_a_known_account_updates_it_instead_of_duplicating()
    {
        var settings = new AppSettings();
        var first = settings.AddOrMerge(new Profile { Service = ListSites.AniList, UserId = 42, UserName = "Alice", EncryptedToken = "old" });

        var again = settings.AddOrMerge(new Profile { Service = ListSites.AniList, UserId = 42, UserName = "Alice2", EncryptedToken = "new" });

        Assert.Same(first, again);
        Assert.Single(settings.Profiles);
        Assert.Equal("new", first.EncryptedToken);
        Assert.Equal("Alice2", first.UserName);
    }

    [Fact]
    public void Same_user_id_on_another_site_is_another_account()
    {
        var settings = new AppSettings();
        settings.AddOrMerge(new Profile { Service = ListSites.AniList, UserId = 42 });
        settings.AddOrMerge(new Profile { Service = ListSites.MyAnimeList, UserId = 42 });
        settings.AddOrMerge(new Profile { Service = ListSites.AniList, UserId = 7 });

        Assert.Equal(3, settings.Profiles.Count);
    }

    [Fact]
    public void Removing_the_active_account_activates_a_connected_one()
    {
        var settings = new AppSettings();
        var expired = settings.AddOrMerge(new Profile { Service = ListSites.AniList, UserId = 1 });
        var active = settings.AddOrMerge(new Profile { Service = ListSites.AniList, UserId = 2, EncryptedToken = "x" });
        var other = settings.AddOrMerge(new Profile { Service = ListSites.MyAnimeList, UserId = 3, EncryptedToken = "y" });
        settings.ActiveProfileId = active.Id;

        settings.RemoveProfile(active);

        Assert.Equal(other.Id, settings.ActiveProfileId); // le compte expiré n'est pas choisi
        Assert.DoesNotContain(active, settings.Profiles);
        Assert.Contains(expired, settings.Profiles);
    }

    [Fact]
    public void Each_account_keeps_its_own_connection()
    {
        var settings = new AppSettings();
        var alice = new Profile { Service = ListSites.AniList, EncryptedToken = Secrets.Encrypt("token-alice") };
        var bob = new Profile { Service = ListSites.AniList, EncryptedToken = Secrets.Encrypt("token-bob") };

        Assert.Equal("token-alice", new AniListService(alice, settings).Token);
        Assert.Equal("token-bob", new AniListService(bob, settings).Token);
        Assert.False(new AniListService(new Profile(), settings).IsAuthenticated);
    }

    [Theory]
    [InlineData(ListSites.AniList, "Alice", "https://anilist.co/user/Alice")]
    [InlineData(ListSites.MyAnimeList, "Alice", "https://myanimelist.net/profile/Alice")]
    public void Profile_page_is_known_even_before_the_site_gives_it(string site, string name, string expected)
    {
        Assert.Equal(expected, new Profile { Service = site, UserName = name }.PageUrl);
        Assert.Null(new Profile { Service = site }.PageUrl);
        Assert.Equal("https://x.test/me", new Profile { Service = site, UserName = name, ProfileUrl = "https://x.test/me" }.PageUrl);
    }

    [Fact]
    public void Tokens_are_encrypted_on_disk()
    {
        var encrypted = Secrets.Encrypt("secret-token")!;

        Assert.DoesNotContain("secret-token", encrypted);
        Assert.Equal("secret-token", Secrets.Decrypt(encrypted));
    }
}
