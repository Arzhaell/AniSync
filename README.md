# AniSync

Appli Windows qui repère l'anime que tu regardes sur ton PC et met ta liste **AniList** ou **MyAnimeList** à jour toute seule quand tu as vraiment fini un épisode.

## Installer

`build.ps1` génère dans `dist\` :

- **`AniSync-Setup-<version>.exe`** (recommandé) : l'installeur, aux couleurs d'AniSync.
- `AniSync-Setup-<version>.msi` : le même installeur au format Windows Installer, avec l'interface standard de Windows. Utile pour un déploiement ou une installation silencieuse (`msiexec /i AniSync-Setup-<version>.msi /qn`).
- `Portable\AniSync.exe` : version sans installation, à lancer de n'importe où.

L'installeur (`.exe` ou `.msi`) :

- installe pour ton compte Windows, sans droits admin, dans `%LOCALAPPDATA%\Programs\AniSync` ;
- crée les raccourcis Bureau et menu Démarrer ;
- active le lancement avec Windows, via un raccourci dans le dossier Démarrage, désactivable dans l'appli ;
- ajoute AniSync à Paramètres > Applications pour le désinstaller.

Pour une mise à jour, lance un installeur plus récent : il ferme l'appli si elle est ouverte et garde tes réglages et ton compte.

Tout est autonome : .NET est inclus, rien d'autre à installer (Windows 10/11 64 bits).

## Premier lancement

À la première ouverture, clique sur **Ajouter un compte** puis choisis **AniList** ou **MyAnimeList**. La fenêtre de connexion guide les 3 étapes. Elles ne sont à faire qu'une fois par site, parce que chaque site demande que l'appli ait sa propre clé.

| | AniList | MyAnimeList |
|---|---|---|
| Page où créer la clé | https://anilist.co/settings/developer, **Create New Client** | https://myanimelist.net/apiconfig, **Create ID** (App Type : *other*) |
| Redirect URL | `http://localhost:47813/callback` | `http://localhost:47813/callback` |
| Ensuite | colle le **Client ID** dans AniSync, puis **Approve** | colle le **Client ID** dans AniSync, puis **Allow** |

Les jetons sont chiffrés avec ton compte Windows (DPAPI) dans `%APPDATA%\AniSync\settings.json`. Celui d'AniList reste valable un an ; celui de MyAnimeList est renouvelé automatiquement.

## Comment ça marche

- **Détection** : l'appli lit les sessions média de Windows (Edge, Chrome, Opera, Firefox, lecteur multimédia…), qui donnent le titre, la lecture ou la pause et la durée. Pour les lecteurs qui ne publient pas ces infos (VLC, MPC-HC, mpv, PotPlayer…), elle lit le titre de la fenêtre et vérifie que le lecteur émet réellement du son.
- **Épisode terminé** : seul le temps de **lecture réelle** compte. Les pauses et les sauts en avant sont exclus. Le seuil par défaut est de 20 min et se règle dans l'appli. Pour les épisodes courts, il suffit de 85 % de la durée.
- **Mise à jour AniList** :
  - Anime absent de ta liste : il est ajouté (En cours, ou Terminé si c'était le dernier épisode).
  - AniList à l'ép. 8 et tu finis l'ép. 12 : il passe à 12.
  - AniList à l'ép. 12 et tu revois l'ép. 8 : **rien ne change**.
  - À voir, En pause ou Abandonné : repasse En cours. Dernier épisode : Terminé, avec les dates de début et de fin remplies.
- **Saisons** : « Saison 2 Épisode 3 », « S02E03 » et la numérotation continue (« Jujutsu Kaisen ép. 30 » devient la saison 2, ép. 6) sont gérés.
- **Titres français** : ils sont souvent dans les synonymes d'AniList, mais sa recherche ne renvoie rien quand le texte contient une lettre accentuée (« Je veux t'aimer jusqu'à ta mort »). L'appli cherche donc aussi la version sans accents. Si AniList n'a vraiment pas le titre, elle demande en dernier recours à Wikidata ses équivalents anglais, romaji et japonais, puis relance la recherche avec ces noms.
- **Bouton Écoute** : coupe ou réactive la surveillance. Il est aussi disponible par clic droit sur l'icône près de l'horloge.

**Quand l'appli n'est pas sûre**, elle ne devine pas : elle propose les animes au titre le plus proche et te laisse valider.

- Dans « En cours », pendant l'épisode : jusqu'à 3 propositions avec un bouton **C'est lui**.
- Dans l'historique, une fois l'épisode fini : **Oui** (c'est bien lui), **Autre…** (en choisir un autre) ou **Ignorer** (ce n'est pas un anime, ne plus suivre ce titre).
- Une notification « AniSync a un doute » part une fois par série quand la proposition est très proche. Un clic dessus ouvre l'appli.

Ton choix est mémorisé pour les prochains épisodes. Si l'appli s'est trompée d'anime, **Corriger** permet de choisir le bon, et chaque mise à jour de l'historique a un bouton **Annuler**.

Fermer la fenêtre laisse AniSync tourner dans la zone de notification. Pour quitter, fais un clic droit sur l'icône puis **Quitter**.

## Comptes (AniList et MyAnimeList)

Tu peux ajouter **autant de comptes que tu veux**, AniList comme MyAnimeList, et choisir le **compte actif** : c'est lui qui est mis à jour quand tu finis un épisode.

- **En haut de l'appli** : un clic sur ton avatar ou ton nom ouvre ta page de profil AniList ou MyAnimeList, et la petite flèche à côté ouvre la **fenêtre Comptes**. Dans celle-ci, tu peux utiliser un compte, le reconnecter, le retirer ou en ajouter un ; un clic sur un compte ouvre aussi sa page.
- **Changement rapide** : clic droit sur l'icône près de l'horloge, puis **Compte**.
- **Ajouter un deuxième compte du même site** : la connexion autorise le compte ouvert dans ton navigateur, donc déconnecte-toi d'abord de ce site dans le navigateur (ou connecte-toi au bon compte). Reconnecter un compte déjà présent le met à jour, sans créer de doublon.
- **Ce qui reste commun à tous les comptes** : corrections, titres ignorés et réglages. L'historique indique quel compte a été mis à jour, et « Annuler » agit sur ce compte-là.
- Une connexion expirée ne supprime pas le compte : il passe en « à reconnecter ».
- Les jetons de chaque compte sont chiffrés avec ton compte Windows (DPAPI).
- En venant d'une version précédente, tes connexions existantes deviennent des comptes automatiquement.

**MyAnimeList**

- La reconnaissance des animes ne change pas : elle se fait toujours via la recherche AniList (titres français, saisons, propositions…), qui donne le numéro MyAnimeList de chaque anime.
- Les règles sont les mêmes : on avance sans jamais reculer, on ajoute si l'anime manque, on passe en « Terminé » au dernier épisode, et « Annuler » reste disponible.
- Il faut créer une fois une clé sur https://myanimelist.net/apiconfig (App Type : *other*, Redirect URL : `http://localhost:47813/callback`). L'appli renouvelle ensuite la connexion toute seule.
- Limite : quelques rares animes présents sur AniList n'ont pas de fiche MyAnimeList, et l'appli le signale.

## Extension navigateur (Crunchyroll…)

Crunchyroll n'affiche ni le nom de l'anime ni le numéro d'épisode dans ses onglets (seulement « Season 1 <titre de l'épisode> »). L'extension lit directement dans la page ce que l'appli ne peut pas voir, puis le transmet à AniSync **sur ce PC uniquement** (`http://localhost:47814`). Elle récupère :

- la série, en anglais depuis l'adresse et en français depuis la page ;
- la saison et le numéro d'épisode ;
- l'état réel de la vidéo : lecture, pause, durée.

Elle fonctionne aussi quand le lecteur est intégré dans une autre page (iframe).

- **Installation** : dans l'appli, Réglages > Extension navigateur > **Installer…**, puis suis les étapes (mode développeur > « Charger l'extension non empaquetée »). Elle marche dans Opera, Edge, Chrome et Brave.
- **Hors navigateur**, rien ne change : AniSync continue de détecter les lecteurs vidéo et les applis comme avant. Quand l'extension est active dans un navigateur, c'est elle qui fait foi pour ce navigateur.
- Seule l'extension peut parler à AniSync : les requêtes sans son en-tête `X-AniSync-Extension` sont refusées, et une page web ne peut pas l'ajouter.

## Limites connues

- Sans l'extension, il faut que le numéro d'épisode apparaisse dans le titre de l'onglet, de la vidéo ou du fichier. Ce n'est pas le cas sur Crunchyroll ni sur Netflix, par exemple.
- Netflix (avec l'extension) : le titre n'est lu que lorsque les contrôles du lecteur s'affichent au moins une fois pendant l'épisode.
- Le temps d'un épisode à moitié vu est gardé tant que l'appli tourne (12 h max). Il est perdu si tu la quittes.

## Développement

Tout reconstruire (tests, exe portable, `.msi` puis `Setup.exe`) :

```bash
powershell -ExecutionPolicy Bypass -File build.ps1
```

La version vient de `<Version>` dans `src/AniSync/AniSync.csproj` : il suffit de l'augmenter avant de reconstruire pour publier une mise à jour.

- `installer/`: `.msi` WiX 5 (`Package.wxs`). WiX est récupéré via NuGet, rien à installer.
- `installer/bundle/`: le `Setup.exe` qui emballe le `.msi`, avec le thème et les textes français dans `Theme/`.

- `browser-extension/`: l'extension (Manifest V3), embarquée dans l'exe
- `src/AniSync/Detection`: lecture des sessions média, des fenêtres et du son, réception des infos de l'extension (`BrowserBridge.cs`)
- `src/AniSync/Core`: analyse des titres, temps de visionnage, règles de synchro, historique
- `src/AniSync/AniList`: API GraphQL, connexion OAuth, recherche de l'anime, traduction des titres français (`WikidataTitles.cs`), liste AniList (`AniListService.cs`)
- `src/AniSync/Mal`: liste MyAnimeList (API v2, connexion OAuth + PKCE, conversion des statuts)
- `src/AniSync/Core/ListServices.cs`: interface commune aux deux sites
- Journal : `%APPDATA%\AniSync\anisync.log`
