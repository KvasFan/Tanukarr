const assert = require('node:assert/strict');
const fs = require('node:fs');
const Module = require('node:module');
const path = require('node:path');
const test = require('node:test');
const PropTypes = require('prop-types');
const React = require('react');
const TestRenderer = require('react-test-renderer');
const ts = require('typescript');

const language = { id: 1, name: 'English' };
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

const emptyValidation = () => ({ errors: [], warnings: [] });
const selectSettings = loadSource('Utilities/selectSettings.ts', {
  'Helpers/Hooks/useApiMutation': { getValidationFailures: emptyValidation },
  'Utilities/Fetch/fetchJson': { ApiError: class ApiError extends Error {} },
  'Utilities/Object/isEmpty': loadSource('Utilities/Object/isEmpty.ts')
}).default;

function createFixture(initialSeriesType, savedType = 'standard', groupingFixture = {}) {
  let options = {
    rootFolderPath: '/generated-media',
    language,
    monitor: 'all',
    qualityProfileId: 1,
    seriesType: savedType,
    seasonFolder: true,
    searchForMissingEpisodes: false,
    searchForCutoffUnmetEpisodes: false,
    tags: []
  };
  const listeners = new Set();
  const submitted = [];
  const queries = [];

  const subscribe = (listener) => {
    listeners.add(listener);
    return () => listeners.delete(listener);
  };

  const setOption = (name, value) => {
    options = { ...options, [name]: value };
    listeners.forEach((listener) => listener());
  };

  const snapshot = () => options;
  const series = {
    id: 42,
    title: 'Generated series',
    originalTitle: 'Generated series',
    overview: 'A generated fixture with no remote metadata requests.',
    year: 2026,
    folder: 'Generated series',
    folders: [],
    translations: [],
    images: [],
    seasonTypes: [{ type: 'official', name: 'Official' }],
    metadataSource: initialSeriesType === 'anime' ? 'aniList' : 'tvdb',
    foreignId: '42',
    tvdbId: initialSeriesType === 'anime' ? -42 : 42,
    tmdbId: 0,
    imdbId: '',
    monitored: true,
    monitorNewItems: 'all',
    seriesType: initialSeriesType,
    seasonType: 'official',
    language,
    seasonFolder: true,
    path: groupingFixture.editing ? '/existing/Generated series' : '',
    rootFolderPath: '/existing',
    qualityProfileId: 1,
    tags: [],
    ...groupingFixture.series
  };

  const wrapper = (name) => {
    function MockContainer({ children }) {
      return React.createElement('section', { 'data-component': name }, children);
    }

    MockContainer.propTypes = { children: PropTypes.node };
    return MockContainer;
  };

  const input = (props) => {
    if (props.name === 'seriesType') {
      return React.createElement('select', {
        'data-field': props.name,
        value: props.value,
        onChange: (event) => props.onChange({ name: props.name, value: event.target.value })
      }, ['standard', 'daily', 'anime'].map((value) =>
        React.createElement('option', { key: value, value }, value)));
    }

    return React.createElement('input', {
      'data-field': props.name,
      value: typeof props.value === 'string' || typeof props.value === 'number' ? props.value : '',
      onChange: (event) => props.onChange({ name: props.name, value: event.target.value })
    });
  };

  const grouping = loadSource('Series/Grouping/animeGrouping.ts');
  const preview = loadSource('Series/Grouping/useAnimeGroupingPreview.ts', {
    'Helpers/Hooks/useApiQuery': (query) => {
      queries.push(query);
      return { data: groupingFixture.preview, isFetching: groupingFixture.isFetching ?? false };
    }
  }).default;
  const groupingOptions = loadSource('Series/Grouping/AnimeGroupingOptions.tsx', {
    'Components/Form/FormInputHelpText': (props) => React.createElement('p', {}, props.text),
    'Components/Form/FormLabel': wrapper('label'),
    'Components/Form/FormRow': wrapper('row'),
    'Components/Form/NumberInput': input,
    'Components/Form/SelectInput': input,
    'Components/Form/TextInput': input,
    'Series/useSeries': () => ({ data: groupingFixture.existingSeries ?? [] }),
    'Utilities/String/translate': (key, args) => `${key}${args ? JSON.stringify(args) : ''}`,
    './animeGrouping': grouping
  }).default;
  const mocks = {
    react: React,
    'AddSeries/addSeriesOptionsStore': {
      useAddSeriesOptions: () => React.useSyncExternalStore(subscribe, snapshot),
      setAddSeriesOption: setOption
    },
    'AddSeries/SeriesMonitoringOptionsPopoverContent': wrapper('monitoringHelp'),
    'AddSeries/SeriesTypePopoverContent': wrapper('seriesTypeHelp'),
    'App/appStore': { useAppDimension: () => false },
    'Components/Form/CheckInput': wrapper('checkbox'),
    'Components/Form/Form': wrapper('form'),
    'Components/Form/FormInput': input,
    'Components/Form/FormInputHelpText': wrapper('help'),
    'Components/Form/FormLabel': wrapper('label'),
    'Components/Form/FormRow': wrapper('row'),
    'Components/Icon': wrapper('icon'),
    'Components/Link/Button': (props) => React.createElement('button', {
      'data-action': 'cancel', onClick: props.onPress
    }, props.children),
    'Components/Link/SpinnerButton': (props) => React.createElement('button', {
      'data-action': 'add', onClick: props.onPress
    }, props.children),
    'Components/Modal/ModalBody': wrapper('body'),
    'Components/Modal/ModalContent': wrapper('modal'),
    'Components/Modal/ModalFooter': wrapper('footer'),
    'Components/Modal/ModalHeader': wrapper('header'),
    'Components/Tooltip/Popover': wrapper('popover'),
    'Helpers/Hooks/useApiMutation': { getValidationFailures: emptyValidation },
    'Helpers/Props': { icons: {}, inputTypes: {}, kinds: {}, tooltipPositions: {} },
    'Language/Language': { DEFAULT_LANGUAGE: language },
    'Language/useLanguages': { useLanguageById: () => language },
    'Series/SeriesPoster': wrapper('poster'),
    'Series/Grouping/AnimeGroupingOptions': groupingOptions,
    'Series/Grouping/animeGrouping': grouping,
    'Series/Grouping/useAnimeGroupingPreview': preview,
    'Settings/MetadataSource/useMetadataSourceSettings': {
      useMetadataSourceSettingsValues: () => ({ preferredMetadataLanguage: language.id })
    },
    'System/Status/useSystemStatus': { useIsWindows: () => false },
    'Utilities/selectSettings': { __esModule: true, default: selectSettings },
    'Utilities/String/translate': (key) => key,
    './useAddSeries': {
      useAddSeries: () => ({ isAdding: false, addError: null, addSeries: (payload) => submitted.push(payload) })
    },
    './useEpisodeOrderings': { useEpisodeOrderings: () => ({ data: [] }) },
    './AddNewSeriesModalContent.module.css': {}
  };

  if (groupingFixture.editing) {
    mocks['AddSeries/SeriesMonitorNewItemsOptionsPopoverContent'] = wrapper('monitorNewHelp');
    mocks['Components/Form/FormInputButton'] = wrapper('inputButton');
    mocks['Components/Link/SpinnerErrorButton'] = (props) => React.createElement('button', {
      'data-action': 'add', onClick: props.onPress
    }, props.children);
    mocks['Helpers/Hooks/useApiQuery'] = () => ({ data: { folder: 'Generated series' }, isFetching: false });
    mocks['Helpers/Hooks/usePendingChangesStore'] = loadSource('Helpers/Hooks/usePendingChangesStore.ts');
    mocks['Helpers/Hooks/usePrevious'] = loadSource('Helpers/Hooks/usePrevious.tsx').default;
    mocks['Series/MoveSeries/MoveSeriesModal'] = wrapper('moveModal');
    mocks['Series/SeriesMovePending'] = wrapper('pendingMove');
    mocks['Series/useSeries'] = {
      useSingleSeries: () => series,
      useSaveSeries: (moveFiles) => ({ saveSeries: (payload) => submitted.push({ ...payload, fixtureMoveFiles: moveFiles }), isSaving: false, saveError: null })
    };
    mocks['./RootFolder/RootFolderModal'] = wrapper('rootModal');
    mocks['./EditSeriesModalContent.module.css'] = {};
    mocks['Helpers/Props'].sizes = {};
  }

  const Component = loadSource(groupingFixture.editing ?
    'Series/Edit/EditSeriesModalContent.tsx' :
    'AddSeries/AddNewSeries/AddNewSeriesModalContent.tsx', mocks).default;
  let renderer = null;
  TestRenderer.act(() => {
    renderer = TestRenderer.create(React.createElement(Component, {
      series, initialSeriesType, seriesId: series.id, onModalClose: () => {}, onDeleteSeriesPress: () => {}
    }));
  });
  return {
    renderer,
    type: () => renderer.root.findByProps({ 'data-field': 'seriesType' }).props.value,
    savedType: () => options.seriesType,
    queries,
    changeField(name, value) {
      TestRenderer.act(() => {
        renderer.root.findByProps({ 'data-field': name }).props.onChange({ target: { value } });
      });
    },
    selectType(value) {
      TestRenderer.act(() => {
        renderer.root.findByProps({ 'data-field': 'seriesType' }).props.onChange({ target: { value } });
      });
    },
    changeQuality(value) {
      TestRenderer.act(() => {
        renderer.root.findByProps({ 'data-field': 'qualityProfileId' }).props.onChange({ target: { value } });
      });
    },
    submit() {
      TestRenderer.act(() => renderer.root.findByProps({ 'data-action': 'add' }).props.onClick());
      return submitted.at(-1);
    },
    cleanup() {
      TestRenderer.act(() => renderer.unmount());
    }
  };
}

for (const recommended of ['anime', 'daily']) {
  test(`mounted Add Series preserves provider-recommended ${recommended} and submits it`, (context) => {
    const fixture = createFixture(recommended);
    context.after(() => fixture.cleanup());
    assert.equal(fixture.type(), recommended);
    const payload = fixture.submit();
    assert.equal(payload.seriesType, recommended);
    assert.equal(payload.foreignId, '42');
    assert.equal(payload.metadataSource, recommended === 'anime' ? 'aniList' : 'tvdb');
    assert.equal(payload.addOptions.searchForMissingEpisodes, false);
  });

  test(`unrelated settings rerender does not replace recommended ${recommended}`, (context) => {
    const fixture = createFixture(recommended);
    context.after(() => fixture.cleanup());
    fixture.changeQuality(2);
    assert.equal(fixture.type(), recommended);
    const payload = fixture.submit();
    assert.equal(payload.seriesType, recommended);
    assert.equal(payload.qualityProfileId, 2);
  });
}

for (const saved of ['standard', 'anime', 'daily']) {
  test(`standard provider recommendation respects saved ${saved} default`, (context) => {
    const fixture = createFixture('standard', saved);
    context.after(() => fixture.cleanup());
    assert.equal(fixture.type(), saved);
    assert.equal(fixture.submit().seriesType, saved);
  });
}

test('explicit Standard selection updates local recommendation even when Standard is already saved', (context) => {
  const fixture = createFixture('anime', 'standard');
  context.after(() => fixture.cleanup());
  fixture.selectType('standard');
  fixture.changeQuality(2);
  assert.equal(fixture.type(), 'standard');
  assert.equal(fixture.savedType(), 'standard');
  assert.equal(fixture.submit().seriesType, 'standard');
});

test('automatic catalogue grouping preview works with Standard parsing and leaves native identity intact', (context) => {
  const fixture = createFixture('standard', 'standard', {
    series: { metadataSource: 'aniList', foreignId: '200' },
    preview: { status: 'resolved', animeGroupId: 'anilist:100', animeGroupTitle: 'Official chain', animeGroupSeasonNumber: 2, animeGroupFolder: 'Official chain' }
  });
  context.after(() => fixture.cleanup());
  assert.equal(fixture.queries.at(-1).path, '/series/grouping');
  assert.deepEqual(fixture.queries.at(-1).queryParams, { metadataSource: 'aniList', foreignId: '200' });
  assert.equal(fixture.queries.at(-1).queryOptions.enabled, true);
  assert.match(JSON.stringify(fixture.renderer.toJSON()), /Official chain/);
  const payload = fixture.submit();
  assert.equal(payload.animeGroupingMode, 'automatic');
  assert.equal(payload.metadataSource, 'aniList');
  assert.equal(payload.foreignId, '200');
  assert.equal(payload.seriesType, 'standard');
  assert.equal(payload.seasonFolder, false);
  assert.equal(payload.animeGroupId, undefined, 'server resolves canonical automatic membership');
});

test('manual existing-group override submits exact group and unique season without replacing provider identity', (context) => {
  const fixture = createFixture('anime', 'standard', {
    existingSeries: [
      { id: 10, animeGroupId: 'anilist:100', animeGroupTitle: 'Official chain', animeGroupSeasonNumber: 1 },
      { id: 11, animeGroupId: 'anilist:100', animeGroupTitle: 'Official chain', animeGroupSeasonNumber: 2 }
    ]
  });
  context.after(() => fixture.cleanup());
  fixture.changeField('animeGroupingMode', 'manual');
  fixture.changeField('animeGroupId', 'anilist:100');
  const payload = fixture.submit();
  assert.equal(payload.animeGroupingMode, 'manual');
  assert.equal(payload.animeGroupId, 'anilist:100');
  assert.equal(payload.animeGroupTitle, 'Official chain');
  assert.equal(payload.animeGroupSeasonNumber, 3);
  assert.equal(payload.seasonFolder, false);
  assert.equal(payload.foreignId, '42');
  assert.equal(payload.seriesType, 'anime');
  assert.equal(fixture.queries.at(-1).queryOptions.enabled, false);
});

test('new manual group uses unique UUID and explicit title/season', (context) => {
  const fixture = createFixture('anime');
  context.after(() => fixture.cleanup());
  fixture.changeField('animeGroupingMode', 'manual');
  fixture.changeField('animeGroupTitle', 'Manual grouping');
  fixture.changeField('animeGroupSeasonNumber', 7);
  const payload = fixture.submit();
  assert.match(payload.animeGroupId, /^manual:[0-9a-f-]{36}$/);
  assert.equal(payload.animeGroupTitle, 'Manual grouping');
  assert.equal(payload.animeGroupSeasonNumber, 7);
});

test('disabled and unresolved automatic grouping stay standalone without changing saved season-folder preference', (context) => {
  const fixture = createFixture('anime');
  context.after(() => fixture.cleanup());
  assert.equal(fixture.submit().seasonFolder, true);
  fixture.changeField('animeGroupingMode', 'manual');
  fixture.changeField('animeGroupingMode', 'disabled');
  const payload = fixture.submit();
  assert.equal(payload.animeGroupingMode, 'disabled');
  assert.equal(payload.animeGroupId, null);
  assert.equal(payload.animeGroupSeasonNumber, null);
  assert.equal(payload.seasonFolder, true);
});

test('ordinary Standard TV retains catalogue seasons and can explicitly choose a manual group without changing its native identity', (context) => {
  const fixture = createFixture('standard');
  context.after(() => fixture.cleanup());
  assert.equal(fixture.renderer.root.findAllByProps({ 'data-field': 'animeGroupingMode' }).length, 1);
  assert.equal(fixture.queries.at(-1).queryOptions.enabled, false);
  assert.equal(fixture.submit().animeGroupingMode, 'automatic');
  assert.equal(fixture.submit().seasonFolder, true);
  assert.match(JSON.stringify(fixture.renderer.toJSON()), /AnimeGroupingNativeSeasons/);
  fixture.changeField('animeGroupingMode', 'manual');
  fixture.changeField('animeGroupTitle', 'Generated TV group');
  fixture.changeField('animeGroupSeasonNumber', 3);
  fixture.changeField('animeGroupMovieFolderStyle', 'numbered');
  const payload = fixture.submit();
  assert.match(payload.animeGroupId, /^manual:[0-9a-f-]{36}$/);
  assert.equal(payload.animeGroupTitle, 'Generated TV group');
  assert.equal(payload.animeGroupSeasonNumber, 3);
  assert.equal(payload.animeGroupMovieFolderStyle, 'numbered');
  assert.equal(payload.tvdbId, 42);
  assert.equal(payload.foreignId, '42');
  assert.equal(payload.metadataSource, 'tvdb');
  assert.equal(payload.seriesType, 'standard');
});

for (const metadataSource of ['tvdb', 'tmdb']) {
  test(`Standard ${metadataSource} manual arc style submits title policy and never requests an unsupported relation preview`, (context) => {
    const fixture = createFixture('standard', 'standard', { series: { metadataSource } });
    context.after(() => fixture.cleanup());
    assert.equal(fixture.queries.at(-1).queryOptions.enabled, false);
    fixture.changeField('animeGroupingMode', 'manual');
    fixture.changeField('animeGroupTitle', 'Tokyo Revengers');
    fixture.changeField('animeGroupSeasonNumber', 3);
    fixture.changeField('animeGroupMovieFolderStyle', 'title');
    const payload = fixture.submit();
    assert.equal(payload.animeGroupMovieFolderStyle, 'title');
    assert.equal(payload.animeGroupMemberKind, 'season');
    assert.equal(payload.animeGroupMovieNumber, null);
    assert.equal(payload.metadataSource, metadataSource);
    assert.equal(payload.seriesType, 'standard');
    assert.match(JSON.stringify(fixture.renderer.toJSON()), /AnimeGroupSeasonFolderStyle/);
  });
}

test('mounted Edit manual override changes only group metadata and preserves native path, parsing and seasonFolder', (context) => {
  const fixture = createFixture('standard', 'standard', {
    editing: true,
    series: { metadataSource: 'aniList', foreignId: '42', animeGroupId: 'anilist:10', animeGroupTitle: 'Official chain', animeGroupSeasonNumber: 2 },
    existingSeries: [{ id: 10, animeGroupId: 'manual:existing', animeGroupTitle: 'Corrected group', animeGroupSeasonNumber: 1 }]
  });
  context.after(() => fixture.cleanup());
  fixture.changeField('animeGroupingMode', 'manual');
  fixture.changeField('animeGroupId', 'manual:existing');
  fixture.changeField('animeGroupSeasonNumber', 3);
  const payload = fixture.submit();
  assert.equal(payload.animeGroupId, 'manual:existing');
  assert.equal(payload.animeGroupSeasonNumber, 3);
  assert.equal(payload.path, '/existing/Generated series');
  assert.equal(payload.seasonFolder, true);
  assert.equal(payload.seriesType, 'standard');
  assert.equal(payload.metadataSource, 'aniList');
  assert.equal(payload.foreignId, '42');
  assert.equal(payload.fixtureMoveFiles, false);
});

test('mounted Edit disabling grouping clears group fields and keeps existing files in place', (context) => {
  const fixture = createFixture('anime', 'standard', {
    editing: true,
    series: { animeGroupingMode: 'manual', animeGroupId: 'manual:generated', animeGroupTitle: 'Generated group', animeGroupSeasonNumber: 2 }
  });
  context.after(() => fixture.cleanup());
  fixture.changeField('animeGroupingMode', 'disabled');
  const payload = fixture.submit();
  assert.equal(payload.animeGroupingMode, 'disabled');
  assert.equal(payload.animeGroupId, null);
  assert.equal(payload.animeGroupTitle, null);
  assert.equal(payload.animeGroupSeasonNumber, null);
  assert.equal(payload.path, '/existing/Generated series');
  assert.equal(payload.seasonFolder, true);
  assert.equal(payload.fixtureMoveFiles, false);
});

test('Add with an explicit path preserves its path and seasonFolder even when grouping resolves', (context) => {
  const fixture = createFixture('anime', 'standard', {
    series: { path: '/explicit/native-path' },
    preview: { status: 'resolved', animeGroupId: 'anilist:100', animeGroupTitle: 'Official chain', animeGroupSeasonNumber: 2, animeGroupFolder: 'Official chain' }
  });
  context.after(() => fixture.cleanup());
  const payload = fixture.submit();
  assert.equal(payload.path, '/explicit/native-path');
  assert.equal(payload.seasonFolder, true);
});

test('automatic movie preview retains movie identity and server member folder without creating a fake season', (context) => {
  const fixture = createFixture('anime', 'standard', {
    series: { animeGroupMemberKind: 'movie' },
    preview: { status: 'resolved', animeGroupId: 'anilist:100', animeGroupTitle: 'Official chain', animeGroupMemberKind: 'movie', animeGroupMovieNumber: 2, animeGroupSeasonNumber: null, animeGroupKind: 'series', animeGroupFolder: 'Official chain', animeGroupMemberFolder: 'Generated movie' }
  });
  context.after(() => fixture.cleanup());
  assert.match(JSON.stringify(fixture.renderer.toJSON()), /AnimeGroupMovieLabel/);
  const payload = fixture.submit();
  assert.equal(payload.animeGroupMemberKind, 'movie');
  assert.equal(payload.metadataSource, 'aniList');
  assert.equal(payload.foreignId, '42');
  assert.equal(payload.seasonFolder, false);
});

test('manual movie saga submits separate movie ordinal and configured folder policy', (context) => {
  const fixture = createFixture('anime', 'standard', { series: { animeGroupMemberKind: 'movie' } });
  context.after(() => fixture.cleanup());
  fixture.changeField('animeGroupingMode', 'manual');
  fixture.changeField('animeGroupMovieNumber', 4);
  fixture.changeField('animeGroupMovieFolderStyle', 'numbered');
  const payload = fixture.submit();
  assert.equal(payload.animeGroupKind, 'movieSaga');
  assert.equal(payload.animeGroupMemberKind, 'movie');
  assert.equal(payload.animeGroupMovieNumber, 4);
  assert.equal(payload.animeGroupSeasonNumber, null);
  assert.equal(payload.animeGroupMovieFolderStyle, 'numbered');
});

test('manual mixed-group movie picks unused movie number separately from occupied season numbers', (context) => {
  const fixture = createFixture('anime', 'standard', {
    series: { animeGroupMemberKind: 'movie' },
    existingSeries: [
      { id: 10, animeGroupId: 'manual:mixed', animeGroupTitle: 'Mixed group', animeGroupSeasonNumber: 1, animeGroupKind: 'series' },
      { id: 11, animeGroupId: 'manual:mixed', animeGroupTitle: 'Mixed group', animeGroupMemberKind: 'movie', animeGroupMovieNumber: 1, animeGroupKind: 'series' }
    ]
  });
  context.after(() => fixture.cleanup());
  fixture.changeField('animeGroupingMode', 'manual');
  fixture.changeField('animeGroupId', 'manual:mixed');
  const payload = fixture.submit();
  assert.equal(payload.animeGroupKind, 'series');
  assert.equal(payload.animeGroupMovieNumber, 2);
  assert.equal(payload.animeGroupSeasonNumber, null);
});

test('manual member-kind change clears the other number and movie edit leaves paths and episodes unchanged', (context) => {
  const fixture = createFixture('anime', 'standard', {
    editing: true,
    series: { animeGroupingMode: 'manual', animeGroupMemberKind: 'movie', animeGroupMovieNumber: 3, animeGroupSeasonNumber: null, animeGroupKind: 'movieSaga', animeGroupId: 'manual:existing', animeGroupTitle: 'Saga' }
  });
  context.after(() => fixture.cleanup());
  fixture.changeField('animeGroupMovieFolderStyle', 'title');
  assert.equal(fixture.submit().path, '/existing/Generated series');
  fixture.changeField('animeGroupMemberKind', 'season');
  const payload = fixture.submit();
  assert.equal(payload.animeGroupMovieNumber, null);
  assert.equal(payload.animeGroupSeasonNumber, 1);
  assert.equal(payload.animeGroupKind, 'series');
  assert.equal(payload.path, '/existing/Generated series');
  assert.equal(payload.seasonFolder, true);
  assert.equal(payload.fixtureMoveFiles, false);
});

test('explicit Anime selection updates a Daily recommendation even when Anime is already saved', (context) => {
  const fixture = createFixture('daily', 'anime');
  context.after(() => fixture.cleanup());
  fixture.selectType('anime');
  fixture.changeQuality(2);
  assert.equal(fixture.type(), 'anime');
  assert.equal(fixture.savedType(), 'anime');
  assert.equal(fixture.submit().seriesType, 'anime');
});

test('manual Daily selection is retained and submitted after other setting changes', (context) => {
  const fixture = createFixture('anime');
  context.after(() => fixture.cleanup());
  fixture.selectType('daily');
  fixture.changeQuality(2);
  assert.equal(fixture.type(), 'daily');
  assert.equal(fixture.savedType(), 'daily');
  assert.equal(fixture.submit().seriesType, 'daily');
});

for (const metadataSource of ['tmdbMovie', 'omdb']) {
  test(`${metadataSource} movie add keeps its owning identity and hides episodic-only controls despite saved anime defaults`, (context) => {
    const foreignId = metadataSource === 'omdb' ? 'tt0245429' : '42';
    const fixture = createFixture('standard', 'anime', { series: { metadataSource, foreignId, mediaType: 'movie' } });
    context.after(() => fixture.cleanup());
    for (const field of ['seriesType', 'seasonFolder', 'seasonType', 'orderingId']) assert.equal(fixture.renderer.root.findAllByProps({ 'data-field': field }).length, 0);
    const payload = fixture.submit();
    assert.equal(payload.seriesType, 'standard');
    assert.equal(payload.metadataSource, metadataSource);
    assert.equal(payload.foreignId, foreignId);
    assert.equal(payload.seasonFolder, false);
    assert.equal(payload.addOptions.monitor, 'all');
    assert.equal(payload.orderingId, null);
  });
}

test('manual special membership chooses an independent unique number and clears season/movie ordinals', (context) => {
  const fixture = createFixture('anime', 'standard', {
    series: { mediaType: 'special', animeGroupMemberKind: 'special' },
    existingSeries: [
      { id: 10, animeGroupId: 'manual:mixed', animeGroupTitle: 'Mixed group', animeGroupSeasonNumber: 1 },
      { id: 11, animeGroupId: 'manual:mixed', animeGroupTitle: 'Mixed group', animeGroupMemberKind: 'movie', animeGroupMovieNumber: 1 },
      { id: 12, animeGroupId: 'manual:mixed', animeGroupTitle: 'Mixed group', animeGroupMemberKind: 'special', animeGroupSpecialNumber: 1 }
    ]
  });
  context.after(() => fixture.cleanup());
  fixture.changeField('animeGroupingMode', 'manual');
  fixture.changeField('animeGroupId', 'manual:mixed');
  const payload = fixture.submit();
  assert.equal(payload.animeGroupMemberKind, 'special');
  assert.equal(payload.animeGroupSpecialNumber, 2);
  assert.equal(payload.animeGroupSeasonNumber, null);
  assert.equal(payload.animeGroupMovieNumber, null);
  assert.equal(payload.metadataSource, 'aniList');
  assert.equal(payload.foreignId, '42');
});
