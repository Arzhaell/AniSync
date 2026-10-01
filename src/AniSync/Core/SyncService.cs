using AniSync.AniList;

namespace AniSync.Core;

/// <summary>
/// Met à jour la liste (AniList ou MyAnimeList, selon le choix de l'utilisateur) quand un épisode est terminé.
/// L'anime est toujours reconnu via AniList ; seul le site écrit change.
/// </summary>
public sealed class SyncService(Func<IListService> activeService, Func<HistoryItem, IListService?> serviceForItem, MediaResolver resolver)
{
    const int MaxRetries = 30;

    readonly SemaphoreSlim _gate = new(1, 1);
    readonly List<ParsedEpisode> _pending = new();

    /// <summary>Chaque résultat (mise à jour, anime introuvable, erreur...) pour l'historique.</summary>
    public event Action<HistoryItem>? Result;

    /// <summary>La connexion au site de liste n'est plus valide.</summary>
    public event Action? AuthExpired;

    public async Task ProcessAsync(ParsedEpisode p, int attempt = 0)
    {
        bool retry = false;
        await _gate.WaitAsync();
        var service = activeService();
        try
        {
            if (!service.IsAuthenticated)
            {
                AddPending(p);
                Emit(HistoryItem.Pending(p, $"en attente de connexion à {service.Name}"));
                return;
            }

            var resolution = await resolver.ResolveAsync(p);
            if (resolution is null)
            {
                // Pas de correspondance sûre : on propose le titre le plus proche plutôt que de deviner.
                var suggestions = await resolver.SuggestAsync(p);
                Emit(suggestions.Count > 0 ? HistoryItem.Unsure(p, suggestions[0]) : HistoryItem.Unrecognized(p));
                return;
            }
            if (resolution.Problem is not null)
            {
                Emit(HistoryItem.Unrecognized(p, resolution.Problem));
                return;
            }

            var media = resolution.Media;
            var state = await service.GetStateAsync(media);
            if (state is null)
            {
                Emit(HistoryItem.Error(p, $"« {media.Title} » n'existe pas sur {service.Name}"));
                return;
            }

            var decision = SyncRules.Decide(state.Entry, resolution.Episode, state.TotalEpisodes);
            if (decision.Action == SyncAction.None)
            {
                Log.Info($"{media.Title} ép. {resolution.Episode} : {decision.Reason}");
                Emit(HistoryItem.Skipped(p, media, decision.Reason, service.Profile, state.PageUrl));
                return;
            }

            var saved = await service.SaveAsync(state, decision);
            resolver.InvalidateMedia(media.Id);
            Log.Info($"{service.Name} mis à jour : {media.Title} → ép. {saved.Progress} ({saved.Status})");
            Emit(HistoryItem.Applied(p, media, resolution.Episode, decision, service.Profile, state, saved));
        }
        catch (AuthExpiredException ex)
        {
            Log.Error($"Connexion {service.Name} refusée", ex);
            AddPending(p);
            AuthExpired?.Invoke();
            Emit(HistoryItem.Pending(p, $"session {service.Name} expirée, reconnecte-toi"));
        }
        catch (Exception ex) when (IsTransient(ex) && attempt < MaxRetries)
        {
            Log.Error($"{service.Name} injoignable (essai {attempt + 1}) pour {p}", ex);
            if (attempt == 0) Emit(HistoryItem.Error(p, $"{service.Name} injoignable, nouvel essai automatique…"));
            retry = true;
        }
        catch (Exception ex)
        {
            Log.Error($"Synchro impossible pour {p}", ex);
            Emit(HistoryItem.Error(p, ex.Message));
        }
        finally
        {
            _gate.Release();
        }

        if (retry)
        {
            await Task.Delay(TimeSpan.FromMinutes(1));
            await ProcessAsync(p, attempt + 1);
        }
    }

    /// <summary>Relance les épisodes vus pendant qu'on n'était pas connecté.</summary>
    public async Task FlushPendingAsync()
    {
        List<ParsedEpisode> items;
        lock (_pending)
        {
            items = _pending.ToList();
            _pending.Clear();
        }
        foreach (var p in items) await ProcessAsync(p);
    }

    /// <summary>Remet la fiche comme avant cette mise à jour, sur le compte qui a été mis à jour.</summary>
    public async Task UndoAsync(HistoryItem item)
    {
        var service = serviceForItem(item)
            ?? throw new AniListException("Le compte mis à jour a été retiré d'AniSync : annulation impossible.");
        if (!service.IsAuthenticated)
            throw new AniListException($"Reconnecte le compte {service.Profile.Label} pour annuler cette mise à jour.");

        await service.UndoAsync(item);
        if (item.MediaId is int id) resolver.InvalidateMedia(id);
        Log.Info($"Annulé sur {service.Profile.Label} : {item.Title} ({item.Message})");
    }

    void AddPending(ParsedEpisode p)
    {
        lock (_pending)
        {
            if (!_pending.Any(x => x.EpisodeKey == p.EpisodeKey)) _pending.Add(p);
        }
    }

    void Emit(HistoryItem item) => Result?.Invoke(item);

    static bool IsTransient(Exception ex) => ex is HttpRequestException or TaskCanceledException or IOException;
}
