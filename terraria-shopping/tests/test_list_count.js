'use strict';

const assert = require('node:assert/strict');
const {readFileSync} = require('node:fs');
const {join} = require('node:path');
const {test} = require('node:test');
const {runInNewContext} = require('node:vm');
const app = readFileSync(join(__dirname, '..', 'app.js'), 'utf8');

function openList({acquired = {}, missing = false, fish = false} = {}) {
  const nodes = new Map();
  const document = {
    querySelector(selector) {
      if (!nodes.has(selector)) {
        const listeners = new Map();
        nodes.set(selector, {
          value: '', checked: false, style: {},
          addEventListener: (event, listener) => listeners.set(event, listener),
          emit: (event, payload) => listeners.get(event)(payload),
        });
      }
      return nodes.get(selector);
    },
    querySelectorAll() { return []; },
  };
  const window = {
    TERRARIA_RECIPE_DATA: {
      nodes: [['Test Project', 3, 1]],
      recipes: [{r: 'Test Project', s: 'Work Bench', i: [['Radar', 1], ['Demon Conch', 1], ['Ore', 4]]}],
    },
    TERRARIA_ENRICHMENT: {items: {
      radar: {name: 'Radar', type: 'Loot', fish: true, fishSource: 'Crate'},
      conch: {name: 'Demon Conch', type: 'Loot', fish: true, fishSource: 'Lava fishing'},
    }},
  };
  const progressKey = 'terraria-shopping-progress-v2';
  const saved = {[progressKey]: JSON.stringify(acquired), 'terraria-shopping-fish-mode-v1': fish ? '1' : '0'};
  const localStorage = {
    getItem: key => saved[key] ?? null,
    setItem: (key, value) => { saved[key] = value; },
  };
  document.querySelector('#missingOnly').checked = missing;
  runInNewContext(app, {window, document, localStorage});
  const body = {dataset: {loaded: '0'}};
  const details = {
    dataset: {name: 'Test Project'}, open: true,
    matches: selector => selector === 'details.project',
    querySelector: () => body,
  };
  // Dispatch through the real lazy-loading handler used by an opened card.
  document.querySelector('#content').emit('toggle', {target: details});
  assert.equal(saved[progressKey], JSON.stringify(acquired));
  return body.innerHTML;
}

function assertCount(html, n) {
  assert.equal((html.match(/class="row"/g) || []).length, n);
  assert.match(html, new RegExp(' • ' + n + ' unique item' + (n === 1 ? '' : 's') + '</div>'));
  assert.match(html, /class="recipeingredients">Radar • Demon Conch • Ore ×4</);
}

test('the project heading counts only listed ingredients, including the empty case', () => {
  const acquired = {'legacy:radar': true};
  assertCount(openList({acquired}), 3);
  assertCount(openList({acquired, missing: true}), 2);
  const complete = {'legacy:radar': true, 'legacy:conch': true, 'gen:Ore': true};
  const html = openList({acquired: complete, missing: true});
  assertCount(html, 0);
  assert.match(html, /Everything in this shopping list is checked/);
});

test('Fish mode count respects both the fishing and missing filters', () => {
  const acquired = {'legacy:radar': true};
  assertCount(openList({acquired, fish: true}), 2);
  const html = openList({acquired, fish: true, missing: true});
  assertCount(html, 1);
  assert.match(html, /Fishing list • 1 unique item<\/div>/);
});
