// ═══════════════════════════════════════════════════════════════
// Internal State Panel (Right Sidebar) + Polling
// ═══════════════════════════════════════════════════════════════

import { escapeHtml, truncate } from './utils.js';
import {
  stateContainer,
  btnRefresh,
  stateMessageJsonModal,
  stateMessageJsonContent,
  stateMessageJsonClose,
  stateMessageJsonCopy
} from './dom.js';
import { app } from './state.js';
import { updateSummaryUI } from './settings.js';

// ── Polling ──────────────────────────────────────────────────
export function startStatePolling() {
  stopStatePolling();
  app.statePollingTimer = setInterval(refreshState, 5000);
}

export function stopStatePolling() {
  if (app.statePollingTimer) {
    clearInterval(app.statePollingTimer);
    app.statePollingTimer = null;
  }
}

let currentStateMessageJson = '';

export function initStatePanel() {
  btnRefresh.addEventListener('click', refreshState);
  stateContainer.addEventListener('click', handleStateContainerClick);
  stateMessageJsonClose.addEventListener('click', closeStateMessageJsonModal);
  stateMessageJsonModal.addEventListener('click', (e) => {
    if (e.target === stateMessageJsonModal) closeStateMessageJsonModal();
  });
  stateMessageJsonCopy.addEventListener('click', () => {
    navigator.clipboard.writeText(currentStateMessageJson).then(() => {
      stateMessageJsonCopy.textContent = 'Copied!';
      setTimeout(() => {
        stateMessageJsonCopy.textContent = 'Copy';
      }, 1500);
    });
  });
}

// ── Fetch & Render ───────────────────────────────────────────
export async function refreshState() {
  try {
    const res = await fetch('/api/state');
    const s = await res.json();
    if (!s.configured) {
      stateContainer.innerHTML = '<div class="empty-state"><p>No service configured yet.</p></div>';
      return;
    }
    renderState(s);
    updateSummaryUI(s);
  } catch (e) {
    stateContainer.innerHTML = `<div class="empty-state"><p style="color:var(--danger)">Failed to fetch state</p></div>`;
  }
}

function renderState(s) {
  let html = '';

  html += section('Service', [
    row('Provider', s.provider),
    row('Model', s.model),
    row('Model Enum', s.modelEnum),
  ]);

  html += section('Generation Settings', [
    row('Temperature', s.temperature?.toFixed(2)),
    row('Top P', s.topP?.toFixed(2)),
    row('Max Output Tokens', s.maxTokens),
    row('Freq Penalty', s.frequencyPenalty?.toFixed(2)),
    row('Pres Penalty', s.presencePenalty?.toFixed(2)),
    row('Stream', s.stream, 'bool'),
  ]);

  html += section('Modes', [
    row('Stateless', s.statelessMode, 'bool'),
    row('Functions Disabled', s.functionsDisabled, 'bool'),
  ]);

  html += section('Function Calling', [
    row('Enabled', s.enableFunctions, 'bool'),
    row('Mode', s.functionCallMode),
    row('Force Function', s.forceFunctionName || '(none)'),
    row('Should Use', s.shouldUseFunctions, 'bool'),
    row('Registered', s.functions?.length ?? 0),
  ]);

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

  html += section('Default Policy', [
    row('Max Rounds', s.defaultPolicy?.maxRounds),
    row('Timeout (s)', s.defaultPolicy?.timeoutSeconds ?? '(none)'),
    row('Max Concurrency', s.defaultPolicy?.maxConcurrency),
    row('Logging', s.defaultPolicy?.enableLogging, 'bool'),
  ]);

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

  html += section('ChatBlock', [
    row('Active Chat ID', s.activeChatId?.substring(0, 8) + '...'),
    row('System Message', s.systemMessage || '(empty)'),
    row('Chat Blocks', s.chatBlockCount),
    row('Total Messages', s.messageCount),
    row('Conversation Tokens', typeof s.conversationTokenCount === 'number' ? s.conversationTokenCount.toLocaleString() : '(unavailable)'),
  ]);

  const totalMsg = s.messages?.length ?? 0;
  const tokenSuffix = typeof s.conversationTokenCount === 'number'
    ? ` · ${s.conversationTokenCount.toLocaleString()} tokens`
    : '';
  html += `<div class="state-section"><div class="state-section-title">Messages (${totalMsg}${tokenSuffix})</div>`;
  if (s.messages && s.messages.length > 0) {
    s.messages.forEach((m, idx) => {
      const roleClass = m.role.toLowerCase();
      html += `<div class="state-msg" data-message-json="${escapeHtml(encodeMessageJson(m))}">
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
      html += `<div class="state-msg-actions">
        <button class="state-msg-json-btn" type="button" title="View message JSON" aria-label="View message JSON">
          <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
            <path d="M9 18l-6-6 6-6"/>
            <path d="M15 6l6 6-6 6"/>
          </svg>
        </button>
      </div>`;
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

function handleStateContainerClick(e) {
  const btn = e.target.closest('.state-msg-json-btn');
  if (!btn) return;

  const card = btn.closest('.state-msg');
  const encoded = card?.dataset.messageJson;
  if (!encoded) return;

  openStateMessageJsonModal(decodeMessageJson(encoded));
}

function openStateMessageJsonModal(jsonText) {
  currentStateMessageJson = jsonText;
  stateMessageJsonModal.classList.remove('hidden');
  stateMessageJsonContent.textContent = jsonText;
  stateMessageJsonCopy.textContent = 'Copy';
  if (window.hljs) {
    window.hljs.highlightElement(stateMessageJsonContent);
  }
}

function closeStateMessageJsonModal() {
  stateMessageJsonModal.classList.add('hidden');
  stateMessageJsonContent.textContent = '';
  currentStateMessageJson = '';
}

function encodeMessageJson(message) {
  return JSON.stringify(message, null, 2);
}

function decodeMessageJson(value) {
  const textarea = document.createElement('textarea');
  textarea.innerHTML = value;
  return textarea.value;
}
