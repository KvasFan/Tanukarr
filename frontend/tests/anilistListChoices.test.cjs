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

const statusNames = ['importCurrent', 'importPlanning', 'importCompleted', 'importDropped', 'importPaused', 'importRepeating', 'importFinished', 'importReleasing', 'importUnreleased', 'importCancelled', 'importHiatus', 'excludeCurrent', 'excludePlanning', 'excludeCompleted', 'excludeDropped', 'excludePaused', 'excludeRepeating'];
const defaultFields = [
  { name: 'username', value: 'generated-account', type: 'textbox' },
  { name: 'accessToken', value: 'generated-token', type: 'textbox', hidden: 'hidden' },
  { name: 'refreshToken', value: 'generated-refresh', type: 'textbox', hidden: 'hidden' },
  { name: 'authenticateWithAniList', value: '', type: 'oAuth' },
  ...statusNames.map((name) => ({ name, value: name === 'importCurrent', type: 'checkbox' })),
  { name: 'includedCustomLists', value: ['Stored unavailable name'], type: 'tag' },
  { name: 'excludedCustomLists', value: ['Excluded stored name'], type: 'tag' },
  { name: 'customList', value: 'Legacy saved filter', type: 'textbox', advanced: true },
  { name: 'futureProviderSetting', value: 'Preserve any new provider field', type: 'textbox', advanced: true }
].map((field, order) => ({ advanced: false, label: field.name, order, errors: [], warnings: [], pending: false, ...field }));

function fixture(response) {
  const requests = [];
  const changes = [];
  const translate = (key) => key;
  const wrapper = ({ children }) => React.createElement('section', {}, children);
  const TagInput = (properties) => React.createElement('div', { 'data-tag-field': properties.name, tags: properties.tags, tagList: properties.tagList, minQueryLength: properties.minQueryLength, delimiters: properties.delimiters, onAdd: properties.onTagAdd, onDelete: properties.onTagDelete });
  const TextTag = loadSource('Components/Form/Tag/TextTagInput.tsx', { './TagInput': TagInput, 'Utilities/String/split': (value) => String(value ?? '').split(',').filter(Boolean) }).default;
  const ProviderField = loadSource('Components/Form/ProviderFieldFormGroup.tsx', {
    'Components/Form/FormInput': (properties) => properties.type === 'textTag' ? React.createElement(TextTag, properties) : React.createElement('div', { 'data-provider-field': properties.name, 'data-value': properties.value, onChange: properties.onChange }),
    'Components/Form/FormInputHelpText': (properties) => React.createElement('p', {}, properties.text),
    'Components/Form/FormLabel': wrapper,
    'Components/Form/FormRow': wrapper,
    'Utilities/String/translate': translate
  }).default;
  const Groups = loadSource('Settings/ImportLists/ImportLists/AniListFieldGroups.tsx', {
    'Components/Alert': wrapper,
    'Components/Form/FormInputHelpText': (properties) => React.createElement('p', {}, properties.text),
    'Components/Form/ProviderFieldFormGroup': ProviderField,
    'Components/Link/SpinnerButton': (properties) => React.createElement('button', { disabled: properties.isSpinning, onClick: properties.onPress }, properties.children),
    'Components/ModalSection': ({ title, children }) => React.createElement('section', { 'data-section-title': title }, children),
    'Helpers/Props': { kinds: { INFO: 'info', WARNING: 'warning' } },
    'Helpers/Hooks/useApiMutation': (options) => {
      const [isPending, setPending] = React.useState(false);
      const mutateAsync = React.useCallback(async (body) => {
        requests.push({ path: options.path, method: options.method, body });
        setPending(true);
        try { return await response(body); } finally { setPending(false); }
      }, []);
      return { mutateAsync, isPending };
    },
    'Utilities/String/translate': translate
  }).default;
  let setData;
  let renderer;
  function FormFixture() {
    const [fields, setFields] = React.useState(defaultFields);
    setData = setFields;
    const onChange = React.useCallback(({ name, value }) => {
      changes.push({ name, value });
      setFields((old) => old.map((field) => field.name === name ? { ...field, value } : field));
    }, []);
    return React.createElement(Groups, { providerData: { id: { value: 0 }, implementation: { value: 'AniListImport' }, name: { value: 'Generated test list' }, enableAutomaticAdd: { value: false }, fields }, advancedSettings: true, onChange });
  }
  TestRenderer.act(() => { renderer = TestRenderer.create(React.createElement(FormFixture)); });
  const tags = (name) => renderer.root.findByProps({ 'data-tag-field': name });
  const changeField = (name, value) => TestRenderer.act(() => setData((old) => old.map((field) => field.name === name ? { ...field, value } : field)));
  return { renderer, requests, changes, tags, changeField };
}

test('AniList grouped settings retain every status, OAuth, legacy and future provider field exactly once', () => {
  const { renderer } = fixture(async () => ({ options: [] }));
  const visible = renderer.root.findAll((item) => item.props['data-provider-field'] || item.props['data-tag-field']).map((item) => item.props['data-provider-field'] ?? item.props['data-tag-field']);
  const expected = defaultFields.filter((field) => field.hidden !== 'hidden').map((field) => field.name);
  assert.deepEqual([...visible].sort(), [...expected].sort());
  assert.equal(new Set(visible).size, visible.length);
  assert.equal(renderer.root.findAllByProps({ 'data-section-title': 'AniListIncludeStatuses' }).length, 1);
  assert.equal(renderer.root.findAllByProps({ 'data-section-title': 'AniListExcludeStatuses' }).length, 1);
  assert.match(JSON.stringify(renderer.toJSON()), /AniListExclusionPrecedenceHelp/);
  assert.match(JSON.stringify(renderer.toJSON()), /AniListTestRetentionHelp/);
  TestRenderer.act(() => renderer.unmount());
});

test('loading custom names uses current unsaved fields, preserves selections and exact comma-containing list names', async () => {
  const item = fixture(async () => ({ options: [{ value: 'Names, with spaces', name: 'Names, with spaces' }, { value: 'Second', name: 'Second' }] }));
  item.changeField('username', 'changed-unsaved-account');
  await TestRenderer.act(async () => { await item.renderer.root.findByType('button').props.onClick(); });
  assert.equal(item.requests.length, 1);
  assert.equal(item.requests[0].path, '/importlist/action/getCustomLists');
  assert.equal(item.requests[0].method, 'POST');
  assert.equal(item.requests[0].body.implementation, 'AniListImport');
  assert.equal(item.requests[0].body.enableAutomaticAdd, false);
  assert.equal(item.requests[0].body.fields.find((field) => field.name === 'username').value, 'changed-unsaved-account');
  assert.equal(item.requests[0].body.fields.find((field) => field.name === 'accessToken').value, 'generated-token');
  assert.deepEqual(item.tags('includedCustomLists').props.tags.map((tag) => tag.name), ['Stored unavailable name']);
  assert.deepEqual(item.tags('excludedCustomLists').props.tags.map((tag) => tag.name), ['Excluded stored name']);
  assert.equal(item.tags('includedCustomLists').props.minQueryLength, 0);
  assert.deepEqual(item.tags('includedCustomLists').props.delimiters, ['Tab', 'Enter']);
  TestRenderer.act(() => item.tags('includedCustomLists').props.onAdd(item.tags('includedCustomLists').props.tagList[0]));
  assert.deepEqual(item.changes.at(-1), { name: 'includedCustomLists', value: ['Stored unavailable name', 'Names, with spaces'] });
  TestRenderer.act(() => item.tags('excludedCustomLists').props.onAdd({ name: 'Typed unmatched, custom list' }));
  assert.deepEqual(item.changes.at(-1), { name: 'excludedCustomLists', value: ['Excluded stored name', 'Typed unmatched, custom list'] });
  TestRenderer.act(() => item.renderer.unmount());
});

test('custom-name request failure keeps manual include and exclude edits usable and never exposes provider response details', async () => {
  const item = fixture(async () => { throw new Error('Synthetic private response detail'); });
  await TestRenderer.act(async () => { await item.renderer.root.findByType('button').props.onClick(); });
  assert.match(JSON.stringify(item.renderer.toJSON()), /AniListCustomListsLoadError/);
  assert.doesNotMatch(JSON.stringify(item.renderer.toJSON()), /Synthetic private response detail/);
  TestRenderer.act(() => item.tags('excludedCustomLists').props.onAdd({ name: 'Manually typed list' }));
  assert.deepEqual(item.changes.at(-1).value, ['Excluded stored name', 'Manually typed list']);
  assert.deepEqual(item.tags('includedCustomLists').props.tags.map((tag) => tag.name), ['Stored unavailable name']);
  TestRenderer.act(() => item.renderer.unmount());
});

test('pending custom-name loads are deduplicated and discarded after account changes', async () => {
  let resolve;
  const item = fixture(() => new Promise((finish) => { resolve = finish; }));
  let pending;
  TestRenderer.act(() => { pending = item.renderer.root.findByType('button').props.onClick(); });
  assert.equal(item.renderer.root.findByType('button').props.disabled, true);
  await TestRenderer.act(async () => { await item.renderer.root.findByType('button').props.onClick(); });
  assert.equal(item.requests.length, 1);
  item.changeField('username', 'second-generated-account');
  await TestRenderer.act(async () => { resolve({ options: [{ value: 'Old account name', name: 'Old account name' }] }); await pending; });
  assert.deepEqual(item.tags('includedCustomLists').props.tagList, []);
  assert.equal(item.renderer.root.findByType('button').props.disabled, false);
  TestRenderer.act(() => item.renderer.unmount());
});

test('an explicit empty custom-name result is helpful and leaves manually stored names unchanged', async () => {
  const item = fixture(async () => ({ options: [] }));
  await TestRenderer.act(async () => { await item.renderer.root.findByType('button').props.onClick(); });
  assert.match(JSON.stringify(item.renderer.toJSON()), /AniListNoCustomLists/);
  assert.deepEqual(item.tags('includedCustomLists').props.tags.map((tag) => tag.name), ['Stored unavailable name']);
  TestRenderer.act(() => item.renderer.unmount());
});

test('selecting an exact loaded name retains surrounding whitespace while free typing retains its existing trimming', async () => {
  const exactName = '  Generated list with surrounding whitespace  ';
  const item = fixture(async () => ({ options: [{ value: exactName, name: exactName }] }));
  await TestRenderer.act(async () => { await item.renderer.root.findByType('button').props.onClick(); });
  TestRenderer.act(() => item.tags('includedCustomLists').props.onAdd(item.tags('includedCustomLists').props.tagList[0]));
  assert.deepEqual(item.changes.at(-1), { name: 'includedCustomLists', value: ['Stored unavailable name', exactName] });
  TestRenderer.act(() => item.tags('excludedCustomLists').props.onAdd({ name: '  Freely typed list  ' }));
  assert.deepEqual(item.changes.at(-1), { name: 'excludedCustomLists', value: ['Excluded stored name', 'Freely typed list'] });
  TestRenderer.act(() => item.renderer.unmount());
});
