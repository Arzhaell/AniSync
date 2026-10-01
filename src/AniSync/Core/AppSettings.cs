using System.Text.Json.Serialization;

namespace AniSync.Core;

/// <summary>Réglages persistés dans %APPDATA%\AniSync\settings.json.</summary>
public sealed class AppSettings
{
    readonly object _gate = new();

    public bool Listening { get; set; } = true;

    /// <summary>Temps de visionnage réel (pauses exclues) pour qu'un épisode compte comme vu.</summary>
    public int MinWatchMinutes { get; set; } = 20;

    public bool Notifications { get; set; } = true;

    /// <summary>Langue de l'interface : « auto » (langue de Windows), « fr » ou « en ».</summary>
    public string Language { get; set; } = L.AutoCode;

    /// <summary>Comptes de liste (AniList / MyAnimeList), autant qu'on veut.</summary>
    public List<Profile> Profiles { get; set; } = new();

    /// <summary>Le compte mis à jour quand un épisode est terminé.</summary>
    public string? ActiveProfileId { get; set; }

    /// <summary>Dernier Client ID AniList saisi (proposé pour les comptes suivants).</summary>
    public string? ClientId { get; set; }

    /// <summary>Dernier Client ID MyAnimeList saisi.</summary>
    public string? MalClientId { get; set; }

    // Ancien format (jusqu'à la 1.4) : un seul compte par site. Converti en comptes au chargement.
    public string? EncryptedToken { get; set; }
    public string? MalEncryptedTokens { get; set; }
    public string? Service { get; set; }

    /// <summary>Corrections manuelles : clé de série détectée → ID AniList.</summary>
    public Dictionary<string, int> TitleMappings { get; set; } = new();

    /// <summary>Séries détectées que l'utilisateur a choisi d'ignorer.</summary>
    public List<string> IgnoredTitles { get; set; } = new();

    /// <summary>Applis dont les médias ne sont jamais pris en compte (musique...).</summary>
    public List<string> IgnoredApps { get; set; } =
        ["spotify", "deezer", "applemusic", "itunes", "tidal", "amazonmusic", "foobar2000", "musicbee", "aimp", "winamp"];

    /// <summary>Mots qui disqualifient un titre (réactions, bandes-annonces...).</summary>
    public List<string> IgnoredKeywords { get; set; } =
    [
        "reaction", "réaction", "react", "review", "critique", "trailer", "bande annonce", "teaser", "amv", "ost",
        "opening", "ending", "ncop", "nced", "recap", "résumé", "theory", "théorie", "analyse", "explained", "podcast",
        "tier list",
    ];

    // ---------- Comptes ----------

    [JsonIgnore]
    public Profile? ActiveProfile
    {
        get { lock (_gate) return Profiles.FirstOrDefault(p => p.Id == ActiveProfileId); }
    }

    public List<Profile> ProfilesSnapshot()
    {
        lock (_gate) return Profiles.ToList();
    }

    public Profile? FindProfile(string? id)
    {
        lock (_gate) return Profiles.FirstOrDefault(p => p.Id == id);
    }

    /// <summary>
    /// Ajoute un compte qui vient de se connecter. Si c'est un compte déjà connu (même site, même utilisateur),
    /// celui-ci est mis à jour au lieu de créer un doublon. Renvoie le compte conservé.
    /// </summary>
    public Profile AddOrMerge(Profile connected)
    {
        lock (_gate)
        {
            if (Profiles.Contains(connected)) return connected;
            var existing = Profiles.FirstOrDefault(p => p.IsSameAccount(connected));
            if (existing is not null)
            {
                existing.CopyAccountFrom(connected);
                return existing;
            }
            Profiles.Add(connected);
            return connected;
        }
    }

    public void RemoveProfile(Profile profile)
    {
        lock (_gate)
        {
            Profiles.Remove(profile);
            if (ActiveProfileId == profile.Id) ActiveProfileId = Profiles.FirstOrDefault(p => p.IsSignedIn)?.Id ?? Profiles.FirstOrDefault()?.Id;
        }
    }

    /// <summary>Convertit l'ancien format (un compte AniList + un compte MyAnimeList) en comptes.</summary>
    public bool MigrateLegacyAccounts()
    {
        lock (_gate)
        {
            if (EncryptedToken is null && MalEncryptedTokens is null) return false;

            Profile? aniList = null, mal = null;
            if (EncryptedToken is not null)
                Profiles.Add(aniList = new Profile { Service = ListSites.AniList, ClientId = ClientId, EncryptedToken = EncryptedToken });
            if (MalEncryptedTokens is not null)
                Profiles.Add(mal = new Profile { Service = ListSites.MyAnimeList, ClientId = MalClientId, EncryptedToken = MalEncryptedTokens });

            ActiveProfileId ??= (Service == ListSites.MyAnimeList ? mal ?? aniList : aniList ?? mal)?.Id;
            EncryptedToken = null;
            MalEncryptedTokens = null;
            Service = null;
            return true;
        }
    }

    public int? GetMapping(string seriesKey)
    {
        lock (_gate) return TitleMappings.TryGetValue(seriesKey, out var id) ? id : null;
    }

    public void SetMapping(string seriesKey, int mediaId)
    {
        lock (_gate) TitleMappings[seriesKey] = mediaId;
        Save();
    }

    public bool IsTitleIgnored(string seriesKey)
    {
        lock (_gate) return IgnoredTitles.Contains(seriesKey);
    }

    public void IgnoreTitle(string seriesKey)
    {
        lock (_gate)
        {
            if (!IgnoredTitles.Contains(seriesKey)) IgnoredTitles.Add(seriesKey);
        }
        Save();
    }

    public void ClearIgnoredTitles()
    {
        lock (_gate) IgnoredTitles.Clear();
        Save();
    }

    public bool IsAppIgnored(string app)
    {
        lock (_gate) return IgnoredApps.Any(a => app.Contains(a, StringComparison.OrdinalIgnoreCase));
    }

    public bool HasIgnoredKeyword(string rawTitle)
    {
        var norm = $" {TitleParser.Normalize(rawTitle)} ";
        lock (_gate) return IgnoredKeywords.Any(k => norm.Contains($" {TitleParser.Normalize(k)} "));
    }

    public static AppSettings Load()
    {
        var settings = JsonStore.Load<AppSettings>(AppPaths.SettingsFile) ?? new AppSettings();
        if (settings.MigrateLegacyAccounts())
        {
            Log.Info($"Comptes : ancien format converti ({settings.Profiles.Count} compte(s))");
            settings.Save();
        }
        return settings;
    }

    public void Save()
    {
        lock (_gate) JsonStore.Save(AppPaths.SettingsFile, this);
    }
}
