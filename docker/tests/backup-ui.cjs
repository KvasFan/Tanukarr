#!/usr/bin/env node
'use strict';

// All credentials belong to a caller-owned empty instance and arrive on stdin.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

async function main(input) {
  const target = new URL(input.baseUrl);
  assert.equal(target.hostname, '127.0.0.1');
  assert.equal(target.protocol, 'http:');
  assert.equal(input.ownedFixture, true);
  assert.match(input.imageId, /^sha256:[a-f0-9]{64}$/);
  const { chromium } = require(input.playwrightModule);
  const browser = await chromium.launch({ headless: true });
  const context = await browser.newContext();
  const page = await context.newPage();
  page.setDefaultTimeout(30000);
  const checks = [];
  const errors = [];
  const outsideRequests = [];
  const requests = [];
  const taskResponses = [];
  let failure = null;
  let before = null;
  let after = null;
  const redact = (value) => {
    let result = String(value);
    for (const secret of [input.apiKey, input.username, input.password]) {
      if (secret) result = result.split(secret).join('[REDACTED]');
    }
    return result;
  };
  page.on('pageerror', (error) => errors.push(redact(error.message)));
  page.on('request', (request) => {
    const url = new URL(request.url());
    if (url.origin !== target.origin) outsideRequests.push(url.origin + url.pathname);
    if (request.method() === 'POST' && url.pathname.endsWith('/command')) {
      const body = request.postDataJSON();
      requests.push({ name: body?.name, method: request.method() });
    }
  });
  page.on('response', async (response) => {
    const url = new URL(response.url());
    if (url.pathname.endsWith('/system/task') && response.request().method() === 'GET') {
      try {
        const tasks = await response.json();
        const task = tasks.find((value) => value.taskName === 'Backup');
        if (task) taskResponses.push(task);
      } catch (_) {
        // Failed response details are verified through the result below.
      }
    }
  });
  try {
    await page.goto(input.baseUrl + '/system/tasks');
    await page.waitForURL('**/login**');
    await page.locator('#sign-in-panel input[name="username"]').fill(input.username);
    await page.locator('#sign-in-panel input[name="password"]').fill(input.password);
    await Promise.all([
      page.waitForURL((url) => !url.pathname.startsWith('/login')),
      page.locator('#sign-in-panel button[type="submit"]').click(),
    ]);
    await page.goto(input.baseUrl + '/system/tasks');
    await page.waitForFunction(() => document.querySelector('table'));
    const row = page.locator('tr').filter({ has: page.locator('td').getByText('Backup', { exact: true }) });
    assert.equal(await row.count(), 1);
    const execution = row.locator('td').nth(2);
    const previousTitle = await execution.getAttribute('title');
    const response = await context.request.get(input.baseUrl + '/api/v5/system/task', { headers: { 'X-Api-Key': input.apiKey } });
    assert.equal(response.status(), 200);
    before = (await response.json()).find((task) => task.taskName === 'Backup');
    assert.ok(before);
    checks.push('actual packaged Tasks page loads one Backup row after Forms authentication');

    const navigation = page.url();
    await row.getByRole('button', { name: 'Run', exact: true }).click();
    const deadline = Date.now() + 30000;
    while (Date.now() < deadline) {
      after = taskResponses.findLast((task) => task.lastExecution !== before.lastExecution);
      if (after && await execution.getAttribute('title') !== previousTitle) break;
      await page.waitForTimeout(100);
    }
    assert.ok(after && after.lastExecution !== before.lastExecution, 'SignalR did not trigger a GET with the successful backup timestamp');
    assert.notEqual(await execution.getAttribute('title'), previousTitle, 'Last Execution cell did not render its new timestamp');
    assert.equal(page.url(), navigation);
    assert.deepEqual(requests, [{ name: 'Backup', method: 'POST' }]);
    checks.push('pressing the actual Backup Run button updates Last Execution through SignalR without reloading');

    const apiResponse = await context.request.get(input.baseUrl + '/api/v3/system/task', { headers: { 'X-Api-Key': input.apiKey } });
    const legacy = (await apiResponse.json()).find((task) => task.taskName === 'Backup');
    assert.equal(legacy.lastExecution, after.lastExecution);
    assert.equal(legacy.lastStartTime, after.lastStartTime);
    checks.push('browser task timestamp matches the persisted V3 compatibility API');
    assert.deepEqual(errors, []);
    assert.deepEqual(outsideRequests, []);
    checks.push('backup button causes no browser error, outbound browser request or acquisition command');
  } catch (error) {
    failure = redact(error.message);
    console.log('FAIL ' + failure);
    await page.screenshot({
      path: path.join(input.artifactDirectory, 'backup-ui-failure.png'), fullPage: true,
      style: 'input[type="password"], #sign-in-panel input { visibility: hidden !important; }',
    }).catch(() => {});
  } finally {
    await browser.close();
    fs.writeFileSync(path.join(input.artifactDirectory, 'backup-ui.json'), JSON.stringify({
      createdUtc: new Date().toISOString(), passed: failure === null, error: failure,
      imageId: input.imageId, checks, pageErrors: errors, outsideRequests, commands: requests,
      before, after, taskResponses, ownedFixture: true, mainConfigurationAccessed: false,
    }, null, 2) + '\n', 'utf8');
  }
  for (const check of checks) console.log('PASS ' + check);
  if (failure) process.exitCode = 1;
}

let input = '';
process.stdin.setEncoding('utf8');
process.stdin.on('data', (chunk) => { input += chunk; });
process.stdin.on('end', () => main(JSON.parse(input)).catch((error) => {
  console.error(error.name);
  process.exitCode = 1;
}));
