using System.Globalization;
using System.Text.Json.Nodes;
using AniSync.AniList;
using AniSync.Core;

namespace AniSync.Mal;

/// <summary>
/// Traduction entre la fiche MyAnimeList et le modèle de l'appli (statuts façon AniList).
/// Sur MAL, un revisionnage = statut « completed » + is_rewatching.
/// </summary>
public static class MalMapping
{
    public static string ToMalStatus(string status) => status switch
    {
        MediaListStatus.Completed or MediaListStatus.Repeating => "completed",
        MediaListStatus.Paused => "on_hold",
        MediaListStatus.Dropped => "dropped",
        MediaListStatus.Planning => "plan_to_watch",
        _ => "watching",
    };

    public static string FromMalStatus(string? status, bool rewatching) => rewatching
        ? MediaListStatus.Repeating
        : status switch
        {
            "completed" => MediaListStatus.Completed,
            "on_hold" => MediaListStatus.Paused,
            "dropped" => MediaListStatus.Dropped,
            "plan_to_watch" => MediaListStatus.Planning,
            _ => MediaListStatus.Current,
        };

    /// <summary>Fiche MAL (« my_list_status ») → fiche de l'appli ; null si l'anime n'est pas dans la liste.</summary>
    public static ListEntry? ParseEntry(int animeId, JsonNode? status)
    {
        if (status is not JsonObject s || Str(s["status"]) is null) return null;
        return new ListEntry(
            animeId,
            FromMalStatus(Str(s["status"]), Bool(s["is_rewatching"])),
            Int(s["num_episodes_watched"]) ?? 0,
            Int(s["num_times_rewatched"]) ?? 0,
            ParseDate(Str(s["start_date"])),
            ParseDate(Str(s["finish_date"])));
    }

    /// <summary>Champs à envoyer à MAL pour appliquer une décision de synchro.</summary>
    public static Dictionary<string, string> UpdateForm(SyncDecision decision, DateTime today)
    {
        var form = new Dictionary<string, string>
        {
            ["status"] = ToMalStatus(decision.Status),
            ["is_rewatching"] = decision.Status == MediaListStatus.Repeating ? "true" : "false",
            ["num_watched_episodes"] = decision.Progress.ToString(CultureInfo.InvariantCulture),
        };
        if (decision.Repeat is int repeat) form["num_times_rewatched"] = repeat.ToString(CultureInfo.InvariantCulture);
        if (decision.SetStartedToday) form["start_date"] = today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (decision.SetCompletedToday) form["finish_date"] = today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return form;
    }

    /// <summary>Champs pour remettre une fiche comme avant (« Annuler »).</summary>
    public static Dictionary<string, string> RestoreForm(int progress, string status, int? repeat)
    {
        var form = new Dictionary<string, string>
        {
            ["status"] = ToMalStatus(status),
            ["is_rewatching"] = status == MediaListStatus.Repeating ? "true" : "false",
            ["num_watched_episodes"] = progress.ToString(CultureInfo.InvariantCulture),
        };
        if (repeat is int r) form["num_times_rewatched"] = r.ToString(CultureInfo.InvariantCulture);
        return form;
    }

    /// <summary>MAL accepte des dates partielles : « 2024-05-12 », « 2024-05 » ou « 2024 ».</summary>
    public static FuzzyDate? ParseDate(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var parts = text.Split('-');
        int? Part(int i) => parts.Length > i && int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : null;
        return new FuzzyDate(Part(0), Part(1), Part(2));
    }

    static string? Str(JsonNode? n) => n is JsonValue v && v.TryGetValue(out string? s) ? s : null;
    static int? Int(JsonNode? n) => n is JsonValue v && v.TryGetValue(out int i) ? i : null;
    static bool Bool(JsonNode? n) => n is JsonValue v && v.TryGetValue(out bool b) && b;
}
