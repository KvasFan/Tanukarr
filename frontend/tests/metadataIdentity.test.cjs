const assert = require('node:assert/strict');
const fs = require('node:fs');
const Module = require('node:module');
const path = require('node:path');
const test = require('node:test');
const ts = require('typescript');

function load(name) {
  const filename = path.resolve(__dirname, `../src/Series/${name}.ts`);
  const output = ts.transpileModule(fs.readFileSync(filename, 'utf8'), {
    compilerOptions: {
      module: ts.ModuleKind.CommonJS,
      target: ts.ScriptTarget.ES2020
    }
  }).outputText;
  const compiled = new Module(filename, module);
  // eslint-disable-next-line no-underscore-dangle
  compiled._compile(output, filename);
  return compiled.exports.default;
}

const provider = load('getMetadataProvider');
const sameSeries = load('isSameSeriesIdentity');

test('native existing-series matches use provider and foreign ID across synthetic TVDB changes', () => {
  const stored = { metadataSource: 'aniList', foreignId: '1', tvdbId: -100 };
  assert.equal(sameSeries(stored, { ...stored, tvdbId: -200 }), true);
  assert.equal(sameSeries(stored, { ...stored, foreignId: '5' }), false);
  assert.equal(sameSeries(stored, { ...stored, metadataSource: 'myAnimeList' }), false);
  assert.equal(sameSeries(stored, { tvdbId: -100 }), false);
});

test('legacy TVDB matching requires a positive real TVDB ID and cannot match native placeholders', () => {
  assert.equal(sameSeries({ tvdbId: 123 }, { metadataSource: 'tvdb', tvdbId: 123 }), true);
  assert.equal(sameSeries({ tvdbId: -1 }, { tvdbId: -1 }), false);
  assert.equal(sameSeries({ tvdbId: 0 }, { tvdbId: 0 }), false);
  assert.equal(sameSeries({ metadataSource: 'aniList', foreignId: undefined }, { metadataSource: 'aniList' }), false);
});

test('AniList, MAL and TMDB attribution and links use the owning provider ID', () => {
  assert.deepEqual(provider({ metadataSource: 'aniList', foreignId: '1', tvdbId: -1 }),
    { name: 'AniList', externalId: '1', url: 'https://anilist.co/anime/1' });
  assert.deepEqual(provider({ metadataSource: 'myAnimeList', foreignId: '5', tvdbId: -1 }),
    { name: 'MyAnimeList', externalId: '5', url: 'https://myanimelist.net/anime/5' });
  assert.deepEqual(provider({ metadataSource: 'tmdb', foreignId: '123', tmdbId: 456, tvdbId: -1 }),
    { name: 'TMDB', externalId: '123', url: 'https://www.themoviedb.org/tv/123' });
  assert.equal(provider({ metadataSource: 'tmdb', tmdbId: 456 }).url, 'https://www.themoviedb.org/tv/456');
});

test('TVDB defaults keep positive links and never send negative placeholders to TheTVDB', () => {
  assert.equal(provider({ tvdbId: 123 }).url, 'https://www.thetvdb.com/?tab=series&id=123');
  assert.equal(provider({ tvdbId: -1 }).url, undefined);
  assert.equal(provider({ metadataSource: 'aniList', foreignId: undefined, tvdbId: 123 }).url, undefined);
});

test('TVmaze links preserve the native ID without treating it as TVDB', () => {
  assert.deepEqual(provider({ metadataSource: 'tvMaze', foreignId: '123', tvdbId: -1 }),
    { name: 'TVmaze', externalId: '123', url: 'https://www.tvmaze.com/shows/123' });
  assert.equal(provider({ metadataSource: 'tvMaze', foreignId: '../123' }).url, undefined);
});

test('provider links reject injected/invalid foreign IDs and preserve IMDb title IDs', () => {
  // eslint-disable-next-line no-script-url
  const invalidIds = ['-1', '0', 'javascript:alert(1)', '../settings/general', '1?apikey=secret'];
  for (const foreignId of invalidIds) {
    assert.equal(provider({ metadataSource: 'aniList', foreignId }).url, undefined);
    assert.equal(provider({ metadataSource: 'myAnimeList', foreignId }).url, undefined);
  }

  assert.equal(provider({ metadataSource: 'imdb', foreignId: 'tt1234' }).url, 'https://www.imdb.com/title/tt1234/');
  assert.equal(provider({ metadataSource: 'imdb', foreignId: 'tt1234/../' }).url, undefined);
});

test('TMDB movie and TV namespaces remain distinct while OMDb retains its IMDb identity', () => {
  assert.equal(sameSeries({ metadataSource: 'tmdbMovie', foreignId: '42' }, { metadataSource: 'tmdb', foreignId: '42' }), false);
  assert.deepEqual(provider({ metadataSource: 'tmdbMovie', foreignId: '42', tvdbId: -42 }), { name: 'TMDB Movies', externalId: '42', url: 'https://www.themoviedb.org/movie/42' });
  assert.deepEqual(provider({ metadataSource: 'omdb', foreignId: 'tt0245429', tvdbId: -42 }), { name: 'OMDb', externalId: 'tt0245429', url: 'https://www.imdb.com/title/tt0245429/' });
  assert.equal(provider({ metadataSource: 'omdb', foreignId: '42' }).url, undefined);
  assert.equal(provider({ metadataSource: 'tmdbMovie', foreignId: '../42' }).url, undefined);
});
