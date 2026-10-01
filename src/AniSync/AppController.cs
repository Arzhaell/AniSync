using System.ComponentModel;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AniSync.AniList;
using AniSync.Core;
using AniSync.Detection;
using AniSync.Ui;

namespace AniSync;

/// <summary>Relie la détection, le suivi, AniList et l'interface.</summary>
public sealed class AppController : IDisposable
{
    sealed record CurrentInfo(bool Loading, AniMedia? Media, int Episode, string? Problem)
    {
        /// <summary>Animes ressemblants à faire valider, quand aucun n'a été reconnu avec certitude.</summary>
        public IReadOnlyList<Suggestion> Suggestions { get; init; } = [];

        /// <summary>L'anime sur le site de liste choisi (ta fiche, nombre d'épisodes) ; null si pas connecté.</summary>
        public ListState? State { get; init; }

        /// <summary>Le site choisi ne connaît pas cet anime (rare sur MyAnimeList).</summary>
        public bool NotOnSite { get; init; }
    }

    readonly Dispatcher _ui;
    /// <summary>Recherche AniList (avec le jeton du compte actif s'il est AniList, sinon sans compte).</summary>
    readonly AniListClient _client = new();
    readonly System.Collections.Concurrent.ConcurrentDictionary<string, IListService> _services = new();
    /// <summary>Utilisé tant qu'aucun compte n'existe : AniList, non connecté.</summary>
    readonly AniListService _guest;
    readonly MediaResolver _resolver;
    readonly SyncService _sync;
    readonly WatchTracker _tracker = new();
    readonly BrowserBridge _bridge = new();
    readonly MediaDetector _detector;
    readonly object _trackerLock = new();
    readonly CancellationTokenSource _cts = new();

    EpisodeSnapshot? _current;
    string? _infoKey;
    CurrentInfo? _info;
    CurrentInfo? _shownSuggestionsOf;
    DateTime _retryInfoAfter;
    readonly HashSet<string> _askedSeries = new();
    public MainViewModel Vm { get; } = new();
    public AppSettings Settings { get; }

    /// <summary>Titre, message : à afficher en notification Windows.</summary>
    public event Action<string, string>? Notification;
    public event Action? ListeningChanged;

    /// <summary>La liste des comptes ou le compte actif a changé (menu de la zone de notification).</summary>
    public event Action? ProfilesChanged;

    public AppController(Dispatcher ui)
    {
        _ui = ui;
        Settings = AppSettings.Load();
        if (Settings.ActiveProfile is null && Settings.ProfilesSnapshot().FirstOrDefault() is { } first)
            Settings.ActiveProfileId = first.Id;
        _guest = new AniListService(new Profile(), Settings);
        UseActiveTokenForSearch();
        _resolver = new MediaResolver(_client, Settings, new WikidataTitles());
        _sync = new SyncService(() => Active, ServiceForItem, _resolver);
        _sync.Result += item => _ui.BeginInvoke(() => OnSyncResult(item));
        _sync.AuthExpired += () => _ui.BeginInvoke(OnAuthExpired);
        _detector = new MediaDetector(_bridge);
        _bridge.IsListening = () => Settings.Listening;

        Vm.Listening = Settings.Listening;
        Vm.MinWatchMinutes = Settings.MinWatchMinutes;
        Vm.Notifications = Settings.Notifications;
        Vm.StartWithWindows = SafeStartupState();
        Vm.IgnoredCount = Settings.IgnoredTitles.Count;
        Vm.Language = Settings.Language is L.FrenchCode or L.EnglishCode ? Settings.Language : L.AutoCode;
        foreach (var item in HistoryStore.Load()) Vm.History.Add(item);
        Vm.PropertyChanged += OnVmChanged;
        RefreshAccountView();
        RefreshCurrentPanel();
    }

    // ---------- Comptes ----------

    /// <summary>La liste mise à jour : celle du compte actif (ou AniList non connecté s'il n'y a aucun compte).</summary>
    public IListService Active => Settings.ActiveProfile is { } profile ? ServiceFor(profile) : _guest;

    public IReadOnlyList<Profile> Profiles => Settings.ProfilesSnapshot();
    public bool IsActive(Profile profile) => Settings.ActiveProfileId == profile.Id;
    public bool IsSignedIn(Profile profile) => ServiceFor(profile).IsAuthenticated;

    IListService ServiceFor(Profile profile) => _services.GetOrAdd(profile.Id, _ => CreateService(profile));

    IListService CreateService(Profile profile) => profile.Service == ListSites.MyAnimeList
        ? new Mal.MalService(profile, Settings)
        : new AniListService(profile, Settings);

    /// <summary>Le compte qui a fait une mise à jour de l'historique (pour « Annuler »).</summary>
    IListService? ServiceForItem(HistoryItem item)
    {
        if (item.ProfileId is not null)
            return Settings.FindProfile(item.ProfileId) is { } owner ? ServiceFor(owner) : null;
        // Lignes d'avant les comptes multiples : le premier compte connecté du même site.
        return Settings.ProfilesSnapshot().Where(p => p.Service == item.Service).Select(ServiceFor).FirstOrDefault(s => s.IsAuthenticated);
    }

    /// <summary>
    /// La recherche AniList utilise le compte actif s'il est AniList ; sinon elle se fait sans compte,
    /// pour qu'un jeton AniList périmé ne fasse pas croire qu'un compte MyAnimeList a expiré.
    /// </summary>
    void UseActiveTokenForSearch() => _client.Token = (Active as AniListService)?.Token;

    public string? DefaultClientId(string service) => service == ListSites.MyAnimeList ? Settings.MalClientId : Settings.ClientId;

    public static string DeveloperPage(string service) => service == ListSites.MyAnimeList ? Mal.MalAuth.DeveloperPage : AniListAuth.DeveloperPage;

    /// <summary>Connecte un nouveau compte (ou reconnecte <paramref name="existing"/>) ; il devient le compte actif.</summary>
    public async Task<Viewer> ConnectAsync(string service, Profile? existing, string clientId, CancellationToken ct)
    {
        var profile = existing ?? new Profile { Service = service };
        var listService = existing is not null ? ServiceFor(existing) : CreateService(profile);
        var viewer = await listService.ConnectAsync(clientId, ct);

        var kept = Settings.AddOrMerge(profile);
        if (ReferenceEquals(kept, profile)) _services[profile.Id] = listService;
        else _services.TryRemove(kept.Id, out _); // compte déjà connu : son service sera recréé avec la nouvelle connexion

        if (service == ListSites.MyAnimeList) Settings.MalClientId = clientId;
        else Settings.ClientId = clientId;
        Settings.ActiveProfileId = kept.Id;
        Settings.Save();
        Log.Info($"Compte connecté : {kept.Label}");

        OnActiveProfileChanged();
        _ = _sync.FlushPendingAsync();
        return viewer;
    }

    public void Activate(Profile profile)
    {
        if (IsActive(profile)) return;
        Settings.ActiveProfileId = profile.Id;
        Settings.Save();
        Log.Info($"Compte actif : {profile.Label}");
        OnActiveProfileChanged();
        if (Active.IsAuthenticated)
        {
            _ = RefreshViewerAsync();
            _ = _sync.FlushPendingAsync();
        }
    }

    public void RemoveProfile(Profile profile)
    {
        Settings.RemoveProfile(profile);
        _services.TryRemove(profile.Id, out _);
        Settings.Save();
        Log.Info($"Compte retiré : {profile.Label}");
        OnActiveProfileChanged();
    }

    void OnActiveProfileChanged()
    {
        UseActiveTokenForSearch();
        RefreshAccountView();
        ReloadCurrentInfo();
    }

    void RefreshAccountView()
    {
        var profile = Settings.ActiveProfile;
        Vm.HasProfile = profile is not null;
        Vm.IsConnected = Active.IsAuthenticated;
        Vm.UserName = profile?.DisplayName;
        Vm.Avatar = LoadImage(profile?.AvatarUrl);
        (Vm.AccountCaption, Vm.AccountCaptionColor) = profile is null ? ("", "#8FA3B8")
            : Active.IsAuthenticated ? ($"{profile.Service} · {L.T("connecté", "signed in")}", "#3DDC84")
            : ($"{profile.Service} · {L.T("à reconnecter", "sign in again")}", "#F5A524");
        ProfilesChanged?.Invoke();
    }

    public ParsedEpisode? CurrentParsed => _current?.Parsed;
    public string? ProfileUrl => Settings.ActiveProfile?.PageUrl;
    public string? CurrentSiteUrl => _info?.State?.PageUrl ?? _info?.Media?.SiteUrl;
    public AniListClient Client => _client;

    public void Start()
    {
        Log.Info($"AniSync démarré (compte actif : {Settings.ActiveProfile?.Label ?? "aucun"})");
        _bridge.Start();
        ExtensionInstaller.RefreshIfExtracted();
        _ = Task.Run(() => LoopAsync(_cts.Token));
        // Nom et avatar de chaque compte connecté (et des comptes venant de l'ancien format, encore sans nom).
        foreach (var service in Settings.ProfilesSnapshot().Select(ServiceFor).Where(s => s.IsAuthenticated))
            _ = RefreshViewerAsync(service);
    }

    // ---------- Boucle de détection (thread de fond) ----------

    async Task LoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                EpisodeSnapshot? current = null;
                List<EpisodeSnapshot> completed = [];

                if (Settings.Listening)
                {
                    try
                    {
                        var candidates = await _detector.PollAsync(Settings);
                        lock (_trackerLock) (current, completed) = _tracker.Tick(candidates, Settings, DateTime.Now);
                    }
                    catch (Exception ex)
                    {
                        Log.Error("Boucle de détection", ex);
                    }
                }

                foreach (var ep in completed)
                {
                    Log.Info($"Épisode terminé : {ep.Parsed} ({ep.SourceName}, {ep.WatchedSeconds / 60:0.0} min regardées)");
                    _ = _sync.ProcessAsync(ep.Parsed);
                }

                await _ui.InvokeAsync(() =>
                {
                    _current = current;
                    RefreshCurrentPanel();
                    RefreshExtensionStatus();
                });
            }
        }
        catch (OperationCanceledException)
        {
            // arrêt de l'appli
        }
    }

    // ---------- Panneau « En cours » ----------

    void RefreshCurrentPanel()
    {
        var cur = Vm.Listening ? _current : null;
        Vm.HasCurrent = cur is not null;
        Vm.EmptyText = Vm.Listening ? L.T("Rien en lecture pour l'instant", "Nothing playing right now") : L.T("L'écoute est désactivée", "Listening is off");
        Vm.EmptyHint = Vm.Listening
            ? L.T("Lance un épisode dans ton navigateur ou ton lecteur vidéo : il apparaîtra ici.", "Start an episode in your browser or video player: it will show up here.")
            : L.T("Active l'écoute pour que tes épisodes soient comptés.", "Turn listening on so your episodes get counted.");
        if (cur is null)
        {
            ShowSuggestions(null);
            return;
        }

        if (_infoKey != cur.Parsed.EpisodeKey && DateTime.Now >= _retryInfoAfter)
        {
            _infoKey = cur.Parsed.EpisodeKey;
            _info = new CurrentInfo(true, null, cur.Parsed.Episode, null);
            _ = LoadInfoAsync(cur.Parsed);
        }
        var info = _info;
        var media = info?.Media;

        Vm.CurrentTitle = media?.Title ?? cur.Parsed.Title;
        var episode = L.T($"Épisode {cur.Parsed.Episode}", $"Episode {cur.Parsed.Episode}");
        if (cur.Parsed.Season is > 1) episode = $"{L.T("Saison", "Season")} {cur.Parsed.Season} · {episode}";
        if (media is not null && info!.Problem is null && info.Episode != cur.Parsed.Episode) episode += L.T($"  (= ép. {info.Episode} sur {Active.Name})", $"  (= ep. {info.Episode} on {Active.Name})");
        Vm.CurrentEpisode = episode;
        Vm.CurrentPlaying = cur.IsPlaying;
        Vm.CurrentSource = $"{cur.SourceName} · {(cur.IsPlaying ? L.T("en lecture", "playing") : L.T("en pause", "paused"))}";
        Vm.CurrentProgress = cur.RequiredSeconds > 0 ? Math.Clamp(cur.WatchedSeconds / cur.RequiredSeconds, 0, 1) : 0;
        Vm.CurrentCompleted = cur.Completed;
        Vm.CurrentTime = cur.Completed
            ? L.T("Épisode compté comme vu ✓", "Episode counted as watched ✓")
            : L.T($"{FormatTime(cur.WatchedSeconds)} regardées sur {FormatTime(cur.RequiredSeconds)} nécessaires", $"{FormatTime(cur.WatchedSeconds)} watched of {FormatTime(cur.RequiredSeconds)} needed");
        Vm.CurrentCover = LoadImage(media?.CoverUrl);

        (Vm.CurrentList, Vm.CurrentListColor) = DescribeList(info);
        ShowSuggestions(info);
    }

    /// <summary>Met à jour la liste des propositions seulement quand elle change (le panneau se rafraîchit chaque seconde).</summary>
    void ShowSuggestions(CurrentInfo? info)
    {
        if (ReferenceEquals(info, _shownSuggestionsOf)) return;
        _shownSuggestionsOf = info;
        Vm.Suggestions.Clear();
        foreach (var s in info?.Suggestions ?? [])
            Vm.Suggestions.Add(new SuggestionItem(s.Media.Id, s.Media.Title, Describe(s.Media), LoadImage(s.Media.CoverUrl)));
    }

    static string Describe(AniMedia m)
    {
        var parts = new List<string>();
        if (m.Format is not null) parts.Add(m.Format.Replace('_', ' '));
        if (m.Year is int year) parts.Add(year.ToString());
        if (m.Episodes is int episodes) parts.Add(L.T($"{episodes} ép.", $"{episodes} ep."));
        return string.Join(" · ", parts);
    }

    (string, string) DescribeList(CurrentInfo? info)
    {
        const string muted = "#8FA3B8", warn = "#F5A524", accent = "#3DB4F2", ok = "#3DDC84";
        if (info is null || info.Loading) return (L.T("Recherche sur AniList…", "Searching AniList…"), muted);
        if (info.Media is null && info.Suggestions.Count > 0)
            return (L.T("Je ne reconnais pas ce titre avec certitude. Est-ce l'un de ces animes ?", "I'm not sure I recognize this title. Is it one of these anime?"), warn);
        if (info.Media is null) return (info.Problem ?? L.T("Anime introuvable sur AniList : clique sur « Corriger ».", "Anime not found on AniList: click \"Fix\"."), warn);
        if (info.Problem is not null) return (info.Problem + L.T(" : clique sur « Corriger ».", ": click \"Fix\"."), warn);
        if (Settings.ActiveProfile is null) return (L.T("Ajoute un compte AniList ou MyAnimeList pour synchroniser.", "Add an AniList or MyAnimeList account to sync."), warn);
        if (!Active.IsAuthenticated) return (L.T($"Reconnecte le compte {Active.Profile.Label} pour synchroniser.", $"Sign in to {Active.Profile.Label} again to sync."), warn);
        if (info.NotOnSite) return (L.T($"Cet anime n'existe pas sur {Active.Name} : il ne pourra pas être synchronisé.", $"This anime doesn't exist on {Active.Name}: it can't be synced."), warn);
        if (info.State is null) return (L.T($"Lecture de ta liste {Active.Name}…", $"Reading your {Active.Name} list…"), muted);

        var entry = info.State.Entry;
        if (entry is null) return (L.T("Pas encore dans ta liste : il sera ajouté à la fin de l'épisode.", "Not in your list yet: it will be added when the episode ends."), accent);

        var total = info.State.TotalEpisodes is int t ? $"/{t}" : "";
        var text = L.T($"Sur ta liste : ép. {entry.Progress}{total} · {MediaListStatus.ToDisplay(entry.Status)}", $"On your list: ep. {entry.Progress}{total} · {MediaListStatus.ToDisplay(entry.Status)}");
        if (entry.Status == MediaListStatus.Completed || info.Episode <= entry.Progress)
            return (text + L.T(" · déjà vu, rien ne changera", " · already watched, nothing will change"), muted);
        return (text + L.T($" → passera à {info.Episode}", $" → will move to {info.Episode}"), ok);
    }

    async Task LoadInfoAsync(ParsedEpisode p)
    {
        CurrentInfo result;
        try
        {
            var resolution = await _resolver.ResolveAsync(p, _cts.Token);
            if (resolution is null)
            {
                result = new CurrentInfo(false, null, p.Episode, null) { Suggestions = await _resolver.SuggestAsync(p, ct: _cts.Token) };
            }
            else
            {
                // Ta fiche sur le site choisi (AniList ou MyAnimeList).
                var service = Active;
                ListState? state = null;
                if (resolution.Problem is null && service.IsAuthenticated)
                    state = await service.GetStateAsync(resolution.Media, _cts.Token);
                result = new CurrentInfo(false, resolution.Media, resolution.Episode, resolution.Problem)
                {
                    State = state,
                    NotOnSite = resolution.Problem is null && service.IsAuthenticated && state is null,
                };
            }
        }
        catch (AuthExpiredException)
        {
            OnAuthExpired();
            result = new CurrentInfo(false, null, p.Episode, L.T($"Session {Active.Name} expirée : reconnecte-toi.", $"{Active.Name} session expired: sign in again."));
        }
        catch (Exception ex)
        {
            Log.Error($"Infos AniList / {Active.Name} pour {p}", ex);
            result = new CurrentInfo(false, null, p.Episode, L.T($"{Active.Name} injoignable pour le moment.", $"Can't reach {Active.Name} right now."));
            _infoKey = null; // on réessaiera, mais pas à chaque seconde
            _retryInfoAfter = DateTime.Now.AddSeconds(30);
        }

        if (_infoKey == p.EpisodeKey || _infoKey is null)
        {
            _info = result;
            RefreshCurrentPanel();
        }
    }

    void ReloadCurrentInfo()
    {
        _infoKey = null;
        _retryInfoAfter = default;
        RefreshCurrentPanel();
    }

    // ---------- Actions de l'utilisateur ----------

    /// <summary>Associe le titre détecté à l'anime AniList choisi par l'utilisateur.</summary>
    public void FixCurrent(int mediaId)
    {
        var cur = _current;
        if (cur is null) return;
        ApplyMapping(cur.Parsed, mediaId);
        if (cur.Completed) _ = _sync.ProcessAsync(cur.Parsed);
    }

    public void FixHistory(HistoryItem item, int mediaId)
    {
        var p = item.ToParsed();
        ApplyMapping(p, mediaId);
        item.Fixed = true;
        HistoryStore.Save(Vm.History);
        _ = _sync.ProcessAsync(p);
    }

    void ApplyMapping(ParsedEpisode p, int mediaId)
    {
        Settings.SetMapping(p.SeriesKey, mediaId);
        _resolver.Forget(p.SeriesKey);
        Log.Info($"Correction : « {p.Title} » (s{p.Season ?? 1}) → AniList {mediaId}");
        ReloadCurrentInfo();
    }

    public void IgnoreCurrent()
    {
        var cur = _current;
        if (cur is null) return;
        Settings.IgnoreTitle(cur.Parsed.SeriesKey);
        Vm.IgnoredCount = Settings.IgnoredTitles.Count;
        _current = null;
        RefreshCurrentPanel();
        Log.Info($"Titre ignoré : {cur.Parsed.Title}");
    }

    public void ClearIgnored()
    {
        Settings.ClearIgnoredTitles();
        Vm.IgnoredCount = 0;
    }

    public async Task UndoAsync(HistoryItem item)
    {
        await _sync.UndoAsync(item);
        item.Undone = true;
        HistoryStore.Save(Vm.History);
        ReloadCurrentInfo();
    }

    // ---------- Événements ----------

    void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MainViewModel.Listening):
                Settings.Listening = Vm.Listening;
                Settings.Save();
                if (!Vm.Listening) lock (_trackerLock) _tracker.Pause();
                Log.Info(Vm.Listening ? "Écoute activée" : "Écoute désactivée");
                ListeningChanged?.Invoke();
                RefreshCurrentPanel();
                break;
            case nameof(MainViewModel.MinWatchMinutes):
                Settings.MinWatchMinutes = Vm.MinWatchMinutes;
                Settings.Save();
                break;
            case nameof(MainViewModel.Notifications):
                Settings.Notifications = Vm.Notifications;
                Settings.Save();
                break;
            case nameof(MainViewModel.StartWithWindows):
                try { StartupManager.Set(Vm.StartWithWindows); }
                catch (Exception ex) { Log.Error("Lancement avec Windows", ex); }
                break;
            case nameof(MainViewModel.Language):
                Settings.Language = Vm.Language;
                Settings.Save();
                Vm.NeedsRestart = L.Detect(Vm.Language) != L.IsFrench;
                break;
        }
    }

    void OnSyncResult(HistoryItem item)
    {
        Vm.History.Insert(0, item);
        while (Vm.History.Count > HistoryStore.MaxItems) Vm.History.RemoveAt(Vm.History.Count - 1);
        HistoryStore.Save(Vm.History);

        if (item.MediaId is int id) _resolver.InvalidateMedia(id);
        ReloadCurrentInfo();

        // Pas de notification pour les séries non-anime (introuvables) ni pour « déjà à jour ».
        if (Settings.Notifications && item.Kind is not (HistoryKind.Skipped or HistoryKind.Unrecognized))
            Notification?.Invoke(item.Title, item.Message);

        // Titre non reconnu mais un anime très ressemblant existe : on demande, une seule fois par série.
        // (Une ressemblance vague reste dans l'historique sans déranger : c'est souvent une série non-anime.)
        if (Settings.Notifications && item.CanConfirm && item.SuggestedScore >= NotifyScore && _askedSeries.Add(item.ToParsed().SeriesKey))
            Notification?.Invoke(L.T("AniSync a un doute", "AniSync isn't sure"), L.T($"« {item.ParsedTitle} » : est-ce « {item.SuggestedTitle} » ? Clique pour valider.", $"\"{item.ParsedTitle}\": is it \"{item.SuggestedTitle}\"? Click to confirm."));
    }

    const double NotifyScore = 0.7;

    /// <summary>« Ce n'est pas un anime » : ce titre ne sera plus suivi.</summary>
    public void IgnoreHistory(HistoryItem item)
    {
        Settings.IgnoreTitle(item.ToParsed().SeriesKey);
        Vm.IgnoredCount = Settings.IgnoredTitles.Count;
        foreach (var other in Vm.History.Where(h => h.CanFix && h.ToParsed().SeriesKey == item.ToParsed().SeriesKey).ToList())
            other.Fixed = true;
        HistoryStore.Save(Vm.History);
        Log.Info($"Titre ignoré : {item.ParsedTitle}");
    }

    /// <summary>L'utilisateur confirme l'anime proposé pour une ligne de l'historique.</summary>
    public void ConfirmHistory(HistoryItem item)
    {
        if (item.SuggestedId is int mediaId) FixHistory(item, mediaId);
    }

    /// <summary>Animes ressemblants pour la fenêtre « Corriger ».</summary>
    public async Task<IReadOnlyList<AniMedia>> SuggestAsync(ParsedEpisode p) =>
        (await _resolver.SuggestAsync(p, max: 6, ct: _cts.Token)).Select(s => s.Media).ToList();

    /// <summary>La connexion du compte actif a expiré : il reste dans la liste, à reconnecter.</summary>
    void OnAuthExpired()
    {
        if (!Vm.IsConnected) return;
        var service = Active;
        service.SignOut();
        Log.Info($"Connexion expirée : {service.Profile.Label}");
        RefreshAccountView();
        ReloadCurrentInfo();
        Notification?.Invoke("AniSync", L.T($"La connexion de {service.Profile.Label} a expiré : reconnecte-le depuis la fenêtre Comptes.", $"{service.Profile.Label} was signed out: sign in again from the Accounts window."));
    }

    /// <summary>Met à jour le nom et l'avatar d'un compte (le compte actif par défaut) : ils ont pu changer sur le site.</summary>
    async Task RefreshViewerAsync(IListService? target = null)
    {
        var service = target ?? Active;
        try
        {
            var viewer = await service.GetAccountAsync(_cts.Token);
            service.Profile.UserId = viewer.Id;
            service.Profile.UserName = viewer.Name;
            service.Profile.AvatarUrl = viewer.AvatarUrl;
            service.Profile.ProfileUrl = viewer.SiteUrl;
            Settings.Save();
            RefreshAccountView();
        }
        catch (AuthExpiredException)
        {
            if (ReferenceEquals(service, Active)) OnAuthExpired();
            else
            {
                service.SignOut();
                RefreshAccountView();
            }
        }
        catch (Exception ex)
        {
            Log.Error($"Profil {service.Profile.Label}", ex);
        }
    }

    // ---------- Utilitaires ----------

    static ImageSource? LoadImage(string? url) => Images.FromUrl(url);

    static string FormatTime(double seconds)
    {
        var t = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return $"{(int)t.TotalMinutes}:{t.Seconds:00}";
    }

    static bool SafeStartupState()
    {
        try { return StartupManager.IsEnabled(); }
        catch { return false; }
    }

    // ---------- Extension navigateur ----------

    void RefreshExtensionStatus()
    {
        var last = _bridge.LastContact;
        if (!_bridge.IsRunning)
            (Vm.ExtensionStatus, Vm.ExtensionStatusColor) = (L.T("Indisponible (port 47814 occupé)", "Unavailable (port 47814 in use)"), "#F2555A");
        else if (last is DateTime at && DateTime.UtcNow - at < TimeSpan.FromSeconds(30))
            (Vm.ExtensionStatus, Vm.ExtensionStatusColor) = (L.T($"Connectée · {_bridge.LastBrowser}", $"Connected · {_bridge.LastBrowser}"), "#3DDC84");
        else if (last is not null)
            (Vm.ExtensionStatus, Vm.ExtensionStatusColor) = (L.T($"Connectée ({_bridge.LastBrowser}) · aucune vidéo en ce moment", $"Connected ({_bridge.LastBrowser}) · no video right now"), "#8FA3B8");
        else if (ExtensionInstaller.IsExtracted)
            (Vm.ExtensionStatus, Vm.ExtensionStatusColor) = (L.T("En attente : lance une vidéo dans le navigateur", "Waiting: play a video in your browser"), "#8FA3B8");
        else
            (Vm.ExtensionStatus, Vm.ExtensionStatusColor) = (L.T("Non installée", "Not installed"), "#8FA3B8");
    }

    public void Dispose()
    {
        _cts.Cancel();
        _bridge.Dispose();
        Settings.Save();
        HistoryStore.Save(Vm.History);
        Log.Info("AniSync arrêté");
    }
}
