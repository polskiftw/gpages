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

test('keyboard Show More focuses the first appended card without moving pointer focus', () => {
  const fixture = Array.from({length: 162}, (_, i) => 'Card ' + String(i).padStart(3, '0'));
  const run = detail => {
    let html = '', button, summaries = [];
    const nodes = new Map();
    const node = () => {
      const listeners = new Map();
      return {value: '', checked: false, style: {},
        addEventListener: (type, fn) => listeners.set(type, fn),
        emit: (type, event) => listeners.get(type)(event)};
    };
    const content = node();
    Object.defineProperty(content, 'innerHTML', {
      get: () => html,
      set(value) {
        html = value;
        button = value.includes('id="showMore"') ? node() : null;
        summaries = [...value.matchAll(/class="card project" data-name="([^"]+)"/g)].map(([, name]) => ({
          name, focus() { document.activeElement = this; },
        }));
      },
    });
    nodes.set('#content', content);
    const document = {
      querySelector(selector) {
        if (selector === '#showMore') return button;
        if (!nodes.has(selector)) nodes.set(selector, node());
        return nodes.get(selector);
      },
      querySelectorAll: selector => selector === 'details.project > summary' ? summaries : [],
    };
    const data = {nodes: fixture.map(name => [name, 1, 1]),
      recipes: fixture.map(r => ({r, s: 'Work Bench', i: [['Ore', 1]]}))};
    runInNewContext(app, {window: {TERRARIA_RECIPE_DATA: data}, document, localStorage: {getItem: () => null}});
    for (const [size, next] of [[160, 'Card 080'], [162, 'Card 160']]) {
      const pressed = button;
      document.activeElement = pressed;
      pressed.emit('click', {detail, currentTarget: pressed});
      assert.equal(summaries.length, size);
      assert.equal(document.activeElement, detail ? pressed : summaries[size === 160 ? 80 : 160], next);
    }
    assert.equal(button, null);
  };
  run(0);
  run(1);
});
