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
        DetectedText.Text = $"Détecté : « {parsed.Title} »" + (parsed.Season is > 1 ? $", saison {parsed.Season}" : "") +
                            $", épisode {parsed.Episode}. Choisis l'anime (la saison qui contient cet épisode, ou la première saison).";
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
        StatusText.Text = "Recherche des titres proches…";
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
        StatusText.Text = found.Count == 0 ? "Aucun résultat. Essaie un autre nom (romaji ou anglais) ou colle le lien AniList." : "";
        if (found.Count > 0) Results.SelectedIndex = 0;
    }

    async Task SearchAsync()
    {
        var text = SearchBox.Text.Trim();
        if (text.Length == 0) return;
        int version = ++_searchVersion;
        StatusText.Text = "Recherche…";
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
            if (version == _searchVersion) StatusText.Text = $"Recherche impossible : {ex.Message}";
        }
    }

    static string Details(AniMedia m)
    {
        var parts = new List<string>();
        if (m.Format is not null) parts.Add(m.Format.Replace('_', ' '));
        if (m.Year is int y) parts.Add(y.ToString());
        if (m.Episodes is int e) parts.Add($"{e} ép.");
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
            StatusText.Text = "Sélectionne un anime dans la liste.";
            return;
        }
        _pickedId = choice.Id;
        DialogResult = true;
    }

    void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
