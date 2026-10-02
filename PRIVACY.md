# AniSync privacy policy

**English** · [Français](#politique-de-confidentialité-danisync)

*Last updated: October 2, 2026*

This policy covers the AniSync browser extension and the AniSync app for Windows.

## Browser extension

**What it reads.** On the streaming sites in its list (Crunchyroll, ADN, Netflix, Prime Video, Disney+, YouTube, HIDIVE, Bilibili) and on the sites you enable yourself with "Enable on this site", the extension reads:

- the series title, season and episode number shown on the page, the page title and the site name;
- the state of the video: playing or paused, position and duration.

It doesn't read anything else: no forms, no passwords, no messages, no browsing history.

**Where it goes.** Only to the AniSync app on the same computer, at `http://localhost:47814`. The extension never sends anything to the developer, to an analytics service or to anyone else. There are no ads, no trackers and no account.

**What it keeps.** Nothing permanent: the latest information for each tab is kept in memory and dropped when the tab is closed. The browser itself remembers which sites you enabled; you can remove them at any time in the extension's settings.

**Permissions.**

- *Access to the listed streaming sites*: to read the episode and the video state, as described above.
- *Access to `http://localhost:47814`*: to talk to the AniSync app on your computer.
- *Optional access to other sites*: asked only when you click "Enable on this site", for that site only.
- *activeTab* and *scripting*: so the extension's window can check the current tab and turn the extension on for a site you enable, without reloading the page.

## AniSync app for Windows

The app receives what the extension reads and also looks at the titles of the media playing on your PC. Then:

- it searches the detected title on **AniList** (and, for some French titles, on **Wikidata**) to find the anime;
- once you've finished an episode, it updates **your AniList or MyAnimeList list** with the account you connected.

Only the anime title and your progress are sent, and only to these sites, under their own privacy policies. Your sign-in tokens are encrypted with your Windows account (DPAPI) and stay on your PC, in `%APPDATA%\AniSync`, along with your settings and history.

## Contact

Questions or requests: https://github.com/Arzhaell/AniSync/issues

---

# Politique de confidentialité d'AniSync

[English](#anisync-privacy-policy) · **Français**

*Dernière mise à jour : 2 octobre 2026*

Cette politique couvre l'extension navigateur AniSync et l'appli AniSync pour Windows.

## Extension navigateur

**Ce qu'elle lit.** Sur les sites de streaming de sa liste (Crunchyroll, ADN, Netflix, Prime Video, Disney+, YouTube, HIDIVE, Bilibili) et sur les sites que tu actives toi-même avec « Activer sur ce site », l'extension lit :

- le titre de la série, la saison et le numéro d'épisode affichés sur la page, le titre de la page et le nom du site ;
- l'état de la vidéo : lecture ou pause, position et durée.

Elle ne lit rien d'autre : ni formulaires, ni mots de passe, ni messages, ni historique de navigation.

**Où ça va.** Uniquement vers l'appli AniSync du même ordinateur, à l'adresse `http://localhost:47814`. L'extension n'envoie jamais rien au développeur, à un service de statistiques ni à qui que ce soit. Pas de pub, pas de pisteur, pas de compte.

**Ce qu'elle garde.** Rien de permanent : les dernières infos de chaque onglet restent en mémoire et disparaissent à la fermeture de l'onglet. C'est le navigateur qui retient les sites que tu as activés ; tu peux les retirer à tout moment dans les réglages de l'extension.

**Autorisations.**

- *Accès aux sites de streaming de la liste* : pour lire l'épisode et l'état de la vidéo, comme décrit plus haut.
- *Accès à `http://localhost:47814`* : pour parler à l'appli AniSync de ton ordinateur.
- *Accès facultatif aux autres sites* : demandé seulement quand tu cliques sur « Activer sur ce site », et pour ce site uniquement.
- *activeTab* et *scripting* : pour que la fenêtre de l'extension vérifie l'onglet en cours et active l'extension sur un site que tu autorises, sans recharger la page.

## Appli AniSync pour Windows

L'appli reçoit ce que lit l'extension et regarde aussi les titres des médias en lecture sur ton PC. Ensuite :

- elle cherche le titre détecté sur **AniList** (et, pour certains titres français, sur **Wikidata**) pour trouver l'anime ;
- quand tu as fini un épisode, elle met à jour **ta liste AniList ou MyAnimeList** avec le compte que tu as connecté.

Seuls le titre de l'anime et ta progression sont envoyés, et seulement à ces sites, selon leurs propres politiques de confidentialité. Tes jetons de connexion sont chiffrés avec ton compte Windows (DPAPI) et restent sur ton PC, dans `%APPDATA%\AniSync`, avec tes réglages et ton historique.

## Contact

Questions ou demandes : https://github.com/Arzhaell/AniSync/issues
