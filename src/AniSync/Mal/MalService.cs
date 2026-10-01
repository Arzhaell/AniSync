using System.Net;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using AniSync.AniList;
using AniSync.Core;

namespace AniSync.Mal;

/// <summary>
/// La liste MyAnimeList d'un compte (API v2 officielle). L'anime est trouvé via AniList, qui donne son numéro MAL.
/// </summary>
public sealed class MalService : IListService
{
    const string Api = "https://api.myanimelist.net/v2";
    static readonly TimeSpan RefreshMargin = TimeSpan.FromDays(1);
    static readonly HttpClient Http = CreateHttp();

    readonly AppSettings _settings;
    readonly SemaphoreSlim _refreshGate = new(1, 1);
    MalTokens? _tokens;

    public MalService(Profile profile, AppSettings settings)
    {
        Profile = profile;
        _settings = settings;
        _tokens = ReadTokens(profile);
    }

    public string Name => ListSites.MyAnimeList;
    public Profile Profile { get; }
    public bool IsAuthenticated => _tokens is not null;
    public string DeveloperPage => MalAuth.DeveloperPage;

    static MalTokens? ReadTokens(Profile profile)
    {
        var json = Secrets.Decrypt(profile.EncryptedToken);
        if (json is null) return null;
        try { return System.Text.Json.JsonSerializer.Deserialize<MalTokens>(json); }
        catch { return null; }
    }

    void StoreTokens(MalTokens? tokens)
    {
        _tokens = tokens;
        Profile.EncryptedToken = tokens is null ? null : Secrets.Encrypt(System.Text.Json.JsonSerializer.Serialize(tokens));
    }

    static HttpClient CreateHttp()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("AniSync/1.4");
        return http;
    }

    public async Task<Viewer> ConnectAsync(string clientId, CancellationToken ct)
    {
        var tokens = await MalAuth.AuthorizeAsync(clientId, ct);
        var previous = _tokens;
        var previousClientId = Profile.ClientId;
        _tokens = tokens;
        Profile.ClientId = clientId; // nécessaire au renouvellement des jetons
        try
        {
            var viewer = await GetAccountAsync(ct);
            Profile.ApplyViewer(viewer, clientId);
            StoreTokens(tokens);
            return viewer;
        }
        catch
        {
            _tokens = previous;
            Profile.ClientId = previousClientId;
            throw;
        }
    }

    public void SignOut()
    {
        StoreTokens(null);
        _settings.Save();
    }

    public async Task<Viewer> GetAccountAsync(CancellationToken ct = default)
    {
        var me = await SendAsync(HttpMethod.Get, "/users/@me?fields=picture", null, ct);
        var name = Str(me["name"]) ?? "?";
        return new Viewer(Int(me["id"]) ?? 0, name, Str(me["picture"]), $"https://myanimelist.net/profile/{Uri.EscapeDataString(name)}");
    }

    public async Task<ListState?> GetStateAsync(AniMedia media, CancellationToken ct = default)
    {
        if (media.MalId is not int malId) return null;
        var (entry, total) = await GetEntryAsync(malId, ct);
        return new ListState(malId, entry, total ?? media.Episodes, PageUrl(malId));
    }

    public async Task<ListEntry> SaveAsync(ListState state, SyncDecision decision, CancellationToken ct = default)
    {
        var saved = await SendAsync(HttpMethod.Patch, $"/anime/{state.SiteId}/my_list_status", MalMapping.UpdateForm(decision, DateTime.Today), ct);
        return MalMapping.ParseEntry(state.SiteId, saved) ?? throw new AniListException("Réponse de MyAnimeList incomplète.");
    }

    public async Task UndoAsync(HistoryItem item, CancellationToken ct = default)
    {
        if (item.SiteId is not int malId) return;
        var (entry, _) = await GetEntryAsync(malId, ct);
        if (entry is null)
            throw new AniListException("Cet anime n'est plus dans ta liste.");
        if (item.NewProgress is int expected && entry.Progress != expected)
            throw new AniListException($"Ta liste a changé depuis (ép. {entry.Progress} maintenant) : annulation impossible.");

        if (item.WasCreated)
            await SendAsync(HttpMethod.Delete, $"/anime/{malId}/my_list_status", null, ct);
        else
            await SendAsync(HttpMethod.Patch, $"/anime/{malId}/my_list_status",
                MalMapping.RestoreForm(item.PrevProgress ?? 0, item.PrevStatus ?? MediaListStatus.Current, item.PrevRepeat), ct);
    }

    public static string PageUrl(int malId) => $"https://myanimelist.net/anime/{malId}";

    async Task<(ListEntry? Entry, int? Total)> GetEntryAsync(int malId, CancellationToken ct)
    {
        var anime = await SendAsync(HttpMethod.Get,
            $"/anime/{malId}?fields=num_episodes,my_list_status{{start_date,finish_date,num_times_rewatched}}", null, ct);
        int? total = Int(anime["num_episodes"]) is int n && n > 0 ? n : null; // 0 = inconnu (en cours de diffusion)
        return (MalMapping.ParseEntry(malId, anime["my_list_status"]), total);
    }

    async Task<JsonNode> SendAsync(HttpMethod method, string path, Dictionary<string, string>? form, CancellationToken ct, bool retried = false)
    {
        await EnsureFreshTokenAsync(force: false, ct);

        using var request = new HttpRequestMessage(method, Api + path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _tokens!.AccessToken);
        if (form is not null) request.Content = new FormUrlEncodedContent(form);

        using var response = await Http.SendAsync(request, ct);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            if (retried) throw new AuthExpiredException("Session MyAnimeList expirée : reconnecte-toi.");
            await EnsureFreshTokenAsync(force: true, ct);
            return await SendAsync(method, path, form, ct, retried: true);
        }

        var text = await response.Content.ReadAsStringAsync(ct);
        if ((int)response.StatusCode >= 500 || response.StatusCode == HttpStatusCode.TooManyRequests)
            throw new HttpRequestException($"MyAnimeList indisponible ({(int)response.StatusCode})");
        if (method == HttpMethod.Delete && response.StatusCode == HttpStatusCode.NotFound)
            return new JsonObject(); // déjà retiré
        if (!response.IsSuccessStatusCode)
        {
            string? message = null;
            try { message = JsonNode.Parse(text)?["message"]?.GetValue<string>(); } catch { /* non JSON */ }
            throw new AniListException($"MyAnimeList : {message ?? $"erreur HTTP {(int)response.StatusCode}"}");
        }

        try { return JsonNode.Parse(text) ?? new JsonObject(); }
        catch { return new JsonObject(); }
    }

    async Task EnsureFreshTokenAsync(bool force, CancellationToken ct)
    {
        if (_tokens is null) throw new AuthExpiredException("Pas connecté à MyAnimeList.");
        if (!force && _tokens.ExpiresAtUtc - DateTime.UtcNow > RefreshMargin) return;

        await _refreshGate.WaitAsync(ct);
        try
        {
            if (_tokens is null) throw new AuthExpiredException("Pas connecté à MyAnimeList.");
            if (!force && _tokens.ExpiresAtUtc - DateTime.UtcNow > RefreshMargin) return; // déjà renouvelé entre-temps
            var clientId = Profile.ClientId ?? throw new AuthExpiredException("Client ID MyAnimeList manquant : reconnecte-toi.");

            try
            {
                StoreTokens(await MalAuth.RefreshAsync(clientId, _tokens.RefreshToken, ct));
                _settings.Save();
                Log.Info($"Jetons MyAnimeList renouvelés ({Profile.DisplayName})");
            }
            catch (AuthExpiredException)
            {
                SignOut();
                throw new AuthExpiredException("Session MyAnimeList expirée : reconnecte-toi.");
            }
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    static string? Str(JsonNode? n) => n is JsonValue v && v.TryGetValue(out string? s) ? s : null;
    static int? Int(JsonNode? n) => n is JsonValue v && v.TryGetValue(out int i) ? i : null;
}
