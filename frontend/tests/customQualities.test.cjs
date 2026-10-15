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
global.window = { addEventListener() {
  return undefined;
}, removeEventListener() {
  return undefined;
} };

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

function fixture({ copy, retired = false, action = false, fail = false, factors = false } = {}) {
  const requests = [];
  const invalidations = [];
  const state = { fail, closed: 0, edited: 0, copied: 0 };
  const defaults = { MPEG2: 0.5, H264: 1, HEVC: 1.67, AV1: 2.22, Unknown: 1 };

  function useMutation(options) {
    const [error, setError] = React.useState(null);
    return { error, isPending: false, mutate(body) {
      requests.push({ path: options.path, method: options.method, body: structuredClone(body) });

      if (state.fail) {
        setError({ statusBody: [{ errorMessage: 'Disable this quality in Generated profile first.' }] });
      } else {
        setError(null);
        options.mutationOptions.onSuccess();
      }
    } };
  }

  useMutation.getValidationFailures = (error) => ({ errors: error.statusBody ?? [], warnings: [] });
  const load = loader({
    '@tanstack/react-query': { useQueryClient: () => ({ invalidateQueries: (value) => invalidations.push(value) }) },
    'Helpers/Hooks/useApiMutation': useMutation,
    'Helpers/Hooks/useApiQuery': () => ({ data: defaults }),
    'Utilities/String/translate': (key) => english[key] ?? key,
    'Components/Alert': ({ children }) => React.createElement('div', { role: 'alert' }, children),
    'Components/Link/Button': ({ children, onPress, isDisabled }) => React.createElement('button', { onClick: onPress, disabled: isDisabled }, children)
  });
  const Editor = load('Settings/Quality/Definition/QualityPresetEditor.tsx').default;
  const Actions = load('Settings/Quality/Definition/QualityPresetActions.tsx').default;
  const Factors = load('Settings/Profiles/Quality/QualityProfileCodecFactors.tsx').default;

  function ProfilePair() {
    const [first, setFirst] = React.useState({ ...defaults, HEVC: 2.1 });
    const [second, setSecond] = React.useState(defaults);
    return React.createElement('div', null,
      React.createElement('section', { id: 'first' }, React.createElement(Factors, { value: first, onChange: setFirst })),
      React.createElement('section', { id: 'second' }, React.createElement(Factors, { value: second, onChange: setSecond })));
  }

  let renderer = null;
  TestRenderer.act(() => {
    let element = React.createElement(Editor, { copy, onClose: () => state.closed++ });

    if (factors) {
      element = React.createElement(ProfilePair);
    } else if (action) {
      element = React.createElement(Actions, { id: 31, qualityId: 19, retired, onEdit: () => state.edited++, onCopy: () => state.copied++ });
    }

    renderer = TestRenderer.create(element);
  });
  return { renderer, requests, invalidations, state,
    edit(name, value) {
      const input = renderer.root.findAll((node) => ['input', 'select'].includes(node.type) && node.props.name === name)[0];
      TestRenderer.act(() => input.props.onChange({ target: { value } }));
    },
    press(label) {
      const button = renderer.root.findAllByType('button').find((node) => node.props.children === label);
      assert.ok(button, `Button ${label} exists`);
      TestRenderer.act(() => button.props.onClick());
    },
    close() {
      TestRenderer.act(() => renderer.unmount());
    }
  };
}

function profileFixture(qualitySettings = false, id = 1) {
  const changes = [];
  const wrap = ({ children }) => React.createElement('div', {}, children);
  const values = { name: 'Generated profile', upgradeAllowed: false, cutoff: 2, minFormatScore: 0,
    minUpgradeFormatScore: 1, cutoffFormatScore: 0, formatItems: [], codecMultipliers: { H264: 1 },
    items: [{ quality: { id: 2, name: 'HDTV-720p' }, allowed: true, minSize: 1, preferredSize: 95, maxSize: 199.9 },
      { quality: { id: 3, name: 'WEB-720p' }, allowed: true, minSize: 2, preferredSize: 100, maxSize: 200 },
      { id: 1001, name: 'Generated group', allowed: true, items: [{ quality: { id: 4, name: 'Grouped quality' }, allowed: true, minSize: 3, preferredSize: 105, maxSize: 210 }] }] };
  let qualityProps = null;
  const mocks = {
    '@dnd-kit/react': { DragDropProvider: wrap },
    'Utilities/String/translate': (key) => english[key] ?? key,
    'Helpers/Props': { icons: {}, inputTypes: {}, kinds: {} },
    'Helpers/Hooks/usePrevious': () => false,
    'Settings/Profiles/Quality/useQualityProfileInUse': () => ({ seriesCount: 0, importListCount: 0 }),
    './useQualityProfiles': { useManageQualityProfile: () => ({
      item: Object.fromEntries(Object.entries(values).map(([name, value]) => [name, { value }])),
      isSaving: false, isSchemaLoading: false, isSchemaFetched: true, saveError: null,
      updateValue: (name, value) => changes.push({ name, value }), saveProvider: () => undefined,
      validationErrors: [], validationWarnings: []
    }) },
    './useQualityProfileDnd': () => ({ displayItems: [], handleDragStart: () => undefined, handleDragOver: () => undefined, handleDragEnd: () => undefined }),
    './QualityProfileItems': (props) => {
      qualityProps = props;
      return React.createElement('div', { 'data-quality-controls': true });
    },
    './QualityProfileCodecFactors': () => React.createElement('div', { 'data-codec-controls': true }),
    './QualityProfileFormatItems': wrap,
    'Components/Form/FormInput': (props) => React.createElement('input', { name: props.name, value: props.value, readOnly: true }),
    'Components/Form/FormInputHelpText': wrap,
    'Components/Icon': () => null,
    'Components/Loading/LoadingIndicator': () => null,
    'Components/Link/Button': wrap,
    'Components/Link/SpinnerErrorButton': wrap,
    'Components/Tooltip/Popover': wrap,
    'Components/Alert': wrap
  };

  for (const component of ['Form/Form', 'Form/FormLabel', 'Form/FormRow', 'Modal/ModalBody', 'Modal/ModalContent', 'Modal/ModalFooter', 'Modal/ModalHeader']) {
    mocks[`Components/${component}`] = wrap;
  }

  const Component = loader(mocks)('Settings/Profiles/Quality/EditQualityProfileModalContent.tsx').default;
  let renderer = null;
  TestRenderer.act(() => {
    renderer = TestRenderer.create(React.createElement(Component, { id: id === null ? undefined : id, qualitySettings, onModalClose: () => undefined }));
  });
  return { renderer, changes, values, qualityProps, close: () => TestRenderer.act(() => renderer.unmount()) };
}

test('the profile policy dialog keeps quality and codec management in Settings Quality', () => {
  const item = profileFixture();
  assert.equal(item.renderer.root.findAllByProps({ 'data-quality-controls': true }).length, 0);
  assert.equal(item.renderer.root.findAllByProps({ 'data-codec-controls': true }).length, 0);
  assert.equal(item.renderer.root.findAllByType('input').filter((input) => input.props.name === 'name').length, 1);
  assert.deepEqual(item.changes, []);
  item.close();
});

test('the Settings Quality dialog edits one quality limit while preserving plain and grouped peers', () => {
  const item = profileFixture(true);
  assert.equal(item.renderer.root.findAllByProps({ 'data-quality-controls': true }).length, 1);
  assert.equal(item.renderer.root.findAllByProps({ 'data-codec-controls': true }).length, 1);
  assert.equal(item.renderer.root.findAllByProps({ name: 'name' }).length, 0);
  TestRenderer.act(() => item.qualityProps.onSizeChange({ qualityId: 2, minSize: 4, preferredSize: 95, maxSize: 199.9 }));
  assert.equal(item.changes.at(-1).name, 'items');
  assert.equal(item.changes.at(-1).value[0].minSize, 4);
  assert.deepEqual(item.changes.at(-1).value.slice(1), item.values.items.slice(1));
  item.close();
});

test('new profiles created under Settings Quality expose both a name and selectable qualities', () => {
  const item = profileFixture(true, null);
  assert.equal(item.renderer.root.findAllByType('input').filter((input) => input.props.name === 'name').length, 1);
  assert.equal(item.renderer.root.findAllByProps({ 'data-quality-controls': true }).length, 1);
  TestRenderer.act(() => item.qualityProps.onItemAllowedChange(3, false));
  assert.equal(item.changes.at(-1).value[1].allowed, false);
  assert.equal(item.changes.at(-1).value[0].allowed, true);
  item.close();
});

test('new quality editor submits individually selected source, resolution, codec and expression', () => {
  const item = fixture();
  item.edit('newQualityTitle', 'Generated BdRip4k');
  item.edit('source', 'bluray');
  item.edit('resolution', '2160');
  item.edit('codec', 'HEVC');
  item.edit('titlePattern', '\\bBDRip\\b');
  item.press('Save');
  assert.deepEqual(item.requests[0], { path: '/qualitydefinition', method: 'POST', body: {
    title: 'Generated BdRip4k', source: 'bluray', resolution: 2160, codec: 'HEVC', titlePattern: '\\bBDRip\\b', copyFromId: undefined
  } });
  assert.equal(item.state.closed, 1);
  assert.deepEqual(item.invalidations.map((value) => value.queryKey[0]), ['/qualitydefinition', '/qualityprofile', '/qualityprofile/schema']);
  item.close();
});

test('copy editor preserves parent identity and original parameters while permitting independent edits', () => {
  const original = { id: 31, quality: { id: 19, source: 'bluray', resolution: 2160 }, title: 'Standard Blu-ray', codec: 'AV1', titlePattern: 'BDRip' };
  const item = fixture({ copy: original });
  item.edit('newQualityTitle', 'Generated alternative');
  item.edit('codec', 'HEVC');
  item.press('Save');
  assert.equal(item.requests[0].body.copyFromId, 31);
  assert.equal(item.requests[0].body.codec, 'HEVC');
  assert.equal(item.requests[0].body.source, 'bluray');
  assert.equal(original.codec, 'AV1');
  assert.equal(original.title, 'Standard Blu-ray');
  item.close();
});

test('failed create keeps all edits and allows a successful retry', () => {
  const item = fixture({ fail: true });
  item.edit('newQualityTitle', 'Generated retained name');
  item.edit('codec', 'HEVC');
  item.press('Save');
  assert.equal(item.state.closed, 0);
  assert.match(JSON.stringify(item.renderer.toJSON()), /Generated profile/);
  item.state.fail = false;
  item.press('Save');
  assert.deepEqual(item.requests[1].body, item.requests[0].body);
  assert.equal(item.state.closed, 1);
  item.close();
});

test('delete requires an explicit click and retains an in-use quality on failure', () => {
  const item = fixture({ action: true, fail: true });
  item.press('Delete');
  assert.equal(item.requests.length, 0);
  item.press('Confirm delete');
  assert.deepEqual(item.requests[0], { path: '/qualitydefinition/31', method: 'DELETE', body: undefined });
  assert.equal(item.invalidations.length, 0);
  assert.match(JSON.stringify(item.renderer.toJSON()), /Generated profile/);
  item.close();
});

test('restore preserves the original quality definition ID', () => {
  const item = fixture({ action: true, retired: true });
  item.press('Restore');
  assert.deepEqual(item.requests[0], { path: '/qualitydefinition/31/restore', method: 'POST', body: undefined });
  assert.equal(item.invalidations.length, 3);
  item.close();
});

test('codec factor controls display defaults and change only their own quality profile', () => {
  const item = fixture({ factors: true });
  const inputs = () => item.renderer.root.findAllByType('input');
  assert.equal(inputs().filter((node) => node.props.name === 'codecMultipliers.HEVC')[0].props.value, '2.1');
  assert.equal(inputs().filter((node) => node.props.name === 'codecMultipliers.MPEG2')[0].props.value, '0.5');
  item.edit('codecMultipliers.HEVC', '2.7');
  assert.equal(inputs().filter((node) => node.props.name === 'codecMultipliers.HEVC')[0].props.value, '2.7');
  assert.equal(inputs().filter((node) => node.props.name === 'codecMultipliers.HEVC')[1].props.value, '1.67');
  assert.equal(inputs().filter((node) => node.props.name === 'codecMultipliers.AV1')[0].props.value, '2.22');
  item.close();
});

function selectionFixture(currentId, { loaded = true } = {}) {
  const qualities = [
    { id: 0, name: 'Unknown' },
    { id: 3, name: 'Current Web 1080p' },
    { id: 19, name: 'Archived standard', isRetired: true },
    { id: 10001, name: 'Current custom' }
  ];
  const archived = { id: 10000, name: 'Archived custom', source: 'bluray', resolution: 2160, isRetired: true };
  const selected = [];
  const queries = [];
  const wrapper = ({ children }) => React.createElement('div', null, children);
  const load = loader({
    'Settings/Profiles/Quality/useQualityProfiles': { useQualityProfileSchema: () => ({ schema: { items: qualities.map((quality) => ({ quality })) }, isSchemaFetched: true, isSchemaLoading: false }) },
    'Settings/Quality/Definition/useQualityDefinitions': { useQualityDefinitions: (enabled) => {
      queries.push(enabled);
      return { data: [{ quality: archived, isRetired: true }], isFetched: loaded };
    } },
    'Helpers/Props': { inputTypes: { SELECT: 'select', CHECK: 'check' }, kinds: { DANGER: 'danger', SUCCESS: 'success' } },
    'Utilities/String/translate': (key) => english[key] ?? key,
    'Components/Alert': wrapper,
    'Components/Form/Form': wrapper,
    'Components/Form/FormLabel': wrapper,
    'Components/Form/FormRow': wrapper,
    'Components/Loading/LoadingIndicator': () => React.createElement('div', { 'data-loading': true }),
    'Components/Modal/ModalBody': wrapper,
    'Components/Modal/ModalContent': wrapper,
    'Components/Modal/ModalFooter': wrapper,
    'Components/Modal/ModalHeader': wrapper,
    'Components/Form/FormInput': (props) => (props.type === 'select' ? React.createElement('select', { name: props.name, value: props.value }, props.values.map((option) => React.createElement('option', { key: option.key, value: option.key }, option.value))) : React.createElement('input', { name: props.name })),
    'Components/Link/Button': ({ children, onPress, isDisabled }) => React.createElement('button', { disabled: isDisabled, onClick: onPress }, children)
  });
  const Content = load('InteractiveImport/Quality/SelectQualityModalContent.tsx').default;
  let renderer = null;
  TestRenderer.act(() => {
    renderer = TestRenderer.create(React.createElement(Content, { qualityId: currentId, proper: true, real: false, modalTitle: 'Generated file', onQualitySelect: (quality) => selected.push(quality), onModalClose() {
      return undefined;
    } }));
  });
  return { renderer, selected, queries, archived,
    options() {
      return renderer.root.findAllByType('option').map((option) => option.props.value);
    },
    select() {
      TestRenderer.act(() => renderer.root.findAllByType('button').find((button) => button.props.children === english.SelectQuality).props.onClick());
    },
    close() {
      TestRenderer.act(() => renderer.unmount());
    }
  };
}

test('new manual selections hide deleted standard and custom qualities without querying archived definitions', () => {
  const item = selectionFixture(0);
  assert.deepEqual(item.options(), [0, 3, 10001]);
  assert.ok(item.queries.every((enabled) => enabled === false));
  item.close();
});

test('existing archived standard quality can retain its identity and revision in the manual selector', () => {
  const item = selectionFixture(19);
  assert.deepEqual(item.options(), [0, 3, 19, 10001]);
  item.select();
  assert.equal(item.selected[0].quality.id, 19);
  assert.equal(item.selected[0].quality.isRetired, true);
  assert.equal(item.selected[0].revision.version, 2);
  item.close();
});

test('existing archived custom quality is loaded by its original ID and stays selectable for that file', () => {
  const item = selectionFixture(10000);
  assert.deepEqual(item.options(), [0, 3, 10001, 10000]);
  assert.ok(item.queries.every((enabled) => enabled === true));
  item.select();
  assert.deepEqual(item.selected[0].quality, item.archived);
  item.close();
});

test('manual selector cannot emit an undefined archived quality while its definition is still loading', () => {
  const item = selectionFixture(10000, { loaded: false });
  assert.equal(item.renderer.root.findAllByType('button').find((button) => button.props.children === english.SelectQuality).props.disabled, true);
  item.close();
});

test('retired profile quality remains visible for historical order but cannot be enabled', () => {
  const changes = [];
  const load = loader({
    '@dnd-kit/react/sortable': { useSortable: () => ({ ref: () => undefined, handleRef: () => undefined, isDragging: false }) },
    'Utilities/String/translate': (key) => english[key] ?? key,
    'Helpers/Props': { icons: {} },
    'Components/Icon': () => null,
    'Components/Link/IconButton': () => null,
    'Components/Form/CheckInput': (props) => React.createElement('input', { name: props.name, disabled: props.isDisabled, onChange: () => props.onChange({ value: true }) }),
    './useQualityProfileDnd': { qualityKey: (id) => `quality-${id}`, ROOT_CONTAINER: 'root' },
    './QualityProfileItemSize': () => null
  });
  const Item = load('Settings/Profiles/Quality/QualityProfileItem.tsx').default;
  let renderer = null;
  TestRenderer.act(() => {
    renderer = TestRenderer.create(React.createElement(Item, { mode: 'default', containerId: 'root', index: 24, qualityId: 10000,
      name: 'Archived 4K', allowed: false, isRetired: true, onItemAllowedChange: (...change) => changes.push(change), onSizeChange: () => undefined }));
  });
  assert.equal(renderer.root.findByType('input').props.disabled, true);
  TestRenderer.act(() => renderer.root.findByType('input').props.onChange());
  assert.equal(changes.length, 0);
  assert.match(JSON.stringify(renderer.toJSON()), /Archived 4K/);
  assert.match(JSON.stringify(renderer.toJSON()), /Deleted/);
  TestRenderer.act(() => renderer.unmount());
});
