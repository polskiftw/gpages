import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import { readFileSync } from "node:fs";
import { test } from "node:test";
import vm from "node:vm";

const html = readFileSync(new URL("../index.html", import.meta.url), "utf8");
const script = html.match(/<script>\s*([\s\S]*?)\s*<\/script>/)?.[1];
assert.ok(script, "Alacritty generator's inline script is missing");
new vm.Script(script); // Ensure the entire browser script is syntactically valid.

const start = script.indexOf("function formatFixedToml(assignments){");
const end = script.indexOf("function overridden(path){", start);
assert.ok(start >= 0 && end > start, "TOML generator function markers are missing");

const context = vm.createContext({
  tomlString: (value) => JSON.stringify(String(value)),
  document: { querySelectorAll: () => [] },
});
vm.runInContext(script.slice(start, end), context);

function parseToml(content) {
  const result = spawnSync("python3", [
    "-c",
    "import json, sys, tomllib; print(json.dumps(tomllib.loads(sys.stdin.read())))",
  ], { input: content, encoding: "utf8" });
  assert.equal(result.status, 0, result.stderr);
  return JSON.parse(result.stdout);
}

function row(values) {
  return { querySelector: (selector) => ({ value: values[selector.slice(1)] ?? "" }) };
}

test("Terminus example uses [font] and [font.normal], not dotted keys", () => {
  const output = context.formatFixedToml([
    "window.dimensions.columns = 95",
    "window.dimensions.lines = 24",
    'cursor.style.blinking = "On"',
    "cursor.blink_timeout = 0",
    'colors.primary.foreground = "#33ff66"',
    'colors.primary.background = "#000000"',
    'font.normal.family = "Terminus"',
    "font.size = 12.0",
  ]).join("\n\n");
  assert.match(output, /\[font\]\nsize = 12\.0/);
  assert.match(output, /\[font\.normal\]\nfamily = "Terminus"/);
  assert.doesNotMatch(output, /^font\.normal\.family\s*=/m);
  const parsed = parseToml(output);
  assert.equal(parsed.font.normal.family, "Terminus");
  assert.equal(parsed.font.size, 12);
  assert.equal(parsed.window.dimensions.columns, 95);
  assert.equal(parsed.colors.primary.background, "#000000");
});

test("all fixed settings generate valid nested TOML tables", () => {
  const paths = [...html.matchAll(/\bpath:"([^"]+)"/g)].map((match) => match[1])
    .filter((path) => !["window.position", "cursor.vi_mode_style"].includes(path));
  assert.ok(paths.length > 80, "Expected to cover the full Alacritty settings schema");
  assert.equal(new Set(paths).size, paths.length, "Duplicate settings paths found");
  const output = context.formatFixedToml(paths.map((path) => path + ' = "test"')).join("\n\n");
  assert.doesNotMatch(output, /^[a-zA-Z_][\w.]*\.[\w.]*\s*=/m);
  const parsed = parseToml(output);
  for (const path of paths) {
    const value = path.split(".").reduce((node, key) => node?.[key], parsed);
    assert.equal(value, "test", "Failed to preserve " + path);
  }
});

test("scalar or table alternatives work, but conflicts are rejected", () => {
  const cleared = context.formatFixedToml(['window.position = "None"']).join("\n\n");
  assert.equal(parseToml(cleared).window.position, "None");
  const coords = context.formatFixedToml([
    "window.position.x = 10",
    "window.position.y = 20",
  ]).join("\n\n");
  assert.equal(parseToml(coords).window.position.x, 10);
  assert.throws(() => context.formatFixedToml([
    'window.position = "None"',
    "window.position.x = 10",
  ]), /Conflicting setting/);
});

test("environment variables and dynamic arrays remain valid TOML", () => {
  const entries = {
    env: [row({ name: "NAME.WITH.DOTS", val: "hello world" }), row({ name: "FOO", val: "bar" })],
    indexed: [row({ idx: "16", color: "#aabbcc" })],
    keyboard: [row({ trigger: "N", triggerType: "key", mods: "Control", resultType: "action", action: "Copy" })],
    mouse: [row({ trigger: "Left", resultType: "action", action: "Copy" })],
    hints: [row({ regex: "https?://example", hyperlinks: "true", hintResult: "action", action: "Copy", bindKey: "O" })],
  };
  context.document = {
    querySelectorAll(selector) {
      const kind = selector.match(/data-dynamic="([^"]+)"/)?.[1];
      return entries[kind] ?? [];
    },
  };
  const blocks = context.formatFixedToml([
    'colors.primary.foreground = "#dddddd"',
    'hints.alphabet = "jfkdls"',
  ]).concat(context.dynamicToml());
  const output = blocks.join("\n\n");
  assert.match(output, /\[env\]\n"NAME\.WITH\.DOTS" = "hello world"/);
  assert.doesNotMatch(output, /^env\./m);
  const parsed = parseToml(output);
  assert.equal(parsed.env["NAME.WITH.DOTS"], "hello world");
  assert.equal(parsed.colors.indexed_colors[0].index, 16);
  assert.equal(parsed.keyboard.bindings[0].key, "N");
  assert.equal(parsed.mouse.bindings[0].mouse, "Left");
  assert.equal(parsed.hints.enabled[0].binding.key, "O");
  entries.env.push(row({ name: "FOO", val: "duplicate" }));
  assert.throws(() => context.dynamicToml(), /duplicated/);
});
