using AniSync.AniList;

namespace AniSync.Core;

public static class ListSites
{
    public const string AniList = "AniList";
    public const string MyAnimeList = "MyAnimeList";
}

/// <summary>Ce que le site de liste sait d'un anime : son numéro chez lui, ta fiche, son nombre d'épisodes.</summary>
public sealed record ListState(int SiteId, ListEntry? Entry, int? TotalEpisodes, string? PageUrl);

/// <summary>
/// La liste d'un compte (AniList ou MyAnimeList). La reconnaissance des animes se fait toujours
/// via la recherche AniList ; seul le compte mis à jour change.
/// </summary>
public interface IListService
{
    /// <summary>Nom du site : « AniList » ou « MyAnimeList ».</summary>
    string Name { get; }

    /// <summary>Le compte derrière ce service (ses infos sont mises à jour à la connexion).</summary>
    Profile Profile { get; }

    bool IsAuthenticated { get; }

    /// <summary>Page du site où l'utilisateur crée sa clé d'appli.</summary>
    string DeveloperPage { get; }

    /// <summary>Connexion via le navigateur ; remplit le compte (identité + jetons).</summary>
    Task<Viewer> ConnectAsync(string clientId, CancellationToken ct);

    /// <summary>Oublie la connexion (le compte reste dans la liste, à reconnecter).</summary>
    void SignOut();

    Task<Viewer> GetAccountAsync(CancellationToken ct = default);

    /// <summary>L'anime sur ce site, avec la fiche de l'utilisateur ; null si le site ne le connaît pas.</summary>
    Task<ListState?> GetStateAsync(AniMedia media, CancellationToken ct = default);

    Task<ListEntry> SaveAsync(ListState state, SyncDecision decision, CancellationToken ct = default);

    /// <summary>Remet la fiche comme avant une mise à jour de l'historique.</summary>
    Task UndoAsync(HistoryItem item, CancellationToken ct = default);
}

public static class ListServiceExtensions
{
    /// <summary>Recopie l'identité du compte connecté dans le profil.</summary>
    public static void ApplyViewer(this Profile profile, Viewer viewer, string clientId)
    {
        profile.UserId = viewer.Id;
        profile.UserName = viewer.Name;
        profile.AvatarUrl = viewer.AvatarUrl;
        profile.ProfileUrl = viewer.SiteUrl;
        profile.ClientId = clientId;
    }
}
