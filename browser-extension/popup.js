const $ = (id) => document.getElementById(id);
const t = (key, ...args) => chrome.i18n.getMessage(key, args.map(String));
// Sites de streaming connus (manifest.json) : l'extension y est active dès l'installation.
const BUILT_IN = chrome.runtime.getManifest().content_scripts.flatMap((c) => c.matches);

document.documentElement.lang = chrome.i18n.getUILanguage();
for (const el of document.querySelectorAll('[data-i18n]')) el.textContent = t(el.dataset.i18n);

async function checkApp() {
  try {
    const response = await fetch('http://localhost:47814/v1/ping', { headers: { 'X-AniSync-Extension': '1' } });
    const json = await response.json();
    $('dot').className = 'dot ' + (json.listening ? 'ok' : 'warn');
    $('app').textContent = t(json.listening ? 'connectedListening' : 'connectedNotListening');
  } catch {
    $('dot').className = 'dot err';
    $('app').textContent = t('appNotRunning');
  }
}

// Ce qu'il faut autoriser pour lire la vidéo de cet onglet : le site s'il n'est pas dans la liste,
// et les lecteurs intégrés venant d'un autre site. Null si tout est déjà autorisé.
let pending = null;

async function missingAccess(tab) {
  const site = AniSyncSites.siteRule(tab.url);
  if (!site) return null;

  // La liste des lecteurs intégrés (l'accès à l'onglet est donné par le clic sur l'icône de l'extension).
  let frames = [];
  try {
    const [result] = await chrome.scripting.executeScript({
      target: { tabId: tab.id },
      func: () => [...document.querySelectorAll('iframe')].map((f) => {
        const r = f.getBoundingClientRect();
        return { src: f.src, width: r.width, height: r.height };
      }),
    });
    frames = (result && result.result) || [];
  } catch {
    // page protégée par le navigateur : on ne demandera que le site
  }

  const wanted = AniSyncSites.isCovered(tab.url, BUILT_IN) ? [] : [site];
  wanted.push(...AniSyncSites.playerFrameRules(frames, tab.url, BUILT_IN));
  const origins = [];
  for (const rule of wanted) {
    if (!(await chrome.permissions.contains({ origins: [rule] }))) origins.push(rule);
  }
  if (!origins.length) return null;
  return { siteMissing: origins[0] === site, host: new URL(tab.url).hostname, origins };
}

function showVideo(tab) {
  chrome.runtime.sendMessage({ type: 'status', tabId: tab.id }, (s) => {
    void chrome.runtime.lastError;
    const fresh = s && s.video && Date.now() - s.videoAt < 10000;
    if (!fresh) {
      $('tab').textContent = t('noVideo');
      $('tabDetail').textContent = '';
      return;
    }
    const page = s.page || {};
    if (page.series && page.episode) {
      $('tab').textContent = page.series[page.series.length - 1];
      $('tabDetail').textContent = (page.season > 1 ? t('season', page.season) : '') + t('episode', page.episode) +
        ' · ' + t(s.video.paused ? 'paused' : 'playing').toLowerCase();
    } else {
      $('tab').textContent = page.title || t('videoDetected');
      $('tabDetail').textContent = t(s.video.paused ? 'paused' : 'playing');
    }
  });
}

async function showTab() {
  const [tab] = await chrome.tabs.query({ active: true, currentWindow: true });
  if (!tab) return;
  pending = tab.url ? await missingAccess(tab) : null;
  $('site').classList.toggle('hidden', !pending);
  if (pending && pending.siteMissing) {
    $('tab').textContent = t('notActiveHere');
    $('tabDetail').textContent = pending.host;
  } else {
    showVideo(tab);
  }
  if (pending) $('siteHint').textContent = t(pending.siteMissing ? 'enableHint' : 'enablePlayerHint');
}

$('enable').addEventListener('click', () => {
  if (!pending) return;
  // Appelé directement dans le clic : le navigateur n'affiche sa demande que pendant une action de l'utilisateur.
  // La fenêtre peut se fermer pendant la demande : le service de l'extension s'occupe alors du reste.
  chrome.permissions.request({ origins: pending.origins }, (granted) => {
    void chrome.runtime.lastError;
    if (!granted) {
      $('siteHint').textContent = t('enableRefused');
      return;
    }
    pending = null;
    $('site').classList.add('hidden');
    $('tab').textContent = t('enabledHere');
    $('tabDetail').textContent = t('enabledHint');
  });
});

checkApp();
showTab();
