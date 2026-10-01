using AniSync.Detection;

namespace AniSync.Core;

/// <summary>État d'un épisode suivi, figé pour l'affichage.</summary>
public sealed record EpisodeSnapshot(
    ParsedEpisode Parsed,
    string SourceName,
    bool IsPlaying,
    double WatchedSeconds,
    double RequiredSeconds,
    bool Completed);

/// <summary>
/// Compte le temps de visionnage RÉEL de chaque épisode : seulement quand la vidéo est en lecture
/// (les pauses et les sauts en avant ne comptent pas). Un épisode est « vu » une fois le seuil atteint.
/// </summary>
public sealed class WatchTracker
{
    /// <summary>Pour les épisodes courts, le seuil devient 85 % de la durée.</summary>
    public const double ShortEpisodeRatio = 0.85;

    static readonly TimeSpan MaxTickGap = TimeSpan.FromSeconds(5);
    static readonly TimeSpan CurrentWindow = TimeSpan.FromSeconds(20);
    static readonly TimeSpan Forget = TimeSpan.FromHours(12);

    sealed class Tracked(ParsedEpisode parsed)
    {
        public ParsedEpisode Parsed { get; } = parsed;
        public string SourceName = "";
        public double Watched;
        public TimeSpan? Duration;
        public bool IsPlaying;
        public DateTime LastSeen;
        public DateTime LastPlaying;
        public bool Completed;
    }

    readonly Dictionary<string, Tracked> _episodes = new();
    readonly Dictionary<string, ParsedEpisode> _sticky = new();
    DateTime? _lastTick;

    public static double RequiredSeconds(int minMinutes, TimeSpan? duration)
    {
        double required = Math.Max(1, minMinutes) * 60.0;
        if (duration is { TotalSeconds: > 60 } d) required = Math.Min(required, d.TotalSeconds * ShortEpisodeRatio);
        return required;
    }

    /// <summary>À appeler quand l'écoute est coupée : le temps ne s'accumule plus.</summary>
    public void Pause()
    {
        _lastTick = null;
        foreach (var e in _episodes.Values) e.IsPlaying = false;
    }

    public (EpisodeSnapshot? Current, List<EpisodeSnapshot> Completed) Tick(
        IReadOnlyList<PlaybackCandidate> candidates, AppSettings settings, DateTime now)
    {
        double elapsed = _lastTick is DateTime last ? Math.Clamp((now - last).TotalSeconds, 0, MaxTickGap.TotalSeconds) : 0;
        _lastTick = now;

        // Un même épisode peut remonter de plusieurs sources : on ne le compte qu'une fois.
        var seen = new Dictionary<string, (ParsedEpisode Parsed, PlaybackCandidate Candidate, bool Playing)>();
        foreach (var candidate in candidates)
        {
            var parsed = ParseCandidate(candidate, settings);
            if (parsed is null || settings.IsTitleIgnored(parsed.SeriesKey)) continue;
            var key = parsed.EpisodeKey;
            seen[key] = seen.TryGetValue(key, out var existing)
                ? (parsed, candidate.Duration is not null ? candidate : existing.Candidate, existing.Playing || candidate.IsPlaying)
                : (parsed, candidate, candidate.IsPlaying);
        }

        var completed = new List<EpisodeSnapshot>();
        foreach (var e in _episodes.Values) e.IsPlaying = false;

        foreach (var (key, (parsed, candidate, playing)) in seen)
        {
            if (!_episodes.TryGetValue(key, out var ep))
                _episodes[key] = ep = new Tracked(parsed);

            ep.LastSeen = now;
            ep.SourceName = candidate.SourceName;
            ep.IsPlaying = playing;
            if (candidate.Duration is not null) ep.Duration = candidate.Duration;

            if (playing)
            {
                ep.Watched += elapsed;
                ep.LastPlaying = now;
            }

            if (!ep.Completed && ep.Watched >= RequiredSeconds(settings.MinWatchMinutes, ep.Duration))
            {
                ep.Completed = true;
                completed.Add(Snapshot(ep, settings));
            }
        }

        foreach (var stale in _episodes.Where(kv => now - kv.Value.LastSeen > Forget).Select(kv => kv.Key).ToList())
            _episodes.Remove(stale);

        var current =
            _episodes.Values.Where(e => now - e.LastPlaying < CurrentWindow).MaxBy(e => e.LastPlaying) ??
            _episodes.Values.Where(e => now - e.LastSeen < CurrentWindow).MaxBy(e => e.LastSeen);

        return (current is null ? null : Snapshot(current, settings), completed);
    }

    ParsedEpisode? ParseCandidate(PlaybackCandidate candidate, AppSettings settings)
    {
        if (candidate.Parsed is not null) return candidate.Parsed;

        // Mémoriser n'a de sens que pour une session média : même titre = même vidéo.
        bool canStick = candidate.SourceKey.StartsWith("smtc|", StringComparison.Ordinal);
        for (int i = 0; i < candidate.Titles.Count; i++)
        {
            var option = candidate.Titles[i];
            if (settings.HasIgnoredKeyword(option.Text))
            {
                // Le titre du média lui-même est disqualifié (réaction, trailer...) : on ignore tout.
                if (i == 0) return null;
                continue;
            }
            var parsed = TitleParser.Parse(option.Text, option.Mode);
            if (parsed is null) continue;
            if (i > 0 && canStick)
            {
                if (_sticky.Count > 200) _sticky.Clear();
                _sticky[candidate.SourceKey] = parsed;
            }
            return parsed;
        }
        // Le lecteur a un titre générique : on garde ce qu'on avait trouvé via l'onglet tant qu'il ne change pas.
        return canStick ? _sticky.GetValueOrDefault(candidate.SourceKey) : null;
    }

    static EpisodeSnapshot Snapshot(Tracked e, AppSettings settings) =>
        new(e.Parsed, e.SourceName, e.IsPlaying, e.Watched, RequiredSeconds(settings.MinWatchMinutes, e.Duration), e.Completed);
}
