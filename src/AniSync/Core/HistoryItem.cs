using System.Text.Json.Serialization;
using AniSync.AniList;

namespace AniSync.Core;

public enum HistoryKind { Added, Updated, Completed, Skipped, Unrecognized, Pending, Error }

/// <summary>Une ligne de l'historique (persistée dans history.json).</summary>
public sealed class HistoryItem : ObservableObject
{
    bool _undone;
    bool _fixed;

    public DateTime Time { get; set; } = DateTime.Now;
    public HistoryKind Kind { get; set; }
    public string Title { get; set; } = "";
    public string Message { get; set; } = "";
    /// <summary>Numéro AniList de l'anime (sert aussi aux corrections).</summary>
    public int? MediaId { get; set; }
    public string? SiteUrl { get; set; }

    /// <summary>Site mis à jour (AniList pour les anciennes lignes) et numéro de l'anime sur ce site.</summary>
    public string Service { get; set; } = ListSites.AniList;
    public int? SiteId { get; set; }

    /// <summary>Compte mis à jour (absent pour les lignes d'avant les comptes multiples).</summary>
    public string? ProfileId { get; set; }
    public string? ProfileName { get; set; }

    // Ce qui a été détecté (pour « Corriger » / relancer la synchro).
    public string ParsedTitle { get; set; } = "";
    public int? Season { get; set; }
    public int Episode { get; set; }

    // Anime proposé quand le titre n'a pas été reconnu : l'utilisateur valide d'un clic.
    public int? SuggestedId { get; set; }
    public string? SuggestedTitle { get; set; }
    public double SuggestedScore { get; set; }

    // De quoi annuler.
    public bool WasCreated { get; set; }
    public int? EntryId { get; set; }
    public int? NewProgress { get; set; }
    public int? PrevProgress { get; set; }
    public string? PrevStatus { get; set; }
    public int? PrevRepeat { get; set; }

    public bool Undone
    {
        get => _undone;
        set { if (Set(ref _undone, value)) Refresh(); }
    }

    public bool Fixed
    {
        get => _fixed;
        set { if (Set(ref _fixed, value)) Refresh(); }
    }

    [JsonIgnore] public bool CanUndo => !Undone && MediaId is not null && Kind is HistoryKind.Added or HistoryKind.Updated or HistoryKind.Completed;
    [JsonIgnore] public bool CanFix => !Fixed && (Undone || Kind is HistoryKind.Unrecognized);
    [JsonIgnore] public bool CanConfirm => CanFix && !Undone && SuggestedId is not null;
    [JsonIgnore] public bool CanFixOnly => CanFix && !CanConfirm;
    [JsonIgnore]
    public string TimeText =>
        (Time.Date == DateTime.Today ? Time.ToString("HH:mm") : Time.ToString("dd/MM HH:mm")) +
        (ProfileName is not null ? $" · {ProfileName}"
            : Service == ListSites.AniList || MediaId is null ? "" : $" · {Service}");
    [JsonIgnore] public string DisplayMessage => Undone ? $"{Message} · annulé" : Message;

    [JsonIgnore]
    public string Glyph => Undone ? "" : Kind switch
    {
        HistoryKind.Added => "",
        HistoryKind.Updated => "",
        HistoryKind.Completed => "",
        HistoryKind.Skipped => "",
        HistoryKind.Unrecognized => "",
        HistoryKind.Pending => "",
        _ => "",
    };

    [JsonIgnore]
    public string Color => Undone ? "#8FA3B8" : Kind switch
    {
        HistoryKind.Added => "#3DB4F2",
        HistoryKind.Updated or HistoryKind.Completed => "#3DDC84",
        HistoryKind.Skipped => "#8FA3B8",
        HistoryKind.Unrecognized or HistoryKind.Pending => "#F5A524",
        _ => "#F2555A",
    };

    public ParsedEpisode ToParsed() => new(ParsedTitle, Season, Episode);

    void Refresh()
    {
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanFix));
        OnPropertyChanged(nameof(CanConfirm));
        OnPropertyChanged(nameof(CanFixOnly));
        OnPropertyChanged(nameof(Glyph));
        OnPropertyChanged(nameof(Color));
        OnPropertyChanged(nameof(DisplayMessage));
    }

    static HistoryItem From(ParsedEpisode p, HistoryKind kind, string title, string message) => new()
    {
        Kind = kind,
        Title = title,
        Message = message,
        ParsedTitle = p.Title,
        Season = p.Season,
        Episode = p.Episode,
    };

    static string Of(int episode, int? total) => total is int t ? $"{episode}/{t}" : $"{episode}";

    public static HistoryItem Applied(ParsedEpisode p, AniMedia media, int episode, SyncDecision decision,
        Profile profile, ListState state, ListEntry saved)
    {
        var prev = state.Entry;
        var total = state.TotalEpisodes;
        HistoryKind kind = decision.Status == MediaListStatus.Completed ? HistoryKind.Completed
            : prev is null ? HistoryKind.Added
            : HistoryKind.Updated;

        string message = kind switch
        {
            HistoryKind.Completed when prev is null => $"Ajouté et terminé · épisode {Of(episode, total)}",
            HistoryKind.Completed => $"Épisode {prev.Progress} → {Of(episode, total)} · Terminé !",
            HistoryKind.Added => $"Ajouté à ta liste · épisode {Of(episode, total)}",
            _ => $"Épisode {prev!.Progress} → {Of(episode, total)}",
        };

        var item = From(p, kind, media.Title, message);
        item.MediaId = media.Id;
        item.SiteUrl = state.PageUrl ?? media.SiteUrl;
        item.SetProfile(profile);
        item.SiteId = state.SiteId;
        item.WasCreated = prev is null;
        item.EntryId = saved.Id;
        item.NewProgress = saved.Progress;
        item.PrevProgress = prev?.Progress;
        item.PrevStatus = prev?.Status;
        item.PrevRepeat = prev?.Repeat;
        return item;
    }

    public static HistoryItem Skipped(ParsedEpisode p, AniMedia media, string reason, Profile? profile = null, string? pageUrl = null)
    {
        var item = From(p, HistoryKind.Skipped, media.Title, $"Épisode {p.Episode} vu · {reason}");
        item.MediaId = media.Id;
        item.SiteUrl = pageUrl ?? media.SiteUrl;
        if (profile is not null) item.SetProfile(profile);
        return item;
    }

    void SetProfile(Profile profile)
    {
        Service = profile.Service;
        ProfileId = profile.Id;
        ProfileName = profile.Label;
    }

    public static HistoryItem Unrecognized(ParsedEpisode p, string? reason = null) =>
        From(p, HistoryKind.Unrecognized, p.Title, reason ?? $"Épisode {p.Episode} vu · anime introuvable sur AniList");

    /// <summary>Titre non reconnu, mais un anime ressemblant est proposé à la validation.</summary>
    public static HistoryItem Unsure(ParsedEpisode p, Suggestion suggestion)
    {
        string question = suggestion.Score >= 0.7
            ? $"est-ce « {suggestion.Media.Title} » ?"
            : $"pas reconnu, peut-être « {suggestion.Media.Title} » ?";
        var item = From(p, HistoryKind.Unrecognized, p.Title, $"Épisode {p.Episode} vu · {question}");
        item.SuggestedId = suggestion.Media.Id;
        item.SuggestedTitle = suggestion.Media.Title;
        item.SuggestedScore = suggestion.Score;
        return item;
    }

    public static HistoryItem Pending(ParsedEpisode p, string reason) =>
        From(p, HistoryKind.Pending, p.Title, $"Épisode {p.Episode} vu · {reason}");

    public static HistoryItem Error(ParsedEpisode p, string reason) =>
        From(p, HistoryKind.Error, p.Title, $"Épisode {p.Episode} vu · {reason}");
}

public static class HistoryStore
{
    public const int MaxItems = 150;

    public static List<HistoryItem> Load() => JsonStore.Load<List<HistoryItem>>(AppPaths.HistoryFile) ?? [];

    public static void Save(IEnumerable<HistoryItem> items) => JsonStore.Save(AppPaths.HistoryFile, items.Take(MaxItems).ToList());
}
