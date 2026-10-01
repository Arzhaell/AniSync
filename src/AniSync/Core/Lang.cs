using System.Globalization;
using System.Text.Json;

namespace AniSync.Core;

/// <summary>
/// Langue de l'interface (français ou anglais), fixée au démarrage : réglage de l'utilisateur,
/// sinon langue de Windows. Chaque texte est écrit une fois avec ses deux langues : L.T("français", "English").
/// </summary>
public static class L
{
    public const string AutoCode = "auto";
    public const string FrenchCode = "fr";
    public const string EnglishCode = "en";

    /// <summary>Lu dès le premier texte affiché (avant même le chargement du thème).</summary>
    public static bool IsFrench { get; private set; } = Detect(ReadSetting());

    public static string T(string french, string english) => IsFrench ? french : english;

    public static bool Detect(string? setting) => setting switch
    {
        FrenchCode => true,
        EnglishCode => false,
        _ => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "fr",
    };

    /// <summary>Change la langue (tests, ou avant l'ouverture des fenêtres).</summary>
    public static void Use(bool french) => IsFrench = french;

    static string? ReadSetting()
    {
        try
        {
            if (!File.Exists(AppPaths.SettingsFile)) return null;
            using var doc = JsonDocument.Parse(File.ReadAllText(AppPaths.SettingsFile));
            return doc.RootElement.TryGetProperty("Language", out var value) ? value.GetString() : null;
        }
        catch
        {
            return null;
        }
    }
}
