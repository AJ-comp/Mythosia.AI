// ═══════════════════════════════════════════════════════════════
// RAG Trace Rendering
// ═══════════════════════════════════════════════════════════════

import { ragViewCode } from './dom.js';
import { escapeHtml, truncate } from './utils.js';

export function renderTrace(ragTrace, trace) {
  const docIds = new Set(trace.documents.map((doc) => doc.id));
  const chunksByDoc = new Map();
  trace.chunks.forEach((chunk) => {
    if (!chunksByDoc.has(chunk.documentId)) {
      chunksByDoc.set(chunk.documentId, []);
    }
    chunksByDoc.get(chunk.documentId).push(chunk);
  });

  for (const list of chunksByDoc.values()) {
    list.sort((a, b) => a.index - b.index);
  }

  const embeddingsByChunk = new Map();
  trace.embeddings.forEach((emb) => {
    if (!embeddingsByChunk.has(emb.chunkId)) {
      embeddingsByChunk.set(emb.chunkId, []);
    }
    embeddingsByChunk.get(emb.chunkId).push(emb);
  });

  const recordsByChunk = new Map();
  trace.records.forEach((rec) => {
    if (!recordsByChunk.has(rec.id)) {
      recordsByChunk.set(rec.id, []);
    }
    recordsByChunk.get(rec.id).push(rec);
  });

  const docNodes = trace.documents
    .map((doc) => renderDocumentNode(doc, chunksByDoc.get(doc.id) || []))
    .join('');

  const orphanChunks = trace.chunks.filter((chunk) => !docIds.has(chunk.documentId));
  const orphanNodes = orphanChunks.length
    ? renderOrphanChunks(orphanChunks)
    : '';

  const chunkNodes = trace.chunks
    .map((chunk) => renderChunkNode(chunk))
    .join('');

  const embeddingNodes = trace.chunks
    .map((chunk) => renderEmbeddingGroupNode(chunk, embeddingsByChunk.get(chunk.id) || []))
    .join('');

  const recordNodes = trace.chunks
    .map((chunk) => renderRecordGroupNode(chunk, recordsByChunk.get(chunk.id) || []))
    .join('');

  const tabs = [
    { key: 'docs', label: 'Documents', count: trace.summary.documentCount, body: docNodes || '<div class="rag-empty">No documents.</div>' },
    { key: 'chunks', label: 'Chunks', count: trace.summary.chunkCount, body: chunkNodes || '<div class="rag-empty">No chunks.</div>' },
    { key: 'embeddings', label: 'Embeddings', count: trace.summary.embeddingCount, body: embeddingNodes || '<div class="rag-empty">No embeddings.</div>' },
    { key: 'vectors', label: 'Vector Table', count: trace.summary.recordCount, body: recordNodes || '<div class="rag-empty">No vector records.</div>' }
  ];

  const tabButtons = tabs
    .map((t, i) => `<button class="rag-trace-tab${i === 0 ? ' active' : ''}" data-panel="rag-tp-${t.key}" type="button">${t.label} <span class="rag-trace-tab-count">${t.count}</span></button>`)
    .join('');

  const tabPanels = tabs
    .map((t, i) => `<div class="rag-trace-panel${i === 0 ? ' active' : ''}" id="rag-tp-${t.key}"><div class="rag-tree">${t.body}</div></div>`)
    .join('');

  ragTrace.innerHTML = `
    <div class="rag-summary">
      <div class="rag-summary-stats">
        <div><strong>${trace.summary.documentCount}</strong> docs</div>
        <div><strong>${trace.summary.chunkCount}</strong> chunks</div>
        <div><strong>${trace.summary.embeddingCount}</strong> embeddings</div>
        <div><strong>${trace.summary.recordCount}</strong> records</div>
        <div><strong>${trace.summary.dimensions}</strong> dims</div>
      </div>
      <div class="rag-summary-actions" id="rag-summary-actions"></div>
    </div>
    <div class="rag-trace-tabs">${tabButtons}</div>
    <div class="rag-trace-panels">
      ${tabPanels}
      ${orphanNodes ? `<div class="rag-trace-orphans">${orphanNodes}</div>` : ''}
    </div>
  `;

  ragTrace.querySelectorAll('.rag-trace-tab').forEach((btn) => {
    btn.addEventListener('click', () => {
      ragTrace.querySelectorAll('.rag-trace-tab').forEach((b) => b.classList.remove('active'));
      ragTrace.querySelectorAll('.rag-trace-panel').forEach((p) => p.classList.remove('active'));
      btn.classList.add('active');
      const panel = ragTrace.querySelector(`#${btn.dataset.panel}`);
      if (panel) panel.classList.add('active');
    });
  });

  ragTrace.addEventListener('click', (e) => {
    const preview = e.target.closest('.rag-preview--clickable');
    if (preview && preview.dataset.fullContent) {
      openContentViewer(preview.dataset.fullTitle || '', preview.dataset.fullMeta || '', preview.dataset.fullContent);
    }
  });

  const summaryActions = ragTrace.querySelector('#rag-summary-actions');
  if (summaryActions && ragViewCode) {
    summaryActions.appendChild(ragViewCode);
  }
}

export function renderLoading() {
  return `
    <div class="rag-loading-card">
      <div class="rag-loading-card__header">
        <div>
          <div class="rag-loading-card__eyebrow">Reference Build</div>
          <div class="rag-loading-card__title">Preparing indexing pipeline…</div>
        </div>
        <div class="rag-loading-card__spinner" aria-hidden="true"></div>
      </div>
      <div class="rag-loading-card__body">
        <div class="rag-loading-card__meta">Initializing document processing.</div>
        <div class="rag-loading-steps">
          <div class="rag-loading-step is-active">
            <span class="rag-loading-step__dot"></span>
            <div>
              <div class="rag-loading-step__title">Preparing request</div>
              <div class="rag-loading-step__meta">Collecting files and pipeline settings</div>
            </div>
          </div>
          <div class="rag-loading-step">
            <span class="rag-loading-step__dot"></span>
            <div>
              <div class="rag-loading-step__title">Parsing documents</div>
              <div class="rag-loading-step__meta">Reading uploaded content and extracting text</div>
            </div>
          </div>
          <div class="rag-loading-step">
            <span class="rag-loading-step__dot"></span>
            <div>
              <div class="rag-loading-step__title">Chunking content</div>
              <div class="rag-loading-step__meta">Splitting documents into retrieval-ready chunks</div>
            </div>
          </div>
          <div class="rag-loading-step">
            <span class="rag-loading-step__dot"></span>
            <div>
              <div class="rag-loading-step__title">Generating embeddings</div>
              <div class="rag-loading-step__meta">Calling the embedding model for vector generation</div>
            </div>
          </div>
          <div class="rag-loading-step">
            <span class="rag-loading-step__dot"></span>
            <div>
              <div class="rag-loading-step__title">Writing vector records</div>
              <div class="rag-loading-step__meta">Persisting chunks and embeddings to the vector store</div>
            </div>
          </div>
        </div>
      </div>
    </div>`;
}

export function renderLoadingDetails(context) {
  const files = Array.isArray(context?.files) ? context.files : [];
  const primaryFile = files[0] || 'Untitled document';
  const extraFileCount = Math.max(0, files.length - 1);
  const sourceLabel = extraFileCount > 0
    ? `${primaryFile} +${extraFileCount} more`
    : primaryFile;
  const provider = context?.provider ? context.provider.toUpperCase() : 'UNSET';
  const model = context?.embeddingModel || 'UNSET';
  const chunker = context?.chunker || 'UNSET';
  const vectorStore = context?.vectorStore || 'UNSET';

  return `
    <div class="rag-loading-card">
      <div class="rag-loading-card__header">
        <div>
          <div class="rag-loading-card__eyebrow">Reference Build</div>
          <div class="rag-loading-card__title">Embedding documents for retrieval</div>
        </div>
        <div class="rag-loading-card__spinner" aria-hidden="true"></div>
      </div>
      <div class="rag-loading-card__body">
        <div class="rag-loading-card__meta">Processing <strong>${escapeHtml(sourceLabel)}</strong> with <strong>${escapeHtml(provider)}</strong> · <strong>${escapeHtml(model)}</strong></div>
        <div class="rag-loading-card__chips">
          <span class="rag-loading-chip">Chunking · ${escapeHtml(chunker)}</span>
          <span class="rag-loading-chip">Vector store · ${escapeHtml(vectorStore)}</span>
          <span class="rag-loading-chip">Files · ${files.length}</span>
        </div>
        <div class="rag-loading-steps">
          <div class="rag-loading-step is-active">
            <span class="rag-loading-step__dot"></span>
            <div>
              <div class="rag-loading-step__title">Loading input files</div>
              <div class="rag-loading-step__meta">Opening uploaded documents and preparing parser input</div>
            </div>
          </div>
          <div class="rag-loading-step is-pending">
            <span class="rag-loading-step__dot"></span>
            <div>
              <div class="rag-loading-step__title">Extracting document text</div>
              <div class="rag-loading-step__meta">Reading content from PDF, Office, Markdown, or text sources</div>
            </div>
          </div>
          <div class="rag-loading-step is-pending">
            <span class="rag-loading-step__dot"></span>
            <div>
              <div class="rag-loading-step__title">Chunking for retrieval</div>
              <div class="rag-loading-step__meta">Applying ${escapeHtml(chunker)} chunking and overlap settings</div>
            </div>
          </div>
          <div class="rag-loading-step is-pending">
            <span class="rag-loading-step__dot"></span>
            <div>
              <div class="rag-loading-step__title">Generating embeddings</div>
              <div class="rag-loading-step__meta">Sending chunks to ${escapeHtml(model)} for vector generation</div>
            </div>
          </div>
          <div class="rag-loading-step is-pending">
            <span class="rag-loading-step__dot"></span>
            <div>
              <div class="rag-loading-step__title">Persisting indexed records</div>
              <div class="rag-loading-step__meta">Writing embeddings and metadata into ${escapeHtml(vectorStore)}</div>
            </div>
          </div>
        </div>
      </div>
    </div>`;
}

export function renderError(message) {
  return `<div class="rag-empty rag-error">${escapeHtml(message)}</div>`;
}

// ── Internal helpers ─────────────────────────────────────────

function renderDocumentNode(doc, chunks) {
  const chunkNodes = chunks.map((chunk) => renderChunkNode(chunk)).join('');
  return `
    <details class="rag-tree-node" open>
      <summary>
        <div>
          <div class="rag-node-title">Document · ${escapeHtml(doc.source || doc.id)}</div>
          <div class="rag-node-meta">${doc.contentLength} chars · ${escapeHtml(doc.id)}</div>
        </div>
      </summary>
      <div class="rag-node-body">
        <div class="rag-preview">${escapeHtml(truncate(doc.preview, 220))}</div>
        ${renderMetadata(doc.metadata)}
      </div>
      <div class="rag-children">
        ${chunkNodes || '<div class="rag-empty">No chunks.</div>'}
      </div>
    </details>`;
}

function renderChunkNode(chunk) {
  const needsExpand = chunk.content && chunk.content.length > 180;
  return `
    <details class="rag-tree-node rag-tree-node--chunk" open>
      <summary>
        <div>
          <div class="rag-node-title">Chunk #${chunk.index}</div>
          <div class="rag-node-meta">${chunk.contentLength} chars · ${escapeHtml(chunk.id)}</div>
        </div>
      </summary>
      <div class="rag-node-body">
        <div class="rag-preview${needsExpand ? ' rag-preview--clickable' : ''}"${needsExpand ? ` data-full-title="Chunk #${chunk.index}" data-full-meta="${chunk.contentLength} chars · ${escapeHtml(chunk.id)}" data-full-content="${escapeHtml(chunk.content).replace(/"/g, '&quot;')}"` : ''}>${escapeHtml(truncate(chunk.content, 180))}</div>
        ${renderMetadata(chunk.metadata)}
      </div>
    </details>`;
}

function renderEmbeddingGroupNode(chunk, embeddings) {
  const embeddingContent = embeddings.length
    ? embeddings.map((emb) => renderEmbeddingNode(emb)).join('')
    : '<div class="rag-leaf rag-leaf--empty">No embeddings.</div>';

  return `
    <details class="rag-tree-node rag-tree-node--chunk" open>
      <summary>
        <div>
          <div class="rag-node-title">Chunk #${chunk.index}</div>
          <div class="rag-node-meta">${chunk.contentLength} chars · ${escapeHtml(chunk.id)}</div>
        </div>
      </summary>
      <div class="rag-children">
        ${embeddingContent}
      </div>
    </details>`;
}

function renderEmbeddingNode(emb) {
  const sample = emb.sample.map((v) => v.toFixed(3)).join(', ');
  const vectorText = emb.vector.map((v) => v.toFixed(6)).join(', ');
  return `
    <details class="rag-leaf rag-leaf--expand" open>
      <summary>
        <div>
          <div class="rag-node-title">Embedding</div>
          <div class="rag-node-meta">${emb.dimensions} dims · ${escapeHtml(emb.chunkId)}</div>
        </div>
      </summary>
      <div class="rag-node-body">
        <div class="rag-preview">[${sample}]</div>
        <details class="rag-vector" open>
          <summary>Full vector</summary>
          <div class="rag-vector-body">${escapeHtml(vectorText)}</div>
        </details>
      </div>
    </details>`;
}

function renderRecordGroupNode(chunk, records) {
  const recordContent = records.length
    ? records.map((rec) => renderRecordNode(rec)).join('')
    : '<div class="rag-leaf rag-leaf--empty">No vector records.</div>';

  return `
    <details class="rag-tree-node rag-tree-node--chunk" open>
      <summary>
        <div>
          <div class="rag-node-title">Chunk #${chunk.index}</div>
          <div class="rag-node-meta">${chunk.contentLength} chars · ${escapeHtml(chunk.id)}</div>
        </div>
      </summary>
      <div class="rag-children">
        ${recordContent}
      </div>
    </details>`;
}

function renderRecordNode(rec) {
  const needsExpand = rec.content && rec.content.length > 140;
  return `
    <div class="rag-leaf">
      <div class="rag-node-title">Vector Record</div>
      <div class="rag-node-meta">${rec.namespace || '(default)'} · ${rec.contentLength} chars</div>
      <div class="rag-preview${needsExpand ? ' rag-preview--clickable' : ''}"${needsExpand ? ` data-full-title="Vector Record" data-full-meta="${rec.namespace || '(default)'} · ${rec.contentLength} chars" data-full-content="${escapeHtml(rec.content).replace(/"/g, '&quot;')}"` : ''}>${escapeHtml(truncate(rec.content, 140))}</div>
      ${renderMetadata(rec.metadata)}
    </div>`;
}

function renderOrphanChunks(orphanChunks) {
  const nodes = orphanChunks
    .map((chunk) => renderChunkNode(chunk))
    .join('');

  return `
    <details class="rag-tree-node rag-tree-node--orphan" open>
      <summary>
        <div>
          <div class="rag-node-title">Unlinked Chunks</div>
          <div class="rag-node-meta">${orphanChunks.length} chunks</div>
        </div>
      </summary>
      <div class="rag-children">
        ${nodes || '<div class="rag-empty">No chunks.</div>'}
      </div>
    </details>`;
}

function renderMetadata(metadata) {
  const entries = Object.entries(metadata || {});
  if (entries.length === 0) return '';
  const rows = entries
    .map(([key, value]) => `<div class="rag-meta-row"><span>${escapeHtml(key)}</span><span>${escapeHtml(String(value))}</span></div>`)
    .join('');
  return `<div class="rag-meta">${rows}</div>`;
}

function openContentViewer(title, meta, content) {
  const existing = document.getElementById('rag-content-viewer');
  if (existing) existing.remove();

  const overlay = document.createElement('div');
  overlay.id = 'rag-content-viewer';
  overlay.className = 'modal-overlay';

  overlay.innerHTML = `
    <div class="modal-card content-viewer-card">
      <div class="modal-header">
        <div>
          <h3>${escapeHtml(title)}</h3>
          ${meta ? `<div class="content-viewer-meta">${escapeHtml(meta)}</div>` : ''}
        </div>
        <div style="display:flex;gap:6px;align-items:center">
          <button class="btn btn-ghost btn-sm" id="content-viewer-copy">Copy</button>
          <button class="btn-icon" id="content-viewer-close" title="Close">
            <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><line x1="18" y1="6" x2="6" y2="18"/><line x1="6" y1="6" x2="18" y2="18"/></svg>
          </button>
        </div>
      </div>
      <div class="modal-body content-viewer-body">
        <pre class="content-viewer-pre"><code class="content-viewer-code"></code></pre>
      </div>
    </div>
  `;

  document.body.appendChild(overlay);

  const codeEl = overlay.querySelector('.content-viewer-code');
  codeEl.textContent = content;

  overlay.querySelector('#content-viewer-close').addEventListener('click', () => overlay.remove());
  overlay.addEventListener('click', (e) => { if (e.target === overlay) overlay.remove(); });

  overlay.querySelector('#content-viewer-copy').addEventListener('click', (e) => {
    navigator.clipboard.writeText(content).then(() => {
      e.target.textContent = 'Copied!';
      setTimeout(() => { e.target.textContent = 'Copy'; }, 1500);
    });
  });
}
