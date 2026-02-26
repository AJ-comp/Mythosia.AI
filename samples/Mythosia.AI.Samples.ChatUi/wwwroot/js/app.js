// ═══════════════════════════════════════════════════════════════
// Mythosia.AI Chat UI — Frontend Logic
// ═══════════════════════════════════════════════════════════════

const $ = (sel) => document.querySelector(sel);
const $$ = (sel) => document.querySelectorAll(sel);

// ── State ───────────────────────────────────────────────────
let selectedModel = null;   // e.g. "Gpt4oMini"
let selectedProvider = null; // e.g. "OpenAI"
let isConnected = false;
let isSending = false;
let apiKeyValue = '';
let statePollingTimer = null;

// ── DOM refs ────────────────────────────────────────────────
const modelListEl    = $('#model-list');
const apikeyArea     = $('#apikey-area');
const apikeyLabel    = $('#apikey-label');
const apikeyInput    = $('#apikey-input');
const apikeyToggle   = $('#apikey-toggle');
const apikeyMasked   = $('#apikey-masked');
const apikeyConnect  = $('#apikey-connect');
const settingsArea   = $('#settings-area');
const chatStatus     = $('#chat-status');
const chatMessages   = $('#chat-messages');
const chatForm       = $('#chat-form');
const chatInput      = $('#chat-input');
const btnSend        = $('#btn-send');
const btnClear       = $('#btn-clear');
const stateContainer = $('#state-container');
const btnRefresh     = $('#btn-refresh-state');

// Settings
const setSystem     = $('#set-system');
const setTemp       = $('#set-temp');
const setTopp       = $('#set-topp');
const setMaxTokens  = $('#set-maxtokens');
const setMaxMsg     = $('#set-maxmsg');
const setStateless  = $('#set-stateless');
const tempVal       = $('#temp-val');
const toppVal       = $('#topp-val');
const settingsApply = $('#settings-apply');

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

    const label = document.createElement('div');
    label.className = 'provider-label';
    label.innerHTML = `<span class="arrow">&#9654;</span> ${g.provider}`;
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
      btn.addEventListener('click', () => onModelSelect(m.name, g.provider, m.description));
      modelsEl.appendChild(btn);
    });

    groupEl.appendChild(modelsEl);
    modelListEl.appendChild(groupEl);

    // Toggle expand/collapse
    label.addEventListener('click', () => {
      const arrow = label.querySelector('.arrow');
      const isOpen = modelsEl.classList.toggle('open');
      arrow.classList.toggle('open', isOpen);
    });
  });
}

// ═══════════════════════════════════════════════════════════════
// 2. Model selection
// ═══════════════════════════════════════════════════════════════
function onModelSelect(modelName, provider, desc) {
  // If same model clicked again → deselect
  if (selectedModel === modelName) {
    deselectModel();
    return;
  }

  // Deselect previous
  $$('.model-item.selected').forEach(el => el.classList.remove('selected'));

  // Select new
  selectedModel = modelName;
  selectedProvider = provider;
  const btn = document.querySelector(`.model-item[data-model="${modelName}"]`);
  if (btn) btn.classList.add('selected');

  // Show API key area
  apikeyArea.classList.remove('hidden');
  apikeyLabel.textContent = `${provider} API Key`;
  apikeyInput.value = '';
  apikeyInput.type = 'password';
  apikeyMasked.classList.add('hidden');
  apikeyConnect.disabled = true;

  // If already connected to a different model, reset
  if (isConnected) {
    isConnected = false;
    chatStatus.textContent = `${desc} selected — enter API key`;
    chatStatus.classList.remove('connected');
    disableChatInput();
  } else {
    chatStatus.textContent = `${desc} selected — enter API key`;
  }
}

function deselectModel() {
  selectedModel = null;
  selectedProvider = null;
  $$('.model-item.selected').forEach(el => el.classList.remove('selected'));
  apikeyArea.classList.add('hidden');
  settingsArea.classList.add('hidden');
  chatStatus.textContent = 'No model selected';
  chatStatus.classList.remove('connected');
  isConnected = false;
  disableChatInput();
  stopStatePolling();
}

// ═══════════════════════════════════════════════════════════════
// 3. API Key handling
// ═══════════════════════════════════════════════════════════════
apikeyInput.addEventListener('input', () => {
  apiKeyValue = apikeyInput.value.trim();
  apikeyConnect.disabled = apiKeyValue.length === 0;
});

apikeyToggle.addEventListener('click', () => {
  apikeyInput.type = apikeyInput.type === 'password' ? 'text' : 'password';
});

function maskApiKey(key) {
  if (!key || key.length <= 8) return '••••••••';
  return key.substring(0, 4) + '••••••••' + key.substring(key.length - 4);
}

apikeyConnect.addEventListener('click', () => connectToModel());

async function connectToModel() {
  if (!selectedModel || !apiKeyValue) return;

  apikeyConnect.disabled = true;
  apikeyConnect.textContent = 'Connecting...';

  try {
    const res = await fetch('/api/configure', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        apiKey: apiKeyValue,
        model: selectedModel,
        systemMessage: setSystem.value || null
      })
    });

    const data = await res.json();
    if (!res.ok) throw new Error(data.error);

    isConnected = true;

    // Show masked key
    apikeyInput.value = '';
    apikeyInput.type = 'password';
    apikeyMasked.textContent = maskApiKey(apiKeyValue);
    apikeyMasked.classList.remove('hidden');

    // Update status
    chatStatus.textContent = `Connected: ${data.model} (${data.provider})`;
    chatStatus.classList.add('connected');

    // Show settings
    settingsArea.classList.remove('hidden');

    // Enable chat
    enableChatInput();

    // Clear previous chat display
    chatMessages.innerHTML = '';

    // Start state polling
    startStatePolling();
    refreshState();

    apikeyConnect.textContent = 'Reconnect';
    apikeyConnect.disabled = false;
  } catch (e) {
    chatStatus.textContent = `Error: ${e.message}`;
    chatStatus.classList.remove('connected');
    apikeyConnect.textContent = 'Connect';
    apikeyConnect.disabled = false;
  }
}

// ═══════════════════════════════════════════════════════════════
// 4. Chat
// ═══════════════════════════════════════════════════════════════
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
  btnSend.disabled = true;

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
      appendMessage('assistant', res.ok ? (data.response || JSON.stringify(data)) : `Error: ${data.error}`);
      refreshState();
      return;
    }

    // SSE streaming
    const div = createMessageElement('assistant');
    const contentSpan = div.querySelector('.msg-content');
    const reader = res.body.getReader();
    const decoder = new TextDecoder();
    let buffer = '';
    let fullText = '';

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
          if (parsed.error) { fullText += '\nError: ' + parsed.error; }
          else if (parsed.content != null) { fullText += parsed.content; }
        } catch (_) {}
      }

      contentSpan.textContent = fullText;
      chatMessages.scrollTop = chatMessages.scrollHeight;
    }

    if (!fullText) contentSpan.textContent = '(empty response)';
    refreshState();
  } catch (e) {
    removeTyping(typingEl);
    appendMessage('assistant', `Network error: ${e.message}`);
  } finally {
    isSending = false;
    btnSend.disabled = false;
    chatInput.focus();
  }
}

function createMessageElement(role) {
  const empty = chatMessages.querySelector('.empty-state');
  if (empty) empty.remove();

  const div = document.createElement('div');
  div.className = `msg ${role}`;
  div.innerHTML = `<span class="msg-role">${role}</span><span class="msg-content"></span>`;
  chatMessages.appendChild(div);
  chatMessages.scrollTop = chatMessages.scrollHeight;
  return div;
}

function appendMessage(role, content) {
  const el = createMessageElement(role);
  el.querySelector('.msg-content').textContent = content;
}

function showTyping() {
  const div = document.createElement('div');
  div.className = 'typing';
  div.innerHTML = '<span></span><span></span><span></span>';
  chatMessages.appendChild(div);
  chatMessages.scrollTop = chatMessages.scrollHeight;
  return div;
}

function removeTyping(el) {
  if (el && el.parentNode) el.parentNode.removeChild(el);
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
setTemp.addEventListener('input', () => { tempVal.textContent = parseFloat(setTemp.value).toFixed(2); });
setTopp.addEventListener('input', () => { toppVal.textContent = parseFloat(setTopp.value).toFixed(2); });

settingsApply.addEventListener('click', async () => {
  if (!isConnected) return;

  try {
    await fetch('/api/settings', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        temperature: parseFloat(setTemp.value),
        topP: parseFloat(setTopp.value),
        maxTokens: parseInt(setMaxTokens.value),
        maxMessageCount: parseInt(setMaxMsg.value),
        statelessMode: setStateless.checked,
        systemMessage: setSystem.value || ''
      })
    });
    refreshState();
  } catch (e) { /* ignore */ }
});

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
    row('Max Tokens', s.maxTokens),
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
    row('Message Count', s.messageCount),
  ]);

  // ── Messages
  html += `<div class="state-section"><div class="state-section-title">Messages (${s.messages?.length ?? 0})</div>`;
  if (s.messages && s.messages.length > 0) {
    s.messages.forEach(m => {
      const roleClass = m.role.toLowerCase();
      html += `<div class="state-msg">
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
// Boot
// ═══════════════════════════════════════════════════════════════
loadModels();
