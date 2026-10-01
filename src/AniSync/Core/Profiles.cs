using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

namespace AniSync.Core;

/// <summary>
/// Un compte de liste : un site (AniList ou MyAnimeList) + la connexion à ce compte.
/// On peut en avoir autant qu'on veut ; un seul est actif à la fois.
/// </summary>
public sealed class Profile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Service { get; set; } = ListSites.AniList;

    // Identité du compte sur le site (remplie à la connexion).
    public int UserId { get; set; }
    public string UserName { get; set; } = "";
    public string? AvatarUrl { get; set; }
    public string? ProfileUrl { get; set; }

    /// <summary>Client ID utilisé pour se connecter (MyAnimeList en a besoin pour renouveler la connexion).</summary>
    public string? ClientId { get; set; }

    /// <summary>Jeton AniList, ou jetons MyAnimeList en JSON, chiffrés avec DPAPI. Absent = déconnecté.</summary>
    public string? EncryptedToken { get; set; }

    [JsonIgnore] public bool IsSignedIn => !string.IsNullOrEmpty(EncryptedToken);

    [JsonIgnore]
    public string DisplayName => string.IsNullOrWhiteSpace(UserName) ? L.T($"Compte {Service}", $"{Service} account") : UserName;

    /// <summary>« Alice · MyAnimeList » : pour l'historique et les menus.</summary>
    [JsonIgnore] public string Label => $"{DisplayName} · {Service}";

    /// <summary>Page du profil sur le site (déduite du nom si le site ne l'a pas encore donnée).</summary>
    [JsonIgnore]
    public string? PageUrl => ProfileUrl ?? (string.IsNullOrWhiteSpace(UserName) ? null
        : Service == ListSites.MyAnimeList
            ? $"https://myanimelist.net/profile/{Uri.EscapeDataString(UserName)}"
            : $"https://anilist.co/user/{Uri.EscapeDataString(UserName)}");

    /// <summary>Même compte sur le même site (pour ne pas créer de doublon en se reconnectant).</summary>
    public bool IsSameAccount(Profile other) => Service == other.Service && UserId != 0 && UserId == other.UserId;

    public void CopyAccountFrom(Profile other)
    {
        UserId = other.UserId;
        UserName = other.UserName;
        AvatarUrl = other.AvatarUrl;
        ProfileUrl = other.ProfileUrl;
        ClientId = other.ClientId;
        EncryptedToken = other.EncryptedToken;
    }
}

/// <summary>Chiffrement des jetons avec DPAPI : lisibles uniquement par ce compte Windows.</summary>
public static class Secrets
{
    static readonly byte[] Entropy = "AniSync.token.v1"u8.ToArray();

    public static string? Encrypt(string? value) => string.IsNullOrEmpty(value)
        ? null
        : Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(value), Entropy, DataProtectionScope.CurrentUser));

    public static string? Decrypt(string? encrypted)
    {
        if (string.IsNullOrEmpty(encrypted)) return null;
        try
        {
            return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(encrypted), Entropy, DataProtectionScope.CurrentUser));
        }
        catch (Exception ex)
        {
            Log.Error("Jeton illisible", ex);
            return null;
        }
    }
}
