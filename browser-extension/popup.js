const $ = (id) => document.getElementById(id);

async function checkApp() {
  try {
    const response = await fetch('http://localhost:47814/v1/ping', { headers: { 'X-AniSync-Extension': '1' } });
    const json = await response.json();
    $('dot').className = 'dot ' + (json.listening ? 'ok' : 'warn');
    $('app').textContent = json.listening ? 'Connecté à AniSync · écoute activée' : 'Connecté à AniSync · écoute désactivée';
  } catch {
    $('dot').className = 'dot err';
    $('app').textContent = "AniSync n'est pas lancé sur ce PC";
  }
}

async function showTab() {
  const [tab] = await chrome.tabs.query({ active: true, currentWindow: true });
  if (!tab) return;
  chrome.runtime.sendMessage({ type: 'status', tabId: tab.id }, (s) => {
    void chrome.runtime.lastError;
    const fresh = s && s.video && Date.now() - s.videoAt < 10000;
    if (!fresh) {
      $('tab').textContent = 'Aucune vidéo en cours';
      $('tabDetail').textContent = '';
      return;
    }
    const page = s.page || {};
    if (page.series && page.episode) {
      $('tab').textContent = page.series[page.series.length - 1];
      $('tabDetail').textContent = (page.season > 1 ? `Saison ${page.season} · ` : '') + `Épisode ${page.episode}` +
        (s.video.paused ? ' · en pause' : ' · en lecture');
    } else {
      $('tab').textContent = page.title || 'Vidéo détectée';
      $('tabDetail').textContent = s.video.paused ? 'En pause' : 'En lecture';
    }
  });
}

checkApp();
showTab();
