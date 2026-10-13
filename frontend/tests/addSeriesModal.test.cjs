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

function createFixture(initialSeriesType, savedType = 'standard') {
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
    imdbId: ''
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
  const Component = loadSource('AddSeries/AddNewSeries/AddNewSeriesModalContent.tsx', mocks).default;
  let renderer = null;
  TestRenderer.act(() => {
    renderer = TestRenderer.create(React.createElement(Component, {
      series, initialSeriesType, onModalClose: () => {}
    }));
  });
  return {
    renderer,
    type: () => renderer.root.findByProps({ 'data-field': 'seriesType' }).props.value,
    savedType: () => options.seriesType,
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
