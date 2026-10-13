const assert = require('node:assert/strict');
const fs = require('node:fs');
const Module = require('node:module');
const path = require('node:path');
const test = require('node:test');
const React = require('react');
const TestRenderer = require('react-test-renderer');
const ts = require('typescript');

function fixture(popup = { closed: false }) {
  const requests = [];
  const opened = [];
  global.window = {
    location: { origin: 'https://owned-app.example' },
    Sonarr: { urlBase: '/tanukarr' },
    open: (url) => {
      opened.push(url);
      return popup;
    }
  };
  const filename = path.resolve(__dirname, '../src/OAuth/useOAuth.ts');
  const output = ts.transpileModule(fs.readFileSync(filename, 'utf8'), {
    compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2020 }
  }).outputText;
  const compiled = new Module(filename, module);
  compiled.paths = module.paths;

  compiled.require = (name) => {
    if (name === 'react') {
      return React;
    }

    if (name === 'Utilities/requestAction') {
      return { default: async(payload) => {
        requests.push(payload);
        return payload.action === 'startOAuth' ?
          { oauthUrl: 'https://myanimelist.net/v1/oauth2/authorize?state=generated-state' } :
          { accessToken: 'generated-access-token', refreshToken: 'generated-refresh-token' };
      } };
    }

    if (name === 'Utilities/createAjaxRequest') {
      return { default: () => {
        throw new Error('Unexpected intermediate request');
      } };
    }

    throw new Error(`Unexpected import ${name}`);
  };

  // eslint-disable-next-line no-underscore-dangle
  compiled._compile(output, filename);
  let current = null;

  function HookFixture() {
    current = compiled.exports.default();
    return null;
  }

  let renderer = null;
  TestRenderer.act(() => {
    renderer = TestRenderer.create(React.createElement(HookFixture));
  });

  const start = async() => {
    let promise = null;
    await TestRenderer.act(async() => {
      promise = current.startOAuth({ name: 'authenticateWithMyAnimeList', provider: 'importList', providerData: { implementation: 'MyAnimeListImport' } });
      // Attach immediately so the deliberate rejection cases never become unhandled.
      promise.catch(() => {});
      await Promise.resolve();
    });
    return { promise };
  };

  return { requests, opened, start, state: () => current, close: () => {
    TestRenderer.act(() => renderer.unmount());
    delete global.window;
  } };
}

test('OAuth decodes the entire callback code and state before token exchange', async() => {
  const item = fixture();

  try {
    const { promise } = await item.start();
    let closes = 0;
    await TestRenderer.act(async() => {
      window.onCompleteOauth('?code=abc%2Bdef%3Dghi%25&state=state%2B%3D%26value', () => {
        closes += 1;
      });
      await promise;
    });
    assert.equal(closes, 1);
    assert.equal(window.onCompleteOauth, undefined);
    assert.equal(item.requests[0].queryParams.callbackUrl, 'https://owned-app.example/tanukarr/oauth.html');
    assert.equal(item.requests[1].queryParams.code, 'abc+def=ghi%');
    assert.equal(item.requests[1].queryParams.state, 'state+=&value');
    assert.equal(item.state().authorizing, false);
    assert.equal(item.state().result.accessToken, 'generated-access-token');
  } finally {
    item.close();
  }
});

for (const query of ['?code=one&code=two&state=generated', '?code=one&state=one&state=two', '?code=one&%73tate=one&state=two']) {
  test(`ambiguous OAuth callback rejects duplicate parameters: ${query}`, async() => {
    const item = fixture();

    try {
      const { promise } = await item.start();
      let closes = 0;
      await TestRenderer.act(async() => {
        window.onCompleteOauth(query, () => {
          closes += 1;
        });
        await assert.rejects(promise, /duplicate parameters/);
      });
      assert.equal(closes, 1);
      assert.equal(item.requests.length, 1);
      assert.equal(window.onCompleteOauth, undefined);
      assert.equal(item.state().authorizing, false);
      assert.equal(item.state().result, null);
    } finally {
      item.close();
    }
  });
}

test('legacy token callbacks retain decoded values and popup failure exposes a field validation error', async() => {
  const item = fixture();

  try {
    const { promise } = await item.start();
    await TestRenderer.act(async() => {
      window.onCompleteOauth('?access_token=one%2Btwo%3D&expires_in=3600', () => {});
      await promise;
    });
    assert.equal(item.requests[1].queryParams.access_token, 'one+two=');
    assert.equal(item.requests[1].queryParams.expires_in, '3600');
  } finally {
    item.close();
  }

  const blocked = fixture(null);

  try {
    const { promise } = await blocked.start();
    await TestRenderer.act(async() => {
      await assert.rejects(promise, (error) => error.status === 400 && error.responseJSON[0].propertyName === 'authenticateWithMyAnimeList');
    });
    assert.equal(blocked.requests.length, 1);
    assert.equal(blocked.state().authorizing, false);
  } finally {
    blocked.close();
  }
});
