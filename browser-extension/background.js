// AniSync : regroupe par onglet la vidéo (qui peut être dans un lecteur intégré) et les infos de la page,
// puis les transmet à l'appli AniSync sur ce PC. Aucune autre destination.
const APP = 'http://localhost:47814';
const PAGE_REFRESH_MS = 8000;
const tabs = new Map(); // tabId → { page, pageAt, video, videoAt, frameId }
let status = { connected: false, listening: null, at: 0 };

function browserName() {
  const brands = (navigator.userAgentData && navigator.userAgentData.brands) || [];
  const brand = brands.map((b) => b.brand).find((b) => !/Chromium|Not.?A.?Brand/i.test(b));
  if (brand) return brand;
  const ua = navigator.userAgent;
  if (/OPR\//.test(ua)) return 'Opera';
  if (/Edg\//.test(ua)) return 'Microsoft Edge';
  return 'Chrome';
}
const BROWSER = browserName();

function requestPage(tabId) {
  chrome.tabs.sendMessage(tabId, { type: 'getPage' }, { frameId: 0 }, (page) => {
    void chrome.runtime.lastError; // page sans script (onglet interne...) : rien à faire
    if (!page) return;
    const tab = tabs.get(tabId) || {};
    tab.page = page;
    tab.pageAt = Date.now();
    tabs.set(tabId, tab);
  });
}

async function push(tabId, tab) {
  const body = { tabId, browser: BROWSER, ...(tab.page || {}), video: tab.video };
  try {
    const response = await fetch(`${APP}/v1/playback`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', 'X-AniSync-Extension': '1' },
      body: JSON.stringify(body),
    });
    const json = response.ok ? await response.json() : {};
    status = { connected: response.ok, listening: json.listening ?? null, at: Date.now() };
  } catch {
    status = { connected: false, listening: null, at: Date.now() };
  }
}

chrome.runtime.onMessage.addListener((message, sender, reply) => {
  if (message && message.type === 'status') {
    const tab = tabs.get(message.tabId);
    reply({ ...status, browser: BROWSER, page: tab && tab.page, video: tab && tab.video, videoAt: tab && tab.videoAt });
    return;
  }
  if (!message || message.type !== 'report' || !sender.tab || !message.video) return;

  const tabId = sender.tab.id;
  const now = Date.now();
  const tab = tabs.get(tabId) || {};

  if (message.page && sender.frameId === 0) {
    tab.page = message.page;
    tab.pageAt = now;
  }
  // Plusieurs vidéos (pub + épisode) : on garde celle en lecture, sinon la dernière signalée.
  const stale = !tab.video || now - tab.videoAt > 5000;
  if (stale || tab.frameId === sender.frameId || (!message.video.paused && tab.video.paused)) {
    tab.video = message.video;
    tab.videoAt = now;
    tab.frameId = sender.frameId;
  }
  tabs.set(tabId, tab);

  if (!tab.page || now - tab.pageAt > PAGE_REFRESH_MS) requestPage(tabId);
  push(tabId, tab);
});

chrome.tabs.onRemoved.addListener((tabId) => tabs.delete(tabId));

// ---------- Sites activés à la main (bouton « Activer sur ce site ») ----------
// Les sites de manifest.json sont lus d'office ; les autres le sont une fois que le navigateur a donné son accord.
const EXTRA_SCRIPT = 'anisync-extra-sites';
const manifest = chrome.runtime.getManifest();
const BUILT_IN = new Set([...manifest.content_scripts.flatMap((c) => c.matches), ...manifest.host_permissions]);

async function registerExtraSites() {
  const { origins = [] } = await chrome.permissions.getAll();
  const extra = origins.filter((origin) => !BUILT_IN.has(origin));
  const existing = await chrome.scripting.getRegisteredContentScripts({ ids: [EXTRA_SCRIPT] });
  if (existing.length) await chrome.scripting.unregisterContentScripts({ ids: [EXTRA_SCRIPT] });
  if (!extra.length) return;
  await chrome.scripting.registerContentScripts([{
    id: EXTRA_SCRIPT,
    matches: extra,
    js: ['content.js'],
    allFrames: true,
    runAt: 'document_idle',
    persistAcrossSessions: true,
  }]);
}

// Une seule mise à jour à la fois (plusieurs événements peuvent arriver ensemble).
let syncing = Promise.resolve();
function syncExtraSites() {
  syncing = syncing.then(registerExtraSites).catch(() => {});
  return syncing;
}

chrome.runtime.onInstalled.addListener(syncExtraSites);
chrome.runtime.onStartup.addListener(syncExtraSites);
chrome.permissions.onRemoved.addListener(syncExtraSites);
chrome.permissions.onAdded.addListener(async ({ origins = [] }) => {
  await syncExtraSites();
  if (!origins.length) return;
  // Les onglets déjà ouverts sur ces sites : pas besoin de recharger la page.
  for (const tab of await chrome.tabs.query({ url: origins })) {
    chrome.scripting.executeScript({ target: { tabId: tab.id, allFrames: true }, files: ['content.js'] }).catch(() => {});
  }
});
