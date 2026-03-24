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
  const rerankedCandidates = ragInfo.rerankedCandidates || null;
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
      ${rewriteResult.keywords?.length ? `<div class="pipe-kv"><span class="pipe-label">Keywords</span><span class="pipe-value pipe-value--mono">${rewriteResult.keywords.map(k => escapeHtml(k)).join(', ')}</span></div>` : ''}
      ${ragInfo.rewriterModel ? `<div class="pipe-kv"><span class="pipe-label">Model</span><span class="pipe-value">${escapeHtml(ragInfo.rewriterModel)}</span></div>` : ''}
      ${diagnostics.rewriteElapsedMs != null ? `<div class="pipe-kv"><span class="pipe-label">Elapsed</span><span class="pipe-value">${diagnostics.rewriteElapsedMs.toLocaleString()} ms</span></div>` : ''}`;
  } else if (hasRewrite) {
    step2Badge = '<span class="pipe-badge pipe-badge--on">Rewritten</span>';
    step2Body = `
      <div class="pipe-kv"><span class="pipe-label">Rewritten Query</span><span class="pipe-value pipe-value--mono">${escapeHtml(ragInfo.rewrittenQuery)}</span></div>
      ${hasRewriter ? `<div class="pipe-kv"><span class="pipe-label">NeedsSearch</span><span class="pipe-value pipe-value--mono">true</span></div>` : ''}
      ${rewriteResult?.keywords?.length ? `<div class="pipe-kv"><span class="pipe-label">Keywords</span><span class="pipe-value pipe-value--mono">${rewriteResult.keywords.map(k => escapeHtml(k)).join(', ')}</span></div>` : ''}
      ${ragInfo.rewriterModel ? `<div class="pipe-kv"><span class="pipe-label">Model</span><span class="pipe-value">${escapeHtml(ragInfo.rewriterModel)}</span></div>` : ''}
      ${diagnostics.rewriteElapsedMs != null ? `<div class="pipe-kv"><span class="pipe-label">Elapsed</span><span class="pipe-value">${diagnostics.rewriteElapsedMs.toLocaleString()} ms</span></div>` : ''}`;
  } else if (hasRewriter) {
    step2Badge = '<span class="pipe-badge pipe-badge--on">Unchanged</span>';
    step2Body = `
      <div class="pipe-kv"><span class="pipe-label">Decision</span><span class="pipe-value">Query kept as-is (search proceeds with original query)</span></div>
      <div class="pipe-kv"><span class="pipe-label">Returned Query</span><span class="pipe-value pipe-value--mono">${escapeHtml(rewriteResult.query)}</span></div>
      <div class="pipe-kv"><span class="pipe-label">NeedsSearch</span><span class="pipe-value pipe-value--mono">true</span></div>
      ${rewriteResult.keywords?.length ? `<div class="pipe-kv"><span class="pipe-label">Keywords</span><span class="pipe-value pipe-value--mono">${rewriteResult.keywords.map(k => escapeHtml(k)).join(', ')}</span></div>` : ''}
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
  const searchModeLabel = searchMode === 'hybrid'
    ? 'Hybrid (Vector + BM25)'
    : searchMode === 'hybrid_dense_fallback'
      ? 'Dense Only (no keywords)'
      : 'Vector Only';
  const providerLabels = { inmemory: 'InMemory', postgres: 'PostgreSQL (pgvector)', qdrant: 'Qdrant', pinecone: 'Pinecone' };
  const providerLabel = providerLabels[vectorStoreProvider] || vectorStoreProvider;
  const retrievalRankById = new Map(retrievalResults.map((r, index) => [r.id ?? `retrieval-${index}`, index + 1]));

  // When rerankedCandidates is available, use it for Step 4 (all re-scored results before pipeline trimming)
  // Otherwise fall back to refs (final results) for backward compatibility
  const rerankSource = rerankedCandidates || refs;
  const allRerankedResults = rerankSource.map((r, index) => {
    const key = r.id ?? `reranked-${index}`;
    const previousRank = retrievalRankById.get(key) ?? null;
    return {
      ...r,
      currentRank: index + 1,
      previousRank,
      rankDelta: previousRank == null ? null : previousRank - (index + 1)
    };
  });

  // When WeightedBlend is active, compute blended scores for all candidates
  // so that both kept and dropped items show the correct final score.
  const finalSelectionMode = reranking.finalSelectionMode || 'RerankerOnly';
  const finalSelectionWeight = reranking.finalSelectionRetrievalWeight ?? 0.65;
  let allBlendedResults = allRerankedResults;
  if (finalSelectionMode === 'WeightedBlend' && rerankEnabled) {
    const retrievalScoreById = new Map(retrievalResults.map(r => [r.id, r.score ?? 0]));
    const clampedWeight = Math.max(0, Math.min(1, finalSelectionWeight));
    const rerankWeight = 1 - clampedWeight;
    allBlendedResults = allRerankedResults.map(r => {
      const retrievalScore = retrievalScoreById.get(r.id) ?? 0;
      const rerankScore = r.score ?? 0;
      return { ...r, score: (clampedWeight * retrievalScore) + (rerankWeight * rerankScore) };
    })
    .sort((a, b) => (b.score ?? 0) - (a.score ?? 0))
    .map((r, index) => ({ ...r, currentRank: index + 1 }));
  }

  // Items selected by final selection (topK + minScore)
  const finalSelectedIds = new Set(refs.map(r => r.id ?? `ref-${r.content}`));
  const keptByFinalSelection = allBlendedResults.filter(r => finalSelectedIds.has(r.id ?? `ref-${r.content}`));
  const droppedByFinalSelection = allBlendedResults.filter(r => !finalSelectedIds.has(r.id ?? `ref-${r.content}`));

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
          ${searchMode === 'hybrid_dense_fallback' ? `<div class="pipe-kv"><span class="pipe-label">Fallback Reason</span><span class="pipe-value">No keywords extracted by query rewriter</span></div>` : ''}
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
  const rerankProviderLabel = providerLabels[rerankProvider] || rerankProvider;
  const rerankAllTable = allRerankedResults.length
    ? renderPaginatedRerankComparisonTable(allRerankedResults, 'rerank-all')
    : '<div class="pipe-muted">No reranked results are available.</div>';

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
               <div class="pipe-kv"><span class="pipe-label">Input Candidates</span><span class="pipe-value">${escapeHtml(String(retrievalResults.length))} chunks</span></div>
               <div class="pipe-kv"><span class="pipe-label">Output (Re-scored)</span><span class="pipe-value">${escapeHtml(String(allRerankedResults.length))} chunks</span></div>
             </div>
             <div class="pipe-muted">The reranker re-scored and reordered all ${escapeHtml(String(retrievalResults.length))} retrieval candidates by relevance. No candidates are dropped at this step — trimming is done in the next Final Selection step.</div>`
          : `<div class="pipe-muted">Re-ranking was not applied. Retrieved results moved directly to the final selection step.</div>`}
        ${rerankEnabled
          ? `<div class="pipe-sub-title">All Re-scored Results (${allRerankedResults.length})</div>
             ${rerankAllTable}`
          : ''}
      </div>
    </div>`;

  // ── Step 5: Final Selection (TopK / MinScore) ──
  const passedRefs = refs.filter((r, i) => {
    const inTopK = (i + 1) <= finalTopK;
    const passesMin = finalMinScore == null || (r.score != null && r.score >= finalMinScore);
    return inTopK && passesMin;
  });
  const filteredTable = passedRefs.length
    ? renderPaginatedFilteringTable(passedRefs)
    : '<div class="pipe-muted">No chunks passed the final selection criteria.</div>';
  const droppedTable = (rerankEnabled && droppedByFinalSelection.length)
    ? renderPaginatedRerankComparisonTable(droppedByFinalSelection, 'final-dropped')
    : '';

  const step5Html = `
    <div class="pipe-step">
      <div class="pipe-step-header">
        <span class="pipe-step-num">5</span>
        <span class="pipe-step-title">Final Selection</span>
        <span class="pipe-badge pipe-badge--info">${passedRefs.length} / ${allBlendedResults.length} chunks</span>
      </div>
      <div class="pipe-step-body">
        <div class="pipe-kv-row">
          <div class="pipe-kv"><span class="pipe-label">Final Top K</span><span class="pipe-value">${escapeHtml(String(finalTopK))}</span></div>
          <div class="pipe-kv"><span class="pipe-label">Final Min Score</span><span class="pipe-value">${finalMinScore != null ? finalMinScore : 'None'}</span></div>
          ${rerankEnabled ? `<div class="pipe-kv"><span class="pipe-label">Dropped</span><span class="pipe-value">${escapeHtml(String(droppedByFinalSelection.length))} chunks</span></div>` : ''}
        </div>
        ${finalSelectionMode === 'WeightedBlend' && rerankEnabled
          ? `<div class="pipe-kv-row"><div class="pipe-kv"><span class="pipe-label">Selection Mode</span><span class="pipe-value">Weighted Blend (retrieval ${(finalSelectionWeight * 100).toFixed(0)}% · reranker ${((1 - finalSelectionWeight) * 100).toFixed(0)}%)</span></div></div>`
          : ''}
        <div class="pipe-muted">TopK and MinScore filters are applied here to select the final chunks sent to the prompt.</div>
        <div class="pipe-sub-title">Final Context Chunks Actually Sent to the Prompt (${passedRefs.length})</div>
        ${filteredTable}
        ${droppedTable ? `<div class="pipe-sub-title">Candidates Dropped by Final Selection (${droppedByFinalSelection.length})</div>${droppedTable}` : ''}
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
        <div class="pipe-header-actions">
          <button class="btn-secondary pipe-export-pdf-btn" title="Export as PDF">
            <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/><polyline points="14 2 14 8 20 8"/><line x1="12" y1="18" x2="12" y2="12"/><polyline points="9 15 12 18 15 15"/></svg>
            Export PDF
          </button>
          <button class="btn-icon rag-popup-close" title="Close">
            <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><line x1="18" y1="6" x2="6" y2="18"/><line x1="6" y1="6" x2="18" y2="18"/></svg>
          </button>
        </div>
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

  // PDF export handler
  const pdfBtn = overlay.querySelector('.pipe-export-pdf-btn');
  pdfBtn?.addEventListener('click', () => exportPipelinePdf(overlay, ragInfo));
}

// ── PDF Export ────────────────────────────────────────────────
async function exportPipelinePdf(overlay, ragInfo) {
  const btn = overlay.querySelector('.pipe-export-pdf-btn');
  if (!btn) return;

  if (typeof window.html2canvas !== 'function' || !window.jspdf?.jsPDF) {
    alert('PDF libraries not loaded. Check your network connection.');
    return;
  }

  const originalText = btn.innerHTML;
  btn.disabled = true;
  btn.innerHTML = '<span class="pipe-pdf-spinner"></span> Generating...';

  // ── Extract data from ragInfo ──
  const retrievalResults = ragInfo.retrievalResults || [];
  const rerankedCandidates = ragInfo.rerankedCandidates || null;
  const refs = ragInfo.references || [];
  const diagnostics = ragInfo.diagnostics || {};
  const finalTopK = diagnostics.finalTopK ?? '-';
  const retrievalTopK = diagnostics.retrievalTopK ?? finalTopK;
  const retrievalMinScore = diagnostics.appliedRetrievalMinScore;
  const finalMinScore = diagnostics.appliedFinalMinScore;
  const elapsedMs = diagnostics.elapsedMs ?? '-';
  const searchMode = ragInfo.searchMode || '-';
  const hybridWeight = ragInfo.hybridWeight;
  const vectorStoreProvider = ragInfo.vectorStoreProvider || '-';
  const reranking = ragInfo.reranking || {};
  const rerankEnabled = !!reranking.enabled;
  const rewriteResult = ragInfo.rewriteResult || null;
  const providerLabels = { inmemory: 'InMemory', postgres: 'PostgreSQL (pgvector)', qdrant: 'Qdrant', pinecone: 'Pinecone' };
  const searchModeLabels = { hybrid: 'Hybrid (Vector + BM25)', hybrid_dense_fallback: 'Dense Only (no keywords)', vector: 'Vector Only' };

  const finalSelectionMode = reranking.finalSelectionMode || 'RerankerOnly';
  const finalSelectionWeight = reranking.finalSelectionRetrievalWeight ?? 0.65;

  // Build reranked list with rank deltas
  const retrievalRankById = new Map(retrievalResults.map((r, i) => [r.id ?? `r-${i}`, i + 1]));
  const rerankSource = rerankedCandidates || refs;
  let allReranked = rerankSource.map((r, i) => {
    const prev = retrievalRankById.get(r.id ?? `r-${i}`) ?? null;
    return { ...r, currentRank: i + 1, previousRank: prev, rankDelta: prev == null ? null : prev - (i + 1) };
  });

  // WeightedBlend scoring
  if (finalSelectionMode === 'WeightedBlend' && rerankEnabled) {
    const retScoreById = new Map(retrievalResults.map(r => [r.id, r.score ?? 0]));
    const w = Math.max(0, Math.min(1, finalSelectionWeight));
    allReranked = allReranked.map(r => ({ ...r, score: w * (retScoreById.get(r.id) ?? 0) + (1 - w) * (r.score ?? 0) }))
      .sort((a, b) => (b.score ?? 0) - (a.score ?? 0))
      .map((r, i) => ({ ...r, currentRank: i + 1 }));
  }
  const finalIds = new Set(refs.map(r => r.id ?? r.content));
  const keptResults = allReranked.filter(r => finalIds.has(r.id ?? r.content));
  const droppedResults = allReranked.filter(r => !finalIds.has(r.id ?? r.content));

  // ── Build report HTML ──
  const esc = (s) => String(s ?? '').replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;');
  const trunc = (s, n) => { s = String(s || ''); return s.length > n ? s.slice(0, n) + '...' : s; };
  const now = new Date();

  function scoreBar(score) {
    const pct = Math.max(0, Math.min(100, (score || 0) * 100));
    return `<div style="display:flex;align-items:center;gap:6px">
      <div style="width:80px;height:8px;background:#e5e7eb;border-radius:4px;overflow:hidden">
        <div style="width:${pct}%;height:100%;background:${pct > 70 ? '#10b981' : pct > 40 ? '#f59e0b' : '#ef4444'};border-radius:4px"></div>
      </div>
      <span>${(score ?? 0).toFixed(4)}</span>
    </div>`;
  }

  function rankDeltaBadge(delta) {
    if (delta == null) return '<span style="color:#8b5cf6">New</span>';
    if (delta === 0) return '<span style="color:#6b7280">-</span>';
    if (delta > 0) return `<span style="color:#10b981">+${delta}</span>`;
    return `<span style="color:#ef4444">${delta}</span>`;
  }

  function resultTable(items, showRankDelta) {
    if (!items.length) return '<p style="color:#9ca3af;font-style:italic">No results.</p>';
    const hdrs = showRankDelta
      ? '<th>Rank</th><th>Score</th><th>Change</th><th>Content</th>'
      : '<th>Rank</th><th>Score</th><th>Content</th>';
    const rows = items.map(r => {
      const cols = showRankDelta
        ? `<td>#${r.currentRank ?? r.rank ?? '-'}</td><td>${scoreBar(r.score)}</td><td>${rankDeltaBadge(r.rankDelta)}</td><td>${esc(trunc(r.content, 120))}</td>`
        : `<td>#${r.currentRank ?? r.rank ?? '-'}</td><td>${scoreBar(r.score)}</td><td>${esc(trunc(r.content, 140))}</td>`;
      return `<tr>${cols}</tr>`;
    }).join('');
    return `<table><thead><tr>${hdrs}</tr></thead><tbody>${rows}</tbody></table>`;
  }

  // Query Rewrite status
  let rewriteStatus, rewriteBody;
  const hasRewrite = !!ragInfo.rewrittenQuery;
  const hasRewriter = rewriteResult != null;
  const gateSkipped = hasRewriter && rewriteResult.needsSearch === false;
  if (gateSkipped) {
    rewriteStatus = 'Search Gate: PASS (RAG skipped)';
    rewriteBody = `<tr><td>Decision</td><td>Search not needed</td></tr>`;
  } else if (hasRewrite) {
    rewriteStatus = 'Rewritten';
    rewriteBody = `<tr><td>Rewritten Query</td><td>${esc(ragInfo.rewrittenQuery)}</td></tr>`;
  } else if (hasRewriter) {
    rewriteStatus = 'Unchanged';
    rewriteBody = `<tr><td>Decision</td><td>Query kept as-is</td></tr>`;
  } else {
    rewriteStatus = 'Skipped';
    rewriteBody = `<tr><td colspan="2" style="color:#9ca3af">Query rewriter not configured</td></tr>`;
  }
  if (hasRewriter) {
    rewriteBody += `<tr><td>Returned Query</td><td>${esc(rewriteResult.query)}</td></tr>`;
    rewriteBody += `<tr><td>NeedsSearch</td><td>${rewriteResult.needsSearch ?? '-'}</td></tr>`;
    if (rewriteResult.keywords?.length) rewriteBody += `<tr><td>Keywords</td><td>${rewriteResult.keywords.map(esc).join(', ')}</td></tr>`;
  }
  if (ragInfo.rewriterModel) rewriteBody += `<tr><td>Model</td><td>${esc(ragInfo.rewriterModel)}</td></tr>`;
  if (diagnostics.rewriteElapsedMs != null) rewriteBody += `<tr><td>Elapsed</td><td>${diagnostics.rewriteElapsedMs.toLocaleString()} ms</td></tr>`;

  const reportHtml = `
    <div class="rpt">
      <style>
        .rpt { font-family: 'Inter', 'Segoe UI', sans-serif; color: #1e293b; line-height: 1.5; padding: 40px; width: 900px; }
        .rpt h1 { font-size: 22px; font-weight: 700; color: #4f46e5; margin: 0 0 4px; }
        .rpt .rpt-sub { font-size: 12px; color: #94a3b8; margin-bottom: 24px; }
        .rpt .rpt-query-box { background: #f8fafc; border: 1px solid #e2e8f0; border-radius: 8px; padding: 12px 16px; font-size: 15px; margin-bottom: 28px; }
        .rpt h2 { font-size: 16px; font-weight: 700; color: #1e293b; margin: 28px 0 6px; padding-bottom: 6px; border-bottom: 2px solid #e2e8f0; display: flex; align-items: center; gap: 8px; }
        .rpt h2 .ch { display: inline-flex; align-items: center; justify-content: center; width: 24px; height: 24px; border-radius: 50%; background: #4f46e5; color: #fff; font-size: 12px; font-weight: 700; flex-shrink: 0; }
        .rpt h2 .badge { font-size: 11px; font-weight: 600; padding: 2px 8px; border-radius: 10px; margin-left: 6px; }
        .rpt h2 .badge-on { background: #dbeafe; color: #2563eb; }
        .rpt h2 .badge-off { background: #f1f5f9; color: #94a3b8; }
        .rpt h2 .badge-info { background: #ede9fe; color: #7c3aed; }
        .rpt .kv-grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(200px, 1fr)); gap: 8px 20px; margin: 10px 0 14px; }
        .rpt .kv-grid dt { font-size: 10px; font-weight: 600; text-transform: uppercase; letter-spacing: 0.5px; color: #94a3b8; margin-bottom: 1px; }
        .rpt .kv-grid dd { font-size: 13px; color: #1e293b; margin: 0; font-weight: 500; }
        .rpt table { width: 100%; border-collapse: collapse; font-size: 12px; margin: 10px 0 6px; }
        .rpt thead th { text-align: left; font-size: 10px; text-transform: uppercase; letter-spacing: 0.5px; color: #94a3b8; padding: 6px 8px; border-bottom: 2px solid #e2e8f0; }
        .rpt tbody td { padding: 6px 8px; border-bottom: 1px solid #f1f5f9; vertical-align: top; }
        .rpt tbody tr:nth-child(even) { background: #f8fafc; }
        .rpt .note { font-size: 11px; color: #94a3b8; font-style: italic; margin: 6px 0 14px; }
        .rpt .kv-table { width: auto; margin: 10px 0; }
        .rpt .kv-table td:first-child { font-size: 11px; font-weight: 600; color: #64748b; padding-right: 20px; white-space: nowrap; width: 140px; }
        .rpt .kv-table td:last-child { font-size: 13px; color: #1e293b; }
        .rpt .prompt-box { background: #1e293b; color: #e2e8f0; font-family: 'Consolas','Courier New',monospace; font-size: 11px; padding: 14px 16px; border-radius: 8px; white-space: pre-wrap; word-break: break-all; margin: 10px 0; line-height: 1.6; }
        .rpt .section-label { font-size: 12px; font-weight: 600; color: #475569; margin: 14px 0 4px; }
        .rpt hr { border: none; border-top: 1px solid #e2e8f0; margin: 20px 0; }
      </style>

      <h1>RAG Pipeline Diagnostics Report</h1>
      <div class="rpt-sub">Generated: ${now.toLocaleString('ko-KR')} &nbsp;|&nbsp; Mythosia.AI</div>

      <div class="rpt-query-box">${esc(ragInfo.originalQuery || '(no query)')}</div>

      ${ragInfo.error ? `<div style="background:#fef2f2;border:1px solid #fecaca;border-radius:8px;padding:10px 14px;color:#dc2626;font-size:13px;margin-bottom:20px"><strong>Error:</strong> ${esc(ragInfo.error)}</div>` : ''}

      <!-- Ch1: Query -->
      <h2><span class="ch">1</span> Query</h2>
      <dl class="kv-grid">
        <div><dt>User Query</dt><dd>${esc(ragInfo.originalQuery || '-')}</dd></div>
      </dl>

      <!-- Ch2: Query Rewrite -->
      <h2><span class="ch">2</span> Query Rewrite <span class="badge ${gateSkipped || !hasRewriter ? 'badge-off' : 'badge-on'}">${esc(rewriteStatus)}</span></h2>
      <table class="kv-table"><tbody>${rewriteBody}</tbody></table>

      <!-- Ch3: Retrieval -->
      <h2><span class="ch">3</span> Retrieval <span class="badge badge-on">${esc(searchModeLabels[searchMode] || searchMode)}</span></h2>
      <dl class="kv-grid">
        <div><dt>Vector Store</dt><dd>${esc(providerLabels[vectorStoreProvider] || vectorStoreProvider)}</dd></div>
        <div><dt>Search Mode</dt><dd>${esc(searchModeLabels[searchMode] || searchMode)}</dd></div>
        ${searchMode === 'hybrid' && hybridWeight != null ? `<div><dt>Vector Weight</dt><dd>${hybridWeight.toFixed(2)}</dd></div>` : ''}
        <div><dt>Elapsed</dt><dd>${Number(elapsedMs).toLocaleString()} ms</dd></div>
        <div><dt>Retrieval Top K</dt><dd>${retrievalTopK}</dd></div>
        <div><dt>Search-Time Min Score</dt><dd>${retrievalMinScore ?? 'None'}</dd></div>
        <div><dt>Candidate Pool</dt><dd>${retrievalResults.length} chunks</dd></div>
      </dl>
      <p class="note">Top K used during retrieval. When re-ranking is enabled, this reflects the multiplier-expanded retrieval size.</p>
      <div class="section-label">Retrieved Candidate Chunks (${retrievalResults.length})</div>
      ${resultTable(retrievalResults.map((r, i) => ({ ...r, currentRank: i + 1 })), false)}

      <!-- Ch4: Re-ranking -->
      <h2><span class="ch">4</span> Re-ranking <span class="badge ${rerankEnabled ? 'badge-on' : 'badge-off'}">${rerankEnabled ? 'Active' : 'Skipped'}</span></h2>
      ${rerankEnabled ? `
        <dl class="kv-grid">
          <div><dt>Provider</dt><dd>${esc(reranking.provider || '-')}</dd></div>
          ${reranking.model ? `<div><dt>Model</dt><dd>${esc(reranking.model)}</dd></div>` : ''}
          ${reranking.retrievalMultiplier != null ? `<div><dt>Multiplier</dt><dd>${reranking.retrievalMultiplier}</dd></div>` : ''}
          <div><dt>Input Candidates</dt><dd>${retrievalResults.length} chunks</dd></div>
          <div><dt>Output (Re-scored)</dt><dd>${allReranked.length} chunks</dd></div>
        </dl>
        <p class="note">All candidates re-scored by relevance. No candidates are dropped here — trimming is done in Final Selection.</p>
        <div class="section-label">All Re-scored Results (${allReranked.length})</div>
        ${resultTable(allReranked, true)}
      ` : '<p class="note">Re-ranking was not applied. Results moved directly to final selection.</p>'}

      <!-- Ch5: Final Selection -->
      <h2><span class="ch">5</span> Final Selection <span class="badge badge-info">${keptResults.length} / ${allReranked.length || retrievalResults.length} chunks</span></h2>
      <dl class="kv-grid">
        <div><dt>Final Top K</dt><dd>${finalTopK}</dd></div>
        <div><dt>Final Min Score</dt><dd>${finalMinScore ?? 'None'}</dd></div>
        ${rerankEnabled ? `<div><dt>Dropped</dt><dd>${droppedResults.length} chunks</dd></div>` : ''}
        ${finalSelectionMode === 'WeightedBlend' && rerankEnabled ? `<div><dt>Selection Mode</dt><dd>Weighted Blend (retrieval ${(finalSelectionWeight * 100).toFixed(0)}% / reranker ${((1 - finalSelectionWeight) * 100).toFixed(0)}%)</dd></div>` : ''}
      </dl>
      <div class="section-label">Final Context Chunks Sent to Prompt (${keptResults.length})</div>
      ${resultTable(keptResults, rerankEnabled)}
      ${droppedResults.length ? `<div class="section-label">Dropped Candidates (${droppedResults.length})</div>${resultTable(droppedResults, true)}` : ''}

      <!-- Ch6: Final Prompt -->
      <h2><span class="ch">6</span> Final Prompt to LLM</h2>
      <div class="prompt-box">${esc(ragInfo.augmentedPrompt || '(no augmented prompt)')}</div>
    </div>`;

  // ── Render off-screen and capture ──
  const container = document.createElement('div');
  container.style.cssText = 'position:fixed;left:-9999px;top:0;z-index:-1;background:#fff;';
  container.innerHTML = reportHtml;
  document.body.appendChild(container);
  const rptEl = container.querySelector('.rpt');

  try {
    await new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r)));

    const canvas = await window.html2canvas(rptEl, {
      scale: 2,
      useCORS: true,
      backgroundColor: '#ffffff',
      logging: false,
      width: rptEl.scrollWidth,
      height: rptEl.scrollHeight
    });

    const imgW = canvas.width;
    const imgH = canvas.height;
    if (!imgW || !imgH) throw new Error('Canvas is empty.');

    const { jsPDF } = window.jspdf;
    const pw = 210, ph = 297, m = 10;
    const cw = pw - m * 2;
    const ch = (imgH * cw) / imgW;
    const pch = ph - m * 2;
    const pages = Math.ceil(ch / pch);

    const pdf = new jsPDF({ orientation: 'portrait', unit: 'mm', format: 'a4' });
    for (let i = 0; i < pages; i++) {
      if (i > 0) pdf.addPage();
      const sy = (i * pch / ch) * imgH;
      const sh = Math.min((pch / ch) * imgH, imgH - sy);
      const pc = document.createElement('canvas');
      pc.width = imgW;
      pc.height = Math.ceil(sh);
      const ctx = pc.getContext('2d');
      ctx.fillStyle = '#fff';
      ctx.fillRect(0, 0, pc.width, pc.height);
      ctx.drawImage(canvas, 0, Math.floor(sy), imgW, Math.ceil(sh), 0, 0, imgW, Math.ceil(sh));
      pdf.addImage(pc.toDataURL('image/png'), 'PNG', m, m, cw, (Math.ceil(sh) * cw) / imgW);
    }

    const ts = now.toISOString().replace(/[:.]/g, '-').slice(0, 19);
    const q = (ragInfo.originalQuery || 'query').slice(0, 30).replace(/[^a-zA-Z0-9가-힣]/g, '_');
    pdf.save(`RAG_Pipeline_Report_${q}_${ts}.pdf`);

    btn.innerHTML = '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><polyline points="20 6 9 17 4 12"/></svg> Saved!';
    setTimeout(() => { btn.innerHTML = originalText; btn.disabled = false; }, 2000);
  } catch (err) {
    console.error('PDF export failed:', err);
    btn.innerHTML = 'Export Failed';
    alert('PDF export failed: ' + (err.message || err));
    setTimeout(() => { btn.innerHTML = originalText; btn.disabled = false; }, 2000);
  } finally {
    container.remove();
  }
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
