using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Runtime.InteropServices;

namespace AniSync.Core;

public static class AppPaths
{
    public static string DataDir { get; } = CreateDataDir();
    public static string SettingsFile => Path.Combine(DataDir, "settings.json");
    public static string HistoryFile => Path.Combine(DataDir, "history.json");
    public static string LogFile => Path.Combine(DataDir, "anisync.log");

    static string CreateDataDir()
    {
        // ANISYNC_DATA_DIR : dossier de données séparé, pour tester sans toucher aux vrais réglages.
        var dir = Environment.GetEnvironmentVariable("ANISYNC_DATA_DIR") is { Length: > 0 } custom
            ? custom
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AniSync");
        Directory.CreateDirectory(dir);
        return dir;
    }
}

public static class Log
{
    static readonly object Gate = new();

    public static void Info(string message) => Write("INFO", message);
    public static void Error(string message, Exception? ex = null) => Write("ERR ", ex is null ? message : $"{message} : {ex}");

    static void Write(string level, string message)
    {
        try
        {
            lock (Gate)
            {
                var file = new FileInfo(AppPaths.LogFile);
                if (file.Exists && file.Length > 2_000_000)
                    File.Move(file.FullName, file.FullName + ".old", overwrite: true);
                File.AppendAllText(AppPaths.LogFile, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {level} {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // Le journal ne doit jamais faire planter l'appli.
        }
    }
}

public static class JsonStore
{
    static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static T? Load<T>(string path) where T : class
    {
        if (!File.Exists(path)) return null;
        try
        {
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options);
        }
        catch (Exception ex)
        {
            Log.Error($"Fichier illisible, sauvegardé en .bak : {path}", ex);
            try { File.Copy(path, path + ".bak", overwrite: true); } catch { }
            return null;
        }
    }

    public static void Save<T>(string path, T value)
    {
        try
        {
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(value, Options));
            File.Move(tmp, path, overwrite: true);
        }
        catch (Exception ex)
        {
            Log.Error($"Écriture impossible : {path}", ex);
        }
    }
}

public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }

    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// Lancement automatique avec Windows : un raccourci AniSync.lnk dans le dossier Démarrage de l'utilisateur
/// (le même que celui créé par l'installeur, qui le retire à la désinstallation).
/// </summary>
public static class StartupManager
{
    public static string ShortcutPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "AniSync.lnk");

    public static bool IsEnabled() => File.Exists(ShortcutPath);

    public static void Set(bool enabled)
    {
        if (enabled) CreateShortcut(ShortcutPath, Environment.ProcessPath!, "--minimized");
        else if (File.Exists(ShortcutPath)) File.Delete(ShortcutPath);
    }

    public static void CreateShortcut(string path, string target, string arguments)
    {
        var shellType = Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("WScript.Shell indisponible");
        dynamic shell = Activator.CreateInstance(shellType)!;
        try
        {
            dynamic link = shell.CreateShortcut(path);
            link.TargetPath = target;
            link.Arguments = arguments;
            link.WorkingDirectory = Path.GetDirectoryName(target);
            link.Description = "AniSync";
            link.Save();
            Marshal.FinalReleaseComObject(link);
        }
        finally
        {
            Marshal.FinalReleaseComObject(shell);
        }
    }
}
