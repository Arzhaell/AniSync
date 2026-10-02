# Publier l'extension sur les boutiques (non répertoriée)

Ce guide sert à envoyer l'extension AniSync au **Chrome Web Store** (Chrome, Brave, Opera) et à **Edge Add-ons**, en **non répertoriée** : elle n'apparaît pas dans les recherches, seules les personnes qui ont le lien (le bouton « Installer… » d'AniSync) la trouvent.

Tout ce qu'il faut est prêt :

| Quoi | Où |
|---|---|
| L'extension à envoyer | `dist\AniSync-Extension-<version>.zip` (créé par `build.ps1`) |
| Icône 128×128 | `browser-extension\icons\icon128.png` |
| Logo 300×300 (Edge) | `store\images\logo-300.png` |
| Tuile promo 440×280 | `store\images\promo-440x280-en.png` et `-fr.png` |
| Captures 1280×800 | `store\images\screenshot-1-en.png`, `screenshot-2-en.png` (et `-fr`) |
| Politique de confidentialité | https://github.com/Arzhaell/AniSync/blob/dev/PRIVACY.md |
| Site / assistance | https://github.com/Arzhaell/AniSync · https://github.com/Arzhaell/AniSync/issues |

> Une fois la version 1.9.0 publiée, tu pourras remplacer `blob/dev` par `blob/main` dans le lien de la politique de confidentialité. Ce n'est pas obligatoire : les deux restent valables.

## 0. Avant d'envoyer : tester en mode développeur

1. Relance AniSync (la version de test) : il met à jour le dossier de l'extension.
2. Dans le navigateur, page des extensions, bouton **Recharger** sur AniSync.
3. Vérifie sur Crunchyroll que l'épisode est bien détecté.
4. Sur un autre site de streaming : clique sur l'icône AniSync, **Activer sur ce site**, accepte la demande du navigateur, lance la vidéo.

## 1. Chrome Web Store

1. Va sur https://chrome.google.com/webstore/devconsole et connecte-toi avec ton compte Google.
2. Accepte le contrat et paie les **frais d'inscription de 5 $** (une seule fois).
3. Renseigne l'adresse e-mail de contact et vérifie-la. Si on te demande si tu es un « professionnel » (*trader*) : tu ne l'es pas (extension gratuite, à titre personnel).
4. **Nouvel élément** → envoie `AniSync-Extension-<version>.zip`.
5. Onglet **Fiche du Store** (*Store listing*) :
   - Description : le texte **anglais** plus bas.
   - Catégorie : **Divertissement** (*Entertainment*). Langue : **English**.
   - Icône, tuile promo (`promo-440x280-en.png`), captures (`screenshot-1-en.png`, `screenshot-2-en.png`).
   - Page d'accueil et assistance : les liens GitHub du tableau.
   - Ajoute la langue **Français** (« Ajouter une fiche localisée ») avec la description française et les images `-fr`.
6. Onglet **Pratiques de confidentialité / Privacy practices** : copie les textes de la section « Confidentialité » plus bas.
7. Onglet **Distribution** : visibilité **Non répertorié** (*Unlisted*), toutes les régions.
8. **Envoyer pour examen.** Compte quelques jours, parfois 2-3 semaines.
9. Une fois acceptée, envoie-moi le lien de la fiche (`https://chromewebstore.google.com/detail/...`).

## 2. Edge Add-ons

1. Va sur https://partner.microsoft.com/dashboard/microsoftedge et inscris-toi avec ton compte Microsoft (**gratuit**, compte **individuel**).
2. **Créer une extension** → envoie le même `.zip`.
3. **Disponibilité** : visibilité **Masquée** (*Hidden*), tous les marchés.
4. **Propriétés** : catégorie **Divertissement**, lien de la politique de confidentialité, site web et assistance (tableau ci-dessus).
5. **Fiches du Store** : ajoute **English** et **Français** avec la description, le logo 300×300, la tuile promo et les captures.
6. **Notes pour la certification** : copie le texte « Notes pour les testeurs » plus bas.
7. **Publier.** Compte jusqu'à une semaine.
8. Une fois acceptée, envoie-moi le lien (`https://microsoftedge.microsoft.com/addons/detail/...`).

Avec les deux liens, je mets les boutons dans la fenêtre « Installer l'extension » de l'appli et je publie la nouvelle version.

## Pour les mises à jour de l'extension

À chaque nouvelle version de l'extension (numéro dans `browser-extension\manifest.json`), envoie le nouveau `.zip` dans les deux boutiques (onglet **Package / Paquet** → **Importer un nouveau paquet**) puis renvoie pour examen. Les navigateurs mettent ensuite l'extension à jour tout seuls.

---

## Textes à copier

### Description (English)

```
AniSync keeps your AniList or MyAnimeList list up to date while you watch anime, automatically, once you've really finished an episode.

This extension is the browser companion of the free AniSync app for Windows (https://github.com/Arzhaell/AniSync). Streaming sites like Crunchyroll don't show the anime name or the episode number in the tab title, so the extension reads them from the page and passes them to the AniSync app on your computer.

What it does
• Reads the series, season and episode number of the video you're watching, and whether it's playing or paused.
• Sends this only to the AniSync app on the same computer (http://localhost). Nothing is sent to the developer or to anyone else.
• The app counts an episode after 20 minutes of actual playback (pauses excluded) and updates your list: progress only moves forward, missing anime are added, the last episode marks it as completed.

Supported sites
Crunchyroll, ADN (Anime Digital Network), Netflix, Prime Video and Disney+ work right away. On any other streaming site, open the extension and click "Enable on this site": your browser asks for your permission, for that site only.

Requirements
• The free AniSync app for Windows 10/11: https://github.com/Arzhaell/AniSync/releases
• An AniList or MyAnimeList account.

AniSync is not affiliated with AniList, MyAnimeList or the streaming sites.
```

### Description (Français)

```
AniSync tient ta liste AniList ou MyAnimeList à jour pendant que tu regardes des animes, tout seul, quand tu as vraiment fini un épisode.

Cette extension accompagne l'appli gratuite AniSync pour Windows (https://github.com/Arzhaell/AniSync). Des sites de streaming comme Crunchyroll n'affichent ni le nom de l'anime ni le numéro d'épisode dans le titre de l'onglet : l'extension les lit dans la page et les transmet à l'appli AniSync de ton ordinateur.

Ce qu'elle fait
• Elle lit la série, la saison et le numéro de l'épisode que tu regardes, et si la vidéo est en lecture ou en pause.
• Elle l'envoie uniquement à l'appli AniSync du même ordinateur (http://localhost). Rien n'est envoyé au développeur ni à qui que ce soit.
• L'appli compte un épisode après 20 minutes de lecture réelle (pauses exclues) et met ta liste à jour : la progression ne fait qu'avancer, un anime absent est ajouté, le dernier épisode le passe en « Terminé ».

Sites pris en charge
Crunchyroll, ADN (Anime Digital Network), Netflix, Prime Video et Disney+ marchent tout de suite. Sur un autre site de streaming, ouvre l'extension et clique sur « Activer sur ce site » : le navigateur te demande ton accord, pour ce site seulement.

Il te faut
• L'appli gratuite AniSync pour Windows 10/11 : https://github.com/Arzhaell/AniSync/releases
• Un compte AniList ou MyAnimeList.

AniSync n'est affilié ni à AniList, ni à MyAnimeList, ni aux sites de streaming.
```

### Confidentialité (Chrome Web Store → *Privacy practices*)

Les examinateurs lisent l'anglais : copie ces textes tels quels.

**Single purpose / Objectif unique**
```
Tell the AniSync desktop app on the user's computer which anime episode is playing in the browser, so the app can update the user's AniList or MyAnimeList list.
```

**activeTab**
```
When the user opens the extension popup, it reads the current tab's address to show whether AniSync is active on this site, and lists the page's embedded video players so the user can enable them.
```

**scripting**
```
Registers the content script on the streaming sites the user enables with "Enable on this site", and injects it into tabs of that site that are already open, so no reload is needed. The popup also uses it to list the embedded video players of the current tab.
```

**Host permissions / Autorisations d'hôte**
```
Listed streaming sites (Crunchyroll, ADN, Netflix, Prime Video, Disney+): the content script reads the series title, season, episode number and play/pause state of the video.
http://localhost:47814: the extension sends what it reads only to the AniSync desktop app running on the same computer. No other server is contacted.
Optional access to other sites: requested at runtime, for one site at a time, only when the user clicks "Enable on this site".
```

**Remote code / Code distant** : **Non, je n'utilise pas de code distant** (*No, I am not using remote code*).

**Données utilisées** : coche **Contenu des sites Web** (*Website content*), puis les trois cases de certification (pas de vente, pas d'autre usage, pas d'évaluation de crédit). Lien de la politique de confidentialité : celui du tableau.

### Notes pour les testeurs (Edge, et Chrome si demandé)

```
This extension is the companion of the free AniSync desktop app for Windows (https://github.com/Arzhaell/AniSync/releases). It reads the anime episode playing on a streaming site and sends it only to the AniSync app on the same computer (http://localhost:47814). Without the app running, the popup shows "AniSync isn't running on this PC", which is expected. No account is needed for the extension itself. To try "Enable on this site", open any video site that is not in the built-in list, click the extension icon, then "Enable on this site".
```
