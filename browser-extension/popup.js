const $ = (id) => document.getElementById(id);
const t = (key, ...args) => chrome.i18n.getMessage(key, args.map(String));

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

async function showTab() {
  const [tab] = await chrome.tabs.query({ active: true, currentWindow: true });
  if (!tab) return;
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

checkApp();
showTab();
