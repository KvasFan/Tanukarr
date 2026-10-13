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
  compiled.require = (name) => (Object.hasOwn(mocks, name) ? mocks[name] : projectRequire(name));
  // eslint-disable-next-line no-underscore-dangle
  compiled._compile(output, filename);
  return compiled.exports;
}

function fieldsFor(implementation) {
  const fields = {
    TmdbListImport: [
      { name: 'accountId', value: 'generated-account', type: 'textbox' },
      { name: 'authToken', value: 'generated-test-token', type: 'password' },
      { name: 'includedListIds', value: ['90'], type: 'tag' },
      { name: 'excludedListIds', value: ['91'], type: 'tag' },
      { name: 'listId', value: null, type: 'number', hidden: 'hidden' }
    ],
    TraktListImport: [
      { name: 'username', value: 'generated-account', type: 'textbox' },
      { name: 'accessToken', value: 'generated-test-token', type: 'textbox', hidden: 'hidden' },
      { name: 'includedLists', value: ['stored-unavailable-list'], type: 'tag' },
      { name: 'excludedLists', value: ['blocked-list'], type: 'tag' },
      { name: 'listname', value: '', type: 'textbox', hidden: 'hidden' },
      { name: 'years', value: '2026', type: 'textbox' }
    ],
    MDBListImport: [
      { name: 'apiKey', value: 'generated-test-key', type: 'password' },
      { name: 'includedListUrls', value: ['https://mdblist.com/lists/generated-owner/saved'], type: 'tag' },
      { name: 'excludedListUrls', value: [], type: 'tag' },
      { name: 'listUrl', value: '', type: 'textbox', hidden: 'hidden' }
    ],
    SimklUserImport: [
      { name: 'clientIdV2', value: '', type: 'textbox' },
      { name: 'clientSecretV2', value: '', type: 'password', advanced: true },
      { name: 'accessToken', value: 'generated-test-token', type: 'textbox', hidden: 'hidden' },
      { name: 'authUser', value: 'generated-user', type: 'textbox', hidden: 'hidden' },
      { name: 'signIn', value: 'startOAuth', type: 'oAuth' },
      { name: 'showType', value: 0, type: 'select', selectOptions: [{ value: 0, name: 'Shows' }, { value: 1, name: 'Anime' }] },
      { name: 'listType', value: 0, type: 'select', selectOptions: [{ value: 0, name: 'Watching' }, { value: 1, name: 'Plan to Watch' }] },
      { name: 'includedCustomListIds', value: [], type: 'tag' },
      { name: 'excludedCustomListIds', value: [], type: 'tag' }
    ],
    MyAnimeListImport: [
      { name: 'username', value: '@me', type: 'textbox' },
      { name: 'clientId', value: 'generated-test-client', type: 'textbox' },
      { name: 'clientSecret', value: '', type: 'password', advanced: true },
      { name: 'accessToken', value: 'generated-test-token', type: 'textbox', hidden: 'hidden' },
      { name: 'signIn', value: '', type: 'oAuth' },
      { name: 'includedTags', value: ['Stored, literal tag'], type: 'tag' },
      { name: 'excludedTags', value: ['Excluded, literal tag'], type: 'tag' },
      { name: 'listStatuses', value: [1, 5], type: 'tagSelect', selectOptions: [
        { value: 0, name: 'All' }, { value: 1, name: 'Watching' }, { value: 2, name: 'Completed' },
        { value: 3, name: 'On Hold' }, { value: 4, name: 'Dropped' }, { value: 5, name: 'Plan to Watch' }
      ] },
      { name: 'importFinished', value: true, type: 'checkbox' },
      { name: 'importAiring', value: true, type: 'checkbox' },
      { name: 'importUnreleased', value: true, type: 'checkbox' },
      { name: 'minimumScore', value: 0, type: 'number' },
      { name: 'listStatus', value: 0, type: 'select', hidden: 'hidden' }
    ]
  }[implementation];
  return [...fields, { name: 'futureProviderSetting', value: 'Preserve future fields', type: 'textbox', advanced: true }]
    .map((field, order) => ({ advanced: false, label: field.name, order, errors: [], warnings: [], pending: false, ...field }));
}

function fixture(implementation, response = async() => ({ options: [] })) {
  const requests = [];
  const changes = [];
  const translate = (key) => key;
  const wrapper = ({ children }) => React.createElement('section', {}, children);
  const tagSurface = (kind) => (properties) => React.createElement('div', {
    'data-tag-field': properties.name, 'data-tag-kind': kind, tags: properties.tags,
    tagList: properties.tagList, delimiters: properties.delimiters, minQueryLength: properties.minQueryLength,
    onAdd: properties.onTagAdd, onDelete: properties.onTagDelete
  });
  const TextTag = loadSource('Components/Form/Tag/TextTagInput.tsx', {
    './TagInput': tagSurface('text'), 'Utilities/String/split': (value) => String(value ?? '').split(',').filter(Boolean)
  }).default;
  const SelectTag = loadSource('Components/Form/Tag/TagSelectInput.tsx', { './TagInput': tagSurface('select') }).default;
  const ProviderField = loadSource('Components/Form/ProviderFieldFormGroup.tsx', {
    'Components/Form/FormInput': (properties) => {
      if (properties.type === 'textTag') {
        return React.createElement(TextTag, properties);
      }

      if (properties.type === 'tagSelect') {
        return React.createElement(SelectTag, properties);
      }

      return React.createElement('div', { 'data-provider-field': properties.name, 'data-input-type': properties.type, value: properties.value, onChange: properties.onChange });
    },
    'Components/Form/FormInputHelpText': (properties) => React.createElement('p', {}, properties.text),
    'Components/Form/FormLabel': wrapper, 'Components/Form/FormRow': wrapper,
    'Utilities/String/translate': translate
  }).default;
  const Groups = loadSource('Settings/ImportLists/ImportLists/ProviderListFieldGroups.tsx', {
    'Components/Alert': wrapper,
    'Components/Form/FormInputHelpText': (properties) => React.createElement('p', {}, properties.text),
    'Components/Form/ProviderFieldFormGroup': ProviderField,
    'Components/Link/SpinnerButton': (properties) => React.createElement('button', { disabled: properties.isSpinning, onClick: properties.onPress }, properties.children),
    'Components/ModalSection': ({ title, children }) => React.createElement('section', { 'data-section-title': title }, children),
    'Helpers/Props': { kinds: { INFO: 'info', WARNING: 'warning' } },
    'Helpers/Hooks/useApiMutation': function useMutation(options) {
      const [isPending, setPending] = React.useState(false);
      const mutateAsync = React.useCallback(async(body) => {
        requests.push({ path: options.path, method: options.method, body });
        setPending(true);

        try {
          return await response(body);
        } finally {
          setPending(false);
        }
      }, [options.method, options.path]);
      return { mutateAsync, isPending };
    },
    'Utilities/String/translate': translate
  }).default;
  let setData = null;
  let renderer = null;

  function FormFixture() {
    const [fields, setFields] = React.useState(() => fieldsFor(implementation));
    setData = setFields;
    const onChange = React.useCallback(({ name, value }) => {
      changes.push({ name, value });
      setFields((old) => old.map((field) => (field.name === name ? { ...field, value } : field)));
    }, []);
    return React.createElement(Groups, { providerData: {
      id: { value: 0 }, implementation: { value: implementation }, name: { value: 'Unsaved generated connection' },
      enableAutomaticAdd: { value: false }, fields
    }, advancedSettings: true, onChange });
  }

  TestRenderer.act(() => {
    renderer = TestRenderer.create(React.createElement(FormFixture));
  });
  const tags = (name) => renderer.root.findByProps({ 'data-tag-field': name });
  const changeField = (name, value) => TestRenderer.act(() => setData((old) => old.map((field) => (field.name === name ? { ...field, value } : field))));
  const load = async() => TestRenderer.act(async() => {
    await renderer.root.findByType('button').props.onClick();
  });
  const close = () => TestRenderer.act(() => renderer.unmount());
  return { renderer, requests, changes, tags, changeField, load, close };
}

test('TMDB human list names select and display positive string IDs using the current unsaved account without saving', async() => {
  const item = fixture('TmdbListImport', async() => ({ options: [
    { value: '42', name: 'Seasonal TV, favorites' }, { value: '43', name: 'Later watching' }
  ] }));
  item.changeField('accountId', 'unsaved-account');
  await item.load();
  assert.equal(item.requests.length, 1);
  assert.equal(item.requests[0].path, '/importlist/action/getAccountLists');
  assert.equal(item.requests[0].method, 'POST');
  assert.equal(item.requests[0].body.implementation, 'TmdbListImport');
  assert.equal(item.requests[0].body.enableAutomaticAdd, false);
  assert.equal(item.requests[0].body.fields.find((field) => field.name === 'accountId').value, 'unsaved-account');
  assert.equal(item.requests[0].body.fields.find((field) => field.name === 'authToken').value, 'generated-test-token');
  assert.deepEqual(item.changes, []);
  assert.deepEqual(item.tags('includedListIds').props.tags.map((tag) => tag.name), ['90']);
  assert.equal(item.tags('includedListIds').props.tagList[0].name, 'Seasonal TV, favorites (42)');
  TestRenderer.act(() => item.tags('includedListIds').props.onAdd(item.tags('includedListIds').props.tagList[0]));
  assert.deepEqual(item.changes.at(-1), { name: 'includedListIds', value: ['90', '42'] });
  assert.deepEqual(item.tags('includedListIds').props.tags.map((tag) => tag.name), ['90', 'Seasonal TV, favorites (42)']);
  assert.equal(item.requests.length, 1);
  assert.equal(item.renderer.root.findAllByProps({ 'data-provider-field': 'listId' }).length, 0);
  assert.equal(item.renderer.root.findAllByProps({ 'data-provider-field': 'futureProviderSetting' }).length, 1);
  item.close();
});

test('Trakt list choices store slugs while keeping human labels, excluded choices and unknown saved slugs', async() => {
  const item = fixture('TraktListImport', async() => ({ options: [{ value: 'watching-this-season', name: 'Watching this season' }] }));
  await item.load();
  assert.equal(item.requests[0].path, '/importlist/action/getLists');
  assert.equal(item.tags('includedLists').props.tagList[0].name, 'Watching this season (watching-this-season)');
  TestRenderer.act(() => item.tags('includedLists').props.onAdd(item.tags('includedLists').props.tagList[0]));
  TestRenderer.act(() => item.tags('excludedLists').props.onAdd(item.tags('excludedLists').props.tagList[0]));
  assert.deepEqual(item.tags('includedLists').props.tags.map((tag) => tag.id), ['stored-unavailable-list', 'watching-this-season']);
  assert.deepEqual(item.changes.at(-1), { name: 'excludedLists', value: ['blocked-list', 'watching-this-season'] });
  assert.match(JSON.stringify(item.renderer.toJSON()), /ImportListsSetDifferenceHelp/);
  assert.equal(item.renderer.root.findAllByProps({ 'data-provider-field': 'listname' }).length, 0);
  item.close();
});

test('MDBList accepts complete literal URLs without exposing an unsupported list-loading action', () => {
  const item = fixture('MDBListImport');
  assert.equal(item.renderer.root.findAllByType('button').length, 0);
  const completeUrl = 'https://mdblist.com/lists/generated-owner/weekend-picks';
  TestRenderer.act(() => item.tags('includedListUrls').props.onAdd({ name: completeUrl }));
  assert.deepEqual(item.changes.at(-1).value, ['https://mdblist.com/lists/generated-owner/saved', completeUrl]);
  TestRenderer.act(() => item.tags('excludedListUrls').props.onAdd({ name: completeUrl }));
  assert.deepEqual(item.changes.at(-1), { name: 'excludedListUrls', value: [completeUrl] });
  assert.deepEqual(item.tags('includedListUrls').props.delimiters, ['Tab', 'Enter']);
  assert.equal(item.renderer.root.findAllByProps({ 'data-provider-field': 'listUrl' }).length, 0);
  assert.deepEqual(item.requests, []);
  item.close();
});

test('MAL preserves comma-containing list tags and numeric multi-status choices alongside account, score and airing controls', () => {
  const item = fixture('MyAnimeListImport');
  assert.equal(item.renderer.root.findAllByType('button').length, 0);
  assert.deepEqual(item.tags('includedTags').props.tags.map((tag) => tag.name), ['Stored, literal tag']);
  TestRenderer.act(() => item.tags('includedTags').props.onAdd({ name: 'Spring, favorites' }));
  TestRenderer.act(() => item.tags('excludedTags').props.onAdd({ name: 'Spring, favorites' }));
  assert.deepEqual(item.changes.at(-1).value, ['Excluded, literal tag', 'Spring, favorites']);
  assert.deepEqual(item.tags('includedTags').props.delimiters, ['Tab', 'Enter']);
  assert.equal(item.tags('listStatuses').props['data-tag-kind'], 'select');
  assert.deepEqual(item.tags('listStatuses').props.tags.map((tag) => tag.id), [1, 5]);
  TestRenderer.act(() => item.tags('listStatuses').props.onAdd({ id: 2, name: 'Completed' }));
  assert.deepEqual(item.changes.at(-1), { name: 'listStatuses', value: [1, 5, 2] });
  TestRenderer.act(() => item.tags('listStatuses').props.onDelete({ index: 0 }));
  assert.deepEqual(item.changes.at(-1), { name: 'listStatuses', value: [5, 2] });
  assert.equal(item.renderer.root.findAllByProps({ 'data-provider-field': 'listStatus' }).length, 0);

  for (const name of ['username', 'clientId', 'clientSecret', 'signIn', 'minimumScore', 'importFinished', 'importAiring', 'importUnreleased']) {
    assert.equal(item.renderer.root.findAllByProps({ 'data-provider-field': name }).length, 1, `Missing retained MAL control ${name}`);
  }

  for (const title of ['ImportListsAccount', 'ImportListsSources', 'ImportListsFilters']) {
    assert.equal(item.renderer.root.findAllByProps({ 'data-section-title': title }).length, 1);
  }

  assert.deepEqual(item.requests, []);
  item.close();
});

test('SIMKL keeps legacy bucket and OAuth controls while optional V2 list names store numeric ID strings', async() => {
  const item = fixture('SimklUserImport', async() => ({ options: [{ value: '501', name: 'Family anime, favorites' }] }));
  assert.equal(item.renderer.root.findByProps({ 'data-provider-field': 'clientIdV2' }).props.value, '');
  assert.deepEqual(item.tags('includedCustomListIds').props.tags, []);

  for (const name of ['showType', 'listType', 'signIn', 'clientIdV2', 'clientSecretV2']) {
    assert.equal(item.renderer.root.findAllByProps({ 'data-provider-field': name }).length, 1);
  }

  item.changeField('clientIdV2', 'generated-v2-application');
  item.changeField('showType', 1);
  await item.load();
  assert.equal(item.requests[0].path, '/importlist/action/getCustomLists');
  assert.equal(item.requests[0].body.fields.find((field) => field.name === 'showType').value, 1);
  assert.equal(item.requests[0].body.fields.find((field) => field.name === 'clientIdV2').value, 'generated-v2-application');
  assert.deepEqual(item.changes, []);
  TestRenderer.act(() => item.tags('includedCustomListIds').props.onAdd(item.tags('includedCustomListIds').props.tagList[0]));
  TestRenderer.act(() => item.tags('excludedCustomListIds').props.onAdd(item.tags('excludedCustomListIds').props.tagList[0]));
  assert.deepEqual(item.changes.at(-1), { name: 'excludedCustomListIds', value: ['501'] });
  assert.equal(item.tags('includedCustomListIds').props.tags[0].name, 'Family anime, favorites (501)');
  assert.equal(item.requests.length, 1);
  item.close();
});

test('SIMKL discards in-flight TV list suggestions after changing the catalogue selection to Anime', async() => {
  let resolve = null;
  const item = fixture('SimklUserImport', () => new Promise((finish) => {
    resolve = finish;
  }));
  item.changeField('clientIdV2', 'generated-v2-application');
  let pending = null;
  TestRenderer.act(() => {
    pending = item.renderer.root.findByType('button').props.onClick();
  });
  item.changeField('showType', 1);
  await TestRenderer.act(async() => {
    resolve({ options: [{ value: '501', name: 'Old TV list' }] });
    await pending;
  });
  assert.deepEqual(item.tags('includedCustomListIds').props.tagList, []);
  assert.equal(item.renderer.root.findByType('button').props.disabled, false);
  item.close();
});

test('list-loading failure retains stored IDs and usable manual editing without displaying private provider details', async() => {
  const item = fixture('TmdbListImport', async() => {
    throw new Error('Synthetic private provider response');
  });
  await item.load();
  assert.match(JSON.stringify(item.renderer.toJSON()), /ImportListsLoadListsError/);
  assert.doesNotMatch(JSON.stringify(item.renderer.toJSON()), /Synthetic private provider response/);
  assert.deepEqual(item.tags('includedListIds').props.tags.map((tag) => tag.id), ['90']);
  assert.deepEqual(item.tags('excludedListIds').props.tags.map((tag) => tag.id), ['91']);
  TestRenderer.act(() => item.tags('excludedListIds').props.onAdd({ name: '92' }));
  assert.deepEqual(item.changes.at(-1), { name: 'excludedListIds', value: ['91', '92'] });
  item.close();
});

test('in-flight loads are deduplicated and an account change discards the previous account response', async() => {
  let resolve = null;
  const item = fixture('TraktListImport', () => new Promise((finish) => {
    resolve = finish;
  }));
  let pending = null;
  TestRenderer.act(() => {
    pending = item.renderer.root.findByType('button').props.onClick();
  });
  assert.equal(item.renderer.root.findByType('button').props.disabled, true);
  await item.load();
  assert.equal(item.requests.length, 1);
  item.changeField('username', 'different-unsaved-user');
  await TestRenderer.act(async() => {
    resolve({ options: [{ value: 'old-user-list', name: 'Old account list' }] });
    await pending;
  });
  assert.deepEqual(item.tags('includedLists').props.tagList, []);
  assert.equal(item.renderer.root.findByType('button').props.disabled, false);
  assert.deepEqual(item.tags('includedLists').props.tags.map((tag) => tag.id), ['stored-unavailable-list']);
  item.close();
});

test('late errors from changed credentials do not poison the next account list-loading operation', async() => {
  let reject = null;
  let calls = 0;
  const item = fixture('TmdbListImport', () => (++calls === 1 ?
    new Promise((resolve, fail) => {
      reject = fail;
    }) :
    Promise.resolve({ options: [{ value: '77', name: 'New account list' }] })));
  let pending = null;
  TestRenderer.act(() => {
    pending = item.renderer.root.findByType('button').props.onClick();
  });
  item.changeField('authToken', 'different-generated-token');
  await TestRenderer.act(async() => {
    reject(new Error('Old account private failure'));
    await pending;
  });
  assert.doesNotMatch(JSON.stringify(item.renderer.toJSON()), /ImportListsLoadListsError|Old account private failure/);
  await item.load();
  assert.equal(item.requests.length, 2);
  assert.equal(item.requests[1].body.fields.find((field) => field.name === 'authToken').value, 'different-generated-token');
  assert.equal(item.tags('includedListIds').props.tagList[0].id, '77');
  item.close();
});

test('malformed remote choices fail safely without replacing saved selections or disabling manual entry', async() => {
  const item = fixture('TmdbListImport', async() => ({ options: [{ value: 42, name: 'Malformed numeric choice' }] }));
  await item.load();
  assert.match(JSON.stringify(item.renderer.toJSON()), /ImportListsLoadListsError/);
  assert.deepEqual(item.tags('includedListIds').props.tagList, []);
  assert.deepEqual(item.tags('includedListIds').props.tags.map((tag) => tag.id), ['90']);
  TestRenderer.act(() => item.tags('includedListIds').props.onAdd({ name: '42' }));
  assert.deepEqual(item.changes.at(-1), { name: 'includedListIds', value: ['90', '42'] });
  item.close();
});
