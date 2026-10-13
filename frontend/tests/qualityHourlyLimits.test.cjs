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

function text(renderer) {
  return JSON.stringify(renderer.toJSON());
}

function inputNode(renderer, name) {
  return renderer.root.findAllByType('input').find((input) => input.props.name === name);
}

function fixture({ mixed = false, failedSave = false, codecError = false, codecs = { 'MPEG-2': 0.5, 'H.264': 1, HEVC: 1.67, AV1: 2.22, Other: 1 }, extraMocks = {} } = {}) {
  const requests = [];
  const invalidations = [];
  const queryClient = { invalidateQueries: (options) => invalidations.push(options), setQueryData() {
    return undefined;
  } };
  const originals = [{ id: 2, quality: { id: 2, name: 'HDTV-720p' }, title: 'HDTV-720p', weight: 20,
    minSize: 1, preferredSize: mixed ? null : 95, maxSize: 199.9,
    minSizeIsMixed: false, preferredSizeIsMixed: mixed, maxSizeIsMixed: false },
  { id: 3, quality: { id: 3, name: 'WEB-720p' }, title: 'WEB-720p', weight: 30,
    minSize: 0, preferredSize: null, maxSize: null,
    minSizeIsMixed: false, preferredSizeIsMixed: false, maxSizeIsMixed: false }];
  let save = null;
  const mocks = {
    'Utilities/String/translate': (key, values = {}) => (english[key] ?? key).replace(/\{(\w+)\}/g, (_, name) => values[name] ?? ''),
    '@tanstack/react-query': { useQueryClient: () => queryClient },
    'Helpers/Props': { kinds: { DANGER: 'danger' }, tooltipPositions: { BOTTOM: 'bottom' } },
    'Components/Alert': ({ children }) => React.createElement('div', { role: 'alert' }, children),
    'Components/Link/Button': ({ children, onPress }) => React.createElement('button', { onClick: onPress }, children),
    'Components/LoadingIndicator': () => React.createElement('div', { 'data-loading': true }),
    'Components/FieldSet': ({ legend, children }) => React.createElement('fieldset', null, React.createElement('legend', null, legend), children),
    'Components/Form/FormInputHelpText': ({ text: help }) => React.createElement('p', null, help),
    'Components/Tooltip/Popover': ({ anchor, body }) => React.createElement('div', null, anchor, body),
    'react-slider': (props) => React.createElement('div', { 'data-slider': true, 'data-values': props.value, 'data-max': props.max, onChange: props.onChange }),
    'Helpers/Hooks/useApiQuery': ({ path: queryPath }) => ({ data: queryPath.endsWith('/codecs') ? codecs : originals,
      isFetched: true, isFetching: false, error: queryPath.endsWith('/codecs') && codecError ? new Error('Private codec response') : null }),
    'Helpers/Hooks/useApiMutation': () => ({ mutate() {
      return undefined;
    }, isPending: false, error: null }),
    'Settings/useSettings': { useSaveSettings: function useSaveSettings(requestPath, onSuccess) {
      const [saveError, setSaveError] = React.useState(null);
      const onSave = React.useCallback((body) => {
        requests.push({ path: requestPath, body: structuredClone(body) });

        if (failedSave) {
          setSaveError(new Error('Private server response with credentials'));
        } else {
          onSuccess();
        }
      }, [requestPath, onSuccess]);
      return { save: onSave, isSaving: false, saveError };
    } },
    ...extraMocks
  };
  const load = loader(mocks);
  const Definitions = load('Settings/Quality/Definition/QualityDefinitions.tsx').default;
  const ProfileSize = load('Settings/Profiles/Quality/QualityProfileItemSize.tsx').default;
  let renderer = null;
  TestRenderer.act(() => {
    renderer = TestRenderer.create(React.createElement(Definitions, { isResettingQualityDefinitions: false,
      setChildSave: (callback) => {
        save = callback;
      }, onChildStateChange() {
        return undefined;
      } }));
  });
  return { originals, requests, invalidations, renderer, ProfileSize, load,
    input: (name) => inputNode(renderer, name),
    edit(name, value) {
      TestRenderer.act(() => this.input(name).props.onChange({ target: { value } }));
    },
    save() {
      TestRenderer.act(() => save());
    },
    close() {
      TestRenderer.act(() => renderer.unmount());
    } };
}

function sortableProfileFixture(grouped) {
  const registrations = new Map();
  const changes = [];
  const item = fixture({ extraMocks: {
    '@dnd-kit/react/sortable': { useSortable: function useSortable(options) {
      const state = React.useRef(null);

      if (state.current == null) {
        state.current = { element: null, handle: null };
      }

      const registration = state.current;
      registration.disabled = options.disabled;
      registrations.set(options.id, registration);
      const ref = React.useCallback((node) => {
        registration.element = node;
      }, [registration]);
      const handleRef = React.useCallback((node) => {
        registration.handle = node;
      }, [registration]);
      return { ref, handleRef, isDragging: false };
    } },
    '@dnd-kit/react': { useDragOperation: () => ({ source: null }) },
    '@dnd-kit/abstract': { CollisionPriority: { Low: 0, Normal: 1 } },
    'Helpers/Props': { kinds: { DANGER: 'danger' }, tooltipPositions: { BOTTOM: 'bottom' }, icons: {} },
    'Components/Icon': () => null,
    'Components/Label': ({ children }) => React.createElement('span', null, children),
    'Components/Link/IconButton': ({ title, onPress }) => React.createElement('button', { onClick: onPress }, title),
    'Components/Form/CheckInput': ({ name, value, isDisabled }) => React.createElement('input', { type: 'checkbox', name, checked: value, disabled: isDisabled })
  } });
  const Component = item.load(`Settings/Profiles/Quality/${grouped ? 'QualityProfileItemGroup' : 'QualityProfileItem'}.tsx`).default;
  const noop = () => undefined;
  const shared = { index: 0, name: 'HDTV-720p', allowed: true, onSizeChange: (change) => changes.push(change) };
  const props = grouped ? { ...shared, groupId: 1000,
    items: [{ quality: { id: 2, name: 'HDTV-720p' }, allowed: true, minSize: 1, preferredSize: 95, maxSize: 199.9 }],
    failuresByQualityId: new Map(), onGroupAllowedChange: noop, onItemAllowedChange: noop,
    onItemGroupNameChange: noop, onDeleteGroupPress: noop } :
    { ...shared, containerId: 'root', qualityId: 2, minSize: 1, preferredSize: 95, maxSize: 199.9,
      onItemAllowedChange: noop, onCreateGroupPress: noop };
  let renderer = null;
  TestRenderer.act(() => {
    renderer = TestRenderer.create(React.createElement(Component, { ...props, mode: 'editGroups' }),
      { createNodeMock: (node) => ({ type: node.type }) });
  });
  return { renderer, registrations, changes,
    setMode(mode) {
      TestRenderer.act(() => renderer.update(React.createElement(Component, { ...props, mode })));
    },
    close() {
      TestRenderer.act(() => renderer.unmount());
      item.close();
    } };
}

for (const grouped of [false, true]) {
  test(`size editing detaches ${grouped ? 'group and nested item' : 'item'} sortable elements while retaining editable hour inputs`, () => {
    const item = sortableProfileFixture(grouped);
    const ids = grouped ? ['g-1000', 'q-2'] : ['q-2'];

    for (const id of ids) {
      assert.ok(item.registrations.get(id).element);
      assert.ok(item.registrations.get(id).handle);
      assert.equal(item.registrations.get(id).disabled, false);
    }

    item.setMode('editSizes');

    for (const id of ids) {
      assert.equal(item.registrations.get(id).element, null);
      assert.equal(item.registrations.get(id).handle, null);
      assert.equal(item.registrations.get(id).disabled, true);
    }

    TestRenderer.act(() => inputNode(item.renderer, '2.min').props.onChange({ target: { value: '120' } }));
    assert.deepEqual(item.changes.at(-1), { qualityId: 2, minSize: 2, preferredSize: 95, maxSize: 199.9 });
    item.setMode('editGroups');

    for (const id of ids) {
      assert.ok(item.registrations.get(id).element);
      assert.ok(item.registrations.get(id).handle);
      assert.equal(item.registrations.get(id).disabled, false);
    }

    assert.equal(inputNode(item.renderer, '2.min'), undefined);
    item.close();
  });
}

test('actual quality rows show all stored limits as editable one-hour values and corresponding GiB', () => {
  const item = fixture();
  assert.equal(item.renderer.root.findAllByType('input').filter((input) => input.props.type === 'number').length, 6);
  assert.equal(item.input('2.minSize').props.value, '60');
  assert.equal(item.input('2.preferredSize').props.value, '5700');
  assert.equal(item.input('2.maxSize').props.value, '11994');
  assert.match(text(item.renderer), /5\.57 GiB\/h/);
  assert.match(text(item.renderer), /MiB/);
  assert.deepEqual(item.requests, []);
  item.close();
});

test('title-only Save omits every size field and preserves all existing rows and profile variations', () => {
  const item = fixture({ mixed: true });
  TestRenderer.act(() => item.renderer.root.findByProps({ name: '2.HDTV-720p', type: 'text' }).props.onChange({ target: { value: 'New quality title' } }));
  item.save();
  assert.deepEqual(item.requests, [{ path: '/qualitydefinition', body: [
    { id: 2, quality: { id: 2, name: 'HDTV-720p' }, title: 'New quality title', weight: 20 }
  ] }]);
  assert.equal(item.originals[0].preferredSizeIsMixed, true);
  assert.equal(item.originals.length, 2);
  assert.deepEqual(item.invalidations, [{ queryKey: ['/qualityprofile'] }]);
  item.close();
});

test('editing a single hour limit converts it to minutes and omits untouched limits and mixed metadata', () => {
  const item = fixture({ mixed: true });
  item.edit('2.minSize', '120');
  item.save();
  const request = item.requests[0].body[0];
  assert.equal(request.minSize, 2);
  assert.ok(!Object.hasOwn(request, 'preferredSize'));
  assert.ok(!Object.hasOwn(request, 'maxSize'));
  assert.ok(!Object.hasOwn(request, 'preferredSizeIsMixed'));
  assert.equal(item.originals[0].preferredSizeIsMixed, true);
  item.close();
});

test('mixed Preferred values are visible and explicitly clearing them sends null across profiles', () => {
  const item = fixture({ mixed: true });
  assert.equal(item.input('2.preferredSize').props.placeholder, 'Mixed');
  assert.match(text(item.renderer), /Varies between quality profiles/);
  item.edit('2.preferredSize', '');
  assert.equal(item.input('2.preferredSize').props.placeholder, 'Unlimited');
  item.save();
  assert.equal(item.requests[0].body[0].preferredSize, null);
  assert.ok(Object.hasOwn(item.requests[0].body[0], 'preferredSize'));
  item.close();
});

test('focusing and blurring a mixed limit leaves all profile values unchanged', () => {
  const item = fixture({ mixed: true });
  TestRenderer.act(() => {
    item.input('2.preferredSize').props.onFocus({});
    item.input('2.preferredSize').props.onBlur({});
  });
  assert.equal(item.input('2.preferredSize').props.placeholder, 'Mixed');
  item.save();
  assert.deepEqual(item.requests[0].body, []);
  item.close();
});

test('the explicit Unlimited action clears an already empty mixed limit intentionally', () => {
  const item = fixture({ mixed: true });
  TestRenderer.act(() => item.renderer.root.findByType('button').props.onClick());
  item.save();
  assert.ok(Object.hasOwn(item.requests[0].body[0], 'preferredSize'));
  assert.equal(item.requests[0].body[0].preferredSize, null);
  assert.ok(!Object.hasOwn(item.requests[0].body[0], 'maxSize'));
  item.close();
});

test('focusing the displayed zero of an unset minimum does not rewrite it as a global numeric limit', () => {
  const item = fixture();
  TestRenderer.act(() => {
    item.input('3.minSize').props.onFocus({});
    item.input('3.minSize').props.onBlur({});
  });
  item.save();
  assert.deepEqual(item.requests[0].body, []);
  item.close();
});

test('unlimited maximum can become a finite hour limit while other fields remain omitted', () => {
  const item = fixture();
  assert.equal(item.input('3.maxSize').props.placeholder, 'Unlimited');
  item.edit('3.maxSize', '6144');
  item.save();
  assert.equal(item.requests[0].body[0].maxSize, 102.4);
  assert.ok(!Object.hasOwn(item.requests[0].body[0], 'preferredSize'));
  assert.ok(!Object.hasOwn(item.requests[0].body[0], 'minSize'));
  item.close();
});

test('an edited maximum zero displays Unlimited while minimum and preferred zeros retain their meaning', () => {
  const item = fixture();
  item.edit('2.maxSize', '0');
  assert.equal(item.input('2.maxSize').props.value, '');
  assert.equal(item.input('2.maxSize').props.placeholder, 'Unlimited');
  const maximum = item.renderer.root.findAllByProps({ className: 'limit' }).find((limit) =>
    limit.findAllByType('input').some((input) => input.props.name === '2.maxSize'));
  assert.deepEqual(maximum.findByProps({ className: 'human' }).children, ['Unlimited']);
  item.edit('2.minSize', '0');
  item.edit('2.preferredSize', '0');
  assert.equal(item.input('2.minSize').props.value, '0');
  assert.equal(item.input('2.preferredSize').props.value, '0');
  item.save();
  assert.equal(item.requests[0].body[0].maxSize, 0);
  assert.equal(item.requests[0].body[0].minSize, 0);
  assert.equal(item.requests[0].body[0].preferredSize, 0);
  item.close();
});

test('a profile maximum zero displays Unlimited without rewriting the original value on focus or blur', () => {
  const item = fixture();
  const changes = [];
  let renderer = null;
  TestRenderer.act(() => {
    renderer = TestRenderer.create(React.createElement(item.ProfileSize, { id: 2, minSize: 0, preferredSize: 0, maxSize: 0,
      onSizeChange: (change) => changes.push(change) }));
  });
  assert.equal(inputNode(renderer, '2.min').props.value, '0');
  assert.equal(inputNode(renderer, '2.preferred').props.value, '0');
  assert.equal(inputNode(renderer, '2.max').props.value, '');
  assert.equal(inputNode(renderer, '2.max').props.placeholder, 'Unlimited');
  TestRenderer.act(() => {
    inputNode(renderer, '2.max').props.onFocus({});
    inputNode(renderer, '2.max').props.onBlur({});
  });
  assert.deepEqual(changes, []);
  TestRenderer.act(() => renderer.unmount());
  item.close();
});

test('an unlimited profile maximum zero keeps the upper slider position and survives another limit edit', () => {
  const item = fixture();
  const changes = [];
  let renderer = null;
  TestRenderer.act(() => {
    renderer = TestRenderer.create(React.createElement(item.ProfileSize, { id: 2, minSize: 1, preferredSize: 95, maxSize: 0,
      onSizeChange: (change) => changes.push(change) }));
  });
  const values = renderer.root.findByProps({ 'data-slider': true }).props['data-values'];
  assert.ok(values[0] < values[1]);
  assert.ok(values[1] < values[2]);
  assert.equal(values[2], Math.round((Math.pow(1000, 1 / 1.1) + Number.EPSILON) * 10) / 10);
  TestRenderer.act(() => inputNode(renderer, '2.min').props.onChange({ target: { value: '120' } }));
  assert.equal(changes.at(-1).minSize, 2);
  assert.equal(changes.at(-1).preferredSize, 95);
  assert.equal(changes.at(-1).maxSize, 0);
  assert.equal(renderer.root.findByProps({ 'data-slider': true }).props['data-values'][2], values[2]);
  TestRenderer.act(() => renderer.unmount());
  item.close();
});

test('equal profile minimum, preferred and maximum limits remain editable without artificial gaps', () => {
  const item = fixture();
  const changes = [];
  let renderer = null;
  TestRenderer.act(() => {
    renderer = TestRenderer.create(React.createElement(item.ProfileSize, { id: 2, minSize: 95, preferredSize: 95, maxSize: 95,
      onSizeChange: (change) => changes.push(change) }));
  });

  for (const field of ['min', 'preferred', 'max']) {
    const input = inputNode(renderer, `2.${field}`);
    assert.equal(input.props.value, '5700');
    assert.ok(input.props.min <= 5700);
    assert.ok(input.props.max >= 5700);
    TestRenderer.act(() => input.props.onChange({ target: { value: '5700' } }));
    assert.deepEqual(changes.at(-1), { qualityId: 2, minSize: 95, preferredSize: 95, maxSize: 95 });
  }

  TestRenderer.act(() => renderer.unmount());
  item.close();
});

test('a 900 MiB per minute profile maximum is displayed and edited exactly within the API range', () => {
  const item = fixture();
  const changes = [];
  let renderer = null;
  TestRenderer.act(() => {
    renderer = TestRenderer.create(React.createElement(item.ProfileSize, { id: 2, minSize: 1, preferredSize: 95, maxSize: 900,
      onSizeChange: (change) => changes.push(change) }));
  });
  const maximum = inputNode(renderer, '2.max');
  const slider = renderer.root.findByProps({ 'data-slider': true });
  assert.equal(maximum.props.value, '54000');
  assert.equal(maximum.props.max, 60000);
  assert.ok(slider.props['data-values'][2] <= slider.props['data-max']);
  TestRenderer.act(() => maximum.props.onChange({ target: { value: '57000' } }));
  assert.deepEqual(changes.at(-1), { qualityId: 2, minSize: 1, preferredSize: 95, maxSize: 950 });
  TestRenderer.act(() => renderer.unmount());
  item.close();
});

test('a preferred zero stays zero in the profile slider and is preserved while maximum is edited', () => {
  const item = fixture();
  const changes = [];
  let renderer = null;
  TestRenderer.act(() => {
    renderer = TestRenderer.create(React.createElement(item.ProfileSize, { id: 2, minSize: 0, preferredSize: 0, maxSize: 900,
      onSizeChange: (change) => changes.push(change) }));
  });
  assert.equal(inputNode(renderer, '2.preferred').props.value, '0');
  assert.equal(renderer.root.findByProps({ 'data-slider': true }).props['data-values'][1], 0);
  TestRenderer.act(() => inputNode(renderer, '2.max').props.onChange({ target: { value: '54000' } }));
  assert.deepEqual(changes.at(-1), { qualityId: 2, minSize: 0, preferredSize: 0, maxSize: 900 });
  assert.equal(renderer.root.findByProps({ 'data-slider': true }).props['data-values'][1], 0);
  TestRenderer.act(() => renderer.unmount());
  item.close();
});

test('reverting a common limit removes the pending edit instead of saving an empty global size update', () => {
  const item = fixture();
  item.edit('2.minSize', '120');
  item.edit('2.minSize', '60');
  item.save();
  assert.deepEqual(item.requests[0].body, []);
  item.close();
});

test('failed Save keeps edited values and renders a safe error without a private response', () => {
  const item = fixture({ failedSave: true });
  item.edit('2.maxSize', '12000');
  item.save();
  assert.equal(item.input('2.maxSize').props.value, '12000');
  assert.match(text(item.renderer), /Unable to save quality definitions/);
  assert.doesNotMatch(text(item.renderer), /Private server response|credentials/);
  item.close();
});

test('codec examples use backend-provided policy factors rather than fixed client values and remain read-only', () => {
  const item = fixture({ codecs: { 'MPEG-2': 0.5, 'H.264': 1, HEVC: 1.91, AV1: 2.22, Other: 1 } });
  const rows = item.renderer.root.findByType('tbody').findAllByType('tr');
  assert.equal(rows.length, 5);
  assert.match(text(item.renderer), /1\.91.*GiB/);
  assert.match(text(item.renderer), /2\.22.*GiB/);
  assert.equal(item.renderer.root.findAllByType('input').filter((input) => input.props.type === 'number').length, 6);
  assert.deepEqual(item.requests, []);
  item.close();
});

test('unavailable codec information does not hide or disable quality limits', () => {
  const item = fixture({ codecError: true });
  assert.match(text(item.renderer), /Codec comparison is currently unavailable/);
  assert.doesNotMatch(text(item.renderer), /Private codec response/);
  item.edit('2.minSize', '180');
  item.save();
  assert.equal(item.requests[0].body[0].minSize, 3);
  item.close();
});

for (const [field, entered, expected] of [['min', '120', 'minSize'], ['preferred', '6000', 'preferredSize'], ['max', '18000', 'maxSize']]) {
  test(`profile ${field} hour editing preserves the other raw limits rather than nonlinear slider positions`, () => {
    const item = fixture();
    const changes = [];
    let renderer = null;
    TestRenderer.act(() => {
      renderer = TestRenderer.create(React.createElement(item.ProfileSize, { id: 2, minSize: 1, preferredSize: 95, maxSize: 199.9,
        onSizeChange: (change) => changes.push(change) }));
    });
    TestRenderer.act(() => inputNode(renderer, `2.${field}`).props.onChange({ target: { value: entered } }));
    const result = changes.at(-1);
    assert.equal(result[expected], Number(entered) / 60);

    for (const [name, original] of [['minSize', 1], ['preferredSize', 95], ['maxSize', 199.9]]) {
      if (name !== expected) {
        assert.equal(result[name], original);
      }
    }

    TestRenderer.act(() => renderer.unmount());
    item.close();
  });
}
