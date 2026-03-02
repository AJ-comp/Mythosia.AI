// ═══════════════════════════════════════════════════════════════
// RAG Diagnostics Modal — Health Check, Why Missing, Query Scores
// ═══════════════════════════════════════════════════════════════

import {
  btnRagDiagnose,
  diagModal,
  diagModalClose,
  diagTabs,
  diagPanels,
  diagHealthBtn,
  diagHealthResult,
  diagWhyQuery,
  diagWhyExpected,
  diagWhyBtn,
  diagWhyResult,
  diagScoreQuery,
  diagScoreExpected,
  diagScoreBtn,
  diagScoreResult
} from './dom.js';
import { escapeHtml, truncate } from './utils.js';

export function initRagDiagnostics() {
  if (!btnRagDiagnose || !diagModal) return;

  btnRagDiagnose.addEventListener('click', () => openDiagModal());
  diagModalClose?.addEventListener('click', () => closeDiagModal());
  diagModal.addEventListener('click', (e) => {
    if (e.target === diagModal) closeDiagModal();
  });

  // Tab switching
  diagTabs.forEach(tab => {
    tab.addEventListener('click', () => {
      diagTabs.forEach(t => t.classList.remove('active'));
      diagPanels.forEach(p => p.classList.remove('active'));
      tab.classList.add('active');
      const panel = document.getElementById(tab.dataset.panel);
      if (panel) panel.classList.add('active');
    });
  });

  diagHealthBtn?.addEventListener('click', runHealthCheck);
  diagWhyBtn?.addEventListener('click', runWhyMissing);
  diagScoreBtn?.addEventListener('click', runQueryScores);

  // Check RAG index status on init to enable/disable the button
  checkRagIndexStatus();
}

export function setDiagnoseEnabled(enabled) {
  if (btnRagDiagnose) btnRagDiagnose.disabled = !enabled;
}

async function checkRagIndexStatus() {
  try {
    const res = await fetch('/api/rag/status');
    const data = await res.json().catch(() => null);
    if (res.ok) setDiagnoseEnabled(!!data?.hasIndex);
  } catch (e) { /* ignore — button stays disabled */ }
}

function openDiagModal() {
  diagModal.classList.remove('hidden');
}

function closeDiagModal() {
  diagModal.classList.add('hidden');
}

// ── Health Check ─────────────────────────────────────────────
async function runHealthCheck() {
  if (!diagHealthResult) return;
  diagHealthBtn.disabled = true;
  diagHealthResult.innerHTML = renderLoading('Running health check...');

  try {
    const res = await fetch('/api/rag/diagnose/health-check');
    const data = await res.json().catch(() => null);
    if (!res.ok) throw new Error(data?.error || 'Health check failed.');

    diagHealthResult.innerHTML = renderHealthCheck(data);
  } catch (err) {
    diagHealthResult.innerHTML = renderError(err.message);
  } finally {
    diagHealthBtn.disabled = false;
  }
}

function renderHealthCheck(data) {
  const items = (data.items || []).map(item => {
    const icon = statusIcon(item.status);
    return `<div class="diag-item diag-item--${item.status}">${icon}<span class="diag-item-cat">${escapeHtml(item.category)}</span><span class="diag-item-msg">${escapeHtml(item.message)}</span></div>`;
  }).join('');

  return `
    <div class="diag-summary">
      <span>Collection: <strong>${escapeHtml(data.collection || 'default')}</strong></span>
      <span>Chunks: <strong>${data.totalChunks}</strong></span>
      ${data.hasWarnings ? '<span class="diag-badge diag-badge--warn">Issues Found</span>' : '<span class="diag-badge diag-badge--pass">Healthy</span>'}
    </div>
    <div class="diag-items">${items}</div>`;
}

// ── Why Missing ──────────────────────────────────────────────
async function runWhyMissing() {
  if (!diagWhyResult) return;
  const query = diagWhyQuery?.value?.trim();
  const expected = diagWhyExpected?.value?.trim();

  if (!query || !expected) {
    diagWhyResult.innerHTML = renderError('Both query and expected text are required.');
    return;
  }

  diagWhyBtn.disabled = true;
  diagWhyResult.innerHTML = renderLoading('Analyzing pipeline...');

  try {
    const res = await fetch('/api/rag/diagnose/why-missing', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ query, expectedText: expected })
    });
    const data = await res.json().catch(() => null);
    if (!res.ok) throw new Error(data?.error || 'Analysis failed.');

    diagWhyResult.innerHTML = renderWhyMissing(data);
  } catch (err) {
    diagWhyResult.innerHTML = renderError(err.message);
  } finally {
    diagWhyBtn.disabled = false;
  }
}

function renderWhyMissing(data) {
  const steps = (data.steps || []).map(step => {
    const icon = statusIcon(step.status);
    let html = `<div class="diag-step diag-step--${step.status}">
      <div class="diag-step-header">${icon}<strong>${escapeHtml(step.stepName)}</strong></div>
      <div class="diag-step-msg">${escapeHtml(step.message)}</div>`;
    if (step.suggestion) {
      html += `<div class="diag-step-suggestion">→ ${escapeHtml(step.suggestion)}</div>`;
    }
    html += '</div>';
    return html;
  }).join('');

  const suggestions = (data.suggestions || []).length
    ? `<div class="diag-suggestions">
        <div class="diag-suggestions-title">Suggested Actions</div>
        <ol>${data.suggestions.map(s => `<li>${escapeHtml(s)}</li>`).join('')}</ol>
       </div>`
    : '';

  const badge = data.hasIssues
    ? '<span class="diag-badge diag-badge--warn">Issues Found</span>'
    : '<span class="diag-badge diag-badge--pass">All Clear</span>';

  return `
    <div class="diag-summary">
      <span>Query: <strong>"${escapeHtml(truncate(data.query, 40))}"</strong></span>
      <span>Expected: <strong>"${escapeHtml(truncate(data.expectedText, 30))}"</strong></span>
      ${badge}
    </div>
    <div class="diag-steps">${steps}</div>
    ${suggestions}`;
}

// ── Query Scores ─────────────────────────────────────────────
async function runQueryScores() {
  if (!diagScoreResult) return;
  const query = diagScoreQuery?.value?.trim();
  if (!query) {
    diagScoreResult.innerHTML = renderError('Query is required.');
    return;
  }

  const expected = diagScoreExpected?.value?.trim() || null;

  diagScoreBtn.disabled = true;
  diagScoreResult.innerHTML = renderLoading('Scoring all chunks...');

  try {
    const res = await fetch('/api/rag/diagnose/query-scores', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ query, expectedText: expected })
    });
    const data = await res.json().catch(() => null);
    if (!res.ok) throw new Error(data?.error || 'Scoring failed.');

    diagScoreResult.innerHTML = renderQueryScores(data);
    wireScoreRowToggles();
  } catch (err) {
    diagScoreResult.innerHTML = renderError(err.message);
  } finally {
    diagScoreBtn.disabled = false;
  }
}

function renderQueryScores(data) {
  const results = data.results || [];
  if (!results.length) {
    return '<div class="diag-empty">No chunks found in index.</div>';
  }

  const topK = data.topK || 5;
  const minScore = data.minScore;
  const targetInfo = data.targetChunk
    ? `<div class="diag-target">Target chunk: Rank #${data.targetChunk.rank} · Score ${data.targetChunk.score.toFixed(4)} · ${data.targetChunk.isInTopK ? '✅ In TopK' : '❌ Outside TopK'} · ${data.targetChunk.passesMinScore ? '✅ Passes MinScore' : '❌ Filtered by MinScore'}</div>`
    : (data.expectedText ? '<div class="diag-target diag-target--miss">Target text not found in any chunk.</div>' : '');

  const rows = results.map(r => {
    const inTopK = r.rank <= topK;
    const passesMin = minScore == null || r.score >= minScore;
    const isTarget = r.containsText;
    const rowClass = isTarget ? 'diag-row--target' : (!inTopK ? 'diag-row--dim' : '');
    const scoreBar = Math.max(0, Math.min(100, r.score * 100));
    const fullContent = r.content || r.preview;
    const needsExpand = fullContent.length > 80;

    return `<tr class="${rowClass} diag-row-toggle" data-rank="${r.rank}">
      <td class="diag-rank">#${r.rank}</td>
      <td class="diag-score-cell">
        <div class="diag-score-bar" style="width:${scoreBar}%"></div>
        <span>${r.score.toFixed(4)}</span>
      </td>
      <td class="diag-flags">
        ${inTopK ? '' : '<span class="diag-flag diag-flag--warn" title="Outside TopK">⊘K</span>'}
        ${passesMin ? '' : '<span class="diag-flag diag-flag--warn" title="Below MinScore">⊘S</span>'}
        ${isTarget ? '<span class="diag-flag diag-flag--target" title="Contains target text">★</span>' : ''}
      </td>
      <td class="diag-preview">${escapeHtml(truncate(r.preview, 80))}${needsExpand ? ' <span class="diag-expand-hint">▸</span>' : ''}</td>
    </tr>
    <tr class="diag-detail-row" data-detail="${r.rank}" style="display:none">
      <td colspan="4"><div class="diag-detail-content">${escapeHtml(fullContent)}</div></td>
    </tr>`;
  }).join('');

  return `
    <div class="diag-summary">
      <span>Total: <strong>${data.totalScored}</strong> chunks</span>
      <span>TopK: <strong>${topK}</strong></span>
      ${minScore != null ? `<span>MinScore: <strong>${minScore}</strong></span>` : ''}
    </div>
    ${targetInfo}
    <div class="diag-scores-table-wrap">
      <table class="diag-scores-table">
        <thead><tr><th>Rank</th><th>Score</th><th></th><th>Preview</th></tr></thead>
        <tbody>${rows}</tbody>
      </table>
    </div>`;
}

function wireScoreRowToggles() {
  if (!diagScoreResult) return;
  diagScoreResult.querySelectorAll('.diag-row-toggle').forEach(row => {
    row.addEventListener('click', () => {
      const rank = row.dataset.rank;
      const detail = diagScoreResult.querySelector(`.diag-detail-row[data-detail="${rank}"]`);
      if (!detail) return;
      const isOpen = detail.style.display !== 'none';
      detail.style.display = isOpen ? 'none' : 'table-row';
      const hint = row.querySelector('.diag-expand-hint');
      if (hint) hint.textContent = isOpen ? '▸' : '▾';
      row.classList.toggle('diag-row--expanded', !isOpen);
    });
  });
}

// ── Shared Renderers ─────────────────────────────────────────
function statusIcon(status) {
  switch (status) {
    case 'pass': return '<span class="diag-icon diag-icon--pass">✓</span>';
    case 'warning': return '<span class="diag-icon diag-icon--warn">⚠</span>';
    case 'fail': return '<span class="diag-icon diag-icon--fail">✗</span>';
    default: return '<span class="diag-icon diag-icon--info">ℹ</span>';
  }
}

function renderLoading(msg) {
  return `<div class="diag-loading"><span class="diag-spinner"></span>${escapeHtml(msg || 'Loading...')}</div>`;
}

function renderError(msg) {
  return `<div class="diag-error">${escapeHtml(msg || 'An error occurred.')}</div>`;
}
