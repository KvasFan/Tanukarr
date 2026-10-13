#!/usr/bin/env node
'use strict';

// External Playwright and generated, disposable Forms credentials arrive only
// on stdin. The caller must create its own empty configuration and image.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

async function main(input) {
  const target = new URL(input.baseUrl);
  assert.equal(target.hostname, '127.0.0.1');
  assert.equal(target.protocol, 'http:');
  assert.equal(input.ownedFixture, true, 'An explicitly owned empty fixture is required');
  assert.match(input.imageId, /^sha256:[0-9a-f]{64}$/);
  const { chromium } = require(input.playwrightModule);
  const browser = await chromium.launch({ headless: true });
  const context = await browser.newContext({ viewport: { width: 1920, height: 1200 } });
  const page = await context.newPage();
  page.setDefaultTimeout(30000);
  const checks = [];
  const failures = [];
  const pageErrors = [];
  const outsideRequests = [];
  const acquisitionRequests = [];
  const qualityWrites = [];
  const observations = {};
  const clone = (value) => JSON.parse(JSON.stringify(value));
  const fields = ['minSize', 'preferredSize', 'maxSize'];
  const redact = (value) => {
    let text = String(value);
    for (const secret of [input.username, input.password, input.apiKey]) {
      if (secret) text = text.split(secret).join('[REDACTED]');
    }
    return text;
  };
  const screenshot = async (name) => {
    if (input.artifactDirectory) await page.screenshot({
      path: path.join(input.artifactDirectory, name + '.png'), fullPage: true,
      style: 'input[type="password"], #sign-in-panel input { visibility: hidden !important; }',
    });
  };
  const leaves = (items) => items.flatMap((item) => item.quality ? [item] : leaves(item.items ?? []));
  const ordered = (profiles) => [...profiles].sort((left, right) => left.id - right.id);
  let definition;
  let qualityId;
  let profileId;
  const selectedLeaf = (profile) => {
    const found = leaves(profile.items).find((item) => item.quality.id === qualityId);
    assert.ok(found, 'The selected quality is absent from an owned profile');
    return found;
  };
  const expectedProfiles = (before, updates, selectedProfileId) => clone(before).map((profile) => {
    if (selectedProfileId == null || profile.id === selectedProfileId) Object.assign(selectedLeaf(profile), updates);
    return profile;
  });
  const api = async (endpoint, method = 'GET', data) => {
    const response = await context.request.fetch(input.baseUrl + endpoint, {
      method, headers: { 'X-Api-Key': input.apiKey }, ...(data === undefined ? {} : { data }),
    });
    assert.ok(response.status() >= 200 && response.status() < 300,
      method + ' ' + endpoint.split('?')[0] + ' returned HTTP ' + response.status());
    const body = await response.text();
    return body ? JSON.parse(body) : null;
  };
  const profiles = async () => {
    const stored = await api('/api/v5/qualityprofile');
    // Inherited MVC serialization omits nullable leaf limits on reads. Treat
    // only absent size properties as null; preserve numbers and every other
    // field so full-profile comparisons still detect unintended changes.
    for (const profile of stored) {
      for (const leaf of leaves(profile.items)) {
        for (const field of fields) {
          if (!Object.hasOwn(leaf, field)) leaf[field] = null;
        }
      }
    }
    return stored;
  };
  const definitions = () => api('/api/v5/qualitydefinition');
  const globalInput = (field) => page.locator('input[name="' + definition.id + '.' + field + '"]');
  const globalRow = () => globalInput('minSize').locator(
    'xpath=ancestor::div[.//input[contains(@name,".minSize")] and .//input[contains(@name,".maxSize")]][1]');
  const globalSave = () => page.getByRole('button', { name: 'Save Changes', exact: true });
  const noChanges = () => page.getByRole('button', { name: 'No Changes', exact: true });
  const expectNumber = async (locator, expected) => {
    assert.equal(await locator.getAttribute('type'), 'number');
    assert.ok(Math.abs(Number(await locator.inputValue()) - expected) < 1e-8,
      'Hourly number differs from the stored minute value');
  };
  const waitGlobalSave = async (status = 200) => {
    const pending = page.waitForResponse((response) =>
      /^\/api\/v[35]\/qualitydefinition\/?$/.test(new URL(response.url()).pathname)
      && response.request().method() === 'PUT');
    await globalSave().click();
    const response = await pending;
    assert.equal(response.status(), status);
    const rows = response.request().postDataJSON();
    assert.equal(rows.length, 1, 'Global UI submitted unchanged definitions');
    assert.equal(rows[0].id, definition.id);
    return rows[0];
  };
  const loadQuality = async () => {
    await page.goto(input.baseUrl + '/settings/quality');
    await globalInput('minSize').waitFor({ state: 'visible' });
    await noChanges().waitFor({ state: 'visible' });
    assert.equal(await noChanges().isDisabled(), true);
  };
  const check = async (name, run) => {
    try {
      await run();
      checks.push(name);
      console.log('PASS ' + name);
    } catch (error) {
      failures.push({ check: name, error: redact(error.message) });
      console.log('FAIL ' + name + ': ' + redact(error.message));
      await screenshot('quality-failure-' + failures.length).catch(() => {});
      if (input.artifactDirectory) fs.writeFileSync(
        path.join(input.artifactDirectory, 'quality-failure-' + failures.length + '.txt'),
        redact(await page.locator('body').innerText().catch(() => '')), 'utf8');
      throw error;
    }
  };
  page.on('pageerror', (error) => pageErrors.push(redact(error.message)));
  await context.route('**/*', async (route) => {
    const url = new URL(route.request().url());
    if (['http:', 'https:'].includes(url.protocol) && url.origin !== target.origin) {
      outsideRequests.push({ origin: url.origin, pathname: url.pathname });
      await route.abort();
    } else {
      await route.continue();
    }
  });
  page.on('request', (request) => {
    const url = new URL(request.url());
    if (request.method() === 'PUT' && /^\/api\/v[35]\/qualitydefinition\/?$/.test(url.pathname)) {
      qualityWrites.push({ path: url.pathname, payload: request.postDataJSON() });
    }
    if (request.method() !== 'GET' && /\/api\/v[35]\/(command|release|indexer|downloadclient)(\/|$)/.test(url.pathname)) {
      acquisitionRequests.push({ method: request.method(), pathname: url.pathname });
    }
  });

  try {
    await check('owned Forms login loads the packaged application without anonymous library access', async () => {
      await page.goto(input.baseUrl + '/settings/quality');
      await page.waitForURL('**/login**');
      await page.locator('#sign-in-panel input[name="username"]').fill(input.username);
      await page.locator('#sign-in-panel input[name="password"]').fill(input.password);
      await Promise.all([
        page.waitForURL((url) => !url.pathname.startsWith('/login')),
        page.locator('#sign-in-panel button[type="submit"]').click(),
      ]);
      await page.locator('#root').waitFor({ state: 'visible' });
      const runtime = await api('/api/v5/system/status');
      assert.equal(runtime.productName, 'Tanukarr');
      assert.equal(runtime.productVersion, '1.0.3');
      observations.runtime = Object.fromEntries(['productName', 'productVersion', 'version', 'runtimeVersion', 'migrationVersion']
        .map((key) => [key, runtime[key]]));
      assert.deepEqual(await api('/api/v5/series'), []);
      assert.deepEqual(await api('/api/v5/importlist'), []);
    });
    await check('global numeric limits use actual hourly values with GiB per hour previews and stated profile scope', async () => {
      const originals = await profiles();
      assert.ok(originals.length >= 2, 'At least two owned default profiles are required');
      const candidates = (await definitions()).filter((row) =>
        originals.every((profile) => leaves(profile.items).some((item) => item.quality.id === row.quality.id)));
      definition = candidates.find((row) => /1080p/i.test(row.quality.name)) ?? candidates[0];
      assert.ok(definition, 'No shared quality definition was returned');
      qualityId = definition.quality.id;
      profileId = originals[0].id;
      for (const profile of expectedProfiles(originals, { minSize: 1.25, preferredSize: 10.125, maxSize: 20.875 })) {
        await api('/api/v5/qualityprofile/' + profile.id, 'PUT', profile);
      }
      await loadQuality();
      for (const [field, raw] of [['minSize', 1.25], ['preferredSize', 10.125], ['maxSize', 20.875]]) {
        await expectNumber(globalInput(field), raw * 60);
        await globalRow().getByText(((raw * 60) / 1024).toFixed(2) + ' GiB/h', { exact: true }).waitFor({ state: 'visible' });
      }
      assert.ok((await globalRow().innerText()).includes('MiB/h'));
      await page.getByText('Values describe a one-hour video. Saving a size limit applies only that edited field to all quality profiles; other limits keep their existing values.',
        { exact: true }).waitFor({ state: 'visible' });
      observations.selectedQuality = { definitionId: definition.id, qualityId, name: definition.quality.name, profileCount: originals.length };
      await screenshot('quality-hourly-initial');
    });
    await check('packaged codec table shows the four requested factors from the same v3 and v5 catalog', async () => {
      const catalog = await api('/api/v5/qualitydefinition/codecs');
      assert.deepEqual(await api('/api/v3/qualitydefinition/codecs'), catalog);
      for (const [name, factor] of [['MPEG-2 / H.262 / x262', .5], ['H.264 / AVC / x264', 1],
        ['H.265 / HEVC / x265', 1.67], ['AV1', 2.22]]) {
        assert.equal(catalog[name], factor);
        const row = page.getByRole('row').filter({ has: page.getByRole('cell', { name, exact: true }) });
        await row.waitFor({ state: 'visible' });
        assert.deepEqual(await row.getByRole('cell').allTextContents(), [name, factor.toFixed(2), factor.toFixed(2) + ' GiB/h']);
      }
      assert.equal(Object.keys(catalog).length, 15);
      observations.codecCatalog = catalog;
    });
    await check('global 120 MiB per hour saves as 2 MiB per minute in every profile without changing other fields', async () => {
      const before = await profiles();
      await globalInput('minSize').fill('120');
      const sent = await waitGlobalSave();
      assert.equal(sent.minSize, 2);
      assert.equal(Object.hasOwn(sent, 'preferredSize'), false);
      assert.equal(Object.hasOwn(sent, 'maxSize'), false);
      assert.deepEqual(ordered(await profiles()), ordered(expectedProfiles(before, { minSize: 2 })));
      await noChanges().waitFor({ state: 'visible' });
      observations.globalMinimumPayload = sent;
    });
    await check('reload retains 120 hourly units while the saved API value remains 2 minute units', async () => {
      await loadQuality();
      await expectNumber(globalInput('minSize'), 120);
      const stored = (await definitions()).find((row) => row.id === definition.id);
      assert.equal(stored.minSize, 2);
      assert.equal(stored.minSizeIsMixed, false);
    });
    await check('real profile differences render Mixed and focus or blur makes no changes or global PUT', async () => {
      const owned = (await profiles()).find((profile) => profile.id === profileId);
      Object.assign(selectedLeaf(owned), { minSize: 2.5, preferredSize: 4.275, maxSize: 8.75 });
      await api('/api/v5/qualityprofile/' + profileId, 'PUT', owned);
      const before = await profiles();
      const writesBefore = qualityWrites.length;
      await loadQuality();
      for (const field of fields) {
        assert.equal(await globalInput(field).inputValue(), '');
        assert.equal(await globalInput(field).getAttribute('placeholder'), 'Mixed');
        await globalInput(field).focus();
        await globalInput(field).blur();
      }
      await page.waitForTimeout(250);
      assert.equal(await noChanges().isDisabled(), true);
      assert.equal(qualityWrites.length, writesBefore);
      assert.deepEqual(ordered(await profiles()), ordered(before));
      await screenshot('quality-mixed');
    });
    await check('explicit Unlimited clears only the selected mixed maximum or preferred field', async () => {
      for (const field of ['maxSize', 'preferredSize']) {
        const before = await profiles();
        const cell = globalInput(field).locator('xpath=ancestor::div[.//button[normalize-space()="Unlimited"]][1]');
        await cell.getByRole('button', { name: 'Unlimited', exact: true }).click();
        const sent = await waitGlobalSave();
        assert.equal(sent[field], null);
        for (const other of fields.filter((value) => value !== field)) assert.equal(Object.hasOwn(sent, other), false);
        assert.deepEqual(ordered(await profiles()), ordered(expectedProfiles(before, { [field]: null })));
        await noChanges().waitFor({ state: 'visible' });
      }
      await loadQuality();
      for (const field of ['preferredSize', 'maxSize']) {
        assert.equal(await globalInput(field).inputValue(), '');
        assert.equal(await globalInput(field).getAttribute('placeholder'), 'Unlimited');
      }
    });
    await check('invalid merged profile limits show a save error, preserve edits and stored data, then permit a valid retry', async () => {
      const owned = (await profiles()).find((profile) => profile.id === profileId);
      Object.assign(selectedLeaf(owned), { preferredSize: 4.275, maxSize: 8.75 });
      await api('/api/v5/qualityprofile/' + profileId, 'PUT', owned);
      await loadQuality();
      const before = await profiles();
      const definitionsBefore = await definitions();
      await globalRow().locator('input[type="text"]').fill('Owned browser quality label');
      await globalInput('minSize').fill('1200');
      const rejected = await waitGlobalSave(400);
      assert.equal(rejected.minSize, 20);
      await page.getByText('Unable to save quality definitions. Check the size limits and try again; your edits are retained.',
        { exact: true }).waitFor({ state: 'visible' });
      await expectNumber(globalInput('minSize'), 1200);
      assert.equal(await globalRow().locator('input[type="text"]').inputValue(), 'Owned browser quality label');
      assert.equal(await globalSave().isDisabled(), false);
      assert.deepEqual(ordered(await profiles()), ordered(before));
      assert.deepEqual(await definitions(), definitionsBefore);
      await screenshot('quality-save-error-retained');
      await globalInput('minSize').fill('180');
      const retry = await waitGlobalSave();
      assert.equal(retry.minSize, 3);
      assert.deepEqual(ordered(await profiles()), ordered(expectedProfiles(before, { minSize: 3 })));
      await noChanges().waitFor({ state: 'visible' });
      assert.equal(await page.getByText('Unable to save quality definitions. Check the size limits and try again; your edits are retained.',
        { exact: true }).count(), 0);
      const saved = (await definitions()).find((row) => row.id === definition.id);
      assert.equal(saved.title, 'Owned browser quality label');
      observations.failedSave = { httpStatus: 400, rawMinimum: rejected.minSize, validRetryMinimum: retry.minSize, retainedBeforeRetry: true };
    });
    await check('profile manual-hour editing preserves exact other limits despite nonlinear slider positions', async () => {
      const owned = (await profiles()).find((profile) => profile.id === profileId);
      Object.assign(selectedLeaf(owned), { minSize: 1.125, preferredSize: 12.345, maxSize: 23.456 });
      await api('/api/v5/qualityprofile/' + profileId, 'PUT', owned);
      const before = await profiles();
      await page.goto(input.baseUrl + '/settings/profiles');
      await page.getByRole('button', { name: 'Edit Quality Profile, ' + owned.name, exact: true }).click();
      const modal = page.getByRole('dialog', { name: 'Edit Quality Profile', exact: true });
      await modal.getByRole('button', { name: 'Sizes', exact: true }).click();
      const minimum = modal.locator('input[name="' + qualityId + '.min"]');
      await expectNumber(minimum, 67.5);
      await expectNumber(modal.locator('input[name="' + qualityId + '.preferred"]'), 740.7);
      await expectNumber(modal.locator('input[name="' + qualityId + '.max"]'), 1407.36);
      const ancestorAttributes = () => minimum.evaluate((element) => {
        const result = [];
        for (let node = element; node; node = node.parentElement) {
          result.push({ tag: node.tagName.toLowerCase(), role: node.getAttribute('role'),
            'aria-disabled': node.getAttribute('aria-disabled'), disabled: node.getAttribute('disabled'),
            tabindex: node.getAttribute('tabindex') });
        }
        return result;
      });
      observations.profileMinimumAncestorsBeforeFill = await ancestorAttributes();
      try {
        await minimum.fill('90');
      } catch (error) {
        observations.profileMinimumAncestorsAfterFailure = await ancestorAttributes();
        throw error;
      }
      await minimum.blur();
      await screenshot('quality-profile-hourly');
      const pending = page.waitForResponse((response) =>
        new URL(response.url()).pathname === '/api/v5/qualityprofile/' + profileId
        && response.request().method() === 'PUT');
      await modal.getByRole('button', { name: 'Save', exact: true }).click();
      const response = await pending;
      assert.ok([200, 202].includes(response.status()));
      await modal.waitFor({ state: 'hidden' });
      assert.deepEqual(ordered(await profiles()), ordered(expectedProfiles(before, { minSize: 1.5 }, profileId)));
      observations.manualProfileEdit = { profileId, rawMinimum: 1.5, hourlyMinimum: 90, rawPreferredUnchanged: 12.345, rawMaximumUnchanged: 23.456 };
    });
    await check('quality editing leaves library and acquisition empty with no browser exceptions or outside requests', async () => {
      assert.deepEqual(pageErrors, []);
      assert.deepEqual(outsideRequests, []);
      assert.deepEqual(acquisitionRequests, []);
      for (const endpoint of ['series', 'importlist', 'indexer', 'downloadclient']) assert.deepEqual(await api('/api/v5/' + endpoint), []);
      assert.deepEqual((await api('/api/v5/queue')).records, []);
      await screenshot('quality-browser-final');
    });
  } catch (error) {
    if (failures.length === 0) failures.push({ check: 'browser execution', error: redact(error.message) });
  } finally {
    await browser.close();
    const report = {
      createdUtc: new Date().toISOString(), passed: failures.length === 0,
      imageId: input.imageId, runtime: observations.runtime, browser: 'Chromium',
      playwrightVersion: require(path.join(input.playwrightModule, 'package.json')).version,
      checks, failures, pageErrors, outsideRequests, acquisitionRequests, qualityWrites, observations,
      generatedFormsCredentialsOnly: true, privateOwnerConfigurationAccessed: false,
      realLibraryTested: false, liveIndexerTested: false, downloadsTested: false,
    };
    if (input.artifactDirectory) fs.writeFileSync(path.join(input.artifactDirectory, 'quality-ui.json'),
      redact(JSON.stringify(report, null, 2)) + '\n', 'utf8');
    process.exitCode = failures.length ? 1 : 0;
  }
}

let input = '';
process.stdin.setEncoding('utf8');
process.stdin.on('data', (chunk) => { input += chunk; });
process.stdin.on('end', () => main(JSON.parse(input)).catch((error) => {
  console.error('FAIL browser execution: ' + error.name);
  process.exitCode = 1;
}));
