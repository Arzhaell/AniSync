using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Data;
using System.Windows.Interop;
using AniSync.Core;

namespace AniSync.Ui;

public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is Visibility.Collapsed;
}

public static class WindowTheming
{
    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    /// <summary>Barre de titre sombre (Windows 10 20H1+ / Windows 11).</summary>
    public static void UseDarkTitleBar(Window window)
    {
        try
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            int on = 1;
            DwmSetWindowAttribute(hwnd, 20, ref on, sizeof(int));
        }
        catch
        {
            // purement esthétique
        }
    }
}

public static class Images
{
    static readonly Dictionary<string, System.Windows.Media.ImageSource?> Cache = new();

    /// <summary>Image téléchargée en arrière-plan (avatars, jaquettes) ; null si pas d'adresse.</summary>
    public static System.Windows.Media.ImageSource? FromUrl(string? url)
    {
        if (string.IsNullOrEmpty(url)) return null;
        if (Cache.TryGetValue(url, out var cached)) return cached;
        System.Windows.Media.ImageSource? image = null;
        try
        {
            var bitmap = new System.Windows.Media.Imaging.BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(url);
            bitmap.DecodePixelWidth = 160;
            bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            image = bitmap;
        }
        catch (Exception ex)
        {
            Log.Error($"Image {url}", ex);
        }
        Cache[url] = image;
        return image;
    }
}

public static class Links
{
    public static void Open(string? url)
    {
        if (string.IsNullOrEmpty(url)) return;
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) { Log.Error($"Ouverture de {url}", ex); }
    }

    /// <summary>Extrait l'ID d'un lien anilist.co/anime/12345/... ou d'un nombre seul.</summary>
    public static int? ParseAniListId(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var m = Regex.Match(text, @"anilist\.co/anime/(\d+)", RegexOptions.IgnoreCase);
        if (m.Success) return int.Parse(m.Groups[1].Value);
        m = Regex.Match(text.Trim(), @"^\d{1,9}$");
        return m.Success ? int.Parse(m.Value) : null;
    }
}
