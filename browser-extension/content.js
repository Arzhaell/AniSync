// AniSync : repère la vidéo en cours dans la page (ou dans un lecteur intégré) et les infos de l'épisode.
// Rien n'est envoyé sur Internet : le service de l'extension transmet seulement à l'appli AniSync sur ce PC.
(() => {
  if (window.__aniSyncLoaded) return;
  window.__aniSyncLoaded = true;

  const isTop = window.top === window;
  const MIN_DURATION = 60; // en dessous : pub, bande-annonce, aperçu
  let lastSent = 0;

  // ---------- Vidéo ----------

  function mainVideo() {
    let best = null;
    let bestScore = 0;
    for (const v of document.querySelectorAll('video')) {
      if (!Number.isFinite(v.duration) || v.duration < MIN_DURATION) continue;
      const r = v.getBoundingClientRect();
      const score = Math.max(1, r.width * r.height) * (v.paused ? 1 : 10);
      if (score > bestScore) {
        best = v;
        bestScore = score;
      }
    }
    return best;
  }

  function videoState() {
    const v = mainVideo();
    if (!v) return null;
    return {
      currentTime: v.currentTime,
      duration: v.duration,
      paused: v.paused,
      ended: v.ended,
      playbackRate: v.playbackRate,
    };
  }

  // ---------- Infos de l'épisode (page principale seulement) ----------

  const dedupe = (list) => [...new Map(list.filter(Boolean).map((s) => [s.trim().toLowerCase(), s.trim()])).values()];
  const toInt = (x) => {
    const n = parseInt(x, 10);
    return Number.isFinite(n) && n > 0 ? n : null;
  };

  // Crunchyroll : /series/GG5H5XQX4/frieren-beyond-journeys-end → « frieren beyond journeys end » (nom anglais)
  function slugTitle(url) {
    const m = /\/series\/[^/]+\/([a-z0-9-]{2,})/i.exec(url || '');
    return m ? m[1].replace(/-/g, ' ') : null;
  }

  // Données structurées schema.org (Crunchyroll et d'autres sites) : série, saison, numéro d'épisode.
  function fromJsonLd() {
    const items = [];
    for (const script of document.querySelectorAll('script[type="application/ld+json"]')) {
      try {
        const json = JSON.parse(script.textContent);
        for (const item of Array.isArray(json) ? json : json['@graph'] || [json]) items.push(item);
      } catch {
        // JSON invalide : on ignore
      }
    }
    const ep = items.find((x) => x && /Episode/i.test([].concat(x['@type'] || []).join(' ')));
    if (!ep) return null;

    const season = ep.partOfSeason || {};
    const series = ep.partOfSeries || season.partOfSeries || {};
    const titles = dedupe([slugTitle(series['@id'] || series.url || season['@id']), series.name]);

    let episode = toInt(ep.episodeNumber);
    if (!episode) {
      const m = /\bE(\d{1,4})\b/.exec(ep.name || '');
      if (m) episode = toInt(m[1]);
    }
    let seasonNumber = toInt(season.seasonNumber);
    if (!seasonNumber) {
      const m = /(?:Season|Saison)\s*(\d+)/i.exec(season.name || ep.name || '');
      if (m) seasonNumber = toInt(m[1]);
    }
    if (!titles.length || !episode) return null;
    return { series: titles, season: seasonNumber, episode };
  }

  // Netflix : le titre n'est affiché que quand les contrôles sont visibles, on garde le dernier vu.
  let netflixCache = null;
  function fromNetflix() {
    if (!/(^|\.)netflix\.com$/.test(location.hostname)) return null;
    const el = document.querySelector('[data-uia="video-title"]');
    if (el) {
      const name = el.querySelector('h4')?.textContent?.trim();
      const text = [...el.querySelectorAll('span')].map((s) => s.textContent).join(' ');
      const s = /\b(?:S|Saison\s*)(\d{1,2})\s*:/i.exec(text);
      const e = /(?:\b[EÉ]|Épisode\s*|Episode\s*)(\d{1,4})\b/i.exec(text);
      if (name && e) netflixCache = { url: location.pathname, series: [name], season: s ? toInt(s[1]) : null, episode: toInt(e[1]) };
    }
    return netflixCache && netflixCache.url === location.pathname ? netflixCache : null;
  }

  function pageInfo() {
    const info = { title: document.title, host: location.hostname.replace(/^www\./, '') };
    const md = navigator.mediaSession && navigator.mediaSession.metadata;
    if (md) {
      info.mediaTitle = md.title || '';
      info.mediaArtist = md.artist || '';
      info.mediaAlbum = md.album || '';
    }
    const structured = fromJsonLd() || fromNetflix();
    if (structured) {
      info.series = structured.series;
      info.season = structured.season;
      info.episode = structured.episode;
    }
    return info;
  }

  // ---------- Envoi au service de l'extension ----------

  function send() {
    const video = videoState();
    if (!video) return; // pas de vidéo dans ce cadre : rien à dire
    const message = { type: 'report', video };
    if (isTop) message.page = pageInfo();
    try {
      chrome.runtime.sendMessage(message);
      lastSent = Date.now();
    } catch {
      // extension rechargée : ce script est orphelin
    }
  }

  setInterval(send, 2000);
  for (const event of ['play', 'pause', 'ended', 'seeked']) {
    document.addEventListener(event, () => Date.now() - lastSent > 300 && send(), true);
  }

  // La vidéo est souvent dans un lecteur intégré : le service demande alors les infos à la page principale.
  if (isTop) {
    chrome.runtime.onMessage.addListener((message, _sender, reply) => {
      if (message && message.type === 'getPage') reply(pageInfo());
    });
  }
})();
