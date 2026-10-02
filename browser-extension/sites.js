// AniSync : quels sites l'extension a le droit de lire. Partagé par le service (background.js) et la petite fenêtre (popup.js).
// Les sites de streaming connus sont dans manifest.json ; les autres s'activent d'un clic, avec l'accord du navigateur.
(function (root) {
  function parse(url) {
    try {
      const u = new URL(url);
      return u.protocol === 'https:' || u.protocol === 'http:' ? u : null;
    } catch {
      return null;
    }
  }

  // Règle « ce site et ses sous-domaines » : https://www.exemple.fr/x → *://*.exemple.fr/*
  function siteRule(url) {
    const u = parse(url);
    if (!u || !u.hostname) return null;
    const host = u.hostname.replace(/^www\./, '');
    // Adresse IP ou nom sans point (localhost...) : pas de sous-domaines possibles.
    const plain = !host.includes('.') || /^[\d.]+$/.test(host) || host.startsWith('[');
    return plain ? `*://${host}/*` : `*://*.${host}/*`;
  }

  // L'adresse est-elle couverte par une règle « *://*.domaine/* », « *://domaine/* » ou « https://domaine/* » ?
  function matches(url, rule) {
    const u = parse(url);
    const m = /^(\*|https?):\/\/(\*\.)?([^/]+)\/\*$/.exec(rule || '');
    if (!u || !m) return false;
    if (m[1] !== '*' && `${m[1]}:` !== u.protocol) return false;
    return u.hostname === m[3] || (!!m[2] && u.hostname.endsWith(`.${m[3]}`));
  }

  const isCovered = (url, rules) => (rules || []).some((rule) => matches(url, rule));

  // Lecteurs intégrés venant d'un autre site : une iframe assez grande pour être un lecteur (pas une pub).
  function playerFrameRules(frames, pageUrl, covered) {
    const rules = [];
    for (const frame of frames || []) {
      if (!(frame.width >= 320 && frame.height >= 180)) continue;
      const rule = siteRule(frame.src);
      if (!rule || rules.includes(rule) || matches(frame.src, siteRule(pageUrl)) || isCovered(frame.src, covered)) continue;
      rules.push(rule);
    }
    return rules;
  }

  const api = { siteRule, matches, isCovered, playerFrameRules };
  if (typeof module !== 'undefined' && module.exports) module.exports = api;
  else root.AniSyncSites = api;
})(typeof self !== 'undefined' ? self : globalThis);
