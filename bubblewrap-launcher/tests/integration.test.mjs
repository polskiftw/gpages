import assert from "node:assert/strict";
import {test} from "node:test";
import {mkdtempSync, mkdirSync, writeFileSync, readFileSync, readlinkSync, rmSync, existsSync, statSync, symlinkSync} from "node:fs";
import {tmpdir} from "node:os";
import path from "node:path";
import {spawnSync} from "node:child_process";
import {buildLauncher, makePreset, validateProfile} from "../generator.mjs";

// Use the shared network for filesystem tests. Some container hosts permit user
// namespaces but deny the NETLINK_ROUTE socket needed to configure a new one.
// Network flag generation is covered separately in generator.test.mjs.
const hostEnv = {...process.env, PATH: "/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin"};
const probe = spawnSync("bwrap", [
  "--unshare-user", "--unshare-pid", "--unshare-ipc", "--unshare-uts",
  "--ro-bind", "/", "/", "--proc", "/proc", "--dev", "/dev", "--", "/bin/true"
], {encoding: "utf8", timeout: 10000, env: hostEnv});
const unavailable = probe.status === 0 ? false :
  "Real Bubblewrap namespaces are unavailable: " + (probe.error?.message || probe.stderr.trim() || "probe failed");
const required = process.env.BWRAP_INTEGRATION_REQUIRED === "1";

test("real Bubblewrap namespace support is available", {
  skip: !required && unavailable
}, () => {
  assert.equal(unavailable, false, unavailable || "");
});

function integration(name, run) {
  test("real Bubblewrap: " + name, {skip: unavailable}, run);
}

function fixture(t) {
  const dir = mkdtempSync(path.join(tmpdir(), "bwrap-real-"));
  t.after(() => rmSync(dir, {recursive: true, force: true}));
  return {
    dir,
    directory(name) {
      const result = path.join(dir, name);
      mkdirSync(result, {recursive: true});
      return result;
    }
  };
}

function launch(options, command, env = {}) {
  const profile = {...makePreset("empty"), network: true, ...options};
  return spawnSync("bash", ["-s", "--", "/usr/bin/bash", "-ec", command], {
    input: buildLauncher(profile).script,
    encoding: "utf8",
    timeout: 15000,
    env: {...hostEnv, ...env}
  });
}

function succeeds(result) {
  assert.equal(result.error, undefined, result.error?.message);
  assert.equal(result.status, 0, result.stderr || result.stdout);
}

function socket(file) {
  // Inert socket inodes exercise mount visibility without a desktop, audio
  // server, or permission to listen on sockets. This does not test IPC traffic.
  const result = spawnSync("python3", ["-c",
    "import os, stat, sys; os.mknod(sys.argv[1], stat.S_IFSOCK | 0o600)", file
  ], {encoding: "utf8", timeout: 10000, env: hostEnv});
  succeeds(result);
}

const networkProbe = spawnSync("bwrap", [
  "--unshare-user", "--unshare-net", "--ro-bind", "/", "/", "--", "/bin/true"
], {encoding: "utf8", timeout: 10000, env: hostEnv});
test("real Bubblewrap: the default profile starts in a separate network namespace", {
  skip: !required && (unavailable || (networkProbe.status !== 0 && "Host does not permit network namespace setup"))
}, () => {
  assert.equal(networkProbe.status, 0, networkProbe.error?.message || networkProbe.stderr);
  const result = launch({network: false}, "readlink /proc/self/ns/net");
  succeeds(result);
  assert.notEqual(result.stdout.trim(), readlinkSync("/proc/self/ns/net"));
});

for (const runtime of ["full", "selected"]) {
  integration(runtime + " runtime starts with usable HOME, working directory, and private identity", () => {
    const result = launch({runtime, tools: [], privateIdentity: true}, [
      'test "$PWD" = /workspace',
      'test "$HOME" = /home/sandbox',
      'test -d "$HOME"',
      'printf home > "$HOME/check"',
      'printf workspace > "$PWD/check"',
      'IFS=: read -r user password uid gid description account_home account_shell < /etc/passwd',
      'test "$account_home" = "$HOME"',
      'test "$account_shell" = ' + (runtime === "selected" ? "/usr/bin/bash" : "/bin/bash"),
      'printf "started\\n"'
    ].join("\n"));
    succeeds(result);
    assert.equal(result.stdout, "started\n");
  });
}

integration("HOME and workdir are created inside a masked parent", () => {
  const result = launch({
    home: "/workspace/home",
    workdir: "/workspace/game",
    mounts: [{target: "/workspace", access: "mask"}]
  }, [
    'test "$PWD" = /workspace/game',
    'test -d "$HOME"',
    'printf data > "$HOME/save"',
    'printf mod > "$PWD/output"'
  ].join("\n"));
  succeeds(result);
});

integration("read-only game data, writable output, and a masked child retain their permissions", (t) => {
  const ctx = fixture(t);
  const source = ctx.directory("source");
  const output = ctx.directory("output");
  ctx.directory("source/generated");
  ctx.directory("source/private");
  writeFileSync(path.join(source, "game.dat"), "original");
  writeFileSync(path.join(source, "private", "secret"), "host-only");
  const result = launch({
    workdir: "/workspace/generated",
    mounts: [
      {source: output, target: "/workspace/generated", access: "rw"},
      {target: "/workspace/private", access: "mask"},
      {source, target: "/workspace", access: "ro"}
    ]
  }, [
    'test "$(</workspace/game.dat)" = original',
    'if (printf changed > /workspace/game.dat) 2>/dev/null; then exit 1; fi',
    'test ! -e /workspace/private/secret',
    'printf temporary > /workspace/private/scratch',
    'printf patched > "$PWD/mod.dat"'
  ].join("\n"));
  succeeds(result);
  assert.equal(readFileSync(path.join(source, "game.dat"), "utf8"), "original");
  assert.equal(readFileSync(path.join(source, "private", "secret"), "utf8"), "host-only");
  assert.equal(existsSync(path.join(source, "private", "scratch")), false);
  assert.equal(existsSync(path.join(source, "generated", "mod.dat")), false);
  assert.equal(readFileSync(path.join(output, "mod.dat"), "utf8"), "patched");
});

integration("a missing child mountpoint below read-only data fails clearly without modifying host data", (t) => {
  const ctx = fixture(t);
  const source = ctx.directory("source");
  const output = ctx.directory("output");
  const result = launch({mounts: [
    {source, target: "/workspace", access: "ro"},
    {source: output, target: "/workspace/generated", access: "rw"}
  ]}, "true");
  assert.notEqual(result.status, 0);
  assert.match(result.stderr, /Error: destination/);
  assert.match(result.stderr, /read.only/i);
  assert.match(result.stderr, /\/workspace\/generated/);
  assert.match(result.stderr, /create|mkdir|existing/i);
  assert.equal(existsSync(path.join(source, "generated")), false);
});

integration("a file mount below read-only data requires an existing file placeholder", (t) => {
  const ctx = fixture(t);
  const source = ctx.directory("source");
  const patchFile = path.join(ctx.dir, "patch.cfg");
  const placeholder = path.join(source, "patch.cfg");
  writeFileSync(patchFile, "patch-settings");
  const options = {mounts: [
    {source, target: "/workspace", access: "ro"},
    {source: patchFile, target: "/workspace/patch.cfg", access: "rw"}
  ]};
  const missing = launch(options, "true");
  assert.notEqual(missing.status, 0);
  assert.match(missing.stderr, /Error: destination \/workspace\/patch.cfg/);
  assert.match(missing.stderr, /Create an empty file/);
  assert.equal(existsSync(placeholder), false);
  assert.equal(readFileSync(patchFile, "utf8"), "patch-settings");

  writeFileSync(placeholder, "original-placeholder");
  const present = launch(options,
    'test "$(</workspace/patch.cfg)" = patch-settings; printf updated > /workspace/patch.cfg');
  succeeds(present);
  assert.equal(readFileSync(patchFile, "utf8"), "updated");
  assert.equal(readFileSync(placeholder, "utf8"), "original-placeholder");
});

for (const field of ["home", "workdir"]) {
  integration("a missing " + field + " under a read-only mount fails clearly", (t) => {
    const ctx = fixture(t);
    const source = ctx.directory("source");
    const result = launch({
      [field]: "/workspace/missing",
      mounts: [{source, target: "/workspace", access: "ro"}]
    }, "true");
    assert.notEqual(result.status, 0);
    assert.match(result.stderr, /Error: destination \/workspace\/missing/);
    assert.match(result.stderr, /read.only/);
    assert.match(result.stderr, /Create an empty directory/);
    assert.equal(existsSync(path.join(source, "missing")), false);
  });
}

integration("existing HOME and workdir under a read-only mount remain usable", (t) => {
  const ctx = fixture(t);
  const source = ctx.directory("source");
  const hostHome = ctx.directory("source/home");
  const hostWork = ctx.directory("source/game");
  writeFileSync(path.join(hostWork, "game.dat"), "original");
  const homeMode = statSync(hostHome).mode;
  const workMode = statSync(hostWork).mode;
  const result = launch({home: "/workspace/home", workdir: "/workspace/game", mounts: [
    {source, target: "/workspace", access: "ro"}
  ]}, [
    'test "$PWD" = /workspace/game',
    'test -d "$HOME"',
    'test "$(<game.dat)" = original',
    'if (printf changed > "$HOME/new") 2>/dev/null; then exit 1; fi'
  ].join("\n"));
  succeeds(result);
  assert.equal(existsSync(path.join(hostHome, "new")), false);
  assert.equal(statSync(hostHome).mode, homeMode);
  assert.equal(statSync(hostWork).mode, workMode);
});

integration("destination symlinks resolve against the sandbox instead of the host", (t) => {
  const ctx = fixture(t);
  const source = ctx.directory("source");
  const output = ctx.directory("output");
  const child = ctx.directory("child");
  ctx.directory("output/mods");
  symlinkSync("/bwrap-integration-output", path.join(source, "link"));
  writeFileSync(path.join(child, "mod.dat"), "patched");
  const result = launch({workdir: "/assets/link/mods", mounts: [
    {source, target: "/assets", access: "ro"},
    {source: output, target: "/bwrap-integration-output", access: "rw"},
    {source: child, target: "/assets/link/mods", access: "rw"}
  ]}, 'test "$(<mod.dat)" = patched; printf saved > save.dat');
  succeeds(result);
  assert.equal(readFileSync(path.join(child, "save.dat"), "utf8"), "saved");
  assert.equal(existsSync(path.join(output, "mods", "save.dat")), false);
  assert.equal(readlinkSync(path.join(source, "link")), "/bwrap-integration-output");
});

integration("a missing optional child is skipped before checking a read-only parent", (t) => {
  const ctx = fixture(t);
  const source = ctx.directory("source");
  const result = launch({mounts: [
    {source, target: "/workspace", access: "ro"},
    {source: path.join(ctx.dir, "missing"), target: "/workspace/generated", access: "rw", optional: true}
  ]}, 'test "$PWD" = /workspace; test ! -e /workspace/generated');
  succeeds(result);
  assert.match(result.stderr, /Skipping optional missing path/);
  assert.equal(existsSync(path.join(source, "generated")), false);
});

integration("a skipped optional parent does not prevent a valid child mount", (t) => {
  const ctx = fixture(t);
  const output = ctx.directory("output");
  const result = launch({workdir: "/workspace/generated", mounts: [
    {source: path.join(ctx.dir, "missing"), target: "/workspace", access: "ro", optional: true},
    {source: output, target: "/workspace/generated", access: "rw"}
  ]}, 'printf patched > "$PWD/mod.dat"');
  succeeds(result);
  assert.match(result.stderr, /Skipping optional missing path/);
  assert.equal(readFileSync(path.join(output, "mod.dat"), "utf8"), "patched");
});

integration("an optional dangling source symlink is treated as a missing path", (t) => {
  const ctx = fixture(t);
  const source = path.join(ctx.dir, "dangling");
  symlinkSync(path.join(ctx.dir, "missing"), source);
  const result = launch({mounts: [{
    source, target: "/workspace/optional", access: "ro", optional: true
  }]}, "test ! -e /workspace/optional");
  succeeds(result);
  assert.match(result.stderr, /Skipping optional missing path/);
});

for (const wholeRun of [true, false]) {
  integration("Wayland preserves custom " + (wholeRun ? "/run" : "/run/user/<uid>") + " mounts", (t) => {
    const ctx = fixture(t);
    const uid = process.getuid();
    const customRun = ctx.directory("run");
    const customRuntime = ctx.directory("run/user/" + uid);
    const hostRuntime = ctx.directory("wayland");
    writeFileSync(path.join(customRuntime, "marker"), "custom-runtime");
    // An existing file avoids creating a mountpoint in the host's custom tree.
    writeFileSync(path.join(customRuntime, "wayland-0"), "mountpoint");
    socket(path.join(customRuntime, "pipewire-0"));
    socket(path.join(hostRuntime, "wayland-test"));
    const result = launch({wayland: true, mounts: [{
      source: wholeRun ? customRun : customRuntime,
      target: wholeRun ? "/run" : "/run/user/" + uid,
      access: "rw"
    }]}, [
      'test "$XDG_RUNTIME_DIR" = /run/user/' + uid,
      'test "$WAYLAND_DISPLAY" = wayland-0',
      'test -S "$XDG_RUNTIME_DIR/$WAYLAND_DISPLAY"',
      'test -S "$XDG_RUNTIME_DIR/pipewire-0"',
      'test "$(<"$XDG_RUNTIME_DIR/marker")" = custom-runtime'
    ].join("\n"), {XDG_RUNTIME_DIR: hostRuntime, WAYLAND_DISPLAY: "wayland-test"});
    succeeds(result);
    assert.equal(readFileSync(path.join(customRuntime, "marker"), "utf8"), "custom-runtime");
    assert.equal(readFileSync(path.join(customRuntime, "wayland-0"), "utf8"), "mountpoint");
    assert.equal(statSync(path.join(customRuntime, "pipewire-0")).isSocket(), true);
  });
}

test("MCP child mounts are rejected before a launcher can silently hide them", () => {
  for (const access of ["ro", "rw", "mask"]) {
    const profile = {...makePreset("bridge"), mounts: [{
      source: "/tmp/plugins", target: "/opt/mcp/plugins", access
    }]};
    assert.ok(validateProfile(profile).errors.some((error) => error.includes("/opt/mcp")));
    assert.throws(() => buildLauncher(profile), /\/opt\/mcp/);
  }
  for (const [launchMode, target] of [["mcp", "/opt/mcp-backup"], ["shell", "/opt/mcp/plugins"]]) {
    const profile = {...makePreset("bridge"), launchMode, mounts: [{
      source: "/tmp/plugins", target, access: "ro"
    }]};
    assert.deepEqual(validateProfile(profile).errors, []);
    assert.doesNotThrow(() => buildLauncher(profile));
  }
});

test("private identity rejects HOME values that would corrupt passwd fields", () => {
  const profile = {...makePreset("bridge"), home: "/workspace/SNES: ROMs"};
  assert.ok(validateProfile(profile).errors.some((error) => /HOME|passwd|identity/.test(error)));
  assert.throws(() => buildLauncher(profile));
  for (const valid of [
    {...profile, privateIdentity: false},
    {...makePreset("bridge"), workdir: "/workspace/SNES: ROMs"}
  ]) {
    assert.deepEqual(validateProfile(valid).errors, []);
    assert.doesNotThrow(() => buildLauncher(valid));
  }
});
