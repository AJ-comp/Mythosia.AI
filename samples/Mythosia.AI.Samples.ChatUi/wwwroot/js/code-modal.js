// ═══════════════════════════════════════════════════════════════
// Code Viewer Modal
// ═══════════════════════════════════════════════════════════════

import { codeModal, codeModalContent, codeModalClose, codeCopyAll } from './dom.js';
import { escapeHtml, truncate } from './utils.js';

const PIPE_PAGE_SIZE = 4;

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

function renderPaginatedRetrievalTable(refs) {
  const pages = chunkArray(refs, PIPE_PAGE_SIZE);
  const pageBodies = pages.map((page, pageIndex) => {
    const rows = page.map((r, itemIndex) => {
      const globalIndex = pageIndex * PIPE_PAGE_SIZE + itemIndex;
      const score = (r.score != null) ? r.score.toFixed(4) : 'N/A';
      const barWidth = Math.max(0, Math.min(100, (r.score || 0) * 100));
      const preview = truncate(r.content || '', 100);
      const fullContent = r.content || '';
      const needsExpand = fullContent.length > 100;
      return `<tr class="pipe-score-row" data-idx="${globalIndex}">
        <td class="pipe-rank">#${globalIndex + 1}</td>
        <td class="pipe-score-cell">
          <div class="pipe-score-bar" style="width:${barWidth}%"></div>
          <span>${score}</span>
        </td>
        <td class="pipe-preview">${escapeHtml(preview)}${needsExpand ? ' <span class="pipe-expand-hint">▸</span>' : ''}</td>
      </tr>
      <tr class="pipe-detail-row" data-detail-idx="${globalIndex}" style="display:none">
        <td colspan="3"><div class="pipe-detail-content">${escapeHtml(fullContent)}</div></td>
      </tr>`;
    }).join('');

    return `<tbody class="pipe-page-body${pageIndex === 0 ? '' : ' hidden'}" data-page="${pageIndex}">${rows}</tbody>`;
  }).join('');

  return renderPaginatedTableShell('retrieval', pages.length, pageBodies, false);
}

function renderPaginatedFilteringTable(refs) {
  const pages = chunkArray(refs, PIPE_PAGE_SIZE);
  const pageBodies = pages.map((page, pageIndex) => {
    const rows = page.map((r, itemIndex) => {
      const globalIndex = pageIndex * PIPE_PAGE_SIZE + itemIndex;
      const score = (r.score != null) ? r.score.toFixed(4) : 'N/A';
      const barWidth = Math.max(0, Math.min(100, (r.score || 0) * 100));
      const preview = truncate(r.content || '', 120);
      return `<tr>
        <td class="pipe-rank">#${globalIndex + 1}</td>
        <td class="pipe-score-cell">
          <div class="pipe-score-bar" style="width:${barWidth}%"></div>
          <span>${score}</span>
        </td>
        <td class="pipe-preview">${escapeHtml(preview)}</td>
      </tr>`;
    }).join('');

    return `<tbody class="pipe-page-body${pageIndex === 0 ? '' : ' hidden'}" data-page="${pageIndex}">${rows}</tbody>`;
  }).join('');

  return renderPaginatedTableShell('filtering', pages.length, pageBodies, true);
}

function renderPaginatedRerankComparisonTable(items, tableKey) {
  const pages = chunkArray(items, PIPE_PAGE_SIZE);
  const pageBodies = pages.map((page, pageIndex) => {
    const rows = page.map((item, itemIndex) => {
      const globalIndex = pageIndex * PIPE_PAGE_SIZE + itemIndex;
      const rankDelta = item.rankDelta == null
        ? 'New'
        : item.rankDelta === 0
          ? 'No change'
          : item.rankDelta > 0
            ? `↑ ${item.rankDelta}`
            : `↓ ${Math.abs(item.rankDelta)}`;
      const rankDeltaClass = item.rankDelta == null
        ? 'pipe-rank-delta--new'
        : item.rankDelta === 0
          ? 'pipe-rank-delta--same'
          : item.rankDelta > 0
            ? 'pipe-rank-delta--up'
            : 'pipe-rank-delta--down';
      const score = (item.score != null) ? item.score.toFixed(4) : 'N/A';
      const barWidth = Math.max(0, Math.min(100, (item.score || 0) * 100));
      const preview = truncate(item.content || '', 100);
      const fullContent = item.content || '';
      const needsExpand = fullContent.length > 100;
      return `<tr class="pipe-score-row" data-idx="${tableKey}-${globalIndex}">
        <td class="pipe-rank">#${item.currentRank}</td>
        <td class="pipe-score-cell">
          <div class="pipe-score-bar" style="width:${barWidth}%"></div>
          <span>${score}</span>
        </td>
        <td><span class="pipe-rank-delta ${rankDeltaClass}">${rankDelta}</span></td>
        <td class="pipe-preview">${escapeHtml(preview)}${needsExpand ? ' <span class="pipe-expand-hint">▸</span>' : ''}</td>
      </tr>
      <tr class="pipe-detail-row" data-detail-idx="${tableKey}-${globalIndex}" style="display:none">
        <td colspan="4"><div class="pipe-detail-content">${escapeHtml(fullContent)}</div></td>
      </tr>`;
    }).join('');

    return `<tbody class="pipe-page-body${pageIndex === 0 ? '' : ' hidden'}" data-page="${pageIndex}">${rows}</tbody>`;
  }).join('');

  return `<div class="pipe-scores-wrap pipe-scores-wrap--short" data-paged-table="${tableKey}" data-total-pages="${pages.length}" data-current-page="0">
      <table class="pipe-scores-table">
        <thead><tr><th>Rank</th><th>Score</th><th>Rank Change</th><th>Content</th></tr></thead>
        ${pageBodies}
      </table>
      ${pages.length > 1
        ? `<div class="pipe-pagination">
             <button class="btn-secondary pipe-page-btn" data-page-action="prev" data-table-key="${tableKey}">Previous</button>
             <span class="pipe-pagination-info" data-page-info="${tableKey}">1 / ${pages.length}</span>
             <button class="btn-secondary pipe-page-btn" data-page-action="next" data-table-key="${tableKey}">Next</button>
           </div>`
        : ''}
    </div>`;
}

function renderPaginatedTableShell(key, totalPages, bodyHtml, shortWrap) {
  return `<div class="pipe-scores-wrap${shortWrap ? ' pipe-scores-wrap--short' : ''}" data-paged-table="${key}" data-total-pages="${totalPages}" data-current-page="0">
      <table class="pipe-scores-table">
        <thead><tr><th>Rank</th><th>Score</th><th>Content</th></tr></thead>
        ${bodyHtml}
      </table>
      ${totalPages > 1
        ? `<div class="pipe-pagination">
             <button class="btn-secondary pipe-page-btn" data-page-action="prev" data-table-key="${key}">Previous</button>
             <span class="pipe-pagination-info" data-page-info="${key}">1 / ${totalPages}</span>
             <button class="btn-secondary pipe-page-btn" data-page-action="next" data-table-key="${key}">Next</button>
           </div>`
        : ''}
    </div>`;
}

function wirePaginatedTables(container) {
  container.querySelectorAll('[data-paged-table]').forEach(tableWrap => {
    updatePagedTable(tableWrap, 0);
  });

  container.querySelectorAll('.pipe-page-btn').forEach(btn => {
    btn.addEventListener('click', () => {
      const key = btn.dataset.tableKey;
      const action = btn.dataset.pageAction;
      const tableWrap = container.querySelector(`[data-paged-table="${key}"]`);
      if (!tableWrap) return;

      const currentPage = parseInt(tableWrap.dataset.currentPage || '0', 10);
      const nextPage = action === 'next' ? currentPage + 1 : currentPage - 1;
      updatePagedTable(tableWrap, nextPage);
    });
  });

  container.querySelectorAll('.pipe-score-row').forEach(row => {
    row.addEventListener('click', () => {
      const idx = row.dataset.idx;
      const detail = container.querySelector(`.pipe-detail-row[data-detail-idx="${idx}"]`);
      if (!detail) return;
      const isOpen = detail.style.display !== 'none';
      detail.style.display = isOpen ? 'none' : 'table-row';
      const hint = row.querySelector('.pipe-expand-hint');
      if (hint) hint.textContent = isOpen ? '▸' : '▾';
    });
  });
}

function updatePagedTable(tableWrap, pageIndex) {
  const totalPages = parseInt(tableWrap.dataset.totalPages || '1', 10);
  const clampedPage = Math.max(0, Math.min(totalPages - 1, pageIndex));
  tableWrap.dataset.currentPage = String(clampedPage);

  tableWrap.querySelectorAll('.pipe-page-body').forEach(body => {
    body.classList.toggle('hidden', body.dataset.page !== String(clampedPage));
  });

  const prevBtn = tableWrap.querySelector('[data-page-action="prev"]');
  const nextBtn = tableWrap.querySelector('[data-page-action="next"]');
  if (prevBtn) prevBtn.disabled = clampedPage <= 0;
  if (nextBtn) nextBtn.disabled = clampedPage >= totalPages - 1;

  const key = tableWrap.dataset.pagedTable;
  const info = key ? tableWrap.querySelector(`[data-page-info="${key}"]`) : null;
  if (info) info.textContent = `${clampedPage + 1} / ${totalPages}`;
}

function chunkArray(items, size) {
  const chunks = [];
  for (let i = 0; i < items.length; i += size) {
    chunks.push(items.slice(i, i + size));
  }
  return chunks;
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

  const retrievalResults = ragInfo.retrievalResults || [];
  const refs = ragInfo.references || [];
  const diagnostics = ragInfo.diagnostics || {};
  const finalTopK = diagnostics.finalTopK ?? 'UNSET';
  const retrievalTopK = diagnostics.retrievalTopK ?? finalTopK;
  const retrievalMinScore = diagnostics.appliedRetrievalMinScore;
  const finalMinScore = diagnostics.appliedFinalMinScore;
  const elapsedMs = diagnostics.elapsedMs ?? '-';
  const searchMode = ragInfo.searchMode || 'UNSET';
  const hybridWeight = ragInfo.hybridWeight;
  const vectorStoreProvider = ragInfo.vectorStoreProvider || 'UNSET';
  const reranking = ragInfo.reranking || {};
  const rerankEnabled = !!reranking.enabled;
  const rerankProvider = reranking.provider || 'UNSET';
  const rerankModel = reranking.model || null;
  const retrievalMultiplier = reranking.retrievalMultiplier;
  const ragError = ragInfo.error || null;

  // ── Error Banner (shown when RAG query failed) ──
  const errorBannerHtml = ragError
    ? `<div class="pipe-error-banner"><strong>RAG query failed</strong>${elapsedMs !== '-' ? ` (after ${Number(elapsedMs).toLocaleString()} ms)` : ''}:<br>${escapeHtml(ragError)}</div>`
    : '';

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
  const rewriteResult = ragInfo.rewriteResult || null;
  const hasRewriter = rewriteResult != null;
  const searchGatePassed = hasRewriter && rewriteResult.needsSearch === false;

  let step2Badge, step2Body;
  if (searchGatePassed) {
    step2Badge = '<span class="pipe-badge pipe-badge--off">Search Gate: PASS</span>';
    step2Body = `
      <div class="pipe-kv"><span class="pipe-label">Decision</span><span class="pipe-value" style="color:#e67e22;font-weight:600">Search not needed — RAG pipeline skipped</span></div>
      <div class="pipe-kv"><span class="pipe-label">Returned Query</span><span class="pipe-value pipe-value--mono">${escapeHtml(rewriteResult.query)}</span></div>
      <div class="pipe-kv"><span class="pipe-label">NeedsSearch</span><span class="pipe-value pipe-value--mono">false</span></div>
      ${ragInfo.rewriterModel ? `<div class="pipe-kv"><span class="pipe-label">Model</span><span class="pipe-value">${escapeHtml(ragInfo.rewriterModel)}</span></div>` : ''}
      ${diagnostics.rewriteElapsedMs != null ? `<div class="pipe-kv"><span class="pipe-label">Elapsed</span><span class="pipe-value">${diagnostics.rewriteElapsedMs.toLocaleString()} ms</span></div>` : ''}`;
  } else if (hasRewrite) {
    step2Badge = '<span class="pipe-badge pipe-badge--on">Rewritten</span>';
    step2Body = `
      <div class="pipe-kv"><span class="pipe-label">Rewritten Query</span><span class="pipe-value pipe-value--mono">${escapeHtml(ragInfo.rewrittenQuery)}</span></div>
      ${hasRewriter ? `<div class="pipe-kv"><span class="pipe-label">NeedsSearch</span><span class="pipe-value pipe-value--mono">true</span></div>` : ''}
      ${ragInfo.rewriterModel ? `<div class="pipe-kv"><span class="pipe-label">Model</span><span class="pipe-value">${escapeHtml(ragInfo.rewriterModel)}</span></div>` : ''}
      ${diagnostics.rewriteElapsedMs != null ? `<div class="pipe-kv"><span class="pipe-label">Elapsed</span><span class="pipe-value">${diagnostics.rewriteElapsedMs.toLocaleString()} ms</span></div>` : ''}`;
  } else if (hasRewriter) {
    step2Badge = '<span class="pipe-badge pipe-badge--on">Unchanged</span>';
    step2Body = `
      <div class="pipe-kv"><span class="pipe-label">Decision</span><span class="pipe-value">Query kept as-is (search proceeds with original query)</span></div>
      <div class="pipe-kv"><span class="pipe-label">Returned Query</span><span class="pipe-value pipe-value--mono">${escapeHtml(rewriteResult.query)}</span></div>
      <div class="pipe-kv"><span class="pipe-label">NeedsSearch</span><span class="pipe-value pipe-value--mono">true</span></div>
      ${ragInfo.rewriterModel ? `<div class="pipe-kv"><span class="pipe-label">Model</span><span class="pipe-value">${escapeHtml(ragInfo.rewriterModel)}</span></div>` : ''}
      ${diagnostics.rewriteElapsedMs != null ? `<div class="pipe-kv"><span class="pipe-label">Elapsed</span><span class="pipe-value">${diagnostics.rewriteElapsedMs.toLocaleString()} ms</span></div>` : ''}`;
  } else {
    step2Badge = '<span class="pipe-badge pipe-badge--off">Skipped</span>';
    step2Body = `<div class="pipe-muted">Query rewriter was not configured or no conversation history.</div>`;
  }

  const step2Html = `
    <div class="pipe-step ${searchGatePassed ? 'pipe-step--skipped' : ''}">
      <div class="pipe-step-header">
        <span class="pipe-step-num">2</span>
        <span class="pipe-step-title">Query Rewrite</span>
        ${step2Badge}
      </div>
      <div class="pipe-step-body">${step2Body}</div>
    </div>`;

  // ── Step 3: Retrieval / Search ──
  const searchModeLabel = searchMode === 'hybrid' ? 'Hybrid (Vector + BM25)' : 'Vector Only';
  const providerLabels = { inmemory: 'InMemory', postgres: 'PostgreSQL (pgvector)', qdrant: 'Qdrant', pinecone: 'Pinecone' };
  const providerLabel = providerLabels[vectorStoreProvider] || vectorStoreProvider;
  const retrievalRankById = new Map(retrievalResults.map((r, index) => [r.id ?? `retrieval-${index}`, index + 1]));
  const rerankedResults = refs.map((r, index) => {
    const key = r.id ?? `reference-${index}`;
    const previousRank = retrievalRankById.get(key) ?? null;
    return {
      ...r,
      currentRank: index + 1,
      previousRank,
      rankDelta: previousRank == null ? null : previousRank - (index + 1)
    };
  });
  const rerankedIds = new Set(rerankedResults.map(r => r.id ?? `${r.currentRank}-${r.content}`));
  const droppedCandidates = retrievalResults
    .filter((r, index) => !rerankedIds.has(r.id ?? `${index + 1}-${r.content}`))
    .map((r, index) => ({
      ...r,
      currentRank: (retrievalRankById.get(r.id ?? `retrieval-${index}`) ?? index + 1),
      previousRank: retrievalRankById.get(r.id ?? `retrieval-${index}`) ?? index + 1,
      rankDelta: -(retrievalRankById.get(r.id ?? `retrieval-${index}`) ?? index + 1)
    }));

  const scoresTable = retrievalResults.length
    ? renderPaginatedRetrievalTable(retrievalResults)
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
        <div class="pipe-kv-row">
          <div class="pipe-kv"><span class="pipe-label">Retrieval Top K</span><span class="pipe-value">${escapeHtml(String(retrievalTopK))}</span></div>
          <div class="pipe-kv"><span class="pipe-label">Search-Time Min Score</span><span class="pipe-value">${retrievalMinScore != null ? retrievalMinScore : 'None'}</span></div>
          <div class="pipe-kv"><span class="pipe-label">Pre-Rerank Candidate Pool</span><span class="pipe-value">${escapeHtml(String(retrievalTopK))} chunks</span></div>
        </div>
        <div class="pipe-muted">This is the Top K used during retrieval. When re-ranking is enabled, it reflects the multiplier-expanded retrieval size. When re-ranking is disabled, it is the same as the final Top K.</div>
        <div class="pipe-sub-title">Pre-Rerank Retrieved Candidate Chunks (${retrievalResults.length})</div>
        ${scoresTable}
      </div>
    </div>`;

  // ── Step 4: Re-ranking ──
  const passedRefs = refs.filter((r, i) => {
    const inTopK = (i + 1) <= finalTopK;
    const passesMin = finalMinScore == null || (r.score != null && r.score >= finalMinScore);
    return inTopK && passesMin;
  });
  const rerankProviderLabel = providerLabels[rerankProvider] || rerankProvider;
  const rerankSelectedTable = rerankedResults.length
    ? renderPaginatedRerankComparisonTable(rerankedResults, 'rerank-selected')
    : '<div class="pipe-muted">No reranked results are available.</div>';
  const rerankDroppedTable = droppedCandidates.length
    ? renderPaginatedRerankComparisonTable(droppedCandidates, 'rerank-dropped')
    : '<div class="pipe-muted">No candidates were dropped during re-ranking.</div>';

  const step4Html = `
    <div class="pipe-step ${rerankEnabled ? '' : 'pipe-step--skipped'}">
      <div class="pipe-step-header">
        <span class="pipe-step-num">4</span>
        <span class="pipe-step-title">Re-ranking</span>
        ${rerankEnabled ? '<span class="pipe-badge pipe-badge--on">Active</span>' : '<span class="pipe-badge pipe-badge--off">Skipped</span>'}
      </div>
      <div class="pipe-step-body">
        ${rerankEnabled
          ? `<div class="pipe-kv-row">
               <div class="pipe-kv"><span class="pipe-label">Provider</span><span class="pipe-value">${escapeHtml(rerankProviderLabel)}</span></div>
               ${rerankModel ? `<div class="pipe-kv"><span class="pipe-label">Model</span><span class="pipe-value">${escapeHtml(rerankModel)}</span></div>` : ''}
               ${retrievalMultiplier != null ? `<div class="pipe-kv"><span class="pipe-label">Multiplier</span><span class="pipe-value">${escapeHtml(String(retrievalMultiplier))}</span></div>` : ''}
             </div>
             <div class="pipe-kv-row">
               <div class="pipe-kv"><span class="pipe-label">Pre-Rerank Input Candidates</span><span class="pipe-value">${escapeHtml(String(retrievalTopK))} chunks</span></div>
               <div class="pipe-kv"><span class="pipe-label">Final Top K After Re-ranking</span><span class="pipe-value">${escapeHtml(String(finalTopK))} chunks</span></div>
               <div class="pipe-kv"><span class="pipe-label">Dropped by Re-ranking</span><span class="pipe-value">${escapeHtml(String(droppedCandidates.length))} chunks</span></div>
             </div>
             <div class="pipe-muted">The retriever first fetched ${escapeHtml(String(retrievalTopK))} candidates. Then the reranker reordered those candidates and kept the best ${escapeHtml(String(finalTopK))} results for the next step.</div>`
          : `<div class="pipe-muted">Re-ranking was not applied. Retrieved results moved directly to the final selection step.</div>`}
        ${rerankEnabled
          ? `<div class="pipe-sub-title">Reranked Results Kept for the Next Step (${rerankedResults.length})</div>
             ${rerankSelectedTable}
             <div class="pipe-sub-title">Candidates Dropped During Re-ranking (${droppedCandidates.length})</div>
             ${rerankDroppedTable}`
          : ''}
      </div>
    </div>`;

  // ── Step 5: Final Selection (TopK / MinScore) ──
  const filteredTable = passedRefs.length
    ? renderPaginatedFilteringTable(passedRefs)
    : '<div class="pipe-muted">No chunks passed the final selection criteria.</div>';

  const step5Html = `
    <div class="pipe-step">
      <div class="pipe-step-header">
        <span class="pipe-step-num">5</span>
        <span class="pipe-step-title">Final Selection</span>
        <span class="pipe-badge pipe-badge--info">${passedRefs.length} / ${refs.length} chunks</span>
      </div>
      <div class="pipe-step-body">
        <div class="pipe-kv-row">
          <div class="pipe-kv"><span class="pipe-label">Final Top K</span><span class="pipe-value">${escapeHtml(String(finalTopK))}</span></div>
          <div class="pipe-kv"><span class="pipe-label">Final Min Score Check</span><span class="pipe-value">${finalMinScore != null ? finalMinScore : 'None'}</span></div>
        </div>
        <div class="pipe-muted">Only the chunks that survived all earlier steps are shown here. These are the chunks that were finally used to build the prompt context.</div>
        <div class="pipe-sub-title">Final Context Chunks Actually Sent to the Prompt (${passedRefs.length})</div>
        ${filteredTable}
      </div>
    </div>`;
  // ── Step 6: Final Prompt ──
  const step6Html = `
    <div class="pipe-step">
      <div class="pipe-step-header">
        <span class="pipe-step-num">6</span>
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
        ${errorBannerHtml}
        <div class="pipe-timeline">
          ${step1Html}
          ${step2Html}
          ${step3Html}
          ${step4Html}
          ${step5Html}
          ${step6Html}
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
  wirePaginatedTables(overlay);

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
