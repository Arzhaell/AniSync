using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using AniSync.Core;

namespace AniSync.AniList;

/// <summary>Accès à l'API GraphQL d'AniList (https://docs.anilist.co).</summary>
public sealed class AniListClient
{
    const string Endpoint = "https://graphql.anilist.co";

    const string MediaFields =
        "id idMal episodes format seasonYear siteUrl title { romaji english native userPreferred } synonyms coverImage { large }";

    const string SearchQuery =
        "query ($search: String) { Page(perPage: 10) { media(search: $search, type: ANIME, sort: SEARCH_MATCH) { " + MediaFields + " } } }";

    const string MediaQuery =
        "query ($id: Int) { Media(id: $id, type: ANIME) { " + MediaFields +
        " relations { edges { relationType(version: 2) node { id type format } } }" +
        " mediaListEntry { id status progress repeat startedAt { year month day } completedAt { year month day } } } }";

    const string ViewerQuery = "query { Viewer { id name siteUrl avatar { medium } } }";

    static readonly HttpClient Http = CreateHttp();

    public string? Token { get; set; }
    public bool IsAuthenticated => !string.IsNullOrEmpty(Token);

    static HttpClient CreateHttp()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("AniSync/1.0");
        return http;
    }

    public async Task<Viewer> GetViewerAsync(CancellationToken ct = default)
    {
        var data = await QueryAsync(ViewerQuery, null, ct);
        var v = data["Viewer"] ?? throw new AniListAuthException(L.T("Compte AniList introuvable", "AniList account not found"));
        return new Viewer(Int(v["id"]) ?? 0, Str(v["name"]) ?? "?", Str(v["avatar"]?["medium"]), Str(v["siteUrl"]));
    }

    /// <summary>
    /// Recherche d'anime par nom (titres et synonymes). La recherche d'AniList ne renvoie rien quand le texte
    /// contient une lettre accentuée (« jusqu'à », « réincarne ») alors que le même titre sans accents est
    /// trouvé dans les synonymes : on cherche donc les deux formes et on fusionne les résultats.
    /// </summary>
    public async Task<IReadOnlyList<AniMedia>> SearchAsync(string search, CancellationToken ct = default)
    {
        var results = await SearchExactAsync(search, ct);
        var plain = TitleParser.RemoveLatinAccents(search);
        if (plain == search) return results;

        var merged = results.ToList();
        foreach (var media in await SearchExactAsync(plain, ct))
            if (merged.All(m => m.Id != media.Id)) merged.Add(media);
        return merged;
    }

    async Task<IReadOnlyList<AniMedia>> SearchExactAsync(string search, CancellationToken ct)
    {
        var data = await QueryAsync(SearchQuery, new Dictionary<string, object?> { ["search"] = search }, ct);
        return (data["Page"]?["media"] as JsonArray ?? [])
            .Where(n => n is not null)
            .Select(n => ParseMedia(n!))
            .ToList();
    }

    /// <summary>Un anime avec ses relations et l'entrée de la liste de l'utilisateur (si connecté).</summary>
    public async Task<AniMedia> GetMediaAsync(int id, CancellationToken ct = default)
    {
        var data = await QueryAsync(MediaQuery, new Dictionary<string, object?> { ["id"] = id }, ct);
        return ParseMedia(data["Media"] ?? throw new AniListException(L.T($"Anime {id} introuvable", $"Anime {id} not found")));
    }

    public async Task<ListEntry> SaveEntryAsync(int mediaId, SyncDecision decision, CancellationToken ct = default)
    {
        var args = new Dictionary<string, (string Type, object? Value)>
        {
            ["mediaId"] = ("Int", mediaId),
            ["progress"] = ("Int", decision.Progress),
            ["status"] = ("MediaListStatus", decision.Status),
        };
        if (decision.Repeat is int repeat) args["repeat"] = ("Int", repeat);
        if (decision.SetStartedToday) args["startedAt"] = ("FuzzyDateInput", Today());
        if (decision.SetCompletedToday) args["completedAt"] = ("FuzzyDateInput", Today());
        return await SaveAsync(args, ct);
    }

    /// <summary>Remet une entrée dans un état précédent (pour « Annuler »).</summary>
    public Task<ListEntry> RestoreEntryAsync(int mediaId, int progress, string status, int? repeat, CancellationToken ct = default)
    {
        var args = new Dictionary<string, (string Type, object? Value)>
        {
            ["mediaId"] = ("Int", mediaId),
            ["progress"] = ("Int", progress),
            ["status"] = ("MediaListStatus", status),
        };
        if (repeat is int r) args["repeat"] = ("Int", r);
        return SaveAsync(args, ct);
    }

    public async Task DeleteEntryAsync(int entryId, CancellationToken ct = default)
    {
        await QueryAsync("mutation ($id: Int) { DeleteMediaListEntry(id: $id) { deleted } }",
            new Dictionary<string, object?> { ["id"] = entryId }, ct);
    }

    async Task<ListEntry> SaveAsync(Dictionary<string, (string Type, object? Value)> args, CancellationToken ct)
    {
        // Seuls les champs fournis sont envoyés : AniList ne touche pas aux autres.
        var defs = string.Join(", ", args.Select(a => $"${a.Key}: {a.Value.Type}"));
        var call = string.Join(", ", args.Select(a => $"{a.Key}: ${a.Key}"));
        var query = $"mutation ({defs}) {{ SaveMediaListEntry({call}) {{ id status progress repeat }} }}";
        var data = await QueryAsync(query, args.ToDictionary(a => a.Key, a => a.Value.Value), ct);
        var e = data["SaveMediaListEntry"] ?? throw new AniListException(L.T("Réponse AniList vide", "Empty response from AniList"));
        return new ListEntry(Int(e["id"]) ?? 0, Str(e["status"]) ?? "", Int(e["progress"]) ?? 0, Int(e["repeat"]) ?? 0, null, null);
    }

    async Task<JsonNode> QueryAsync(string query, Dictionary<string, object?>? variables, CancellationToken ct)
    {
        for (int attempt = 0; ; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
            {
                Content = JsonContent.Create(new { query, variables = variables ?? new Dictionary<string, object?>() }),
            };
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            if (IsAuthenticated) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token);

            using var response = await Http.SendAsync(request, ct);

            if (response.StatusCode == HttpStatusCode.TooManyRequests && attempt < 2)
            {
                var wait = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(30);
                Log.Info($"AniList : limite de requêtes atteinte, pause de {wait.TotalSeconds:0} s");
                await Task.Delay(wait > TimeSpan.FromSeconds(65) ? TimeSpan.FromSeconds(65) : wait, ct);
                continue;
            }

            var text = await response.Content.ReadAsStringAsync(ct);
            JsonNode? root = null;
            try { root = JsonNode.Parse(text); } catch { /* réponse non JSON */ }

            var errors = root?["errors"] as JsonArray;
            string? message = errors is { Count: > 0 } ? Str(errors[0]?["message"]) : null;

            if (response.StatusCode == HttpStatusCode.Unauthorized ||
                (message?.Contains("invalid token", StringComparison.OrdinalIgnoreCase) ?? false))
                throw new AniListAuthException(message ?? L.T("Jeton AniList invalide", "Invalid AniList token"));

            if ((int)response.StatusCode >= 500)
                throw new HttpRequestException(L.T($"AniList indisponible ({(int)response.StatusCode})", $"AniList unavailable ({(int)response.StatusCode})"));

            if (message is not null) throw new AniListException(message);
            if (!response.IsSuccessStatusCode) throw new AniListException(L.T($"Erreur AniList HTTP {(int)response.StatusCode}", $"AniList HTTP error {(int)response.StatusCode}"));

            return root?["data"] ?? throw new AniListException(L.T("Réponse AniList vide", "Empty response from AniList"));
        }
    }

    static AniMedia ParseMedia(JsonNode m)
    {
        var t = m["title"];
        var titles = new List<string>();
        foreach (var key in new[] { "userPreferred", "romaji", "english", "native" })
            if (Str(t?[key]) is { Length: > 0 } s && !titles.Contains(s)) titles.Add(s);
        if (m["synonyms"] is JsonArray synonyms)
            foreach (var syn in synonyms)
                if (Str(syn) is { Length: > 0 } s && !titles.Contains(s)) titles.Add(s);

        var relations = new List<MediaRelation>();
        if (m["relations"]?["edges"] is JsonArray edges)
        {
            foreach (var edge in edges)
            {
                var node = edge?["node"];
                if (node is null) continue;
                relations.Add(new MediaRelation(Str(edge!["relationType"]) ?? "", Int(node["id"]) ?? 0, Str(node["type"]), Str(node["format"])));
            }
        }

        ListEntry? entry = null;
        if (m["mediaListEntry"] is JsonObject e)
        {
            entry = new ListEntry(
                Int(e["id"]) ?? 0,
                Str(e["status"]) ?? MediaListStatus.Current,
                Int(e["progress"]) ?? 0,
                Int(e["repeat"]) ?? 0,
                Date(e["startedAt"]),
                Date(e["completedAt"]));
        }

        return new AniMedia(
            Int(m["id"]) ?? 0,
            Str(t?["userPreferred"]) ?? Str(t?["romaji"]) ?? "?",
            titles,
            Int(m["episodes"]),
            Str(m["format"]),
            Int(m["seasonYear"]),
            Str(m["coverImage"]?["large"]),
            Str(m["siteUrl"]),
            relations,
            entry) { MalId = Int(m["idMal"]) };
    }

    static FuzzyDate? Date(JsonNode? n) => n is null ? null : new FuzzyDate(Int(n["year"]), Int(n["month"]), Int(n["day"]));

    static object Today()
    {
        var d = DateTime.Today;
        return new Dictionary<string, int> { ["year"] = d.Year, ["month"] = d.Month, ["day"] = d.Day };
    }

    static string? Str(JsonNode? n) => n is JsonValue v && v.TryGetValue(out string? s) ? s : null;
    static int? Int(JsonNode? n) => n is JsonValue v && v.TryGetValue(out int i) ? i : null;
}
