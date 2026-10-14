const assert = require('node:assert/strict');
const fs = require('node:fs');
const Module = require('node:module');
const path = require('node:path');
const test = require('node:test');
const React = require('react');
const TestRenderer = require('react-test-renderer');
const ts = require('typescript');

const projectRequire = Module.createRequire(path.resolve(__dirname, '../../package.json'));

// Use the installed dependency itself, including its genuine Node ESM namespace.
// Conventional CommonJS transpilation alone unwraps __esModule and hides the
// namespace-object default produced by the production Rolldown bundle.
function loadSeriesImage(dependency) {
  const filename = path.resolve(__dirname, '../src/Series/SeriesImage.tsx');
  const output = ts.transpileModule(fs.readFileSync(filename, 'utf8'), {
    compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2020, jsx: ts.JsxEmit.React, esModuleInterop: false }
  }).outputText;
  const compiled = new Module(filename, module);
  compiled.paths = module.paths;

  compiled.require = (name) => {
    if (name === 'react-lazyload') {
      return dependency;
    }

    if (name === 'Utilities/String/translate') {
      return { default: (key) => key };
    }

    if (name === 'react') {
      return { ...React, default: React };
    }

    return projectRequire(name);
  };

  // eslint-disable-next-line no-underscore-dangle
  compiled._compile(output, filename);
  return compiled.exports.default;
}

function withBrowser(callback, pixelRatio = 1) {
  const original = { window: global.window, document: global.document, HTMLElement: global.HTMLElement };
  global.document = { documentElement: { clientHeight: 1000, clientWidth: 1000 } };
  global.window = { Sonarr: { urlBase: '' }, devicePixelRatio: pixelRatio, innerHeight: 1000, innerWidth: 1000, addEventListener: () => undefined, removeEventListener: () => undefined };
  global.HTMLElement = class FixtureElement {
    constructor() {
      this.style = {};
      this.ownerDocument = global.document;
      this.offsetWidth = 168;
      this.offsetHeight = 247;
    }
    getBoundingClientRect() {
      return { top: 0, left: 0, width: 168, height: 247 };
    }
    getClientRects() {
      return [this.getBoundingClientRect()];
    }
  };

  try {
    return callback();
  } finally {
    Object.assign(global, original);
  }
}

function mount(Image, properties = {}) {
  let renderer = null;
  TestRenderer.act(() => {
    renderer = TestRenderer.create(React.createElement(Image, {
      images: [{ coverType: 'poster', url: '/MediaCover/42/poster.jpg' }],
      coverType: 'poster', placeholder: '/Content/Images/poster-dark.png', title: 'Generated covered group',
      ...properties
    }), { createNodeMock: () => new global.HTMLElement() });
  });
  return renderer;
}

test('a covered series mounts the real LazyLoad component through the production-style ESM namespace default', async() => {
  const dependency = await import('react-lazyload');
  assert.equal(typeof dependency.default, 'object', 'the genuine CJS namespace shape is required to exercise the regression');
  assert.equal(typeof dependency.default.default, 'function');
  withBrowser(() => {
    const Image = loadSeriesImage(dependency);
    const renderer = mount(Image);
    assert.equal(renderer.root.findByType(dependency.default.default).props.offset, 100);
    assert.equal(renderer.root.findByType('img').props.src, '/MediaCover/42/poster-250.jpg');
    assert.equal(renderer.root.findByType('img').props.alt, 'Generated covered group ImagePoster');
    TestRenderer.act(() => renderer.unmount());
  });
});

test('conventional CommonJS exports still mount the same real LazyLoad class', () => {
  const dependency = projectRequire('react-lazyload');
  withBrowser(() => {
    const renderer = mount(loadSeriesImage(dependency));
    assert.equal(renderer.root.findByType(dependency.default).props.height, 250);
    assert.equal(renderer.root.findByType('img').props.src, '/MediaCover/42/poster-250.jpg');
    TestRenderer.act(() => renderer.unmount());
  });
});

test('real lazy images retain load/error callbacks and recover when the cover URL changes', async() => {
  const dependency = await import('react-lazyload');
  withBrowser(() => {
    let loaded = 0;
    let errors = 0;
    const Image = loadSeriesImage(dependency);
    const callbacks = { onLoad: () => {
      loaded += 1;
    }, onError: () => {
      errors += 1;
    } };
    const renderer = mount(Image, callbacks);
    TestRenderer.act(() => renderer.root.findByType('img').props.onLoad());
    assert.equal(loaded, 1);
    TestRenderer.act(() => renderer.root.findByType('img').props.onError());
    assert.equal(errors, 0, 'a missing resized image retries the original before signalling failure');
    assert.equal(renderer.root.findByType('img').props.src, '/MediaCover/42/poster.jpg');
    TestRenderer.act(() => renderer.root.findByType('img').props.onError());
    assert.equal(errors, 1);
    assert.equal(renderer.root.findByType('img').props.src, '/Content/Images/poster-dark.png');
    TestRenderer.act(() => renderer.update(React.createElement(Image, {
      images: [{ coverType: 'poster', url: '/MediaCover/43/poster.jpg' }], coverType: 'poster',
      placeholder: '/Content/Images/poster-dark.png', title: 'Replacement cover', ...callbacks
    })));
    assert.equal(renderer.root.findByType('img').props.src, '/MediaCover/43/poster-250.jpg');
    assert.equal(renderer.root.findByType('img').props.alt, 'Replacement cover ImagePoster');
    TestRenderer.act(() => renderer.unmount());
  });
});

test('missing thumbnails fall back to authenticated original cached covers and preserve their cache query', async() => {
  const dependency = await import('react-lazyload');
  withBrowser(() => {
    let errors = 0;
    let loaded = 0;
    global.window.Sonarr.urlBase = '/tanukarr';
    const renderer = mount(loadSeriesImage(dependency), { lazy: false, images: [{ coverType: 'poster', url: '/tanukarr/MediaCover/42/poster.jpg?lastWrite=20261014' }], onError: () => {
      errors++;
    }, onLoad: () => {
      loaded++;
    } });
    assert.equal(renderer.root.findByType('img').props.src, '/tanukarr/MediaCover/42/poster-250.jpg?lastWrite=20261014');
    TestRenderer.act(() => renderer.root.findByType('img').props.onError());
    assert.equal(renderer.root.findByType('img').props.src, '/tanukarr/MediaCover/42/poster.jpg?lastWrite=20261014');
    TestRenderer.act(() => renderer.root.findByType('img').props.onLoad());
    assert.equal(errors, 0);
    assert.equal(loaded, 1);
    TestRenderer.act(() => renderer.unmount());
  });
});

for (const pixelRatio of [1, 2, 3]) {
  test(`device pixel ratio ${pixelRatio} requests only thumbnail heights actually generated by MediaCoverService`, () => {
    withBrowser(() => {
      const Image = loadSeriesImage(projectRequire('react-lazyload'));
      const regular = mount(Image, { lazy: false });
      const large = mount(Image, { lazy: false, size: 500 });
      const banner = mount(Image, { lazy: false, coverType: 'banner', size: 70, images: [{ coverType: 'banner', url: '/MediaCover/42/banner.jpg?h=1' }] });
      const fanart = mount(Image, { lazy: false, coverType: 'fanart', size: 360, images: [{ coverType: 'fanart', url: '/MediaCover/42/fanart.jpg?h=1' }] });
      assert.equal(regular.root.findByType('img').props.src, `/MediaCover/42/poster-${pixelRatio === 1 ? 250 : 500}.jpg`);
      assert.equal(large.root.findByType('img').props.src, '/MediaCover/42/poster-500.jpg');
      assert.equal(banner.root.findByType('img').props.src, '/MediaCover/42/banner-70.jpg?h=1');
      assert.equal(fanart.root.findByType('img').props.src, '/MediaCover/42/fanart-360.jpg?h=1');
      TestRenderer.act(() => {
        regular.unmount();
        large.unmount();
        banner.unmount();
        fanart.unmount();
      });
    }, pixelRatio);
  });
}

test('remote provider artwork URLs are never rewritten to invent resized CDN resources', () => {
  withBrowser(() => {
    const renderer = mount(loadSeriesImage(projectRequire('react-lazyload')), { lazy: false, images: [{ coverType: 'poster', url: 'https://catalogue.example/MediaCover/42/poster.jpg?token=fixture' }] });
    assert.equal(renderer.root.findByType('img').props.src, 'https://catalogue.example/MediaCover/42/poster.jpg?token=fixture');
    TestRenderer.act(() => renderer.unmount());
  }, 3);
});

test('non-resized provider artwork reports failure once without looping on the same URL', async() => {
  const dependency = await import('react-lazyload');
  withBrowser(() => {
    let errors = 0;
    const renderer = mount(loadSeriesImage(dependency), { lazy: false, images: [{ coverType: 'poster', url: '/MediaCover/42/original.png' }], onError: () => {
      errors++;
    } });
    TestRenderer.act(() => renderer.root.findByType('img').props.onError());
    assert.equal(errors, 1);
    assert.equal(renderer.root.findByType('img').props.src, '/Content/Images/poster-dark.png');
    TestRenderer.act(() => renderer.unmount());
  });
});

test('missing covers and explicit non-lazy covers preserve their existing image paths', async() => {
  const dependency = await import('react-lazyload');
  withBrowser(() => {
    const Image = loadSeriesImage(dependency);
    const missing = mount(Image, { images: [] });
    const immediate = mount(Image, { lazy: false });
    assert.equal(missing.root.findByType('img').props.src, '/Content/Images/poster-dark.png');
    assert.equal(immediate.root.findByType('img').props.src, '/MediaCover/42/poster-250.jpg');
    assert.equal(missing.root.findAllByType(dependency.default.default).length, 0);
    assert.equal(immediate.root.findAllByType(dependency.default.default).length, 0);
    TestRenderer.act(() => {
      missing.unmount();
      immediate.unmount();
    });
  });
});
