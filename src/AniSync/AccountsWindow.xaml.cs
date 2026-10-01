using System.Windows;
using System.Windows.Media;
using AniSync.Core;
using AniSync.Ui;

namespace AniSync;

/// <summary>Les comptes de liste : choisir le compte actif, en ajouter, reconnecter ou retirer.</summary>
public partial class AccountsWindow : Window
{
    public sealed record AccountRow(Profile Profile, bool IsActive, bool SignedIn, ImageSource? Avatar)
    {
        public string Name => Profile.DisplayName;
        public string Site => Profile.Service;
        public string Status => !SignedIn ? "à reconnecter" : IsActive ? "compte actif" : "connecté";
        public string StatusColor => !SignedIn ? "#F5A524" : IsActive ? "#3DDC84" : "#8FA3B8";
        public string BorderColor => IsActive ? "#2B6E52" : "#22324A";
        public bool CanUse => SignedIn && !IsActive;
        public bool NeedsReconnect => !SignedIn;
    }

    readonly AppController _controller;

    public AccountsWindow(AppController controller)
    {
        InitializeComponent();
        _controller = controller;
        SourceInitialized += (_, _) => WindowTheming.UseDarkTitleBar(this);
        Refresh();
    }

    void Refresh()
    {
        var rows = _controller.Profiles
            .Select(p => new AccountRow(p, _controller.IsActive(p), _controller.IsSignedIn(p), Images.FromUrl(p.AvatarUrl)))
            .OrderByDescending(r => r.IsActive).ThenBy(r => r.Site).ThenBy(r => r.Name)
            .ToList();
        AccountList.ItemsSource = rows;
        EmptyText.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    void OpenProfile_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is AccountRow row) Links.Open(row.Profile.PageUrl);
    }

    void Use_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not AccountRow row) return;
        _controller.Activate(row.Profile);
        Refresh();
    }

    void Reconnect_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not AccountRow row) return;
        new ConnectWindow(_controller, row.Site, row.Profile) { Owner = this }.ShowDialog();
        Refresh();
    }

    void Remove_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not AccountRow row) return;
        var answer = MessageBox.Show(this,
            $"Retirer le compte {row.Profile.Label} d'AniSync ?\nTa liste sur {row.Site} n'est pas touchée ; tu pourras le rajouter plus tard.",
            "AniSync", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes) return;
        _controller.RemoveProfile(row.Profile);
        Refresh();
    }

    void AddAniList_Click(object sender, RoutedEventArgs e) => Add(ListSites.AniList);
    void AddMal_Click(object sender, RoutedEventArgs e) => Add(ListSites.MyAnimeList);

    void Add(string site)
    {
        new ConnectWindow(_controller, site) { Owner = this }.ShowDialog();
        Refresh();
    }

    void Close_Click(object sender, RoutedEventArgs e) => Close();
}
