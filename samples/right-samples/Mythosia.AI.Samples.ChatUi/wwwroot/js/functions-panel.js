// ═══════════════════════════════════════════════════════════════
// Functions Panel (Left Sidebar)
// ═══════════════════════════════════════════════════════════════

import { escapeHtml } from './utils.js';
import { fnList, fnPresetToggle } from './dom.js';
import { refreshState } from './state-panel.js';

export async function refreshFunctions() {
  try {
    const res = await fetch('/api/functions');
    const data = await res.json();
    renderFunctionList(data);
    fnPresetToggle.checked = data.presetEnabled !== false;
  } catch(_) {}
}

function renderFunctionList(data) {
  const fns = data.functions || [];
  if (fns.length === 0) {
    fnList.innerHTML = '<p class="fn-empty">No functions registered.</p>';
    return;
  }
  let html = '';
  fns.forEach(f => {
    const params = (f.parameters || []).map(p => {
      const req = p.required ? '*' : '';
      return `<span class="fn-param">${escapeHtml(p.name)}${req}: <em>${escapeHtml(p.type || 'string')}</em></span>`;
    }).join('');
    html += `<div class="fn-item">
      <div class="fn-item-name">${escapeHtml(f.name)}</div>
      <div class="fn-item-desc">${escapeHtml(f.description || '')}</div>
      ${params ? '<div class="fn-item-params">' + params + '</div>' : ''}
    </div>`;
  });
  fnList.innerHTML = html;
}

export function initFunctionsPanel() {
  fnPresetToggle.addEventListener('change', async () => {
    try {
      await fetch('/api/functions/toggle-preset', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ enabled: fnPresetToggle.checked })
      });
      refreshFunctions();
      refreshState();
    } catch(_) {}
  });
}
