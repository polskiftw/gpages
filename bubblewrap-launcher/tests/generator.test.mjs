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

test("every statically referenced browser control exists in the HTML", () => {
  const html = readFileSync(new URL("../index.html", import.meta.url), "utf8");
  const browserCode = readFileSync(new URL("../app.mjs", import.meta.url), "utf8");
  const ids = new Set([...html.matchAll(/\bid="([^"]+)"/g)].map((m) => m[1]));
  const referenced = [...browserCode.matchAll(/\$\("([A-Za-z][A-Za-z0-9]*)"\)/g)].map((m) => m[1]);
  assert.ok(referenced.length > 15);
  for (const id of referenced) assert.ok(ids.has(id), "Missing HTML control: " + id);
  const result = spawnSync("node", ["--check", new URL("../app.mjs", import.meta.url).pathname], {encoding: "utf8"});
  assert.equal(result.status, 0, result.stderr);
});


test("shared HOME and working directory create one mountpoint, even when mounted read-only", () => {
  const p = make({
    name: "FF9",
    home: "/workspace",
    workdir: "/workspace",
    mounts: [{source: "~/Documents/ff9", target: "/workspace", access: "ro"}]
  });
  const {script, warnings} = buildLauncher(p);
  syntax(script);
  const dirFlags = script.split("\n").filter((line) => line.includes("--dir") && line.includes("/workspace"));
  assert.deepEqual(dirFlags, ["bwrap_args+=(--dir '/workspace')"]);
  assert.match(script, /add_mount '~\/Documents\/ff9' '\/workspace' 'ro' 0/);
  assert.ok(script.indexOf("--dir '/workspace'") < script.indexOf("add_mount '~/Documents/ff9'"));
  assert.ok(warnings.some((warning) => warning.includes("HOME (/workspace)") && warning.includes("read-only mount")));
  assert.doesNotMatch(script, /if \[\[ 'full' == 'full' \]\]/);
  assert.doesNotMatch(script, /mount_tool\(\)/);
});

test("distinct HOME and workdir paths are emitted once and kept before mounts", () => {
  const {script} = buildLauncher(make({
    home: "/home/sandbox",
    workdir: "/workspace/project",
    mounts: [{source: "/tmp/example", target: "/workspace", access: "ro", optional: true}]
  }));
  syntax(script);
  const dirs = [...script.matchAll(/bwrap_args\+=\(--dir '([^']+)'\)/g)].map((x) => x[1]);
  assert.deepEqual(dirs, ["/workspace", "/home", "/home/sandbox", "/workspace/project"]);
  assert.equal(new Set(dirs).size, dirs.length);
  assert.ok(script.indexOf("--dir '/workspace/project'") < script.indexOf("add_mount '/tmp/example'"));
});

test("selected-tool launcher excludes unused full-runtime branch", () => {
  const {script} = buildLauncher(make({runtime: "selected", tools: ["ls"]}));
  syntax(script);
  assert.match(script, /mount_tool 'ls'/);
  assert.doesNotMatch(script, /for path in \/usr \/bin \/sbin \/lib \/lib64 \/lib32/);
  assert.doesNotMatch(script, /if \[\[ 'selected' == 'full' \]\]/);
});

test("generated bwrap argument list has no duplicate directory creation", () => {
  const dir = mkdtempSync(path.join(tmpdir(), "bwrap-dedupe-"));
  const p = make({
    home: "/workspace",
    workdir: "/workspace",
    mounts: [{source: dir, target: "/workspace", access: "ro"}]
  });
  const ctx = runWithBwrapStub(p);
  try {
    const result = ctx.run();
    assert.equal(result.status, 0, result.stderr);
    const args = readFileSync(ctx.capture, "utf8").trim().split("\n");
    const dirLocations = args.flatMap((arg, index) => arg === "--dir" ? [args[index + 1]] : []);
    assert.equal(dirLocations.filter((location) => location === "/workspace").length, 1);
    assert.ok(args.some((arg, index) => arg === "--ro-bind" && args[index + 1] === dir && args[index + 2] === "/workspace"));
  } finally {
    ctx.clean();
    rmSync(dir, {recursive: true, force: true});
  }
});


test("FF9-style profile keeps filesystem isolation boundaries explicit", () => {
  const p = make({
    name: "FF9",
    network: true,
    runtime: "full",
    home: "/workspace",
    workdir: "/workspace",
    gpu: "nvidia",
    cudaToolkit: true,
    wayland: true,
    mounts: [{source: "~/Documents/ff9", target: "/workspace", access: "rw"}]
  });
  const {script, warnings} = buildLauncher(p);
  syntax(script);
  assert.match(script, /--unshare-user/);
  assert.match(script, /--new-session/);
  assert.match(script, /--unshare-pid/);
  assert.match(script, /--unshare-ipc/);
  assert.match(script, /--unshare-uts/);
  assert.doesNotMatch(script, /--unshare-net/);
  assert.ok(warnings.some((w) => w.includes("host network")));
  assert.match(script, /add_mount '~\/Documents\/ff9' '\/workspace' 'rw' 0/);
  assert.equal((script.match(/--dir '\/workspace'/g) || []).length, 1);
  assert.equal((script.match(/--dir '\/home'/g) || []).length, 0);
  assert.doesNotMatch(script, /--ro-bind-try \/sys \/sys/);
  assert.match(script, /\/sys\/module \/sys\/bus\/pci \/sys\/class\/drm/);
  assert.match(script, /--perms 0700 --dir \"\/run\/user\/\$uid\"/);
  assert.match(script, /--setenv XDG_RUNTIME_DIR/);
  assert.match(script, /--setenv CUDA_HOME/);
  assert.match(script, /--setenv LD_LIBRARY_PATH/);
});

test("clearenv occurs before CUDA/Wayland custom environment and PATH is set only once", () => {
  const p = make({
    gpu: "nvidia", cudaToolkit: true, wayland: true,
    env: [{name: "EDITOR", value: "vim"}]
  });
  const {script} = buildLauncher(p);
  syntax(script);
  const firstClear = script.indexOf("  --clearenv");
  assert.ok(firstClear > 0, "clearenv should be in bwrap initial argument array");
  assert.equal((script.match(/--clearenv/g) || []).length, 2, "one flag and one explanatory comment");
  for (const name of ["XDG_RUNTIME_DIR", "WAYLAND_DISPLAY", "CUDA_HOME", "CUDA_PATH", "LD_LIBRARY_PATH", "PATH", "EDITOR"]) {
    const token = name === "EDITOR" ? "--setenv 'EDITOR'" : "--setenv " + name;
    assert.ok(script.indexOf(token) > firstClear, name + " should be set after clearenv");
  }
  assert.equal((script.match(/bwrap_args\+=\(--setenv PATH /g) || []).length, 1);
  assert.match(script, /sandbox_path="\$cuda_root\/bin:\$sandbox_path"/);
  assert.match(script, /cuda_real="\$\(readlink -e -- "\$candidate"\)"/);
});

test("runtime command arguments are captured without repeated clearenv or PATH assignments", () => {
  const p = make({home: "/workspace", workdir: "/workspace", env: [{name: "FF9_TEST", value: "ok"}]});
  const ctx = runWithBwrapStub(p, ["/usr/bin/true"]);
  try {
    const result = ctx.run();
    assert.equal(result.status, 0, result.stderr);
    const args = readFileSync(ctx.capture, "utf8").trim().split("\n");
    const clearIndexes = args.flatMap((part, i) => part === "--clearenv" ? [i] : []);
    const pathIndexes = args.flatMap((part, i) => part === "--setenv" && args[i + 1] === "PATH" ? [i] : []);
    assert.equal(clearIndexes.length, 1);
    assert.equal(pathIndexes.length, 1);
    assert.ok(clearIndexes[0] < pathIndexes[0]);
    assert.ok(args.some((part, i) => part === "--setenv" && args[i + 1] === "FF9_TEST" && args[i + 2] === "ok"));
    assert.ok(args.includes("--new-session"));
  } finally { ctx.clean(); }
});


test("Wayland passthrough uses one private runtime socket, not an entire host /run directory", () => {
  const p = make({wayland: true});
  const {script} = buildLauncher(p);
  syntax(script);
  assert.match(script, /host_runtime_dir="\$\{XDG_RUNTIME_DIR:-\/run\/user\/\$uid\}"/);
  assert.match(script, /for candidate in "\$host_runtime_dir"\/wayland-\*/);
  assert.match(script, /--tmpfs \/run --dir \/run\/user --perms 0700 --dir/);
  assert.match(script, /--bind "\$host_wayland_socket" "\/run\/user\/\$uid\/wayland-0"/);
  assert.match(script, /--setenv WAYLAND_DISPLAY wayland-0/);
  assert.doesNotMatch(script, /--bind "\$XDG_RUNTIME_DIR" "\$XDG_RUNTIME_DIR"/);
});

test("MCP bridge preset matches existing start-gentoo-shell startup contract", () => {
  for (const id of ["bridge", "minecraft", "developer"]) {
    const p = makePreset(id);
    assert.equal(p.launchMode, "mcp", id);
    assert.equal(p.home, "/workspace", id);
    assert.equal(p.mcpHostDir, "$HOME/Documents/gentoo-shell-mcp");
    const {script} = buildLauncher(p);
    syntax(script);
    assert.match(script, /bwrap_args\+=\(--ro-bind "\$mcp_server_dir" \/opt\/mcp\)/);
    assert.match(script, /cmd=\(\/opt\/mcp\/.venv\/bin\/python \/opt\/mcp\/server.py\)/);
    assert.match(script, /case "\$\{1:-\}" in/);
    assert.match(script, /--shell\) cmd=\(/);
    assert.match(script, /--test\) cmd=\(/);
    assert.match(script, /--dir '\/opt'/);
    assert.ok(script.indexOf("--dir '/opt'") < script.indexOf('--ro-bind "$mcp_server_dir" /opt/mcp'));
    assert.doesNotMatch(script, /cmd=\(\/bin\/bash --noprofile --norc -i\)\nfi/);
  }
});

test("old FF9 profiles without startup mode migrate to the MCP bridge by default", () => {
  const p = makePreset("empty");
  delete p.launchMode;
  const normalized = validateProfile(p);
  assert.deepEqual(normalized.errors, []);
  assert.equal(normalized.profile.launchMode, "mcp");
  assert.equal(normalized.profile.mcpHostDir, "$HOME/Documents/gentoo-shell-mcp");
});

test("MCP host folder is mounted read-only and no-argument launch runs server", () => {
  const dir = mkdtempSync(path.join(tmpdir(), "bwrap-mcp-host-"));
  mkdirSync(path.join(dir, ".venv", "bin"), {recursive: true});
  writeFileSync(path.join(dir, "server.py"), "# test server\n");
  writeFileSync(path.join(dir, ".venv", "bin", "python"), "#!/bin/sh\nexit 0\n", {mode: 0o755});
  const p = makePreset("bridge");
  p.mcpHostDir = dir;
  p.mounts = [];
  const ctx = runWithBwrapStub(p);
  try {
    const run = ctx.run();
    assert.equal(run.status, 0, run.stderr);
    const args = readFileSync(ctx.capture, "utf8").trim().split("\n");
    assert.ok(args.some((x, i) => x === "--ro-bind" && args[i+1] === dir && args[i+2] === "/opt/mcp"));
    assert.deepEqual(args.slice(-3), ["--", "/opt/mcp/.venv/bin/python", "/opt/mcp/server.py"]);
  } finally {
    ctx.clean();
    rmSync(dir, {recursive: true, force: true});
  }
});

test("MCP --shell and --test use expected commands while missing MCP files fail clearly", () => {
  const dir = mkdtempSync(path.join(tmpdir(), "bwrap-mcp-options-"));
  mkdirSync(path.join(dir, ".venv", "bin"), {recursive: true});
  writeFileSync(path.join(dir, "server.py"), "# dummy\n");
  writeFileSync(path.join(dir, ".venv", "bin", "python"), "#!/bin/sh\nexit 0\n", {mode: 0o755});
  const p = makePreset("bridge");
  p.mcpHostDir = dir;
  p.mounts = [];
  for (const [flag, expected] of [["--shell", "/bin/bash"], ["--test", "-ec"]]) {
    const ctx = runWithBwrapStub(p, [flag]);
    try {
      const run = ctx.run();
      assert.equal(run.status, 0, run.stderr);
      const args = readFileSync(ctx.capture, "utf8").trim().split("\n");
      assert.ok(args.includes(expected), flag + " missing " + expected);
      assert.equal(args.includes("/opt/mcp/server.py"), false, flag + " should not run MCP server");
    } finally { ctx.clean(); }
  }
  rmSync(dir, {recursive: true, force: true});
  const missing = makePreset("bridge");
  missing.mcpHostDir = "/nonexistent/mcp-root";
  missing.mounts = [];
  const ctx = runWithBwrapStub(missing);
  try {
    const run = ctx.run();
    assert.notEqual(run.status, 0);
    assert.match(run.stderr, /MCP server.py or .venv\/bin\/python is missing/);
    assert.equal(existsSync(ctx.capture), false, "must fail before bwrap starts");
  } finally { ctx.clean(); }
});

test("interactive shell remains selectable and has no hidden MCP mount", () => {
  const p = makePreset("empty");
  assert.equal(p.launchMode, "shell");
  const {script} = buildLauncher(p);
  syntax(script);
  assert.doesNotMatch(script, /mcp_server_dir=/);
  assert.doesNotMatch(script, /\/opt\/mcp\/server.py/);
  assert.match(script, /cmd=\(\/bin\/bash --noprofile --norc -i\)/);
});

test("MCP server mount cannot be overridden by another user mount", () => {
  const p = makePreset("bridge");
  p.mounts.push({source: "/tmp/other", target: "/opt/mcp", access: "rw"});
  assert.ok(validateProfile(p).errors.some((x) => x.includes("/opt/mcp is reserved")));
  assert.throws(() => buildLauncher(p));
  p.mounts[1].target = "/opt";
  assert.ok(validateProfile(p).errors.some((x) => x.includes("/opt/mcp is reserved")));
});


test("private identity defaults on for MCP bridge profiles and remains optional for shell presets", () => {
  for (const name of ["bridge", "minecraft", "developer"]) {
    const p = makePreset(name);
    assert.equal(p.privateIdentity, true, name);
    const {script} = buildLauncher(p);
    syntax(script);
    assert.match(script, /--ro-bind-data "\$sandbox_passwd_fd" \/etc\/passwd/);
    assert.match(script, /--ro-bind-data "\$sandbox_group_fd" \/etc\/group/);
    assert.match(script, /--ro-bind-data "\$sandbox_hosts_fd" \/etc\/hosts/);
    assert.doesNotMatch(script, /for path in \/etc\/passwd \/etc\/group \/etc\/hosts/);
  }
  assert.equal(makePreset("empty").privateIdentity, false);
  const shellScript = buildLauncher(makePreset("empty")).script;
  assert.match(shellScript, /for path in \/etc\/passwd \/etc\/group \/etc\/hosts/);
  assert.doesNotMatch(shellScript, /--ro-bind-data/);
});

test("old saved FF9 profiles get private identity enabled without losing their settings", () => {
  const p = makePreset("bridge");
  p.name = "ff9";
  p.home = "/workspace";
  p.mounts = [{source: "~/Documents/ff9", target: "/workspace", access: "rw"}];
  delete p.privateIdentity;
  const {profile, errors} = validateProfile(p);
  assert.deepEqual(errors, []);
  assert.equal(profile.privateIdentity, true);
  assert.equal(profile.mounts[0].source, "~/Documents/ff9");
  assert.equal(profile.workdir, "/workspace");
});

test("private passwd/group/hosts content travels via inherited file descriptors, not host files", () => {
  const p = makePreset("empty");
  p.privateIdentity = true;
  p.home = "/workspace";
  const ctx = runWithBwrapStub(p, ["/usr/bin/true"]);
  const stub = path.join(ctx.dir, "bin", "bwrap");
  writeFileSync(stub, [
    "#!/usr/bin/env bash",
    "set -euo pipefail",
    "printf '%s\\n' \"$@\" > \"$CAPTURE_FILE\"",
    "args=(\"$@\")",
    "for ((i=0; i < $#; i++)); do",
    "  if [[ \"\${args[i]}\" == --ro-bind-data ]]; then",
    "    fd=\"\${args[i+1]}\"",
    "    dest=\"\${args[i+2]##*/}\"",
    "    cat \"/proc/self/fd/$fd\" > \"$CAPTURE_FILE.$dest\"",
    "  fi",
    "done",
    ""
  ].join("\n"), {mode: 0o755});
  try {
    const result = ctx.run();
    assert.equal(result.status, 0, result.stderr);
    const args = readFileSync(ctx.capture, "utf8").trim().split("\n");
    for (const target of ["/etc/passwd", "/etc/group", "/etc/hosts"]) {
      assert.ok(args.some((arg, i) => arg === "--ro-bind-data" && /^\d+$/.test(args[i + 1]) && args[i + 2] === target), target);
      assert.equal(args.some((arg, i) => arg === "--ro-bind" && args[i + 1] === target && args[i + 2] === target), false);
    }
    const passwd = readFileSync(ctx.capture + ".passwd", "utf8");
    const group = readFileSync(ctx.capture + ".group", "utf8");
    const hosts = readFileSync(ctx.capture + ".hosts", "utf8");
    const uid = process.getuid();
    const gid = process.getgid();
    assert.ok(passwd.includes(":x:" + uid + ":" + gid + ":Sandbox User:/workspace:/bin/bash\n"), passwd);
    assert.ok(group.includes(":x:" + gid + ":\n"), group);
    assert.equal(hosts, "127.0.0.1 localhost\n::1 localhost\n");
    assert.equal(passwd.split("\n").filter(Boolean).length, 1);
    assert.equal(group.split("\n").filter(Boolean).length, 1);
    assert.doesNotMatch(buildLauncher(p).script, /mktemp|\.sandbox\/passwd|\.sandbox\/group|\.sandbox\/hosts/);
  } finally {
    ctx.clean();
  }
});

test("turning off private files preserves host read-only binds and warns for MCP", () => {
  const p = makePreset("bridge");
  p.privateIdentity = false;
  const {script, warnings} = buildLauncher(p);
  syntax(script);
  assert.doesNotMatch(script, /--ro-bind-data/);
  assert.match(script, /for path in \/etc\/passwd \/etc\/group \/etc\/hosts/);
  assert.ok(warnings.some((w) => w.includes("host passwd/group/hosts")));
});

test("private identity file HOME and shell match profile customization", () => {
  const p = makePreset("bridge");
  p.home = "/home/sandbox";
  p.shell = "sh";
  p.runtime = "selected";
  const {script} = buildLauncher(p);
  syntax(script);
  assert.match(script, /'\/home\/sandbox' '\/usr\/bin\/sh'/);
  assert.match(script, /--ro-bind-data "\$sandbox_passwd_fd" \/etc\/passwd/);
});


test("private passwd/group/hosts use standard readable 0644 modes", () => {
  const p = makePreset("bridge");
  const {script} = buildLauncher(p);
  syntax(script);
  for (const [fd, file] of [
    ["sandbox_passwd_fd", "passwd"],
    ["sandbox_group_fd", "group"],
    ["sandbox_hosts_fd", "hosts"]
  ]) {
    assert.match(script, new RegExp('bwrap_args\\+=\\(--perms 0644 --ro-bind-data "\\$' + fd + '" /etc/' + file + '\\)'));
  }
  assert.doesNotMatch(script, /--ro-bind \/etc\/passwd \/etc\/passwd/);
  assert.doesNotMatch(script, /--ro-bind \/etc\/group \/etc\/group/);
  assert.doesNotMatch(script, /--ro-bind \/etc\/hosts \/etc\/hosts/);
});

test("CUDA compiler discovery ignores installations without nvcc and exports CUDACXX", () => {
  const p = makePreset("minecraft");
  const {script} = buildLauncher(p);
  syntax(script);
  assert.match(script, /if \[\[ -x "\$candidate\/bin\/nvcc" \]\]; then/);
  assert.match(script, /--setenv CUDA_HOME "\$cuda_root" --setenv CUDA_PATH "\$cuda_root" --setenv CUDACXX "\$cuda_root\/bin\/nvcc"/);
  assert.match(script, /no executable nvcc found under/);
  assert.doesNotMatch(script, /if \[\[ -d "\$candidate" \]\]; then/);
});


test("MCP smoke test imports from server folder without changing startup workdir", () => {
  const {script} = buildLauncher(makePreset("bridge"));
  syntax(script);
  assert.match(script, /\(cd \/opt\/mcp && \/opt\/mcp\/.venv\/bin\/python -c/);
  assert.match(script, /PASS: sandbox working directory: \$PWD \(HOME=\$HOME\)/);
  assert.doesNotMatch(script, /'cd \/opt\/mcp; test -r server\.py/);
});
