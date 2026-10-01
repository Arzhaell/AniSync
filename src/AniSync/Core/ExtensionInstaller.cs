namespace AniSync.Core;

/// <summary>Dépose l'extension navigateur (embarquée dans l'exe) dans un dossier que le navigateur peut charger.</summary>
public static class ExtensionInstaller
{
    const string Prefix = "BrowserExtension/";

    public static string Folder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AniSync", "BrowserExtension");

    public static bool IsExtracted => File.Exists(Path.Combine(Folder, "manifest.json"));

    public static string Extract()
    {
        var assembly = typeof(ExtensionInstaller).Assembly;
        foreach (var name in assembly.GetManifestResourceNames().Where(n => n.StartsWith(Prefix, StringComparison.Ordinal)))
        {
            var relative = name[Prefix.Length..].Replace('\\', '/');
            var path = Path.Combine(Folder, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using var source = assembly.GetManifestResourceStream(name)!;
            using var target = File.Create(path);
            source.CopyTo(target);
        }
        return Folder;
    }

    /// <summary>Après une mise à jour de l'appli, garde les fichiers de l'extension à jour.</summary>
    public static void RefreshIfExtracted()
    {
        if (!IsExtracted) return;
        try
        {
            Extract();
        }
        catch (Exception ex)
        {
            Log.Error("Mise à jour des fichiers de l'extension", ex);
        }
    }
}
