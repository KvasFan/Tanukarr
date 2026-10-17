#!/usr/bin/env node
'use strict';

// Playwright is supplied by external tooling. This does not add an application
// dependency. Input containing disposable credentials is read only from stdin.
const fs = require('node:fs');
const path = require('node:path');
const assert = require('node:assert/strict');

async function main(input) {
  const { chromium } = require(input.playwrightModule);
  const target = new URL(input.baseUrl);
  assert.equal(target.hostname, '127.0.0.1');
  assert.equal(target.protocol, 'http:');
  const results = [];
  const failures = [];
  const pageErrors = [];
  const cloudRequests = [];
  const browser = await chromium.launch({ headless: true });
  const context = await browser.newContext({ viewport: { width: 1920, height: 1200 } });
  const page = await context.newPage();
  page.setDefaultTimeout(30000);
  const redact = (value) => {
    let text = String(value);
    for (const secret of [input.username, input.password, input.apiKey]) {
      if (secret) text = text.split(secret).join('[REDACTED]');
    }
    return text;
  };
  page.on('pageerror', (error) => pageErrors.push(redact(error.message)));
  page.on('request', (request) => {
    const hostname = new URL(request.url()).hostname;
    if (hostname === 'services.sonarr.tv' || hostname === 'sentry.sonarr.tv') {
      cloudRequests.push(redact(request.url()));
    }
  });
  async function check(name, run) {
    try {
      await run();
      results.push(name);
      console.log('PASS ' + name);
    } catch (error) {
      const detail = redact(error.message);
      failures.push({ check: name, error: detail });
      console.log('FAIL ' + name + ': ' + detail);
      if (input.artifactDirectory) {
        await page.screenshot({ path: path.join(input.artifactDirectory, 'ui-failure-' + failures.length + '.png'), fullPage: true }).catch(() => {});
        fs.writeFileSync(path.join(input.artifactDirectory, 'ui-failure-' + failures.length + '.txt'),
          redact(await page.locator('body').innerText().catch(() => '')), 'utf8');
      }
    }
  }
  try {
    await check('browser anonymous UI redirects to login and valid Forms login loads the application', async () => {
      await page.goto(input.baseUrl + '/');
      await page.waitForURL('**/login**');
      await page.locator('#sign-in-panel input[name="username"]').fill(input.username);
      await page.locator('#sign-in-panel input[name="password"]').fill(input.password);
      await Promise.all([
        page.waitForURL((url) => !url.pathname.startsWith('/login')),
        page.locator('#sign-in-panel button[type="submit"]').click(),
      ]);
      await page.locator('#root').waitFor({ state: 'visible' });
      await page.getByRole('link', { name: 'Series', exact: true }).first().waitFor({ state: 'visible' });
    });
    if (input.seriesId && !input.menuOnly) {
      await check('native AniList series detail renders and complete-series modal alone issues its scoped search', async () => {
        let completeSearches = 0;
        const listener = (request) => {
          const url = new URL(request.url());
          if (url.pathname === '/api/v5/release' && url.searchParams.get('completeSeries') === 'true') completeSearches += 1;
        };
        page.on('request', listener);
        try {
          await page.goto(input.baseUrl + '/series/anilist-1');
          await page.getByRole('button', { name: 'Search Complete Series', exact: true }).waitFor({ state: 'visible' });
          assert.equal(completeSearches, 0, 'Closed search modal caused a release request');
          const releaseResponse = page.waitForResponse((response) => {
            const url = new URL(response.url());
            return url.pathname === '/api/v5/release' && url.searchParams.get('completeSeries') === 'true';
          }, { timeout: 90000 });
          await page.getByRole('button', { name: 'Search Complete Series', exact: true }).click();
          const response = await releaseResponse;
          assert.equal(response.status(), 200);
          const url = new URL(response.url());
          assert.equal(Number(url.searchParams.get('seriesId')), input.seriesId);
          assert.equal(url.searchParams.has('episodeId'), false);
          assert.equal(url.searchParams.has('seasonNumber'), false);
          await page.getByText('No results found', { exact: true }).waitFor({ state: 'visible' });
          await page.getByRole('button', { name: 'Close', exact: true }).last().click();
          assert.equal(completeSearches, 1, 'Modal issued duplicate complete-series requests');
          if (input.artifactDirectory) await page.screenshot({ path: path.join(input.artifactDirectory, 'native-anilist-series.png'), fullPage: true });
        } finally {
          page.off('request', listener);
        }
      });
    }
    if (!input.menuOnly) {
      await check('Add Series separates all enabled catalogues, TheTVDB, TMDB, AniList, MyAnimeList, TVmaze, TMDB Movie and OMDb, and performs native AniList lookup', async () => {
        await page.goto(input.baseUrl + '/add/new');
        const provider = page.locator('select[name="metadataSource"]');
        await provider.waitFor({ state: 'visible' });
        // A fresh configuration enables every catalogue in the default order behind the
        // combined "all" entry; a single source stays selectable on its own.
        assert.deepEqual(await provider.locator('option').evaluateAll((options) => options.map((option) => option.value)),
          ['all', 'tvdb', 'tmdb', 'aniList', 'myAnimeList', 'tvMaze', 'tmdbMovie', 'omdb']);
        await provider.selectOption('aniList');
        const responsePromise = page.waitForResponse((response) => {
          const url = new URL(response.url());
          return url.pathname === '/api/v5/series/lookup' && url.searchParams.get('metadataSource') === 'aniList';
        }, { timeout: 90000 });
        await page.locator('input[name="seriesLookup"]').fill('Cowboy Bebop');
        const response = await responsePromise;
        assert.equal(response.status(), 200);
        const found = await response.json();
        assert.ok(found.some((series) => series.metadataSource === 'aniList' && String(series.foreignId) === '1'));
        await page.getByRole('link', { name: 'Cowboy Bebop', exact: true }).first().waitFor({ state: 'visible' });
        await page.getByText('Metadata is provided by AniList', { exact: true }).first().waitFor({ state: 'visible' });
        assert.ok(await page.locator('a[href="https://anilist.co/anime/1"]').count());
        assert.equal(await page.locator('a[href*="thetvdb.com"][href*="id=-"]').count(), 0);
      });
    }
    await check('AniList import-list editor distinguishes username/custom list, user-list status and media-release status', async () => {
      await page.goto(input.baseUrl + '/settings/importlists');
      await page.getByRole('button', { name: 'Add Import List', exact: true }).waitFor({ state: 'visible' });
      const advancedButton = page.getByRole('button', { name: 'Show Advanced', exact: true });
      if (await advancedButton.count()) await advancedButton.click();
      await page.getByRole('button', { name: 'Add Import List', exact: true }).click();
      await page.getByRole('button', { name: 'Add Import List - AniList List', exact: true }).click();
      await page.locator('input[name="username"]').waitFor({ state: 'visible' });
      assert.equal(await page.locator('input[name="customList"]').count(), 0, 'Obsolete single-list textbox is still displayed');
      for (const name of ['importCurrent', 'importPlanning', 'importCompleted', 'importDropped', 'importPaused', 'importRepeating',
        'importFinished', 'importReleasing', 'importUnreleased', 'importCancelled', 'importHiatus']) {
        assert.equal(await page.locator('input[name="' + name + '"]').count(), 1, 'Missing distinct field ' + name);
      }
      await page.getByText('List: Currently Watching', { exact: true }).waitFor({ state: 'visible' });
      await page.getByText('Media: All episodes have aired', { exact: true }).waitFor({ state: 'visible' });
      assert.equal(await page.locator('input[name="importCurrent"]').isChecked(), true);
      assert.equal(await page.locator('input[name="importFinished"]').isChecked(), true);
      await page.locator('input[name="username"]').fill('isolated-user');
      assert.equal(await page.locator('input[name="username"]').inputValue(), 'isolated-user');
      if (input.artifactDirectory) await page.screenshot({ path: path.join(input.artifactDirectory, 'anilist-list-status-fields.png'), fullPage: true });
      await page.getByRole('button', { name: 'Cancel', exact: true }).last().click();
    });
    await check('AniList custom-list help states excluded lists remove entries from included lists', async () => {
      await page.getByRole('button', { name: 'Add Import List', exact: true }).click();
      await page.getByRole('button', { name: 'Add Import List - AniList List', exact: true }).click();
      await page.locator('input[name="includedCustomLists"]').waitFor({ state: 'visible' });
      // The visible help comes from ImportListsAniListSettingsIncludedCustomListsHelpText
      // and ImportListsAniListSettingsExcludedCustomListsHelpText in en.json.
      await page.getByText('Enter each AniList custom list name and press Enter. Select entries in any named list within the selected list and media statuses, then remove entries in Excluded Custom Lists. Leave empty to use the status filters and exclusions', { exact: true }).waitFor({ state: 'visible' });
      await page.getByText('Enter each custom list name and press Enter', { exact: true }).waitFor({ state: 'visible' });
      await page.getByRole('button', { name: 'Cancel', exact: true }).last().click();
    });
    await check('AniList custom-list exclusions preserve literal names while obsolete status exclusions are absent', async () => {
      const savedBefore = await context.request.get(input.baseUrl + '/api/v5/importlist', {
        headers: { 'X-Api-Key': input.apiKey },
      });
      assert.equal(savedBefore.status(), 200);
      const originalLists = await savedBefore.json();
      await page.getByRole('button', { name: 'Add Import List', exact: true }).click();
      await page.getByRole('button', { name: 'Add Import List - AniList List', exact: true }).click();
      const included = page.locator('input[name="includedCustomLists"]');
      const excluded = page.locator('input[name="excludedCustomLists"]');
      await included.waitFor({ state: 'visible' });
      await excluded.waitFor({ state: 'visible' });
      const obsoleteStatusExclusions = ['excludeCurrent', 'excludePlanning', 'excludeCompleted', 'excludeDropped', 'excludePaused', 'excludeRepeating'];
      for (const name of obsoleteStatusExclusions) {
        assert.equal(await page.locator('input[name="' + name + '"]').count(), 0, 'Removed status exclusion is still displayed: ' + name);
      }
      assert.equal(await page.getByText('Always exclude these list statuses', { exact: true }).count(), 0);
      const selectedName = 'Seasonal, favorites';
      const excludedName = 'Do not download, duplicates';
      await included.fill(selectedName);
      await included.press('Enter');
      await page.getByText(selectedName, { exact: true }).waitFor({ state: 'visible' });
      await page.waitForTimeout(300); // TagInput debounces repeated additions for 250 ms.
      await included.fill('Watching later');
      await included.press('Enter');
      await excluded.fill(excludedName);
      await excluded.press('Enter');
      await page.getByText(excludedName, { exact: true }).waitFor({ state: 'visible' });
      await page.waitForTimeout(300);
      await excluded.fill(selectedName);
      await excluded.press('Enter');
      assert.equal(await page.getByText(selectedName, { exact: true }).count(), 2);
      assert.equal(await page.getByText(excludedName, { exact: true }).count(), 1);
      assert.equal(await page.locator('input[name="importCurrent"]').isChecked(), true);
      await page.locator('input[name="username"]').fill('isolated-user');
      const testResponse = page.waitForResponse((response) =>
        /\/api\/v[35]\/importlist\/test\/?$/.test(new URL(response.url()).pathname) && response.request().method() === 'POST');
      await page.getByRole('button', { name: 'Test', exact: true }).last().click();
      const response = await testResponse;
      // Missing OAuth credentials intentionally fail local validation before any AniList call.
      assert.equal(response.status(), 400);
      const fields = new Map(response.request().postDataJSON().fields.map((field) => [field.name, field.value]));
      assert.deepEqual(fields.get('includedCustomLists'), [selectedName, 'Watching later']);
      assert.deepEqual(fields.get('excludedCustomLists'), [excludedName, selectedName]);
      assert.equal(fields.get('importCurrent'), true);
      assert.ok(obsoleteStatusExclusions.every((name) => !fields.has(name)), 'Removed status exclusions were submitted');
      assert.ok(!fields.has('customList') || !fields.get('customList'), 'An obsolete hidden custom-list restriction was submitted');
      if (input.artifactDirectory) await page.screenshot({
        path: path.join(input.artifactDirectory, 'anilist-included-excluded-lists.png'), fullPage: true,
        style: 'input[type="password"] { visibility: hidden !important; }',
      });
      await page.getByRole('button', { name: 'Cancel', exact: true }).last().click();
      const savedAfter = await context.request.get(input.baseUrl + '/api/v5/importlist', {
        headers: { 'X-Api-Key': input.apiKey },
      });
      assert.equal(savedAfter.status(), 200);
      assert.deepEqual(await savedAfter.json(), originalLists, 'Cancelled filter editor changed saved lists');
    });
    for (const provider of [
      { implementation: 'TmdbListImport', include: 'includedListIds', exclude: 'excludedListIds', sample: '42', legacy: ['listId', 'accountListId'], controls: ['authToken', 'accountId'], loading: true },
      { implementation: 'TraktListImport', include: 'includedLists', exclude: 'excludedLists', sample: 'watching-this-season', legacy: ['listname'], controls: ['username', 'years'], loading: true },
      { implementation: 'MDBListImport', include: 'includedListUrls', exclude: 'excludedListUrls', sample: 'https://mdblist.com/lists/generated-owner/weekend-picks', legacy: ['listUrl'], controls: ['apiKey'], loading: false },
      { implementation: 'MyAnimeListImport', include: 'includedTags', exclude: 'excludedTags', sample: 'Spring, favorites', legacy: ['listStatus'], controls: ['username', 'clientId', 'clientSecret', 'minimumScore', 'importFinished', 'importAiring', 'importUnreleased'], loading: false },
      { implementation: 'SimklUserImport', include: 'includedCustomListIds', exclude: 'excludedCustomListIds', sample: '501', legacy: [], controls: ['clientIdV2', 'clientSecretV2'], choices: ['Shows', 'Watching'], loading: true },
    ]) {
      await check(provider.implementation + ' grouped list form preserves literal selections, excludes overlaps and cancels without account access', async () => {
        const headers = { 'X-Api-Key': input.apiKey };
        const schemaResponse = await context.request.get(input.baseUrl + '/api/v5/importlist/schema', { headers });
        assert.equal(schemaResponse.status(), 200);
        const schema = (await schemaResponse.json()).find((candidate) => candidate.implementation === provider.implementation);
        assert.ok(schema, 'The packaged provider schema is missing');
        const savedBefore = await context.request.get(input.baseUrl + '/api/v5/importlist', { headers });
        assert.equal(savedBefore.status(), 200);
        const originalLists = await savedBefore.json();
        await page.getByRole('button', { name: 'Add Import List', exact: true }).click();
        await page.getByRole('button', { name: 'Add Import List - ' + schema.implementationName, exact: true }).click();
        const included = page.locator('input[name="' + provider.include + '"]');
        const excluded = page.locator('input[name="' + provider.exclude + '"]');
        await included.waitFor({ state: 'visible' });
        await excluded.waitFor({ state: 'visible' });
        await page.getByText('Included selections are combined, then entries in any excluded selection are removed. Exclusions always win, including overlaps', { exact: true }).waitFor({ state: 'visible' });
        await page.getByText('Test checks the current form without saving it. If the provider returns incomplete or unknown required list data, the last successful import list is retained', { exact: true }).waitFor({ state: 'visible' });
        for (const name of provider.controls) {
          assert.equal(await page.locator('input[name="' + name + '"]').count(), 1, 'Missing provider control ' + name);
        }
        for (const name of provider.choices ?? []) {
          assert.equal(await page.getByRole('button', { name, exact: true }).count(), 1, 'Missing provider catalogue/status choice ' + name);
        }
        for (const name of provider.legacy) {
          assert.equal(await page.locator('input[name="' + name + '"]').count(), 0, 'Obsolete single-selection field is visible');
        }
        assert.equal(await page.getByRole('button', { name: 'Load lists', exact: true }).count(), provider.loading ? 1 : 0);
        await included.fill(provider.sample);
        await included.press('Enter');
        await excluded.fill(provider.sample);
        await excluded.press('Enter');
        await page.getByText(provider.sample, { exact: true }).nth(1).waitFor({ state: 'visible' });
        assert.equal(await page.getByText(provider.sample, { exact: true }).count(), 2, 'A literal selection was split or dropped');
        if (provider.implementation === 'TraktListImport') await page.locator('input[name="username"]').fill('generated-user');
        if (provider.implementation === 'SimklUserImport') {
          await page.locator('input[name="clientIdV2"]').fill('generated-v2-application');
          await page.getByRole('button', { name: 'Shows', exact: true }).click();
          await page.getByText('Anime', { exact: true }).last().click();
        }
        if (provider.implementation === 'MyAnimeListImport') {
          const statuses = page.locator('input[name="listStatuses"]');
          await statuses.waitFor({ state: 'visible' });
          await statuses.fill('Watching');
          await statuses.press('Enter');
        }
        const testResponse = page.waitForResponse((response) =>
          /\/api\/v[35]\/importlist\/test\/?$/.test(new URL(response.url()).pathname) && response.request().method() === 'POST');
        await page.getByRole('button', { name: 'Test', exact: true }).last().click();
        const response = await testResponse;
        // Every generated form deliberately omits credentials. Local validation
        // must fail before a request to a real account/list service.
        assert.equal(response.status(), 400);
        const fields = new Map(response.request().postDataJSON().fields.map((field) => [field.name, field.value]));
        assert.deepEqual(fields.get(provider.include), [provider.sample]);
        assert.deepEqual(fields.get(provider.exclude), [provider.sample]);
        if (provider.implementation === 'MyAnimeListImport') {
          assert.ok(Array.isArray(fields.get('listStatuses')) && fields.get('listStatuses').includes(1));
          assert.ok(fields.get('listStatuses').every((value) => Number.isInteger(value)));
        }
        if (provider.implementation === 'SimklUserImport') {
          assert.equal(fields.get('clientIdV2'), 'generated-v2-application');
          assert.equal(fields.get('showType'), 1);
          assert.equal(fields.get('listType'), 0);
        }
        for (const name of provider.legacy) assert.ok(!fields.has(name) || !fields.get(name), 'A hidden restriction was submitted');
        await page.getByRole('button', { name: 'Cancel', exact: true }).last().click();
        const savedAfter = await context.request.get(input.baseUrl + '/api/v5/importlist', { headers });
        assert.equal(savedAfter.status(), 200);
        assert.deepEqual(await savedAfter.json(), originalLists, 'Cancelled provider editor changed saved lists');
      });
    }
    await check('browser navigation produces no unhandled JavaScript exceptions or forbidden Sonarr-cloud requests', async () => {
      assert.deepEqual(pageErrors, []);
      assert.deepEqual(cloudRequests, []);
    });
  } finally {
    await browser.close();
  }
  const report = { passed: failures.length === 0, browser: 'Chromium', playwrightVersion: require(path.join(input.playwrightModule, 'package.json')).version,
    menuOnly: Boolean(input.menuOnly),
    checks: results, failures, pageErrors, forbiddenCloudRequests: cloudRequests, realLibraryTested: false, downloadsTested: false, privateAniListAccountTested: false };
  if (input.artifactDirectory) fs.writeFileSync(path.join(input.artifactDirectory, 'ui-security.json'), JSON.stringify(report, null, 2) + '\n');
  process.exitCode = failures.length ? 1 : 0;
}

let input = '';
process.stdin.setEncoding('utf8');
process.stdin.on('data', (chunk) => { input += chunk; });
process.stdin.on('end', () => main(JSON.parse(input)).catch((error) => {
  console.error('FAIL browser execution: ' + error.name);
  process.exitCode = 1;
}));
