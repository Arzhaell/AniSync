using System.Text.RegularExpressions;

namespace AniSync.Core;

/// <summary>Compare un titre détecté avec les titres AniList (romaji, anglais, natif, synonymes).</summary>
public static class TitleMatcher
{
    /// <summary>Score minimal pour accepter une correspondance automatiquement.</summary>
    public const double AcceptThreshold = 0.6;

    /// <summary>Score minimal pour proposer un anime à l'utilisateur (sans le choisir à sa place).</summary>
    public const double SuggestThreshold = 0.3;

    static readonly HashSet<string> SmallWords =
    [
        "le", "la", "les", "de", "des", "du", "un", "une", "et", "en", "au", "aux", "dans", "pour", "sur", "que", "qui",
        "the", "of", "and", "an", "to", "in", "on", "for", "with", "no", "wa", "ga", "wo", "ni", "na",
        "saison", "season", "episode", "partie", "part", "film", "movie",
    ];

    /// <summary>
    /// À quel point un anime ressemble au titre détecté, pour le classement des propositions.
    /// <paramref name="queryNorm"/> est le morceau du titre qui a servi à le trouver (« frieren » pour
    /// « frieren le voyage continue ») : lui ressembler compte aussi, d'autant plus que le morceau est long.
    /// </summary>
    public static double SuggestionScore(string norm, IEnumerable<string> titles, string? queryNorm = null)
    {
        double weight = queryNorm is null || norm.Length == 0
            ? 0
            : 0.4 + 0.6 * Math.Min(1.0, (double)queryNorm.Length / norm.Length);

        double best = 0;
        foreach (var title in titles)
        {
            var other = TitleParser.Normalize(title);
            best = Math.Max(best, Similarity(norm, other));
            if (queryNorm is not null) best = Math.Max(best, weight * Similarity(queryNorm, other));
        }
        return best;
    }

    /// <summary>
    /// Recherches plus larges quand le titre complet ne donne rien de sûr : sans le sous-titre, puis le début
    /// du titre (3 mots, 2 mots, premier mot marquant), là où se trouve en général le nom propre.
    /// </summary>
    public static IEnumerable<string> RelaxedQueries(string title)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { title };

        IEnumerable<string> Candidates()
        {
            foreach (var separator in new[] { ":", " - ", " – " })
            {
                int i = title.IndexOf(separator, StringComparison.Ordinal);
                if (i > 2) yield return title[..i].Trim();
            }

            var words = title.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length > 3) yield return string.Join(' ', words.Take(3));
            if (words.Length > 2) yield return string.Join(' ', words.Take(2));

            var first = Regex.Split(TitleParser.RemoveLatinAccents(title).ToLowerInvariant(), @"[^\p{L}\p{N}]+")
                .FirstOrDefault(w => w.Length >= 5 && !SmallWords.Contains(w));
            if (first is not null && words.Length > 1) yield return first;
        }

        foreach (var raw in Candidates())
        {
            var candidate = raw.Trim(' ', ':', ',', '-', '–');
            if (candidate.Length >= 3 && seen.Add(candidate)) yield return candidate;
        }
    }

    static readonly string[] Ordinals =["", "first", "second", "third", "fourth", "fifth", "sixth", "seventh", "eighth", "ninth", "tenth"];
    static readonly string[] Romans = ["", "i", "ii", "iii", "iv", "v", "vi", "vii", "viii", "ix", "x"];

    /// <summary>Similarité entre deux titres déjà normalisés (0 à 1).</summary>
    public static double Similarity(string a, string b)
    {
        if (a.Length == 0 || b.Length == 0) return 0;
        if (a == b) return 1;

        double dice = Dice(a.Replace(" ", ""), b.Replace(" ", ""));

        // "frieren" dans "sousou no frieren" : tous les mots présents et une longueur comparable.
        var tokensB = new HashSet<string>(b.Split(' '));
        if (a.Length >= 3 && a.Split(' ').All(tokensB.Contains) && a.Length >= 0.4 * b.Length)
            dice = Math.Max(dice, 0.7);

        return dice;
    }

    /// <summary>Coefficient de Dice sur les bigrammes de caractères.</summary>
    public static double Dice(string a, string b)
    {
        if (a.Length < 2 || b.Length < 2) return a == b ? 1 : 0;
        var counts = new Dictionary<(char, char), int>();
        for (int i = 0; i < a.Length - 1; i++)
        {
            var k = (a[i], a[i + 1]);
            counts[k] = counts.GetValueOrDefault(k) + 1;
        }
        int common = 0;
        for (int i = 0; i < b.Length - 1; i++)
        {
            var k = (b[i], b[i + 1]);
            if (counts.TryGetValue(k, out var n) && n > 0)
            {
                counts[k] = n - 1;
                common++;
            }
        }
        return 2.0 * common / (a.Length - 1 + b.Length - 1);
    }

    /// <summary>Le titre (normalisé) désigne-t-il explicitement la saison n ? ("2nd season", "season 2", "ii", "... 2")</summary>
    public static bool HasSeasonMarker(string norm, int n)
    {
        if (n < 2) return false;
        var patterns = new List<string>
        {
            $@"\b{n}(st|nd|rd|th)? season\b",
            $@"\bseason {n}\b",
            $@"\bsaison {n}\b",
            $@"\b{n}$",
        };
        if (n < Ordinals.Length) patterns.Add($@"\b{Ordinals[n]} season\b");
        if (n < Romans.Length) patterns.Add($@"\b{Romans[n]}\b");
        return patterns.Any(p => Regex.IsMatch(norm, p));
    }

    /// <summary>Le titre porte-t-il une marque de suite (saison 2 ou plus) ?</summary>
    public static bool LooksLikeSequel(string norm) =>
        Enumerable.Range(2, 8).Any(n => HasSeasonMarker(norm, n));

    /// <summary>Retire les mentions de saison d'un titre normalisé.</summary>
    public static string StripSeason(string norm)
    {
        var s = Regex.Replace(norm, @"\b\d{1,2}(st|nd|rd|th)? season\b|\b(season|saison) \d{1,2}\b|\b(second|third|fourth|fifth|sixth) season\b", " ");
        s = Regex.Replace(s, @"\b(ii|iii|iv|v|vi)\b|\b\d{1,2}$", " ");
        return Regex.Replace(s, @"\s+", " ").Trim();
    }
}
