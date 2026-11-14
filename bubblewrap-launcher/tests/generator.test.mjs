import assert from "node:assert/strict";
import {test} from "node:test";
import {mkdtempSync, mkdirSync, writeFileSync, readFileSync, rmSync, existsSync} from "node:fs";
import {tmpdir} from "node:os";
import path from "node:path";
import {spawnSync} from "node:child_process";
import {buildLauncher, makePreset, validateProfile} from "../generator.mjs";

function make(options = {}) {
  return {...makePreset("empty"), ...options};
}
function syntax(script) {
  const result = spawnSync("bash", ["-n"], {input: script, encoding: "utf8"});
  assert.equal(result.status, 0, result.stderr);
}
function runWithBwrapStub(profile, extraArgs = [], cwd = undefined) {
  const dir = mkdtempSync(path.join(tmpdir(), "bwrap-generator-test-"));
  const bin = path.join(dir, "bin");
  mkdirSync(bin);
  const stub = path.join(bin, "bwrap");
  const capture = path.join(dir, "bwrap-args.txt");
  writeFileSync(stub, '#!/bin/bash\nprintf "%s\\n" "$@" > "$CAPTURE_FILE"\n', {mode: 0o755});
  const scriptPath = path.join(dir, "start-isolated.sh");
  writeFileSync(scriptPath, buildLauncher(profile).script, {mode: 0o755});
  return {
    dir, capture, scriptPath,
    run: () => spawnSync("bash", [scriptPath, ...extraArgs], {
      encoding: "utf8",
      cwd,
      env: {...process.env, CAPTURE_FILE: capture, PATH: bin + ":/usr/local/bin:/usr/bin:/bin"}
    }),
    clean: () => rmSync(dir, {recursive: true, force: true})
  };
}
test("all presets emit syntactically valid Bash", () => {
  for (const name of ["empty", "bridge", "developer", "minecraft"]) {
    const {script} = buildLauncher(makePreset(name));
    syntax(script);
    assert.match(script, /^#!\/usr\/bin\/env bash/);
    assert.match(script, /exec bwrap /);
    assert.doesNotMatch(script, /\beval\b/);
  }
});
test("no network and no broad host home mounts by default", () => {
  const {script, warnings} = buildLauncher(makePreset("empty"));
  assert.match(script, /--unshare-net/);
  assert.doesNotMatch(script, /add_mount '\$HOME'/);
  assert.ok(warnings.some((w) => w.includes("No host folders")));
});
test("network enabled means sharing the host network, not outbound-only", () => {
  const {script, warnings} = buildLauncher(make({network: true}));
  assert.doesNotMatch(script, /--unshare-net/);
  assert.ok(warnings.some((w) => w.includes("not an outbound-only")));
});
test("read-only, read-write, masks and nested mount order are preserved", () => {
  const p = make({mounts: [
    {source: "/tmp/deep", target: "/workspace/deep", access: "rw"},
    {source: "/tmp/base", target: "/workspace", access: "ro"},
    {target: "/workspace/deep/private", access: "mask"},
  ]});
  const {script} = buildLauncher(p);
  assert.ok(script.indexOf("add_mount '/tmp/base'") < script.indexOf("add_mount '/tmp/deep'"));
  assert.ok(script.indexOf("add_mount '/tmp/deep'") < script.indexOf("bwrap_args+=(--tmpfs '/workspace/deep/private')"));
  assert.match(script, /add_mount '\/tmp\/deep' '\/workspace\/deep' 'rw' 0/);
  syntax(script);
});
test("invalid paths, duplicate mount points and bad tool names are rejected", () => {
  let p = make({mounts: [{source: "relative", target: "/workspace", access: "ro"}]});
  assert.ok(validateProfile(p).errors.some((e) => e.includes("host path")));
  p = make({mounts: [{source: "/a", target: "/workspace/a", access: "ro"}, {source: "/b", target: "/workspace/a/", access: "rw"}]});
  assert.ok(validateProfile(p).errors.some((e) => e.includes("duplicate")));
  p = make({mounts: [{source: "/a", target: "/workspace/../etc", access: "ro"}]});
  assert.ok(validateProfile(p).errors.some((e) => e.includes("sandbox path")));
  p = make({runtime: "selected", extraTools: "curl;evil"});
  assert.ok(validateProfile(p).errors.some((e) => e.includes("Tool names")));
  assert.throws(() => buildLauncher(p));
});
test("shell metacharacters in host path are data, not code", () => {
  const tmp = mkdtempSync(path.join(tmpdir(), "bwrap-quoting-"));
  const marker = path.join(tmp, "UNWANTED");
  const source = path.join(tmp, "space ' apostrophe; $(touch UNWANTED) and symbols");
  mkdirSync(source);
  const p = make({mounts: [{source, target: "/workspace/quoted", access: "rw"}]});
  const ctx = runWithBwrapStub(p, ["/bin/true"], tmp);
  try {
    const result = ctx.run();
    assert.equal(result.status, 0, result.stderr);
    assert.equal(existsSync(marker), false);
    const args = readFileSync(ctx.capture, "utf8");
    assert.ok(args.includes(source));
    assert.ok(args.includes("/workspace/quoted"));
    assert.ok(args.includes("--bind"));
    assert.ok(args.includes("/bin/true"));
  } finally {
    ctx.clean(); rmSync(tmp, {recursive: true, force: true});
  }
});
test("home-variable paths expand only at launch, without eval", () => {
  const p = make({mounts: [{source: "$HOME/Documents/a path", target: "/workspace/demo", access: "ro", optional: true}]});
  const {script} = buildLauncher(p);
  assert.match(script, /add_mount '\$HOME\/Documents\/a path'/);
  syntax(script);
  const ctx = runWithBwrapStub(p);
  try {
    const result = ctx.run();
    assert.equal(result.status, 0, result.stderr);
    assert.match(result.stderr, /Skipping optional missing path:/);
  } finally { ctx.clean(); }
});
test("selected tools mode mounts just requested executable names, not all of /usr", () => {
  const p = make({runtime: "selected", tools: ["ls"], extraTools: ""});
  const {script, warnings} = buildLauncher(p);
  assert.match(script, /mount_tool 'ls'/);
  assert.ok(warnings.some((w) => w.includes("best-effort")));
  syntax(script);
  const ctx = runWithBwrapStub(p);
  try {
    const result = ctx.run();
    assert.equal(result.status, 0, result.stderr);
    const args = readFileSync(ctx.capture, "utf8");
    assert.ok(args.includes("/usr/bin/ls"));
    assert.equal(args.includes("--ro-bind\n/usr\n/usr\n"), false);
  } finally { ctx.clean(); }
});
test("GPU and CUDA support is separate from network permissions", () => {
  const p = make({gpu: "nvidia", cudaToolkit: true, network: false, wayland: true});
  const {script} = buildLauncher(p);
  assert.match(script, /--unshare-net/);
  assert.match(script, /\/dev\/nvidiactl/);
  assert.match(script, /--dev-bind \/dev\/dri/);
  assert.match(script, /\/opt\/cuda-\*/);
  assert.match(script, /WAYLAND_DISPLAY/);
  syntax(script);
});
test("environment names are checked and values quoted", () => {
  const p = make({env: [{name: "MY_FLAG", value: "hello ' there; $(id)"}]});
  const {script} = buildLauncher(p);
  assert.match(script, /--setenv 'MY_FLAG'/);
  syntax(script);
  assert.ok(validateProfile(make({env: [{name: "BAD NAME", value: "x"}]})).errors.length);
});
