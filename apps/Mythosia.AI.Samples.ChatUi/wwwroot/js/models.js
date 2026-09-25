// ═══════════════════════════════════════════════════════════════
// Model Loading, Selection & Connection
// ═══════════════════════════════════════════════════════════════

import { $$ } from './utils.js';
import { escapeHtml } from './utils.js';
import {
  modelListEl, settingsArea, chatStatus, chatMessages, functionsArea, ragRewriterModel
} from './dom.js';
import {
  app, providerKeys, alibabaSettings, enableChatInput, disableChatInput, autoScroll
} from './state.js';
import { openKeyModal } from './apikey-modal.js';
import { openAlibabaSettingsModal } from './alibaba-settings.js';
import { updateReasoningUI, updateSamplingUI, updateModelControls } from './settings.js';
import { startStatePolling, stopStatePolling, refreshState } from './state-panel.js';
import { refreshFunctions } from './functions-panel.js';
import { populateRewriterModels } from './rag-rewriter-models.js';

// ── Load models from API ─────────────────────────────────────
export async function loadModels() {
  try {
    const res = await fetch('/api/models');
    if (!res.ok) throw new Error('Model catalogue unavailable');
    const groups = await res.json();
    if (!Array.isArray(groups)) throw new Error('Invalid model catalogue');
    renderModelList(groups);
    populateRewriterModels(ragRewriterModel, groups);
    const search = document.getElementById('model-search');
    if (search) search.oninput = () => filterModels(search.value);
    const clearSearch = document.getElementById('model-search-clear');
    if (clearSearch) clearSearch.onclick = () => {
      if (search) search.value = '';
      filterModels('');
      search?.focus();
    };
    filterModels(search?.value || '');
  } catch (e) {
    modelListEl.innerHTML = '<p class="model-search-empty" role="alert">The model catalogue could not be loaded.</p><button type="button" class="btn btn-secondary" id="retry-models">Try again</button>';
    document.getElementById('retry-models')?.addEventListener('click', loadModels);
    const count = document.getElementById('model-count');
    if (count) count.textContent = 'Connection unavailable';
  }
}

function renderModelList(groups) {
  modelListEl.innerHTML = '';
  groups.forEach((g, groupIndex) => {
    const groupEl = document.createElement('div');
    groupEl.className = 'provider-group';
    groupEl.dataset.provider = g.provider;

    if (!providerKeys[g.provider]) groupEl.classList.add('disabled');

    const label = document.createElement('div');
    label.className = 'provider-label';
    const toggle = document.createElement('button');
    toggle.type = 'button';
    toggle.className = 'provider-toggle';
    toggle.setAttribute('aria-controls', `provider-models-${groupIndex}`);
    // Keep every provider visible on first load, regardless of catalogue size.
    const initiallyOpen = false;
    toggle.setAttribute('aria-expanded', String(initiallyOpen));
    toggle.innerHTML = `<span class="provider-glyph" aria-hidden="true">${escapeHtml(g.provider.substring(0, 2))}</span><span class="provider-name">${escapeHtml(g.provider)}</span><span class="provider-model-count">${g.models.length}</span><span class="arrow${initiallyOpen ? ' open' : ''}" aria-hidden="true">&#9654;</span>`;
    label.appendChild(toggle);

    const keyBtn = document.createElement('button');
    keyBtn.className = 'provider-key-btn' + (providerKeys[g.provider] ? ' has-key' : '');
    keyBtn.type = 'button';
    keyBtn.setAttribute('aria-label', `${g.provider} ${g.provider === 'Alibaba' ? 'connection settings' : 'API key'}`);
    if (g.provider === 'Alibaba') {
      keyBtn.textContent = providerKeys[g.provider] ? 'Settings \u2713' : 'Settings';
      keyBtn.addEventListener('click', (e) => {
        e.stopPropagation();
        openAlibabaSettingsModal();
      });
    } else {
      keyBtn.textContent = providerKeys[g.provider] ? 'Key \u2713' : 'API Key';
      keyBtn.addEventListener('click', (e) => {
        e.stopPropagation();
        openKeyModal(g.provider);
      });
    }
    label.appendChild(keyBtn);
    groupEl.appendChild(label);

    const modelsEl = document.createElement('div');
    modelsEl.className = 'provider-models' + (initiallyOpen ? ' open' : '');
    modelsEl.id = `provider-models-${groupIndex}`;

    g.models.forEach(m => {
      const btn = document.createElement('button');
      btn.className = 'model-item';
      btn.type = 'button';
      btn.setAttribute('aria-pressed', 'false');
      btn.textContent = m.description;
      btn.dataset.model = m.name;
      btn.dataset.provider = g.provider;
      btn.dataset.desc = m.description;
      btn.dataset.reasoning = m.reasoning ? JSON.stringify(m.reasoning) : '';
      btn.dataset.maxOutputTokens = m.maxOutputTokens || '';
      btn.addEventListener('click', () => onModelSelect(m.name, g.provider, m.description, m.reasoning, m.maxOutputTokens, m.sampling));
      modelsEl.appendChild(btn);
    });

    groupEl.appendChild(modelsEl);
    modelListEl.appendChild(groupEl);

    toggle.addEventListener('click', () => {
      const arrow = label.querySelector('.arrow');
      const isOpen = modelsEl.classList.toggle('open');
      arrow.classList.toggle('open', isOpen);
      toggle.setAttribute('aria-expanded', String(isOpen));
    });
  });
}

export function filterModels(value) {
  const query = value.trim().toLowerCase();
  const clearSearch = document.getElementById('model-search-clear');
  if (clearSearch) clearSearch.hidden = value.length === 0;
  let visibleCount = 0;
  const groups = modelListEl.querySelectorAll('.provider-group');
  groups.forEach(group => {
    let matches = 0;
    group.querySelectorAll('.model-item').forEach(button => {
      const match = !query || `${button.dataset.provider} ${button.dataset.desc} ${button.dataset.model}`.toLowerCase().includes(query);
      button.hidden = !match;
      if (match) matches++;
    });
    group.hidden = matches === 0;
    const list = group.querySelector('.provider-models');
    const toggle = group.querySelector('.provider-toggle');
    if (query && matches) {
      if (!('beforeSearch' in group.dataset)) group.dataset.beforeSearch = String(list.classList.contains('open'));
      list.classList.add('open');
    } else if (!query && 'beforeSearch' in group.dataset) {
      list.classList.toggle('open', group.dataset.beforeSearch === 'true');
      delete group.dataset.beforeSearch;
    }
    const open = list.classList.contains('open');
    toggle.setAttribute('aria-expanded', String(open));
    group.querySelector('.arrow').classList.toggle('open', open);
    visibleCount += matches;
  });
  const count = document.getElementById('model-count');
  if (count) count.textContent = query ? `${visibleCount} matching models` : `${groups.length} providers · ${visibleCount} models`;
  document.getElementById('model-search-empty')?.classList.toggle('hidden', visibleCount !== 0);
}

export function refreshProviderGroup(provider) {
  const group = modelListEl.querySelector(`.provider-group[data-provider="${provider}"]`);
  if (!group) return;
  const hasKey = !!providerKeys[provider];
  group.classList.toggle('disabled', !hasKey);
  const keyBtn = group.querySelector('.provider-key-btn');
  if (keyBtn) {
    keyBtn.classList.toggle('has-key', hasKey);
    if (provider === 'Alibaba') {
      keyBtn.textContent = hasKey ? 'Settings \u2713' : 'Settings';
    } else {
      keyBtn.textContent = hasKey ? 'Key \u2713' : 'API Key';
    }
  }
}

// ── Model selection ──────────────────────────────────────────
import { setMaxTokens } from './dom.js';

function onModelSelect(modelName, provider, desc, reasoning, maxOutputTokens, sampling) {
  if (app.isSending) return;

  if (app.selectedModel === modelName) {
    deselectModel();
    return;
  }

  if (!providerKeys[provider]) {
    if (provider === 'Alibaba') openAlibabaSettingsModal();
    else openKeyModal(provider);
    return;
  }
  const connectionRevision = app.connectionRevision = (app.connectionRevision || 0) + 1;
  app.controlsRevision = (app.controlsRevision || 0) + 1;
  app.settingsPending = false;
  app.isConnected = false;
  disableChatInput();
  stopStatePolling();

  $$('.model-item.selected').forEach(el => { el.classList.remove('selected'); el.setAttribute('aria-pressed', 'false'); });

  app.selectedModel = modelName;
  app.selectedProvider = provider;
  app.modelReasoningInfo = reasoning || null;
  app.modelSamplingInfo = sampling || null;
  updateReasoningUI();
  updateSamplingUI();
  // Unknown model limits use an editable initial budget, not an advertised maximum.
  setMaxTokens.value = maxOutputTokens || 4096;
  const btn = document.querySelector(`.model-item[data-model="${modelName}"]`);
  if (btn) { btn.classList.add('selected'); btn.setAttribute('aria-pressed', 'true'); }

  chatStatus.textContent = `Connecting to ${desc}...`;
  chatStatus.classList.remove('connected');
  connectToModel(modelName, provider, desc, connectionRevision);
}

export function deselectModel() {
  app.connectionRevision = (app.connectionRevision || 0) + 1;
  app.controlsRevision = (app.controlsRevision || 0) + 1;
  app.settingsPending = false;
  app.selectedModel = null;
  app.selectedProvider = null;
  app.modelReasoningInfo = null;
  app.modelSamplingInfo = null;
  updateReasoningUI();
  updateSamplingUI();
  $$('.model-item.selected').forEach(el => { el.classList.remove('selected'); el.setAttribute('aria-pressed', 'false'); });
  settingsArea.classList.add('hidden');
  chatStatus.textContent = 'No model selected';
  chatStatus.classList.remove('connected');
  app.isConnected = false;
  disableChatInput();
  stopStatePolling();
}

// ── Connect to model ─────────────────────────────────────────
import { setSystem } from './dom.js';

async function connectToModel(modelName, provider, desc, connectionRevision) {
  const apiKey = providerKeys[provider];
  if (!modelName || !apiKey) return;

  try {
    const body = {
      apiKey: provider === 'Alibaba' ? null : apiKey,
      model: modelName,
      systemMessage: setSystem.value || null
    };
    if (provider === 'Alibaba' && alibabaSettings.baseUrl) {
      if (!alibabaSettings.platform) {
        throw new Error('Alibaba custom endpoint platform is required.');
      }
      body.baseUrl = alibabaSettings.baseUrl;
      body.platform = alibabaSettings.platform;
      if (alibabaSettings.modelOverrideEnabled && alibabaSettings.modelIdOverride) {
        body.modelIdOverride = alibabaSettings.modelIdOverride;
      }
    }

    const res = await fetch('/api/configure', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body)
    });

    const data = await res.json();
    if (app.connectionRevision !== connectionRevision) return;
    if (app.selectedModel !== modelName || app.selectedProvider !== provider) return;
    if (!res.ok) throw new Error(data.error);

    updateModelControls(data.controls);
    // The editable budget is the configured value, not the model's supported ceiling.
    if (data.controls?.currentMaxTokens != null) setMaxTokens.value = data.controls.currentMaxTokens;

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
    if (app.connectionRevision !== connectionRevision) return;
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
