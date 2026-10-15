'use strict';
const assert = require('node:assert/strict');

const ROUTES = [
  ['library', '/series', 'Series'],
  ['add-new', '/add/new', 'Add New Series'],
  ['library-import', '/add/import', 'Import Series'],
  ['calendar', '/calendar', 'Calendar'],
  ['statistics', '/statistics', 'Statistics'],
  ['wanted-missing', '/wanted/missing', 'Missing'],
  ['wanted-cutoff', '/wanted/cutoffunmet', 'Cutoff Unmet'],
  ['queue', '/activity/queue', 'Queue'],
  ['history', '/activity/history', 'History'],
  ['blocklist', '/activity/blocklist', 'Blocklist'],
  ['settings', '/settings', 'Settings'],
  ['settings-media', '/settings/mediamanagement', 'Media Management'],
  ['settings-profiles', '/settings/profiles', 'Profiles'],
  ['settings-quality', '/settings/quality', 'Quality'],
  ['settings-custom-formats', '/settings/customformats', 'Custom Formats'],
  ['settings-indexers', '/settings/indexers', 'Indexers'],
  ['settings-download-clients', '/settings/downloadclients', 'Download Clients'],
  ['settings-import-lists', '/settings/importlists', 'Import Lists'],
  ['settings-connect', '/settings/connect', 'Connect'],
  ['settings-metadata', '/settings/metadata', 'Metadata'],
  ['settings-metadata-sources', '/settings/metadatasource', 'Metadata Source'],
  ['settings-tags', '/settings/tags', 'Tags'],
  ['settings-general', '/settings/general', 'General'],
  ['settings-ui', '/settings/ui', 'UI'],
  ['system-status', '/system/status', 'Status'],
  ['library-tools', '/system/library', 'Library tools'],
  ['system-tasks', '/system/tasks', 'Tasks'],
  ['system-backups', '/system/backups', 'Backups'],
  ['system-updates', '/system/updates', 'Updates'],
  ['system-events', '/system/events', 'Events'],
  ['system-log-files', '/system/logs/files', 'Log Files'],
];

async function auditGuiWorkflows({ input, context, createContext, api, screenshot, diagnostic, checks, observations }) {
  const records = [];
  observations.guiWorkflows = { routes: records, dialogs: [], settingsSavedAndRestored: false, generatedMoviesAdded: [] };
  const gui = observations.guiWorkflows;

  async function settled(page) {
    await page.locator('main').waitFor({ state: 'visible' });
    await page.waitForLoadState('networkidle');
    await page.waitForFunction(() => [...document.querySelectorAll('main [class*="LoadingIndicator_rippleContainer"]')].every((element) => element.getBoundingClientRect().width === 0));
    await page.evaluate(async () => { await document.fonts.ready; });
  }

  async function geometry(page) {
    return page.locator('main').evaluate((main) => {
      const width = document.documentElement.clientWidth;
      const fields = [...main.querySelectorAll('input:not([type="hidden"]), select, textarea')].map((field) => {
        const box = field.getBoundingClientRect();
        return { name: field.name, x: box.x, width: box.width, right: box.right };
      }).filter((field) => field.width > 1);
      const tables = [...main.querySelectorAll('table')].map((table) => {
        let container = table.parentElement;
        while (container && container !== main && !['auto', 'scroll'].includes(getComputedStyle(container).overflowX)) container = container.parentElement;
        const box = container?.getBoundingClientRect();
        return { tableWidth: table.getBoundingClientRect().width, containerWidth: container?.clientWidth, containerScrollWidth: container?.scrollWidth,
          containerOverflow: container ? getComputedStyle(container).overflowX : undefined,
          left: box?.left, right: box?.right };
      });
      return { viewportWidth: width, documentScrollWidth: document.documentElement.scrollWidth, mainWidth: main.clientWidth, mainScrollWidth: main.scrollWidth, tables, fields,
        clippedFields: fields.filter((field) => field.x < -1 || field.right > width + 1) };
    });
  }

  async function modalFits(page) {
    const box = await page.getByRole('dialog').last().boundingBox();
    const viewport = page.viewportSize();
    assert.ok(box && box.x >= -1 && box.y >= -1 && box.x + box.width <= viewport.width + 1 && box.y + box.height <= viewport.height + 1, 'dialog and actions fit in the viewport');
  }

  async function closeModal(page) {
    const dialog = page.getByRole('dialog').last();
    const button = dialog.getByRole('button', { name: /^(Cancel|Close)$/ }).last();
    await button.click();
    await dialog.waitFor({ state: 'hidden' });
  }

  async function providerDialog(page, route, add, schemaPath, implementation, name, view) {
    await page.goto(input.baseUrl + route);
    await settled(page);
    await page.getByRole('button', { name: add, exact: true }).click();
    await page.getByRole('dialog').last().waitFor({ state: 'visible' });
    const schema = (await api(schemaPath)).find((item) => item.implementation === implementation);
    assert.ok(schema, `${implementation} schema is available`);
    await page.getByRole('dialog').last().getByRole('button', { name: add + ' - ' + schema.implementationName, exact: true }).click();
    const dialog = page.getByRole('dialog').last();
    await dialog.locator('input[name="name"]').waitFor({ state: 'visible' });
    await dialog.locator('input[name="name"]').fill('Generated GUI ' + name);
    assert.equal(await dialog.locator('input[name="name"]').inputValue(), 'Generated GUI ' + name);
    assert.ok(await dialog.locator('input, select').count() >= 3, 'the complete provider editor has real schema fields');
    if (implementation === 'Jellyfin' && view === 'mobile') {
      const triggerGroup = dialog.getByRole('group', { name: 'Notification Triggers', exact: true });
      const triggerBounds = await triggerGroup.evaluate((group) => {
        const label = group.querySelector('label').getBoundingClientRect();
        const input = group.querySelector('input[type="checkbox"]').getBoundingClientRect();
        return { labelBottom: label.bottom, labelTop: label.top, inputTop: input.top, labelWidth: label.width };
      });
      assert.ok(triggerBounds.inputTop >= triggerBounds.labelBottom && triggerBounds.labelWidth >= 150, 'the grouped trigger label sits above the checkbox list without overlap on touch screens');
      gui.notificationTriggerBounds = triggerBounds;
    }
    if (['Newznab', 'Torznab'].includes(implementation)) {
      const movies = schema.fields.find((field) => field.name === 'movieCategories');
      const television = schema.fields.find((field) => field.name === 'categories');
      assert.deepEqual(movies?.value, [2000], `${implementation} has independent movie categories`);
      assert.deepEqual(television?.value, [5030, 5040], `${implementation} retains its TV categories`);
      const label = dialog.locator('label').filter({ hasText: /^Movie Categories$/ });
      await label.scrollIntoViewIfNeeded();
      await label.locator('..').getByRole('button').filter({ hasText: /2000|Movies/i }).first().waitFor({ state: 'visible' });
      const controls = await label.locator('..').getByRole('button').allTextContents();
      assert.match(controls.join(' '), /2000|Movies/i, 'the movie category default is actually visible in its editor');
      gui.movieCategories ||= [];
      gui.movieCategories.push({ view, implementation, movies: movies.value, television: television.value, controls });
    }
    await modalFits(page);
    await screenshot(`gui-${name}-editor-${view}.png`, page);
    gui.dialogs.push({ view, provider: implementation, fields: await dialog.locator('input, select').evaluateAll((fields) => fields.map((field) => field.name)) });
    await closeModal(page);
  }

  const storageState = await context.storageState();
  for (const view of ['desktop', 'mobile']) {
    const mobile = view === 'mobile';
    const browserContext = await createContext({ storageState, viewport: mobile ? { width: 390, height: 844 } : { width: 1600, height: 1100 }, isMobile: mobile, hasTouch: mobile });
    const page = await browserContext.newPage();
    diagnostic(page);
    await page.goto(input.baseUrl + '/series');
    await settled(page);
    await page.evaluate(() => { window.__guiNavigationSession = 'generated-navigation-marker'; });
    const calendarLink = page.locator(`nav${mobile ? '[aria-label="Main Navigation"]' : ''} a[href="/calendar"]:visible`).first();
    await calendarLink.focus();
    await calendarLink.press('Enter');
    await page.waitForURL(input.baseUrl + '/calendar');
    await settled(page);
    assert.equal(await page.evaluate(() => window.__guiNavigationSession), 'generated-navigation-marker', 'keyboard route navigation preserves the running application');
    assert.equal(await calendarLink.evaluate((link) => document.activeElement === link), true, 'lazy-route loading preserves keyboard focus in persistent navigation');
    for (const [name, route, title] of ROUTES) {
      await page.goto(input.baseUrl + route);
      await settled(page);
      if (name === 'statistics') {
        await page.locator('main canvas').first().waitFor({ state: 'visible' });
        // Chart.js animates canvas pixels after the API and DOM have settled.
        await page.waitForFunction(() => {
          const frame = [...document.querySelectorAll('main canvas')].map((canvas) => canvas.toDataURL()).join('|');
          const previous = window.__guiChartFrame;
          window.__guiChartFrame = { frame, stable: previous?.frame === frame ? previous.stable + 1 : 0 };
          return window.__guiChartFrame.stable >= 6;
        }, undefined, { polling: 100 });
      }
      if (route.startsWith('/settings')) {
        const advanced = page.getByRole('button', { name: 'Show Advanced', exact: true });
        if (await advanced.count()) await advanced.click();
      }
      const accessibleTitle = await page.locator('main').getAttribute('aria-label');
      const headings = await page.locator('main h1').allTextContents();
      assert.ok([accessibleTitle, ...headings].some((text) => text.toLowerCase().includes(title.toLowerCase())), `${route} renders its accessible title or visible page heading`);
      const bounds = await geometry(page);
      records.push({ view, route, title, geometry: bounds });
      assert.ok(bounds.documentScrollWidth <= bounds.viewportWidth + 1, `${route} has no page-wide horizontal overflow at ${view}`);
      for (const table of bounds.tables.filter((item) => item.tableWidth > bounds.viewportWidth)) {
        assert.ok(['auto', 'scroll'].includes(table.containerOverflow) && table.left >= -1 && table.right <= bounds.viewportWidth + 1, `${route} keeps wide tables inside a bounded horizontal scroller at ${view}`);
      }
      if (route.startsWith('/settings')) assert.deepEqual(bounds.clippedFields, [], `${route} form fields fit at ${view}`);
      await screenshot(`gui-${name}-${view}.png`, page);
      if (route.startsWith('/settings/')) {
        await page.locator('main').evaluate((main) => {
          window.scrollTo(0, document.documentElement.scrollHeight);
          for (const element of main.querySelectorAll('*')) if (element.scrollHeight > element.clientHeight && ['auto', 'scroll'].includes(getComputedStyle(element).overflowY)) element.scrollTop = element.scrollHeight;
        });
        await screenshot(`gui-${name}-details-${view}.png`, page, false);
      }
    }

    await providerDialog(page, '/settings/indexers', 'Add Indexer', 'indexer/schema', 'Nyaa', 'indexer', view);
    await providerDialog(page, '/settings/downloadclients', 'Add Download Client', 'downloadclient/schema', 'QBittorrent', 'download-client', view);
    await providerDialog(page, '/settings/connect', 'Add Connection', 'connection/schema', 'Jellyfin', 'jellyfin', view);
    const aniList = (await api('importlist/schema')).find((item) => item.implementation.toLowerCase().includes('anilist'));
    assert.ok(aniList);
    await providerDialog(page, '/settings/importlists', 'Add Import List', 'importlist/schema', aniList.implementation, 'import-list', view);

    await page.goto(input.baseUrl + '/settings/metadata');
    await settled(page);
    const metadata = (await api('metadata'))[0];
    assert.ok(metadata?.name);
    await page.getByRole('button', { name: 'Metadata, ' + metadata.name, exact: true }).click();
    const metadataDialog = page.getByRole('dialog');
    const enableMetadata = metadataDialog.locator('input[name="enable"]');
    await enableMetadata.waitFor({ state: 'attached' });
    if (!await enableMetadata.isChecked()) await enableMetadata.locator('..').click();
    assert.ok(await metadataDialog.locator('input:enabled, select:enabled').count() >= 2, 'metadata exporter options respond to their Enable control');
    await modalFits(page);
    await screenshot(`gui-metadata-editor-${view}.png`, page);
    await closeModal(page);

    await page.goto(input.baseUrl + '/add/import');
    await settled(page);
    const root = (await api('rootfolder')).find((item) => item.path === '/media/library');
    assert.ok(root, 'the owned generated media root is present');
    await page.getByRole('button', { name: 'Choose another folder', exact: true }).click();
    const fileBrowser = page.getByRole('dialog');
    await fileBrowser.locator('input[name="rootFolderPath"]').fill(root.path);
    await fileBrowser.locator('table').waitFor({ state: 'visible' });
    await modalFits(page);
    await screenshot(`gui-library-file-browser-${view}.png`, page);
    await closeModal(page);
    await page.locator(`a[href="/add/import/${root.id}"]`).click();
    await settled(page);
    await page.getByRole('button', { name: 'Find Series', exact: true }).first().waitFor({ state: 'visible' });
    await screenshot(`gui-library-import-root-${view}.png`, page);

    await page.goto(input.baseUrl + '/settings/customformats');
    await settled(page);
    await page.getByRole('button', { name: 'Add Custom Format', exact: true }).click();
    await page.getByRole('dialog').locator('input[name="name"]').fill('Generated GUI Format');
    await modalFits(page);
    await screenshot(`gui-custom-format-editor-${view}.png`, page);
    await closeModal(page);

    await page.goto(input.baseUrl + '/settings/mediamanagement');
    await settled(page);
    const namingFields = ['standardEpisodeFormat', 'movieFormat', 'animeGroupFolderFormat', 'animeGroupSeasonFolderFormat', 'animeGroupArcFolderFormat', 'animeGroupMovieFolderFormat', 'animeGroupMovieSagaFolderFormat'];
    for (const field of namingFields) {
      const control = page.locator(`input[name="${field}"]`);
      await control.scrollIntoViewIfNeeded();
      const before = await control.inputValue();
      await control.locator('..').locator('..').getByRole('button').click();
      const dialog = page.getByRole('dialog');
      await dialog.locator(`input[name="${field}"]`).waitFor({ state: 'visible' });
      await modalFits(page);
      if (field === 'movieFormat') assert.equal(await dialog.getByRole('button', { name: /^\{season:/i }).count(), 0);
      await screenshot(`gui-naming-${field}-${view}.png`, page);
      await closeModal(page);
      assert.equal(await control.inputValue(), before, 'closing a token picker preserves the unchanged format');
    }
    await browserContext.close();
  }

  const settingsContext = await createContext({ storageState });
  const settingsPage = await settingsContext.newPage();
  diagnostic(settingsPage);
  await settingsPage.goto(input.baseUrl + '/settings/ui');
  await settled(settingsPage);
  const original = (await api('settings/ui')).timeFormat;
  const replacement = original === 'HH:mm' ? 'h(:mm)a' : 'HH:mm';
  const timeLabels = { 'HH:mm': '17:00/17:30', 'h(:mm)a': '5pm/5:30pm' };
  for (const value of [replacement, original]) {
    const row = settingsPage.locator('label').filter({ hasText: /^Time Format$/ }).locator('..');
    await row.getByRole('button').click();
    await settingsPage.locator('#portal-root').getByText(timeLabels[value], { exact: true }).last().click();
    const saved = settingsPage.waitForResponse((response) => response.request().method() === 'PUT' && /\/api\/v5\/settings\/ui(?:\/\d+)?$/.test(new URL(response.url()).pathname));
    await settingsPage.getByRole('button', { name: 'Save Changes', exact: true }).click();
    assert.equal((await saved).ok(), true);
    await settingsPage.reload();
    await settled(settingsPage);
    assert.ok(await settingsPage.locator('label').filter({ hasText: /^Time Format$/ }).locator('..').getByRole('button').getByText(timeLabels[value], { exact: true }).isVisible());
    assert.equal((await api('settings/ui')).timeFormat, value);
  }
  gui.settingsSavedAndRestored = true;
  await screenshot('gui-ui-setting-restored-desktop.png', settingsPage);

  await settingsPage.goto(input.baseUrl + '/settings/metadatasource');
  await settled(settingsPage);
  for (const source of ['aniList', 'tmdbMovie', 'omdb']) {
    const enabled = settingsPage.locator(`input[name="${source}"]`);
    if (!await enabled.isChecked()) await enabled.locator('..').click();
  }
  const enabled = settingsPage.waitForResponse((response) => response.request().method() === 'PUT' && /\/api\/v5\/settings\/metadata(?:\/\d+)?$/.test(new URL(response.url()).pathname));
  await settingsPage.getByRole('button', { name: 'Save Changes', exact: true }).click();
  assert.equal((await enabled).ok(), true);
  const addMobileContext = await createContext({ storageState: await settingsContext.storageState(), viewport: { width: 390, height: 844 }, isMobile: true, hasTouch: true });
  const addMobilePage = await addMobileContext.newPage();

  for (const [index, fixture] of input.guiCatalogue.entries()) {
    const mobile = index === 1 || index === 4;
    const page = mobile ? addMobilePage : settingsPage;
    diagnostic(page);
    await page.goto(input.baseUrl + '/add/new');
    await settled(page);
    await page.locator('select[name="metadataSource"]').selectOption(fixture.source);
    const search = fixture.source === 'tmdbMovie' && index === 0 ? 'imdb:tt0' + fixture.foreignId : fixture.title;
    await page.locator('input[name="seriesLookup"]').fill(search);
    await page.getByRole('button', { name: 'Add ' + fixture.title, exact: true }).click();
    const dialog = page.getByRole('dialog');
    await dialog.locator('select[name="animeGroupingMode"]').waitFor({ state: 'attached' });
    if (fixture.source === 'omdb' || fixture.special) {
      await dialog.locator('select[name="animeGroupingMode"]').selectOption('manual');
      await dialog.locator('select[name="animeGroupId"]').selectOption('anilist:900501');
      assert.equal(await dialog.locator('select[name="animeGroupMemberKind"]').inputValue(), fixture.special ? 'special' : 'movie');
    } else {
      await dialog.getByText(/Generated Feature Saga/).first().waitFor({ state: 'visible' });
    }
    if (!fixture.special) {
      for (const name of ['seriesType', 'seasonFolder', 'seasonType', 'orderingId']) assert.equal(await dialog.locator(`[name="${name}"]`).count(), 0, 'movie add hides irrelevant episodic controls');
    }
    for (const name of ['searchForMissingEpisodes', 'searchForCutoffUnmetEpisodes']) {
      const acquisition = dialog.locator(`input[name="${name}"]`);
      if (await acquisition.isChecked()) await acquisition.locator('..').click();
    }
    await modalFits(page);
    await screenshot(`gui-add-${fixture.source}-${fixture.foreignId}-${mobile ? 'mobile' : 'desktop'}.png`, page);
    const created = page.waitForResponse((response) => response.request().method() === 'POST' && new URL(response.url()).pathname === '/api/v5/series');
    await dialog.getByRole('button', { name: 'Add ' + fixture.title, exact: true }).click();
    const response = await created;
    assert.equal(response.ok(), true);
    const added = await response.json();
    assert.equal(added.metadataSource, fixture.source);
    assert.equal(added.foreignId, fixture.foreignId);
    assert.equal(added.mediaType, fixture.special ? 'special' : 'movie');
    assert.ok(added.tvdbId < 0, 'native movie/special IDs never become TVDB IDs');
    await dialog.waitFor({ state: 'hidden' });
    let episodes = [];
    for (let attempt = 0; attempt < 100; attempt++) {
      episodes = await api('episode?seriesId=' + added.id);
      if (episodes.length === (fixture.special ? 2 : 1)) break;
      await new Promise((resolve) => setTimeout(resolve, 200));
    }
    assert.deepEqual(episodes.map((episode) => [episode.seasonNumber, episode.episodeNumber]).sort((a, b) => a[1] - b[1]), fixture.special ? [[1, 1], [1, 2]] : [[1, 1]]);
    gui.generatedMoviesAdded.push({ id: added.id, source: fixture.source, foreignId: fixture.foreignId, mediaType: added.mediaType, path: added.path, titleSlug: added.titleSlug, title: fixture.title, year: fixture.year, special: fixture.special || false });
  }

  diagnostic(settingsPage);
  await settingsPage.goto(input.baseUrl + '/collections/' + encodeURIComponent('series:' + input.nativeId));
  await settingsPage.locator('article[data-native-season="1"]').getByRole('button', { name: 'Season Settings', exact: true }).click();
  const nativeDialog = settingsPage.getByRole('dialog');
  await nativeDialog.locator('select[name="animeGroupingMode"]').selectOption('manual');
  await nativeDialog.locator('select[name="animeGroupId"]').selectOption('anilist:900501');
  assert.equal(await nativeDialog.locator('input[name="animeGroupSeasonNumber"]').inputValue(), '3');
  await screenshot('gui-native-tv-mixed-group-editor-desktop.png', settingsPage);
  const grouped = settingsPage.waitForResponse((response) => response.request().method() === 'PUT' && new URL(response.url()).pathname === '/api/v5/series');
  await nativeDialog.getByRole('button', { name: 'Save', exact: true }).click();
  assert.equal((await grouped).status(), 202);
  await nativeDialog.waitFor({ state: 'hidden' });

  for (const [view, page] of [['desktop', settingsPage], ['mobile', addMobilePage]]) {
    diagnostic(page);
    await page.goto(input.baseUrl + '/collections/' + encodeURIComponent('group:anilist:900501'));
    await page.locator('[data-anime-group-section="specials"] article').first().waitFor({ state: 'visible' });
    assert.equal(await page.locator('[data-anime-group-section="seasons"] article').count(), 4);
    assert.equal(await page.locator('[data-anime-group-section="movies"] article').count(), 2);
    assert.equal(await page.locator('[data-anime-group-section="specials"] article').count(), 3);
    for (const number of [1, 2]) assert.equal(await page.getByText('Special ' + number, { exact: true }).count(), 1);
    for (const section of ['seasons', 'movies', 'specials']) {
      await page.locator(`[data-anime-group-section="${section}"]`).scrollIntoViewIfNeeded();
      await page.waitForLoadState('networkidle');
      await page.waitForFunction(() => [...document.querySelectorAll('main img')].filter((image) => {
        const bounds = image.getBoundingClientRect();
        return bounds.width > 0 && bounds.bottom > 0 && bounds.top < innerHeight;
      }).every((image) => image.complete && image.naturalWidth > 0));
      await screenshot(`gui-full-mixed-${section}-${view}.png`, page, false);
    }
    const movie = gui.generatedMoviesAdded.find((item) => item.source === 'tmdbMovie');
    await page.goto(input.baseUrl + `/series/${movie.titleSlug}?season=1#season-1`);
    await page.locator('#season-1 table tbody tr').waitFor({ state: 'visible' });
    await page.locator('#season-1').getByText('Movie', { exact: true }).waitFor({ state: 'visible' });
    assert.equal(await page.locator('#season-1').getByText('Season 1', { exact: true }).count(), 0);
    await screenshot(`gui-native-movie-${view}.png`, page, false);
    await page.locator('#season-1').getByRole('button', { name: 'Search Movie', exact: true }).click();
    await page.getByText(`${movie.title}.${movie.year}.1080p.WEB-DL.H264-Fixture`, { exact: true }).first().waitFor({ state: 'visible' });
    await modalFits(page);
    await screenshot(`gui-movie-search-${view}.png`, page);
    await closeModal(page);
  }
  for (const [view, page] of [['desktop', settingsPage], ['mobile', addMobilePage]]) {
    diagnostic(page);
    await providerDialog(page, '/settings/indexers', 'Add Indexer', 'indexer/schema', 'Newznab', 'newznab-movie-categories', view);
    await providerDialog(page, '/settings/indexers', 'Add Indexer', 'indexer/schema', 'Torznab', 'torznab-movie-categories', view);
  }
  if (input.guiImportMovie) {
    const fixture = input.guiImportMovie;
    const root = (await api('rootfolder')).find((item) => item.path === '/media/explicit');
    assert.ok(root, 'the generated import root exists');
    for (const [view, page] of [['desktop', settingsPage], ['mobile', addMobilePage]]) {
      diagnostic(page);
      await page.goto(input.baseUrl + '/add/import/' + root.id);
      await settled(page);
      const source = page.locator('select[name="importMetadataSource"]');
      const choices = await source.locator('option').evaluateAll((options) => options.map((option) => option.value));
      assert.equal(await source.locator('option[value="all"]').innerText(), 'All enabled sources', 'the aggregate source choice uses readable localized text');
      assert.ok(choices.includes('all') && choices.includes(fixture.source) && !choices.includes('tvdb'), 'Library Import respects disabled sources and offers all enabled catalogues');
      await source.selectOption(fixture.source);
      const card = page.locator('[data-import-folder]').filter({ hasText: fixture.title });
      await card.getByRole('button', { name: 'Find Series', exact: true }).click();
      const result = page.getByRole('grid').getByRole('row').filter({ hasText: fixture.title });
      await result.waitFor({ state: 'visible' });
      assert.equal(await result.getByRole('link', { name: 'TMDB Movies ' + fixture.foreignId }).getAttribute('href'), 'https://www.themoviedb.org/movie/' + fixture.foreignId);
      const matchGeometry = await result.evaluate((row) => {
        const cell = row.querySelector('[class*="ImportSeriesSearchResult"][class*="_seriesCell_"]').getBoundingClientRect();
        const title = row.querySelector('[class*="ImportSeriesTitle"][class*="_title_"]');
        const year = row.querySelector('[class*="ImportSeriesTitle"][class*="_year_"]');
        const range = document.createRange();
        range.selectNodeContents(title);
        return { title: title.textContent, year: year.textContent, clip: cell.toJSON(),
          titleRects: [...range.getClientRects()].map((rect) => rect.toJSON()), yearBounds: year.getBoundingClientRect().toJSON() };
      });
      assert.equal(matchGeometry.title, fixture.title);
      assert.equal(matchGeometry.year, '(' + fixture.year + ')');
      assert.ok([...matchGeometry.titleRects, matchGeometry.yearBounds].every((box) => box.left >= matchGeometry.clip.left - 1 && box.right <= matchGeometry.clip.right + 1 && box.top >= matchGeometry.clip.top - 1 && box.bottom <= matchGeometry.clip.bottom + 1), 'the full movie title and disambiguating year are readable inside their result cell');
      gui.importMatchGeometry ||= [];
      gui.importMatchGeometry.push({ view, ...matchGeometry });
      await screenshot(`gui-library-import-movie-match-${view}.png`, page, false);
      await result.getByRole('button').click();
      await card.getByTitle('Change match', { exact: true }).waitFor({ state: 'visible' });
      for (const name of ['Monitored', 'Any', 'English']) {
        await card.getByRole('button', { name, exact: true }).waitFor({ state: 'visible' });
      }
      for (const name of ['seriesType', 'seasonType', 'seasonFolder']) assert.equal(await card.locator(`[name="${name}"]`).count(), 0, 'matched movies hide episodic import controls');
      const bounds = await geometry(page);
      assert.deepEqual(bounds.clippedFields, []);
      await screenshot(`gui-library-import-movie-ready-${view}.png`, page, false);
      if (view === 'mobile') {
        const importButton = page.getByRole('button', { name: 'Import 1 Series', exact: true });
        await importButton.scrollIntoViewIfNeeded();
        const actionBounds = await importButton.boundingBox();
        const navigationBounds = await page.locator('nav[aria-label="Main Navigation"]').boundingBox();
        assert.ok(actionBounds && actionBounds.y >= 0 && actionBounds.y + actionBounds.height <= navigationBounds.y + 1, 'the complete touch Import action scrolls above the fixed mobile navigation');
        gui.importActionBounds = { action: actionBounds, navigation: navigationBounds };
        await screenshot('gui-library-import-movie-actions-mobile.png', page, false);
        const saved = page.waitForResponse((response) => response.request().method() === 'POST' && new URL(response.url()).pathname === '/api/v5/series/import');
        await importButton.click();
        const response = await saved;
        assert.equal(response.ok(), true, 'the real native library import succeeds');
        const [added] = await response.json();
        assert.equal(added.metadataSource, fixture.source);
        assert.equal(added.foreignId, fixture.foreignId);
        assert.equal(added.mediaType, 'movie');
        assert.equal(added.path, fixture.path);
        assert.equal(added.seasonFolder, false);
        assert.ok(added.tvdbId < 0);
        let episodes = [];
        for (let attempt = 0; attempt < 150; attempt++) {
          episodes = await api('episode?seriesId=' + added.id);
          if (episodes.length === 1 && episodes[0].hasFile) break;
          await new Promise((resolve) => setTimeout(resolve, 200));
        }
        assert.deepEqual(episodes.map((episode) => [episode.seasonNumber, episode.episodeNumber, episode.hasFile]), [[1, 1, true]], 'the existing generated film imports under its owning catalogue native1/E1');
        gui.libraryImportedMovie = { id: added.id, source: fixture.source, foreignId: fixture.foreignId, path: added.path, episodeFileId: episodes[0].episodeFileId };
        gui.generatedMoviesAdded.push({ ...gui.libraryImportedMovie, mediaType: 'movie', title: fixture.title, year: fixture.year, titleSlug: added.titleSlug });
        await page.reload();
        await settled(page);
        await page.getByText('All series in ' + root.path + ' have been imported', { exact: true }).waitFor({ state: 'visible' });
        await screenshot('gui-library-import-movie-completed-mobile.png', page);
      }
    }
    checks.push('actual desktop and touch Library Import select an enabled native movie catalogue, retain the real provider ID and import the generated film file without TVDB identity substitution');
  }
  await addMobileContext.close();
  await settingsContext.close();
  checks.push('every main page and all Settings submenus render on desktop and actual touch mobile with bounded forms and screenshots');
  checks.push('keyboard navigation through lazy routes preserves the running application and focus in persistent desktop and touch navigation');
  checks.push('real indexer, download client, import list, Jellyfin, metadata exporter, custom format and naming editors open with functional fields and reachable actions without configuring external services');
  checks.push('library import opens a functioning file browser and the owned generated root import workflow without importing unknown media');
  checks.push('UI time format saves, survives reload and is restored through the actual packaged settings API');
  checks.push('actual desktop and touch Add forms create TMDB/IMDb-resolved movies, an OMDb movie and two independent specials while preserving native provider identities');
  checks.push('manual mixed collection combines a multi-season TV member, anime, movies and multiple specials without changing catalogue season or episode numbers');
  checks.push('native movie pages and interactive searches use movie labels and return unnumbered feature releases on desktop and touch mobile');
  checks.push('Newznab and Torznab editors expose independent movie category2000 while retaining TV5030/5040 on desktop and touch mobile');
  return gui.generatedMoviesAdded;
}

module.exports = { auditGuiWorkflows };
