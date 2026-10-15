const assert = require('node:assert/strict');
const fs = require('node:fs');
const Module = require('node:module');
const path = require('node:path');
const test = require('node:test');
const React = require('react');
const TestRenderer = require('react-test-renderer');
const ts = require('typescript');

const sourceRoot = path.resolve(__dirname, '../src');
const projectRequire = Module.createRequire(path.resolve(__dirname, '../../package.json'));
const english = JSON.parse(fs.readFileSync(path.resolve(__dirname, '../../src/NzbDrone.Core/Localization/Core/en.json'), 'utf8'));

function loader(mocks) {
  const cache = new Map();

  function load(relative) {
    const filename = path.resolve(sourceRoot, relative);

    if (cache.has(filename)) {
      return cache.get(filename).exports;
    }

    const compiled = new Module(filename, module);
    cache.set(filename, compiled);
    compiled.paths = module.paths;

    compiled.require = (name) => {
      if (Object.hasOwn(mocks, name)) {
        return mocks[name];
      }

      if (name.endsWith('.css')) {
        return { __esModule: true, default: new Proxy({}, { get: (_, key) => key }) };
      }

      const target = name.startsWith('.') ? path.resolve(path.dirname(filename), name) : path.resolve(sourceRoot, name);

      for (const extension of ['.tsx', '.ts']) {
        if (fs.existsSync(target + extension)) {
          return load(path.relative(sourceRoot, target + extension));
        }
      }

      return projectRequire(name);
    };

    const output = ts.transpileModule(fs.readFileSync(filename, 'utf8'), {
      compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2020, jsx: ts.JsxEmit.React, esModuleInterop: true }
    }).outputText;
    // eslint-disable-next-line no-underscore-dangle
    compiled._compile(output, filename);
    return compiled.exports;
  }

  return load;
}

function fixture(disabled = []) {
  let saved = null;
  const load = loader({
    'Components/Alert': ({ children }) => React.createElement('div', null, children),
    'Components/FieldSet': ({ children }) => React.createElement('section', null, children),
    'Components/Form/CheckInput': (props) => React.createElement('check-input', props),
    'Components/Link/Button': (props) => React.createElement('action-button', props),
    'Components/Markdown/InlineMarkdown': () => null,
    'Helpers/Props': { kinds: { INFO: 'info' } },
    'Utilities/String/translate': (key) => english[key] ?? key
  });
  const Sources = load('Settings/MetadataSource/Sources.tsx').default;

  function Host() {
    const [order, setOrder] = React.useState(['aniList', 'tvdb', 'tmdb', 'myAnimeList', 'tvMaze']);
    const [disabledSources, setDisabled] = React.useState(disabled);
    saved = { order, disabled: disabledSources };
    return React.createElement(Sources, { order, disabled: disabledSources, hasTmdbKey: false, onOrderChange: setOrder, onDisabledChange: setDisabled });
  }

  let renderer = null;
  TestRenderer.act(() => {
    renderer = TestRenderer.create(React.createElement(Host));
  });
  return { renderer, values: () => saved, load };
}

test('mounted source controls move catalogues and retain enabled state', () => {
  const fixtureState = fixture(['tmdb']);
  const button = fixtureState.renderer.root.findAllByType('action-button').find((item) => item.props.title === 'Move down AniList');
  TestRenderer.act(() => button.props.onPress());
  assert.deepEqual(fixtureState.values().order, ['tvdb', 'aniList', 'tmdb', 'myAnimeList', 'tvMaze']);
  assert.deepEqual(fixtureState.values().disabled, ['tmdb']);
});

test('mounted enable controls toggle independently and keep at least one source', () => {
  const fixtureState = fixture(['tvdb', 'tmdb', 'myAnimeList', 'tvMaze']);
  const inputs = () => fixtureState.renderer.root.findAllByType('check-input');
  assert.equal(inputs().find((item) => item.props.name === 'aniList').props.isDisabled, true);
  TestRenderer.act(() => inputs().find((item) => item.props.name === 'tvMaze').props.onChange({ name: 'tvMaze', value: true }));
  assert.deepEqual(fixtureState.values().disabled, ['tvdb', 'tmdb', 'myAnimeList']);
  assert.equal(inputs().find((item) => item.props.name === 'aniList').props.isDisabled, false);
  TestRenderer.act(() => inputs().find((item) => item.props.name === 'aniList').props.onChange({ name: 'aniList', value: false }));
  assert.deepEqual(fixtureState.values().disabled, ['tvdb', 'tmdb', 'myAnimeList', 'aniList']);
});

test('ordered choices remove disabled and unknown sources without duplicate entries', () => {
  const { load } = fixture();
  const { orderedMetadataSources } = load('Settings/MetadataSource/metadataSources.ts');
  assert.deepEqual(orderedMetadataSources(['tvMaze', 'tvMaze', 'unknown', 'aniList'], ['tmdb']).map(({ key }) => key), ['tvMaze', 'aniList', 'tvdb', 'myAnimeList']);
});

test('lookup sends allSources only for aggregate search and retains explicit native provider', () => {
  const requests = [];
  const load = loader({
    'Helpers/Hooks/useApiQuery': (request) => {
      requests.push(request);
      return {};
    },
    'Helpers/Hooks/useApiMutation': () => ({})
  });
  const { useLookupSeries } = load('AddSeries/AddNewSeries/useAddSeries.ts');
  useLookupSeries('  Title  ', { language: { id: 1 }, metadataSource: 'all' });
  useLookupSeries('Title', { language: { id: 1 }, metadataSource: 'tvMaze' });
  assert.deepEqual(requests[0].queryParams, { term: 'Title', language: 1, metadataSource: undefined, allSources: true });
  assert.deepEqual(requests[1].queryParams, { term: 'Title', language: 1, metadataSource: 'tvMaze', allSources: undefined });
});
