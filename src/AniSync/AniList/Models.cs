namespace AniSync.AniList;

public sealed record FuzzyDate(int? Year, int? Month, int? Day)
{
    public bool IsEmpty => Year is null;
}

/// <summary>L'entrée de la liste de l'utilisateur pour un anime.</summary>
public sealed record ListEntry(int Id, string Status, int Progress, int Repeat, FuzzyDate? StartedAt, FuzzyDate? CompletedAt);

public sealed record MediaRelation(string RelationType, int Id, string? Type, string? Format);

public sealed record AniMedia(
    int Id,
    string Title,
    IReadOnlyList<string> AllTitles,
    int? Episodes,
    string? Format,
    int? Year,
    string? CoverUrl,
    string? SiteUrl,
    IReadOnlyList<MediaRelation> Relations,
    ListEntry? Entry)
{
    /// <summary>Numéro du même anime sur MyAnimeList (absent pour quelques rares animes).</summary>
    public int? MalId { get; init; }
}

/// <summary>Le compte connecté (AniList ou MyAnimeList).</summary>
public sealed record Viewer(int Id, string Name, string? AvatarUrl, string? SiteUrl);

/// <summary>Erreur renvoyée par un site de liste (AniList ou MyAnimeList).</summary>
public class AniListException(string message) : Exception(message);

/// <summary>Connexion au site de liste absente, refusée ou expirée : il faut se reconnecter.</summary>
public class AuthExpiredException(string message) : AniListException(message);

/// <summary>Jeton AniList absent, invalide ou expiré.</summary>
public sealed class AniListAuthException(string message) : AuthExpiredException(message);

public static class MediaListStatus
{
    public const string Current = "CURRENT";
    public const string Planning = "PLANNING";
    public const string Completed = "COMPLETED";
    public const string Dropped = "DROPPED";
    public const string Paused = "PAUSED";
    public const string Repeating = "REPEATING";

    public static string ToFrench(string status) => status switch
    {
        Current => "En cours",
        Planning => "À voir",
        Completed => "Terminé",
        Dropped => "Abandonné",
        Paused => "En pause",
        Repeating => "Revisionnage",
        _ => status,
    };
}
