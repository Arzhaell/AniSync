using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using AniSync.Core;

namespace AniSync.AniList;

/// <summary>
/// « Traducteur » de titres : AniList ne connaît pas tous les titres français
/// (« Je veux t'aimer jusqu'à ta mort »), Wikidata si, avec leurs équivalents anglais, romaji et japonais
/// (« I Want to Love You Till Your Dying Day », « Kimi ga Shinu made Koi wo Shitai »...).
/// </summary>
public sealed class WikidataTitles
{
    const string Api = "https://www.wikidata.org/w/api.php";
    const double MinLabelSimilarity = 0.85;
    const int MaxEntities = 3;
    static readonly TimeSpan CacheTtl = TimeSpan.FromHours(6);
    static readonly HttpClient Http = CreateHttp();

    readonly ConcurrentDictionary<string, (IReadOnlyList<string> Titles, DateTime At)> _cache = new();

    static HttpClient CreateHttp()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("AniSync/1.2 (application de bureau Windows; recherche de titres d'anime)");
        return http;
    }

    DateTime _pauseUntil;

    /// <summary>
    /// Autres noms du même titre (anglais, romaji, japonais), du plus utile au moins utile.
    /// Vide si Wikidata ne connaît pas ce titre ; null si Wikidata n'a pas pu répondre (à réessayer plus tard).
    /// </summary>
    public async Task<IReadOnlyList<string>?> AlternativesAsync(string title, CancellationToken ct = default)
    {
        var norm = TitleParser.Normalize(title);
        if (norm.Length < 3) return [];
        if (_cache.TryGetValue(norm, out var cached) && DateTime.Now - cached.At < CacheTtl) return cached.Titles;
        if (DateTime.Now < _pauseUntil) return null;

        try
        {
            var search = await GetAsync(
                $"{Api}?action=wbsearchentities&search={Uri.EscapeDataString(title)}&language=fr&uselang=fr&type=item&limit=6&format=json", ct);
            var ids = RelevantIds(search, norm).Take(MaxEntities).ToList();

            IReadOnlyList<string> titles = [];
            if (ids.Count > 0)
            {
                var entities = await GetAsync(
                    $"{Api}?action=wbgetentities&ids={Uri.EscapeDataString(string.Join("|", ids))}&props=labels%7Caliases&languages=en%7Cja&format=json", ct);
                titles = ExtractTitles(entities, ids, norm);
            }

            _cache[norm] = (titles, DateTime.Now);
            return titles;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            // Wikidata injoignable ou trop sollicité : rien en cache, et on le laisse tranquille une minute.
            _pauseUntil = DateTime.Now.AddMinutes(1);
            Log.Error($"Wikidata indisponible pour « {title} »", ex);
            return null;
        }
    }

    static async Task<JsonNode?> GetAsync(string url, CancellationToken ct) =>
        JsonNode.Parse(await Http.GetStringAsync(url, ct));

    /// <summary>Les fiches dont le nom (ou un alias) correspond vraiment au titre cherché, pas seulement à son début.</summary>
    public static IEnumerable<string> RelevantIds(JsonNode? searchResponse, string norm)
    {
        if (searchResponse?["search"] is not JsonArray results) yield break;
        foreach (var item in results)
        {
            var id = Str(item?["id"]);
            var text = Str(item?["match"]?["text"]) ?? Str(item?["label"]);
            if (id is null || text is null) continue;
            if (TitleMatcher.Similarity(norm, TitleParser.Normalize(text)) >= MinLabelSimilarity) yield return id;
        }
    }

    /// <summary>Nom anglais, alias (souvent le romaji) puis nom japonais de chaque fiche, sans doublon.</summary>
    public static IReadOnlyList<string> ExtractTitles(JsonNode? entitiesResponse, IEnumerable<string> ids, string norm)
    {
        var titles = new List<string>();
        var seen = new HashSet<string> { norm };

        void Add(string? title)
        {
            if (string.IsNullOrWhiteSpace(title)) return;
            var n = TitleParser.Normalize(title);
            if (n.Length >= 2 && seen.Add(n)) titles.Add(title.Trim());
        }

        foreach (var id in ids)
        {
            var entity = entitiesResponse?["entities"]?[id];
            if (entity is null) continue;

            Add(Str(entity["labels"]?["en"]?["value"]));

            // Les alias courts sont des abréviations (« TenSura », « KimiShinu ») : inutiles pour la recherche.
            var aliases = (entity["aliases"]?["en"] as JsonArray ?? [])
                .Select(a => Str(a?["value"]))
                .Where(a => a is not null && (a.Length >= 12 || a.Contains(' ')))
                .Take(2);
            foreach (var alias in aliases) Add(alias);

            Add(Str(entity["labels"]?["ja"]?["value"]));
        }
        return titles;
    }

    static string? Str(JsonNode? n) => n is JsonValue v && v.TryGetValue(out string? s) ? s : null;
}
