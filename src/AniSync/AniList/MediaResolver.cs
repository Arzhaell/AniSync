using System.Collections.Concurrent;
using AniSync.Core;

namespace AniSync.AniList;

/// <summary>L'anime AniList correspondant à un épisode détecté, et le numéro d'épisode dans cet anime.</summary>
public sealed record Resolution(AniMedia Media, int Episode, string? Problem = null);

/// <summary>Un anime au titre proche de celui détecté, à faire valider par l'utilisateur.</summary>
public sealed record Suggestion(AniMedia Media, double Score);

/// <summary>
/// Trouve l'anime AniList qui correspond à un titre détecté.
/// Gère les saisons ("Saison 2") et la numérotation continue (ép. 30 d'une série → ép. 6 de la saison 2).
/// </summary>
public sealed class MediaResolver(AniListClient client, AppSettings settings, WikidataTitles? translator = null)
{
    static readonly TimeSpan NotFoundTtl = TimeSpan.FromMinutes(20);
    static readonly TimeSpan MediaTtl = TimeSpan.FromMinutes(10);
    static readonly HashSet<string> SeriesFormats = ["TV", "TV_SHORT", "ONA"];

    readonly ConcurrentDictionary<string, (int? Id, DateTime At)> _series = new();
    readonly ConcurrentDictionary<int, (AniMedia Media, DateTime At)> _media = new();
    readonly ConcurrentDictionary<string, (IReadOnlyList<Suggestion> List, DateTime At)> _suggestions = new();

    public void Forget(string seriesKey)
    {
        _series.TryRemove(seriesKey, out _);
        _suggestions.TryRemove(seriesKey, out _);
    }

    public void InvalidateMedia(int id) => _media.TryRemove(id, out _);

    /// <summary>
    /// Quand aucun anime n'est assez proche pour être choisi automatiquement : les plus ressemblants,
    /// du plus au moins probable, pour que l'utilisateur valide lui-même.
    /// </summary>
    public async Task<IReadOnlyList<Suggestion>> SuggestAsync(ParsedEpisode p, int max = 3, CancellationToken ct = default)
    {
        const int kept = 6; // on en garde un peu plus en mémoire pour la fenêtre « Corriger »
        const double convincing = 0.75; // en dessous, on élargit la recherche (AniList limite le nombre de requêtes)
        if (_suggestions.TryGetValue(p.SeriesKey, out var cached) && DateTime.Now - cached.At < NotFoundTtl)
            return cached.List.Take(max).ToList();

        var found = new Dictionary<int, Suggestion>();

        // compareTo : le titre complet ; part : le morceau de titre cherché, s'il ne s'agit pas du titre entier.
        async Task CollectAsync(string query, string compareTo, string? part = null)
        {
            foreach (var media in await client.SearchAsync(query, ct))
            {
                double score = TitleMatcher.SuggestionScore(compareTo, media.AllTitles, part);
                if (!found.TryGetValue(media.Id, out var existing) || score > existing.Score)
                    found[media.Id] = new Suggestion(media, score);
            }
        }

        bool Convinced() => found.Values.Any(s => s.Score >= convincing);

        // 1) Le titre tel quel, puis ses autres noms connus.
        var titles = new List<string> { p.Title };
        titles.AddRange(p.AltTitles);
        foreach (var title in titles) await CollectAsync(title, TitleParser.Normalize(title));

        // 2) Ses traductions (Wikidata), si AniList n'a rien de convaincant.
        if (!Convinced() && translator is not null)
            foreach (var translated in (await translator.AlternativesAsync(p.Title, ct) ?? []).Take(2))
                await CollectAsync(translated, TitleParser.Normalize(translated));

        // 3) Des recherches plus larges : sans le sous-titre, puis le début du titre.
        if (!Convinced())
        {
            string full = TitleParser.Normalize(p.Title);
            foreach (var query in TitleMatcher.RelaxedQueries(p.Title).Take(4))
                await CollectAsync(query, full, TitleParser.Normalize(query));
        }

        var list = found.Values
            .Where(s => s.Score >= TitleMatcher.SuggestThreshold)
            .OrderByDescending(s => s.Score)
            .Take(kept)
            .ToList();
        _suggestions[p.SeriesKey] = (list, DateTime.Now);
        return list.Take(max).ToList();
    }

    public async Task<Resolution?> ResolveAsync(ParsedEpisode p, CancellationToken ct = default)
    {
        int? baseId = settings.GetMapping(p.SeriesKey);
        if (baseId is null)
        {
            if (_series.TryGetValue(p.SeriesKey, out var cached) && (cached.Id is not null || DateTime.Now - cached.At < NotFoundTtl))
            {
                baseId = cached.Id;
            }
            else
            {
                baseId = await FindSeriesAsync(p, ct);
                // Autres noms connus (ex. nom français donné par Crunchyroll en plus du nom anglais).
                foreach (var alt in p.AltTitles)
                {
                    if (baseId is not null) break;
                    baseId = await FindSeriesAsync(p with { Title = alt }, ct);
                }

                // Titre français inconnu d'AniList : Wikidata donne ses équivalents anglais / romaji / japonais.
                bool translatorFailed = false;
                if (baseId is null && translator is not null)
                {
                    var translations = await translator.AlternativesAsync(p.Title, ct);
                    translatorFailed = translations is null;
                    foreach (var translated in translations ?? [])
                    {
                        baseId = await FindSeriesAsync(p with { Title = translated }, ct);
                        if (baseId is null) continue;
                        Log.Info($"Titre traduit via Wikidata : {p.Title} = {translated}");
                        break;
                    }
                }

                // Wikidata n'a pas pu répondre : on ne retient pas « introuvable », on réessaiera.
                if (baseId is not null || !translatorFailed) _series[p.SeriesKey] = (baseId, DateTime.Now);
                Log.Info(baseId is null ? $"Introuvable sur AniList : {p}" : $"« {p.Title} » (s{p.Season ?? 1}) → AniList {baseId}");
            }
        }
        if (baseId is null) return null;

        var media = await GetMediaAsync(baseId.Value, ct);
        int episode = p.Episode;

        // « Saison 2, épisode 25 » : numéroté depuis le début de la série (Crunchyroll le fait souvent).
        if (p.Season is > 1 && media.Episodes is int seasonCount && episode > seasonCount)
        {
            int before = await EpisodesBeforeAsync(media, ct);
            if (before > 0 && episode - before >= 1 && episode - before <= seasonCount) episode -= before;
        }

        // Numérotation continue : on avance dans les suites tant que l'épisode dépasse la saison.
        for (int hop = 0; hop < 10 && media.Episodes is int count && episode > count; hop++)
        {
            var sequel = NextSeason(media);
            if (sequel is null) break;
            episode -= count;
            media = await GetMediaAsync(sequel.Value, ct);
        }

        if (media.Episodes is int total && episode > total)
            return new Resolution(media, episode, L.T($"Épisode {p.Episode} alors que «\u00A0{media.Title}\u00A0» n'en a que {total}", $"Episode {p.Episode} but \"{media.Title}\" only has {total}"));

        return new Resolution(media, episode);
    }

    async Task<int?> FindSeriesAsync(ParsedEpisode p, CancellationToken ct)
    {
        string norm = TitleParser.Normalize(p.Title);
        int season = p.Season ?? 1;

        if (season > 1)
        {
            // 1) Une entrée AniList qui porte le numéro de saison ("... 2nd Season", "... Season 2", "... II").
            var results = await client.SearchAsync($"{p.Title} season {season}", ct);
            var best = results
                .Select((m, rank) => (m, rank, score: SeasonScore(norm, season, m)))
                .Where(x => x.score >= TitleMatcher.AcceptThreshold)
                .OrderByDescending(x => x.score).ThenBy(x => x.rank)
                .FirstOrDefault();
            if (best.m is not null) return best.m.Id;

            // 2) Sinon : la saison 1, puis on suit les suites.
            var firstId = await BestMatchAsync(p.Title, 1, ct);
            if (firstId is null) return null;
            var media = await GetMediaAsync(firstId.Value, ct);
            for (int s = 1; s < season; s++)
            {
                var next = NextSeason(media);
                if (next is null) return null;
                media = await GetMediaAsync(next.Value, ct);
            }
            return media.Id;
        }

        return await BestMatchAsync(p.Title, season, ct);
    }

    async Task<int?> BestMatchAsync(string title, int season, CancellationToken ct)
    {
        // (texte cherché sur AniList, titre auquel comparer les résultats)
        string fullNorm = TitleParser.Normalize(title);
        var queries = new List<(string Query, string Norm)> { (title, fullNorm) };
        int colon = title.IndexOf(':');
        if (colon > 2) queries.Add((title[..colon].Trim(), TitleParser.Normalize(title[..colon])));

        // Mots collés (« BLUELOCK » pour « Blue Lock ») : AniList ne les trouve pas,
        // mais cherche bien le début (« BLUE ») ; la comparaison se fait sans espaces.
        if (!fullNorm.Contains(' ') && fullNorm.Length >= 7)
            queries.Add((fullNorm[..Math.Max(4, fullNorm.Length / 2)], fullNorm));

        foreach (var (query, norm) in queries)
        {
            var results = await client.SearchAsync(query, ct);
            var best = results
                .Select((m, rank) => (m, score: Score(norm, m, season) - rank * 0.01))
                .OrderByDescending(x => x.score)
                .FirstOrDefault();
            if (best.m is not null && best.score >= TitleMatcher.AcceptThreshold) return best.m.Id;
        }
        return null;
    }

    static double Score(string norm, AniMedia m, int season)
    {
        double best = 0;
        foreach (var t in m.AllTitles)
        {
            var tn = TitleParser.Normalize(t);
            var s = TitleMatcher.Similarity(norm, tn);
            if (season == 1 && TitleMatcher.LooksLikeSequel(tn) && !TitleMatcher.LooksLikeSequel(norm)) s -= 0.1;
            best = Math.Max(best, s);
        }
        return best + (m.Format is not null && SeriesFormats.Contains(m.Format) ? 0.02 : 0);
    }

    static double SeasonScore(string norm, int season, AniMedia m)
    {
        double best = 0;
        foreach (var t in m.AllTitles)
        {
            var tn = TitleParser.Normalize(t);
            if (!TitleMatcher.HasSeasonMarker(tn, season)) continue;
            best = Math.Max(best, TitleMatcher.Similarity(norm, TitleMatcher.StripSeason(tn)));
        }
        return best;
    }

    static int? NextSeason(AniMedia m) => Related(m, "SEQUEL");

    static int? Related(AniMedia m, string relation) =>
        m.Relations
            .Where(r => r.RelationType == relation && r.Type == "ANIME" && r.Format is not null && SeriesFormats.Contains(r.Format))
            .OrderBy(r => r.Format == "TV" ? 0 : 1)
            .Select(r => (int?)r.Id)
            .FirstOrDefault();

    /// <summary>Nombre total d'épisodes des saisons précédentes (0 si inconnu).</summary>
    async Task<int> EpisodesBeforeAsync(AniMedia media, CancellationToken ct)
    {
        int total = 0;
        for (int hop = 0; hop < 10 && Related(media, "PREQUEL") is int prequelId; hop++)
        {
            media = await GetMediaAsync(prequelId, ct);
            if (media.Episodes is not int count) return 0;
            total += count;
        }
        return total;
    }

    async Task<AniMedia> GetMediaAsync(int id, CancellationToken ct)
    {
        if (_media.TryGetValue(id, out var cached) && DateTime.Now - cached.At < MediaTtl) return cached.Media;
        var media = await client.GetMediaAsync(id, ct);
        _media[id] = (media, DateTime.Now);
        return media;
    }
}
