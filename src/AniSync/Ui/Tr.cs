using AniSync.Core;

namespace AniSync.Ui;

/// <summary>Textes des fenêtres (XAML : {x:Static ui:Tr.Nom}), en français et en anglais.</summary>
public static class Tr
{
    // ----- Fenêtre principale -----
    public static string AppTagline => L.T("Synchro AniList & MyAnimeList", "AniList & MyAnimeList sync");
    public static string OpenMyProfileTip => L.T("Ouvrir mon profil sur le site", "Open my profile on the site");
    public static string AccountsTip => L.T("Changer de compte, en ajouter ou en retirer", "Switch, add or remove accounts");
    public static string AddAccount => L.T("Ajouter un compte", "Add an account");
    public static string ListenTip => L.T("Activer / désactiver l'écoute du PC", "Turn PC listening on or off");
    public static string ListenOn => L.T("Écoute activée", "Listening on");
    public static string ListenOnHint => L.T("AniSync surveille ce que tu regardes", "AniSync is watching what you play");
    public static string ListenOff => L.T("Écoute désactivée", "Listening off");
    public static string ListenOffHint => L.T("Ta liste ne sera pas mise à jour", "Your list won't be updated");
    public static string OpenAnimeTip => L.T("Ouvrir la fiche de l'anime", "Open the anime page");
    public static string ThisOne => L.T("C'est lui", "That's it");
    public static string ThisOneTip => L.T("Associer ce titre à cet anime (retenu pour les prochains épisodes)", "Link this title to this anime (remembered for next episodes)");
    public static string Fix => L.T("Corriger", "Fix");
    public static string FixCurrentTip => L.T("Ce n'est pas le bon anime ? Choisis le bon", "Wrong anime? Pick the right one");
    public static string IgnoreTitle => L.T("Ignorer ce titre", "Ignore this title");
    public static string IgnoreTitleTip => L.T("Ne plus jamais suivre ce titre", "Never track this title again");
    public static string History => L.T("Historique", "History");
    public static string HistoryEmpty => L.T("Rien pour l'instant. Chaque épisode terminé apparaîtra ici, avec la possibilité d'annuler.", "Nothing yet. Every finished episode will show up here, with an undo option.");
    public static string Undo => L.T("Annuler", "Undo");
    public static string UndoTip => L.T("Remettre ta liste comme avant", "Put your list back as it was");
    public static string Yes => L.T("Oui", "Yes");
    public static string YesTip => L.T("C'est bien cet anime : l'associer et synchroniser", "That's the anime: link it and sync");
    public static string Other => L.T("Autre…", "Other…");
    public static string OtherTip => L.T("Ce n'est pas lui : choisir un autre anime", "Not this one: pick another anime");
    public static string FixHistoryTip => L.T("Choisir le bon anime et synchroniser", "Pick the right anime and sync");
    public static string Ignore => L.T("Ignorer", "Ignore");
    public static string IgnoreHistoryTip => L.T("Ce n'est pas un anime : ne plus suivre ce titre", "Not an anime: stop tracking this title");
    public static string MinTime => L.T("Temps minimum pour compter un épisode", "Minimum time to count an episode");
    public static string MinTimeHint => L.T("Lecture réelle, pauses exclues (85 % de la durée pour les épisodes courts)", "Actual playback, pauses excluded (85% of the length for short episodes)");
    public static string Extension => L.T("Extension navigateur (Crunchyroll, Opera, Edge…)", "Browser extension (Crunchyroll, Opera, Edge…)");
    public static string InstallEllipsis => L.T("Installer…", "Install…");
    public static string InstallExtensionTip => L.T("Installer l'extension dans Opera ou Edge", "Install the extension in Opera or Edge");
    public static string StartWithWindows => L.T("Lancer avec Windows", "Start with Windows");
    public static string Notifications => "Notifications";
    public static string ReenableIgnored => L.T("Réactiver les titres ignorés (", "Re-enable ignored titles (");
    public static string Language => L.T("Langue", "Language");
    public static string LanguageHint => L.T("Auto = langue de Windows", "Auto = Windows language");
    public static string Restart => L.T("Redémarrer", "Restart");
    public static string RestartTip => L.T("Redémarrer AniSync pour appliquer la langue", "Restart AniSync to apply the language");

    // ----- Connexion -----
    public static string OpenPage => L.T("Ouvrir la page", "Open the page");
    public static string Copy => L.T("Copier", "Copy");
    public static string Open => L.T("Ouvrir", "Open");
    public static string Cancel => L.T("Annuler", "Cancel");
    public static string SignIn => L.T("Se connecter", "Sign in");

    // ----- Comptes -----
    public static string Accounts => L.T("Comptes", "Accounts");
    public static string AccountsIntro => L.T("Le compte actif est celui mis à jour quand tu finis un épisode. Tu peux en avoir autant que tu veux, sur AniList comme sur MyAnimeList.", "The active account is the one updated when you finish an episode. You can have as many as you like, on AniList and MyAnimeList.");
    public static string AccountsEmpty => L.T("Aucun compte pour l'instant : ajoutes-en un ci-dessous.", "No account yet: add one below.");
    public static string Use => L.T("Utiliser", "Use");
    public static string UseTip => L.T("Mettre à jour ce compte quand tu finis un épisode", "Update this account when you finish an episode");
    public static string Reconnect => L.T("Reconnecter", "Reconnect");
    public static string RemoveAccountTip => L.T("Retirer ce compte d'AniSync", "Remove this account from AniSync");
    public static string OpenProfileTip => L.T("Ouvrir le profil sur le site", "Open the profile on the site");
    public static string Close => L.T("Fermer", "Close");

    // ----- Choix de l'anime -----
    public static string PickWindowTitle => L.T("Choisir le bon anime", "Pick the right anime");
    public static string PickTitle => L.T("Quel anime regardes-tu ?", "Which anime are you watching?");
    public static string Search => L.T("Chercher", "Search");
    public static string SearchTip => L.T("Un nom, ou un lien anilist.co/anime/…", "A name, or an anilist.co/anime/… link");
    public static string ThatOne => L.T("C'est celui-là", "This one");

    // ----- Extension -----
    public static string ExtensionWindowTitle => L.T("Extension navigateur", "Browser extension");
    public static string ExtensionHeading => L.T("Installer l'extension navigateur", "Install the browser extension");
    public static string ExtensionIntro => L.T(
        "Elle lit directement dans la page l'anime, la saison et l'épisode (indispensable pour Crunchyroll, qui ne les affiche pas dans l'onglet), puis les transmet à AniSync sur ce PC. Hors navigateur, AniSync continue de détecter comme avant.",
        "It reads the anime, season and episode right from the page (essential for Crunchyroll, which doesn't show them in the tab), then passes them to AniSync on this PC. Outside the browser, AniSync keeps detecting as before.");
    public static string ExtensionStep1 => L.T("Le dossier de l'extension est prêt :", "The extension folder is ready:");
    public static string ExtensionStep2 => L.T("Dans ton navigateur, tape dans la barre d'adresse :", "In your browser, type in the address bar:");
    public static string ExtensionStep3 => L.T("Active le « Mode développeur » (interrupteur en haut à droite, ou à gauche dans Edge).", "Turn on \"Developer mode\" (toggle at the top right, or on the left in Edge).");
    public static string ExtensionStep4 => L.T("Clique sur « Charger l'extension non empaquetée » et choisis le dossier de l'étape 1.", "Click \"Load unpacked\" and pick the folder from step 1.");
    public static string ExtensionStep5 => L.T("Lance un épisode : l'état ci-dessous passe au vert.", "Play an episode: the status below turns green.");
    public static string Done => L.T("Terminé", "Done");
    public static string CopyAddressTip => L.T("Copier l'adresse", "Copy the address");
}
