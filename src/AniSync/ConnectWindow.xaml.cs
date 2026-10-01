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

    /// <summary>Guillemets français avec espaces insécables.</summary>
    static string Q(string text) => $"« {text} »";

    /// <param name="existing">Compte à reconnecter ; null pour en ajouter un nouveau.</param>
    public ConnectWindow(AppController controller, string site, Profile? existing = null)
    {
        InitializeComponent();
        _controller = controller;
        _site = site;
        _existing = existing;
        _isMal = site == ListSites.MyAnimeList;

        Title = existing is null
            ? L.T($"Ajouter un compte {site}", $"Add a {site} account")
            : L.T($"Reconnecter {existing.Label}", $"Reconnect {existing.Label}");
        TitleText.Text = existing is null
            ? L.T($"Ajouter un compte {site}", $"Add a {site} account")
            : L.T($"Reconnecter {existing.DisplayName}", $"Reconnect {existing.DisplayName}");

        // La connexion autorise le compte ouvert dans le navigateur.
        string browserHint = existing is not null
            ? L.T($" Vérifie que le compte {existing.DisplayName} est bien celui ouvert sur {site} dans ton navigateur.",
                  $" Make sure {existing.DisplayName} is the account signed in on {site} in your browser.")
            : controller.Profiles.Any(p => p.Service == site)
                ? L.T($" Pour ajouter un autre compte {site}, déconnecte-toi d'abord de {site} dans ton navigateur (ou connecte-toi au bon compte).",
                      $" To add another {site} account, first sign out of {site} in your browser (or sign in to the right account).")
                : "";
        bool hasKey = !string.IsNullOrEmpty(existing?.ClientId ?? controller.DefaultClientId(site));
        string alreadySaved = L.T($"Ta clé {site} est déjà enregistrée : clique directement sur {Q("Se connecter")}.",
                                  $"Your {site} key is already saved: just click \"Sign in\".");

        if (_isMal)
        {
            IntroText.Text = (hasKey
                ? alreadySaved
                : L.T("À faire une seule fois : MyAnimeList demande que chaque appli ait sa propre clé.",
                      "One-time setup: MyAnimeList requires each app to have its own key.")) + browserHint;
            Step1Text.Text = L.T($"1. Ouvre la page {Q("API")} de MyAnimeList (connecte-toi si besoin) et clique sur {Q("Create ID")}.",
                                 "1. Open MyAnimeList's \"API\" page (sign in if needed) and click \"Create ID\".");
            Step2Text.Text = L.T($"2. App Name : AniSync. App Type : other. Remplis le reste librement (usage personnel, non commercial). Dans {Q("App Redirect URL")}, mets :",
                                 "2. App Name: AniSync. App Type: other. Fill in the rest as you like (personal, non-commercial use). In \"App Redirect URL\", enter:");
            Step3Text.Text = L.T($"3. Envoie le formulaire, rouvre l'appli créée ({Q("Edit")}) et copie ici son Client ID :",
                                 "3. Submit the form, open the created app again (\"Edit\") and paste its Client ID here:");
        }
        else
        {
            IntroText.Text = (hasKey
                ? alreadySaved
                : L.T("À faire une seule fois. AniList demande que chaque appli ait sa propre clé : ça prend une minute.",
                      "One-time setup. AniList requires each app to have its own key: it takes a minute.")) + browserHint;
            Step1Text.Text = L.T("1. Ouvre la page développeur d'AniList (connecte-toi si besoin).",
                                 "1. Open AniList's developer page (sign in if needed).");
            Step2Text.Text = L.T($"2. Clique sur {Q("Create New Client")}. Nom : AniSync. Dans {Q("Redirect URL")}, mets :",
                                 "2. Click \"Create New Client\". Name: AniSync. In \"Redirect URL\", enter:");
            Step3Text.Text = L.T($"3. Clique sur {Q("Save")}, puis copie ici le Client ID (un nombre) :",
                                 "3. Click \"Save\", then paste the Client ID (a number) here:");
        }

        RedirectBox.Text = AniListAuth.RedirectUrl;
        ClientIdBox.Text = existing?.ClientId ?? controller.DefaultClientId(site) ?? "";
        SourceInitialized += (_, _) => WindowTheming.UseDarkTitleBar(this);
        Closed += (_, _) => _cts?.Cancel();
    }

    void OpenDeveloper_Click(object sender, RoutedEventArgs e) => Links.Open(AppController.DeveloperPage(_site));

    void CopyRedirect_Click(object sender, RoutedEventArgs e)
    {
        try { Clipboard.SetText(AniListAuth.RedirectUrl); ShowStatus(L.T("Adresse copiée.", "Address copied."), "#8FA3B8"); }
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
                ? L.T($"Le Client ID MyAnimeList est une suite de 32 lettres et chiffres, visible dans {Q("Edit")} sur la page de ton appli.",
                      "The MyAnimeList Client ID is a string of 32 letters and digits, shown under \"Edit\" on your app's page.")
                : L.T("Le Client ID est un nombre (ex. 12345), visible sur la page de ton client AniList.",
                      "The Client ID is a number (e.g. 12345), shown on your AniList client's page."), "#F5A524");
            ClientIdBox.Focus();
            return;
        }

        _cts?.Cancel();
        _cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        ConnectButton.IsEnabled = false;
        string approve = _isMal ? "Allow" : "Approve";
        ShowStatus(L.T($"Une page {_site} s'est ouverte dans ton navigateur : clique sur {Q(approve)}…",
                       $"A {_site} page opened in your browser: click \"{approve}\"…"), "#3DB4F2");

        try
        {
            await _controller.ConnectAsync(_site, _existing, clientId, _cts.Token);
            ShowStatus(L.T($"Connecté en tant que {_controller.Vm.UserName} ✓", $"Signed in as {_controller.Vm.UserName} ✓"), "#3DDC84");
            await Task.Delay(900);
            Close();
        }
        catch (OperationCanceledException)
        {
            if (IsLoaded) ShowStatus(L.T($"Temps écoulé sans réponse de {_site}. Réessaie.", $"No answer from {_site} in time. Try again."), "#F5A524");
        }
        catch (Exception ex)
        {
            ShowStatus(L.T($"Connexion impossible : {ex.Message}", $"Couldn't sign in: {ex.Message}"), "#F2555A");
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
