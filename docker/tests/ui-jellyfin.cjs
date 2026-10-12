#!/usr/bin/env node
'use strict';

// Playwright and disposable credentials are supplied by the isolated driver.
// No application dependency is added and no settings are saved by this test.
const fs = require('node:fs');
const path = require('node:path');
const assert = require('node:assert/strict');

async function main(input) {
  const target = new URL(input.baseUrl);
  assert.equal(target.hostname, '127.0.0.1');
  assert.equal(target.protocol, 'http:');
  const baseUrl = target.origin;
  const { chromium } = require(input.playwrightModule);
  const checks = [];
  const failures = [];
  const pageErrors = [];
  const forbiddenCloudRequests = [];
  const settingsMutations = [];
  const libraryRequests = [];
  const redact = (value) => {
    let text = String(value);
    for (const secret of [input.apiKey, input.username, input.password]) {
      if (secret) text = text.split(secret).join('[REDACTED]');
    }
    return text;
  };
  if (input.artifactDirectory) fs.mkdirSync(input.artifactDirectory, { recursive: true });
  const browser = await chromium.launch({ headless: true });
  const context = await browser.newContext({ viewport: { width: 1920, height: 1200 } });
  const page = await context.newPage();
  page.setDefaultTimeout(30000);
  const floatingOptions = page.locator('[data-floating-ui-portal]');

  page.on('pageerror', (error) => pageErrors.push(redact(error.message)));
  page.on('request', (request) => {
    const url = new URL(request.url());
    if (url.hostname === 'services.sonarr.tv' || url.hostname === 'sentry.sonarr.tv') {
      forbiddenCloudRequests.push(redact(url.origin + url.pathname));
    }
    if (/^\/api\/v[35]\/(notification|connection)(\/\d+)?\/?$/.test(url.pathname) &&
        ['POST', 'PUT', 'DELETE'].includes(request.method())) {
      settingsMutations.push(request.method() + ' ' + url.pathname);
    }
  });

  function row(label) {
    return page.locator('label').filter({ hasText: new RegExp('^' + label + '$') }).locator('..');
  }

  function isLibraryRequest(request, host) {
    const url = new URL(request.url());
    if (!url.pathname.endsWith('/notification/action/libraries') || request.method() !== 'POST') return false;
    try {
      return request.postDataJSON().fields.some((field) => field.name === 'host' && field.value === host);
    } catch {
      return false;
    }
  }

  function isLibraryResponse(response, host) {
    return isLibraryRequest(response.request(), host);
  }

  async function snapshot() {
    const response = await context.request.get(baseUrl + '/api/v3/notification', {
      headers: { 'X-Api-Key': input.apiKey },
    });
    assert.equal(response.status(), 200, 'Unable to read isolated connection settings');
    const definition = (await response.json()).find((item) => item.name === 'Isolated Jellyfin');
    assert.ok(definition, 'Isolated Jellyfin connection is missing');
    return {
      id: definition.id, name: definition.name, implementation: definition.implementation,
      onDownload: definition.onDownload, onUpgrade: definition.onUpgrade, tags: definition.tags,
      fields: definition.fields.map((field) => ({ name: field.name, value: field.value })),
    };
  }

  async function closeOptions(button) {
    if (await floatingOptions.count()) {
      await button.click();
      await floatingOptions.waitFor({ state: 'detached' });
    }
  }

  async function screenshot(filename) {
    if (input.artifactDirectory) {
      await page.screenshot({
        path: path.join(input.artifactDirectory, filename), fullPage: true,
        style: 'input[name="apiKey"], input[type="password"] { visibility: hidden !important; }',
      });
    }
  }

  async function check(name, run) {
    try {
      await run();
      checks.push(name);
      console.log('PASS ' + name);
    } catch (error) {
      const detail = redact(error.message);
      failures.push({ check: name, error: detail });
      console.log('FAIL ' + name + ': ' + detail);
      await screenshot('ui-jellyfin-failure.png').catch(() => {});
      throw error;
    }
  }

  let original;
  try {
    await check('Jellyfin is available in Settings > Connect with the saved selected library', async () => {
      original = await snapshot();
      assert.equal(original.implementation, 'Jellyfin');
      assert.equal(original.fields.find((field) => field.name === 'host').value, 'jellyfin-test');
      assert.equal(original.fields.find((field) => field.name === 'refreshIntervalMinutes').value, 1);
      assert.equal(String(original.fields.find((field) => field.name === 'libraryId').value).replaceAll('-', '').toLowerCase(),
        String(input.libraryId).replaceAll('-', '').toLowerCase());
      await page.goto(baseUrl + '/settings/connect');
      if (new URL(page.url()).pathname.startsWith('/login')) {
        await page.locator('#sign-in-panel input[name="username"]').fill(input.username);
        await page.locator('#sign-in-panel input[name="password"]').fill(input.password);
        await Promise.all([
          page.waitForURL((url) => !url.pathname.startsWith('/login')),
          page.locator('#sign-in-panel button[type="submit"]').click(),
        ]);
        await page.goto(baseUrl + '/settings/connect');
      }
      const [response] = await Promise.all([
        page.waitForResponse((item) => isLibraryResponse(item, 'jellyfin-test')),
        page.getByRole('button', { name: 'Edit Connection, Isolated Jellyfin', exact: true }).click(),
      ]);
      assert.equal(response.status(), 200);
      const options = (await response.json()).options;
      assert.deepEqual(options.map((option) => option.name).sort(), ['Anime', 'Untouched']);
      libraryRequests.push({ host: 'jellyfin-test', status: response.status(), optionCount: options.length });
      await page.locator('input[name="host"]').waitFor({ state: 'visible' });
      assert.equal(await page.locator('input[name="apiKey"]').inputValue(), '********');
      assert.equal(await page.locator('input[name="refreshIntervalMinutes"]').inputValue(), '1');
      await row('Library').getByRole('button').getByText('Anime', { exact: true }).waitFor({ state: 'visible' });
    });

    await check('Jellyfin offers all three refresh modes and both individual libraries in the editor', async () => {
      const modeButton = row('Refresh Mode').getByRole('button');
      await modeButton.click();
      for (const label of ['Every imported media file', 'At intervals when imports are pending', 'Every import and at intervals']) {
        await floatingOptions.getByText(label, { exact: true }).waitFor({ state: 'visible' });
      }
      await closeOptions(modeButton);
      const libraryButton = row('Library').getByRole('button');
      await libraryButton.click();
      for (const name of ['Anime', 'Untouched']) await floatingOptions.getByText(name, { exact: true }).waitFor({ state: 'visible' });
      await screenshot('ui-jellyfin-settings.png');
      await closeOptions(libraryButton);
    });

    await check('Changing the Jellyfin host clears old library options and restoring it refetches the correct libraries', async () => {
      const host = page.locator('input[name="host"]');
      const libraryButton = row('Library').getByRole('button');
      let releaseRequest;
      const pendingRequest = new Promise((resolve) => { releaseRequest = resolve; });
      const routePattern = '**/notification/action/libraries';
      const holdInvalidHost = async (route) => {
        if (isLibraryRequest(route.request(), 'nonexistent-jellyfin.invalid')) await pendingRequest;
        await route.continue();
      };
      await page.route(routePattern, holdInvalidHost);
      try {
        // Delay dispatch, then continue the real request. This proves old options
        // disappear during loading, without depending on DNS failure timing.
        const [failure] = await Promise.all([
          page.waitForResponse((response) => isLibraryResponse(response, 'nonexistent-jellyfin.invalid')),
          (async () => {
            try {
              await Promise.all([
                page.waitForRequest((request) => isLibraryRequest(request, 'nonexistent-jellyfin.invalid')),
                host.fill('nonexistent-jellyfin.invalid'),
              ]);
              await libraryButton.click();
              for (const name of ['Anime', 'Untouched']) {
                await floatingOptions.getByText(name, { exact: true }).waitFor({ state: 'hidden' });
              }
            } finally {
              releaseRequest();
            }
          })(),
        ]);
        assert.ok(failure.status() >= 400, 'The invalid host unexpectedly succeeded');
        libraryRequests.push({ host: 'nonexistent-jellyfin.invalid', status: failure.status() });
      } finally {
        releaseRequest();
        await page.unroute(routePattern, holdInvalidHost);
      }
      await closeOptions(libraryButton);
      const [restored] = await Promise.all([
        page.waitForResponse((response) => isLibraryResponse(response, 'jellyfin-test')),
        host.fill('jellyfin-test'),
      ]);
      assert.equal(restored.status(), 200);
      const options = (await restored.json()).options;
      assert.deepEqual(options.map((option) => option.name).sort(), ['Anime', 'Untouched']);
      libraryRequests.push({ host: 'jellyfin-test', status: restored.status(), optionCount: options.length });
      await libraryButton.click();
      for (const name of ['Anime', 'Untouched']) await floatingOptions.getByText(name, { exact: true }).waitFor({ state: 'visible' });
      await closeOptions(libraryButton);
      await screenshot('ui-jellyfin-restored-library-options.png');
      await page.getByRole('button', { name: 'Cancel', exact: true }).last().click();
      await host.waitFor({ state: 'hidden' });
    });

    await check('The Jellyfin browser check saves no settings and produces no JavaScript errors or forbidden cloud requests', async () => {
      assert.deepEqual(await snapshot(), original);
      assert.deepEqual(settingsMutations, []);
      assert.deepEqual(pageErrors, []);
      assert.deepEqual(forbiddenCloudRequests, []);
    });
  } catch (error) {
    if (!failures.length) failures.push({ check: 'browser setup', error: redact(error.message) });
  } finally {
    await browser.close();
  }
  const report = {
    passed: failures.length === 0, browser: 'Chromium',
    playwrightVersion: require(path.join(input.playwrightModule, 'package.json')).version,
    checks, failures, libraryRequests, pageErrors, forbiddenCloudRequests, settingsMutations,
    settingsSaved: settingsMutations.length > 0,
    actualJellyfinLibrariesListed: libraryRequests.some((request) => request.optionCount === 2),
  };
  if (input.artifactDirectory) fs.writeFileSync(path.join(input.artifactDirectory, 'ui-jellyfin.json'),
    redact(JSON.stringify(report, null, 2)) + '\n', 'utf8');
  process.exitCode = failures.length ? 1 : 0;
}

let input = '';
process.stdin.setEncoding('utf8');
process.stdin.on('data', (chunk) => { input += chunk; });
process.stdin.on('end', () => main(JSON.parse(input)).catch((error) => {
  console.error('FAIL Jellyfin browser execution: ' + error.name);
  process.exitCode = 1;
}));
