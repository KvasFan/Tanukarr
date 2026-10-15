const assert = require('node:assert/strict');
const fs = require('node:fs');
const Module = require('node:module');
const path = require('node:path');
const test = require('node:test');
const PropTypes = require('prop-types');
const React = require('react');
const TestRenderer = require('react-test-renderer');
const ts = require('typescript');

const projectRequire = Module.createRequire(path.resolve(__dirname, '../../package.json'));

function loadSource(relative, mocks = {}) {
  const filename = path.resolve(__dirname, '../src', relative);
  const output = ts.transpileModule(fs.readFileSync(filename, 'utf8'), {
    compilerOptions: {
      module: ts.ModuleKind.CommonJS,
      target: ts.ScriptTarget.ES2020,
      jsx: ts.JsxEmit.React,
      esModuleInterop: true
    }
  }).outputText;
  const compiled = new Module(filename, module);
  compiled.paths = module.paths;

  compiled.require = (name) => {
    if (Object.hasOwn(mocks, name)) {
      return mocks[name];
    }
    if (name === 'Series/getMediaType') return loadSource('Series/getMediaType.ts');

    return projectRequire(name);
  };

  // eslint-disable-next-line no-underscore-dangle
  compiled._compile(output, filename);
  return compiled.exports;
}

const grouping = loadSource('Series/Grouping/animeGrouping.ts');
const presentation = loadSource('Series/Grouping/collectionPresentation.ts', { './animeGrouping': grouping });
const { groupAnimeSeries, hasAnimeGroup } = grouping;

test('multi-season members reserve consecutive logical slots independently of movies and specials', () => {
  const tv = { id: 1, animeGroupSeasonNumber: 1, seasons: [{ seasonNumber: 0 }, { seasonNumber: 2 }, { seasonNumber: 4 }] };
  const sequel = { id: 2, animeGroupSeasonNumber: 4, seasons: [{ seasonNumber: 1 }] };
  const special = { id: 3, animeGroupMemberKind: 'special', animeGroupSpecialNumber: 1 };
  assert.equal(grouping.getAvailableGroupMemberNumber([tv, sequel, special], 'season'), 3);
  assert.equal(grouping.getAvailableGroupMemberNumber([tv, sequel, special], 'season', undefined, 2), 5);
  assert.equal(grouping.getAvailableGroupMemberNumber([tv, sequel, special], 'movie'), 1);
  assert.equal(grouping.getAvailableGroupMemberNumber([tv, sequel, special], 'special'), 2);
});

function makeSeries(id, season, groupId = 'anilist:10', overrides = {}) {
  return {
    id, title: `Generated entry ${id}`, sortTitle: `generated entry ${id}`,
    metadataSource: 'aniList', foreignId: String(id), tvdbId: -id,
    seriesType: 'standard', images: [], tags: [],
    seasons: [{ seasonNumber: 1, statistics: { episodeCount: 12, totalEpisodeCount: 12 } }],
    animeGroupingMode: 'automatic', animeGroupId: groupId,
    animeGroupTitle: 'Generated chain', animeGroupSeasonNumber: season,
    statistics: { episodeCount: 12, episodeFileCount: season },
    ...overrides
  };
}

test('group projection preserves input order, native identity and every season', () => {
  const items = [makeSeries(2, 2), makeSeries(50, null, null), makeSeries(1, 1), makeSeries(3, 3)];
  const result = groupAnimeSeries(items, true);
  assert.deepEqual(result.displayItems.map((item) => item.id), [2, 50]);
  assert.deepEqual(result.groupsByRepresentativeId.get(2).members.map((item) => item.id), [1, 2, 3]);
  assert.equal(result.displayItems[0], items[0]);
  assert.deepEqual(items.map((item) => item.id), [2, 50, 1, 3], 'projection does not mutate filtered source');
});

test('grouping never merges equal titles or equal foreign IDs owned by different catalogues', () => {
  const items = [makeSeries(1, 1), makeSeries(2, 1, 'myanimelist:10', { metadataSource: 'myAnimeList', foreignId: '1' }), makeSeries(3, null, null)];
  const result = groupAnimeSeries(items, true);
  assert.equal(result.displayItems.length, 3);
  assert.equal(result.groupsByRepresentativeId.size, 2);
});

test('filtering before grouping includes only visible members and retains a singleton season badge', () => {
  const result = groupAnimeSeries([makeSeries(3, 3)], true);
  assert.equal(result.groupsByRepresentativeId.get(3).members.length, 1);
  assert.equal(result.groupsByRepresentativeId.get(3).members[0].animeGroupSeasonNumber, 3);
});

test('disabled, empty and invalid group seasons remain standalone', () => {
  for (const season of [null, 0, -1, 1.2, 1000]) {
    assert.equal(hasAnimeGroup(makeSeries(1, season)), false);
  }

  assert.equal(hasAnimeGroup(makeSeries(1, 1, 'anilist:10', { animeGroupingMode: 'disabled' })), false);
  assert.equal(hasAnimeGroup(makeSeries(1, 1, 'anilist:10', { animeGroupTitle: ' ' })), false);
  const items = [makeSeries(1, 1), makeSeries(2, 2)];
  assert.deepEqual(groupAnimeSeries(items, false).displayItems, items);
  assert.equal(groupAnimeSeries(items, false).groupsByRepresentativeId.size, 0);
});

function wrapper(name) {
  function MockContainer({ children }) {
    return React.createElement('section', { 'data-component': name }, children);
  }

  MockContainer.propTypes = { children: PropTypes.node };
  return MockContainer;
}

function createIndexFixture(view, initialItems = [makeSeries(1, 1), makeSeries(2, 2), makeSeries(3, 3), makeSeries(4, null, null)]) {
  const actions = [];
  let items = initialItems;
  let options = { selectedFilterKey: 'all', sortKey: 'sortTitle', sortDirection: 'ascending', view, groupAnimeSeasons: true, columns: [], pathFilter: '' };
  const listeners = new Set();

  const subscribe = (listener) => {
    listeners.add(listener);
    return () => listeners.delete(listener);
  };

  const notify = () => listeners.forEach((listener) => listener());
  const noop = () => undefined;
  const scroller = { addEventListener: noop, removeEventListener: noop, scrollTo: noop, clientWidth: 1200, scrollTop: 0, offsetTop: 0 };
  global.window = { innerWidth: 1200, innerHeight: 800, addEventListener: noop, removeEventListener: noop };
  global.document = { documentElement: { scrollTop: 0 }, body: { scrollTop: 0 } };

  const store = loadSource('App/Select/useSelectStore.ts', {
    'Utilities/Table/getToggledRange': () => {
      throw new Error('fixture does not issue shift selection');
    }
  }).default;
  const context = loadSource('App/Select/SelectContext.tsx', { 'App/Select/useSelectStore': store });
  const translate = (key, args) => `${key}${args ? JSON.stringify(args) : ''}`;
  const nativeEntry = (props) => React.createElement('article', { 'data-native-series': props.seriesId },
    React.createElement('a', { href: `/series/generated-${props.seriesId}` }, `Native entry ${props.seriesId}`));
  const seasonSearch = loadSource('Series/Search/SeasonReleaseSearchButton.tsx', {
    'Components/Link/Button': (props) => React.createElement('button', { disabled: props.isDisabled, onClick: props.onPress }, props.children),
    'Helpers/Props': { sizes: { SMALL: 'small' } },
    'Utilities/String/translate': translate,
    './SeasonInteractiveSearchModal': (props) => (props.isOpen ? React.createElement('output', { 'data-season-search-series': props.seriesId, 'data-season-search-native-number': props.seasonNumber }) : null)
  }).default;
  const summary = loadSource('Series/Grouping/AnimeGroupSummary.tsx', {
    'App/Select/SelectContext': context,
    'Components/Form/CheckInput': (props) => React.createElement('input', {
      'data-group-selection': props.name, type: 'checkbox', checked: props.value === true,
      onChange: (event) => props.onChange({ name: props.name, value: event.target.checked, shiftKey: false })
    }),
    'Components/Link/Link': (props) => React.createElement('a', { href: props.to }, props.children),
    './CollectionPoster': (props) => React.createElement('img', { alt: props.title }),
    './collectionPresentation': presentation,
    'Components/Modal/Modal': (props) => (props.isOpen ? React.createElement('aside', {}, props.children) : null),
    'Components/Modal/ModalContent': wrapper('modal'),
    'Components/Modal/ModalHeader': wrapper('header'),
    'Components/Modal/ModalBody': wrapper('body'),
    'Components/Modal/ModalFooter': wrapper('footer'),
    'Series/Index/Overview/SeriesIndexOverview': nativeEntry,
    'Series/SeriesPoster': (props) => React.createElement('img', { alt: props.title }),
    'Series/Search/SeasonReleaseSearchButton': seasonSearch,
    'Utilities/String/translate': translate,
    './AnimeGroupSummary.module.css': {}
  });
  const virtual = React.forwardRef((props, ref) => {
    React.useImperativeHandle(ref, () => ({ scrollTo: noop }));
    const Row = props.children;
    const count = props.itemCount ?? props.rowCount * props.columnCount;
    return React.createElement('div', { 'data-virtual-count': count }, Array.from({ length: count }, (_, index) =>
      React.createElement(Row, { key: index, index, rowIndex: Math.floor(index / (props.columnCount ?? 1)), columnIndex: index % (props.columnCount ?? 1), data: props.itemData, style: {} })));
  });
  virtual.displayName = 'GeneratedVirtualList';
  virtual.propTypes = {
    children: PropTypes.func, itemCount: PropTypes.number, rowCount: PropTypes.number,
    columnCount: PropTypes.number, itemData: PropTypes.object
  };
  const virtualTable = (props) => React.createElement(virtual, { ref: props.listRef, itemCount: props.itemCount, itemData: props.itemData }, props.Row);
  const bounds = { width: 1200, height: 800 };
  const dimensions = { pageContentBodyPadding: 20, pageContentBodyPaddingSmallScreen: 10, seriesIndexColumnPadding: 5, seriesIndexColumnPaddingSmallScreen: 5, progressBarSmallHeight: 10, progressBarMediumHeight: 15 };
  const optionHooks = {
    useSeriesOptions: () => React.useSyncExternalStore(subscribe, () => options),
    setSeriesOption: (name, value) => {
      options = { ...options, [name]: value };
      notify();
    },
    setSeriesSort: noop, setSeriesTableOptions: noop,
    useSeriesPosterOptions: () => ({ size: 'large' }),
    useSeriesOverviewOptions: () => ({ size: 'medium' }),
    useSeriesTableOptions: () => ({ showBanners: false }),
    useSeriesOption: () => []
  };
  const viewMocks = {
    'react-window': { FixedSizeList: virtual, FixedSizeGrid: virtual },
    'Helpers/Hooks/useMeasure': () => [() => {}, bounds],
    'Helpers/Props/sortDirections': { DESCENDING: 'descending' },
    'Series/Grouping/AnimeGroupSummary': summary,
    'Series/seriesOptionsStore': optionHooks,
    'Styles/Variables/dimensions': dimensions,
    'Utilities/Array/getIndexOfFirstCharacter': () => 0,
    'Series/Index/Posters/SeriesIndexPoster': nativeEntry,
    './SeriesIndexOverview': nativeEntry,
    './SeriesIndexRow': nativeEntry,
    './SeriesIndexTableHeader': wrapper('table-header'),
    './overviewLayout': { ROW_BORDER: 1, ROW_VERTICAL_PADDING: 10 },
    'Components/Table/VirtualTable': virtualTable,
    './SeriesIndexTable.module.css': {}
  };
  const posters = loadSource('Series/Index/Posters/SeriesIndexPosters.tsx', viewMocks).default;
  const overview = loadSource('Series/Index/Overview/SeriesIndexOverviews.tsx', viewMocks).default;
  const table = loadSource('Series/Index/Table/SeriesIndexTable.tsx', viewMocks).default;
  const pageBody = React.forwardRef((props, ref) => {
    React.useImperativeHandle(ref, () => scroller);
    return React.createElement('main', {}, props.children);
  });
  pageBody.displayName = 'GeneratedPageBody';
  pageBody.propTypes = { children: PropTypes.node };
  const mocks = {
    'Activity/Queue/Details/QueueDetailsProvider': wrapper('queue'),
    'App/appStore': { useAppDimension: () => false },
    'App/Select/SelectContext': context,
    'Commands/CommandNames': { RefreshSeries: 'RefreshSeries', RssSync: 'RssSync' },
    'Commands/useCommands': { useCommandExecuting: () => false, useExecuteCommand: () => (payload) => actions.push(payload) },
    'Components/Alert': wrapper('alert'),
    'Components/Loading/LoadingIndicator': wrapper('loading'),
    'Components/Page/PageContent': wrapper('page'),
    'Components/Page/PageContentBody': pageBody,
    'Components/Page/PageJumpBar': wrapper('jumpbar'),
    'Components/Page/Toolbar/Overflow': { OverflowDivider: wrapper('divider') },
    'Components/Page/Toolbar/PageToolbar': wrapper('toolbar'),
    'Components/Page/Toolbar/PageToolbarSeparator': wrapper('separator'),
    'Components/Page/Toolbar/PageToolbarSpacer': wrapper('spacer'),
    'Components/Page/Toolbar/ToolbarItem': (props) => React.createElement('button', { 'data-toolbar-id': props.id, onClick: props.onPress }, props.label, props.children),
    'Components/Table/TableOptions/TableOptionsModal': () => null,
    'Filters/useCustomFilters': { useCustomFiltersList: () => [] },
    'Helpers/Props': { icons: {}, kinds: {} },
    'Helpers/Props/sortDirections': { DESCENDING: 'descending' },
    'Series/NoSeries': wrapper('empty'),
    'Series/Grouping/animeGrouping': grouping,
    'Series/seriesOptionsStore': optionHooks,
    'Series/useSeries': { FILTERS: [], useSeriesIndex: () => {
      const data = React.useSyncExternalStore(subscribe, () => items);
      return { isLoading: false, isFetched: true, isError: false, data, totalItems: data.length };
    } },
    'Utilities/String/translate': translate,
    './Posters/SeriesIndexPosters': posters,
    './Overview/SeriesIndexOverviews': overview,
    './Table/SeriesIndexTable': table,
    './SeriesIndex.module.css': {}
  };

  for (const name of [
    './Menus/SeriesIndexFilterMenu', './Menus/SeriesIndexSortMenu', './Menus/SeriesIndexViewMenu',
    './Overview/Options/SeriesIndexOverviewOptionsModal', './Posters/Options/SeriesIndexPosterOptionsModal',
    './Select/SeriesIndexSelectAllButton', './Select/SeriesIndexSelectAllMenuItem', './Select/SeriesIndexSelectFooter',
    './Select/SeriesIndexSelectModeButton', './Select/SeriesIndexSelectModeMenuItem', './SeriesIndexFooter',
    './SeriesPathFilter', './Table/SeriesIndexTableOptions'
  ]) {
    mocks[name] = wrapper(name);
  }

  const Component = loadSource('Series/Index/SeriesIndex.tsx', mocks).default;
  let renderer = null;
  TestRenderer.act(() => {
    renderer = TestRenderer.create(React.createElement(Component));
  });
  return {
    renderer, actions,
    press(id) {
      TestRenderer.act(() => renderer.root.findByProps({ 'data-toolbar-id': id }).props.onClick());
    },
    selectGroup(value) {
      TestRenderer.act(() => renderer.root.findByProps({ 'data-group-selection': 'anime-group-anilist:10' }).props.onChange({ target: { checked: value } }));
    },
    filter(next) {
      TestRenderer.act(() => {
        items = next;
        notify();
      });
    },
    groupLink() {
      return renderer.root.findAllByType('a').find((item) => item.children.includes('Generated chain')).props.href;
    },
    cleanup() {
      TestRenderer.act(() => renderer.unmount());
    }
  };
}

test('movie ordinals are independent of seasons and mixed groups order seasons before movies', () => {
  const movie = makeSeries(8, null, 'anilist:10', { animeGroupMemberKind: 'movie', animeGroupMovieNumber: 1, title: 'Generated movie' });
  const season = makeSeries(2, 1);
  assert.equal(hasAnimeGroup(movie), true);
  assert.equal(hasAnimeGroup({ ...movie, animeGroupMovieNumber: null, animeGroupSeasonNumber: 1 }), false);
  const projected = groupAnimeSeries([movie, season], true);
  assert.deepEqual(projected.groupsByRepresentativeId.get(8).members.map((member) => member.id), [2, 8]);
  assert.equal(grouping.getAvailableGroupMemberNumber([movie, season], 'movie'), 2);
  assert.equal(grouping.getAvailableGroupMemberNumber([movie, season], 'season'), 2);
});

for (const view of ['posters', 'overview', 'table']) {
  test(`mounted ${view} group opens the dedicated collection route for arc entries`, (context) => {
    const fixture = createIndexFixture(view, [makeSeries(1, 1), makeSeries(2, 2, 'anilist:10', { title: 'Tokyo Revengers: War Of Three Titans Arc' })]);
    context.after(() => fixture.cleanup());
    assert.equal(fixture.groupLink(), '/collections/group%3Aanilist%3A10');
    assert.equal(fixture.renderer.root.findAllByType('aside').length, 0, 'collection navigation does not open a modal');
  });
  test(`mounted ${view} mixed group includes movie and season counts`, (context) => {
    const fixture = createIndexFixture(view, [makeSeries(2, 2), makeSeries(8, null, 'anilist:10', { animeGroupMemberKind: 'movie', animeGroupMovieNumber: 1, title: 'Generated movie' })]);
    context.after(() => fixture.cleanup());
    assert.match(JSON.stringify(fixture.renderer.toJSON()), /AnimeGroupMemberCounts.*seasons.*1.*movies.*1/);
    assert.equal(fixture.groupLink(), '/collections/group%3Aanilist%3A10');
  });
}

for (const view of ['posters', 'overview', 'table']) {
  test(`mounted ${view} index shows one collection link plus a standalone native entry`, (context) => {
    const fixture = createIndexFixture(view);
    context.after(() => fixture.cleanup());
    assert.equal(fixture.renderer.root.findAllByProps({ 'data-anime-group': 'anilist:10' }).length, 1);
    assert.equal(fixture.renderer.root.findAllByProps({ 'data-native-series': 4 }).length, 1);
    assert.equal(fixture.groupLink(), '/collections/group%3Aanilist%3A10');
    assert.ok(fixture.renderer.root.findAllByType('a').some((item) => item.props.href === '/series/generated-4'));
  });

  test(`mounted ${view} group selection refreshes all visible native IDs and preserves filtering`, (context) => {
    const fixture = createIndexFixture(view);
    context.after(() => fixture.cleanup());
    fixture.press('select');
    fixture.selectGroup(true);
    fixture.press('refresh');
    assert.deepEqual(fixture.actions.at(-1).seriesIds, [1, 2, 3]);
    fixture.filter([makeSeries(2, 2), makeSeries(4, null, null)]);
    fixture.press('refresh');
    assert.deepEqual(fixture.actions.at(-1).seriesIds, [2]);
    fixture.selectGroup(false);
    fixture.press('refresh');
    assert.deepEqual(fixture.actions.at(-1).seriesIds, [2, 4]);
    assert.match(JSON.stringify(fixture.renderer.toJSON()), /AnimeGroupSeasonLabel.*2/);
    fixture.press('anime-groups');
    assert.equal(fixture.renderer.root.findAllByProps({ 'data-anime-group': 'anilist:10' }).length, 0);
    assert.deepEqual(fixture.renderer.root.findAllByType('article').map((item) => item.props['data-native-series']), [2, 4]);
  });
}
