using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace AniSync.Core;

/// <summary>Un épisode reconnu dans un titre de fenêtre / d'onglet / de fichier.</summary>
public sealed record ParsedEpisode(string Title, int? Season, int Episode)
{
    /// <summary>Autres noms de la série à essayer sur AniList (ex. nom français en plus du nom anglais).</summary>
    public IReadOnlyList<string> AltTitles { get; init; } = [];

    /// <summary>Identifie la série (titre + saison), indépendamment de l'épisode.</summary>
    public string SeriesKey => TitleParser.SeriesKey(Title, Season);

    /// <summary>Identifie un épisode précis.</summary>
    public string EpisodeKey => $"{SeriesKey}|e{Episode}";

    public override string ToString() =>
        Season is > 1 ? $"{Title} (saison {Season}) ép. {Episode}" : $"{Title} ép. {Episode}";
}

/// <summary>
/// Niveau d'exigence pour reconnaître un numéro d'épisode.
/// Strict : seulement "S01E05" ou "Épisode 5" (titres d'onglets pas forcément liés à la vidéo).
/// Normal : + "#5", "E05", "Titre - 05" (titres de médias venant du navigateur).
/// Loose  : + "Titre 05" (noms de fichiers dans un lecteur vidéo).
/// </summary>
public enum ParseMode { Strict, Normal, Loose }

public static class TitleParser
{
    const RegexOptions Opts = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    static readonly Regex Invisible = new(@"[​-‏‪-‮⁠-⁤﻿]");
    static readonly Regex Spaces = new(@"\s+");

    static readonly Regex AppSuffix = new(
        @"\s*[-–—|]\s*(?:VLC media player|Lecteur multimédia VLC|Opera(?: GX)?|Google Chrome|Chromium|(?:Mozilla )?Firefox|Microsoft Edge|Brave|Vivaldi|MPC-HC|MPC-BE|Media Player Classic|PotPlayer|mpv(?:\.net)?|SMPlayer|KMPlayer|GOM Player|Lecteur multimédia|Media Player|Films et TV|Movies & TV|Stremio)\s*$",
        Opts);

    static readonly Regex VideoExt = new(@"\.(?:mkv|mp4|avi|webm|m4v|mov|wmv|flv|m2ts|ts|ogm|rmvb)\b", Opts);
    static readonly Regex Brackets = new(@"\[[^\]]*\]|【[^】]*】|\{[^}]*\}|「[^」]*」");
    static readonly Regex BracketChars = new(@"[\[\]【】{}「」]");
    static readonly Regex Parens = new(@"\([^)]*\)");

    // --- Marqueurs d'épisode, du plus fiable au moins fiable ---
    static readonly Regex SeasonEpisode = new(@"(?<![\p{L}\d])S(?<s>\d{1,2})\s?[._-]?\s?E(?<e>\d{1,4})(?!\d)", Opts);
    static readonly Regex EpisodeWord = new(
        @"(?<![\p{L}\d])(?:[ée]pisode|episodio|[ée]pi|eps?|folge|cap[ií]tulo)\s*\.?\s*(?:n[°º]\s*|#\s*)?(?<e>\d{1,4})(?<dec>[.,]\d+)?(?!\d)",
        Opts);
    static readonly Regex HashNumber = new(@"#(?<e>\d{1,4})(?!\d)");
    static readonly Regex BareE = new(@"(?<![\p{L}\d])E(?<e>\d{1,4})(?![\p{L}\d])"); // sensible à la casse exprès
    static readonly Regex DashNumber = new(@"\s[-–—]\s(?<e>\d{1,4})(?:v\d)?(?=\s|$|[\[(])");
    static readonly Regex TrailingNumber = new(@"\s(?<e>\d{1,3})(?:v\d)?\s*$");

    // --- Saison ---
    static readonly Regex[] SeasonPatterns =
    [
        new(@"(?<![\p{L}\d])(?:saison|season|staffel|temporada|stagione)\s*(?<n>\d{1,2})(?!\d)", Opts),
        new(@"(?<![\p{L}\d])(?<n>\d{1,2})\s*(?:st|nd|rd|th|e|è|ème|eme|er|ère|re)\s+(?:saison|season)(?!\p{L})", Opts),
        new(@"(?<![\p{L}\d])S(?<n>\d{1,2})(?![\p{L}\d])", Opts),
    ];
    static readonly (Regex Pattern, int Season)[] SeasonWords =
    [
        (new(@"(?<!\p{L})second\s+season(?!\p{L})", Opts), 2),
        (new(@"(?<!\p{L})third\s+season(?!\p{L})", Opts), 3),
        (new(@"(?<!\p{L})fourth\s+season(?!\p{L})", Opts), 4),
        (new(@"(?<!\p{L})fifth\s+season(?!\p{L})", Opts), 5),
    ];

    // --- Bruit à retirer du titre ---
    static readonly Regex Noise = new(
        @"(?<![\p{L}\d])(?:vostfr|vosta|vostf|vf|vo|subfrench|truefrench|french|multi|multisub|sub(?:bed|s)?|dub(?:bed)?|english|eng|jap|1080p|720p|480p|2160p|4k|uhd|fhd|hd|webrip|web-?dl|bluray|bdrip|x26[45]|h\.?26[45]|hevc|avc|aac|flac|10bits?|8bits?|en streaming|streaming|gratuit|complet)(?![\p{L}\d])",
        Opts);
    static readonly Regex LeadingNoise = new(@"^(?:watch|regarder|voir|streaming|anime|animes)\s+", Opts);
    static readonly Regex Years = new(@"(?<![\p{L}\d])(?:19[5-9]\d|20[0-4]\d)(?![\p{L}\d])");
    static readonly string[] SegmentSeparators = [" | ", "｜", " • "];

    public static ParsedEpisode? Parse(string? raw, ParseMode mode = ParseMode.Normal)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        string s = Spaces.Replace(Invisible.Replace(raw, ""), " ").Trim();
        for (int i = 0; i < 3; i++)
        {
            var stripped = AppSuffix.Replace(s, "");
            if (stripped == s) break;
            s = stripped;
        }
        s = VideoExt.Replace(s, " ");

        // Noms de fichiers du genre "Jujutsu.Kaisen.S02E05.1080p"
        int dots = s.Count(c => c == '.'), spaces = s.Count(c => c == ' ');
        if (dots >= 2 && spaces < 2) s = s.Replace('.', ' ');
        s = s.Replace('_', ' ');

        // Les crochets sont d'habitude des étiquettes ([SubsPlease], [1080p]) qu'on retire...
        var withoutTags = ParseCore(Spaces.Replace(Brackets.Replace(s, " "), " ").Trim(), mode);
        if (withoutTags is not null || !Brackets.IsMatch(s)) return withoutTags;

        // ...sauf quand ils font partie du nom : « [Oshi no Ko] Épisode 3 ».
        return ParseCore(Spaces.Replace(BracketChars.Replace(s, " "), " ").Trim(), mode);
    }

    static ParsedEpisode? ParseCore(string s, ParseMode mode)
    {
        int? season = null;
        Match m;
        if ((m = SeasonEpisode.Match(s)).Success)
        {
            season = int.Parse(m.Groups["s"].Value);
        }
        else if ((m = EpisodeWord.Match(s)).Success)
        {
            // "Épisode 12.5" = récap / spécial : on ne compte pas.
            if (m.Groups["dec"].Success && m.Groups["dec"].Value.Skip(1).Any(c => c != '0')) return null;
        }
        else if (mode >= ParseMode.Normal && (m = HashNumber.Match(s)).Success) { }
        else if (mode >= ParseMode.Normal && (m = BareE.Match(s)).Success) { }
        else if (mode >= ParseMode.Normal && (m = DashNumber.Match(s)).Success) { }
        else if (mode >= ParseMode.Loose && (m = TrailingNumber.Match(s)).Success)
        {
            var n = int.Parse(m.Groups["e"].Value);
            if (n is >= 1900 and <= 2100) return null;
        }
        else return null;

        int episode = int.Parse(m.Groups["e"].Value);
        string before = s[..m.Index];
        string after = s[(m.Index + m.Length)..];

        string title = CleanTitle(before, ref season);
        season ??= ExtractSeason(ref after);

        if (title.Length < 2 || !title.Any(char.IsLetter)) return null;
        return new ParsedEpisode(title, season, episode);
    }

    static string CleanTitle(string t, ref int? season)
    {
        // "Site | Anime" : on garde le morceau le plus proche du numéro d'épisode.
        var parts = t.Split(SegmentSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                     .Where(p => p.Any(char.IsLetterOrDigit)).ToArray();
        if (parts.Length > 1) t = parts[^1];

        var found = ExtractSeason(ref t);
        season ??= found;

        t = Parens.Replace(t, " ");
        t = Noise.Replace(t, " ");
        t = Years.Replace(t, " ");
        t = Spaces.Replace(t, " ").Trim();
        t = LeadingNoise.Replace(t, "");
        return t.Trim(' ', '-', '–', '—', ':', '|', '~', ',', '.', '/', '·', '•', '"', '\'', '«', '»');
    }

    static int? ExtractSeason(ref string t)
    {
        foreach (var p in SeasonPatterns)
        {
            var m = p.Match(t);
            if (!m.Success) continue;
            t = t.Remove(m.Index, m.Length);
            return int.Parse(m.Groups["n"].Value);
        }
        foreach (var (p, n) in SeasonWords)
        {
            var m = p.Match(t);
            if (!m.Success) continue;
            t = t.Remove(m.Index, m.Length);
            return n;
        }
        return null;
    }

    public static string SeriesKey(string title, int? season) => $"{Normalize(title)}|s{season ?? 1}";

    /// <summary>
    /// Retire les accents des lettres latines (« jusqu'à » → « jusqu'a », « cœur » → « coeur »)
    /// sans toucher aux autres écritures (japonais, coréen...).
    /// </summary>
    public static string RemoveLatinAccents(string s)
    {
        s = s.Replace("œ", "oe").Replace("Œ", "Oe").Replace("æ", "ae").Replace("Æ", "Ae");
        var decomposed = s.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        char lastBase = '\0';
        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
            {
                if (lastBase >= 'ɐ') sb.Append(ch); // marque d'une lettre non latine : on la garde
                continue;
            }
            lastBase = ch;
            sb.Append(ch);
        }
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    /// <summary>Minuscules, sans accents ni ponctuation, espaces simples.</summary>
    public static string Normalize(string s)
    {
        s = s.Replace("×", "x").Replace("’", "").Replace("'", "").Replace("`", "");
        var decomposed = s.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
            sb.Append(char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : ' ');
        }
        return Spaces.Replace(sb.ToString(), " ").Trim();
    }
}
