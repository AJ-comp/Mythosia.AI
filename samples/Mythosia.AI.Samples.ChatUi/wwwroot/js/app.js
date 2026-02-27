// ═══════════════════════════════════════════════════════════════
// Mythosia.AI Chat UI — Frontend Logic
// ═══════════════════════════════════════════════════════════════

const $ = (sel) => document.querySelector(sel);
const $$ = (sel) => document.querySelectorAll(sel);

// ── Markdown config ──────────────────────────────────────────
marked.setOptions({
  breaks: true,
  gfm: true,
  highlight: function(code, lang) {
    if (lang && hljs.getLanguage(lang)) {
      return hljs.highlight(code, { language: lang }).value;
    }
    return hljs.highlightAuto(code).value;
  }
});

function renderMarkdown(raw) {
  try { return marked.parse(raw); }
  catch(_) { return escapeHtml(raw); }
}

function addCopyButtons(container) {
  container.querySelectorAll('pre code').forEach(block => {
    if (block.parentElement.querySelector('.code-copy-btn')) return;
    const btn = document.createElement('button');
    btn.className = 'code-copy-btn';
    btn.textContent = 'Copy';
    btn.addEventListener('click', () => {
      navigator.clipboard.writeText(block.textContent).then(() => {
        btn.textContent = 'Copied!';
        setTimeout(() => btn.textContent = 'Copy', 1500);
      });
    });
    block.parentElement.style.position = 'relative';
    block.parentElement.appendChild(btn);
  });
}

// ── State ───────────────────────────────────────────────────
let selectedModel = null;   // e.g. "Gpt4oMini"
let selectedProvider = null; // e.g. "OpenAI"
let isConnected = false;
let isSending = false;
let statePollingTimer = null;
let modelReasoningInfo = null; // { type, levels } or null
let shouldAutoScroll = true;

// Auto-scroll: only when user is at bottom
function isNearBottom(threshold = 60) {
  return chatMessages.scrollHeight - chatMessages.scrollTop - chatMessages.clientHeight < threshold;
}
function autoScroll() {
  if (shouldAutoScroll) chatMessages.scrollTop = chatMessages.scrollHeight;
}

// Provider API keys { "OpenAI": "sk-...", "Google": "AI..." }
const STORAGE_KEY = 'mythosia_ai_keys';
const providerKeys = {};
let modalTargetProvider = null; // provider name currently editing in modal

function saveKeysToStorage() {
  try { localStorage.setItem(STORAGE_KEY, JSON.stringify(providerKeys)); } catch(_) {}
}

function loadKeysFromStorage() {
  try {
    const raw = localStorage.getItem(STORAGE_KEY);
    if (raw) Object.assign(providerKeys, JSON.parse(raw));
  } catch(_) {}
}

// ── DOM refs ────────────────────────────────────────────────
const modelListEl    = $('#model-list');
const settingsArea   = $('#settings-area');
const chatStatus     = $('#chat-status');
const chatMessages   = $('#chat-messages');
const chatForm       = $('#chat-form');
const chatInput      = $('#chat-input');
const btnSend        = $('#btn-send');
const btnClear       = $('#btn-clear');
const stateContainer = $('#state-container');
const btnRefresh     = $('#btn-refresh-state');

// Code Modal refs
const codeModal        = $('#code-modal');
const codeModalContent = $('#code-modal-content');
const codeModalClose   = $('#code-modal-close');
const codeCopyAll      = $('#code-copy-all');

// Modal refs
const modalOverlay     = $('#apikey-modal');
const modalTitle       = $('#modal-title');
const modalProviderName = $('#modal-provider-name');
const modalInput       = $('#modal-apikey-input');
const modalToggle      = $('#modal-apikey-toggle');
const modalKeyStatus   = $('#modal-key-status');
const modalSave        = $('#modal-save');
const modalCancel      = $('#modal-cancel');
const modalClose       = $('#modal-close');
const modalRemoveKey   = $('#modal-remove-key');

// Settings
const setSystem     = $('#set-system');
const setTemp       = $('#set-temp');
const setTopp       = $('#set-topp');
const setMaxTokens  = $('#set-maxtokens');
const setMaxMsg     = $('#set-maxmsg');
const setStateless  = $('#set-stateless');
const setReasoning  = $('#set-reasoning');
const reasoningOpts = $('#reasoning-options');
const reasoningLvls = $('#reasoning-levels');
const tempVal       = $('#temp-val');
const toppVal       = $('#topp-val');
const sidebarLeft   = $('#sidebar-left');

// ── Scroll tracking: update shouldAutoScroll based on user scroll position ──
chatMessages.addEventListener('scroll', () => {
  if (isSending) shouldAutoScroll = isNearBottom();
});

// ── Disable/enable sidebar during streaming ──
function updateSidebarDisabled(disabled) {
  sidebarLeft.classList.toggle('disabled', disabled);
}

// ═══════════════════════════════════════════════════════════════
// 1. Load models
// ═══════════════════════════════════════════════════════════════
async function loadModels() {
  try {
    const res = await fetch('/api/models');
    const groups = await res.json();
    renderModelList(groups);
  } catch (e) {
    modelListEl.innerHTML = `<p style="color:var(--danger);padding:12px;">Failed to load models</p>`;
  }
}

function renderModelList(groups) {
  modelListEl.innerHTML = '';
  groups.forEach(g => {
    const groupEl = document.createElement('div');
    groupEl.className = 'provider-group';
    groupEl.dataset.provider = g.provider;

    // Start disabled (no key)
    if (!providerKeys[g.provider]) groupEl.classList.add('disabled');

    const label = document.createElement('div');
    label.className = 'provider-label';
    label.innerHTML = `<span class="arrow">&#9654;</span><span class="provider-name">${g.provider}</span>`;

    // API Key button
    const keyBtn = document.createElement('button');
    keyBtn.className = 'provider-key-btn' + (providerKeys[g.provider] ? ' has-key' : '');
    keyBtn.textContent = providerKeys[g.provider] ? 'Key ✓' : 'API Key';
    keyBtn.addEventListener('click', (e) => {
      e.stopPropagation();
      openKeyModal(g.provider);
    });
    label.appendChild(keyBtn);
    groupEl.appendChild(label);

    const modelsEl = document.createElement('div');
    modelsEl.className = 'provider-models';

    g.models.forEach(m => {
      const btn = document.createElement('button');
      btn.className = 'model-item';
      btn.textContent = m.description;
      btn.dataset.model = m.name;
      btn.dataset.provider = g.provider;
      btn.dataset.desc = m.description;
      btn.dataset.reasoning = m.reasoning ? JSON.stringify(m.reasoning) : '';
      btn.dataset.maxOutputTokens = m.maxOutputTokens || '';
      btn.addEventListener('click', () => onModelSelect(m.name, g.provider, m.description, m.reasoning, m.maxOutputTokens));
      modelsEl.appendChild(btn);
    });

    groupEl.appendChild(modelsEl);
    modelListEl.appendChild(groupEl);

    // Toggle expand/collapse — only if provider has a key
    label.addEventListener('click', (e) => {
      if (e.target.closest('.provider-key-btn')) return;
      if (!providerKeys[g.provider]) return; // disabled — can't expand
      const arrow = label.querySelector('.arrow');
      const isOpen = modelsEl.classList.toggle('open');
      arrow.classList.toggle('open', isOpen);
    });
  });
}

// Update a single provider group's visual state
function refreshProviderGroup(provider) {
  const group = modelListEl.querySelector(`.provider-group[data-provider="${provider}"]`);
  if (!group) return;
  const hasKey = !!providerKeys[provider];
  group.classList.toggle('disabled', !hasKey);
  const keyBtn = group.querySelector('.provider-key-btn');
  if (keyBtn) {
    keyBtn.classList.toggle('has-key', hasKey);
    keyBtn.textContent = hasKey ? 'Key ✓' : 'API Key';
  }
}

// ═══════════════════════════════════════════════════════════════
// 2. Model selection
// ═══════════════════════════════════════════════════════════════
function onModelSelect(modelName, provider, desc, reasoning, maxOutputTokens) {
// Block model switch while streaming
if (isSending) return;

// If same model clicked again → deselect
if (selectedModel === modelName) {
  deselectModel();
  return;
}

// Must have key for this provider
if (!providerKeys[provider]) return;

// Deselect previous
$$('.model-item.selected').forEach(el => el.classList.remove('selected'));

// Select new
selectedModel = modelName;
selectedProvider = provider;
modelReasoningInfo = reasoning || null;
updateReasoningUI();
if (maxOutputTokens) setMaxTokens.value = maxOutputTokens;
const btn = document.querySelector(`.model-item[data-model="${modelName}"]`);
if (btn) btn.classList.add('selected');

  // Auto-connect using stored key
  chatStatus.textContent = `Connecting to ${desc}...`;
  chatStatus.classList.remove('connected');
  connectToModel(modelName, provider, desc);
}

function deselectModel() {
  selectedModel = null;
  selectedProvider = null;
  modelReasoningInfo = null;
  updateReasoningUI();
  $$('.model-item.selected').forEach(el => el.classList.remove('selected'));
  settingsArea.classList.add('hidden');
  chatStatus.textContent = 'No model selected';
  chatStatus.classList.remove('connected');
  isConnected = false;
  disableChatInput();
  stopStatePolling();
}

// ═══════════════════════════════════════════════════════════════
// 3. API Key Modal
// ═══════════════════════════════════════════════════════════════
function openKeyModal(provider) {
  modalTargetProvider = provider;
  modalTitle.textContent = `${provider} API Key`;
  modalProviderName.textContent = provider;
  modalInput.value = providerKeys[provider] || '';
  modalInput.type = 'password';
  modalKeyStatus.classList.add('hidden');
  modalKeyStatus.className = 'modal-key-status hidden';
  modalSave.disabled = !(modalInput.value.trim());
  modalRemoveKey.style.display = providerKeys[provider] ? '' : 'none';
  modalOverlay.classList.remove('hidden');
  setTimeout(() => modalInput.focus(), 50);
}

function closeKeyModal() {
  modalOverlay.classList.add('hidden');
  modalTargetProvider = null;
  modalInput.value = '';
}

modalInput.addEventListener('input', () => {
  modalSave.disabled = !modalInput.value.trim();
});

modalToggle.addEventListener('click', () => {
  modalInput.type = modalInput.type === 'password' ? 'text' : 'password';
});

modalClose.addEventListener('click', closeKeyModal);
modalCancel.addEventListener('click', closeKeyModal);
modalOverlay.addEventListener('click', (e) => {
  if (e.target === modalOverlay) closeKeyModal();
});

modalRemoveKey.addEventListener('click', () => {
  if (!modalTargetProvider) return;
  delete providerKeys[modalTargetProvider];
  saveKeysToStorage();
  refreshProviderGroup(modalTargetProvider);
  // If currently connected to this provider, disconnect
  if (selectedProvider === modalTargetProvider) {
    deselectModel();
  }
  closeKeyModal();
});

modalSave.addEventListener('click', () => {
  const key = modalInput.value.trim();
  if (!key || !modalTargetProvider) return;
  providerKeys[modalTargetProvider] = key;
  saveKeysToStorage();
  refreshProviderGroup(modalTargetProvider);

  // Show success
  modalKeyStatus.textContent = `Key saved for ${modalTargetProvider} (localStorage)`;
  modalKeyStatus.className = 'modal-key-status success';

  // Show remove button after save
  modalRemoveKey.style.display = '';
});

function maskApiKey(key) {
  if (!key || key.length <= 8) return '••••••••';
  return key.substring(0, 4) + '••••••••' + key.substring(key.length - 4);
}

// ═══════════════════════════════════════════════════════════════
// 3b. Connect to model (uses stored provider key)
// ═══════════════════════════════════════════════════════════════
async function connectToModel(modelName, provider, desc) {
  const apiKey = providerKeys[provider];
  if (!modelName || !apiKey) return;

  try {
    const res = await fetch('/api/configure', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        apiKey: apiKey,
        model: modelName,
        systemMessage: setSystem.value || null
      })
    });

    const data = await res.json();
    if (!res.ok) throw new Error(data.error);

    isConnected = true;

    // Update status
    chatStatus.textContent = `Connected: ${data.model} (${data.provider})`;
    chatStatus.classList.add('connected');

    // Show model switch event in chat
    appendModelSwitchEvent(data.model, data.provider);

    // Show settings
    settingsArea.classList.remove('hidden');

    // Enable chat
    enableChatInput();

    // Start state polling
    startStatePolling();
    refreshState();
  } catch (e) {
    chatStatus.textContent = `Error: ${e.message}`;
    chatStatus.classList.remove('connected');
    isConnected = false;
    disableChatInput();
  }
}

// ═══════════════════════════════════════════════════════════════
// 4. Chat
// ═══════════════════════════════════════════════════════════════
function appendModelSwitchEvent(model, provider) {
  const empty = chatMessages.querySelector('.empty-state');
  if (empty) empty.remove();
  const div = document.createElement('div');
  div.className = 'model-switch-event';
  div.innerHTML = `<span class="model-switch-icon">⇄</span> Switched to <strong>${escapeHtml(model)}</strong> <span class="model-switch-provider">${escapeHtml(provider)}</span>`;
  chatMessages.appendChild(div);
  chatMessages.scrollTop = chatMessages.scrollHeight;
}

function enableChatInput() {
  chatInput.disabled = false;
  btnSend.disabled = false;
  chatInput.focus();
}

function disableChatInput() {
  chatInput.disabled = true;
  btnSend.disabled = true;
}

chatForm.addEventListener('submit', (e) => {
  e.preventDefault();
  sendMessage();
});

chatInput.addEventListener('keydown', (e) => {
  if (e.key === 'Enter' && !e.shiftKey) {
    e.preventDefault();
    sendMessage();
  }
});

// Auto-resize textarea
chatInput.addEventListener('input', () => {
  chatInput.style.height = 'auto';
  chatInput.style.height = Math.min(chatInput.scrollHeight, 120) + 'px';
});

async function sendMessage() {
  const text = chatInput.value.trim();
  if (!text || isSending || !isConnected) return;

  isSending = true;
  shouldAutoScroll = true;
  btnSend.disabled = true;
  updateSidebarDisabled(true);

  // Add user bubble
  appendMessage('user', text);
  chatInput.value = '';
  chatInput.style.height = 'auto';

  // Show typing indicator
  const typingEl = showTyping();

  try {
    const res = await fetch('/api/chat', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ message: text })
    });

    removeTyping(typingEl);

    const contentType = res.headers.get('Content-Type') || '';

    // Non-stream response (error JSON)
    if (!contentType.includes('text/event-stream')) {
      const data = await res.json();
      const el = appendMessage('assistant', res.ok ? (data.response || JSON.stringify(data)) : `Error: ${data.error}`);
      if (res.ok && el) addViewCodeButton(el, text);
      refreshState();
      return;
    }

    // SSE streaming with reasoning support
    const reader = res.body.getReader();
    const decoder = new TextDecoder();
    let buffer = '';
    let fullText = '';
    let reasoningText = '';
    let thinkingEl = null;
    let thinkingContent = null;
    let msgDiv = null;
    let contentSpan = null;
    let gotText = false;

    while (true) {
      const { done, value } = await reader.read();
      if (done) break;

      buffer += decoder.decode(value, { stream: true });
      const lines = buffer.split('\n');
      buffer = lines.pop();

      for (const line of lines) {
        const trimmed = line.trim();
        if (!trimmed.startsWith('data: ')) continue;
        const payload = trimmed.substring(6);
        if (payload === '[DONE]') continue;
        try {
          const parsed = JSON.parse(payload);

          if (parsed.type === 'reasoning' && parsed.content != null) {
            reasoningText += parsed.content;
            if (!thinkingEl) {
              thinkingEl = createThinkingBubble();
              thinkingContent = thinkingEl.querySelector('.thinking-content');
            }
            thinkingContent.textContent = reasoningText;
            thinkingContent.scrollTop = thinkingContent.scrollHeight;
          }
          else if (parsed.type === 'text' && parsed.content != null) {
            if (!gotText) {
              gotText = true;
              // Collapse thinking bubble
              if (thinkingEl) collapseThinking(thinkingEl);
              // Create assistant message bubble
              msgDiv = createMessageElement('assistant');
              contentSpan = msgDiv.querySelector('.msg-content');
            }
            fullText += parsed.content;
            contentSpan.innerHTML = renderMarkdown(fullText);
            addCopyButtons(contentSpan);
          }
          else if (parsed.type === 'error') {
            fullText += '\nError: ' + (parsed.content || 'Unknown error');
            if (!msgDiv) {
              msgDiv = createMessageElement('assistant');
              contentSpan = msgDiv.querySelector('.msg-content');
            }
            contentSpan.innerHTML = renderMarkdown(fullText);
          }
        } catch (_) {}
      }

      autoScroll();
    }

    // If we only got reasoning but no text
    if (!gotText && !fullText) {
      if (thinkingEl) collapseThinking(thinkingEl);
      const fallbackEl = appendMessage('assistant', reasoningText || '(empty response)');
      if (fallbackEl) addViewCodeButton(fallbackEl, text);
    } else if (msgDiv && !fullText) {
      contentSpan.innerHTML = '<p>(empty response)</p>';
    }
    // Add View Code button to the streamed assistant message
    if (msgDiv) addViewCodeButton(msgDiv, text);
    // Mark thinking as done
    if (thinkingEl) {
      const hdr = thinkingEl.querySelector('.thinking-header');
      if (hdr) hdr.classList.add('done');
    }
    refreshState();
  } catch (e) {
    removeTyping(typingEl);
    appendMessage('assistant', `Network error: ${e.message}`);
  } finally {
    isSending = false;
    shouldAutoScroll = true;
    btnSend.disabled = false;
    updateSidebarDisabled(false);
    chatInput.focus();
  }
}

function createMessageElement(role) {
  const empty = chatMessages.querySelector('.empty-state');
  if (empty) empty.remove();

  const div = document.createElement('div');
  div.className = `msg ${role}`;
  div.innerHTML = `<span class="msg-role">${role}</span><div class="msg-content"></div>`;
  chatMessages.appendChild(div);
  autoScroll();
  return div;
}

function appendMessage(role, content) {
  const el = createMessageElement(role);
  const span = el.querySelector('.msg-content');
  if (role === 'assistant') {
    span.innerHTML = renderMarkdown(content);
    addCopyButtons(span);
  } else {
    span.textContent = content;
  }
  return el;
}

function showTyping() {
  const div = document.createElement('div');
  div.className = 'typing';
  div.innerHTML = '<span></span><span></span><span></span>';
  chatMessages.appendChild(div);
  autoScroll();
  return div;
}

function removeTyping(el) {
  if (el && el.parentNode) el.parentNode.removeChild(el);
}

function createThinkingBubble() {
  const empty = chatMessages.querySelector('.empty-state');
  if (empty) empty.remove();

  const div = document.createElement('div');
  div.className = 'msg-thinking';
  div.innerHTML = `
    <div class="thinking-header">
      <span class="thinking-icon"></span>
      <span>Thinking</span>
      <span class="thinking-arrow open">&#9654;</span>
    </div>
    <div class="thinking-content"></div>`;
  chatMessages.appendChild(div);

  // Toggle collapse on header click
  const hdr = div.querySelector('.thinking-header');
  const content = div.querySelector('.thinking-content');
  const arrow = div.querySelector('.thinking-arrow');
  hdr.addEventListener('click', () => {
    content.classList.toggle('collapsed');
    arrow.classList.toggle('open');
  });

  autoScroll();
  return div;
}

function collapseThinking(el) {
  if (!el) return;
  const content = el.querySelector('.thinking-content');
  const arrow = el.querySelector('.thinking-arrow');
  if (content) content.classList.add('collapsed');
  if (arrow) arrow.classList.remove('open');
  // Stop spinner
  const hdr = el.querySelector('.thinking-header');
  if (hdr) hdr.classList.add('done');
}

btnClear.addEventListener('click', async () => {
  if (!isConnected) return;
  try {
    await fetch('/api/clear', { method: 'POST' });
    chatMessages.innerHTML = '';
    refreshState();
  } catch (e) { /* ignore */ }
});

// ═══════════════════════════════════════════════════════════════
// 5. Settings
// ═══════════════════════════════════════════════════════════════
// ── Debounced auto-apply settings ──
let _settingsTimer = null;
function scheduleApplySettings(delay = 400) {
  clearTimeout(_settingsTimer);
  _settingsTimer = setTimeout(applySettings, delay);
}

async function applySettings() {
  if (!isConnected) return;

  const body = {
    temperature: parseFloat(setTemp.value),
    topP: parseFloat(setTopp.value),
    maxTokens: parseInt(setMaxTokens.value),
    maxMessageCount: parseInt(setMaxMsg.value),
    statelessMode: setStateless.checked,
    systemMessage: setSystem.value || '',
    reasoningEnabled: setReasoning.checked,
    reasoningLevel: null,
    reasoningType: null
  };

  if (setReasoning.checked && modelReasoningInfo) {
    const sel = reasoningLvls.querySelector('input[name="reasoning-level"]:checked');
    body.reasoningLevel = sel ? sel.value : modelReasoningInfo.levels[0];
    body.reasoningType = modelReasoningInfo.type;
  }

  try {
    await fetch('/api/settings', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body)
    });
    refreshState();
  } catch (e) { /* ignore */ }
}

setTemp.addEventListener('input', () => {
  tempVal.textContent = parseFloat(setTemp.value).toFixed(2);
  scheduleApplySettings();
});
setTopp.addEventListener('input', () => {
  toppVal.textContent = parseFloat(setTopp.value).toFixed(2);
  scheduleApplySettings();
});
setMaxTokens.addEventListener('change', () => scheduleApplySettings(200));
setMaxMsg.addEventListener('change', () => scheduleApplySettings(200));
setStateless.addEventListener('change', () => scheduleApplySettings(0));
setSystem.addEventListener('input', () => scheduleApplySettings(800));

setReasoning.addEventListener('change', () => {
  if (setReasoning.checked && modelReasoningInfo) {
    reasoningOpts.classList.remove('hidden');
  } else {
    reasoningOpts.classList.add('hidden');
  }
  scheduleApplySettings(0);
});

function updateReasoningUI() {
  if (modelReasoningInfo) {
    setReasoning.disabled = false;
    reasoningLvls.innerHTML = '';
    modelReasoningInfo.levels.forEach((lvl, i) => {
      const label = document.createElement('label');
      label.innerHTML = `<input type="radio" name="reasoning-level" value="${lvl}" ${i === 0 ? 'checked' : ''} /><span>${lvl}</span>`;
      label.querySelector('input').addEventListener('change', () => scheduleApplySettings(0));
      reasoningLvls.appendChild(label);
    });
  } else {
    setReasoning.disabled = true;
    setReasoning.checked = false;
    reasoningOpts.classList.add('hidden');
    reasoningLvls.innerHTML = '';
  }
}

// ═══════════════════════════════════════════════════════════════
// 6. Internal State (Right Panel)
// ═══════════════════════════════════════════════════════════════
function startStatePolling() {
  stopStatePolling();
  statePollingTimer = setInterval(refreshState, 5000);
}

function stopStatePolling() {
  if (statePollingTimer) {
    clearInterval(statePollingTimer);
    statePollingTimer = null;
  }
}

btnRefresh.addEventListener('click', refreshState);

async function refreshState() {
  try {
    const res = await fetch('/api/state');
    const s = await res.json();
    if (!s.configured) {
      stateContainer.innerHTML = '<div class="empty-state"><p>No service configured yet.</p></div>';
      return;
    }
    renderState(s);
  } catch (e) {
    stateContainer.innerHTML = `<div class="empty-state"><p style="color:var(--danger)">Failed to fetch state</p></div>`;
  }
}

function renderState(s) {
  let html = '';

  // ── Service Info
  html += section('Service', [
    row('Provider', s.provider),
    row('Model', s.model),
    row('Model Enum', s.modelEnum),
  ]);

  // ── Generation Settings
  html += section('Generation Settings', [
    row('Temperature', s.temperature?.toFixed(2)),
    row('Top P', s.topP?.toFixed(2)),
    row('Max Output Tokens', s.maxTokens),
    row('Max Messages', s.maxMessageCount),
    row('Freq Penalty', s.frequencyPenalty?.toFixed(2)),
    row('Pres Penalty', s.presencePenalty?.toFixed(2)),
    row('Stream', s.stream, 'bool'),
  ]);

  // ── Modes
  html += section('Modes', [
    row('Stateless', s.statelessMode, 'bool'),
    row('Functions Disabled', s.functionsDisabled, 'bool'),
  ]);

  // ── Function Settings
  html += section('Function Calling', [
    row('Enabled', s.enableFunctions, 'bool'),
    row('Mode', s.functionCallMode),
    row('Force Function', s.forceFunctionName || '(none)'),
    row('Should Use', s.shouldUseFunctions, 'bool'),
    row('Registered', s.functions?.length ?? 0),
  ]);

  // ── Registered Functions detail
  if (s.functions && s.functions.length > 0) {
    html += `<div class="state-section"><div class="state-section-title">Registered Functions</div>`;
    s.functions.forEach(f => {
      html += `<div class="state-func">
        <div class="state-func-name">${escapeHtml(f.name)}</div>
        <div class="state-func-desc">${escapeHtml(f.description || '')}</div>`;
      if (f.parameters && f.parameters.length > 0) {
        html += `<div class="state-func-params">`;
        f.parameters.forEach(p => {
          const req = p.required ? ' *' : '';
          html += `${escapeHtml(p.name)}: ${escapeHtml(p.type || 'string')}${req}<br/>`;
        });
        html += `</div>`;
      }
      html += `</div>`;
    });
    html += `</div>`;
  }

  // ── Default Policy
  html += section('Default Policy', [
    row('Max Rounds', s.defaultPolicy?.maxRounds),
    row('Timeout (s)', s.defaultPolicy?.timeoutSeconds ?? '(none)'),
    row('Max Concurrency', s.defaultPolicy?.maxConcurrency),
    row('Logging', s.defaultPolicy?.enableLogging, 'bool'),
  ]);

  // ── Summary Policy
  if (s.summaryPolicy) {
    const sp = s.summaryPolicy;
    html += section('Summary Policy', [
      row('Trigger Tokens', sp.triggerTokens ?? '(off)'),
      row('Trigger Count', sp.triggerCount ?? '(off)'),
      row('Keep Tokens', sp.keepRecentTokens ?? '(off)'),
      row('Keep Count', sp.keepRecentCount ?? '(off)'),
      row('Current Summary', sp.currentSummary ? 'Yes' : 'None'),
    ]);
    if (sp.currentSummary) {
      html += `<div class="state-section"><div class="state-section-title">Summary Content</div>
        <div class="state-msg"><div class="state-msg-content">${escapeHtml(sp.currentSummary)}</div></div></div>`;
    }
  } else {
    html += section('Summary Policy', [row('Status', '(not configured)')]);
  }

  // ── ChatBlock
  html += section('ChatBlock', [
    row('Active Chat ID', s.activeChatId?.substring(0, 8) + '...'),
    row('System Message', s.systemMessage || '(empty)'),
    row('Chat Blocks', s.chatBlockCount),
    row('Total Messages', s.messageCount),
    row('Sent to API', `${s.sentMessageCount ?? s.messageCount} / ${s.maxMessageCount} (window)`),
  ]);

  // ── Messages
  const totalMsg = s.messages?.length ?? 0;
  const windowStart = Math.max(0, totalMsg - (s.maxMessageCount ?? totalMsg));
  html += `<div class="state-section"><div class="state-section-title">Messages (${totalMsg})</div>`;
  if (s.messages && s.messages.length > 0) {
    s.messages.forEach((m, idx) => {
      // Insert separator at the window boundary
      if (idx === windowStart && windowStart > 0) {
        html += `<div class="state-window-separator">▼ sent to api ▼</div>`;
      }
      const roleClass = m.role.toLowerCase();
      const windowClass = idx < windowStart ? ' outside-window' : ' in-window';
      html += `<div class="state-msg${windowClass}">
        <div class="state-msg-header">
          <span class="state-msg-role ${roleClass}">${m.role}</span>
          <span class="state-msg-time">${m.timestamp}</span>
        </div>
        <div class="state-msg-content">${escapeHtml(truncate(m.content, 300))}</div>`;
      if (m.metadata && Object.keys(m.metadata).length > 0) {
        html += `<div class="state-msg-meta">`;
        Object.entries(m.metadata).forEach(([k, v]) => {
          html += `${escapeHtml(k)}: ${escapeHtml(truncate(String(v), 100))}<br/>`;
        });
        html += `</div>`;
      }
      html += `</div>`;
    });
  } else {
    html += `<p style="color:var(--text-muted);padding:4px 0;">No messages yet.</p>`;
  }
  html += `</div>`;

  stateContainer.innerHTML = html;
}

// ── Helpers ──────────────────────────────────────────────────
function section(title, rows) {
  return `<div class="state-section"><div class="state-section-title">${title}</div>${rows.join('')}</div>`;
}

function row(key, val, type) {
  let cls = 'state-val';
  if (type === 'bool') {
    cls += val ? ' bool-true' : ' bool-false';
    val = val ? 'true' : 'false';
  }
  return `<div class="state-row"><span class="state-key">${key}</span><span class="${cls}">${val ?? ''}</span></div>`;
}

function escapeHtml(str) {
  if (!str) return '';
  return String(str)
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;');
}

function truncate(str, max) {
  if (!str) return '';
  return str.length > max ? str.substring(0, max) + '...' : str;
}

// ═══════════════════════════════════════════════════════════════
// 7. Code Viewer Modal
// ═══════════════════════════════════════════════════════════════
function openCodeModal(userMessage) {
  codeModal.classList.remove('hidden');
  codeModalContent.textContent = 'Loading...';
  codeCopyAll.textContent = 'Copy';

  fetch('/api/code-snippet', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ userMessage })
  })
  .then(r => r.json())
  .then(data => {
    codeModalContent.textContent = data.code || 'No code available';
    hljs.highlightElement(codeModalContent);
  })
  .catch(() => {
    codeModalContent.textContent = '// Failed to load code snippet';
  });
}

function closeCodeModal() {
  codeModal.classList.add('hidden');
  codeModalContent.textContent = '';
}

codeModalClose.addEventListener('click', closeCodeModal);
codeModal.addEventListener('click', (e) => {
  if (e.target === codeModal) closeCodeModal();
});
codeCopyAll.addEventListener('click', () => {
  navigator.clipboard.writeText(codeModalContent.textContent).then(() => {
    codeCopyAll.textContent = 'Copied!';
    setTimeout(() => codeCopyAll.textContent = 'Copy', 1500);
  });
});

function addViewCodeButton(msgDiv, userMessage) {
  const actions = document.createElement('div');
  actions.className = 'msg-actions';
  const btn = document.createElement('button');
  btn.className = 'msg-code-btn';
  btn.innerHTML = '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><polyline points="16 18 22 12 16 6"/><polyline points="8 6 2 12 8 18"/></svg> View Code';
  btn.addEventListener('click', () => openCodeModal(userMessage));
  actions.appendChild(btn);
  msgDiv.appendChild(actions);
}

// ═══════════════════════════════════════════════════════════════
// Boot
// ═══════════════════════════════════════════════════════════════
loadKeysFromStorage();
loadModels();
