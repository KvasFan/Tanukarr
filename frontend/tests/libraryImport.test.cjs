const assert = require('node:assert/strict');
const fs = require('node:fs');
const Module = require('node:module');
const path = require('node:path');
const test = require('node:test');
const React = require('react');
const TestRenderer = require('react-test-renderer');
const ts = require('typescript');

const projectRequire = Module.createRequire(path.resolve(__dirname, '../../package.json'));
function load(relative, mocks = {}) {
  const filename = path.resolve(__dirname, '../src', relative);
  const output = ts.transpileModule(fs.readFileSync(filename, 'utf8'), { fileName: filename, compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2020, jsx: ts.JsxEmit.React, esModuleInterop: true } }).outputText;
  const compiled = new Module(filename, module);
  compiled.paths = module.paths;
  compiled.require = (name) => {
    if (Object.hasOwn(mocks, name)) return mocks[name];
    if (name === 'Series/getMediaType') return load('Series/getMediaType.ts');
    if (name === 'Series/isSameSeriesIdentity') return load('Series/isSameSeriesIdentity.ts');
    if (name === 'Series/getMetadataProvider') return load('Series/getMetadataProvider.ts');
    return projectRequire(name);
  };
  // eslint-disable-next-line no-underscore-dangle
  compiled._compile(output, filename);
  return compiled.exports;
}
const identity = load('Series/isSameSeriesIdentity.ts');
const eligibility = load('AddSeries/ImportSeries/Import/importSeriesEligibility.ts');
const movie = (foreignId, metadataSource = 'tmdbMovie', overrides = {}) => ({ metadataSource, foreignId, tvdbId: 0, title: `Generated ${foreignId}`, mediaType: 'movie', seriesType: 'standard', seasonType: 'official', ...overrides });
function mount(Component) {
  let renderer;
  TestRenderer.act(() => { renderer = TestRenderer.create(React.createElement(Component)); });
  return renderer;
}

test('native library import controls use real English translations instead of raw localization keys', () => {
  const dictionary = JSON.parse(fs.readFileSync(path.resolve(__dirname, '../../src/NzbDrone.Core/Localization/Core/en.json'), 'utf8'));
  const directory = path.resolve(__dirname, '../src/AddSeries/ImportSeries/Import');
  const files = ['ImportSeries.tsx', 'ImportSeriesCard.tsx', 'ImportSeriesFooter.tsx', 'SelectSeries/ImportSeriesSelectSeries.tsx', 'SelectSeries/ImportSeriesTitle.tsx'];
  let checked = 0;
  for (const file of files) {
    for (const [, key] of fs.readFileSync(path.join(directory, file), 'utf8').matchAll(/translate\('([^']+)'/g)) {
      assert.ok(Object.hasOwn(dictionary, key), `${file} localization ${key} must have readable English text`);
      checked++;
    }
  }
  assert.ok(checked > 30);
});

test('library import keeps distinct native film and television namespaces despite shared placeholder TVDB IDs', () => {
  const items = [movie('42'), movie('43'), movie('42', 'tmdb'), movie('tt0042', 'omdb'), movie('42', 'aniList')].map((selectedSeries, index) => ({ id: String(index), selectedSeries }));
  const result = eligibility.getImportSeriesEligibility(items, new Set());
  assert.deepEqual([...result.eligibilityById.values()], ['ready', 'ready', 'ready', 'ready', 'ready']);
  assert.equal(result.duplicateIdsByIdentity.size, 0);
  assert.deepEqual(eligibility.getImportSeriesSelection(result, items.map((item) => item.id)).importableIds, ['0', '1', '2', '3', '4']);
});

test('existing native films and duplicate folder choices use their actual provider identity', () => {
  const items = [
    { id: 'existing', selectedSeries: movie('42', 'tmdbMovie', { tvdbId: -999 }) },
    { id: 'first', selectedSeries: movie('43') },
    { id: 'second', selectedSeries: movie('43', 'tmdbMovie', { tvdbId: -123 }) },
    { id: 'other-source', selectedSeries: movie('43', 'tmdb') }
  ];
  const result = eligibility.getImportSeriesEligibility(items, new Set([identity.getSeriesIdentityKey(movie('42', 'tmdbMovie', { tvdbId: -1 }))]));
  assert.deepEqual([...result.eligibilityById.values()], ['existing', 'duplicate', 'duplicate', 'ready']);
  assert.deepEqual(result.duplicateIdsByIdentity.get('tmdbmovie:43'), ['first', 'second']);
  const selection = eligibility.getImportSeriesSelection(result, ['existing', 'second', 'first', 'other-source']);
  assert.deepEqual(selection.importableIds, ['first', 'other-source']);
  assert.deepEqual([...selection.duplicateIdsToDeselect], ['second']);
});

test('legacy library imports require a positive TVDB ID and incomplete native identities remain unmatched', () => {
  const items = [{ tvdbId: 12 }, { metadataSource: 'tvdb', tvdbId: 12 }, { tvdbId: 0 }, { tvdbId: -12 }, { metadataSource: 'omdb', tvdbId: 0 }, undefined].map((selectedSeries, index) => ({ id: String(index), selectedSeries }));
  const result = eligibility.getImportSeriesEligibility(items, new Set(['tvdb:12']));
  assert.deepEqual([...result.eligibilityById.values()], ['existing', 'existing', 'unmatched', 'unmatched', 'unmatched', 'unmatched']);
  assert.deepEqual(eligibility.getImportSeriesSelection(result, items.map((item) => item.id)).importableIds, []);
});

test('actual import mutation preserves separate native films, prevents duplicate identities and omits invalid IDs', () => {
  const submitted = [];
  const selected = [movie('42'), movie('43'), movie('tt0044', 'omdb'), movie('42'), { tvdbId: 88, seriesType: 'standard', seasonType: 'official' }, { tvdbId: 88 }, { tvdbId: -88 }];
  const items = selected.map((selectedSeries, index) => ({ id: String(index), selectedSeries, path: `/generated/library/${index}`, qualityProfileId: 3, monitor: index === 2 ? 'none' : 'future', seriesType: 'anime', seasonType: 'alternate', seasonFolder: true, language: { id: 1, name: 'English' } }));
  const { useImportSeries } = load('AddSeries/ImportSeries/Import/useImportSeries.ts', {
    '@tanstack/react-query': { useQueryClient: () => ({}) },
    'Helpers/Hooks/useApiMutation': () => ({ mutate: (data) => submitted.push(data) }),
    './importSeriesStore': { getImportSeriesItems: () => items, removeImportSeriesItemByPath: () => {} }
  });
  let current;
  mount(() => { current = useImportSeries(); return null; });
  TestRenderer.act(() => current.importSeries(items.map((item) => item.id)));
  assert.equal(submitted.length, 1);
  const payload = submitted[0];
  assert.equal(payload.length, 4);
  assert.deepEqual(payload.slice(0, 3).map((item) => [item.metadataSource, item.foreignId, item.tvdbId, item.path]), [
    ['tmdbMovie', '42', 0, '/generated/library/0'], ['tmdbMovie', '43', 0, '/generated/library/1'], ['omdb', 'tt0044', 0, '/generated/library/2']
  ]);
  for (const item of payload.slice(0, 3)) {
    assert.equal(item.seriesType, 'standard');
    assert.equal(item.seasonType, 'official');
    assert.equal(item.seasonFolder, false);
    assert.equal(item.addOptions.searchForMissingEpisodes, false);
    assert.equal(item.addOptions.searchForCutoffUnmetEpisodes, false);
  }
  assert.equal(payload[0].addOptions.monitor, 'all');
  assert.equal(payload[2].addOptions.monitor, 'none');
  assert.equal(payload[3].tvdbId, 88);
  assert.equal(payload[3].seriesType, 'anime');
  assert.equal(payload[3].seasonFolder, true);
  assert.equal(payload[3].addOptions.monitor, 'future');
});

test('import source selection honors enabled order and falls back from a disabled persisted source', () => {
  let saved = 'tvdb';
  const settings = { metadataSourceOrder: ['omdb', 'tmdbMovie', 'aniList'], disabledMetadataSources: ['tvdb', 'tmdb', 'myAnimeList', 'tvMaze'] };
  const { default: useEnabledMetadataSource, changeMetadataSource } = load('Settings/MetadataSource/useEnabledMetadataSource.ts', {
    'AddSeries/AddNewSeries/metadataSourceOptionsStore': { useMetadataSourceOption: () => saved, setMetadataSourceOption: (value) => { saved = value; } },
    './metadataSources': load('Settings/MetadataSource/metadataSources.ts'),
    './useMetadataSourceSettings': { useMetadataSourceSettings: () => ({ data: settings, isFetched: true }) }
  });
  assert.equal(useEnabledMetadataSource().metadataSource, 'all');
  assert.deepEqual(useEnabledMetadataSource().sources.map((source) => source.key), ['omdb', 'tmdbMovie', 'aniList']);
  changeMetadataSource({ value: 'omdb' });
  assert.equal(useEnabledMetadataSource().metadataSource, 'omdb');
  assert.equal(useEnabledMetadataSource().isFetched, true);
});

test('changing import sources retries unmatched folders while preserving explicit native matches', () => {
  const store = load('AddSeries/ImportSeries/Import/importSeriesStore.ts', {
    'AddSeries/addSeriesOptionsStore': { useAddSeriesOptions: () => ({}), useAddSeriesLanguage: () => ({ id: 1, name: 'English' }) },
    'Helpers/Hooks/useOptionsStore': { createOptionsStore: () => ({ useOption: () => false, setOption: () => {} }) }
  });
  const folders = ['matched', 'unmatched'].map((id) => ({ id, path: `/generated/${id}`, name: id }));
  mount(() => { store.useEnsureImportSeriesItems(folders); return null; });
  const selectedSeries = movie('42');
  store.updateImportSeriesItem({ id: 'matched', selectedSeries, hasSearched: true });
  store.updateImportSeriesItem({ id: 'unmatched', hasSearched: true });
  store.resetUnmatchedSearches();
  const [matched, unmatched] = store.getImportSeriesItems(['matched', 'unmatched']);
  assert.equal(matched.selectedSeries, selectedSeries);
  assert.equal(matched.hasSearched, true);
  assert.equal(unmatched.hasSearched, false);
});

test('the actual import picker queries its enabled native source and keyboard-selects the exact result object', () => {
  const candidates = [movie('42'), movie('43', 'tmdbMovie', { title: 'Second native movie' })];
  const changes = [];
  const selected = [];
  const queries = [];
  const props = { id: 'generated folder', onInputChange: (change) => changes.push(change), onEditingChange: () => {} };
  const source = 'tmdbMovie';
  const component = load('AddSeries/ImportSeries/Import/SelectSeries/ImportSeriesSelectSeries.tsx', {
    '@floating-ui/react': { autoUpdate: () => {}, flip: () => {}, shift: () => {}, size: () => {}, useDismiss: () => ({}), useInteractions: () => ({ getReferenceProps: () => ({}), getFloatingProps: () => ({}) }), useFloating: () => ({ refs: { setReference: () => {}, setFloating: () => {} }, context: {}, floatingStyles: {} }), FloatingPortal: ({ children }) => children },
    'AddSeries/AddNewSeries/useAddSeries': { useLookupSeries: (query, options) => { queries.push({ query, ...options }); return { data: candidates, isFetched: false }; } },
    'Components/Form/TextInput': React.forwardRef((input, ref) => React.createElement('input', { ref, onFocus: input.onFocus, value: input.value, onChange: () => {} })),
    'Components/Icon': () => null,
    'Components/Link/Button': (button) => React.createElement('button', { onClick: button.onPress }, button.children),
    'Components/Link/Link': () => null,
    'Components/Loading/LoadingIndicator': () => null,
    'Helpers/Hooks/useDebounce': (value) => value,
    'Helpers/Props': { icons: {}, kinds: {} },
    'Language/useLanguages': { usePreferredMetadataLanguage: () => 'en' },
    'Series/useExistingSeries': () => false,
    'Settings/MetadataSource/useEnabledMetadataSource': () => ({ metadataSource: source, isFetched: true }),
    'Utilities/Object/getErrorMessage': () => '',
    'Utilities/String/translate': (value) => value,
    '../importSeriesStore': { useImportSeriesItem: () => ({ name: 'Generated folder' }), useIsCurrentLookupQueueItem: () => true, useIsCurrentItemQueued: () => false, addToLookupQueue: () => {}, removeFromLookupQueue: () => {}, updateImportSeriesItem: (value) => selected.push(value) },
    './ImportSeriesSearchResult': () => null,
    './ImportSeriesSelectSeries.module.css': {}
  }).default;
  const renderer = mount(() => React.createElement(component, props));
  TestRenderer.act(() => renderer.root.findByType('button').props.onClick());
  const combo = renderer.root.findByProps({ role: 'combobox' });
  TestRenderer.act(() => combo.props.onKeyDown({ key: 'ArrowDown', preventDefault() {} }));
  TestRenderer.act(() => renderer.root.findByProps({ role: 'combobox' }).props.onKeyDown({ key: 'Enter', preventDefault() {} }));
  assert.ok(queries.every((query) => query.metadataSource === source && query.isEnabled));
  assert.equal(selected.at(-1).selectedSeries, candidates[1]);
  assert.ok(changes.some((change) => change.name === 'seasonFolder' && change.value === false));
});

test('a root import row can render before its store item exists without querying an undefined title', () => {
  const queries = [];
  const Component = load('AddSeries/ImportSeries/Import/SelectSeries/ImportSeriesSelectSeries.tsx', {
    '@floating-ui/react': { autoUpdate: () => {}, flip: () => {}, shift: () => {}, size: () => {}, useDismiss: () => ({}), useInteractions: () => ({ getReferenceProps: () => ({}), getFloatingProps: () => ({}) }), useFloating: () => ({ refs: {}, context: {}, floatingStyles: {} }) },
    'AddSeries/AddNewSeries/useAddSeries': { useLookupSeries: (query) => { queries.push(query.trim()); return { data: [] }; } },
    'Components/Form/TextInput': () => null,
    'Components/Icon': () => null,
    'Components/Link/Button': () => null,
    'Components/Link/Link': () => null,
    'Components/Loading/LoadingIndicator': () => null,
    'Helpers/Hooks/useDebounce': (value) => value,
    'Helpers/Props': { icons: {}, kinds: {} },
    'Language/useLanguages': { usePreferredMetadataLanguage: () => ({ id: 1 }) },
    'Series/useExistingSeries': () => false,
    'Settings/MetadataSource/useEnabledMetadataSource': () => ({ metadataSource: 'tmdbMovie', isFetched: true }),
    'Utilities/Object/getErrorMessage': () => '',
    'Utilities/String/translate': (value) => value,
    '../importSeriesStore': { useImportSeriesItem: () => undefined, useIsCurrentLookupQueueItem: () => false, useIsCurrentItemQueued: () => false },
    './ImportSeriesSearchResult': () => null,
    './ImportSeriesSelectSeries.module.css': {}
  }).default;
  mount(() => React.createElement(Component, { id: 'pending folder', onInputChange: () => {}, onEditingChange: () => {} }));
  assert.deepEqual(queries, ['']);
});

function providerOptionsFixture(implementation = 'Jellyfin', fields = {}) {
  const queries = [];
  const data = { implementation: { value: implementation }, name: { value: 'Generated provider' }, fields: Object.entries(fields).map(([name, value]) => ({ name, value })) };
  const useProviderOptions = load('Settings/useProviderOptions.ts', {
    '@tanstack/react-query': { keepPreviousData: 'keep-previous', useQuery: (options) => { queries.push(options); return {}; } },
    'Utilities/Fetch/fetchJson': { default: () => {}, urlBase: '/generated-base' }
  }).default;
  return (action) => { mount(() => { useProviderOptions({ provider: 'notification', action, providerData: data }); return null; }); return queries.at(-1); };
}

test('empty or incomplete Jellyfin editors defer library/user requests until connection credentials exist', () => {
  for (const fields of [{}, { host: 'generated-server', port: 8096 }, { host: ' ', port: 8096, apiKey: 'generated-key' }, { host: 'generated-server', port: 0, apiKey: 'generated-key' }, { host: 'generated-server', port: 65536, apiKey: 'generated-key' }]) {
    for (const action of ['libraries', 'users']) assert.equal(providerOptionsFixture('Jellyfin', fields)(action).enabled, false);
  }
  for (const action of ['libraries', 'users']) assert.equal(providerOptionsFixture('Jellyfin', { host: 'generated-server', port: 8096, apiKey: 'generated-key' })(action).enabled, true);
  assert.equal(providerOptionsFixture('Newznab', {})('newznabCategories').enabled, true);
  assert.equal(providerOptionsFixture('OtherProvider', {})('users').enabled, true);
});

test('Jellyfin user and library choices are isolated by connection and never show previous-server choices', () => {
  const fields = { host: 'first-generated-server', port: 8096, useSsl: false, urlBase: '/generated', apiKey: 'generated-key' };
  for (const action of ['users', 'libraries']) {
    const initial = providerOptionsFixture('Jellyfin', fields)(action);
    for (const change of [{ host: 'second-generated-server' }, { port: 8920 }, { useSsl: true }, { urlBase: '/other-base' }, { apiKey: 'different-generated-key' }]) {
      assert.notDeepEqual(initial.queryKey, providerOptionsFixture('Jellyfin', { ...fields, ...change })(action).queryKey);
    }
    assert.equal(initial.placeholderData, undefined);
  }
  assert.equal(providerOptionsFixture('Newznab', {})('newznabCategories').placeholderData, 'keep-previous');
});

test('library import result links use actual TMDB Movie and IMDb identities and preserve the selected object', () => {
  for (const candidate of [movie('42'), movie('tt0044', 'omdb')]) {
    const pressed = [];
    const matched = [];
    const Component = load('AddSeries/ImportSeries/Import/SelectSeries/ImportSeriesSearchResult.tsx', {
      'Components/Icon': () => null,
      'Components/Link/Link': (props) => React.createElement('a', { href: props.to, onClick: props.onPress }, props.children),
      'Helpers/Props': { icons: {} },
      'Series/useExistingSeries': (value) => { matched.push(value); return false; },
      './ImportSeriesTitle': ({ title }) => title,
      './ImportSeriesSearchResult.module.css': {}
    }).default;
    const renderer = mount(() => React.createElement(Component, { id: 'result', series: candidate, isHighlighted: false, onPress: (value) => pressed.push(value) }));
    const links = renderer.root.findAllByType('a');
    assert.equal(links[1].props.href, candidate.metadataSource === 'omdb' ? 'https://www.imdb.com/title/tt0044/' : 'https://www.themoviedb.org/movie/42');
    TestRenderer.act(() => links[0].props.onClick());
    assert.equal(pressed[0], candidate);
    assert.equal(matched[0], candidate);
  }
});

test('matched film import cards expose movie monitoring without unrelated episode or season controls', () => {
  for (const isMovie of [true, false]) {
    const selectedSeries = isMovie ? movie('42', 'tmdbMovie', { images: [] }) : { title: 'Generated TV', tvdbId: 12, images: [], seriesType: 'standard' };
    const input = (props) => React.createElement('select', { name: props.name, value: props.value }, (props.values || []).map((option) => React.createElement('option', { key: option.key, value: option.key }, option.value)));
    const mocks = Object.fromEntries(['EnhancedSelectInput', 'LanguageSelectInput', 'MonitorEpisodesSelectInput', 'QualityProfileSelectInput', 'SeasonTypeSelectInput', 'SeriesTypeSelectInput'].map((name) => [`Components/Form/${name === 'EnhancedSelectInput' ? 'Select/EnhancedSelectInput' : ['LanguageSelectInput', 'MonitorEpisodesSelectInput', 'QualityProfileSelectInput', 'SeasonTypeSelectInput', 'SeriesTypeSelectInput'].includes(name) ? 'Select/' + name : name}`, input]));
    const Component = load('AddSeries/ImportSeries/Import/ImportSeriesCard.tsx', {
      ...mocks,
      'App/Select/SelectContext': { useSelect: () => ({ setItemStates: () => {}, toggleSelected: () => {}, useIsSelected: () => true }) },
      'Components/Form/CheckInput': () => null,
      'Components/Label': ({ children }) => React.createElement('span', {}, children),
      'Helpers/Props': { kinds: {} },
      'Series/SeriesPoster': () => null,
      'Utilities/String/translate': (value) => value,
      './importSeriesStore': { useImportSeriesItem: () => ({ selectedSeries, relativePath: 'Generated', monitor: 'future', seasonType: 'official', qualityProfileId: 1 }), updateImportSeriesItem: () => {} },
      './SelectSeries/ImportSeriesSelectSeries': () => null,
      './ImportSeriesCard.module.css': {}
    }).default;
    const renderer = mount(() => React.createElement(Component, { unmappedFolder: { id: 'generated', path: '/generated' }, duplicateIds: [], eligibility: 'ready', isCompact: false, isStacked: false }));
    const inputs = renderer.root.findAllByType('select');
    assert.deepEqual(inputs.map((element) => element.props.name), isMovie ? ['monitor', 'qualityProfileId', 'language'] : ['monitor', 'qualityProfileId', 'seriesType', 'seasonType', 'language', 'seasonFolder']);
    if (isMovie) {
      const monitor = inputs.find((element) => element.props.name === 'monitor');
      assert.equal(monitor.props.value, 'all');
      assert.deepEqual(monitor.findAllByType('option').map((option) => option.props.value), ['all', 'none']);
    }
  }
});
