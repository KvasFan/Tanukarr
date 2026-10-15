'use strict';
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

async function main(input) {
  assert.equal(input.ownedFixture, true);
  const target = new URL(input.baseUrl);
  assert.equal(target.origin, `http://127.0.0.1:${target.port}`);
  assert.match(input.imageId, /^sha256:[a-f0-9]{64}$/);
  const { chromium } = require(input.playwrightModule);
  const dependency = require(require.resolve(path.join(input.playwrightModule, 'package.json')));
  if (input.playwrightVersion) assert.equal(dependency.version, input.playwrightVersion);
  const browser = await chromium.launch({ headless: true });
  const checks = [], errors = [], outside = [];
  let passed = false;
  try {
    const page = await browser.newPage({ viewport: { width: 1280, height: 900 } });
    await page.route('**/*', async (route) => {
      const url = new URL(route.request().url());
      if (url.origin !== target.origin) {
        outside.push(url.origin + url.pathname);
        await route.abort();
        return;
      }
      await route.continue();
    });
    page.on('pageerror', (error) => errors.push(error.message));
    await page.goto(input.baseUrl + '/');
    await page.getByRole('button', { name: 'Create account', exact: true }).waitFor();
    assert.equal(await page.getByRole('button', { name: 'Login', exact: true }).isVisible(), false);
    await page.screenshot({ path: path.join(input.artifactDirectory, 'create-account.png') });
    await page.setViewportSize({ width: 375, height: 812 });
    assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth), true,
      'The mobile setup page must not scroll horizontally');
    for (const selector of ['#setup-username', '#setup-password', '#setup-password-confirmation', '#setup-panel button']) {
      const bounds = await page.locator(selector).boundingBox();
      assert.ok(bounds && bounds.x >= 0 && bounds.y >= 0 && bounds.x + bounds.width <= 375 && bounds.y + bounds.height <= 812,
        'Every account field and the submit button must fit the mobile viewport');
    }
    await page.screenshot({ path: path.join(input.artifactDirectory, 'create-account-mobile.png') });
    checks.push('mobile 375x812 form keeps every field and submit button visible without horizontal clipping');
    await page.setViewportSize({ width: 1280, height: 900 });
    await page.getByLabel('Username', { exact: true }).fill(input.username);
    await page.getByLabel('Password', { exact: true }).fill(input.password);
    await page.getByLabel('Confirm password', { exact: true }).fill('different-password');
    await page.getByRole('button', { name: 'Create account', exact: true }).click();
    await page.getByRole('alert').waitFor();
    checks.push('visible first-run form with labelled inputs and password mismatch feedback');
    await page.getByLabel('Username', { exact: true }).fill(input.username);
    await page.getByLabel('Password', { exact: true }).fill(input.password);
    await page.getByLabel('Confirm password', { exact: true }).fill(input.password);
    await page.getByRole('button', { name: 'Create account', exact: true }).click();
    await page.waitForURL(input.baseUrl + '/series');
    await page.locator('a[href="/series"]').first().waitFor();
    const initialization = await page.request.get(input.baseUrl + '/initialize.json');
    assert.equal(initialization.status(), 200);
    checks.push('real form submission creates the account and a working authenticated browser session');
    await page.goto(input.baseUrl + '/logout');
    await page.getByRole('button', { name: 'Login', exact: true }).waitFor();
    assert.equal(await page.getByRole('button', { name: 'Create account', exact: true }).isVisible(), false);
    await page.locator('#sign-in-panel input[name="username"]').fill(input.username);
    await page.locator('#sign-in-panel input[name="password"]').fill(input.password);
    await page.getByRole('button', { name: 'Login', exact: true }).click();
    await page.waitForURL(input.baseUrl + '/series');
    await page.locator('a[href="/series"]').first().waitFor();
    checks.push('logout closes setup permanently and the chosen credentials sign in again');
    assert.deepEqual(errors, []);
    assert.deepEqual(outside, [], 'Account setup and login must use only the owned local fixture');
    passed = true;
  } finally {
    await browser.close();
    fs.writeFileSync(path.join(input.artifactDirectory, 'first-run-browser.json'), JSON.stringify({
      imageId: input.imageId, passed, checks, javascriptErrorCount: errors.length, playwrightVersion: dependency.version,
      screenshots: ['create-account.png', 'create-account-mobile.png'],
      externalRequestCount: outside.length,
    }, null, 2) + '\n');
  }
  process.stdout.write('PASS first-run browser: ' + checks.length + ' checks\n');
}

let input = '';
process.stdin.on('data', (chunk) => { input += chunk; });
process.stdin.on('end', () => main(JSON.parse(input)).catch((error) => {
  process.stderr.write(String(error.message).replaceAll(JSON.parse(input).password, '[REDACTED]') + '\n');
  process.exitCode = 1;
}));
