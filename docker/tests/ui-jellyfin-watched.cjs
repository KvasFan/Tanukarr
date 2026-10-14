#!/usr/bin/env node
'use strict';

// Credentials arrive on stdin and belong exclusively to disposable fixtures.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

async function main(input) {
  assert.equal(input.ownedFixture, true);
  assert.match(input.imageId, /^sha256:[a-f0-9]{64}$/);
  const target = new URL(input.baseUrl);
  const jellyfin = new URL(input.jellyfinBaseUrl);
  for (const url of [target, jellyfin]) {
    assert.equal(url.hostname, '127.0.0.1');
    assert.equal(url.protocol, 'http:');
  }
  const { chromium } = require(input.playwrightModule);
  const browser = await chromium.launch({ headless: true });
  const context = await browser.newContext();
  const page = await context.newPage();
  page.setDefaultTimeout(30000);
  const checks = [];
  const errors = [];
  const outside = [];
  const commands = [];
  const observations = [];
  let failure = null;
  const redact = (value) => {
    let result = String(value);
    for (const secret of [input.apiKey, input.password, input.jellyfinToken]) {
      if (secret) result = result.split(secret).join('[REDACTED]');
    }
    return result;
  };
  page.on('pageerror', (error) => errors.push(redact(error.message)));
  page.on('request', (request) => {
    const url = new URL(request.url());
    if (url.origin !== target.origin) outside.push(url.origin + url.pathname);
  });
  async function api(endpoint, method = 'GET', data) {
    const response = await context.request.fetch(input.baseUrl + '/api/v5' + endpoint, {
      method, headers: { 'X-Api-Key': input.apiKey }, data,
    });
    assert.ok(response.ok(), 'Disposable Tanukarr API request failed');
    return response.json();
  }
  async function changePlayed(played) {
    const response = await context.request.fetch(input.jellyfinBaseUrl + '/Users/' +
      encodeURIComponent(input.selectedUserId) + '/PlayedItems/' + encodeURIComponent(input.jellyfinItemId), {
      method: played ? 'POST' : 'DELETE', headers: {
        Authorization: 'MediaBrowser Client="Tanukarr integration test", Device="Generated fixture", ' +
          'DeviceId="tanukarr-isolated-audit", Version="1.0.4", Token="' + input.jellyfinToken + '"',
      },
    });
    assert.ok(response.ok(), 'Disposable Jellyfin played mutation failed (HTTP ' + response.status() + ')');
    const command = await api('/command', 'POST', { name: 'SyncJellyfinWatched' });
    commands.push(command.name);
    const deadline = Date.now() + 30000;
    while (Date.now() < deadline) {
      const current = await api('/command/' + command.id);
      if (current.status === 'completed') return;
      assert.notEqual(current.status, 'failed', 'Watched command failed');
      await page.waitForTimeout(100);
    }
    assert.fail('Watched command timed out');
  }
  async function observe(count) {
    const label = page.getByText(count + '/' + input.totalEpisodes + ' watched', { exact: true });
    await label.waitFor({ state: 'visible' });
    const episodes = await api('/episode?seriesId=' + input.seriesId);
    assert.equal(episodes.filter((episode) => episode.watched).length, count);
    observations.push({ watchedCount: count, renderedLabel: await label.textContent() });
  }
  try {
    const serverResponse = await context.request.get(input.jellyfinBaseUrl + '/System/Info/Public');
    assert.equal(serverResponse.status(), 200, 'Owned Jellyfin public identity is unreachable');
    const server = await serverResponse.json();
    assert.equal(server.ServerName, 'Isolated Jellyfin audit');
    assert.match(server.Version, /^12\.2(?:\.|$)/);
    await page.goto(input.baseUrl + '/series/' + encodeURIComponent(input.titleSlug));
    await page.waitForURL('**/login**');
    await page.locator('#sign-in-panel input[name="username"]').fill(input.username);
    await page.locator('#sign-in-panel input[name="password"]').fill(input.password);
    await Promise.all([
      page.waitForURL((url) => !url.pathname.startsWith('/login')),
      page.locator('#sign-in-panel button[type="submit"]').click(),
    ]);
    await page.goto(input.baseUrl + '/series/' + encodeURIComponent(input.titleSlug));
    await observe(input.expectedWatched);
    checks.push('actual packaged series page renders the persisted Jellyfin watched count after Forms login');
    const navigation = page.url();
    await changePlayed(false);
    await observe(input.expectedWatched - 1);
    assert.equal(page.url(), navigation);
    checks.push('real local Jellyfin unwatch updates the season count through SignalR without page reload');
    await changePlayed(true);
    await observe(input.expectedWatched);
    assert.equal(page.url(), navigation);
    checks.push('real local Jellyfin watch increments the displayed season count through SignalR without page reload');
    assert.deepEqual(commands, ['SyncJellyfinWatched', 'SyncJellyfinWatched']);
    assert.deepEqual(errors, []);
    assert.deepEqual(outside, []);
    assert.equal((await api('/downloadclient')).length, 0);
    assert.equal((await api('/queue')).totalRecords, 0);
    checks.push('watched UI has no JavaScript failures or external page requests and submits only owned watch commands');
    await page.screenshot({ path: path.join(input.artifactDirectory, 'jellyfin-watched-ui.png'), fullPage: true });
  } catch (error) {
    failure = redact(error.stack || error.message);
    await page.screenshot({ path: path.join(input.artifactDirectory, 'jellyfin-watched-ui-failure.png'), fullPage: true }).catch(() => {});
  } finally {
    await browser.close();
    const report = { passed: failure === null, imageId: input.imageId, checks, observations,
      javascriptErrors: errors, outsideRequests: outside, commands, failure, ownedFixture: true };
    fs.writeFileSync(path.join(input.artifactDirectory, 'jellyfin-watched-ui.json'), redact(JSON.stringify(report, null, 2)) + '\n');
    process.stdout.write(JSON.stringify({ passed: report.passed, checks: checks.length, failure }) + '\n');
  }
  return failure ? 1 : 0;
}

let input = '';
process.stdin.setEncoding('utf8');
process.stdin.on('data', (chunk) => { input += chunk; });
process.stdin.on('end', () => {
  main(JSON.parse(input)).then((code) => { process.exitCode = code; }).catch(() => {
    process.stderr.write('Owned Jellyfin watched browser fixture could not start.\n');
    process.exitCode = 1;
  });
});
