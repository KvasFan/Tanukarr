const assert = require('node:assert/strict');
const fs = require('node:fs');
const Module = require('node:module');
const path = require('node:path');
const test = require('node:test');
const ts = require('typescript');
function load(fetch) {
  const filename = path.resolve(__dirname, '../src/Episode/fetchEpisodes.ts');
  const compiled = new Module(filename, module);
  compiled.require = () => ({ default: fetch });
  compiled._compile(
    ts.transpileModule(fs.readFileSync(filename, 'utf8'), {
      compilerOptions: {
        module: ts.ModuleKind.CommonJS,
        target: ts.ScriptTarget.ES2020,
      },
    }).outputText,
    filename
  );
  return compiled.exports.default;
}
function request(ids) {
  const query = new URLSearchParams();
  ids.forEach((id) => query.append('episodeIds', id));
  return '/api/v5/episode?' + query;
}
test('large queue returns all episodes through bounded authenticated requests', async () => {
  const ids = Array.from({ length: 1500 }, (_, i) => 2147480000 + i);
  const signal = new AbortController().signal;
  const headers = { 'X-Api-Key': 'test' };
  const calls = [];
  const fetch = load(async (options) => {
    calls.push(options);
    assert.ok(options.path.length < 4096);
    assert.equal(options.signal, signal);
    assert.equal(options.headers, headers);
    return new URL(options.path, 'http://test').searchParams
      .getAll('episodeIds')
      .map(Number);
  });
  assert.deepEqual(await fetch({ path: request(ids), signal, headers }), ids);
  assert.equal(calls.length, 15);
});
test('small and series-based requests retain their original options', async () => {
  for (const path of [request([1, 2]), '/api/v5/episode?seriesId=42']) {
    const options = { path };
    const fetch = load(async (value) => {
      assert.equal(value, options);
      return [42];
    });
    assert.deepEqual(await fetch(options), [42]);
  }
});
test('large lookups deduplicate IDs and retain filters in every batch', async () => {
  const ids = Array.from({ length: 120 }, (_, i) => i + 1);
  const calls = [];
  const fetch = load(async (options) => {
    const params = new URL(options.path, 'http://test').searchParams;
    assert.equal(params.get('seriesId'), '42');
    assert.equal(params.get('includeEpisodeFile'), 'true');
    calls.push(params.getAll('episodeIds').map(Number));
    return calls.at(-1);
  });
  const path =
    request([...ids, ...ids]) + '&seriesId=42&includeEpisodeFile=true';
  assert.deepEqual(await fetch({ path }), ids);
  assert.deepEqual(
    calls.map((batch) => batch.length),
    [100, 20]
  );
});
test('batch failure rejects the complete lookup without issuing remaining batches', async () => {
  let count = 0;
  const fetch = load(async () => {
    if (++count === 2) throw new Error('failed');
    return [1];
  });
  await assert.rejects(
    fetch({ path: request(Array.from({ length: 350 }, (_, i) => i)) }),
    /failed/
  );
  assert.equal(count, 2);
});
test('aborted query stops subsequent work', async () => {
  const controller = new AbortController();
  let count = 0;
  const fetch = load(async ({ signal }) => {
    signal.throwIfAborted();
    count++;
    controller.abort();
    return [1];
  });
  await assert.rejects(
    fetch({
      path: request(Array.from({ length: 350 }, (_, i) => i)),
      signal: controller.signal,
    }),
    { name: 'AbortError' }
  );
  assert.equal(count, 1);
});
