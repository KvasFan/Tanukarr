const assert = require('node:assert/strict');
const fs = require('node:fs');
const Module = require('node:module');
const path = require('node:path');
const test = require('node:test');
const PropTypes = require('prop-types');
const React = require('react');
const TestRenderer = require('react-test-renderer');
const ts = require('typescript');
const Query = require('@tanstack/react-query');

const projectRequire = Module.createRequire(path.resolve(__dirname, '../../package.json'));

function load(relative, mocks = {}) {
  const filename = path.resolve(__dirname, '../src', relative);
  const output = ts.transpileModule(fs.readFileSync(filename, 'utf8'), { fileName: filename, compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2020, jsx: ts.JsxEmit.React, esModuleInterop: true } }).outputText;
  const compiled = new Module(filename, module);
  compiled.paths = module.paths;
  compiled.require = (name) => (Object.hasOwn(mocks, name) ? mocks[name] : projectRequire(name));
  // eslint-disable-next-line no-underscore-dangle
  compiled._compile(output, filename);
  return compiled.exports;
}

const grouping = load('Series/Grouping/animeGrouping.ts');
const presentation = load('Series/Grouping/collectionPresentation.ts', { './animeGrouping': grouping });
const mergeSeriesUpdate = load('Series/mergeSeriesUpdate.ts').default;
const translate = (key, args) => `${key}${args ? JSON.stringify(args) : ''}`;
const wrap = ({ children }) => React.createElement('section', {}, children);
const body = React.forwardRef(({ children }, ref) => React.createElement('section', { ref }, children));
body.propTypes = { children: PropTypes.node };
const button = (props) => React.createElement('button', { disabled: props.isDisabled, onClick: props.onPress }, props.children);
const modal = ({ children, isOpen }) => (isOpen ? React.createElement('aside', {}, children) : null);
const link = (props) => React.createElement('a', { href: props.to }, props.children);
const common = {
  'Components/Alert': wrap,
  'Components/Icon': () => null,
  'Components/Link/Button': button,
  'Components/Link/Link': link,
  'Components/Link/SpinnerErrorButton': button,
  'Components/Modal/Modal': modal,
  'Components/Modal/ModalBody': body,
  'Components/Modal/ModalContent': wrap,
  'Components/Modal/ModalHeader': wrap,
  'Components/Modal/ModalFooter': wrap,
  'Components/Form/Form': wrap,
  'Components/Form/FormRow': wrap,
  'Components/Form/FormLabel': wrap,
  'Components/Form/TextInput': (props) => React.createElement('input', { name: props.name, value: props.value, onChange: (event) => props.onChange({ name: props.name, value: event.target.value }) }),
  'Components/Form/SelectInput': (props) => React.createElement('select', { name: props.name, value: props.value, onChange: (event) => props.onChange({ name: props.name, value: event.target.value }) }, props.values.map((option) => React.createElement('option', { key: option.key, value: option.key, disabled: option.disabled }, typeof option.value === 'function' ? option.value() : option.value))),
  'Components/Form/CheckInput': (props) => React.createElement('input', { name: props.name, type: 'checkbox', checked: props.value, onChange: (event) => props.onChange({ value: event.target.checked }) }),
  'Helpers/Props': { align: { RIGHT: 'right' }, kinds: { DANGER: 'danger', INFO: 'info', PRIMARY: 'primary' }, sizes: { EXTRA_EXTRA_LARGE: 'xxlarge' }, icons: {} },
  'Utilities/String/translate': translate,
  'react-lazyload': { forceCheck: () => undefined },
  'Helpers/Hooks/useApiQuery': () => ({ data: undefined }),
  './collectionPresentation': presentation,
  './CollectionPage.module.css': {}
};

function member(id, ordinal, overrides = {}) {
  return {
    id, title: `Native entry ${id}`, titleSlug: `native-${id}`, images: [], genres: ['Generated genre'], overview: `Overview ${id}`, year: 2026,
    animeGroupId: 'anilist:900', animeGroupTitle: 'Generated Collection', animeGroupSeasonNumber: ordinal, animeGroupMemberKind: 'season', animeGroupKind: 'series', animeGroupingMode: 'automatic',
    seasons: [{ seasonNumber: 1, monitored: true, statistics: { totalEpisodeCount: 12, episodeFileCount: 2 } }],
    ...overrides
  };
}

function mount(Component, props) {
  let renderer = null;
  TestRenderer.act(() => {
    renderer = TestRenderer.create(React.createElement(Component, props));
  });
  return renderer;
}

function press(renderer, text) {
  const target = renderer.root.findAllByType('button').find((item) => item.children.includes(text));
  assert.ok(target, `button ${text} is present`);
  TestRenderer.act(() => target.props.onClick());
}

function releaseFilteringFixture() {
  const globalOptions = { seasonSelectedFilterKey: 'season-pack', episodeSelectedFilterKey: 'all', rejectionFilterTags: [] };
  const writes = [];
  const filterTypes = load('Helpers/Props/filterTypes.ts');
  const sortDirections = load('Helpers/Props/sortDirections.ts');
  const getFilterTypePredicate = load('Helpers/Props/getFilterTypePredicate.ts').default;
  const clientSideFilterAndSort = load('Utilities/Filter/clientSideFilterAndSort.ts', {
    'Helpers/Props': { filterTypes, sortDirections },
    'Helpers/Props/getFilterTypePredicate': getFilterTypePredicate,
    'Utilities/Filter/findSelectedFilters': load('Utilities/Filter/findSelectedFilters.ts').default
  }).default;
  const releases = [
    { id: 1, parsedInfo: { fullSeason: true }, release: { title: 'Generated season pack' }, decision: { rejections: [] }, releaseWeight: 1 },
    { id: 2, parsedInfo: { fullSeason: false }, release: { title: 'Generated individual episode' }, decision: { rejections: [] }, releaseWeight: 2 }
  ];
  const options = {
    useReleaseOptions: () => globalOptions,
    getReleaseOption: (key) => globalOptions[key],
    setReleaseOption: (key, value) => {
      writes.push({ key, value });
      globalOptions[key] = value;
    }
  };
  const hooks = load('InteractiveSearch/useReleases.ts', {
    'Filters/useCustomFilters': { useCustomFiltersList: () => [] },
    'Helpers/Hooks/useApiMutation': () => undefined,
    'Helpers/Hooks/useApiQuery': () => ({ data: releases, isFetching: false, isFetched: true }),
    'Helpers/Hooks/useOptionsStore': { applySort: () => undefined },
    'Helpers/Props': { filterBuilderTypes: {}, filterBuilderValueTypes: {} },
    'Helpers/Props/getFilterTypePredicate': getFilterTypePredicate,
    'Utilities/Array/sortByProp': load('Utilities/Array/sortByProp.ts').default,
    'Utilities/Filter/clientSideFilterAndSort': clientSideFilterAndSort,
    'Utilities/String/enumToTitle': load('Utilities/String/enumToTitle.ts').default,
    'Utilities/String/translate': translate,
    './releaseOptionsStore': options
  });
  return { globalOptions, writes, options, hooks };
}

test('collection episode search filters actual releases locally and leaves existing season and episode preferences intact', () => {
  const fixture = releaseFilteringFixture();

  function FilterMenu(props) {
    return React.createElement('nav', { 'data-selected-filter': props.selectedFilterKey });
  }

  FilterMenu.propTypes = { selectedFilterKey: PropTypes.oneOfType([PropTypes.string, PropTypes.number]) };

  const List = React.forwardRef((props, ref) => {
    React.useImperativeHandle(ref, () => ({ resetAfterIndex: () => undefined, scrollTo: () => undefined }));
    return React.createElement('div', {}, Array.from({ length: props.itemCount }, (_, index) => React.createElement(props.children, { key: index, index, style: {}, data: props.itemData })));
  });
  List.propTypes = { itemCount: PropTypes.number, children: PropTypes.func, itemData: PropTypes.object };
  const Component = load('InteractiveSearch/InteractiveSearch.tsx', {
    ...common,
    'react-window': { VariableSizeList: List },
    'Components/Loading/LoadingIndicator': () => null,
    'Components/Menu/FilterMenu': FilterMenu,
    'Components/Menu/PageMenuButton': button,
    'Filters/useCustomFilters': { useCustomFiltersList: () => [] },
    'Helpers/Hooks/useMeasure': () => [() => undefined, { width: 800 }],
    'Utilities/Object/getErrorMessage': () => '',
    './InteractiveSearchFilterModal': () => null,
    './InteractiveSearchRow': (props) => React.createElement('output', {}, props.release.title),
    './releaseOptionsStore': fixture.options,
    './useReleases': fixture.hooks,
    './InteractiveSearch.module.css': {}
  }).default;
  const props = { type: 'season', searchPayload: { seriesId: 42, seasonNumber: 1 }, scrollerRef: { current: null } };
  let renderer = mount(Component, { ...props, initialSelectedFilterKey: 'not-season-pack' });
  assert.deepEqual(renderer.root.findAllByType('output').map((item) => item.children.join('')), ['Generated individual episode']);
  TestRenderer.act(() => renderer.root.findByType(FilterMenu).props.onFilterSelect('season-pack'));
  assert.deepEqual(renderer.root.findAllByType('output').map((item) => item.children.join('')), ['Generated season pack']);
  TestRenderer.act(() => renderer.root.findByType(FilterMenu).props.onFilterSelect('all'));
  assert.equal(renderer.root.findAllByType('output').length, 2);
  assert.equal(fixture.globalOptions.seasonSelectedFilterKey, 'season-pack');
  assert.equal(fixture.globalOptions.episodeSelectedFilterKey, 'all');
  assert.deepEqual(fixture.writes.filter((item) => item.key.endsWith('SelectedFilterKey')), []);
  TestRenderer.act(() => renderer.unmount());
  renderer = mount(Component, { ...props, initialSelectedFilterKey: 'not-season-pack' });
  assert.deepEqual(renderer.root.findAllByType('output').map((item) => item.children.join('')), ['Generated individual episode'], 'reopening restores the caller-specific initial filter');
  TestRenderer.act(() => renderer.unmount());
  renderer = mount(Component, props);
  assert.deepEqual(renderer.root.findAllByType('output').map((item) => item.children.join('')), ['Generated season pack'], 'existing native season search still uses its saved preference');
  TestRenderer.act(() => renderer.root.findByType(FilterMenu).props.onFilterSelect('all'));
  assert.equal(fixture.globalOptions.seasonSelectedFilterKey, 'all', 'unscoped search still saves its global preference');
  TestRenderer.act(() => renderer.unmount());
});

test('custom filter selections in a collection episode modal remain local while native modal selections persist', () => {
  const fixture = releaseFilteringFixture();
  const local = [];

  function FilterModal(props) {
    return React.createElement('section', { 'data-filter-count': props.sectionItems.length });
  }

  FilterModal.propTypes = { sectionItems: PropTypes.array };

  const Component = load('InteractiveSearch/InteractiveSearchFilterModal.tsx', {
    'Components/Filter/FilterModal': FilterModal,
    './releaseOptionsStore': fixture.options,
    './useReleases': fixture.hooks
  }).default;
  let renderer = mount(Component, { type: 'season', searchPayload: { seriesId: 42, seasonNumber: 1 }, localFilterKey: 'not-season-pack', onLocalFilterSelect: (key) => local.push(key) });
  assert.deepEqual(renderer.root.findByType(FilterModal).props.sectionItems.map((item) => item.id), [2]);
  TestRenderer.act(() => renderer.root.findByType(FilterModal).props.dispatchSetFilter({ selectedFilterKey: 900 }));
  assert.deepEqual(local, [900]);
  assert.deepEqual(fixture.writes.filter((item) => item.key.endsWith('SelectedFilterKey')), []);
  TestRenderer.act(() => renderer.unmount());
  renderer = mount(Component, { type: 'season', searchPayload: { seriesId: 42, seasonNumber: 1 } });
  TestRenderer.act(() => renderer.root.findByType(FilterModal).props.dispatchSetFilter({ selectedFilterKey: 'all' }));
  assert.equal(fixture.globalOptions.seasonSelectedFilterKey, 'all');
  TestRenderer.act(() => renderer.unmount());
});

test('season modal forwards a caller-specific filter through its actual content without changing the native search payload', () => {
  const Content = load('Series/Search/SeasonInteractiveSearchModalContent.tsx', {
    ...common,
    'InteractiveSearch/InteractiveSearch': (props) => React.createElement('output', { 'data-filter': props.initialSelectedFilterKey, 'data-payload': props.searchPayload }),
    'InteractiveSearch/useReleases': { useClearReleasesOnUnmount: () => undefined },
    'Season/formatSeason': String,
    './SeasonInteractiveSearchModalContent.module.css': {}
  }).default;
  const Modal = load('Series/Search/SeasonInteractiveSearchModal.tsx', { ...common, './SeasonInteractiveSearchModalContent': Content, './SeasonInteractiveSearchModalContent.module.css': {} }).default;
  const renderer = mount(Modal, { isOpen: true, seriesId: 42, seasonNumber: 1, displaySeasonNumber: 3, episodeCount: 12, initialSelectedFilterKey: 'not-season-pack', onModalClose: () => undefined });
  assert.equal(renderer.root.findByType('output').props['data-filter'], 'not-season-pack');
  assert.deepEqual(renderer.root.findByType('output').props['data-payload'], { seriesId: 42, seasonNumber: 1 });
  TestRenderer.act(() => renderer.unmount());
});

function localizationFixture(fetcher, cached) {
  const previousWindow = global.window;
  const previousStorage = global.localStorage;
  global.window = { Sonarr: { apiKey: 'generated-fixture-key', productName: 'Tanukarr', isProduction: true } };
  global.localStorage = { setItem: () => undefined, removeItem: () => undefined };
  const translations = load('Utilities/String/translate.ts');
  const useApiQuery = load('Helpers/Hooks/useApiQuery.ts', {
    'Utilities/Fetch/fetchJson': fetcher,
    'Utilities/Fetch/getQueryPath': (value) => value,
    'Utilities/Fetch/getQueryString': () => ''
  }).default;
  const useTranslations = load('App/useTranslations.ts', { 'Helpers/Hooks/useApiQuery': useApiQuery, 'Utilities/String/translate': translations }).useTranslations;
  const populated = () => ({ isFetched: true, error: null });
  const useAppPage = load('Helpers/Hooks/useAppPage.ts', {
    'App/useTranslations': { useTranslations },
    'Commands/useCommands': () => undefined,
    'Filters/useCustomFilters': populated,
    'Language/useLanguageName': { useInitializeLanguage: () => ({ isInitialized: true }) },
    'Language/useLanguages': { useLanguages: populated },
    'Series/useSeries': populated,
    'Settings/Indexers/useIndexerFlags': populated,
    'Settings/MetadataSource/useMetadataSourceSettings': { useMetadataSourceSettings: populated },
    'Settings/Profiles/Quality/useQualityProfiles': { useQualityProfiles: populated },
    'Settings/UI/useUiSettings': { useUiSettings: populated },
    'System/Status/useSystemStatus': populated,
    'Tags/useTags': populated
  }).default;
  const client = new Query.QueryClient({ defaultOptions: { queries: { retry: false } } });

  if (cached) {
    client.setQueryData(['/localization'], cached);
  }

  const renders = [];
  const children = [];

  function Child() {
    const label = translations.translate('CollectionSettings');
    children.push(label);
    return React.createElement('output', {}, label);
  }

  function PageGate() {
    const { hasError, isPopulated } = useAppPage();
    renders.push({ hasError, isPopulated });

    if (hasError) {
      return React.createElement('aside', {}, 'Error page');
    }

    return isPopulated ? React.createElement(Child) : React.createElement('span', {}, 'Loading');
  }

  const Component = () => React.createElement(Query.QueryClientProvider, { client }, React.createElement(PageGate));

  const cleanup = () => {
    client.clear();
    global.window = previousWindow;
    global.localStorage = previousStorage;
  };

  return { client, translations, renders, children, Component, cleanup };
}

async function flushQueryNotifications() {
  await TestRenderer.act(async() => {
    await new Promise((resolve) => setTimeout(resolve, 20));
  });
}

test('localization gates a cold native page and a complete cached remount until the actual dictionary is installed', async() => {
  let complete = null;
  const fixture = localizationFixture(() => new Promise((resolve) => {
    complete = resolve;
  }));
  let renderer = null;

  try {
    renderer = mount(fixture.Component);
    assert.equal(renderer.root.findAllByType('output').length, 0);
    assert.deepEqual(fixture.renders, [{ hasError: false, isPopulated: false }]);
    complete({ strings: { CollectionSettings: 'Collection Settings' } });
    await flushQueryNotifications();
    assert.deepEqual(fixture.children, ['Collection Settings']);
    assert.equal(renderer.root.findByType('output').children.join(''), 'Collection Settings');
    assert.equal(fixture.renders.at(-1).isPopulated, true);
    TestRenderer.act(() => renderer.unmount());
    fixture.translations.setTranslations({});
    const firstRemountRender = fixture.renders.length;
    renderer = mount(fixture.Component);
    assert.equal(fixture.renders[firstRemountRender].isPopulated, false, 'cached query data alone cannot unlock the page before the installation effect');
    assert.deepEqual(fixture.children, ['Collection Settings', 'Collection Settings']);
    assert.equal(renderer.root.findByType('output').children.join(''), 'Collection Settings');
    const stabilized = fixture.renders.length;
    await flushQueryNotifications();
    assert.equal(fixture.renders.length, stabilized, 'readiness does not cause an effect/render loop');
  } finally {
    if (renderer) {
      TestRenderer.act(() => renderer.unmount());
    }

    fixture.cleanup();
  }
});

test('cached localization and locale replacements install their own dictionary before collection children render', async() => {
  const fixture = localizationFixture(async() => {
    throw new Error('fresh cached translations must not fetch');
  }, { strings: { CollectionSettings: 'Collection Settings' } });
  let renderer = null;

  try {
    renderer = mount(fixture.Component);
    assert.equal(fixture.renders[0].isPopulated, false);
    assert.deepEqual(fixture.children, ['Collection Settings']);
    fixture.client.setQueryData(['/localization'], { strings: { CollectionSettings: 'Impostazioni raccolta' } });
    await flushQueryNotifications();
    assert.equal(renderer.root.findByType('output').children.join(''), 'Impostazioni raccolta');
    assert.deepEqual(fixture.children, ['Collection Settings', 'Impostazioni raccolta'], 'the new response never renders children using the preceding language');
    fixture.client.setQueryData(['/localization'], { strings: { CollectionSettings: 'Collection Settings' } });
    await flushQueryNotifications();
    assert.deepEqual(fixture.children, ['Collection Settings', 'Impostazioni raccolta', 'Collection Settings']);
    assert.equal(fixture.renders.at(-1).isPopulated, true);
  } finally {
    if (renderer) {
      TestRenderer.act(() => renderer.unmount());
    }

    fixture.cleanup();
  }
});

test('a failed localization request reaches the existing error page and a real query retry enables localized children', async() => {
  let attempts = 0;
  const fixture = localizationFixture(async() => {
    attempts++;

    if (attempts === 1) {
      throw new Error('generated localization outage');
    }

    return { strings: { CollectionSettings: 'Collection Settings' } };
  });
  let renderer = null;

  try {
    renderer = mount(fixture.Component);
    await flushQueryNotifications();
    assert.equal(renderer.root.findByType('aside').children.join(''), 'Error page');
    assert.equal(fixture.renders.at(-1).hasError, true);
    assert.deepEqual(fixture.children, []);
    await fixture.client.refetchQueries({ queryKey: ['/localization'] });
    await flushQueryNotifications();
    assert.equal(attempts, 2);
    assert.equal(renderer.root.findByType('output').children.join(''), 'Collection Settings');
    assert.deepEqual(fixture.children, ['Collection Settings']);
    assert.equal(fixture.renders.at(-1).hasError, false);
  } finally {
    if (renderer) {
      TestRenderer.act(() => renderer.unmount());
    }

    fixture.cleanup();
  }
});

test('collections resolve only explicit group IDs and native series IDs, including complete groups outside library filters', () => {
  const items = [member(2, 2), member(1, 1), member(3, 1, { animeGroupId: 'myanimelist:900' })];
  const collection = presentation.resolveCollection(items, 'group:anilist:900');
  assert.deepEqual(collection.members.map((item) => item.id), [1, 2]);
  assert.equal(presentation.resolveCollection(items, 'group:missing'), undefined);
  assert.equal(presentation.resolveCollection(items, 'title:Generated Collection'), undefined);
  assert.equal(presentation.resolveCollection(items, 'series:2').members[0], items[0]);
  assert.equal(presentation.getCollectionLink('manual:a/b #c'), '/collections/group%3Amanual%3Aa%2Fb%20%23c');
  assert.equal(items[0].id, 2, 'sorting does not mutate the source store');
});

test('native TV seasons, distinct anime seasons, movies and Specials retain their own catalogue numbering', () => {
  const anime = member(4, 3);
  const tv = member(9, 1, { seasons: [{ seasonNumber: 0, statistics: { totalEpisodeCount: 2 } }, { seasonNumber: 1 }, { seasonNumber: 2 }] });
  const movie = member(7, null, { animeGroupMemberKind: 'movie', animeGroupMovieNumber: 2 });
  const group = { id: 'anilist:900', title: 'Generated collection', members: [anime, tv, movie] };
  const entries = presentation.getCollectionEntries(group);
  assert.deepEqual(entries.map((entry) => [entry.member.id, entry.season.seasonNumber, entry.displayNumber, entry.section]), [[4, 1, 3, 'seasons'], [9, 0, 0, 'specials'], [9, 1, 1, 'seasons'], [9, 2, 2, 'seasons'], [7, 1, 1, 'movies']]);
  assert.equal(presentation.getNativeSeasonLink(anime, 1), '/series/native-4?season=1#season-1');
  const nativeTV = { ...tv, animeGroupId: null, collectionTitle: 'My collection name' };
  assert.equal(presentation.getSeriesLibraryLink(nativeTV), '/collections/series%3A9');
  assert.equal(presentation.resolveCollection([nativeTV], 'series:9').title, 'My collection name');
});

test('collection artwork falls back to a real member cover and advances after failure without showing a mascot', () => {
  const covers = [];
  const Poster = load('Series/Grouping/CollectionPoster.tsx', { ...common, 'Series/SeriesPoster': (props) => {
    covers.push(props);
    return React.createElement('img', { src: props.images[0].url, onError: props.onError });
  } }).default;
  const members = [member(1, 1), member(2, 2, { images: [{ coverType: 'poster', url: '/MediaCover/2/poster.jpg' }] }), member(3, 3, { images: [{ coverType: 'poster', url: '/MediaCover/3/poster.jpg' }] })];
  const renderer = mount(Poster, { members, title: 'Generated collection' });
  assert.equal(covers.at(-1).overflow, true, 'lazy collection covers observe their nested page or library scroller');
  assert.equal(renderer.root.findByType('img').props.src, '/MediaCover/2/poster.jpg');
  TestRenderer.act(() => renderer.root.findByType('img').props.onError());
  assert.equal(renderer.root.findByType('img').props.src, '/MediaCover/3/poster.jpg');
  TestRenderer.act(() => renderer.root.findByType('img').props.onError());
  assert.equal(renderer.root.findAllByType('img').length, 0);
  assert.equal(renderer.root.findByProps({ role: 'img' }).props['aria-label'], 'Generated collection');
  assert.doesNotMatch(JSON.stringify(renderer.toJSON()), /poster-dark|Tanuki|logo/i);
  TestRenderer.act(() => renderer.unmount());
});

test('collection page renders a scrolling hero and separate season/movie/special sections with refresh for all native members', () => {
  const items = [member(2, 2, { images: [{ coverType: 'fanart', url: '/MediaCover/2/fanart.jpg' }] }), member(1, 1, { overview: '' }), member(3, null, { animeGroupMemberKind: 'movie', animeGroupMovieNumber: 1, seasons: [{ seasonNumber: 0, statistics: { totalEpisodeCount: 1 } }, { seasonNumber: 1 }] })];
  const commands = [];
  let coverChecks = 0;
  const Component = load('Series/Grouping/CollectionPage.tsx', {
    ...common,
    'react-lazyload': { forceCheck: () => {
      coverChecks++;
    } },
    'react-router': { useParams: () => ({ collectionId: 'group:anilist:900' }) },
    'Commands/CommandNames': { RefreshSeries: 'RefreshSeries' },
    'Commands/useCommands': { useExecuteCommand: () => (payload) => commands.push(payload) },
    'Components/Loading/LoadingIndicator': () => null,
    'Components/NotFound': () => null,
    'Components/Page/PageContent': wrap,
    'Components/Page/PageContentBody': body,
    'Series/useSeries': () => ({ data: items, isFetched: true }),
    './CollectionPoster': () => null,
    './CollectionMemberCard': ({ entry }) => React.createElement('output', { 'data-entry': entry.key }),
    './CollectionSearchModal': () => null,
    './CollectionSettingsModal': () => null
  }).default;
  const renderer = mount(Component);
  assert.deepEqual(renderer.root.findAllByType('output').map((item) => item.props['data-entry']), ['1:1', '2:1', '3:1', '3:0']);
  assert.deepEqual(renderer.root.findAllByProps({ 'data-anime-group-section': 'specials' }).length, 1);
  assert.match(JSON.stringify(renderer.toJSON()), /Overview 2/);
  assert.match(JSON.stringify(renderer.toJSON()), /fanart.jpg/);
  TestRenderer.act(() => renderer.root.findByType(body).props.onScroll({ scrollTop: 566, scrollLeft: 0 }));
  assert.equal(coverChecks, 1, 'nested page scroll reevaluates below-fold covers without a window scroll event');
  press(renderer, 'RefreshAndScan');
  assert.deepEqual(commands, [{ name: 'RefreshSeries', seriesIds: [1, 2, 3] }]);
  assert.doesNotMatch(JSON.stringify(renderer.toJSON()), /Each entry keeps/);
  TestRenderer.act(() => renderer.unmount());
});

test('canonical collection presentation replaces a sequel synopsis and cover without replacing member metadata', () => {
  const items = [member(2, 2, { overview: 'Native sequel synopsis' })];
  const queries = [];
  const Component = load('Series/Grouping/CollectionPage.tsx', {
    ...common,
    'react-router': { useParams: () => ({ collectionId: 'group:anilist:900' }) },
    'Commands/CommandNames': { RefreshSeries: 'RefreshSeries' },
    'Commands/useCommands': { useExecuteCommand: () => () => undefined },
    'Components/Loading/LoadingIndicator': () => null,
    'Components/NotFound': () => null,
    'Components/Page/PageContent': wrap,
    'Components/Page/PageContentBody': body,
    'Helpers/Hooks/useApiQuery': (options) => {
      queries.push(options);
      return { data: { overview: 'Canonical collection synopsis', images: [{ coverType: 'poster', remoteUrl: 'https://catalogue.example/root.jpg' }] } };
    },
    'Series/useSeries': () => ({ data: items, isFetched: true }),
    './CollectionPoster': (props) => React.createElement('img', { src: props.preferredImages[0].url }),
    './CollectionMemberCard': ({ entry }) => React.createElement('output', { 'data-native-overview': entry.member.overview }),
    './CollectionSearchModal': () => null,
    './CollectionSettingsModal': () => null
  }).default;
  const renderer = mount(Component);
  assert.equal(queries[0].path, '/collection/presentation');
  assert.deepEqual(queries[0].queryParams, { groupId: 'anilist:900' });
  assert.equal(queries[0].queryOptions.enabled, true);
  assert.equal(queries[0].queryOptions.staleTime, 900000);
  assert.equal(queries[0].queryOptions.retry, false);
  assert.match(JSON.stringify(renderer.toJSON()), /Canonical collection synopsis/);
  assert.equal(renderer.root.findByType('img').props.src, 'https://catalogue.example/root.jpg');
  assert.equal(renderer.root.findByType('output').props['data-native-overview'], 'Native sequel synopsis');
  assert.equal(items[0].overview, 'Native sequel synopsis');
  TestRenderer.act(() => renderer.unmount());
});

test('every season card links to its native season and opens real pack, episode and settings workflows', () => {
  const pack = load('Series/Search/SeasonReleaseSearchButton.tsx', { ...common, 'Helpers/Props': { sizes: { SMALL: 'small' } }, './SeasonInteractiveSearchModal': (props) => (props.isOpen ? React.createElement('output', { 'data-pack': `${props.seriesId}:${props.seasonNumber}:${props.displaySeasonNumber}` }) : null) }).default;
  const Component = load('Series/Grouping/CollectionMemberCard.tsx', {
    ...common,
    './CollectionPoster': () => null,
    'Series/Search/SeasonReleaseSearchButton': pack,
    'Series/Search/SeasonInteractiveSearchModal': (props) => (props.isOpen ? React.createElement('output', { 'data-episodes': `${props.seriesId}:${props.seasonNumber}`, 'data-initial-filter': props.initialSelectedFilterKey }) : null),
    './CollectionSeasonSettingsModal': (props) => (props.isOpen ? React.createElement('button', { onClick: props.onEditEntry }, 'EditEntry') : null),
    'Series/Edit/EditSeriesModal': (props) => (props.isOpen ? React.createElement('output', { 'data-edit': props.seriesId }) : null),
    'Series/Delete/DeleteSeriesModal': () => null
  }).default;
  const native = member(42, 3);
  const entry = presentation.getCollectionEntries({ members: [native] })[0];
  const renderer = mount(Component, { entry });
  assert.ok(renderer.root.findAllByType('a').every((item) => item.props.href === '/series/native-42?season=1#season-1'));
  press(renderer, 'SearchSeasonPacksWithNumber{"season":3}');
  assert.equal(renderer.root.findByType('output').props['data-pack'], '42:1:3');
  press(renderer, 'SearchEpisodes');
  assert.ok(renderer.root.findAllByType('output').some((item) => item.props['data-episodes'] === '42:1'));
  assert.equal(renderer.root.findByProps({ 'data-episodes': '42:1' }).props['data-initial-filter'], 'not-season-pack');
  press(renderer, 'SeasonSettings');
  press(renderer, 'EditEntry');
  assert.ok(renderer.root.findAllByType('output').some((item) => item.props['data-edit'] === 42));
  TestRenderer.act(() => renderer.unmount());
});

test('season monitoring saves the native series and season independently of logical collection season', () => {
  const calls = [];
  const Component = load('Series/Grouping/CollectionSeasonSettingsModal.tsx', { ...common, 'Series/useSeries': { useToggleSeasonMonitored: (id) => ({ toggleSeasonMonitored: (payload, options) => {
    calls.push({ id, payload });
    options.onSuccess();
  } }) } }).default;
  let closed = 0;
  const native = member(42, 3);
  const renderer = mount(Component, { isOpen: true, member: native, season: native.seasons[0], title: 'Season 3', onModalClose: () => closed++, onEditEntry: () => undefined });
  TestRenderer.act(() => renderer.root.findByType('input').props.onChange({ target: { checked: false } }));
  press(renderer, 'Save');
  assert.deepEqual(calls, [{ id: 42, payload: { seasonNumber: 1, monitored: false } }]);
  assert.equal(closed, 1);
  TestRenderer.act(() => renderer.unmount());
});

for (const isNativeCollection of [false, true]) {
  test(`collection settings save ${isNativeCollection ? 'native display title' : 'complete group title'} without rewriting paths or IDs`, () => {
    const calls = [];
    const Component = load('Series/Grouping/CollectionSettingsModal.tsx', { ...common, 'Series/useSeries': { useSaveSeriesEditor: () => ({ saveSeriesEditor: (payload, options) => {
      calls.push(payload);
      options.onSuccess();
    } }) }, 'Settings/Profiles/Quality/useQualityProfiles': { useQualityProfilesData: () => [{ id: 1, name: 'Any' }] } }).default;
    const members = isNativeCollection ? [member(42, 1)] : [member(42, 1), member(43, 2)];
    const renderer = mount(Component, { isOpen: true, isNativeCollection, collection: { id: 'anilist:900', title: 'Old name', members }, onModalClose: () => undefined });
    TestRenderer.act(() => renderer.root.findByType('input').props.onChange({ target: { value: 'New collection name' } }));
    press(renderer, 'Save');
    const expected = isNativeCollection ? { seriesIds: [42], collectionTitle: 'New collection name' } : { seriesIds: [42, 43], animeGroupTitle: 'New collection name', animeGroupKind: 'series', animeGroupMovieFolderStyle: 'automatic' };
    assert.deepEqual(calls, [expected]);
    assert.equal(renderer.root.findAllByType('select').length, isNativeCollection ? 3 : 5);
    TestRenderer.act(() => renderer.unmount());
  });
}

function searchFixture(fetcher, members = [member(1, 1), member(2, 2)]) {
  global.window = { Sonarr: { apiKey: 'generated-fixture-key' } };
  const cache = new Map();
  const cleared = [];
  const queryClient = { setQueryData: (key, value) => cache.set(JSON.stringify(key), value), removeQueries: (filter) => {
    cleared.push(filter);
    cache.delete(JSON.stringify(filter.queryKey));
  } };
  const Component = load('Series/Grouping/CollectionSearchModal.tsx', { ...common,
    '@tanstack/react-query': { useQueryClient: () => queryClient },
    'Utilities/Fetch/fetchJson': fetcher,
    'Utilities/Fetch/getQueryPath': (value) => `/api/v5${value}`,
    'Utilities/Fetch/getQueryString': (params) => `?${new URLSearchParams(params)}`,
    'InteractiveSearch/InteractiveSearch': (props) => React.createElement('output', { 'data-results-native-id': props.searchPayload.seriesId, 'data-cached': cache.has(JSON.stringify(['/release', props.searchPayload])) })
  }).default;
  const renderer = mount(Component, { isOpen: true, collection: { id: 'anilist:900', title: 'Generated Collection', members }, onModalClose: () => undefined });
  return { renderer, cache, cleared };
}

test('Search Collection fetches every member sequentially, shares actual interactive cache keys and skips empty entries', async() => {
  const requests = [];
  const pending = [];
  const { renderer, cache, cleared } = searchFixture((options) => {
    requests.push(options);
    return new Promise((resolve) => pending.push(resolve));
  }, [member(1, 1), member(2, 2), member(3, 3, { seasons: [{ seasonNumber: 1, statistics: { totalEpisodeCount: 0 } }] })]);
  assert.equal(requests.length, 1);
  assert.equal(requests[0].path, '/api/v5/release?seriesId=1&completeSeries=true');
  await TestRenderer.act(async() => {
    pending[0]([{ id: 123 }]);
  });
  assert.equal(requests.length, 2);
  assert.equal(requests[1].path, '/api/v5/release?seriesId=2&completeSeries=true');
  assert.equal(renderer.root.findByType('output').props['data-cached'], true, 'interactive results reuse the collection fetch instead of issuing another query');
  await TestRenderer.act(async() => {
    pending[1]([]);
  });
  assert.equal(cache.size, 2);
  const select = renderer.root.findByType('select');
  TestRenderer.act(() => select.props.onChange({ target: { value: '2' } }));
  assert.equal(renderer.root.findByType('output').props['data-results-native-id'], 2);
  assert.equal(requests.length, 2);
  TestRenderer.act(() => renderer.unmount());
  assert.equal(cache.size, 0);
  assert.equal(cleared.length, 2);
});

test('closing collection search aborts pending work and never starts another member search', async() => {
  const requests = [];
  let finish = null;
  const { renderer, cache } = searchFixture((options) => {
    requests.push(options);
    return new Promise((resolve) => {
      finish = resolve;
    });
  });
  TestRenderer.act(() => renderer.unmount());
  assert.equal(requests[0].signal.aborted, true);
  await TestRenderer.act(async() => finish([]));
  assert.equal(requests.length, 1);
  assert.equal(cache.size, 0);
});

test('one failed member does not hide successful collection searches or start infinite retries', async() => {
  const requests = [];
  const fixture = searchFixture(async(options) => {
    requests.push(options);

    if (requests.length === 1) {
      throw new Error('controlled provider unavailable');
    }

    return [];
  });
  await TestRenderer.act(async() => {
    await new Promise((resolve) => setImmediate(resolve));
  });
  assert.equal(requests.length, 2);
  assert.match(JSON.stringify(fixture.renderer.toJSON()), /CollectionSearchEntryError.*Native entry 1/);
  assert.equal(fixture.renderer.root.findByType('output').props['data-results-native-id'], 2);
  TestRenderer.act(() => fixture.renderer.unmount());
});

function mutationFixture(kind, response) {
  const initial = member(42, 3, { statistics: { totalEpisodeCount: 12, episodeFileCount: 2 }, images: [{ coverType: 'poster', url: '/MediaCover/42/poster.jpg' }] });
  const untouched = member(43, 4);
  const queryClient = new Query.QueryClient({ defaultOptions: { queries: { retry: false } } });
  queryClient.setQueryData(['/series'], [initial, untouched]);
  const submitted = [];
  const constants = new Proxy({}, { get: (_target, key) => key });

  function useFixtureMutation(options) {
    return Query.useMutation({ ...options.mutationOptions, mutationFn: async(payload) => {
      submitted.push({ path: options.path, payload });
      return response;
    } });
  }

  function useFixtureQuery() {
    return Query.useQuery({ queryKey: ['/series'], queryFn: async() => {
      throw new Error('Fixture queries never access the network');
    }, enabled: false });
  }

  const hooks = load('Series/useSeries.ts', {
    'Filters/useCustomFilters': { useCustomFiltersList: () => [] },
    'Helpers/Hooks/useApiMutation': useFixtureMutation,
    'Helpers/Hooks/useApiQuery': useFixtureQuery,
    'Helpers/Props': { filterBuilderTypes: constants, filterBuilderValueTypes: constants, sortDirections: { DESCENDING: 'descending' } },
    'Helpers/Props/getFilterTypePredicate': () => () => true,
    'Utilities/Array/sortByProp': () => () => 0,
    'Utilities/Date/dateFilterPredicate': () => true,
    'Utilities/Filter/clientSideFilterAndSort': () => [],
    'Utilities/String/translate': translate,
    './mergeSeriesUpdate': mergeSeriesUpdate,
    './seriesOptionsStore': { useSeriesOptions: () => ({}) }
  });

  function Frame() {
    const { data } = hooks.default();
    const hook = {
      bulk: hooks.useSaveSeriesEditor,
      entry: hooks.useSaveSeries,
      season: hooks.useToggleSeasonMonitored,
      series: hooks.useToggleSeriesMonitored,
      pass: hooks.useUpdateSeriesMonitor
    }[kind](kind === 'entry' || kind === 'pass' ? false : 42);
    const save = hook.saveSeriesEditor || hook.saveSeries || hook.toggleSeasonMonitored || hook.toggleSeriesMonitored || hook.updateSeriesMonitor;
    const payloads = {
      bulk: { seriesIds: [42], animeGroupTitle: 'Renamed' },
      entry: { ...initial, monitored: false },
      season: { seasonNumber: 1, monitored: false },
      series: { monitored: false },
      pass: { series: [{ id: 42, monitored: false, seasons: [{ seasonNumber: 1, monitored: false }] }] }
    };
    const current = data.find((item) => item.id === 42);
    return React.createElement('section', {},
      React.createElement('button', { onClick: () => save(payloads[kind]) }, 'Apply'),
      React.createElement('output', { 'data-search-enabled': (current.seasons[0].statistics?.totalEpisodeCount ?? 0) > 0, 'data-monitored': current.seasons[0].monitored }));
  }

  const renderer = mount(Query.QueryClientProvider, { client: queryClient, children: React.createElement(Frame) });
  return { renderer, queryClient, initial, untouched, submitted };
}

for (const kind of ['bulk', 'entry', 'season', 'series']) {
  for (const stats of [undefined, null]) {
    test(`mounted ${kind} mutation preserves usable episode statistics when the API ${stats === null ? 'returns null' : 'omits them'}`, async() => {
      const updated = member(42, 3, { statistics: stats, seasons: [{ seasonNumber: 1, monitored: false, title: 'New native season name', statistics: stats }], animeGroupTitle: 'Renamed' });

      if (stats === undefined) {
        delete updated.statistics;
        delete updated.seasons[0].statistics;
      }

      let response = updated;

      if (kind === 'bulk') {
        response = [updated];
      } else if (kind === 'season') {
        response = updated.seasons[0];
      }

      const fixture = mutationFixture(kind, response);
      await TestRenderer.act(async() => {
        press(fixture.renderer, 'Apply');
        await new Promise((resolve) => setTimeout(resolve, 20));
      });
      const result = fixture.queryClient.getQueryData(['/series']);
      assert.equal(result[0].statistics, fixture.initial.statistics);
      assert.equal(result[0].seasons[0].statistics, fixture.initial.seasons[0].statistics);
      assert.equal(result[0].seasons[0].title, 'New native season name');
      assert.equal(result[0].seasons[0].monitored, false);
      assert.equal(result[1], fixture.untouched, 'another native entry is not replaced');
      assert.equal(fixture.renderer.root.findByType('output').props['data-search-enabled'], true, 'collection searches remain available immediately without a page reload');
      assert.equal(fixture.submitted.length, 1);
      TestRenderer.act(() => fixture.renderer.unmount());
      fixture.queryClient.clear();
    });
  }
}

test('mounted SeasonPass honors its no-content contract and preserves known counts while updating requested monitoring flags', async() => {
  const fixture = mutationFixture('pass', {});
  await TestRenderer.act(async() => {
    press(fixture.renderer, 'Apply');
    await new Promise((resolve) => setTimeout(resolve, 20));
  });
  const result = fixture.queryClient.getQueryData(['/series']);
  assert.equal(result[0].monitored, false);
  assert.equal(result[0].seasons[0].monitored, false);
  assert.equal(result[0].statistics, fixture.initial.statistics);
  assert.equal(result[0].seasons[0].statistics, fixture.initial.seasons[0].statistics);
  assert.equal(result[1], fixture.untouched);
  assert.equal(fixture.renderer.root.findByType('output').props['data-search-enabled'], true);
  assert.deepEqual(fixture.submitted, [{ path: '/seasonPass', payload: { series: [{ id: 42, monitored: false, seasons: [{ seasonNumber: 1, monitored: false }] }] } }]);
  TestRenderer.act(() => fixture.renderer.unmount());
  fixture.queryClient.clear();
});

test('series updates honor explicit zero statistics and authoritative new/removed native seasons', () => {
  const original = member(42, 3, { statistics: { totalEpisodeCount: 12 }, seasons: [{ seasonNumber: 1, monitored: true, statistics: { totalEpisodeCount: 12 } }, { seasonNumber: 2, monitored: true, statistics: { totalEpisodeCount: 5 } }] });
  const explicit = { totalEpisodeCount: 0, episodeFileCount: 0 };
  const result = mergeSeriesUpdate(original, { statistics: explicit, seasons: [{ seasonNumber: 1, monitored: false, statistics: explicit }, { seasonNumber: 3, monitored: true }] });
  assert.equal(result.statistics, explicit);
  assert.equal(result.seasons[0].statistics, explicit);
  assert.deepEqual(result.seasons.map((season) => season.seasonNumber), [1, 3]);
  assert.equal(result.seasons[1].statistics, undefined, 'new seasons do not borrow another season count');
  assert.deepEqual(mergeSeriesUpdate(original, { seasons: [] }).seasons, [], 'an explicit empty season list is authoritative');
  assert.equal(mergeSeriesUpdate(original, { monitored: false }).seasons, original.seasons, 'an omitted season list keeps the existing membership');
});
