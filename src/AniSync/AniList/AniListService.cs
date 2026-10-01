using AniSync.Core;

namespace AniSync.AniList;

/// <summary>La liste AniList d'un compte.</summary>
public sealed class AniListService : IListService
{
    readonly AppSettings _settings;
    readonly AniListClient _client = new();

    public AniListService(Profile profile, AppSettings settings)
    {
        Profile = profile;
        _settings = settings;
        _client.Token = Secrets.Decrypt(profile.EncryptedToken);
    }

    public string Name => ListSites.AniList;
    public Profile Profile { get; }
    public bool IsAuthenticated => _client.IsAuthenticated;
    public string DeveloperPage => AniListAuth.DeveloperPage;

    /// <summary>Jeton du compte, pour que la recherche AniList respecte ses préférences.</summary>
    public string? Token => _client.Token;

    public async Task<Viewer> ConnectAsync(string clientId, CancellationToken ct)
    {
        var token = await AniListAuth.AuthorizeAsync(clientId, ct);
        var previous = _client.Token;
        _client.Token = token;
        try
        {
            var viewer = await _client.GetViewerAsync(ct);
            Profile.ApplyViewer(viewer, clientId);
            Profile.EncryptedToken = Secrets.Encrypt(token);
            return viewer;
        }
        catch
        {
            _client.Token = previous;
            throw;
        }
    }

    public void SignOut()
    {
        _client.Token = null;
        Profile.EncryptedToken = null;
        _settings.Save();
    }

    public Task<Viewer> GetAccountAsync(CancellationToken ct = default) => _client.GetViewerAsync(ct);

    public async Task<ListState?> GetStateAsync(AniMedia media, CancellationToken ct = default)
    {
        // Toujours relire juste avant d'écrire : la fiche a pu changer sur le site.
        var fresh = await _client.GetMediaAsync(media.Id, ct);
        return new ListState(fresh.Id, fresh.Entry, fresh.Episodes, fresh.SiteUrl);
    }

    public Task<ListEntry> SaveAsync(ListState state, SyncDecision decision, CancellationToken ct = default) =>
        _client.SaveEntryAsync(state.SiteId, decision, ct);

    public async Task UndoAsync(HistoryItem item, CancellationToken ct = default)
    {
        if ((item.SiteId ?? item.MediaId) is not int mediaId) return;

        var media = await _client.GetMediaAsync(mediaId, ct);
        if (media.Entry is null)
            throw new AniListException(L.T("Cet anime n'est plus dans ta liste.", "This anime is no longer in your list."));
        if (item.NewProgress is int expected && media.Entry.Progress != expected)
            throw new AniListException(L.T($"Ta liste a changé depuis (ép. {media.Entry.Progress} maintenant) : annulation impossible.", $"Your list has changed since (ep. {media.Entry.Progress} now): can't undo."));

        if (item.WasCreated)
            await _client.DeleteEntryAsync(media.Entry.Id, ct);
        else
            await _client.RestoreEntryAsync(mediaId, item.PrevProgress ?? 0, item.PrevStatus ?? MediaListStatus.Current, item.PrevRepeat, ct);
    }
}
