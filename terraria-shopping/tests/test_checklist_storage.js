'use strict';

const assert = require('node:assert/strict');
const {readFileSync} = require('node:fs');
const {join} = require('node:path');
const {test} = require('node:test');
const {runInNewContext} = require('node:vm');

const app = readFileSync(join(__dirname, '..', 'app.js'), 'utf8');
const v1 = 'terraria-shopping-progress-v1';
const v2 = 'terraria-shopping-progress-v2';
const favorites = 'terraria-shopping-favorites-v1';
const legacy = JSON.stringify({compass: true});

function start(storage) {
  // Run the real entry point and event listeners with a minimal DOM. No
  // recipe data is needed to render the global checked count.
  const nodes = new Map();
  const document = {
    querySelector(selector) {
      if (!nodes.has(selector)) {
        const listeners = new Map();
        nodes.set(selector, {
          value: '', checked: false, style: {},
          addEventListener: (event, listener) => listeners.set(event, listener),
          emit: (event, arg) => listeners.get(event)(arg),
        });
      }
      return nodes.get(selector);
    },
    querySelectorAll() { return []; },
  };
  const localStorage = {
    getItem: key => storage.get(key) ?? null,
    setItem: (key, value) => storage.set(key, value),
  };
  runInNewContext(app, {window: {}, document, localStorage, confirm: () => true});
  return {
    count: () => Number(nodes.get('#stats').innerHTML.match(/<strong>(\d+)<\/strong> checked/)[1]),
    reset: () => nodes.get('#resetChecks').emit('click'),
    uncheck: key => nodes.get('#content').emit('change', {
      target: {matches: selector => selector === '.check', checked: false, dataset: {key}},
    }),
  };
}

test('missing or invalid v2 retains legacy migration', () => {
  for (const saved of [undefined, '{broken', 'null', 'true', '[]', '"invalid"']) {
    const storage = new Map([[v1, legacy]]);
    if (saved !== undefined) storage.set(v2, saved);
    assert.equal(start(storage).count(), 1, `stored v2: ${saved}`);
  }
});

test('explicit empty v2 overrides legacy progress', () => {
  const storage = new Map([[v1, legacy], [v2, '{}']]);
  assert.equal(start(storage).count(), 0);
});

test('nonempty v2 does not merge old v1 checks', () => {
  const storage = new Map([[v1, legacy], [v2, '{"gen:fixture":true}']]);
  assert.equal(start(storage).count(), 1);
});

test('reset saves an empty map that survives reload independently of favorites', () => {
  const storedFavorites = '["Shellphone"]';
  const storage = new Map([[v1, legacy], [favorites, storedFavorites]]);
  const page = start(storage);
  assert.equal(page.count(), 1);
  page.reset();
  assert.equal(page.count(), 0);
  assert.equal(storage.get(v2), '{}');
  assert.equal(start(storage).count(), 0);
  assert.equal(storage.get(v1), legacy);
  assert.equal(storage.get(favorites), storedFavorites);
});

test('unchecking the final migrated item survives reload', () => {
  const storage = new Map([[v1, legacy]]);
  const page = start(storage);
  assert.equal(page.count(), 1);
  page.uncheck('legacy:compass');
  assert.equal(page.count(), 0);
  assert.equal(storage.get(v2), '{}');
  assert.equal(start(storage).count(), 0);
});
