# AniSync

**English** · [Français](README.fr.md)

A Windows app that spots the anime you're watching on your PC and updates your **AniList** or **MyAnimeList** list on its own once you've really finished an episode.

## Install

`build.ps1` builds, in `dist\`:

- **`AniSync-Setup-<version>.exe`** (recommended): the installer, in AniSync colors, in English or French depending on your Windows language.
- `AniSync-Setup-<version>-en.msi` / `-fr.msi`: the same installer as a Windows Installer package, in English or French, with the standard Windows UI. Handy for deployment or a silent install (`msiexec /i AniSync-Setup-<version>-en.msi /qn`).
- `AniSync-Portable-<version>.exe`: no-install version, runs from anywhere. It shares settings and accounts with the installed version (`%APPDATA%\AniSync`). Only one of the two can run at a time.

The installer (`.exe` or `.msi`):

- installs for your Windows account, without admin rights, in `%LOCALAPPDATA%\Programs\AniSync`;
- creates Desktop and Start menu shortcuts;
- turns on start with Windows, through a shortcut in the Startup folder, which you can turn off in the app;
- adds AniSync to Settings > Apps so you can uninstall it.

To update, run a newer installer: it closes the app if it's open and keeps your settings and accounts.

Everything is self-contained: .NET is included, nothing else to install (64-bit Windows 10/11).

## Language

The app shows up in English or French depending on your Windows language. To force one: Settings > **Language** (Auto, Français or English), then **Restart**. The browser extension follows the browser's language.

## First launch

On first launch, click **Add an account** and pick **AniList** or **MyAnimeList**. The sign-in window walks you through 3 steps. You only do them once per site, because each site requires the app to have its own key.

| | AniList | MyAnimeList |
|---|---|---|
| Page to create the key | https://anilist.co/settings/developer, **Create New Client** | https://myanimelist.net/apiconfig, **Create ID** (App Type: *other*) |
| Redirect URL | `http://localhost:47813/callback` | `http://localhost:47813/callback` |
| Then | paste the **Client ID** into AniSync, then **Approve** | paste the **Client ID** into AniSync, then **Allow** |

Tokens are encrypted with your Windows account (DPAPI) in `%APPDATA%\AniSync\settings.json`. The AniList one lasts a year; the MyAnimeList one is renewed automatically.

## How it works

- **Detection**: the app reads Windows media sessions (Edge, Chrome, Opera, Firefox, Media Player…), which give the title, play or pause, and the duration. For players that don't publish this (VLC, MPC-HC, mpv, PotPlayer…), it reads the window title and checks that the player is actually playing sound.
- **Finished episode**: only **actual playback time** counts. Pauses and skipping ahead are excluded. The default threshold is 20 min and can be changed in the app. For short episodes, 85% of the length is enough.
- **List update**:
  - Anime missing from your list: it's added (Watching, or Completed if it was the last episode).
  - List at ep. 8 and you finish ep. 12: it moves to 12.
  - List at ep. 12 and you rewatch ep. 8: **nothing changes**.
  - Planning, Paused or Dropped: back to Watching. Last episode: Completed, with start and finish dates filled in.
- **Seasons**: "Season 2 Episode 3", "S02E03" and continuous numbering ("Jujutsu Kaisen ep. 30" becomes season 2, ep. 6) are handled.
- **French titles**: they're often in AniList's synonyms, but its search returns nothing when the text contains an accented letter ("Je veux t'aimer jusqu'à ta mort"). So the app also searches without accents. If AniList really doesn't have the title, as a last resort it asks Wikidata for its English, romaji and Japanese equivalents, then searches again with those names.
- **Listening switch**: turns monitoring off or back on. It's also in the right-click menu of the icon by the clock.

**When the app isn't sure**, it doesn't guess: it suggests the anime with the closest titles and lets you confirm.

- In the current episode panel, during the episode: up to 3 suggestions with a **That's it** button.
- In the history, once the episode is over: **Yes** (that's the one), **Other…** (pick another) or **Ignore** (not an anime, stop following this title).
- An "AniSync isn't sure" notification shows up once per series when the suggestion is very close. Clicking it opens the app.

Your choice is remembered for the next episodes. If the app picked the wrong anime, **Fix** lets you choose the right one, and every update in the history has an **Undo** button.

Closing the window keeps AniSync running in the notification area. To quit, right-click the icon and choose **Quit**.

## Accounts (AniList and MyAnimeList)

You can add **as many accounts as you like**, AniList and MyAnimeList alike, and pick the **active account**: that's the one updated when you finish an episode.

- **At the top of the app**: clicking your avatar or name opens your AniList or MyAnimeList profile page, and the small arrow next to it opens the **Accounts window**. There you can use an account, reconnect it, remove it or add one; clicking an account also opens its page.
- **Quick switch**: right-click the icon by the clock, then **Account**.
- **Adding a second account on the same site**: signing in authorizes the account open in your browser, so first sign out of that site in the browser (or sign in to the right account). Reconnecting an existing account updates it without creating a duplicate.
- **Shared by all accounts**: fixes, ignored titles and settings. The history shows which account was updated, and "Undo" acts on that account.
- An expired sign-in doesn't remove the account: it's marked "needs reconnecting".
- Each account's tokens are encrypted with your Windows account (DPAPI).
- Coming from an earlier version, your existing sign-ins become accounts automatically.

**MyAnimeList**

- Anime recognition doesn't change: it still goes through AniList search (French titles, seasons, suggestions…), which gives each anime's MyAnimeList number.
- The rules are the same: move forward without ever going back, add the anime if it's missing, mark it Completed at the last episode, and "Undo" is still there.
- You create a key once at https://myanimelist.net/apiconfig (App Type: *other*, Redirect URL: `http://localhost:47813/callback`). After that, the app renews the sign-in on its own.
- Limitation: a few rare anime on AniList have no MyAnimeList entry, and the app tells you so.

## Browser extension (Crunchyroll…)

Crunchyroll shows neither the anime name nor the episode number in its tabs (only "Season 1 <episode title>"). The extension reads what the app can't see straight from the page, then passes it to AniSync **on this PC only** (`http://localhost:47814`). It picks up:

- the series, in English from the address and in the page's language from the page;
- the season and episode number;
- the actual video state: playing, paused, duration.

It also works when the player is embedded in another page (iframe).

- **Sites**: Crunchyroll, ADN, Netflix, Prime Video and Disney+ work right away. On any other streaming site, click the extension icon, then **Enable on this site**: the browser asks for your permission, for that site (and its embedded player) only. You can remove a site at any time in the extension's settings in the browser.
- **Install**: in the app, Settings > Browser extension > **Install…**, then follow the steps (developer mode > "Load unpacked"). It works in Opera, Edge, Chrome and Brave. One-click install from the Chrome Web Store and Edge Add-ons is on its way.
- **Privacy**: see [PRIVACY.md](PRIVACY.md).
- **Outside the browser**, nothing changes: AniSync keeps detecting video players and apps as before. When the extension is active in a browser, it's the reference for that browser.
- Only the extension can talk to AniSync: requests without its `X-AniSync-Extension` header are refused, and a web page can't add it.

## Known limitations

- Without the extension, the episode number has to appear in the tab, video or file title. That's not the case on Crunchyroll or Netflix, for example.
- Netflix (with the extension): the title is only read once the player controls have shown at least once during the episode.
- Time from a half-watched episode is kept while the app runs (12 h max). It's lost if you quit.

## Development

Rebuild everything (tests, portable exe, `.msi` files, then `Setup.exe`):

```bash
powershell -ExecutionPolicy Bypass -File build.ps1
```

The version comes from `<Version>` in `src/AniSync/AniSync.csproj`: just bump it before rebuilding to ship an update.

- `installer/`: WiX 5 `.msi` (`Package.wxs`), one per language (strings in `Package.en-US.wxl` and `Package.fr-FR.wxl`, licenses `License.en.rtf` and `License.fr.rtf`). WiX comes from NuGet, nothing to install.
- `installer/bundle/`: the `Setup.exe` wrapping the English `.msi`, with its theme and strings in `Theme/` (`AniSyncTheme.wxl` in English, `AniSyncTheme.fr.wxl` for French Windows).

- `browser-extension/`: the extension (Manifest V3), embedded in the exe, with its strings in `_locales/` and the site list in `manifest.json` (site logic: `sites.js`, tested by `tests/extension/`)
- `store/`: everything for the extension stores (guide in French, listing texts, images); `build.ps1` also creates `dist\AniSync-Extension-<version>.zip`
- `src/AniSync/Detection`: reading media sessions, windows and sound, receiving the extension's info (`BrowserBridge.cs`)
- `src/AniSync/Core`: title parsing, watch time, sync rules, history
- `src/AniSync/AniList`: GraphQL API, OAuth sign-in, anime lookup, French title translation (`WikidataTitles.cs`), AniList list (`AniListService.cs`)
- `src/AniSync/Mal`: MyAnimeList list (API v2, OAuth + PKCE sign-in, status mapping)
- `src/AniSync/Core/ListServices.cs`: common interface for both sites
- `src/AniSync/Core/Lang.cs` and `src/AniSync/Ui/Tr.cs`: English and French strings (`L.T("français", "English")`)
- Log: `%APPDATA%\AniSync\anisync.log`
