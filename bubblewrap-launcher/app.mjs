import {TOOL_GROUPS, makePreset, validateProfile, buildLauncher} from "./generator.mjs";

const $ = (id) => document.getElementById(id);
const STORAGE_KEY = "gpages-bubblewrap-profile-v1";
let profile = makePreset("empty");
let compiled = "";

try {
  const saved = localStorage.getItem(STORAGE_KEY);
  if (saved) {
    const candidate = JSON.parse(saved);
    if (!validateProfile(candidate).errors.length) profile = validateProfile(candidate).profile;
  }
} catch {
  // Private browsing or blocked storage should not break the generator.
}

function notify(message) {
  $("message").textContent = message;
}
function store() {
  try { localStorage.setItem(STORAGE_KEY, JSON.stringify(profile)); } catch {}
}
function labelField(title, value, type = "text", klass = "") {
  const label = document.createElement("label");
  label.className = "field " + klass;
  const heading = document.createElement("span");
  heading.textContent = title;
  label.append(heading);
  const input = document.createElement(type === "select" ? "select" : "input");
  if (type !== "select") {
    input.type = type;
    input.spellcheck = false;
  }
  if (value != null) input.value = value;
  label.append(input);
  return {label, input};
}
function appendOption(select, value, text) {
  const opt = document.createElement("option");
  opt.value = value;
  opt.textContent = text;
  select.append(opt);
}

function renderMounts() {
  const list = $("mountList");
  list.replaceChildren();
  if (!profile.mounts.length) {
    const empty = document.createElement("div");
    empty.className = "empty";
    empty.textContent = "No host files are exposed yet. Use '+ Add mount' to share a folder.";
    list.append(empty);
    return;
  }
  profile.mounts.forEach((mount, index) => {
    const row = document.createElement("article");
    row.className = "mount-card";

    const head = document.createElement("div");
    head.className = "mount-header";
    const title = document.createElement("span");
    title.className = "mount-number";
    title.textContent = "Mount " + (index + 1);
    const remove = document.createElement("button");
    remove.className = "danger tiny";
    remove.type = "button";
    remove.textContent = "Remove";
    remove.setAttribute("aria-label", "Remove mount " + (index + 1));
    remove.addEventListener("click", () => {
      profile.mounts.splice(index, 1);
      renderMounts();
      update();
    });
    head.append(title, remove);

    const fields = document.createElement("div");
    fields.className = "mount-fields";
    const host = labelField("Host path (real Linux folder/file)", mount.source || "");
    host.input.placeholder = "$HOME/Documents/project";
    host.input.disabled = mount.access === "mask";
    host.input.addEventListener("input", () => { mount.source = host.input.value; update(); });
    const target = labelField("Sandbox path (visible as)", mount.target || "");
    target.input.placeholder = "/workspace/project";
    target.input.addEventListener("input", () => { mount.target = target.input.value; update(); });
    fields.append(host.label, target.label);

    const options = document.createElement("div");
    options.className = "mount-options";
    const access = labelField("Permissions", null, "select");
    appendOption(access.input, "ro", "Read-only (+ contents)");
    appendOption(access.input, "rw", "Read/write (+ contents)");
    appendOption(access.input, "mask", "Hide / empty temporary directory");
    access.input.value = mount.access || "ro";
    access.input.addEventListener("change", () => {
      mount.access = access.input.value;
      renderMounts();
      update();
    });
    const optionalLabel = document.createElement("label");
    optionalLabel.className = "checkbox-row";
    const optional = document.createElement("input");
    optional.type = "checkbox";
    optional.checked = Boolean(mount.optional);
    optional.disabled = mount.access === "mask";
    optional.addEventListener("change", () => { mount.optional = optional.checked; update(); });
    const optionalText = document.createElement("span");
    optionalText.textContent = "Skip if host path is missing";
    optionalLabel.append(optional, optionalText);
    options.append(access.label, optionalLabel);

    row.append(head, fields, options);
    if (mount.access === "mask") {
      const note = document.createElement("p");
      note.className = "mask-note";
      note.textContent = "This mount has no host source. It hides a sandbox DIRECTORY under an empty, temporary filesystem.";
      row.append(note);
    }
    list.append(row);
  });
}

function renderToolGroups() {
  const root = $("toolGroups");
  root.replaceChildren();
  const selected = new Set(profile.tools || []);
  const names = {
    basic: "Basic file & shell utilities",
    development: "Compilers & build tools",
    languages: "Languages & runtimes",
    network: "Networking tools",
    gpu: "GPU utilities"
  };
  for (const [group, tools] of Object.entries(TOOL_GROUPS)) {
    const div = document.createElement("div");
    div.className = "tool-group";
    const title = document.createElement("h4");
    title.textContent = names[group];
    const choices = document.createElement("div");
    choices.className = "tool-list";
    for (const tool of tools) {
      const label = document.createElement("label");
      const box = document.createElement("input");
      box.type = "checkbox";
      box.checked = selected.has(tool);
      box.addEventListener("change", () => {
        if (box.checked) selected.add(tool); else selected.delete(tool);
        profile.tools = [...selected];
        update();
      });
      const text = document.createElement("span");
      text.textContent = tool;
      label.append(box, text);
      choices.append(label);
    }
    div.append(title, choices);
    root.append(div);
  }
}
function renderEnv() {
  const list = $("envList");
  list.replaceChildren();
  if (!profile.env?.length) {
    const empty = document.createElement("div");
    empty.className = "empty";
    empty.textContent = "No additional environment variables.";
    list.append(empty);
  }
  (profile.env || []).forEach((entry, index) => {
    const row = document.createElement("div");
    row.className = "env-row";
    const key = labelField("Name", entry.name);
    key.input.placeholder = "MY_VARIABLE";
    key.input.addEventListener("input", () => { entry.name = key.input.value; update(); });
    const value = labelField("Value", entry.value);
    value.input.placeholder = "some value";
    value.input.addEventListener("input", () => { entry.value = value.input.value; update(); });
    const remove = document.createElement("button");
    remove.className = "danger compact";
    remove.type = "button";
    remove.textContent = "Remove";
    remove.addEventListener("click", () => {
      profile.env.splice(index, 1);
      renderEnv();
      update();
    });
    row.append(key.label, value.label, remove);
    list.append(row);
  });
}
function setPanelVisibility() {
  const selected = profile.runtime === "selected";
  $("toolsPanel").hidden = !selected;
  $("runtimeHint").textContent = selected
    ? "Selected mode mounts only the named executable files into /usr/bin, alongside libraries and support files. It may not work with every program and is NOT a security-grade executable allowlist."
    : "Full mode exposes all executables in your host's /usr, /bin and related system folders, read-only. This is the recommended mode for toolchains, Java, Python, Git, Minecraft and CUDA.";
}
function renderControls() {
  for (const [id, key] of [
    ["profileName", "name"], ["shell", "shell"], ["workdir", "workdir"],
    ["home", "home"], ["runtime", "runtime"], ["gpu", "gpu"], ["extraTools", "extraTools"]
  ]) $(id).value = profile[key] ?? "";
  for (const key of ["network", "cudaToolkit", "wayland", "pid", "ipc", "uts", "clearenv"]) {
    $(key).checked = Boolean(profile[key]);
  }
  setPanelVisibility();
  renderMounts();
  renderToolGroups();
  renderEnv();
}
function listFeedback(items, klass) {
  if (!items.length) return null;
  const wrap = document.createElement("div");
  wrap.className = klass;
  const title = document.createElement("strong");
  title.textContent = klass === "errors" ? "Fix before downloading:" : "Important notes:";
  const ul = document.createElement("ul");
  for (const item of items) {
    const li = document.createElement("li");
    li.textContent = item;
    ul.append(li);
  }
  wrap.append(title, ul);
  return wrap;
}
function update() {
  const {profile: checked, errors, warnings} = validateProfile(profile);
  const feedback = $("feedback");
  feedback.replaceChildren();
  const err = listFeedback(errors, "errors");
  const warn = listFeedback(warnings, "warnings");
  if (err) feedback.append(err);
  if (warn) feedback.append(warn);
  const badge = $("validBadge");
  badge.classList.toggle("invalid", errors.length > 0);
  badge.textContent = errors.length ? "Needs fixes" : "Ready";
  $("summary").textContent = [
    profile.mounts.length + " mounts",
    profile.network ? "host network" : "network isolated",
    (profile.gpu === "nvidia" ? "NVIDIA + DRI" : profile.gpu === "dri" ? "DRI only" : "no GPU"),
    profile.runtime === "full" ? "full system tools" : new Set([...checked.tools, "bash", "sh", "env"]).size + " selected tools incl. shell essentials"
  ].join(" · ");
  if (errors.length) {
    compiled = "";
    $("scriptOutput").value = "# Fix the settings above to generate a launcher.";
  } else {
    try {
      compiled = buildLauncher(profile).script;
      $("scriptOutput").value = compiled;
    } catch (error) {
      compiled = "";
      $("scriptOutput").value = "# " + error.message;
    }
  }
  $("downloadScript").disabled = !compiled;
  $("copyScript").disabled = !compiled;
  store();
}
function download(filename, contents, mime) {
  const blob = new Blob([contents], {type: mime});
  const url = URL.createObjectURL(blob);
  const link = document.createElement("a");
  link.href = url;
  link.download = filename;
  document.body.append(link);
  link.click();
  link.remove();
  setTimeout(() => URL.revokeObjectURL(url), 1500);
}
for (const [id, key] of [
  ["profileName", "name"], ["shell", "shell"], ["workdir", "workdir"],
  ["home", "home"], ["runtime", "runtime"], ["gpu", "gpu"], ["extraTools", "extraTools"],
  ["network", "network"], ["cudaToolkit", "cudaToolkit"], ["wayland", "wayland"],
  ["pid", "pid"], ["ipc", "ipc"], ["uts", "uts"], ["clearenv", "clearenv"]
]) {
  const el = $(id);
  el.addEventListener(el.type === "checkbox" || el.tagName === "SELECT" ? "change" : "input", () => {
    profile[key] = el.type === "checkbox" ? el.checked : el.value;
    if (key === "runtime") setPanelVisibility();
    update();
  });
}
$("addMount").addEventListener("click", () => {
  let n = profile.mounts.length + 1;
  while (profile.mounts.some((m) => m.target === "/data" + n)) n += 1;
  profile.mounts.push({source: "", target: "/data" + n, access: "ro", optional: false});
  renderMounts();
  update();
  $("mountList").lastElementChild?.querySelector("input")?.focus();
});
$("addEnv").addEventListener("click", () => {
  profile.env = profile.env || [];
  profile.env.push({name: "", value: ""});
  renderEnv();
  update();
});
$("clearTools").addEventListener("click", () => {
  profile.tools = [];
  $("extraTools").value = "";
  profile.extraTools = "";
  renderToolGroups();
  update();
});
$("applyPreset").addEventListener("click", () => {
  if (!confirm("Replace your current settings with this preset?")) return;
  profile = makePreset($("preset").value);
  renderControls();
  update();
  notify("Preset applied. Check the host paths before downloading.");
});
$("resetProfile").addEventListener("click", () => {
  if (!confirm("Reset the entire generator to an empty sandbox?")) return;
  profile = makePreset("empty");
  $("preset").value = "empty";
  renderControls();
  update();
  notify("Reset complete.");
});
$("downloadScript").addEventListener("click", () => {
  if (!compiled) return;
  download("start-isolated.sh", compiled, "text/x-shellscript;charset=utf-8");
  notify("Launcher downloaded. Remember to run chmod +x start-isolated.sh.");
});
$("copyScript").addEventListener("click", async () => {
  if (!compiled) return;
  try {
    await navigator.clipboard.writeText(compiled);
    notify("Launcher copied to clipboard.");
  } catch {
    $("scriptOutput").focus();
    $("scriptOutput").select();
    notify("Clipboard access was blocked. The launcher text is selected for manual copy.");
  }
});
$("exportProfile").addEventListener("click", () => {
  if (validateProfile(profile).errors.length) {
    notify("Fix invalid settings before exporting.");
    return;
  }
  download("bubblewrap-profile.json", JSON.stringify(profile, null, 2) + "\n", "application/json");
  notify("JSON profile exported.");
});
$("importProfile").addEventListener("click", () => $("profileFile").click());
$("profileFile").addEventListener("change", async (event) => {
  const file = event.target.files?.[0];
  event.target.value = "";
  if (!file) return;
  if (file.size > 131072) { notify("Profile is too large (128 KiB maximum)."); return; }
  try {
    const candidate = JSON.parse(await file.text());
    if (!candidate || typeof candidate !== "object" || Array.isArray(candidate)) throw new Error("Expected a JSON object.");
    const {errors} = validateProfile(candidate);
    if (errors.length) throw new Error(errors.join(" "));
    profile = validateProfile(candidate).profile;
    renderControls();
    update();
    notify("JSON profile loaded.");
  } catch (error) {
    notify("Could not import profile: " + error.message);
  }
});

renderControls();
update();
