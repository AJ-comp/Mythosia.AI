// ═══════════════════════════════════════════════════════════════
// Settings Panel
// ═══════════════════════════════════════════════════════════════

import {
  setSystem, setTemp, setTopp, setMaxTokens,
  setStateless, setReasoning, reasoningOpts, reasoningLvls,
  tempVal, toppVal,
  setSummary, summaryOpts, summaryTriggerType, summaryTriggerVal,
  summaryKeepVal, summaryError, summaryCurrent, summaryText, summaryClear
} from './dom.js';
import { app } from './state.js';
import { refreshState } from './state-panel.js';

let _settingsTimer = null;
let _settingsRevision = 0;

export function scheduleApplySettings(delay = 400) {
  clearTimeout(_settingsTimer);
  const revision = ++_settingsRevision;
  app.controlsRevision = (app.controlsRevision || 0) + 1;
  app.settingsPending = true;
  const connectionRevision = app.connectionRevision;
  _settingsTimer = setTimeout(() => applySettings(revision, connectionRevision), delay);
}

async function applySettings(revision, connectionRevision) {
  if (app.connectionRevision !== connectionRevision || revision !== _settingsRevision) return;
  if (!app.isConnected) { app.settingsPending = false; return; }
  const selectedModel = app.selectedModel;

  const body = {
    temperature: setTemp.disabled ? null : parseFloat(setTemp.value),
    topP: setTopp.disabled ? null : parseFloat(setTopp.value),
    maxTokens: parseInt(setMaxTokens.value),
    statelessMode: setStateless.checked,
    systemMessage: setSystem.value || '',
    reasoningEnabled: app.modelReasoningInfo ? setReasoning.checked : null,
    reasoningLevel: null,
    reasoningType: null,
    speed: document.getElementById('set-speed')?.value || 'ProviderDefault'
  };
  if (app.selectedProvider === 'Perplexity') {
    body.perplexityPreset = document.getElementById('set-perplexity-preset').value;
    body.perplexityMaxSteps = Number(document.getElementById('set-perplexity-steps').value);
    body.perplexityWebSearch = document.getElementById('set-perplexity-search').checked;
  }

  if (setReasoning.checked && app.modelReasoningInfo) {
    const info = app.modelReasoningInfo;
    if (info.type === 'grok_always') {
      if (info.levels.length > 0) {
        const sel = reasoningLvls.querySelector('input[name="reasoning-level"]:checked');
        body.reasoningEnabled = true;
        body.reasoningLevel = sel ? sel.value : info.levels[0];
        body.reasoningType = info.type;
      } else {
        body.reasoningEnabled = null;
      }
    } else if (info.type === 'claude_always' || info.type === 'gemini3' || info.type === 'gpt6') {
      const sel = reasoningLvls.querySelector('input[name="reasoning-level"]:checked');
      body.reasoningEnabled = true;
      body.reasoningLevel = sel ? sel.value : info.levels[0];
      body.reasoningType = info.type;
    } else if (info.type === 'qwen_thinking') {
      // Qwen3: simple on/off, send type so backend knows
      body.reasoningType = info.type;
    } else {
      const sel = reasoningLvls.querySelector('input[name="reasoning-level"]:checked');
      body.reasoningLevel = sel ? sel.value : info.levels[0];
      body.reasoningType = info.type;
    }
  }

  try {
    const response = await fetch('/api/settings', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body)
    });
    const result = await response.json();
    if (revision !== _settingsRevision || app.connectionRevision !== connectionRevision ||
        app.selectedModel !== selectedModel) return;
    if (response.ok) updateModelControls(result.controls);
    setSettingsError(response.ok ? '' : result.error || 'Could not apply settings.');
    if (app.selectedProvider === 'Perplexity') {
      document.getElementById('perplexity-settings-error').textContent = response.ok ? '' : result.error || 'Could not apply settings.';
    }
    refreshState();
  } catch (e) {
    if (revision === _settingsRevision && app.connectionRevision === connectionRevision)
      setSettingsError(`Could not save settings: ${e.message}`);
  }
  finally {
    if (revision === _settingsRevision && app.connectionRevision === connectionRevision) {
      app.settingsPending = false;
      app.controlsRevision = (app.controlsRevision || 0) + 1;
    }
  }
}

export function updateReasoningUI() {
  const perplexityPanel = document.getElementById('perplexity-options');
  if (perplexityPanel) perplexityPanel.classList.toggle('hidden', app.selectedProvider !== 'Perplexity');
  if (app.modelReasoningInfo) {
    const info = app.modelReasoningInfo;

    if (info.type === 'perplexity') {
      setReasoning.checked = false;
      setReasoning.disabled = false;
      reasoningOpts.classList.remove('hidden');
      reasoningLvls.innerHTML = '<span class="reasoning-always-label">Auto keeps the model or preset default. Available effort depends on the model.</span>';
      info.levels.forEach((lvl, i) => {
        const label = document.createElement('label');
        label.innerHTML = `<input type="radio" name="reasoning-level" value="${lvl}" ${i === 0 ? 'checked' : ''} /><span>${lvl}</span>`;
        label.querySelector('input').addEventListener('change', () => scheduleApplySettings(0));
        reasoningLvls.appendChild(label);
      });
    } else if (info.type === 'grok_always') {
      setReasoning.checked = true;
      setReasoning.disabled = true;
      reasoningOpts.classList.remove('hidden');
      if (info.levels.length === 0) {
        reasoningLvls.innerHTML = '<span class="reasoning-always-label">Always On — reasoning is built-in</span>';
      } else {
        reasoningLvls.innerHTML = '<span class="reasoning-always-label">Always on — select effort</span>';
        info.levels.forEach((lvl, i) => {
          const label = document.createElement('label');
          label.innerHTML = `<input type="radio" name="reasoning-level" value="${lvl}" ${i === 0 ? 'checked' : ''} /><span>${lvl}</span>`;
          label.querySelector('input').addEventListener('change', () => scheduleApplySettings(0));
          reasoningLvls.appendChild(label);
        });
      }
    } else if (info.type === 'claude_always' || info.type === 'gemini3' || info.type === 'gpt6') {
      setReasoning.checked = true;
      setReasoning.disabled = true;
      reasoningOpts.classList.remove('hidden');
      reasoningLvls.innerHTML = info.type === 'gpt6' && info.levels.includes('None')
        ? '<span class="reasoning-always-label">Select effort — None disables reasoning</span>'
        : `<span class="reasoning-always-label">Always on — select ${info.type === 'gemini3' ? 'thinking level' : 'effort'}</span>`;
      info.levels.forEach(lvl => {
        const label = document.createElement('label');
        label.innerHTML = `<input type="radio" name="reasoning-level" value="${lvl}" ${lvl === (info.defaultLevel || info.levels[0]) ? 'checked' : ''} /><span>${lvl}</span>`;
        label.querySelector('input').addEventListener('change', () => scheduleApplySettings(0));
        reasoningLvls.appendChild(label);
      });
    } else if (info.type === 'qwen_thinking' || info.type === 'deepseek_thinking') {
      // DeepSeek starts with thinking disabled and preserves the selected effort when toggled.
      setReasoning.disabled = false;
      if (info.type === 'deepseek_thinking') setReasoning.checked = false;
      reasoningOpts.classList.remove('hidden');
      reasoningLvls.innerHTML = info.type === 'deepseek_thinking'
        ? '<span class="reasoning-always-label">Enable reasoning for harder tasks; Auto uses High</span>'
        : '<span class="reasoning-always-label">Enable extended thinking</span>';
      info.levels.forEach((lvl, i) => {
        const label = document.createElement('label');
        label.innerHTML = `<input type="radio" name="reasoning-level" value="${lvl}" ${i === 0 ? 'checked' : ''} /><span>${lvl}</span>`;
        label.querySelector('input').addEventListener('change', () => scheduleApplySettings(0));
        reasoningLvls.appendChild(label);
      });
    } else {
      setReasoning.disabled = false;
      reasoningLvls.innerHTML = '';
      info.levels.forEach((lvl, i) => {
        const label = document.createElement('label');
        label.innerHTML = `<input type="radio" name="reasoning-level" value="${lvl}" ${i === 0 ? 'checked' : ''} /><span>${lvl}</span>`;
        label.querySelector('input').addEventListener('change', () => scheduleApplySettings(0));
        reasoningLvls.appendChild(label);
      });
    }
  } else {
    setReasoning.disabled = true;
    setReasoning.checked = false;
    reasoningOpts.classList.add('hidden');
    reasoningLvls.innerHTML = '';
  }
}

// ── Summary Policy ───────────────────────────────────────────
export function updateModelControls(controls) {
  if (!controls) return;
  const reasoning = controls.reasoning ?? null;
  if (JSON.stringify(app.modelReasoningInfo) !== JSON.stringify(reasoning)) {
    app.modelReasoningInfo = reasoning;
    updateReasoningUI();
  }
  app.modelSamplingInfo = controls.sampling ?? null;
  updateSamplingUI();
  app.modelSpeedInfo = controls.speed ?? null;
  updateSpeedUI();
  renderCapabilities(controls.capabilities);
  if (controls.maxOutputTokens) setMaxTokens.max = controls.maxOutputTokens;
  else setMaxTokens.removeAttribute('max');
}

function setSettingsError(message) {
  const error = document.getElementById('settings-error');
  if (!error) return;
  error.textContent = message;
  error.classList.toggle('hidden', !message);
}

function updateSpeedUI() {
  const select = document.getElementById('set-speed');
  const help = document.getElementById('speed-help');
  if (!select) return;
  const info = app.modelSpeedInfo;
  for (const option of select.options) {
    if (option.value === 'ProviderDefault') continue;
    const support = info?.[option.value.toLowerCase()] || 'Unknown';
    option.disabled = support !== 'Supported';
    const label = option.value === 'Fast' ? 'Fast · premium' : 'Standard';
    option.textContent = support === 'Supported' ? label
      : `${label} · ${support === 'Unknown' ? 'not verified' : 'unavailable'}`;
  }
  select.value = info?.selected || 'ProviderDefault';
  if (select.selectedOptions[0]?.disabled) select.value = 'ProviderDefault';
  if (help) help.textContent = select.value === 'Fast'
    ? 'Premium processing may cost more. Availability and latency depend on your provider and account.'
    : select.value === 'Standard'
      ? 'Explicitly request standard processing. This does not change the model or reasoning effort.'
      : 'No speed override is sent. Your provider or project default applies.';
}

function renderCapabilities(capabilities) {
  const container = document.getElementById('model-capabilities');
  if (!container) return;
  const signature = JSON.stringify(capabilities || null);
  if (container.dataset.capabilities === signature) return;
  container.dataset.capabilities = signature;
  const wasOpen = container.querySelector('details')?.open || false;
  container.replaceChildren();
  if (!capabilities) return;
  const details = document.createElement('details');
  details.open = wasOpen;
  const summary = document.createElement('summary');
  summary.textContent = 'Model capabilities';
  details.appendChild(summary);
  const explanation = document.createElement('p');
  explanation.className = 'setting-help';
  explanation.textContent = 'Library support for this model and endpoint. Availability does not enable a feature or guarantee account access.';
  details.appendChild(explanation);
  const list = document.createElement('dl');
  list.className = 'capability-list';
  for (const [key, label] of Object.entries({ streaming: 'Streaming', reasoning: 'Reasoning', functionCalling: 'Tool calling',
    asyncFunctionCalling: 'Async tools', steering: 'Steering', imageInput: 'Image input',
    webSearch: 'Native web search', fileSearch: 'Native file search', structuredOutput: 'Structured output' })) {
    const support = capabilities[key] || 'Unknown';
    const term = document.createElement('dt');
    term.textContent = label;
    const value = document.createElement('dd');
    value.textContent = support === 'Supported' ? 'Supported' : support === 'Unsupported' ? 'Unavailable' : 'Not verified';
    value.dataset.support = support.toLowerCase();
    list.append(term, value);
  }
  details.appendChild(list);
  container.appendChild(details);
}

export function updateSamplingUI() {
  const sampling = app.modelSamplingInfo || { temperature: false, topP: false };
  setTemp.disabled = sampling.temperature === false;
  setTopp.disabled = sampling.topP === false;
  setTemp.title = sampling.temperatureSupport === 'Unknown'
    ? 'Temperature support has not been verified for this connection.'
    : setTemp.disabled ? 'Temperature is unavailable for this request.' : '';
  setTopp.title = sampling.topPSupport === 'Unknown'
    ? 'Top P support has not been verified for this connection.'
    : setTopp.disabled ? 'Top P is unavailable for this request.' : '';
}

let _summaryTimer = null;

function setSummaryError(message) {
  if (!summaryError) return;
  if (!message) {
    summaryError.textContent = '';
    summaryError.classList.add('hidden');
    return;
  }
  summaryError.textContent = message;
  summaryError.classList.remove('hidden');
}

async function applySummaryPolicy() {
  if (!app.isConnected) return;
  try {
    const res = await fetch('/api/summary-policy', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        enabled: setSummary.checked,
        triggerType: summaryTriggerType.value,
        threshold: parseInt(summaryTriggerVal.value) || 20,
        keepRecent: parseInt(summaryKeepVal.value) || 5
      })
    });
    const data = await res.json().catch(() => ({}));
    if (!res.ok) {
      setSummaryError(data.error || 'Failed to apply summary policy.');
      return;
    }
    setSummaryError('');
    refreshState();
  } catch (e) {
    setSummaryError(`Network error: ${e.message}`);
  }
}

function scheduleSummaryApply(delay = 400) {
  clearTimeout(_summaryTimer);
  _summaryTimer = setTimeout(applySummaryPolicy, delay);
}

export function updateSummaryUI(stateData) {
  if (!stateData?.summaryPolicy) {
    summaryCurrent.classList.add('hidden');
    summaryText.textContent = '';
    return;
  }
  const sp = stateData.summaryPolicy;
  if (sp.currentSummary) {
    summaryCurrent.classList.remove('hidden');
    summaryText.textContent = sp.currentSummary;
  } else {
    summaryCurrent.classList.add('hidden');
    summaryText.textContent = '';
  }
}

// ── Event listeners ──────────────────────────────────────────
export function initSettings() {
  document.getElementById('set-speed')?.addEventListener('change', () => {
    const select = document.getElementById('set-speed');
    app.modelSpeedInfo = { ...app.modelSpeedInfo, selected: select.value };
    updateSpeedUI();
    scheduleApplySettings(0);
  });
  for (const id of ['set-perplexity-preset', 'set-perplexity-steps', 'set-perplexity-search']) {
    document.getElementById(id)?.addEventListener('change', () => {
      const preset = document.getElementById('set-perplexity-preset').value !== 'Model';
      const search = document.getElementById('set-perplexity-search');
      search.disabled = preset;
      if (preset) search.checked = true;
      scheduleApplySettings(0);
    });
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
  setStateless.addEventListener('change', () => scheduleApplySettings(0));
  setSystem.addEventListener('input', () => scheduleApplySettings(800));

  setReasoning.addEventListener('change', () => {
    updateSamplingUI();
    if (setReasoning.checked && app.modelReasoningInfo) {
      reasoningOpts.classList.remove('hidden');
    } else {
      reasoningOpts.classList.add('hidden');
    }
    scheduleApplySettings(0);
  });

  // Summary policy
  setSummary.addEventListener('change', () => {
    if (setSummary.checked) {
      summaryOpts.classList.remove('hidden');
    } else {
      summaryOpts.classList.add('hidden');
    }
    setSummaryError('');
    scheduleSummaryApply(0);
  });
  summaryTriggerType.addEventListener('change', () => scheduleSummaryApply(0));
  summaryTriggerVal.addEventListener('change', () => scheduleSummaryApply(200));
  summaryKeepVal.addEventListener('change', () => scheduleSummaryApply(200));
  summaryClear.addEventListener('click', async () => {
    try {
      await fetch('/api/summary-clear', { method: 'POST' });
      summaryCurrent.classList.add('hidden');
      summaryText.textContent = '';
      refreshState();
    } catch (_) {}
  });
}

export function updatePerplexitySettings(state) {
  if (state?.reasoning?.type !== 'perplexity') return;
  const panel = document.getElementById('perplexity-options');
  if (!panel || panel.contains(document.activeElement)) return;
  const info = state.reasoning;
  document.getElementById('set-perplexity-preset').value = info.preset || 'Model';
  document.getElementById('set-perplexity-steps').value = info.maxSteps || 0;
  const search = document.getElementById('set-perplexity-search');
  search.checked = info.webSearch;
  search.disabled = info.preset !== 'Model';
  setReasoning.checked = info.enabled;
  for (const radio of reasoningLvls.querySelectorAll('input[name="reasoning-level"]')) radio.checked = radio.value === info.effort;
}
