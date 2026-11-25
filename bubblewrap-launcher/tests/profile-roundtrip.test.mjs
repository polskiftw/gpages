import assert from "node:assert/strict";
import {test} from "node:test";
import {mkdtempSync, mkdirSync, writeFileSync, readFileSync, rmSync} from "node:fs";
import {tmpdir} from "node:os";
import path from "node:path";
import {spawnSync} from "node:child_process";
import {makePreset, validateProfile, buildLauncher, getSelectedTools} from "../generator.mjs";

test("extra tool text remains editable after a saved profile is loaded", () => {
  const saved = JSON.stringify({
    ...makePreset("empty"), runtime: "selected", tools: ["git"],
    extraTools: "qemu-system-i386, radare2"
  });
  const {profile: loaded, errors} = validateProfile(JSON.parse(saved));
  assert.deepEqual(errors, []);
  assert.deepEqual(loaded.tools, ["git"]);
  assert.deepEqual(getSelectedTools(loaded), ["git", "qemu-system-i386", "radare2"]);
  assert.match(buildLauncher(loaded).script, /mount_tool 'qemu-system-i386'/);
  assert.match(buildLauncher(loaded).script, /mount_tool 'radare2'/);

  loaded.extraTools = "";
  const {script} = buildLauncher(loaded);
  assert.match(script, /mount_tool 'git'/);
  assert.doesNotMatch(script, /mount_tool '(?:qemu-system-i386|radare2)'/);
});

test("normalization is idempotent without mixing selected and extra tools", () => {
  const input = {
    ...makePreset("empty"), runtime: "selected", tools: ["git", "ls", "git"],
    extraTools: "git,  qemu-system-i386\tradare2"
  };
  const first = validateProfile(input);
  const second = validateProfile(JSON.parse(JSON.stringify(first.profile)));
  assert.deepEqual(first.errors, []);
  assert.deepEqual(second, first);
  assert.deepEqual(first.profile.tools, ["git", "ls"]);
  const {script} = buildLauncher(first.profile);
  assert.equal(script.split("\n").filter((line) => line === "mount_tool 'git'").length, 1);
  assert.ok(validateProfile({...input, extraTools: "qemu-system-i386;bad"}).errors.length);
});

test("literal shell-template markers survive generation and launch arguments", () => {
  const dir = mkdtempSync(path.join(tmpdir(), "bwrap-literal-profile-"));
  try {
    const bin = path.join(dir, "bin");
    const source = path.join(dir, "ROMs @@{literal} and ' quotes");
    const capture = path.join(dir, "args");
    const scriptPath = path.join(dir, "launcher.sh");
    mkdirSync(bin);
    mkdirSync(source);
    writeFileSync(path.join(bin, "bwrap"), '#!/bin/bash\nprintf "%s\\0" "$@" > "$CAPTURE_FILE"\n', {mode: 0o755});
    const profile = {
      ...makePreset("empty"), name: "Game @@{reference}",
      home: "/home/@@{literal}", workdir: "/workspace/@@{literal}",
      mounts: [{source, target: "/workspace/@@{files}", access: "ro"}],
      env: [{name: "GAME_CONFIG", value: "@@{literal} and ${also-literal} ' quotes"}]
    };
    const {script} = buildLauncher(profile);
    assert.ok(script.includes("# Profile: " + profile.name));
    const syntax = spawnSync("bash", ["-n"], {input: script, encoding: "utf8"});
    assert.equal(syntax.status, 0, syntax.stderr);
    writeFileSync(scriptPath, script);
    const result = spawnSync("bash", [scriptPath, "/bin/true"], {
      encoding: "utf8",
      env: {...process.env, PATH: bin + ":/usr/local/bin:/usr/bin:/bin", CAPTURE_FILE: capture}
    });
    assert.equal(result.status, 0, result.stderr);
    const args = readFileSync(capture, "utf8").split("\0");
    assert.ok(args.some((arg, i) => arg === "--ro-bind" && args[i + 1] === source && args[i + 2] === profile.mounts[0].target));
    assert.ok(args.some((arg, i) => arg === "--setenv" && args[i + 1] === "HOME" && args[i + 2] === profile.home));
    assert.ok(args.some((arg, i) => arg === "--chdir" && args[i + 1] === profile.workdir));
    assert.ok(args.some((arg, i) => arg === "--setenv" && args[i + 1] === "GAME_CONFIG" && args[i + 2] === profile.env[0].value));
  } finally {
    rmSync(dir, {recursive: true, force: true});
  }
});
