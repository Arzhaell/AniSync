using System.ComponentModel;
using System.Windows;
using AniSync.Core;
using AniSync.Ui;

namespace AniSync;

public partial class MainWindow : Window
{
    readonly AppController _controller;

    public MainWindow(AppController controller)
    {
        InitializeComponent();
        _controller = controller;
        DataContext = controller.Vm;
        SourceInitialized += (_, _) => WindowTheming.UseDarkTitleBar(this);
    }

    /// <summary>Faux : fermer la fenêtre la cache seulement (l'appli reste dans la zone de notification).</summary>
    public bool AllowClose { get; set; }

    public event Action? HiddenToTray;

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!AllowClose)
        {
            e.Cancel = true;
            Hide();
            HiddenToTray?.Invoke();
        }
        base.OnClosing(e);
    }

    void Accounts_Click(object sender, RoutedEventArgs e) => ShowAccounts();

    /// <summary>Page du profil du compte actif (AniList ou MyAnimeList) ; à défaut, la fenêtre Comptes.</summary>
    void OpenProfile_Click(object sender, RoutedEventArgs e)
    {
        if (_controller.ProfileUrl is { } url) Links.Open(url);
        else ShowAccounts();
    }

    public void ShowAccounts() => new AccountsWindow(_controller) { Owner = this }.ShowDialog();

    void Restart_Click(object sender, RoutedEventArgs e) => ((App)Application.Current).Restart();

    void CurrentTitle_Click(object sender, RoutedEventArgs e) => Links.Open(_controller.CurrentSiteUrl);

    void FixCurrent_Click(object sender, RoutedEventArgs e)
    {
        var parsed = _controller.CurrentParsed;
        if (parsed is null) return;
        var id = AnimePickerWindow.Pick(this, _controller.Client, parsed, () => _controller.SuggestAsync(parsed));
        if (id is int mediaId) _controller.FixCurrent(mediaId);
    }

    void ConfirmSuggestion_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is SuggestionItem suggestion) _controller.FixCurrent(suggestion.Id);
    }

    void IgnoreHistory_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is HistoryItem item) _controller.IgnoreHistory(item);
    }

    void ConfirmHistory_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is HistoryItem item) _controller.ConfirmHistory(item);
    }

    void IgnoreCurrent_Click(object sender, RoutedEventArgs e)
    {
        var parsed = _controller.CurrentParsed;
        if (parsed is null) return;
        var answer = MessageBox.Show(this,
            L.T($"Ne plus suivre « {parsed.Title} » ?\nTu pourras le réactiver dans les réglages.",
                $"Stop tracking \"{parsed.Title}\"?\nYou can re-enable it in the settings."),
            "AniSync", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer == MessageBoxResult.Yes) _controller.IgnoreCurrent();
    }

    async void Undo_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not HistoryItem item) return;
        if (item.WasCreated)
        {
            var answer = MessageBox.Show(this,
                L.T($"Retirer « {item.Title} » de ta liste {item.Service} ?\n(Il y avait été ajouté automatiquement.)",
                    $"Remove \"{item.Title}\" from your {item.Service} list?\n(It was added automatically.)"),
                "AniSync", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (answer != MessageBoxResult.Yes) return;
        }

        var button = (FrameworkElement)sender;
        button.IsEnabled = false;
        try
        {
            await _controller.UndoAsync(item);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "AniSync", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            button.IsEnabled = true;
        }
    }

    void FixHistory_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not HistoryItem item) return;
        var parsed = item.ToParsed();
        var id = AnimePickerWindow.Pick(this, _controller.Client, parsed, () => _controller.SuggestAsync(parsed));
        if (id is int mediaId) _controller.FixHistory(item, mediaId);
    }

    void MinusMinutes_Click(object sender, RoutedEventArgs e) => _controller.Vm.MinWatchMinutes -= 1;

    void PlusMinutes_Click(object sender, RoutedEventArgs e) => _controller.Vm.MinWatchMinutes += 1;

    void ClearIgnored_Click(object sender, RoutedEventArgs e) => _controller.ClearIgnored();

    void InstallExtension_Click(object sender, RoutedEventArgs e)
    {
        new ExtensionWindow(_controller.Vm) { Owner = this }.ShowDialog();
    }
}
