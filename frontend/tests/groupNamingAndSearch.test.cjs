const assert = require('node:assert/strict');
const fs = require('node:fs');
const Module = require('node:module');
const path = require('node:path');
const test = require('node:test');
const React = require('react');
const TestRenderer = require('react-test-renderer');
const ts = require('typescript');

const projectRequire = Module.createRequire(path.resolve(__dirname, '../../package.json'));

function loadSource(relative, mocks = {}) {
  const filename = path.resolve(__dirname, '../src', relative);
  const output = ts.transpileModule(fs.readFileSync(filename, 'utf8'), {
    compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2020, jsx: ts.JsxEmit.React, esModuleInterop: true }
  }).outputText;
  const compiled = new Module(filename, module);
  compiled.paths = module.paths;
  compiled.require = (name) => Object.hasOwn(mocks, name) ? mocks[name] : projectRequire(name);
  // eslint-disable-next-line no-underscore-dangle
  compiled._compile(output, filename);
  return compiled.exports;
}

const translate = (key, args) => `${key}${args ? JSON.stringify(args) : ''}`;
const button = (props) => React.createElement('button', { disabled: props.isDisabled, onClick: props.onPress }, props.children);
const wrapper = ({ children }) => React.createElement('section', {}, children);
const refWrapper = React.forwardRef(({ children }, ref) => React.createElement('section', { ref }, children));
const modal = ({ children, isOpen }) => isOpen ? React.createElement('aside', {}, children) : null;
const props = { sizes: { SMALL: 'small', MEDIUM: 'medium', LARGE: 'large' }, icons: { INFO: 'info' }, kinds: { INFO: 'info', DANGER: 'danger' }, inputTypes: { TEXT: 'text', CHECK: 'check', SELECT: 'select' }, tooltipPositions: { TOP: 'top' } };
const common = {
  'Components/Alert': wrapper,
  'Components/Link/Button': button,
  'Components/Modal/Modal': modal,
  'Components/Modal/ModalBody': refWrapper,
  'Components/Modal/ModalContent': wrapper,
  'Components/Modal/ModalFooter': wrapper,
  'Components/Modal/ModalHeader': wrapper,
  'Components/ModalSection': wrapper,
  'Helpers/Props': props,
  'Utilities/String/translate': translate
};

function searchFixture(initial = {}) {
  const requests = [];
  const clears = [];
  function InteractiveSearch({ searchPayload }) {
    React.useEffect(() => { requests.push(searchPayload); }, []);
    return React.createElement('output', { 'data-search': JSON.stringify(searchPayload) });
  }
  const searchMocks = {
    ...common,
    'InteractiveSearch/InteractiveSearch': InteractiveSearch,
    'InteractiveSearch/useReleases': { useClearReleasesOnUnmount: (payload) => { clears.push(payload); } },
    'Season/formatSeason': String,
    './SeasonInteractiveSearchModalContent.module.css': {}
  };
  const Content = loadSource('Series/Search/SeasonInteractiveSearchModalContent.tsx', searchMocks).default;
  const Modal = loadSource('Series/Search/SeasonInteractiveSearchModal.tsx', {
    ...common,
    './SeasonInteractiveSearchModalContent': Content,
    './SeasonInteractiveSearchModalContent.module.css': {}
  }).default;
  const SearchButton = loadSource('Series/Search/SeasonReleaseSearchButton.tsx', { ...common, './SeasonInteractiveSearchModal': Modal }).default;
  let renderer;
  TestRenderer.act(() => { renderer = TestRenderer.create(React.createElement(SearchButton, { seriesId: 42, seasonNumber: 1, displaySeasonNumber: 3, episodeCount: 12, ...initial })); });
  return { renderer, requests, clears, Content, searchMocks };
}

test('season-pack button sends native series and season, displays the logical group season, and guards repeated presses', () => {
  const fixture = searchFixture();
  const { renderer, requests, clears } = fixture;
  assert.match(renderer.root.findByType('button').children.join(''), /SearchSeasonPacksWithNumber.*3/);
  const open = () => renderer.root.findAllByType('button')[0];
  TestRenderer.act(() => open().props.onClick());
  assert.deepEqual(requests, [{ seriesId: 42, seasonNumber: 1, seasonPacksOnly: true }]);
  assert.equal(open().props.disabled, true);
  TestRenderer.act(() => open().props.onClick());
  assert.equal(requests.length, 1, 'an open search is not mounted or requested twice');
  assert.deepEqual(clears.at(-1), requests[0], 'release cache cleanup uses the same packs-only query key');
  assert.match(JSON.stringify(renderer.toJSON()), /InteractiveSearchModalHeaderSeason.*3/);
  assert.match(JSON.stringify(renderer.toJSON()), /SearchSeasonPacksHelpText/);
  TestRenderer.act(() => renderer.root.findAllByType('button').at(-1).props.onClick());
  assert.equal(open().props.disabled, false);
  TestRenderer.act(() => open().props.onClick());
  assert.equal(requests.length, 2, 'closing allows an intentional fresh search');
  TestRenderer.act(() => renderer.unmount());
});

test('empty seasons cannot initiate a release search', () => {
  const { renderer, requests } = searchFixture({ episodeCount: 0 });
  assert.equal(renderer.root.findByType('button').props.disabled, true);
  TestRenderer.act(() => renderer.root.findByType('button').props.onClick());
  assert.equal(requests.length, 0);
  TestRenderer.act(() => renderer.unmount());
});

test('existing full interactive season search keeps episode fallback and its original payload', () => {
  const fixture = searchFixture();
  let renderer;
  TestRenderer.act(() => { renderer = TestRenderer.create(React.createElement(fixture.Content, { seriesId: 77, seasonNumber: 2, episodeCount: 12, onModalClose: () => undefined })); });
  assert.deepEqual(fixture.requests, [{ seriesId: 77, seasonNumber: 2 }]);
  assert.doesNotMatch(JSON.stringify(renderer.toJSON()), /SearchSeasonPacksHelpText/);
  TestRenderer.act(() => { renderer.unmount(); fixture.renderer.unmount(); });
});

test('complete-series search remains a read-only release query with the complete-series criterion', () => {
  const fixture = searchFixture();
  const Content = loadSource('Series/Search/CompleteSeriesSearchModalContent.tsx', fixture.searchMocks).default;
  let renderer;
  TestRenderer.act(() => { renderer = TestRenderer.create(React.createElement(Content, { seriesId: 91, onModalClose: () => undefined })); });
  assert.deepEqual(fixture.requests, [{ seriesId: 91, completeSeries: true }]);
  TestRenderer.act(() => { renderer.unmount(); fixture.renderer.unmount(); });
});

function namingFixture() {
  const updates = [];
  const defaults = {
    renameEpisodes: true, replaceIllegalCharacters: false, colonReplacementFormat: 0, customColonReplacementFormat: '', multiEpisodeStyle: 0,
    standardEpisodeFormat: '{Series Title} S{season:00}E{episode:00}', dailyEpisodeFormat: '{Series Title} {Air-Date}', animeEpisodeFormat: '{Series Title} {absolute:000}',
    seriesFolderFormat: '{Series Title}', seasonFolderFormat: 'Season {season:00}', specialsFolderFormat: 'Specials',
    animeGroupFolderFormat: '{Group Title}', animeGroupSeasonFolderFormat: 'Season {Group Season:00}', animeGroupArcFolderFormat: '{Group Arc Title}', animeGroupMovieFolderFormat: '{Series Title}', animeGroupMovieSagaFolderFormat: 'Film {Group Movie:00}'
  };
  const Option = loadSource('Settings/MediaManagement/Naming/NamingOption.tsx', {
    ...common,
    'Components/Icon': () => null,
    'Components/Link/Link': button,
    'Components/Markdown/InlineMarkdown': () => null,
    'Components/Tooltip/Tooltip': () => null,
    './NamingOption.module.css': {}
  }).default;
  const Modal = loadSource('Settings/MediaManagement/Naming/NamingModal.tsx', {
    ...common,
    'Components/Icon': () => null,
    'Components/Form/SelectInput': (properties) => React.createElement('select', properties),
    'Components/Form/TextInput': (properties) => React.createElement('textarea', properties),
    './NamingOption': Option,
    './NamingModal.module.css': {}
  }).default;
  const saveSettings = () => undefined;
  const Naming = loadSource('Settings/MediaManagement/Naming/Naming.tsx', {
    ...common,
    'Components/FieldSet': wrapper,
    'Components/Form/Form': wrapper,
    'Components/Form/FormInput': (properties) => React.createElement('div', { 'data-input': properties.name, onChange: properties.onChange, 'data-value': properties.value, 'data-errors': properties.errors }, properties.buttons),
    'Components/Form/FormInputButton': button,
    'Components/Form/FormInputHelpText': (properties) => React.createElement('p', {}, properties.text),
    'Components/Form/FormLabel': wrapper,
    'Components/Form/FormRow': wrapper,
    'Components/Loading/LoadingIndicator': () => null,
    'Helpers/Hooks/useDebounce': (value) => value,
    'Helpers/Hooks/useModalOpenState': loadSource('Helpers/Hooks/useModalOpenState.ts').default,
    'Settings/advancedSettingsStore': { useShowAdvancedSettings: () => true },
    './NamingModal': Modal,
    './useNamingSettings': {
      useManageNamingSettings: () => {
        const [values, setValues] = React.useState(defaults);
        const updateSetting = React.useCallback((name, value) => { updates.push({ name, value }); setValues((old) => ({ ...old, [name]: value })); }, []);
        return { settings: Object.fromEntries(Object.entries(values).map(([key, value]) => [key, { value, errors: [], warnings: [] }])), updateSetting, isFetching: false, error: null, hasSettings: true, hasPendingChanges: false, isSaving: false, saveSettings };
      },
      useNamingExamples: () => ({ examples: { singleEpisodeExample: 'Native S01E01', multiEpisodeExample: 'Native S01E01-E02', dailyEpisodeExample: 'Native 2026-10-13', animeEpisodeExample: 'Native 001', animeMultiEpisodeExample: 'Native 001-002', seriesFolderExample: 'Native', seasonFolderExample: 'Season 01', specialsFolderExample: 'Specials', animeGroupFolderExample: 'Group', animeGroupSeasonFolderExample: 'Season 02', animeGroupArcFolderExample: 'War Of Three Titans Arc', animeGroupMovieFolderExample: 'Movie title', animeGroupMovieSagaFolderExample: 'Film 01', animeGroupMovieEpisodeExample: 'Group Film 01 Native episode 001' } })
    },
    './Naming.module.css': {}
  }).default;
  let renderer;
  TestRenderer.act(() => { renderer = TestRenderer.create(React.createElement(Naming, { setChildSave: () => undefined, onChildStateChange: () => undefined })); });
  return { renderer, updates, defaults };
}

test('all five group folder formats retain distinct settings, examples and editable token pickers', () => {
  const { renderer, updates, defaults } = namingFixture();
  const names = ['animeGroupFolderFormat', 'animeGroupSeasonFolderFormat', 'animeGroupArcFolderFormat', 'animeGroupMovieFolderFormat', 'animeGroupMovieSagaFolderFormat'];
  for (const name of names) {
    const input = renderer.root.findByProps({ 'data-input': name });
    assert.equal(input.props['data-value'], defaults[name]);
    TestRenderer.act(() => input.props.onChange({ name, value: `Prefix ${defaults[name]}` }));
    assert.deepEqual(updates.at(-1), { name, value: `Prefix ${defaults[name]}` });
    TestRenderer.act(() => input.findByType('button').props.onClick());
    assert.equal(renderer.root.findByType('textarea').props.name, name);
    TestRenderer.act(() => renderer.root.findAllByType('button').at(-1).props.onClick());
  }
  assert.match(JSON.stringify(renderer.toJSON()), /Group Film 01 Native episode 001/);
  TestRenderer.act(() => renderer.unmount());
});

test('actual naming token clicks insert group tokens at the caret and keep native season and episode tokens', () => {
  const { renderer, updates } = namingFixture();
  TestRenderer.act(() => renderer.root.findByProps({ 'data-input': 'animeEpisodeFormat' }).findByType('button').props.onClick());
  const textarea = () => renderer.root.findByType('textarea');
  const tokenButton = (token) => renderer.root.findAllByType('button').find((item) => item.findAllByType('div').some((element) => element.children.length === 1 && element.children[0] === token));
  for (const token of ['{Group Title}', '{Group Arc Title}', '{Group Season:00}', '{Group Movie:00}']) {
    assert.ok(tokenButton(token), `token picker exposes ${token}`);
    TestRenderer.act(() => tokenButton(token).props.onClick());
    assert.equal(updates.at(-1).name, 'animeEpisodeFormat');
    assert.ok(updates.at(-1).value.endsWith(token));
  }
  assert.ok(tokenButton('{season:00}'));
  assert.ok(tokenButton('{episode:00}'));
  TestRenderer.act(() => textarea().props.onSelectionChange(0, 0));
  TestRenderer.act(() => tokenButton('{Group Title}').props.onClick());
  assert.ok(updates.at(-1).value.startsWith('{Group Title}'), 'the actual token handler respects the selected caret');
  TestRenderer.act(() => renderer.unmount());
});

test('group folder pickers offer only the matching validated token families while native and filename pickers retain their choices', () => {
  const { renderer } = namingFixture();
  const tokens = () => renderer.root.findByType('aside').findAllByType('div')
    .filter((item) => item.children.length === 1 && typeof item.children[0] === 'string' && item.children[0].startsWith('{'))
    .map((item) => item.children[0]);
  const open = (name) => TestRenderer.act(() => renderer.root.findByProps({ 'data-input': name }).findByType('button').props.onClick());
  const close = () => TestRenderer.act(() => renderer.root.findAllByType('button').at(-1).props.onClick());
  const common = ['{Series Title}', '{Series Year}', '{Group Title}', '{Group Arc Title}'];
  for (const name of ['animeGroupFolderFormat', 'animeGroupSeasonFolderFormat', 'animeGroupArcFolderFormat', 'animeGroupMovieFolderFormat', 'animeGroupMovieSagaFolderFormat']) {
    open(name);
    const expected = [...common];
    if (name === 'animeGroupSeasonFolderFormat') { expected.push('{Group Season:00}'); }
    if (name === 'animeGroupMovieFolderFormat' || name === 'animeGroupMovieSagaFolderFormat') { expected.push('{Group Movie:00}'); }
    assert.deepEqual([...new Set(tokens())].sort(), expected.sort(), `${name} matches the backend's strict folder-token families`);
    close();
  }
  open('seriesFolderFormat');
  assert.ok(tokens().includes('{Series TitleYear}'));
  assert.ok(tokens().includes('{TvdbId}'));
  close();
  open('animeEpisodeFormat');
  assert.ok(tokens().includes('{season:00}'));
  assert.ok(tokens().includes('{episode:00}'));
  assert.ok(tokens().includes('{Group Movie:00}'));
  assert.ok(tokens().includes('{Group Arc Title}'));
  TestRenderer.act(() => renderer.unmount());
});

test('actual poster and banner components use the generated Tanuki placeholders and preserve native cover identity', () => {
  const previousWindow = globalThis.window;
  try {
    for (const urlBase of ['', '/configured-base']) {
      globalThis.window = { Sonarr: { urlBase } };
      for (const [component, coverType, filename] of [['SeriesPoster', 'poster', 'poster-dark.png'], ['SeriesBanner', 'banner', 'banner-dark.png']]) {
        const expectedUrl = `${urlBase}/Content/Images/${filename}`;
        const Component = loadSource(`Series/${component}.tsx`, {
          './SeriesImage': (properties) => React.createElement('img', { src: properties.placeholder, 'data-cover-type': properties.coverType, 'data-size': properties.size, 'data-native-image-url': properties.images[0].url })
        }).default;
        let renderer;
        TestRenderer.act(() => { renderer = TestRenderer.create(React.createElement(Component, { images: [{ coverType, url: '/MediaCover/42/native.jpg' }], title: 'Generated identity' })); });
        const image = renderer.root.findByType('img');
        assert.equal(image.props.src, expectedUrl, 'the actual component uses the supported Content route and configured URL base');
        assert.equal(image.props['data-cover-type'], coverType);
        assert.equal(image.props['data-native-image-url'], '/MediaCover/42/native.jpg');
        assert.equal(image.props['data-size'], coverType === 'poster' ? 250 : 70);
        TestRenderer.act(() => renderer.unmount());
      }
    }
  } finally {
    globalThis.window = previousWindow;
  }
});
