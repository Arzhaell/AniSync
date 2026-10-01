using AniSync.AniList;

namespace AniSync.Core;

public enum SyncAction { None, Create, Update }

public sealed record SyncDecision(
    SyncAction Action,
    int Progress,
    string Status,
    int? Repeat = null,
    bool SetStartedToday = false,
    bool SetCompletedToday = false,
    string Reason = "")
{
    public static SyncDecision Skip(string reason) => new(SyncAction.None, 0, "", Reason: reason);
}

/// <summary>
/// Règles de mise à jour AniList quand un épisode vient d'être terminé.
/// La progression ne fait qu'avancer : ép. 8 sur AniList + ép. 12 vu → 12 ; ép. 12 + ép. 8 vu → rien.
/// </summary>
public static class SyncRules
{
    public static SyncDecision Decide(ListEntry? entry, int episode, int? totalEpisodes)
    {
        if (episode < 1) return SyncDecision.Skip(L.T("Épisode 0 ignoré", "Episode 0 ignored"));

        bool finishes = totalEpisodes is > 0 && episode >= totalEpisodes;
        string doneOrWatching = finishes ? MediaListStatus.Completed : MediaListStatus.Current;

        if (entry is null)
            return new SyncDecision(SyncAction.Create, episode, doneOrWatching, SetStartedToday: true, SetCompletedToday: finishes);

        if (entry.Status == MediaListStatus.Completed)
            return SyncDecision.Skip(L.T("Déjà terminé sur ta liste", "Already completed on your list"));

        if (episode <= entry.Progress)
            return SyncDecision.Skip(L.T($"Déjà à jour (ép. {entry.Progress} sur ta liste)", $"Already up to date (ep. {entry.Progress} on your list)"));

        if (entry.Status == MediaListStatus.Repeating)
        {
            return finishes
                ? new SyncDecision(SyncAction.Update, episode, MediaListStatus.Completed, Repeat: entry.Repeat + 1)
                : new SyncDecision(SyncAction.Update, episode, MediaListStatus.Repeating);
        }

        // CURRENT, PLANNING, PAUSED ou DROPPED : on (re)passe en cours, ou terminé.
        return new SyncDecision(
            SyncAction.Update,
            episode,
            doneOrWatching,
            SetStartedToday: entry.StartedAt is null || entry.StartedAt.IsEmpty,
            SetCompletedToday: finishes && (entry.CompletedAt is null || entry.CompletedAt.IsEmpty));
    }
}
