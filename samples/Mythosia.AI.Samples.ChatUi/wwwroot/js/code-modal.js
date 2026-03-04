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
  const scoreRows = refs.map((r, i) => {
    const score = (r.score != null) ? r.score.toFixed(4) : 'N/A';
    const barWidth = Math.max(0, Math.min(100, (r.score || 0) * 100));
    const preview = truncate(r.content || '', 120);
    return `<tr>
      <td class="rag-popup-rank">#${i + 1}</td>
      <td class="rag-popup-score-cell">
        <div class="rag-popup-score-bar" style="width:${barWidth}%"></div>
        <span>${score}</span>
      </td>
      <td class="rag-popup-preview">${escapeHtml(preview)}</td>
    </tr>`;
  }).join('');

  const scoresHtml = refs.length
    ? `<div class="rag-popup-scores-wrap">
        <table class="rag-popup-scores-table">
          <thead><tr><th>Rank</th><th>Score</th><th>Content</th></tr></thead>
          <tbody>${scoreRows}</tbody>
        </table>
       </div>`
    : '<p class="rag-popup-empty">No references retrieved.</p>';

  overlay.innerHTML = `
    <div class="modal-card rag-popup-card">
      <div class="modal-header">
        <h3><svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" style="vertical-align:-2px;margin-right:6px"><circle cx="11" cy="11" r="8"/><line x1="21" y1="21" x2="16.65" y2="16.65"/></svg>RAG Diagnose</h3>
        <button class="btn-icon rag-popup-close" title="Close">
          <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><line x1="18" y1="6" x2="6" y2="18"/><line x1="6" y1="6" x2="18" y2="18"/></svg>
        </button>
      </div>
      <div class="modal-body rag-popup-body">
        <div class="rag-popup-section">
          <div class="rag-popup-section-title">
            <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M12 20h9"/><path d="M16.5 3.5a2.121 2.121 0 0 1 3 3L7 19l-4 1 1-4Z"/></svg>
            Query Scores
          </div>
          <div class="rag-popup-meta">
            <span>Original Query: <strong>"${escapeHtml(truncate(ragInfo.originalQuery || '', 60))}"</strong></span>
            <span>References: <strong>${refs.length}</strong></span>
          </div>
          ${scoresHtml}
        </div>
        <div class="rag-popup-section">
          <div class="rag-popup-section-title">
            <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8Z"/><polyline points="14 2 14 8 20 8"/></svg>
            Final Augmented Prompt
          </div>
          <div class="rag-popup-prompt-wrap">
            <pre class="rag-popup-prompt"><code>${escapeHtml(ragInfo.augmentedPrompt || '(no augmented prompt)')}</code></pre>
            <button class="rag-popup-copy-btn" title="Copy prompt">Copy</button>
          </div>
        </div>
      </div>
    </div>`;

  document.body.appendChild(overlay);

  // Close handlers
  overlay.querySelector('.rag-popup-close').addEventListener('click', () => overlay.remove());
  overlay.addEventListener('click', (e) => {
    if (e.target === overlay) overlay.remove();
  });

  // Copy handler
  const copyBtn = overlay.querySelector('.rag-popup-copy-btn');
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
