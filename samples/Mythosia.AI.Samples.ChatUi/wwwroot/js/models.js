// ═══════════════════════════════════════════════════════════════
// Model Loading, Selection & Connection
// ═══════════════════════════════════════════════════════════════

import { $$ } from './utils.js';
import { escapeHtml } from './utils.js';
import {
  modelListEl, settingsArea, chatStatus, chatMessages, functionsArea
} from './dom.js';
import {
  app, providerKeys, enableChatInput, disableChatInput, autoScroll
} from './state.js';
import { openKeyModal } from './apikey-modal.js';
import { updateReasoningUI } from './settings.js';
import { startStatePolling, stopStatePolling, refreshState } from './state-panel.js';
import { refreshFunctions } from './functions-panel.js';

// ── Load models from API ─────────────────────────────────────
export async function loadModels() {
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

    if (!providerKeys[g.provider]) groupEl.classList.add('disabled');

    const label = document.createElement('div');
    label.className = 'provider-label';
    label.innerHTML = `<span class="arrow">&#9654;</span><span class="provider-name">${g.provider}</span>`;

    const keyBtn = document.createElement('button');
    keyBtn.className = 'provider-key-btn' + (providerKeys[g.provider] ? ' has-key' : '');
    keyBtn.textContent = providerKeys[g.provider] ? 'Key \u2713' : 'API Key';
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

    label.addEventListener('click', (e) => {
      if (e.target.closest('.provider-key-btn')) return;
      if (!providerKeys[g.provider]) return;
      const arrow = label.querySelector('.arrow');
      const isOpen = modelsEl.classList.toggle('open');
      arrow.classList.toggle('open', isOpen);
    });
  });
}

export function refreshProviderGroup(provider) {
  const group = modelListEl.querySelector(`.provider-group[data-provider="${provider}"]`);
  if (!group) return;
  const hasKey = !!providerKeys[provider];
  group.classList.toggle('disabled', !hasKey);
  const keyBtn = group.querySelector('.provider-key-btn');
  if (keyBtn) {
    keyBtn.classList.toggle('has-key', hasKey);
    keyBtn.textContent = hasKey ? 'Key \u2713' : 'API Key';
  }
}

// ── Model selection ──────────────────────────────────────────
import { setMaxTokens } from './dom.js';

function onModelSelect(modelName, provider, desc, reasoning, maxOutputTokens) {
  if (app.isSending) return;

  if (app.selectedModel === modelName) {
    deselectModel();
    return;
  }

  if (!providerKeys[provider]) return;

  $$('.model-item.selected').forEach(el => el.classList.remove('selected'));

  app.selectedModel = modelName;
  app.selectedProvider = provider;
  app.modelReasoningInfo = reasoning || null;
  updateReasoningUI();
  if (maxOutputTokens) setMaxTokens.value = maxOutputTokens;
  const btn = document.querySelector(`.model-item[data-model="${modelName}"]`);
  if (btn) btn.classList.add('selected');

  chatStatus.textContent = `Connecting to ${desc}...`;
  chatStatus.classList.remove('connected');
  connectToModel(modelName, provider, desc);
}

export function deselectModel() {
  app.selectedModel = null;
  app.selectedProvider = null;
  app.modelReasoningInfo = null;
  updateReasoningUI();
  $$('.model-item.selected').forEach(el => el.classList.remove('selected'));
  settingsArea.classList.add('hidden');
  chatStatus.textContent = 'No model selected';
  chatStatus.classList.remove('connected');
  app.isConnected = false;
  disableChatInput();
  stopStatePolling();
}

// ── Connect to model ─────────────────────────────────────────
import { setSystem } from './dom.js';

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

    app.isConnected = true;

    chatStatus.textContent = `Connected: ${data.model} (${data.provider})`;
    chatStatus.classList.add('connected');

    appendModelSwitchEvent(data.model, data.provider);

    settingsArea.classList.remove('hidden');
    functionsArea.classList.remove('hidden');

    enableChatInput();
    startStatePolling();
    refreshState();
    refreshFunctions();
  } catch (e) {
    chatStatus.textContent = `Error: ${e.message}`;
    chatStatus.classList.remove('connected');
    app.isConnected = false;
    disableChatInput();
  }
}

function appendModelSwitchEvent(model, provider) {
  const empty = chatMessages.querySelector('.empty-state');
  if (empty) empty.remove();
  const div = document.createElement('div');
  div.className = 'model-switch-event';
  div.innerHTML = `<span class="model-switch-icon">\u21C4</span> Switched to <strong>${escapeHtml(model)}</strong> <span class="model-switch-provider">${escapeHtml(provider)}</span>`;
  chatMessages.appendChild(div);
  chatMessages.scrollTop = chatMessages.scrollHeight;
}
