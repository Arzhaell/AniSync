using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using AniSync.AniList;
using AniSync.Core;
using AniSync.Ui;

namespace AniSync;

/// <summary>Ajout (ou reconnexion) d'un compte AniList ou MyAnimeList, étapes guidées.</summary>
public partial class ConnectWindow : Window
{
    readonly AppController _controller;
    readonly string _site;
    readonly Profile? _existing;
    readonly bool _isMal;
    CancellationTokenSource? _cts;

    /// <param name="existing">Compte à reconnecter ; null pour en ajouter un nouveau.</param>
    public ConnectWindow(AppController controller, string site, Profile? existing = null)
    {
        InitializeComponent();
        _controller = controller;
        _site = site;
        _existing = existing;
        _isMal = site == ListSites.MyAnimeList;

        Title = existing is null ? $"Ajouter un compte {site}" : $"Reconnecter {existing.Label}";
        TitleText.Text = existing is null ? $"Ajouter un compte {site}" : $"Reconnecter {existing.DisplayName}";

        // La connexion autorise le compte ouvert dans le navigateur.
        string browserHint = existing is not null
            ? $" Vérifie que le compte {existing.DisplayName} est bien celui ouvert sur {site} dans ton navigateur."
            : controller.Profiles.Any(p => p.Service == site)
                ? $" Pour ajouter un autre compte {site}, déconnecte-toi d'abord de {site} dans ton navigateur (ou connecte-toi au bon compte)."
                : "";
        bool hasKey = !string.IsNullOrEmpty(existing?.ClientId ?? controller.DefaultClientId(site));

        if (_isMal)
        {
            IntroText.Text = (hasKey
                ? "Ta clé MyAnimeList est déjà enregistrée : clique directement sur « Se connecter »."
                : "À faire une seule fois : MyAnimeList demande que chaque appli ait sa propre clé.") + browserHint;
            Step1Text.Text = "1. Ouvre la page « API » de MyAnimeList (connecte-toi si besoin) et clique sur « Create ID ».";
            Step2Text.Text = "2. App Name : AniSync. App Type : other. Remplis le reste librement (usage personnel, non commercial). Dans « App Redirect URL », mets :";
            Step3Text.Text = "3. Envoie le formulaire, rouvre l'appli créée (« Edit ») et copie ici son Client ID :";
        }
        else
        {
            IntroText.Text = (hasKey
                ? "Ta clé AniList est déjà enregistrée : clique directement sur « Se connecter »."
                : "À faire une seule fois. AniList demande que chaque appli ait sa propre clé : ça prend une minute.") + browserHint;
            Step1Text.Text = "1. Ouvre la page développeur d'AniList (connecte-toi si besoin).";
            Step2Text.Text = "2. Clique sur « Create New Client ». Nom : AniSync. Dans « Redirect URL », mets :";
            Step3Text.Text = "3. Clique sur « Save », puis copie ici le Client ID (un nombre) :";
        }

        RedirectBox.Text = AniListAuth.RedirectUrl;
        ClientIdBox.Text = existing?.ClientId ?? controller.DefaultClientId(site) ?? "";
        SourceInitialized += (_, _) => WindowTheming.UseDarkTitleBar(this);
        Closed += (_, _) => _cts?.Cancel();
    }

    void OpenDeveloper_Click(object sender, RoutedEventArgs e) => Links.Open(AppController.DeveloperPage(_site));

    void CopyRedirect_Click(object sender, RoutedEventArgs e)
    {
        try { Clipboard.SetText(AniListAuth.RedirectUrl); ShowStatus("Adresse copiée.", "#8FA3B8"); }
        catch { /* presse-papiers occupé */ }
    }

    void Cancel_Click(object sender, RoutedEventArgs e) => Close();

    async void Connect_Click(object sender, RoutedEventArgs e)
    {
        var clientId = ClientIdBox.Text.Trim();
        bool valid = _isMal ? Regex.IsMatch(clientId, @"^[A-Za-z0-9]{16,64}$") : Regex.IsMatch(clientId, @"^\d{1,10}$");
        if (!valid)
        {
            ShowStatus(_isMal
                ? "Le Client ID MyAnimeList est une suite de 32 lettres et chiffres, visible dans « Edit » sur la page de ton appli."
                : "Le Client ID est un nombre (ex. 12345), visible sur la page de ton client AniList.", "#F5A524");
            ClientIdBox.Focus();
            return;
        }

        _cts?.Cancel();
        _cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        ConnectButton.IsEnabled = false;
        ShowStatus($"Une page {_site} s'est ouverte dans ton navigateur : clique sur « {(_isMal ? "Allow" : "Approve")} »…", "#3DB4F2");

        try
        {
            await _controller.ConnectAsync(_site, _existing, clientId, _cts.Token);
            ShowStatus($"Connecté en tant que {_controller.Vm.UserName} ✓", "#3DDC84");
            await Task.Delay(900);
            Close();
        }
        catch (OperationCanceledException)
        {
            if (IsLoaded) ShowStatus($"Temps écoulé sans réponse de {_site}. Réessaie.", "#F5A524");
        }
        catch (Exception ex)
        {
            ShowStatus($"Connexion impossible : {ex.Message}", "#F2555A");
        }
        finally
        {
            ConnectButton.IsEnabled = true;
        }
    }

    void ShowStatus(string text, string color)
    {
        StatusText.Text = text;
        StatusText.Foreground = (Brush)new BrushConverter().ConvertFromString(color)!;
        StatusText.Visibility = Visibility.Visible;
    }
}
