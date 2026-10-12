const assert = require('node:assert/strict');
const fs = require('node:fs');
const Module = require('node:module');
const path = require('node:path');
const test = require('node:test');
const ts = require('typescript');

const filename = path.resolve(
  __dirname,
  '../src/Activity/Queue/Details/getSeriesQueueDetails.ts'
);
const output = ts.transpileModule(fs.readFileSync(filename, 'utf8'), {
  compilerOptions: {
    module: ts.ModuleKind.CommonJS,
    target: ts.ScriptTarget.ES2020,
  },
}).outputText;
const compiled = new Module(filename, module);
compiled._compile(output, filename);
const getDetails = compiled.exports.default;

const seasonSizes = [22, 22, 22, 22, 20, 13, 12];
let nextId = 1;
const episodeIdsBySeason = Object.fromEntries(
  seasonSizes.map((count, index) => [
    index + 1,
    Array.from({ length: count }, () => nextId++),
  ])
);
const pack = {
  seriesId: 42,
  trackedDownloadState: 'downloading',
  episodeIds: Object.values(episodeIdsBySeason).flat(),
  episodeIdsBySeason,
  episodeIdsWithFiles: episodeIdsBySeason[1],
};

test('full pack counts each season separately and marks existing season as upgrading', () => {
  seasonSizes.forEach((count, index) => {
    assert.deepEqual(getDetails([pack], 42, index + 1), {
      count,
      episodesWithFiles: index === 0 ? 22 : 0,
    });
  });
  assert.deepEqual(getDetails([pack], 42), {
    count: 133,
    episodesWithFiles: 22,
  });
});

test('overlapping packs and episodes do not inflate counts', () => {
  const single = {
    ...pack,
    episodeIds: [1],
    episodeIdsBySeason: { 1: [1] },
    episodeIdsWithFiles: [1],
  };
  assert.deepEqual(getDetails([pack, pack, single], 42, 1), {
    count: 22,
    episodesWithFiles: 22,
  });
  assert.deepEqual(getDetails([pack, pack, single], 42), {
    count: 133,
    episodesWithFiles: 22,
  });
});

test('imported torrents and other series do not mark a season as downloading', () => {
  assert.deepEqual(
    getDetails(
      [
        { ...pack, trackedDownloadState: 'imported' },
        { ...pack, seriesId: 43 },
      ],
      42,
      1
    ),
    { count: 0, episodesWithFiles: 0 }
  );
});

test('empty queue and uncovered season report zero', () => {
  assert.deepEqual(getDetails(undefined, 42, 1), {
    count: 0,
    episodesWithFiles: 0,
  });
  assert.deepEqual(getDetails([], 42), { count: 0, episodesWithFiles: 0 });
  assert.deepEqual(getDetails([pack], 42, 8), {
    count: 0,
    episodesWithFiles: 0,
  });
});

test('specials season zero is filtered instead of being treated as all seasons', () => {
  const special = {
    ...pack,
    episodeIds: [500],
    episodeIdsBySeason: { 0: [500] },
    episodeIdsWithFiles: [],
  };
  assert.deepEqual(getDetails([pack, special], 42, 0), {
    count: 1,
    episodesWithFiles: 0,
  });
});

test('partial import only subtracts existing files in the requested season', () => {
  const partial = {
    ...pack,
    episodeIdsWithFiles: [
      ...pack.episodeIdsWithFiles,
      ...episodeIdsBySeason[2].slice(0, 5),
    ],
  };
  assert.deepEqual(getDetails([partial], 42, 2), {
    count: 22,
    episodesWithFiles: 5,
  });
  assert.deepEqual(getDetails([partial], 42, 3), {
    count: 22,
    episodesWithFiles: 0,
  });
});
