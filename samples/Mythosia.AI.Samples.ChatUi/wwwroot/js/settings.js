// ═══════════════════════════════════════════════════════════════
// Settings Panel
// ═══════════════════════════════════════════════════════════════

import {
  setSystem, setTemp, setTopp, setMaxTokens, setMaxMsg,
  setStateless, setReasoning, reasoningOpts, reasoningLvls,
  tempVal, toppVal,
  setSummary, summaryOpts, summaryTriggerType, summaryTriggerVal,
  summaryKeepVal, summaryError, summaryCurrent, summaryText, summaryClear
} from './dom.js';
import { app } from './state.js';
import { refreshState } from './state-panel.js';

let _settingsTimer = null;

export function scheduleApplySettings(delay = 400) {
  clearTimeout(_settingsTimer);
  _settingsTimer = setTimeout(applySettings, delay);
}

async function applySettings() {
  if (!app.isConnected) return;

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

  if (setReasoning.checked && app.modelReasoningInfo) {
    const info = app.modelReasoningInfo;
    if (info.type === 'grok_always') {
      // Grok4: always reasoning, nothing to send
      body.reasoningEnabled = null;
    } else {
      const sel = reasoningLvls.querySelector('input[name="reasoning-level"]:checked');
      body.reasoningLevel = sel ? sel.value : info.levels[0];
      body.reasoningType = info.type;
    }
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

export function updateReasoningUI() {
  if (app.modelReasoningInfo) {
    const info = app.modelReasoningInfo;

    if (info.type === 'grok_always') {
      // Grok4: always reasoning, no controllable parameters
      setReasoning.checked = true;
      setReasoning.disabled = true;
      reasoningOpts.classList.remove('hidden');
      reasoningLvls.innerHTML = '<span class="reasoning-always-label">Always On — reasoning is built-in</span>';
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
