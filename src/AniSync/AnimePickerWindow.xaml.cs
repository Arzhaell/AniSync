using System.Windows;
using System.Windows.Input;
using AniSync.AniList;
using AniSync.Core;
using AniSync.Ui;

namespace AniSync;

/// <summary>Recherche sur AniList pour associer un titre détecté au bon anime.</summary>
public partial class AnimePickerWindow : Window
{
    public sealed record Choice(int Id, string Title, string Details, string? CoverUrl);

    readonly AniListClient _client;
    int? _pickedId;
    int _searchVersion;

    AnimePickerWindow(AniListClient client, ParsedEpisode parsed, Func<Task<IReadOnlyList<AniMedia>>>? suggest)
    {
        InitializeComponent();
        _client = client;
        DetectedText.Text = L.T(
            $"Détecté : «\u00A0{parsed.Title}\u00A0»" + (parsed.Season is > 1 ? $", saison {parsed.Season}" : "") + $", épisode {parsed.Episode}. Choisis l'anime (la saison qui contient cet épisode, ou la première saison).",
            $"Detected: \"{parsed.Title}\"" + (parsed.Season is > 1 ? $", season {parsed.Season}" : "") + $", episode {parsed.Episode}. Pick the anime (the season that contains this episode, or the first season).");
        SearchBox.Text = parsed.Title;
        SourceInitialized += (_, _) => WindowTheming.UseDarkTitleBar(this);
        Loaded += async (_, _) =>
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
            if (!await ShowSuggestionsAsync(suggest)) await SearchAsync();
        };
    }

    /// <summary>
    /// Renvoie l'ID AniList choisi, ou null si annulé. <paramref name="suggest"/> fournit les animes
    /// au titre proche, affichés à l'ouverture ; la recherche simple sert sinon.
    /// </summary>
    public static int? Pick(Window owner, AniListClient client, ParsedEpisode parsed, Func<Task<IReadOnlyList<AniMedia>>>? suggest = null)
    {
        var window = new AnimePickerWindow(client, parsed, suggest) { Owner = owner };
        return window.ShowDialog() == true ? window._pickedId : null;
    }

    async Task<bool> ShowSuggestionsAsync(Func<Task<IReadOnlyList<AniMedia>>>? suggest)
    {
        if (suggest is null) return false;
        int version = ++_searchVersion;
        StatusText.Text = L.T("Recherche des titres proches…", "Looking for similar titles…");
        try
        {
            var found = await suggest();
            if (version != _searchVersion || found.Count == 0) return false;
            ShowResults(found);
            return true;
        }
        catch
        {
            return false; // la recherche simple prendra le relais
        }
    }

    void ShowResults(IReadOnlyList<AniMedia> found)
    {
        Results.ItemsSource = found.Select(m => new Choice(m.Id, m.Title, Details(m), m.CoverUrl)).ToList();
        StatusText.Text = found.Count == 0 ? L.T("Aucun résultat. Essaie un autre nom (romaji ou anglais) ou colle le lien AniList.", "No results. Try another name (romaji or English) or paste the AniList link.") : "";
        if (found.Count > 0) Results.SelectedIndex = 0;
    }

    async Task SearchAsync()
    {
        var text = SearchBox.Text.Trim();
        if (text.Length == 0) return;
        int version = ++_searchVersion;
        StatusText.Text = L.T("Recherche…", "Searching…");
        Results.ItemsSource = null;

        try
        {
            IReadOnlyList<AniMedia> found;
            if (Links.ParseAniListId(text) is int id) found = [await _client.GetMediaAsync(id)];
            else found = await _client.SearchAsync(text);
            if (version != _searchVersion) return;
            ShowResults(found);
        }
        catch (Exception ex)
        {
            if (version == _searchVersion) StatusText.Text = L.T($"Recherche impossible : {ex.Message}", $"Search failed: {ex.Message}");
        }
    }

    static string Details(AniMedia m)
    {
        var parts = new List<string>();
        if (m.Format is not null) parts.Add(m.Format.Replace('_', ' '));
        if (m.Year is int y) parts.Add(y.ToString());
        if (m.Episodes is int e) parts.Add(L.T($"{e} ép.", $"{e} ep."));
        parts.Add($"#{m.Id}");
        return string.Join(" · ", parts);
    }

    async void Search_Click(object sender, RoutedEventArgs e) => await SearchAsync();

    async void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        await SearchAsync();
    }

    void Results_DoubleClick(object sender, MouseButtonEventArgs e) => Ok_Click(sender, e);

    void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (Results.SelectedItem is not Choice choice)
        {
            StatusText.Text = L.T("Sélectionne un anime dans la liste.", "Select an anime in the list.");
            return;
        }
        _pickedId = choice.Id;
        DialogResult = true;
    }

    void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
