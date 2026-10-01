using System.Diagnostics;
using System.Net;
using System.Text;

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
            throw new AniListException($"Impossible d'ouvrir le port {Port} ({ex.Message}). Ferme l'appli qui l'utilise et réessaie.");
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

    const string CallbackPage = """
        <!doctype html>
        <html lang="fr"><head><meta charset="utf-8"><title>AniSync</title>
        <style>
          body{margin:0;height:100vh;display:grid;place-items:center;background:#0b1622;color:#e6edf3;font:16px "Segoe UI",sans-serif}
          .card{background:#152232;border:1px solid #22324a;border-radius:16px;padding:32px 40px;text-align:center;max-width:420px}
          h1{margin:0 0 8px;font-size:22px} p{margin:0;color:#8fa3b8} .ok{color:#3ddc84} .err{color:#f2555a}
        </style></head>
        <body><div class="card"><h1 id="t">Connexion…</h1><p id="m">Un instant.</p></div>
        <script>
          const p = new URLSearchParams(location.hash.slice(1));
          const token = p.get('access_token');
          const t = document.getElementById('t'), m = document.getElementById('m');
          if (token) {
            fetch('/token', { method: 'POST', body: token })
              .then(() => { t.textContent = 'Connecté à AniSync ✓'; t.className = 'ok'; m.textContent = 'Tu peux fermer cet onglet.'; history.replaceState(null, '', '/callback'); })
              .catch(() => { t.textContent = 'AniSync ne répond pas'; t.className = 'err'; m.textContent = "Vérifie que l'appli est ouverte puis réessaie."; });
          } else {
            t.textContent = 'Connexion refusée'; t.className = 'err';
            m.textContent = new URLSearchParams(location.search).get('error_description') || "AniList n'a pas renvoyé de jeton.";
          }
        </script></body></html>
        """;
}
