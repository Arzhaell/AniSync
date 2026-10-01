using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using AniSync.AniList;

namespace AniSync.Mal;

/// <summary>Jetons MyAnimeList : l'accès expire (environ un mois), le jeton de renouvellement permet d'en obtenir un nouveau.</summary>
public sealed record MalTokens(string AccessToken, string RefreshToken, DateTime ExpiresAtUtc);

/// <summary>
/// Connexion OAuth 2 de MyAnimeList (code d'autorisation + PKCE en mode « plain », seul mode accepté par MAL).
/// MAL renvoie vers http://localhost:47813/callback?code=... ; l'appli échange ce code contre des jetons.
/// </summary>
public static class MalAuth
{
    public const string DeveloperPage = "https://myanimelist.net/apiconfig";
    const string AuthorizeUrl = "https://myanimelist.net/v1/oauth2/authorize";
    const string TokenUrl = "https://myanimelist.net/v1/oauth2/token";
    const string VerifierChars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-._~";

    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };

    /// <summary>Code PKCE : 43 à 128 caractères parmi [A-Z a-z 0-9 - . _ ~].</summary>
    public static string NewCodeVerifier(int length = 96) =>
        new(Enumerable.Range(0, length).Select(_ => VerifierChars[RandomNumberGenerator.GetInt32(VerifierChars.Length)]).ToArray());

    public static async Task<MalTokens> AuthorizeAsync(string clientId, CancellationToken ct)
    {
        var verifier = NewCodeVerifier();
        var state = NewCodeVerifier(24);

        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://localhost:{AniListAuth.Port}/");
        try
        {
            listener.Start();
        }
        catch (HttpListenerException ex)
        {
            throw new AniListException($"Impossible d'ouvrir le port {AniListAuth.Port} ({ex.Message}). Ferme l'appli qui l'utilise et réessaie.");
        }
        using var stop = ct.Register(() => { try { listener.Stop(); } catch { } });

        var url = $"{AuthorizeUrl}?response_type=code&client_id={Uri.EscapeDataString(clientId)}" +
                  $"&code_challenge={verifier}&code_challenge_method=plain&state={state}" +
                  $"&redirect_uri={Uri.EscapeDataString(AniListAuth.RedirectUrl)}";
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

        while (true)
        {
            HttpListenerContext context;
            try
            {
                context = await listener.GetContextAsync();
            }
            catch (Exception) when (ct.IsCancellationRequested)
            {
                throw new OperationCanceledException(ct);
            }

            if (context.Request.Url?.AbsolutePath != "/callback")
            {
                context.Response.StatusCode = 404;
                context.Response.Close();
                continue;
            }

            var query = context.Request.QueryString;
            if (query["error"] is { } error)
            {
                await RespondAsync(context.Response, false, "Connexion refusée par MyAnimeList.");
                throw new AniListException($"Connexion refusée par MyAnimeList ({error}).");
            }
            if (query["state"] != state || query["code"] is not { Length: > 0 } code)
            {
                await RespondAsync(context.Response, false, "Réponse inattendue : relance la connexion depuis AniSync.");
                continue;
            }

            try
            {
                var tokens = await RequestTokensAsync(new Dictionary<string, string>
                {
                    ["client_id"] = clientId,
                    ["grant_type"] = "authorization_code",
                    ["code"] = code,
                    ["redirect_uri"] = AniListAuth.RedirectUrl,
                    ["code_verifier"] = verifier,
                }, ct);
                await RespondAsync(context.Response, true, "Tu peux fermer cet onglet.");
                return tokens;
            }
            catch (Exception ex)
            {
                await RespondAsync(context.Response, false, ex.Message);
                throw;
            }
        }
    }

    public static Task<MalTokens> RefreshAsync(string clientId, string refreshToken, CancellationToken ct) =>
        RequestTokensAsync(new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
        }, ct);

    static async Task<MalTokens> RequestTokensAsync(Dictionary<string, string> form, CancellationToken ct)
    {
        using var response = await Http.PostAsync(TokenUrl, new FormUrlEncodedContent(form), ct);
        var text = await response.Content.ReadAsStringAsync(ct);
        JsonNode? json = null;
        try { json = JsonNode.Parse(text); } catch { /* réponse non JSON */ }

        if (!response.IsSuccessStatusCode)
        {
            var error = json?["error"]?.GetValue<string>();
            if (error == "invalid_client")
                throw new AniListException("Client ID refusé par MyAnimeList : vérifie-le, et que le type d'appli est « other ».");
            if ((int)response.StatusCode >= 500)
                throw new HttpRequestException($"MyAnimeList indisponible ({(int)response.StatusCode})");
            throw new AuthExpiredException($"MyAnimeList a refusé la connexion ({error ?? ((int)response.StatusCode).ToString()}).");
        }

        var access = json?["access_token"]?.GetValue<string>();
        var refresh = json?["refresh_token"]?.GetValue<string>();
        var expiresIn = json?["expires_in"]?.GetValue<int>() ?? 3600;
        if (string.IsNullOrEmpty(access) || string.IsNullOrEmpty(refresh))
            throw new AniListException("Réponse de MyAnimeList incomplète.");
        return new MalTokens(access, refresh, DateTime.UtcNow.AddSeconds(expiresIn));
    }

    static async Task RespondAsync(HttpListenerResponse response, bool ok, string message)
    {
        var title = ok ? "Connecté à AniSync ✓" : "Connexion impossible";
        var color = ok ? "#3ddc84" : "#f2555a";
        var html = $$"""
            <!doctype html><html lang="fr"><head><meta charset="utf-8"><title>AniSync</title>
            <style>body{margin:0;height:100vh;display:grid;place-items:center;background:#0b1622;color:#e6edf3;font:16px "Segoe UI",sans-serif}
            .card{background:#152232;border:1px solid #22324a;border-radius:16px;padding:32px 40px;text-align:center;max-width:440px}
            h1{margin:0 0 8px;font-size:22px;color:{{color}}} p{margin:0;color:#8fa3b8}</style></head>
            <body><div class="card"><h1>{{WebUtility.HtmlEncode(title)}}</h1><p>{{WebUtility.HtmlEncode(message)}}</p></div></body></html>
            """;
        var bytes = Encoding.UTF8.GetBytes(html);
        response.ContentType = "text/html; charset=utf-8";
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes);
        response.Close();
    }
}
