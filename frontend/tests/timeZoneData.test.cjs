const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const test = require('node:test');
const vm = require('node:vm');
const ts = require('typescript');
const moment = require('moment-timezone/moment-timezone');
const data = require('moment-timezone/data/packed/latest.json');

function load(fetch) {
  const filename = path.resolve(__dirname, '../src/Language/loadTimeZoneData.ts');
  const code = ts.transpileModule(fs.readFileSync(filename, 'utf8'), {
    compilerOptions: { module: ts.ModuleKind.CommonJS, esModuleInterop: true },
  }).outputText;
  const module = { exports: {} };
  vm.runInNewContext(code, {
    exports: module.exports,
    module,
    fetch,
    require: (name) => {
      if (name === 'moment-timezone') return moment;
      if (name.endsWith('latest.json?url')) return '/assets/time-zones.json';
      throw new Error('Unexpected dependency: ' + name);
    },
  }, { filename });
  return module.exports.default;
}

test('bootstrap loads the complete installed time-zone database, including historical offsets and aliases', async () => {
  const requests = [];
  await load(async (url) => {
    requests.push(url);
    return { ok: true, json: async () => data };
  })();
  assert.deepEqual(requests, ['/assets/time-zones.json']);
  assert.equal(moment.tz.dataVersion, data.version);
  for (const packed of data.zones) assert.ok(moment.tz.zone(packed.split('|')[0]));
  for (const linked of data.links) {
    for (const name of linked.split('|')) assert.ok(moment.tz.zone(name));
  }
  assert.equal(moment.tz('1945-08-14 12:00', 'America/New_York').format('Z'), '-04:00');
  assert.equal(moment.tz('2026-01-15 12:00', 'Europe/Rome').format('Z'), '+01:00');
  assert.equal(moment.tz('2026-07-15 12:00', 'Europe/Rome').format('Z'), '+02:00');
  assert.equal(moment.tz('2026-07-15 12:00', 'US/Eastern').format('Z'), '-04:00');
});

test('a failed time-zone asset request prevents bootstrap from silently formatting dates without its data', async () => {
  let decoded = false;
  await assert.rejects(load(async () => ({ ok: false, json: async () => { decoded = true; return data; } }))(), /Unable to load time zone definitions/);
  assert.equal(decoded, false);
});
