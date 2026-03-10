// ═══════════════════════════════════════════════════════════════
// Code Viewer Modal
// ═══════════════════════════════════════════════════════════════

import { codeModal, codeModalContent, codeModalClose, codeCopyAll } from './dom.js';
import { escapeHtml, truncate } from './utils.js';

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

export function addViewCodeButton(msgDiv, userMessage, ragInfo) {
  const actions = document.createElement('div');
  actions.className = 'msg-actions';

  const btn = document.createElement('button');
  btn.className = 'msg-code-btn';
  btn.innerHTML = '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><polyline points="16 18 22 12 16 6"/><polyline points="8 6 2 12 8 18"/></svg> View Code';
  btn.addEventListener('click', () => openCodeModal(userMessage));
  actions.appendChild(btn);

  if (ragInfo) {
    const ragBtn = document.createElement('button');
    ragBtn.className = 'msg-rag-diagnose-btn';
    ragBtn.innerHTML = '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><circle cx="11" cy="11" r="8"/><line x1="21" y1="21" x2="16.65" y2="16.65"/></svg> RAG Diagnose';
    ragBtn.addEventListener('click', () => openRagDiagnosePopup(ragInfo));
    actions.appendChild(ragBtn);
  }

  msgDiv.appendChild(actions);
}

function openRagDiagnosePopup(ragInfo) {
  // Remove any existing popup
  const existing = document.getElementById('msg-rag-diagnose-modal');
  if (existing) existing.remove();

  const overlay = document.createElement('div');
  overlay.id = 'msg-rag-diagnose-modal';
  overlay.className = 'modal-overlay msg-rag-modal-overlay';

  const refs = ragInfo.references || [];
  const diagnostics = ragInfo.diagnostics || {};
  const appliedTopK = diagnostics.appliedTopK ?? 5;
  const appliedMinScore = diagnostics.appliedMinScore;
  const elapsedMs = diagnostics.elapsedMs ?? '-';
  const searchMode = ragInfo.searchMode || 'vector';
  const hybridWeight = ragInfo.hybridWeight;
  const vectorStoreProvider = ragInfo.vectorStoreProvider || 'inmemory';

  // ── Step 1: Query ──
  const step1Html = `
    <div class="pipe-step">
      <div class="pipe-step-header">
        <span class="pipe-step-num">1</span>
        <span class="pipe-step-title">Query</span>
      </div>
      <div class="pipe-step-body">
        <div class="pipe-kv"><span class="pipe-label">User Query</span><span class="pipe-value pipe-value--mono">${escapeHtml(ragInfo.originalQuery || '')}</span></div>
      </div>
    </div>`;

  // ── Step 2: Query Rewrite ──
  const hasRewrite = !!ragInfo.rewrittenQuery;
  const step2Html = `
    <div class="pipe-step ${hasRewrite ? '' : 'pipe-step--skipped'}">
      <div class="pipe-step-header">
        <span class="pipe-step-num">2</span>
        <span class="pipe-step-title">Query Rewrite</span>
        ${hasRewrite ? '<span class="pipe-badge pipe-badge--on">Active</span>' : '<span class="pipe-badge pipe-badge--off">Skipped</span>'}
      </div>
      <div class="pipe-step-body">
        ${hasRewrite
          ? `<div class="pipe-kv"><span class="pipe-label">Rewritten Query</span><span class="pipe-value pipe-value--mono">${escapeHtml(ragInfo.rewrittenQuery)}</span></div>
             ${ragInfo.rewriterModel ? `<div class="pipe-kv"><span class="pipe-label">Model</span><span class="pipe-value">${escapeHtml(ragInfo.rewriterModel)}</span></div>` : ''}`
          : `<div class="pipe-muted">Query rewriting was not applied. The original query was used directly.</div>`}
      </div>
    </div>`;

  // ── Step 3: Retrieval / Search ──
  const searchModeLabel = searchMode === 'hybrid' ? 'Hybrid (Vector + BM25)' : 'Vector Only';
  const providerLabels = { inmemory: 'InMemory', postgres: 'PostgreSQL (pgvector)', qdrant: 'Qdrant', pinecone: 'Pinecone' };
  const providerLabel = providerLabels[vectorStoreProvider] || vectorStoreProvider;

  const allScoreRows = refs.map((r, i) => {
    const score = (r.score != null) ? r.score.toFixed(4) : 'N/A';
    const barWidth = Math.max(0, Math.min(100, (r.score || 0) * 100));
    const preview = truncate(r.content || '', 100);
    const fullContent = r.content || '';
    const needsExpand = fullContent.length > 100;
    return `<tr class="pipe-score-row" data-idx="${i}">
      <td class="pipe-rank">#${i + 1}</td>
      <td class="pipe-score-cell">
        <div class="pipe-score-bar" style="width:${barWidth}%"></div>
        <span>${score}</span>
      </td>
      <td class="pipe-preview">${escapeHtml(preview)}${needsExpand ? ' <span class="pipe-expand-hint">▸</span>' : ''}</td>
    </tr>
    <tr class="pipe-detail-row" data-detail-idx="${i}" style="display:none">
      <td colspan="3"><div class="pipe-detail-content">${escapeHtml(fullContent)}</div></td>
    </tr>`;
  }).join('');

  const scoresTable = refs.length
    ? `<div class="pipe-scores-wrap">
        <table class="pipe-scores-table">
          <thead><tr><th>Rank</th><th>Score</th><th>Content</th></tr></thead>
          <tbody>${allScoreRows}</tbody>
        </table>
       </div>`
    : '<div class="pipe-muted">No results returned from the vector store.</div>';

  const step3Html = `
    <div class="pipe-step">
      <div class="pipe-step-header">
        <span class="pipe-step-num">3</span>
        <span class="pipe-step-title">Retrieval</span>
        <span class="pipe-badge pipe-badge--on">${escapeHtml(searchModeLabel)}</span>
      </div>
      <div class="pipe-step-body">
        <div class="pipe-kv-row">
          <div class="pipe-kv"><span class="pipe-label">Vector Store</span><span class="pipe-value">${escapeHtml(providerLabel)}</span></div>
          <div class="pipe-kv"><span class="pipe-label">Search Mode</span><span class="pipe-value">${escapeHtml(searchModeLabel)}</span></div>
          ${searchMode === 'hybrid' && hybridWeight != null ? `<div class="pipe-kv"><span class="pipe-label">Vector Weight</span><span class="pipe-value">${hybridWeight.toFixed(2)}</span></div>` : ''}
          <div class="pipe-kv"><span class="pipe-label">Elapsed</span><span class="pipe-value">${escapeHtml(String(elapsedMs))} ms</span></div>
        </div>
        <div class="pipe-sub-title">All Retrieved Chunks (${refs.length})</div>
        ${scoresTable}
      </div>
    </div>`;

  // ── Step 4: Filtering (TopK / MinScore) ──
  const passedRefs = refs.filter((r, i) => {
    const inTopK = (i + 1) <= appliedTopK;
    const passesMin = appliedMinScore == null || (r.score != null && r.score >= appliedMinScore);
    return inTopK && passesMin;
  });

  const filteredRows = passedRefs.map((r, i) => {
    const score = (r.score != null) ? r.score.toFixed(4) : 'N/A';
    const barWidth = Math.max(0, Math.min(100, (r.score || 0) * 100));
    const preview = truncate(r.content || '', 120);
    return `<tr>
      <td class="pipe-rank">#${i + 1}</td>
      <td class="pipe-score-cell">
        <div class="pipe-score-bar" style="width:${barWidth}%"></div>
        <span>${score}</span>
      </td>
      <td class="pipe-preview">${escapeHtml(preview)}</td>
    </tr>`;
  }).join('');

  const filteredTable = passedRefs.length
    ? `<div class="pipe-scores-wrap pipe-scores-wrap--short">
        <table class="pipe-scores-table">
          <thead><tr><th>Rank</th><th>Score</th><th>Content</th></tr></thead>
          <tbody>${filteredRows}</tbody>
        </table>
       </div>`
    : '<div class="pipe-muted">No chunks passed the filtering criteria.</div>';

  const step4Html = `
    <div class="pipe-step">
      <div class="pipe-step-header">
        <span class="pipe-step-num">4</span>
        <span class="pipe-step-title">Filtering</span>
        <span class="pipe-badge pipe-badge--info">${passedRefs.length} / ${refs.length} chunks</span>
      </div>
      <div class="pipe-step-body">
        <div class="pipe-kv-row">
          <div class="pipe-kv"><span class="pipe-label">Top K</span><span class="pipe-value">${escapeHtml(String(appliedTopK))}</span></div>
          <div class="pipe-kv"><span class="pipe-label">Min Score</span><span class="pipe-value">${appliedMinScore != null ? appliedMinScore : 'None'}</span></div>
        </div>
        <div class="pipe-sub-title">Final Context Chunks (${passedRefs.length})</div>
        ${filteredTable}
      </div>
    </div>`;

  // ── Step 5: Final Prompt ──
  const step5Html = `
    <div class="pipe-step">
      <div class="pipe-step-header">
        <span class="pipe-step-num">5</span>
        <span class="pipe-step-title">Final Prompt to LLM</span>
      </div>
      <div class="pipe-step-body">
        <div class="pipe-prompt-wrap">
          <pre class="pipe-prompt"><code>${escapeHtml(ragInfo.augmentedPrompt || '(no augmented prompt)')}</code></pre>
          <button class="pipe-copy-btn" title="Copy prompt">Copy</button>
        </div>
      </div>
    </div>`;

  overlay.innerHTML = `
    <div class="modal-card pipe-modal-card">
      <div class="modal-header">
        <h3>
          <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" style="vertical-align:-2px;margin-right:6px"><circle cx="11" cy="11" r="8"/><line x1="21" y1="21" x2="16.65" y2="16.65"/></svg>
          RAG Pipeline Diagnostics
        </h3>
        <button class="btn-icon rag-popup-close" title="Close">
          <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><line x1="18" y1="6" x2="6" y2="18"/><line x1="6" y1="6" x2="18" y2="18"/></svg>
        </button>
      </div>
      <div class="modal-body pipe-modal-body">
        <div class="pipe-timeline">
          ${step1Html}
          ${step2Html}
          ${step3Html}
          ${step4Html}
          ${step5Html}
        </div>
      </div>
    </div>`;

  document.body.appendChild(overlay);

  // Close handlers
  overlay.querySelector('.rag-popup-close').addEventListener('click', () => overlay.remove());
  overlay.addEventListener('click', (e) => {
    if (e.target === overlay) overlay.remove();
  });

  // Expand/collapse score rows
  overlay.querySelectorAll('.pipe-score-row').forEach(row => {
    row.addEventListener('click', () => {
      const idx = row.dataset.idx;
      const detail = overlay.querySelector(`.pipe-detail-row[data-detail-idx="${idx}"]`);
      if (!detail) return;
      const isOpen = detail.style.display !== 'none';
      detail.style.display = isOpen ? 'none' : 'table-row';
      const hint = row.querySelector('.pipe-expand-hint');
      if (hint) hint.textContent = isOpen ? '▸' : '▾';
    });
  });

  // Copy handler
  const copyBtn = overlay.querySelector('.pipe-copy-btn');
  copyBtn?.addEventListener('click', () => {
    navigator.clipboard.writeText(ragInfo.augmentedPrompt || '').then(() => {
      copyBtn.textContent = 'Copied!';
      setTimeout(() => copyBtn.textContent = 'Copy', 1500);
    });
  });
}

export function initCodeModal() {
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
}
