(() => {
  "use strict";

  const API_BASE = "https://openrouter.ai/api/v1";
  const DB_NAME = "openrouter-chat";
  const DB_VERSION = 1;
  const CHAT_STORE = "chats";
  const LOCAL_KEY = "openrouter-chat.apiKey";
  const SESSION_KEY = "openrouter-chat.sessionApiKey";
  const FAVORITES_KEY = "openrouter-chat.favoriteModels";
  const PRESETS_KEY = "openrouter-chat.promptPresets";
  const LAST_MODEL_KEY = "openrouter-chat.lastModel";

  const state = {
    apiKey: "",
    rememberKey: false,
    db: null,
    chats: [],
    activeChat: null,
    models: [],
    selectedModel: null,
    favorites: new Set(),
    presets: [],
    abortController: null,
    streaming: false,
    lastRequest: null,
    saveTimer: null,
  };

  const el = {};

  document.addEventListener("DOMContentLoaded", init);

  async function init() {
    cacheElements();
    bindEvents();
    loadLocalPreferences();

    try {
      state.db = await openDatabase();
      state.chats = await dbGetAllChats();
      state.chats.sort((a, b) => b.updatedAt - a.updatedAt);
    } catch (error) {
      console.error("IndexedDB unavailable", error);
    }

    if (state.chats.length) {
      state.activeChat = state.chats[0];
    } else {
      state.activeChat = makeNewChat();
      state.chats = [state.activeChat];
      await saveActiveChatNow();
    }

    renderAll();

    const persistentKey = localStorage.getItem(LOCAL_KEY) || "";
    const sessionKey = sessionStorage.getItem(SESSION_KEY) || "";
    state.apiKey = persistentKey || sessionKey;
    state.rememberKey = Boolean(persistentKey);
    el.apiKeyInput.value = state.apiKey;
    el.rememberKeyInput.checked = state.rememberKey;

    if (state.apiKey) {
      await connectWithCurrentKey(false);
    } else {
      setConnectionStatus("Not connected", false);
      queueMicrotask(() => el.keyDialog.showModal());
    }
  }

  function cacheElements() {
    const ids = [
      "sidebar", "newChatButton", "exportButton", "importButton", "importFile", "chatList",
      "sidebarToggle", "modelSearch", "modelMenu", "modelMeta", "favoriteModelButton", "keyButton",
      "systemPrompt", "promptPreset", "savePresetButton", "deletePresetButton", "temperatureField",
      "temperatureInput", "topPField", "topPInput", "maxTokensField", "maxTokensInput",
      "reasoningField", "reasoningInput", "messages", "requestPreview", "contextStatus", "costStatus",
      "connectionStatus", "composerInput", "stopButton", "sendButton", "keyDialog", "keyForm",
      "apiKeyInput", "rememberKeyInput", "keyError", "forgetKeyButton", "connectButton"
    ];
    for (const id of ids) el[id] = document.getElementById(id);
  }

  function bindEvents() {
    el.newChatButton.addEventListener("click", createNewChat);
    el.sidebarToggle.addEventListener("click", () => el.sidebar.classList.toggle("open"));
    el.exportButton.addEventListener("click", exportChats);
    el.importButton.addEventListener("click", () => el.importFile.click());
    el.importFile.addEventListener("change", importChats);

    el.keyButton.addEventListener("click", openKeyDialog);
    el.connectButton.addEventListener("click", () => connectWithCurrentKey(true));
    el.forgetKeyButton.addEventListener("click", forgetKey);
    el.keyForm.addEventListener("submit", (event) => {
      if (event.submitter?.value !== "cancel") event.preventDefault();
    });

    el.modelSearch.addEventListener("focus", () => renderModelMenu(el.modelSearch.value));
    el.modelSearch.addEventListener("input", () => renderModelMenu(el.modelSearch.value));
    el.modelSearch.addEventListener("keydown", handleModelSearchKeydown);
    el.favoriteModelButton.addEventListener("click", toggleFavoriteModel);
    document.addEventListener("click", (event) => {
      if (!event.target.closest("#modelCombo")) el.modelMenu.hidden = true;
    });

    el.systemPrompt.addEventListener("input", () => {
      if (!state.activeChat) return;
      state.activeChat.systemPrompt = el.systemPrompt.value;
      touchActiveChat();
    });
    el.temperatureInput.addEventListener("input", updateParamsFromUi);
    el.topPInput.addEventListener("input", updateParamsFromUi);
    el.maxTokensInput.addEventListener("input", updateParamsFromUi);
    el.reasoningInput.addEventListener("change", updateParamsFromUi);

    el.promptPreset.addEventListener("change", loadSelectedPreset);
    el.savePresetButton.addEventListener("click", savePromptPreset);
    el.deletePresetButton.addEventListener("click", deletePromptPreset);

    el.sendButton.addEventListener("click", sendMessage);
    el.stopButton.addEventListener("click", stopStreaming);
    el.composerInput.addEventListener("keydown", (event) => {
      if (event.key === "Enter" && !event.shiftKey && !event.isComposing) {
        event.preventDefault();
        sendMessage();
      }
    });
  }

  function loadLocalPreferences() {
    state.favorites = new Set(safeJsonParse(localStorage.getItem(FAVORITES_KEY), []));
    state.presets = safeJsonParse(localStorage.getItem(PRESETS_KEY), []);
    if (!Array.isArray(state.presets)) state.presets = [];
    renderPresets();
  }

  function makeNewChat() {
    const now = Date.now();
    return {
      id: crypto.randomUUID(),
      title: "New chat",
      createdAt: now,
      updatedAt: now,
      systemPrompt: "",
      modelId: localStorage.getItem(LAST_MODEL_KEY) || "",
      params: { temperature: "", topP: "", maxTokens: "", reasoning: "" },
      messages: [],
      totalCost: 0,
    };
  }

  async function createNewChat() {
    if (state.streaming) return;
    state.activeChat = makeNewChat();
    state.chats.unshift(state.activeChat);
    if (state.selectedModel) state.activeChat.modelId = state.selectedModel.id;
    await saveActiveChatNow();
    renderAll();
    el.composerInput.focus();
    el.sidebar.classList.remove("open");
  }

  function renderAll() {
    renderChatList();
    renderActiveChatControls();
    renderMessages();
    selectModelForActiveChat();
    updateStatus();
    updateRequestPreview();
  }

  function renderChatList() {
    el.chatList.replaceChildren();
    const sorted = [...state.chats].sort((a, b) => b.updatedAt - a.updatedAt);
    for (const chat of sorted) {
      const row = document.createElement("div");
      row.className = "chat-row";

      const choose = document.createElement("button");
      choose.type = "button";
      choose.className = "chat-select" + (state.activeChat?.id === chat.id ? " active" : "");
      choose.addEventListener("click", () => switchChat(chat.id));

      const title = document.createElement("span");
      title.className = "chat-title";
      title.textContent = chat.title || "New chat";
      const date = document.createElement("span");
      date.className = "chat-date";
      date.textContent = formatChatDate(chat.updatedAt);
      choose.append(title, date);

      const remove = document.createElement("button");
      remove.type = "button";
      remove.className = "chat-delete";
      remove.textContent = "×";
      remove.title = "Delete chat";
      remove.setAttribute("aria-label", `Delete ${chat.title || "chat"}`);
      remove.addEventListener("click", () => deleteChat(chat.id));

      row.append(choose, remove);
      el.chatList.append(row);
    }
  }

  async function switchChat(id) {
    if (state.streaming) return;
    const chat = state.chats.find((item) => item.id === id);
    if (!chat) return;
    state.activeChat = chat;
    renderAll();
    el.sidebar.classList.remove("open");
  }

  async function deleteChat(id) {
    if (state.streaming || !confirm("Delete this local chat?")) return;
    state.chats = state.chats.filter((chat) => chat.id !== id);
    if (state.db) await dbDeleteChat(id);
    if (!state.chats.length) {
      state.activeChat = makeNewChat();
      state.chats = [state.activeChat];
      await saveActiveChatNow();
    } else if (state.activeChat?.id === id) {
      state.activeChat = [...state.chats].sort((a, b) => b.updatedAt - a.updatedAt)[0];
    }
    renderAll();
  }

  function renderActiveChatControls() {
    const chat = state.activeChat;
    if (!chat) return;
    chat.params ||= { temperature: "", topP: "", maxTokens: "", reasoning: "" };
    el.systemPrompt.value = chat.systemPrompt || "";
    el.temperatureInput.value = chat.params.temperature ?? "";
    el.topPInput.value = chat.params.topP ?? "";
    el.maxTokensInput.value = chat.params.maxTokens ?? "";
    el.reasoningInput.value = chat.params.reasoning ?? "";
  }

  function renderMessages() {
    el.messages.replaceChildren();
    const messages = state.activeChat?.messages || [];
    if (!messages.length) {
      const empty = document.createElement("div");
      empty.className = "empty-state";
      const box = document.createElement("div");
      const strong = document.createElement("strong");
      strong.textContent = "A very small OpenRouter client.";
      const line = document.createElement("span");
      line.textContent = "Pick a model, set a system prompt if you want one, and chat.";
      box.append(strong, line);
      empty.append(box);
      el.messages.append(empty);
      return;
    }

    for (const message of messages) {
      el.messages.append(renderMessage(message));
    }
    scrollMessagesToBottom();
  }

  function renderMessage(message) {
    const article = document.createElement("article");
    article.className = `message ${message.role}`;
    article.dataset.messageId = message.id;

    const role = document.createElement("div");
    role.className = "message-role";
    role.textContent = message.role === "assistant" ? "Assistant" : "You";

    const content = document.createElement("div");
    content.className = "message-content";
    if (message.streaming) content.classList.add("cursor");
    if (message.error) content.classList.add("message-error");
    renderSafeText(content, message.content || (message.streaming ? "" : "[empty response]"));

    article.append(role, content);

    if (message.role === "assistant" && (message.usage || message.model)) {
      const footer = document.createElement("div");
      footer.className = "message-footer";
      const bits = [];
      if (message.model) bits.push(message.model);
      if (message.usage?.prompt_tokens != null) bits.push(`${formatNumber(message.usage.prompt_tokens)} in`);
      if (message.usage?.completion_tokens != null) bits.push(`${formatNumber(message.usage.completion_tokens)} out`);
      if (message.usage?.completion_tokens_details?.reasoning_tokens != null) {
        bits.push(`${formatNumber(message.usage.completion_tokens_details.reasoning_tokens)} reasoning`);
      }
      if (Number.isFinite(message.usage?.cost)) bits.push(formatCost(message.usage.cost));
      footer.textContent = bits.join(" · ");
      article.append(footer);
    }
    return article;
  }

  function renderSafeText(container, text) {
    container.replaceChildren();
    const parts = String(text).split(/```/);
    parts.forEach((part, index) => {
      if (index % 2 === 1) {
        const pre = document.createElement("pre");
        const firstNewline = part.indexOf("\n");
        const body = firstNewline >= 0 ? part.slice(firstNewline + 1) : part;
        pre.textContent = body.replace(/\n$/, "");
        container.append(pre);
      } else {
        appendInlineCode(container, part);
      }
    });
  }

  function appendInlineCode(container, text) {
    const bits = String(text).split(/(`[^`\n]+`)/g);
    for (const bit of bits) {
      if (/^`[^`\n]+`$/.test(bit)) {
        const code = document.createElement("code");
        code.className = "inline-code";
        code.textContent = bit.slice(1, -1);
        container.append(code);
      } else if (bit) {
        container.append(document.createTextNode(bit));
      }
    }
  }

  async function connectWithCurrentKey(closeOnSuccess) {
    const candidate = el.apiKeyInput.value.trim() || state.apiKey;
    if (!candidate) {
      el.keyError.textContent = "Paste an OpenRouter API key first.";
      return;
    }

    el.connectButton.disabled = true;
    el.keyError.textContent = "";
    setConnectionStatus("Connecting…", null);

    try {
      const models = await fetchModels(candidate);
      state.apiKey = candidate;
      state.rememberKey = el.rememberKeyInput.checked;
      if (state.rememberKey) {
        localStorage.setItem(LOCAL_KEY, candidate);
        sessionStorage.removeItem(SESSION_KEY);
      } else {
        sessionStorage.setItem(SESSION_KEY, candidate);
        localStorage.removeItem(LOCAL_KEY);
      }
      state.models = models;
      setConnectionStatus(`Connected · ${models.length} models`, true);
      selectModelForActiveChat();
      updateParameterAvailability();
      updateRequestPreview();
      if (closeOnSuccess && el.keyDialog.open) el.keyDialog.close();
    } catch (error) {
      console.error(error);
      state.models = [];
      state.selectedModel = null;
      setConnectionStatus("Connection failed", false);
      el.keyError.textContent = error.message || "Could not connect to OpenRouter.";
    } finally {
      el.connectButton.disabled = false;
    }
  }

  async function fetchModels(apiKey) {
    const url = `${API_BASE}/models?input_modalities=text&output_modalities=text&limit=1000`;
    const response = await fetch(url, {
      headers: { Authorization: `Bearer ${apiKey}` },
    });
    const data = await readJsonResponse(response);
    if (!response.ok) throw new Error(extractApiError(data, response.status));
    const models = Array.isArray(data?.data) ? data.data : [];
    return models.filter((model) => {
      const inputs = model.architecture?.input_modalities || [];
      const outputs = model.architecture?.output_modalities || [];
      return inputs.includes("text") && outputs.includes("text");
    });
  }

  function openKeyDialog() {
    el.apiKeyInput.value = state.apiKey;
    el.rememberKeyInput.checked = state.rememberKey;
    el.keyError.textContent = "";
    el.keyDialog.showModal();
    el.apiKeyInput.focus();
  }

  function forgetKey() {
    stopStreaming();
    localStorage.removeItem(LOCAL_KEY);
    sessionStorage.removeItem(SESSION_KEY);
    state.apiKey = "";
    state.models = [];
    state.selectedModel = null;
    state.rememberKey = false;
    el.apiKeyInput.value = "";
    el.rememberKeyInput.checked = false;
    el.modelSearch.value = "";
    el.modelMeta.textContent = "No model loaded";
    el.modelMenu.hidden = true;
    updateFavoriteButton();
    updateParameterAvailability();
    setConnectionStatus("Not connected", false);
    updateRequestPreview();
  }

  function selectModelForActiveChat() {
    if (!state.models.length || !state.activeChat) {
      updateParameterAvailability();
      return;
    }
    const requested = state.activeChat.modelId || localStorage.getItem(LAST_MODEL_KEY) || "";
    state.selectedModel = state.models.find((model) => model.id === requested) || state.models[0];
    state.activeChat.modelId = state.selectedModel.id;
    localStorage.setItem(LAST_MODEL_KEY, state.selectedModel.id);
    el.modelSearch.value = state.selectedModel.name || state.selectedModel.id;
    updateModelMeta();
    updateFavoriteButton();
    updateParameterAvailability();
    updateStatus();
    updateRequestPreview();
  }

  function renderModelMenu(query = "") {
    if (!state.models.length) {
      el.modelMenu.hidden = true;
      return;
    }
    const q = query.trim().toLowerCase();
    const ranked = [...state.models]
      .filter((model) => !q || model.name?.toLowerCase().includes(q) || model.id.toLowerCase().includes(q))
      .sort((a, b) => {
        const af = state.favorites.has(a.id) ? 1 : 0;
        const bf = state.favorites.has(b.id) ? 1 : 0;
        if (af !== bf) return bf - af;
        return (a.name || a.id).localeCompare(b.name || b.id);
      })
      .slice(0, 120);

    el.modelMenu.replaceChildren();
    for (const model of ranked) {
      const button = document.createElement("button");
      button.type = "button";
      button.className = "model-option";
      button.setAttribute("role", "option");
      button.setAttribute("aria-selected", String(state.selectedModel?.id === model.id));
      button.addEventListener("click", () => chooseModel(model.id));

      const name = document.createElement("span");
      name.className = "model-option-name";
      name.textContent = `${state.favorites.has(model.id) ? "★ " : ""}${model.name || model.id}`;
      const id = document.createElement("span");
      id.className = "model-option-id";
      id.textContent = model.id;
      const meta = document.createElement("span");
      meta.className = "model-option-meta";
      meta.textContent = describeModel(model);
      button.append(name, id, meta);
      el.modelMenu.append(button);
    }

    if (!ranked.length) {
      const empty = document.createElement("div");
      empty.className = "model-option-meta";
      empty.classList.add("model-menu-empty");
      empty.textContent = "No matching models.";
      el.modelMenu.append(empty);
    }
    el.modelMenu.hidden = false;
  }

  function chooseModel(id) {
    const model = state.models.find((item) => item.id === id);
    if (!model || !state.activeChat) return;
    state.selectedModel = model;
    state.activeChat.modelId = model.id;
    localStorage.setItem(LAST_MODEL_KEY, model.id);
    el.modelSearch.value = model.name || model.id;
    el.modelMenu.hidden = true;
    updateModelMeta();
    updateFavoriteButton();
    updateParameterAvailability();
    updateStatus();
    updateRequestPreview();
    touchActiveChat();
  }

  function handleModelSearchKeydown(event) {
    if (event.key === "Escape") {
      el.modelMenu.hidden = true;
      if (state.selectedModel) el.modelSearch.value = state.selectedModel.name || state.selectedModel.id;
      return;
    }
    if (event.key === "Enter" && !el.modelMenu.hidden) {
      const first = el.modelMenu.querySelector(".model-option");
      if (first) {
        event.preventDefault();
        first.click();
      }
    }
  }

  function toggleFavoriteModel() {
    if (!state.selectedModel) return;
    const id = state.selectedModel.id;
    if (state.favorites.has(id)) state.favorites.delete(id);
    else state.favorites.add(id);
    localStorage.setItem(FAVORITES_KEY, JSON.stringify([...state.favorites]));
    updateFavoriteButton();
  }

  function updateFavoriteButton() {
    const favorite = Boolean(state.selectedModel && state.favorites.has(state.selectedModel.id));
    el.favoriteModelButton.textContent = favorite ? "★" : "☆";
    el.favoriteModelButton.classList.toggle("is-favorite", favorite);
    el.favoriteModelButton.disabled = !state.selectedModel;
  }

  function updateModelMeta() {
    if (!state.selectedModel) {
      el.modelMeta.textContent = "No model loaded";
      return;
    }
    el.modelMeta.textContent = `${state.selectedModel.id} · ${describeModel(state.selectedModel)}`;
  }

  function describeModel(model) {
    const context = model.context_length ? `${formatCompact(model.context_length)} ctx` : "context ?";
    const prompt = pricePerMillion(model.pricing?.prompt);
    const completion = pricePerMillion(model.pricing?.completion);
    return `${context} · ${prompt}/M in · ${completion}/M out`;
  }

  function updateParamsFromUi() {
    if (!state.activeChat) return;
    state.activeChat.params = {
      temperature: el.temperatureInput.value,
      topP: el.topPInput.value,
      maxTokens: el.maxTokensInput.value,
      reasoning: el.reasoningInput.value,
    };
    touchActiveChat();
  }

  function updateParameterAvailability() {
    const supported = new Set(state.selectedModel?.supported_parameters || []);
    setParameterSupport(el.temperatureField, el.temperatureInput, supported.has("temperature"));
    setParameterSupport(el.topPField, el.topPInput, supported.has("top_p"));
    setParameterSupport(el.maxTokensField, el.maxTokensInput, supported.has("max_tokens") || supported.has("max_completion_tokens"));
    setParameterSupport(el.reasoningField, el.reasoningInput, supported.has("reasoning") || supported.has("reasoning_effort"));
  }

  function setParameterSupport(field, input, supported) {
    field.classList.toggle("unsupported", !supported);
    input.disabled = !supported;
  }

  function renderPresets() {
    const selected = el.promptPreset?.value || "";
    if (!el.promptPreset) return;
    el.promptPreset.replaceChildren();
    const initial = document.createElement("option");
    initial.value = "";
    initial.textContent = "Presets…";
    el.promptPreset.append(initial);
    for (const preset of state.presets.sort((a, b) => a.name.localeCompare(b.name))) {
      const option = document.createElement("option");
      option.value = preset.id;
      option.textContent = preset.name;
      el.promptPreset.append(option);
    }
    if (state.presets.some((preset) => preset.id === selected)) el.promptPreset.value = selected;
  }

  function loadSelectedPreset() {
    const preset = state.presets.find((item) => item.id === el.promptPreset.value);
    if (!preset || !state.activeChat) return;
    el.systemPrompt.value = preset.prompt;
    state.activeChat.systemPrompt = preset.prompt;
    touchActiveChat();
  }

  function savePromptPreset() {
    const prompt = el.systemPrompt.value;
    if (!prompt.trim()) {
      alert("There is no system prompt to save.");
      return;
    }
    const name = promptForName("Preset name:");
    if (!name) return;
    const existing = state.presets.find((preset) => preset.name.toLowerCase() === name.toLowerCase());
    if (existing) {
      existing.prompt = prompt;
      el.promptPreset.value = existing.id;
    } else {
      const preset = { id: crypto.randomUUID(), name, prompt };
      state.presets.push(preset);
      el.promptPreset.value = preset.id;
    }
    localStorage.setItem(PRESETS_KEY, JSON.stringify(state.presets));
    renderPresets();
  }

  function deletePromptPreset() {
    const id = el.promptPreset.value;
    if (!id) return;
    const preset = state.presets.find((item) => item.id === id);
    if (!preset || !confirm(`Delete preset “${preset.name}”?`)) return;
    state.presets = state.presets.filter((item) => item.id !== id);
    localStorage.setItem(PRESETS_KEY, JSON.stringify(state.presets));
    el.promptPreset.value = "";
    renderPresets();
  }

  async function sendMessage() {
    if (state.streaming) return;
    const text = el.composerInput.value.trim();
    if (!text) return;
    if (!state.apiKey) {
      openKeyDialog();
      return;
    }
    if (!state.selectedModel) {
      alert("Choose a model first.");
      return;
    }

    const chat = state.activeChat;
    const userMessage = { id: crypto.randomUUID(), role: "user", content: text, createdAt: Date.now() };
    const assistantMessage = {
      id: crypto.randomUUID(), role: "assistant", content: "", createdAt: Date.now(), streaming: true,
      model: state.selectedModel.id, usage: null
    };
    chat.messages.push(userMessage, assistantMessage);
    if (chat.title === "New chat") chat.title = makeTitle(text);
    el.composerInput.value = "";
    chat.updatedAt = Date.now();
    await saveActiveChatNow();
    renderChatList();
    renderMessages();

    const request = buildRequest(chat, assistantMessage.id);
    state.lastRequest = request;
    updateRequestPreview();

    state.abortController = new AbortController();
    state.streaming = true;
    updateStreamingUi();

    try {
      const response = await fetch(`${API_BASE}/chat/completions`, {
        method: "POST",
        headers: {
          Authorization: `Bearer ${state.apiKey}`,
          "Content-Type": "application/json",
          "HTTP-Referer": location.origin + location.pathname,
          "X-Title": "gpages OpenRouter Chat",
        },
        body: JSON.stringify(request),
        signal: state.abortController.signal,
      });

      if (!response.ok) {
        const data = await readJsonResponse(response);
        throw new Error(extractApiError(data, response.status));
      }
      if (!response.body) throw new Error("OpenRouter returned no response stream.");

      await consumeSse(response.body, (chunk) => applyStreamChunk(assistantMessage, chunk));
      assistantMessage.streaming = false;
      if (!assistantMessage.content) assistantMessage.content = "[empty response]";
    } catch (error) {
      assistantMessage.streaming = false;
      if (error.name === "AbortError") {
        if (!assistantMessage.content) assistantMessage.content = "[stopped]";
      } else {
        console.error(error);
        assistantMessage.error = true;
        assistantMessage.content = assistantMessage.content
          ? `${assistantMessage.content}\n\n[Error: ${error.message}]`
          : `Error: ${error.message}`;
      }
    } finally {
      state.streaming = false;
      state.abortController = null;
      chat.updatedAt = Date.now();
      await saveActiveChatNow();
      renderChatList();
      renderMessages();
      updateStreamingUi();
      updateStatus();
      updateRequestPreview();
      el.composerInput.focus();
    }
  }

  function buildRequest(chat, excludeMessageId = null) {
    const messages = [];
    if (chat.systemPrompt?.trim()) messages.push({ role: "system", content: chat.systemPrompt });
    for (const message of chat.messages || []) {
      if (message.id === excludeMessageId) continue;
      if (message.role !== "user" && message.role !== "assistant") continue;
      if (!message.content) continue;
      messages.push({ role: message.role, content: message.content });
    }

    const request = {
      model: state.selectedModel?.id || chat.modelId || "",
      messages,
      stream: true,
      usage: { include: true },
      session_id: chat.id,
    };

    const supported = new Set(state.selectedModel?.supported_parameters || []);
    const params = chat.params || {};
    if (supported.has("temperature") && params.temperature !== "") request.temperature = Number(params.temperature);
    if (supported.has("top_p") && params.topP !== "") request.top_p = Number(params.topP);
    if (params.maxTokens !== "") {
      if (supported.has("max_completion_tokens")) request.max_completion_tokens = Number(params.maxTokens);
      else if (supported.has("max_tokens")) request.max_tokens = Number(params.maxTokens);
    }
    if (params.reasoning) {
      if (supported.has("reasoning")) request.reasoning = { effort: params.reasoning };
      else if (supported.has("reasoning_effort")) request.reasoning_effort = params.reasoning;
    }
    return request;
  }

  async function consumeSse(stream, onChunk) {
    const reader = stream.getReader();
    const decoder = new TextDecoder();
    let buffer = "";

    while (true) {
      const { value, done } = await reader.read();
      if (done) break;
      buffer += decoder.decode(value, { stream: true });
      const events = buffer.split(/\r?\n\r?\n/);
      buffer = events.pop() || "";
      for (const event of events) parseSseEvent(event, onChunk);
    }
    buffer += decoder.decode();
    if (buffer.trim()) parseSseEvent(buffer, onChunk);
  }

  function parseSseEvent(event, onChunk) {
    for (const line of event.split(/\r?\n/)) {
      if (!line.startsWith("data:")) continue;
      const payload = line.slice(5).trim();
      if (!payload || payload === "[DONE]") continue;
      try {
        onChunk(JSON.parse(payload));
      } catch (error) {
        console.warn("Could not parse stream chunk", payload, error);
      }
    }
  }

  function applyStreamChunk(message, chunk) {
    if (chunk?.error) throw new Error(chunk.error.message || "OpenRouter stream error.");
    const delta = chunk?.choices?.[0]?.delta;
    const content = extractDeltaText(delta?.content);
    if (content) message.content += content;
    if (chunk?.model) message.model = chunk.model;
    if (chunk?.usage) {
      message.usage = chunk.usage;
      const cost = Number(chunk.usage.cost);
      if (Number.isFinite(cost)) {
        const previous = Number(message.accountedCost || 0);
        const difference = cost - previous;
        if (difference > 0) state.activeChat.totalCost = Number(state.activeChat.totalCost || 0) + difference;
        message.accountedCost = cost;
      }
    }

    const node = el.messages.querySelector(`[data-message-id="${message.id}"] .message-content`);
    if (node) {
      renderSafeText(node, message.content);
      node.classList.add("cursor");
    } else {
      renderMessages();
    }
    updateStatus();
    scrollMessagesToBottom();
  }

  function extractDeltaText(content) {
    if (typeof content === "string") return content;
    if (!Array.isArray(content)) return "";
    return content.map((part) => {
      if (typeof part === "string") return part;
      if (part?.type === "text" && typeof part.text === "string") return part.text;
      return "";
    }).join("");
  }

  function stopStreaming() {
    state.abortController?.abort();
  }

  function updateStreamingUi() {
    el.sendButton.disabled = state.streaming;
    el.stopButton.hidden = !state.streaming;
    el.modelSearch.disabled = state.streaming;
    el.favoriteModelButton.disabled = state.streaming || !state.selectedModel;
    el.newChatButton.disabled = state.streaming;
  }

  function updateStatus() {
    const chat = state.activeChat;
    const approximate = approximateTokens(chat);
    const context = state.selectedModel?.context_length;
    el.contextStatus.textContent = context
      ? `~${formatCompact(approximate)} / ${formatCompact(context)} ctx`
      : `~${formatCompact(approximate)} tokens`;
    el.costStatus.textContent = `${formatCost(Number(chat?.totalCost || 0))} this chat`;
  }

  function approximateTokens(chat) {
    if (!chat) return 0;
    let chars = (chat.systemPrompt || "").length;
    for (const message of chat.messages || []) chars += (message.content || "").length + 12;
    return Math.ceil(chars / 4);
  }

  function updateRequestPreview() {
    if (!state.activeChat) return;
    const preview = buildRequest(state.activeChat, null);
    el.requestPreview.textContent = JSON.stringify(preview, null, 2);
  }

  function touchActiveChat() {
    if (!state.activeChat) return;
    state.activeChat.updatedAt = Date.now();
    updateRequestPreview();
    updateStatus();
    clearTimeout(state.saveTimer);
    state.saveTimer = setTimeout(async () => {
      await saveActiveChatNow();
      renderChatList();
    }, 250);
  }

  async function saveActiveChatNow() {
    if (!state.activeChat) return;
    const index = state.chats.findIndex((chat) => chat.id === state.activeChat.id);
    if (index >= 0) state.chats[index] = state.activeChat;
    else state.chats.push(state.activeChat);
    if (state.db) await dbPutChat(state.activeChat);
  }

  function setConnectionStatus(text, ok) {
    el.connectionStatus.textContent = text;
    el.connectionStatus.classList.toggle("connection-ok", ok === true);
    el.connectionStatus.classList.toggle("connection-bad", ok === false);
  }

  function exportChats() {
    const payload = {
      format: "gpages-openrouter-chat",
      version: 1,
      exportedAt: new Date().toISOString(),
      chats: state.chats,
      presets: state.presets,
      favorites: [...state.favorites],
    };
    const blob = new Blob([JSON.stringify(payload, null, 2)], { type: "application/json" });
    const url = URL.createObjectURL(blob);
    const link = document.createElement("a");
    link.href = url;
    link.download = `openrouter-chat-${new Date().toISOString().slice(0, 10)}.json`;
    document.body.append(link);
    link.click();
    link.remove();
    URL.revokeObjectURL(url);
  }

  async function importChats(event) {
    const file = event.target.files?.[0];
    event.target.value = "";
    if (!file) return;
    try {
      const parsed = JSON.parse(await file.text());
      if (parsed?.format !== "gpages-openrouter-chat" || !Array.isArray(parsed.chats)) {
        throw new Error("That is not an OpenRouter Chat export.");
      }
      const existingIds = new Set(state.chats.map((chat) => chat.id));
      const incoming = parsed.chats.filter((chat) => chat && chat.id && !existingIds.has(chat.id));
      for (const chat of incoming) {
        chat.totalCost = Number(chat.totalCost || 0);
        chat.params ||= { temperature: "", topP: "", maxTokens: "", reasoning: "" };
        if (state.db) await dbPutChat(chat);
      }
      state.chats.push(...incoming);
      if (Array.isArray(parsed.presets)) {
        const presetIds = new Set(state.presets.map((preset) => preset.id));
        state.presets.push(...parsed.presets.filter((preset) => preset?.id && !presetIds.has(preset.id)));
        localStorage.setItem(PRESETS_KEY, JSON.stringify(state.presets));
      }
      if (Array.isArray(parsed.favorites)) {
        for (const id of parsed.favorites) state.favorites.add(id);
        localStorage.setItem(FAVORITES_KEY, JSON.stringify([...state.favorites]));
      }
      renderPresets();
      renderChatList();
      alert(`Imported ${incoming.length} chat${incoming.length === 1 ? "" : "s"}.`);
    } catch (error) {
      alert(error.message || "Could not import that file.");
    }
  }

  function openDatabase() {
    return new Promise((resolve, reject) => {
      const request = indexedDB.open(DB_NAME, DB_VERSION);
      request.onupgradeneeded = () => {
        const db = request.result;
        if (!db.objectStoreNames.contains(CHAT_STORE)) {
          const store = db.createObjectStore(CHAT_STORE, { keyPath: "id" });
          store.createIndex("updatedAt", "updatedAt");
        }
      };
      request.onsuccess = () => resolve(request.result);
      request.onerror = () => reject(request.error);
    });
  }

  function dbGetAllChats() {
    return dbRequest("readonly", (store) => store.getAll());
  }

  function dbPutChat(chat) {
    return dbRequest("readwrite", (store) => store.put(structuredClone(chat)));
  }

  function dbDeleteChat(id) {
    return dbRequest("readwrite", (store) => store.delete(id));
  }

  function dbRequest(mode, operation) {
    return new Promise((resolve, reject) => {
      const transaction = state.db.transaction(CHAT_STORE, mode);
      const store = transaction.objectStore(CHAT_STORE);
      const request = operation(store);
      request.onsuccess = () => resolve(request.result);
      request.onerror = () => reject(request.error);
    });
  }

  async function readJsonResponse(response) {
    const text = await response.text();
    if (!text) return {};
    try { return JSON.parse(text); }
    catch { return { raw: text }; }
  }

  function extractApiError(data, status) {
    return data?.error?.message || data?.message || data?.raw || `OpenRouter returned HTTP ${status}.`;
  }

  function makeTitle(text) {
    const oneLine = text.replace(/\s+/g, " ").trim();
    return oneLine.length > 48 ? `${oneLine.slice(0, 47)}…` : oneLine;
  }

  function formatChatDate(timestamp) {
    const date = new Date(timestamp);
    const now = new Date();
    if (date.toDateString() === now.toDateString()) {
      return date.toLocaleTimeString([], { hour: "numeric", minute: "2-digit" });
    }
    return date.toLocaleDateString([], { month: "short", day: "numeric" });
  }

  function formatCompact(value) {
    const number = Number(value || 0);
    if (number >= 1_000_000) return `${(number / 1_000_000).toFixed(number >= 10_000_000 ? 0 : 1)}M`;
    if (number >= 1_000) return `${(number / 1_000).toFixed(number >= 100_000 ? 0 : 1)}k`;
    return String(Math.round(number));
  }

  function pricePerMillion(perToken) {
    const value = Number(perToken);
    if (!Number.isFinite(value)) return "$?";
    const perMillion = value * 1_000_000;
    if (perMillion === 0) return "$0";
    if (perMillion < 0.01) return `$${perMillion.toFixed(4)}`;
    if (perMillion < 1) return `$${perMillion.toFixed(3).replace(/0+$/, "").replace(/\.$/, "")}`;
    return `$${perMillion.toFixed(perMillion < 10 ? 2 : 1).replace(/\.0$/, "")}`;
  }

  function formatCost(value) {
    const number = Number(value || 0);
    if (number === 0) return "$0.0000";
    if (number < 0.0001) return `$${number.toFixed(6)}`;
    if (number < 0.01) return `$${number.toFixed(5)}`;
    return `$${number.toFixed(4)}`;
  }

  function formatNumber(value) {
    return new Intl.NumberFormat().format(Number(value || 0));
  }

  function safeJsonParse(value, fallback) {
    if (!value) return fallback;
    try { return JSON.parse(value); }
    catch { return fallback; }
  }

  function promptForName(message) {
    const value = prompt(message);
    return value?.trim() || "";
  }

  function scrollMessagesToBottom() {
    requestAnimationFrame(() => {
      el.messages.scrollTop = el.messages.scrollHeight;
    });
  }
})();
