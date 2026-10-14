'use strict';
const assert = require('node:assert/strict');
const crypto = require('node:crypto');
const fs = require('node:fs');
const path = require('node:path');

async function main(input) {
  assert.equal(input.ownedFixture, true);
  assert.match(input.imageId, /^sha256:[a-f0-9]{64}$/);
  const target = new URL(input.baseUrl);
  assert.equal(target.origin, `http://127.0.0.1:${target.port}`);
  const playwright = require(input.playwrightModule);
  const dependency = require(require.resolve(path.join(input.playwrightModule, 'package.json')));
  assert.equal(dependency.version, input.playwrightVersion, 'The browser dependency must use the explicitly pinned version');
  const directory = path.resolve(input.artifactDirectory);
  const destination = path.join(directory, 'collections-browser.json');
  assert.equal(fs.existsSync(destination), false, 'Keep previous verification evidence');
  const browser = await playwright.chromium.launch({ headless: true });
  const checks = [], errors = [], consoleErrors = [], outside = [], requests = [], mutations = [], images = [], screenshots = [], screenshotProofs = [];
  const observations = { playwrightVersion: dependency.version, browserVersion: browser.version(), devicePixelRatios: [] };
  const contexts = [];
  const redact = (value) => {
    let result = String(value);
    for (const secret of [input.apiKey, input.username, input.password]) result = result.split(secret).join('[REDACTED]');
    return result;
  };
  const createContext = async (options = {}) => {
    const context = await browser.newContext({ viewport: { width: 1600, height: 1100 }, locale: 'en-US', ...options });
    contexts.push(context);
    await context.route('**/*', async (route) => {
      const url = new URL(route.request().url());
      const localSocket = url.protocol === 'ws:' && url.hostname === target.hostname && url.port === target.port;
      if (url.origin !== target.origin && !localSocket) {
        outside.push(redact(url.origin + url.pathname));
        await route.abort();
        return;
      }
      await route.continue();
    });
    context.on('page', (page) => {
      page.setDefaultTimeout(30000);
      page.on('pageerror', (error) => errors.push(redact(error.message)));
      page.on('console', (message) => {
        if (message.type() === 'error') {
          const location = message.location();
          consoleErrors.push({ text: redact(message.text()), path: location.url ? new URL(location.url).pathname : null });
        }
      });
      page.on('request', (request) => {
        const url = new URL(request.url());
        if (url.pathname.startsWith('/api/')) requests.push({ path: url.pathname, query: Object.fromEntries(url.searchParams), method: request.method() });
        if (url.pathname.startsWith('/api/') && request.method() !== 'GET') mutations.push({ path: url.pathname, method: request.method(), body: request.postDataJSON() });
      });
      page.on('response', (response) => {
        const url = new URL(response.url());
        if (url.pathname.startsWith('/MediaCover') || url.pathname.startsWith('/mediacover')) images.push({ path: url.pathname, status: response.status(), contentType: response.headers()['content-type'] });
      });
    });
    return context;
  };
  const context = await createContext();
  const page = await context.newPage();
  let diagnosticPage = page;
  let failure = null;
  const api = async (resource) => {
    const response = await context.request.get(input.baseUrl + '/api/v5/' + resource, { headers: { 'X-Api-Key': input.apiKey } });
    assert.equal(response.status(), 200, `GET ${resource} must succeed`);
    return response.json();
  };
  const snapshot = (series) => series.map((item) => ({ id: item.id, title: item.title, path: item.path, tvdbId: item.tvdbId, foreignId: item.foreignId, metadataSource: item.metadataSource })).sort((a, b) => a.id - b.id);
  const groupRoute = (id) => `/collections/${encodeURIComponent('group:' + id)}`;
  const nativeRoute = `/collections/${encodeURIComponent('series:' + input.nativeId)}`;
  const close = async () => page.getByRole('button', { name: 'Close', exact: true }).last().click();
  const visibleCovers = async (activePage) => {
    const cards = activePage.locator('article[data-native-season]');
    await cards.first().waitFor({ state: 'visible' });
    for (let index = 0; index < await cards.count(); index++) {
      const card = cards.nth(index);
      await card.scrollIntoViewIfNeeded();
      const identity = { seriesId: await card.getAttribute('data-anime-group-series-id'), season: await card.getAttribute('data-native-season') };
      await activePage.waitForFunction(({ seriesId, season }) => {
        const image = document.querySelector(`article[data-anime-group-series-id="${seriesId}"][data-native-season="${season}"] img`);
        return image && image.complete && image.naturalWidth > 0 && !image.src.includes('/Content/Images/');
      }, identity);
    }
    await activePage.waitForFunction(() => {
      const covers = [...document.querySelectorAll('article[data-native-season] img')];
      return covers.length > 0 && covers.every((image) => image.complete && image.naturalWidth > 0);
    });
    return activePage.locator('article[data-native-season] img').evaluateAll((elements) => elements.map((image) => ({ src: new URL(image.src).pathname, width: image.naturalWidth, height: image.naturalHeight })));
  };
  const screenshot = async (name, activePage = page) => {
    await activePage.locator('main').evaluate((main) => [...main.querySelectorAll('*')].forEach((node) => {
      if (node.scrollHeight > node.clientHeight && ['auto', 'scroll'].includes(getComputedStyle(node).overflowY)) node.scrollTop = 0;
    }));
    await activePage.screenshot({ path: path.join(directory, name), fullPage: true, style: 'input[type="password"] { visibility: hidden !important; }' });
    screenshots.push(name);
    const bytes = fs.readFileSync(path.join(directory, name));
    assert.equal(bytes.subarray(0, 8).toString('hex'), '89504e470d0a1a0a');
    assert.equal(bytes.subarray(12, 16).toString('ascii'), 'IHDR');
    screenshotProofs.push({ name, sha256: crypto.createHash('sha256').update(bytes).digest('hex'), byteLength: bytes.length,
      width: bytes.readUInt32BE(16), height: bytes.readUInt32BE(20) });
  };
  try {
    const initial = snapshot(await api('series'));
    assert.equal(initial.length, 8);
    await page.goto(input.baseUrl + '/series');
    await page.waitForURL('**/login**');
    await page.locator('#sign-in-panel input[name="username"]').fill(input.username);
    await page.locator('#sign-in-panel input[name="password"]').fill(input.password);
    await Promise.all([page.waitForURL((url) => !url.pathname.startsWith('/login')), page.locator('#sign-in-panel button[type="submit"]').click()]);
    await page.locator('[data-anime-group="anilist:900401"] a').first().waitFor({ state: 'visible' });
    await page.locator('[data-anime-group="anilist:900401"] a').first().click();
    await page.waitForURL((url) => url.pathname === groupRoute('anilist:900401'));
    assert.equal(await page.locator('h1').innerText(), 'Fixture Linear Anime');
    await page.getByText('Canonical generated synopsis for Fixture Linear Anime.', { exact: true }).first().waitFor({ state: 'visible' });
    await page.waitForFunction(() => [...document.querySelectorAll('header[data-collection-id] img')].some((image) => image.complete && image.naturalWidth > 0));
    const canonicalCover = input.canonicalPresentation.images.find((image) => image.coverType === 'poster');
    const canonicalPath = new URL(canonicalCover.url, input.baseUrl).pathname;
    await page.waitForFunction((expected) => [...document.querySelectorAll('header[data-collection-id] img')].some((image) => new URL(image.src).pathname === expected && image.complete && image.naturalWidth > 0), canonicalPath);
    await visibleCovers(page);
    assert.equal(await page.locator('article[data-native-season]').count(), 3);
    assert.equal(await page.locator('[data-anime-group-section="seasons"]').count(), 1);
    assert.equal(await page.getByText('Each entry keeps its own metadata, episodes and actions.', { exact: true }).count(), 0);
    const scrollable = await page.locator('main').evaluate((main) => [...main.querySelectorAll('*')].some((node) => node.scrollHeight > node.clientHeight && ['auto', 'scroll'].includes(getComputedStyle(node).overflowY)));
    assert.equal(scrollable, true, 'The collection is a scrolling page');
    await screenshot('collection-anime-desktop.png');
    checks.push('Forms-authenticated packaged library opens a scrollable collection route with canonical synopsis, backdrop and decoded real covers');

    const firstMember = input.series.find((item) => item.foreignId === '900401');
    const secondMember = input.series.find((item) => item.foreignId === '900402');
    const secondCard = page.locator(`article[data-anime-group-series-id="${secondMember.id}"][data-native-season="1"]`);
    const href = await secondCard.locator('a').first().getAttribute('href');
    assert.equal(href, `/series/${secondMember.titleSlug}?season=1#season-1`);
    await secondCard.locator('a').first().click();
    await page.waitForURL((url) => url.pathname === '/series/' + secondMember.titleSlug && url.searchParams.get('season') === '1');
    await page.locator('#season-1 table').waitFor({ state: 'visible' });
    assert.equal(await page.locator('#season-1 table tbody tr').count(), 3);
    await page.getByRole('link', { name: 'Collection', exact: true }).click();
    await page.waitForURL((url) => url.pathname === groupRoute('anilist:900401'));
    checks.push('logical Season 2 card opens native Season 1, expands actual episodes and links back to its collection');

    const refreshResponse = page.waitForResponse((response) => response.request().method() === 'POST' && new URL(response.url()).pathname === '/api/v5/command');
    await page.getByRole('button', { name: 'Refresh & Scan', exact: true }).click();
    const refreshed = await refreshResponse;
    assert.equal(refreshed.status(), 201);
    const refreshCommand = await refreshed.json();
    assert.deepEqual(refreshCommand.body.seriesIds.slice().sort((a, b) => a - b), input.series.filter((item) => item.animeGroupId === 'anilist:900401').map((item) => item.id).sort((a, b) => a - b));
    let refreshState = refreshCommand;
    for (let attempt = 0; attempt < 150 && !['completed', 'failed', 'aborted'].includes(refreshState.status); attempt++) {
      await new Promise((resolve) => setTimeout(resolve, 200));
      refreshState = await api('command/' + refreshCommand.id);
    }
    assert.equal(refreshState.status, 'completed');
    checks.push('Refresh & Scan executes a real successful metadata refresh command for every native collection member');

    await page.getByRole('button', { name: 'Collection Settings', exact: true }).click();
    await page.locator('input[name="animeGroupTitle"]').fill('Generated Renamed Collection');
    const savedGroup = page.waitForResponse((response) => response.request().method() === 'PUT' && new URL(response.url()).pathname === '/api/v5/series/editor');
    await page.getByRole('button', { name: 'Save', exact: true }).last().click();
    const groupResponse = await savedGroup;
    assert.equal(groupResponse.status(), 200);
    const groupBody = await groupResponse.json();
    assert.equal(Array.isArray(groupBody), true);
    observations.mutationResponseContracts = { bulkEditor: { status: groupResponse.status(), array: true,
      nativeIds: groupBody.map((item) => item.id), seasonStatisticsNullable: groupBody.flatMap((item) => item.seasons).some((season) => season.statistics == null) } };
    await page.getByRole('heading', { name: 'Generated Renamed Collection', exact: true }).waitFor({ state: 'visible' });
    const groupMembers = (await api('series')).filter((item) => item.animeGroupId === 'anilist:900401');
    assert.equal(groupMembers.length, 3);
    assert.ok(groupMembers.every((item) => item.animeGroupTitle === 'Generated Renamed Collection' && item.animeGroupingMode === 'manual'));
    assert.deepEqual(snapshot(await api('series')), initial);
    checks.push('collection settings rename all native members atomically and preserve provider identities, catalogue titles and existing paths');

    const searchStart = requests.length;
    await page.getByRole('button', { name: 'Search Collection', exact: true }).click();
    await page.getByRole('status').filter({ hasText: 'Searched 3 of 3 entries' }).waitFor({ state: 'visible' });
    const allSearches = requests.slice(searchStart).filter((item) => item.path === '/api/v5/release' && item.query.completeSeries === 'true');
    assert.equal(allSearches.length, 3);
    assert.deepEqual(allSearches.map((item) => Number(item.query.seriesId)).sort((a, b) => a - b), groupMembers.map((item) => item.id).sort((a, b) => a - b));
    const selector = page.locator('select[name="collectionSearchSeries"]');
    await selector.selectOption(String(secondMember.id));
    await page.getByText('Fixture Linear Anime Sequel.S01.1080p.WEB-DL.H264-Fixture', { exact: true }).first().waitFor({ state: 'visible' });
    assert.equal(requests.slice(searchStart).filter((item) => item.path === '/api/v5/release' && item.query.completeSeries === 'true').length, 3);
    await close();
    checks.push('Search Collection performs actual read-only complete-series release queries for every member and changing results reuses the native cache');

    await secondCard.getByRole('button', { name: 'Search Season 2 Packs', exact: true }).click();
    await page.getByText('Fixture Linear Anime Sequel.S01.1080p.WEB-DL.H264-Fixture', { exact: true }).first().waitFor({ state: 'visible' });
    const packSearch = requests.filter((item) => item.path === '/api/v5/release' && item.query.seasonPacksOnly === 'true').at(-1);
    assert.equal(packSearch.query.seriesId, String(secondMember.id));
    assert.equal(packSearch.query.seasonNumber, '1');
    await close();
    await secondCard.getByRole('button', { name: 'Search Episodes', exact: true }).click();
    await page.getByText('Fixture Linear Anime Sequel.S01E01.1080p.WEB-DL.H264-Fixture', { exact: true }).first().waitFor({ state: 'visible' });
    const episodeSearch = requests.filter((item) => item.path === '/api/v5/release' && item.query.seasonNumber === '1' && !item.query.seasonPacksOnly).at(-1);
    assert.equal(episodeSearch.query.seriesId, String(secondMember.id));
    await close();
    await secondCard.getByRole('button', { name: 'Search Season 2 Packs', exact: true }).click();
    await page.getByText('Fixture Linear Anime Sequel.S01.1080p.WEB-DL.H264-Fixture', { exact: true }).first().waitFor({ state: 'visible' });
    await close();
    checks.push('each logical season pack and episode button initially displays its real parsed RSS releases using native identities without changing the other search preference');

    await secondCard.getByRole('button', { name: 'Season Settings', exact: true }).click();
    await page.locator('input[name="monitored"]').locator('..').click();
    assert.equal(await page.locator('input[name="monitored"]').isChecked(), true);
    const savedSeason = page.waitForResponse((response) => response.request().method() === 'PUT' && new URL(response.url()).pathname === `/api/v5/series/${secondMember.id}/season`);
    await page.getByRole('button', { name: 'Save', exact: true }).last().click();
    const seasonResponse = await savedSeason;
    assert.equal(seasonResponse.status(), 200);
    const seasonBody = await seasonResponse.json();
    assert.equal(seasonBody.seasonNumber, 1);
    assert.equal(seasonBody.monitored, true);
    assert.equal(Object.hasOwn(seasonBody, 'seasons'), false);
    observations.mutationResponseContracts.seasonMonitor = { status: seasonResponse.status(), singleSeason: true,
      seasonNumber: seasonBody.seasonNumber, monitored: seasonBody.monitored, statisticsNullable: seasonBody.statistics == null };
    assert.equal((await api('series/' + secondMember.id)).seasons.find((season) => season.seasonNumber === 1).monitored, true);
    assert.equal((await api('series/' + firstMember.id)).seasons.find((season) => season.seasonNumber === 1).monitored, false);
    checks.push('season settings persist monitoring only for the selected native season');

    await secondCard.getByRole('button', { name: 'Season Settings', exact: true }).click();
    await page.getByRole('button', { name: 'Edit Series', exact: true }).last().click();
    await page.locator('input[name="monitored"]').locator('..').click();
    assert.equal(await page.locator('input[name="monitored"]').isChecked(), true);
    const savedEntry = page.waitForResponse((response) => response.request().method() === 'PUT' && new URL(response.url()).pathname === '/api/v5/series');
    await page.getByRole('button', { name: 'Save', exact: true }).last().click();
    const entryResponse = await savedEntry;
    assert.equal(entryResponse.status(), 202);
    const entryBody = await entryResponse.json();
    assert.equal(entryBody.id, secondMember.id);
    assert.equal(Array.isArray(entryBody.seasons), true);
    observations.mutationResponseContracts.nativeEntry = { status: entryResponse.status(), nativeSeriesId: entryBody.id,
      seasons: entryBody.seasons.map((season) => season.seasonNumber), statisticsPresent: entryBody.statistics != null };
    assert.equal((await api('series/' + secondMember.id)).monitored, true);
    assert.equal(await secondCard.getByRole('button', { name: 'Search Season 2 Packs', exact: true }).isEnabled(), true);
    checks.push('native entry editing uses the real Accepted SeriesResource response and immediately retains usable season actions');

    await page.goto(input.baseUrl + groupRoute('anilist:900501'));
    await page.locator('article[data-native-season]').first().waitFor({ state: 'visible' });
    assert.equal(await page.locator('[data-anime-group-section="seasons"] article').count(), 1);
    assert.equal(await page.locator('[data-anime-group-section="movies"] article').count(), 1);
    await page.getByText('Canonical generated synopsis for Mixed Fixture Anime.', { exact: true }).first().waitFor({ state: 'visible' });
    assert.equal(input.series.some((item) => item.foreignId === '900501'), false);
    await visibleCovers(page);
    const movie = input.series.find((item) => item.foreignId === '900502');
    assert.equal(await page.locator(`[data-anime-group-series-id="${movie.id}"] a`).first().getAttribute('href'), `/series/${movie.titleSlug}?season=1#season-1`);
    await screenshot('collection-mixed-desktop.png');
    await page.locator(`[data-anime-group-series-id="${movie.id}"] a`).first().click();
    await page.locator('#season-1 table').waitFor({ state: 'visible' });
    assert.equal(await page.locator('#season-1 table tbody tr').count(), 1);
    await page.goto(input.baseUrl + groupRoute('anilist:900601'));
    await page.locator('[data-anime-group-section="movies"] article').first().waitFor({ state: 'visible' });
    assert.equal(await page.locator('[data-anime-group-section="movies"] article').count(), 2);
    assert.equal(await page.locator('[data-anime-group-section="seasons"]').count(), 0);
    await visibleCovers(page);
    checks.push('mixed series/movie collections and pure movie sagas show distinct clickable, covered entries');

    await page.goto(input.baseUrl + nativeRoute);
    await page.locator('[data-anime-group-section="specials"] article').waitFor({ state: 'visible' });
    assert.equal(await page.locator('[data-anime-group-section="seasons"] article').count(), 3);
    assert.equal(await page.locator('[data-anime-group-section="specials"] article').count(), 1);
    await page.locator(`article[data-anime-group-series-id="${input.nativeId}"][data-native-season="0"] a`).first().click();
    await page.locator('#season-0 table').waitFor({ state: 'visible' });
    assert.equal(await page.locator('#season-0 table tbody tr').count(), 1);
    await page.goto(input.baseUrl + nativeRoute);
    const nativeCard = page.locator(`article[data-anime-group-series-id="${input.nativeId}"][data-native-season="2"]`);
    await nativeCard.locator('a').first().click();
    await page.locator('#season-2 table').waitFor({ state: 'visible' });
    assert.equal(await page.locator('#season-2 table tbody tr').count(), 1);
    await page.goto(input.baseUrl + nativeRoute);
    await page.getByRole('button', { name: 'Collection Settings', exact: true }).click();
    await page.locator('input[name="collectionTitle"]').fill('Generated Native Display Name');
    const savedNative = page.waitForResponse((response) => response.request().method() === 'PUT' && new URL(response.url()).pathname === '/api/v5/series/editor');
    await page.getByRole('button', { name: 'Save', exact: true }).last().click();
    assert.equal((await savedNative).status(), 200);
    await page.getByRole('heading', { name: 'Generated Native Display Name', exact: true }).waitFor({ state: 'visible' });
    const native = await api('series/' + input.nativeId);
    assert.equal(native.collectionTitle, 'Generated Native Display Name');
    assert.equal(native.title, 'Fixture Native Collection Show');
    assert.deepEqual(snapshot(await api('series')), initial);
    await screenshot('collection-native-desktop.png');
    checks.push('native TV collections include every real season and Specials, open native episode tables and persist an independent display name');

    const storageState = await context.storageState();
    for (const dpr of [1, 2, 3]) {
      const dpiContext = await createContext({ deviceScaleFactor: dpr, storageState });
      const dpiPage = await dpiContext.newPage();
      diagnosticPage = dpiPage;
      await dpiPage.goto(input.baseUrl + nativeRoute);
      const covers = await visibleCovers(dpiPage);
      const expectedHeight = dpr === 1 ? 250 : 500;
      assert.ok(covers.every((cover) => cover.src.endsWith(`poster-${expectedHeight}.jpg`)));
      assert.ok(covers.every((cover) => cover.height === expectedHeight));
      observations.devicePixelRatios.push({ dpr, covers });
      await dpiContext.close();
      diagnosticPage = page;
    }
    checks.push('actual Chromium decodes only supported 250/500px cover sizes at device pixel ratios 1, 2 and 3');

    const retryContext = await createContext({ deviceScaleFactor: 3, storageState });
    await retryContext.route('**/MediaCover/100/poster-500.jpg*', async (route) => route.fulfill({ status: 404, body: 'controlled missing thumbnail' }));
    const retryPage = await retryContext.newPage();
    diagnosticPage = retryPage;
    await retryPage.goto(input.baseUrl + nativeRoute);
    const retryCovers = await visibleCovers(retryPage);
    assert.ok(retryCovers.every((cover) => cover.src.endsWith('/poster.jpg') && cover.width > 0));
    await retryContext.close();
    diagnosticPage = page;
    checks.push('a controlled missing thumbnail recovers through the existing original cached image before showing a placeholder');

    await page.setViewportSize({ width: 390, height: 844 });
    await page.goto(input.baseUrl + groupRoute('anilist:900501'));
    await visibleCovers(page);
    assert.equal(await page.locator('main').evaluate((main) => main.scrollWidth <= main.clientWidth + 1), true);
    const card = page.locator('article[data-native-season]').first();
    await card.getByRole('button', { name: 'Search Episodes', exact: true }).scrollIntoViewIfNeeded();
    assert.equal(await card.getByRole('button', { name: 'Search Episodes', exact: true }).isVisible(), true);
    await screenshot('collection-mixed-mobile.png');
    checks.push('collection layout remains within a narrow mobile viewport with reachable native actions');

    const allEpisodes = [];
    for (const item of input.series) allEpisodes.push(...await api('episode?seriesId=' + item.id));
    assert.deepEqual(allEpisodes.map((item) => [item.id, item.seriesId, item.seasonNumber, item.episodeNumber]).sort((a, b) => a[0] - b[0]), input.episodeIdentities.sort((a, b) => a[0] - b[0]));
    assert.deepEqual(snapshot(await api('series')), initial);
    assert.deepEqual(errors, []);
    assert.deepEqual(consoleErrors.filter((entry) => !(entry.path === '/MediaCover/100/poster-500.jpg' && entry.text.includes('Failed to load resource'))), []);
    assert.deepEqual(outside, []);
    assert.ok(mutations.every((entry) => (entry.method === 'PUT' && (entry.path === '/api/v5/series' || entry.path === '/api/v5/series/editor' || /\/series\/\d+\/season$/.test(entry.path))) || (entry.method === 'POST' && entry.path === '/api/v5/command' && entry.body.name === 'RefreshSeries')));
    assert.ok(images.every((image) => !/poster-(750|1000|1500)\.jpg/.test(image.path)));
    checks.push('browser preserves native identities and paths, has no JavaScript errors or outside requests, and performs no acquisition mutation');
  } catch (error) {
    failure = redact(error.message);
    observations.failurePage = await diagnosticPage.evaluate(() => ({
      pathname: location.pathname,
      dpr: window.devicePixelRatio,
      covers: [...document.querySelectorAll('article[data-native-season]')].map((card) => ({
        seriesId: card.getAttribute('data-anime-group-series-id'), season: card.getAttribute('data-native-season'),
        bounds: card.getBoundingClientRect().toJSON(), images: [...card.querySelectorAll('img')].map((image) => ({
          src: new URL(image.src).pathname, complete: image.complete, naturalWidth: image.naturalWidth, naturalHeight: image.naturalHeight,
          bounds: image.getBoundingClientRect().toJSON(),
        })),
      })),
      scrollers: [...document.querySelectorAll('*')].filter((node) => node.scrollHeight > node.clientHeight && ['auto', 'scroll'].includes(getComputedStyle(node).overflowY)).map((node) => ({
        tag: node.tagName, className: node.className, clientHeight: node.clientHeight, scrollHeight: node.scrollHeight, scrollTop: node.scrollTop,
        bounds: node.getBoundingClientRect().toJSON(),
      })),
    })).catch(() => null);
    await screenshot('collection-browser-failure.png', diagnosticPage).catch(() => {});
    fs.writeFileSync(path.join(directory, 'collection-browser-failure.txt'), redact(await diagnosticPage.locator('body').innerText().catch(() => '')), 'utf8');
  } finally {
    await Promise.allSettled(contexts.map((item) => item.close()));
    await browser.close();
  }
  const report = { passed: !failure, imageId: input.imageId, sourceRevision: input.sourceRevision, checks, observations,
    javascriptErrors: errors, consoleErrors, outsideRequests: outside, requests, mutations, imageResponses: images, screenshots, screenshotProofs, failure,
    scope: { ownedFixture: true, ownerConfigurationAccessed: false, privateAccountUsed: false, publicMetadataRequests: false, acquisitionTested: false } };
  fs.writeFileSync(destination, JSON.stringify(report, null, 2) + '\n', 'utf8');
  console.log(JSON.stringify({ passed: report.passed, checks: checks.length, failure }));
  process.exitCode = failure ? 1 : 0;
}
let input = '';
process.stdin.setEncoding('utf8');
process.stdin.on('data', (chunk) => { input += chunk; });
process.stdin.on('end', () => main(JSON.parse(input)).catch((error) => {
  console.error('FAIL collection browser helper: ' + error.name);
  process.exitCode = 1;
}));
