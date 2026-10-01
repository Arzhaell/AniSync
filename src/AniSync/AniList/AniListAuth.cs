using System.Diagnostics;
using System.Net;
using System.Text;
using AniSync.Core;

namespace AniSync.AniList;

/// <summary>
/// Connexion OAuth "implicit grant" : AniList renvoie le jeton vers http://localhost:47813/callback,
/// une petite page locale le récupère et le transmet à l'appli. Rien ne transite ailleurs.
/// </summary>
public static class AniListAuth
{
    public const int Port = 47813;
    public const string RedirectUrl = "http://localhost:47813/callback";
    public const string DeveloperPage = "https://anilist.co/settings/developer";

    public static async Task<string> AuthorizeAsync(string clientId, CancellationToken ct)
    {
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://localhost:{Port}/");
        try
        {
            listener.Start();
        }
        catch (HttpListenerException ex)
        {
            throw new AniListException(L.T($"Impossible d'ouvrir le port {Port} ({ex.Message}). Ferme l'appli qui l'utilise et réessaie.", $"Couldn't open port {Port} ({ex.Message}). Close the app using it and try again."));
        }

        using var stop = ct.Register(() => { try { listener.Stop(); } catch { } });

        var authorizeUrl = $"https://anilist.co/api/v2/oauth/authorize?client_id={Uri.EscapeDataString(clientId)}&response_type=token";
        Process.Start(new ProcessStartInfo(authorizeUrl) { UseShellExecute = true });

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

            var path = context.Request.Url?.AbsolutePath ?? "";
            if (path == "/callback")
            {
                await RespondAsync(context.Response, "text/html", CallbackPage);
            }
            else if (path == "/token" && context.Request.HttpMethod == "POST")
            {
                using var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8);
                var token = (await reader.ReadToEndAsync(ct)).Trim();
                await RespondAsync(context.Response, "text/plain", "ok");
                if (token.Length > 20) return token;
            }
            else
            {
                context.Response.StatusCode = 404;
                context.Response.Close();
            }
        }
    }

    static async Task RespondAsync(HttpListenerResponse response, string contentType, string body)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        response.ContentType = contentType + "; charset=utf-8";
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes);
        response.Close();
    }

    static string Js(string text) => System.Text.Json.JsonSerializer.Serialize(text);

    static string CallbackPage => $$"""
        <!doctype html>
        <html lang="{{(L.IsFrench ? "fr" : "en")}}"><head><meta charset="utf-8"><title>AniSync</title>
        <style>
          body{margin:0;height:100vh;display:grid;place-items:center;background:#0b1622;color:#e6edf3;font:16px "Segoe UI",sans-serif}
          .card{background:#152232;border:1px solid #22324a;border-radius:16px;padding:32px 40px;text-align:center;max-width:420px}
          h1{margin:0 0 8px;font-size:22px} p{margin:0;color:#8fa3b8} .ok{color:#3ddc84} .err{color:#f2555a}
        </style></head>
        <body><div class="card"><h1 id="t">{{WebUtility.HtmlEncode(L.T("Connexion…", "Signing in…"))}}</h1><p id="m">{{WebUtility.HtmlEncode(L.T("Un instant.", "One moment."))}}</p></div>
        <script>
          const p = new URLSearchParams(location.hash.slice(1));
          const token = p.get('access_token');
          const t = document.getElementById('t'), m = document.getElementById('m');
          if (token) {
            fetch('/token', { method: 'POST', body: token })
              .then(() => { t.textContent = {{Js(L.T("Connecté à AniSync ✓", "Connected to AniSync ✓"))}}; t.className = 'ok'; m.textContent = {{Js(L.T("Tu peux fermer cet onglet.", "You can close this tab."))}}; history.replaceState(null, '', '/callback'); })
              .catch(() => { t.textContent = {{Js(L.T("AniSync ne répond pas", "AniSync isn't responding"))}}; t.className = 'err'; m.textContent = {{Js(L.T("Vérifie que l'appli est ouverte puis réessaie.", "Make sure the app is open, then try again."))}}; });
          } else {
            t.textContent = {{Js(L.T("Connexion refusée", "Sign-in refused"))}}; t.className = 'err';
            m.textContent = new URLSearchParams(location.search).get('error_description') || {{Js(L.T("AniList n'a pas renvoyé de jeton.", "AniList didn't send back a token."))}};
          }
        </script></body></html>
        """;
}
