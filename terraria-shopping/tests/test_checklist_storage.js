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
const fishMode = 'terraria-shopping-fish-mode-v1';
const legacy = JSON.stringify({compass: true});

function start(storage) {
  // Run the real entry point and event listeners with a minimal DOM.
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
  const window = {
    TERRARIA_RECIPE_DATA: {
      nodes: [['Test Project', 2, 1]],
      recipes: [{r: 'Test Project', s: 'Work Bench', i: [['Radar', 1], ['Demon Conch', 1]]}],
    },
    TERRARIA_ENRICHMENT: {items: {
      radar: {name: 'Radar', type: 'Loot', fish: true},
      conch: {name: 'Demon Conch', type: 'Loot', fish: true},
    }},
  };
  runInNewContext(app, {window, document, localStorage, confirm: () => true});
  return {
    count: () => Number(nodes.get('#stats').innerHTML.match(/<strong>(\d+)<\/strong> checked/)[1]),
    reset: () => nodes.get('#resetChecks').emit('click'),
    setFishMode(enabled) {
      const toggle = nodes.get('#fishMode');
      toggle.checked = enabled;
      toggle.emit('change');
    },
    projectHTML: () => nodes.get('#content').innerHTML,
    check: (key, checked = true) => nodes.get('#content').emit('change', {
      target: {matches: selector => selector === '.check', checked, dataset: {key}},
    }),
    clickStar(name, pressed = false) {
      const button = {
        dataset: {name}, classList: {toggle() {}},
        getAttribute: () => String(pressed), setAttribute() {},
      };
      nodes.get('#content').emit('click', {
        target: {closest: () => button}, preventDefault() {}, stopPropagation() {},
      });
    },
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
  page.check('legacy:compass', false);
  assert.equal(page.count(), 0);
  assert.equal(storage.get(v2), '{}');
  assert.equal(start(storage).count(), 0);
});

test('two already-open pages can acquire different items without losing progress', () => {
  const storage = new Map();
  const a = start(storage), b = start(storage);
  a.check('gen:Compass');
  b.check('gen:Demon Conch');
  assert.equal(start(storage).count(), 2);
});

test('an edit in an older page cannot resurrect items cleared by Reset', () => {
  const storage = new Map([[v1, legacy], [v2, '{"gen:Compass":true}']]);
  const a = start(storage), b = start(storage);
  a.reset();
  b.check('gen:Demon Conch');
  assert.deepEqual(JSON.parse(storage.get(v2)), {'gen:Demon Conch': true});
  assert.equal(start(storage).count(), 1);
});

test('two already-open pages can uncheck different items without resurrecting either', () => {
  const storage = new Map([[v2, '{"gen:Compass":true,"gen:Demon Conch":true}']]);
  const a = start(storage), b = start(storage);
  a.check('gen:Compass', false);
  b.check('gen:Demon Conch', false);
  assert.equal(storage.get(v2), '{}');
  assert.equal(start(storage).count(), 0);
});

test('favorite edits across pages preserve other favorites and honor the rendered star', () => {
  const storage = new Map();
  const a = start(storage), b = start(storage);
  a.clickStar('Shellphone');
  b.clickStar('Ankh Shield');
  assert.deepEqual(JSON.parse(storage.get(favorites)), ['Shellphone', 'Ankh Shield']);
  b.clickStar('Shellphone'); // Its unpressed star requests an add, not a toggle of storage.
  assert.deepEqual(JSON.parse(storage.get(favorites)), ['Shellphone', 'Ankh Shield']);
  a.clickStar('Shellphone', true);
  assert.deepEqual(JSON.parse(storage.get(favorites)), ['Ankh Shield']);
});

test('re-enabling Fish Mode snapshots saved checks from another open page without writing progress', () => {
  const storage = new Map();
  const a = start(storage), b = start(storage);
  a.check('legacy:radar');
  a.check('legacy:conch');
  const complete = storage.get(v2);
  b.setFishMode(true);
  assert.doesNotMatch(b.projectHTML(), /data-name="Test Project"/);
  assert.equal(storage.get(v2), complete);
  assert.equal(storage.get(fishMode), '1');

  a.check('legacy:conch', false);
  const partial = storage.get(v2);
  b.setFishMode(false);
  b.setFishMode(true);
  assert.match(b.projectHTML(), /1\/2 FISHABLE/);
  assert.equal(storage.get(v2), partial);
  b.check('legacy:conch');
  assert.match(b.projectHTML(), /2\/2 FISHABLE/);
});

test('enabling Fish Mode honors an explicit reset and falls back on invalid saved checks', () => {
  const storage = new Map([[v2, '{"legacy:radar":true,"legacy:conch":true}']]);
  const a = start(storage), b = start(storage);
  a.reset();
  b.setFishMode(true);
  assert.match(b.projectHTML(), /0\/2 FISHABLE/);
  assert.equal(storage.get(v2), '{}');

  b.check('legacy:radar');
  storage.set(v2, '{invalid');
  b.setFishMode(false);
  b.setFishMode(true);
  assert.match(b.projectHTML(), /1\/2 FISHABLE/);
  assert.equal(storage.get(v2), '{invalid');
});
