'use strict';

const assert = require('node:assert/strict');
const {readFileSync} = require('node:fs');
const {join} = require('node:path');
const {test} = require('node:test');
const {runInNewContext} = require('node:vm');
const app = readFileSync(join(__dirname, '..', 'app.js'), 'utf8');
const lantern = "Jack 'O Lantern";
const queries = ["Jack O' Lantern", 'Jack O’ Lantern', 'JACK O LANTERN', lantern];

function start() {
  const viewButtons = ['projects', 'components', 'fishable', 'remaining'].map(view => makeNode({view}));
  const nodes = new Map();
  function makeNode(dataset = {}) {
    const listeners = new Map();
    return {
      value: '', checked: false, style: {}, dataset, classList: {toggle() {}},
      addEventListener: (event, listener) => listeners.set(event, listener),
      emit: event => listeners.get(event)(),
    };
  }
  const document = {
    querySelector(selector) {
      if (!nodes.has(selector)) nodes.set(selector, makeNode());
      return nodes.get(selector);
    },
    querySelectorAll(selector) { return selector === '.segments button' ? viewButtons : []; },
  };
  const sources = {
    fish: {name: 'Radar', type: 'Loot', fish: true, fishSource: "Fisher's Crate", source: 'Fishing'},
    land: {name: 'Spider Fang', type: 'Loot', fish: false, source: "Spider's Nest"},
  };
  const window = {
    TERRARIA_RECIPE_DATA: {
      nodes: [[lantern, 1, 1], ['Hanging ' + lantern, 1, 0]],
      recipes: [{r: lantern, s: 'Work Bench', i: [['Pumpkin', 1]]},
        {r: 'Hanging ' + lantern, s: 'Work Bench', i: [[lantern, 1]]}],
    },
    TERRARIA_ENRICHMENT: {items: sources},
  };
  const localStorage = {getItem: () => null};
  runInNewContext(app, {window, document, localStorage, scrollTo() {}});
  return {
    render(query, view) {
      if (view) viewButtons.find(button => button.dataset.view === view).emit('click');
      const search = document.querySelector('#search');
      search.value = query;
      search.emit('input');
      return document.querySelector('#content').innerHTML;
    },
  };
}

test('Projects and Components accept straight, curly, relocated and absent apostrophes', () => {
  const page = start();
  for (const view of ['projects', 'components']) {
    for (const query of queries) {
      const result = page.render(query, view);
      assert.match(result, /class="projecttitle">Jack &#039;O Lantern</, query);
      assert.equal(result.includes('class="projecttitle">Hanging Jack &#039;O Lantern<'), view === 'components');
    }
    assert.match(page.render(' Pumpkin ', view), /class="projecttitle">Jack &#039;O Lantern</);
    assert.match(page.render('invalid search', view), /No craftables match/);
  }
});

test('Fishable and Remaining normalize source text but preserve its display', () => {
  const page = start();
  for (const query of ["Fisher's Crate", 'Fisher’s Crate', 'Fishers Crate']) {
    assert.match(page.render(query, 'fishable'), /class="itemname">Radar</);
  }
  for (const query of ["Spider's Nest", 'Spider’s Nest', 'Spiders Nest', 'Spider Fang']) {
    const result = page.render(query, 'remaining');
    assert.match(result, /class="itemname">Spider Fang</);
    assert.match(result, /Spider&#039;s Nest/);
  }
  assert.match(page.render('invalid search', 'remaining'), /No landlocked ingredients match/);
});
