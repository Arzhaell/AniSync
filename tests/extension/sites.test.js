// Tests de browser-extension/sites.js (lancés par build.ps1 avec « node --test »).
const test = require('node:test');
const assert = require('node:assert');
const path = require('node:path');
const fs = require('node:fs');
const sites = require('../../browser-extension/sites.js');

const manifest = JSON.parse(fs.readFileSync(path.join(__dirname, '../../browser-extension/manifest.json'), 'utf8'));
const BUILT_IN = manifest.content_scripts.flatMap((c) => c.matches);

test('la règle couvre le site et ses sous-domaines, sans le www', () => {
  assert.strictEqual(sites.siteRule('https://www.anime-site.fr/episode/12'), '*://*.anime-site.fr/*');
  assert.strictEqual(sites.siteRule('http://video.example.com/x?y=1'), '*://*.video.example.com/*');
  assert.strictEqual(sites.siteRule('http://192.168.1.20:8096/web'), '*://192.168.1.20/*');
  assert.strictEqual(sites.siteRule('http://localhost:8096/'), '*://localhost/*');
});

test('pas de règle pour les pages du navigateur', () => {
  for (const url of ['chrome://extensions', 'edge://newtab', 'about:blank', 'file:///C:/video.mp4', '', undefined]) {
    assert.strictEqual(sites.siteRule(url), null, String(url));
  }
});

test('les sites de streaming connus sont couverts, pas les autres', () => {
  assert.ok(sites.isCovered('https://www.crunchyroll.com/fr/watch/GG1U2Q5VJ/episode', BUILT_IN));
  assert.ok(sites.isCovered('https://static.crunchyroll.com/vilos-v2/web/vilos/player.html', BUILT_IN));
  assert.ok(sites.isCovered('https://animationdigitalnetwork.fr/video/frieren', BUILT_IN));
  assert.ok(sites.isCovered('https://www.netflix.com/watch/81726716', BUILT_IN));
  assert.ok(!sites.isCovered('https://www.notcrunchyroll.com/watch', BUILT_IN));
  assert.ok(!sites.isCovered('https://crunchyroll.com.evil.example/watch', BUILT_IN));
  assert.ok(!sites.isCovered('https://www.anime-site.fr/episode/12', BUILT_IN));
  // Retirés de la liste de base : à activer à la main si besoin.
  for (const url of ['https://www.youtube.com/watch?v=abc', 'https://www.hidive.com/video/1', 'https://www.bilibili.tv/en/video/1']) {
    assert.ok(!sites.isCovered(url, BUILT_IN), url);
  }
});

test('une règle accordée couvre bien le site', () => {
  const rule = sites.siteRule('https://www.anime-site.fr/');
  assert.ok(sites.matches('https://anime-site.fr/a', rule));
  assert.ok(sites.matches('http://player.anime-site.fr/b', rule));
  assert.ok(!sites.matches('https://other-site.fr/', rule));
  assert.ok(sites.matches('https://example.com/', 'https://example.com/*'));
  assert.ok(!sites.matches('http://example.com/', 'https://example.com/*'));
});

test('les lecteurs intégrés d\'un autre site sont demandés, pas les pubs ni les cadres déjà couverts', () => {
  const frames = [
    { src: 'https://player.video-host.net/embed/abc', width: 960, height: 540 },
    { src: 'https://player.video-host.net/embed/abc', width: 960, height: 540 }, // en double
    { src: 'https://ads.example.com/banner', width: 300, height: 250 },          // trop petit pour un lecteur
    { src: 'https://cdn.anime-site.fr/player', width: 960, height: 540 },        // même site que la page
    { src: 'https://static.crunchyroll.com/player', width: 960, height: 540 },   // déjà dans la liste
    { src: 'about:blank', width: 960, height: 540 },
  ];
  assert.deepStrictEqual(
    sites.playerFrameRules(frames, 'https://www.anime-site.fr/episode/12', BUILT_IN),
    ['*://*.player.video-host.net/*']);
});
