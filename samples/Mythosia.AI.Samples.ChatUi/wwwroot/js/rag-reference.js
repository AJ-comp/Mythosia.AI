// ═══════════════════════════════════════════════════════════════
// RAG Reference Modal + Trace Viewer
// ═══════════════════════════════════════════════════════════════

import {
  btnDocReference,
  btnRagSettings,
  ragModal,
  ragModalClose,
  ragSettingsModal,
  ragSettingsClose,
  ragSettingsSave,
  ragSettingsStatus,
  ragFiles,
  ragFileList,
  ragChunkSize,
  ragChunkOverlap,
  ragChunker,
  ragEmbeddingProvider,
  ragEmbeddingModel,
  ragEmbeddingDimensions,
  ragEmbeddingBaseRow,
  ragEmbeddingBaseUrl,
  ragEmbeddingHint,
  ragOpenAiKey,
  ragOpenAiKeyInput,
  ragOpenAiKeySave,
  ragOpenAiKeyStatus,
  ragRun,
  ragViewCode,
  ragStatus,
  ragTrace,
  ragHistoryList,
  ragTopK,
  ragMinScore,
  ragPromptTemplate,
  ragChatStatus,
  codeModal,
  codeModalContent,
  codeCopyAll
} from './dom.js';
import { escapeHtml, truncate } from './utils.js';
import { providerKeys, saveKeysToStorage } from './state.js';
import { refreshProviderGroup } from './models.js';
import { setDiagnoseEnabled } from './rag-diagnostics.js';

export function initRagReference() {
  if (!btnDocReference || !ragModal) return;

  btnDocReference.addEventListener('click', () => openModal());
  btnRagSettings?.addEventListener('click', () => openSettingsModal());
  ragModalClose.addEventListener('click', () => closeModal());
  ragModal.addEventListener('click', (e) => {
    if (e.target === ragModal) closeModal();
  });
  ragSettingsClose?.addEventListener('click', () => closeSettingsModal());
  ragSettingsModal?.addEventListener('click', (e) => {
    if (e.target === ragSettingsModal) closeSettingsModal();
  });
  ragSettingsSave?.addEventListener('click', savePipelineSettings);

  ragFiles.addEventListener('change', updateFileList);
  ragEmbeddingProvider?.addEventListener('change', () => {
    updateEmbeddingUI();
    markReferenceStale();
  });
  ragEmbeddingModel?.addEventListener('input', markReferenceStale);
  ragEmbeddingDimensions?.addEventListener('input', markReferenceStale);
  ragEmbeddingBaseUrl?.addEventListener('input', markReferenceStale);
  ragTopK?.addEventListener('input', markReferenceStale);
  ragMinScore?.addEventListener('input', markReferenceStale);
  ragPromptTemplate?.addEventListener('input', markReferenceStale);
  ragRun.addEventListener('click', runReference);
  ragViewCode?.addEventListener('click', openRagCodeModal);
  ragChunkSize?.addEventListener('input', markReferenceStale);
  ragChunkOverlap?.addEventListener('input', markReferenceStale);
  ragChunker?.addEventListener('change', markReferenceStale);
  ragOpenAiKeyInput?.addEventListener('input', () => {
    if (ragOpenAiKeySave) {
      ragOpenAiKeySave.disabled = !ragOpenAiKeyInput.value.trim();
    }
  });
  ragOpenAiKeySave?.addEventListener('click', saveInlineOpenAiKey);

  updateFileList();
  updateEmbeddingUI();
  loadPipelineSettings();
  refreshRagStatus();
  refreshReferenceHistory();
}

function buildHistoryConfigLine(config) {
  if (!config) return '';
  const topK = config.topK ?? '-';
  const minScore = config.minScore ?? '-';
  const chunkSize = config.chunkSize ?? '-';
  const overlap = config.chunkOverlap ?? '-';
  const chunker = config.chunker ? config.chunker.toString().toUpperCase() : 'N/A';
  const embed = config.embeddingProvider ? config.embeddingProvider.toString().toUpperCase() : 'N/A';
  const model = config.embeddingModel || 'N/A';
  const template = config.promptTemplate ? 'Template' : 'Default prompt';

  return `TopK ${topK} · MinScore ${minScore} · Chunk ${chunkSize}/${overlap} · ${chunker} · ${embed}:${model} · ${template}`;
}

async function loadPipelineSettings() {
  if (!ragChunkSize && !ragSettingsModal) return;
  try {
    const res = await fetch('/api/rag/pipeline-settings');
    const payload = await res.json().catch(() => null);
    if (!res.ok) throw new Error(payload?.error || 'Failed to load settings.');

    applyPipelineSettings(payload || {});
    refreshRagStatus(payload);
  } catch (err) {
    if (ragSettingsStatus) {
      ragSettingsStatus.textContent = err.message || 'Failed to load settings.';
    }
    showRagStatusError(err);
  }
}

function applyPipelineSettings(settings) {
  if (ragChunkSize && settings.chunkSize) ragChunkSize.value = settings.chunkSize;
  if (ragChunkOverlap && settings.chunkOverlap) ragChunkOverlap.value = settings.chunkOverlap;
  if (ragChunker && settings.chunker && !autoChunkerFromFiles) setSelectValue(ragChunker, settings.chunker);
  if (ragEmbeddingProvider && settings.embeddingProvider) setSelectValue(ragEmbeddingProvider, settings.embeddingProvider);
  if (ragEmbeddingModel && settings.embeddingModel) ragEmbeddingModel.value = settings.embeddingModel;
  if (ragEmbeddingDimensions && settings.embeddingDimensions) ragEmbeddingDimensions.value = settings.embeddingDimensions;
  if (ragEmbeddingBaseUrl && settings.embeddingBaseUrl) ragEmbeddingBaseUrl.value = settings.embeddingBaseUrl;
  if (ragTopK && settings.topK) ragTopK.value = settings.topK;
  if (ragMinScore) ragMinScore.value = settings.minScore ?? '';
  if (ragPromptTemplate) ragPromptTemplate.value = settings.promptTemplate ?? '';

  updateEmbeddingUI();
}

async function savePipelineSettings() {
  if (!ragSettingsSave) return;
  ragSettingsSave.disabled = true;
  if (ragSettingsStatus) ragSettingsStatus.textContent = 'Saving...';

  const payload = {
    chunkSize: toInt(ragChunkSize?.value, 300),
    chunkOverlap: toInt(ragChunkOverlap?.value, 30),
    chunker: ragChunker?.value || 'character',
    embeddingProvider: ragEmbeddingProvider?.value || 'local',
    embeddingModel: ragEmbeddingModel?.value?.trim() || '',
    embeddingDimensions: toInt(ragEmbeddingDimensions?.value, 1024),
    embeddingBaseUrl: ragEmbeddingBaseUrl?.value?.trim() || '',
    topK: toInt(ragTopK?.value, 3),
    minScore: toFloatOrNull(ragMinScore?.value),
    promptTemplate: ragPromptTemplate?.value?.trim() || null
  };

  try {
    const res = await fetch('/api/rag/pipeline-settings', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(payload)
    });
    const data = await res.json().catch(() => null);
    if (!res.ok) throw new Error(data?.error || 'Failed to save settings.');

    if (ragSettingsStatus) ragSettingsStatus.textContent = 'Settings saved.';
    refreshRagStatus(data);
  } catch (err) {
    if (ragSettingsStatus) ragSettingsStatus.textContent = err.message || 'Failed to save settings.';
    showRagStatusError(err);
  } finally {
    ragSettingsSave.disabled = false;
  }
}

function setSelectValue(select, value) {
  if (!select) return;
  select.value = value;
  select.dispatchEvent(new Event('change', { bubbles: true }));
}

function toInt(value, fallback) {
  const parsed = parseInt(value, 10);
  return Number.isFinite(parsed) && parsed > 0 ? parsed : fallback;
}

function toFloatOrNull(value) {
  if (!value) return null;
  const parsed = parseFloat(value);
  return Number.isFinite(parsed) ? parsed : null;
}

function openModal() {
  ragModal.classList.remove('hidden');
  updateEmbeddingUI();
  setViewCodeEnabled(hasReferenceRun);
  refreshReferenceHistory();
}

function openSettingsModal() {
  if (!ragSettingsModal) return;
  ragSettingsModal.classList.remove('hidden');
  loadPipelineSettings();
}

function closeSettingsModal() {
  ragSettingsModal?.classList.add('hidden');
}

function closeModal() {
  ragModal.classList.add('hidden');
}

function updateFileList() {
  markReferenceStale();
  const files = Array.from(ragFiles.files || []);
  updateRunState(files);

  if (files.length > 0 && ragChunker) {
    const hasNonTxt = files.some((f) => !f.name.toLowerCase().endsWith('.txt'));
    setSelectValue(ragChunker, hasNonTxt ? 'markdown' : 'recursive');
    autoChunkerFromFiles = true;
  } else {
    autoChunkerFromFiles = false;
  }

  if (!ragFileList) return;
  if (files.length === 0) {
    ragFileList.innerHTML = '<div class="rag-empty">No files selected.</div>';
    return;
  }

  ragFileList.innerHTML = files
    .map((file) => {
      const size = `${Math.max(1, Math.round(file.size / 1024))} KB`;
      return `<div class="rag-file-row"><span>${escapeHtml(file.name)}</span><span class="rag-file-size">${size}</span></div>`;
    })
    .join('');
}

function getSelectedEmbeddingProvider() {
  return ragEmbeddingProvider?.value || 'local';
}

function updateEmbeddingUI() {
  const provider = getSelectedEmbeddingProvider();
  const hasOpenAiKey = !!providerKeys?.OpenAI;

  if (ragOpenAiKey) {
    ragOpenAiKey.classList.toggle('hidden', provider !== 'openai' || hasOpenAiKey);
  }

  if (ragEmbeddingBaseRow) {
    ragEmbeddingBaseRow.classList.toggle('hidden', provider !== 'ollama');
  }

  if (ragOpenAiKeyInput && provider !== 'openai') {
    ragOpenAiKeyInput.value = '';
  }

  if (ragOpenAiKeyStatus) {
    ragOpenAiKeyStatus.textContent = hasOpenAiKey
      ? 'OpenAI key already saved in localStorage.'
      : 'Stored in localStorage for this browser.';
  }

  if (ragEmbeddingHint) {
    if (provider === 'ollama') {
      ragEmbeddingHint.textContent = 'Ollama must be installed and running on the base URL below.';
    } else if (provider === 'openai') {
      ragEmbeddingHint.textContent = hasOpenAiKey
        ? 'Using stored OpenAI API key.'
        : 'OpenAI API key required. Enter it below.';
    } else {
      ragEmbeddingHint.textContent = 'Local hashing embeddings (no key required).';
    }
  }

  updateRunState();
}

function updateRunState(files) {
  const fileCount = files ? files.length : (ragFiles.files ? ragFiles.files.length : 0);
  const provider = getSelectedEmbeddingProvider();
  const needsKey = provider === 'openai';
  const hasKey = !needsKey || !!providerKeys?.OpenAI;
  ragRun.disabled = fileCount === 0 || !hasKey;
}

async function runReference() {
  const files = Array.from(ragFiles.files || []);
  if (files.length === 0) return;

  setViewCodeEnabled(false);

  const provider = getSelectedEmbeddingProvider();
  const openAiKey = providerKeys?.OpenAI;
  if (provider === 'openai' && !openAiKey) {
    ragStatus.textContent = 'OpenAI API key is required.';
    ragTrace.innerHTML = renderError('OpenAI API key is required.');
    updateRunState(files);
    return;
  }

  ragRun.disabled = true;
  ragStatus.textContent = 'Indexing documents...';
  ragTrace.innerHTML = renderLoading();

  const formData = new FormData();
  files.forEach((file) => formData.append('files', file));
  if (ragChunkSize) formData.append('chunkSize', ragChunkSize.value || '300');
  if (ragChunkOverlap) formData.append('chunkOverlap', ragChunkOverlap.value || '30');
  if (ragChunker) formData.append('chunker', ragChunker.value || 'character');
  formData.append('embeddingProvider', provider);
  if (ragEmbeddingModel) formData.append('embeddingModel', ragEmbeddingModel.value || '');
  if (ragEmbeddingDimensions) formData.append('embeddingDimensions', ragEmbeddingDimensions.value || '');
  if (ragEmbeddingBaseUrl) formData.append('embeddingBaseUrl', ragEmbeddingBaseUrl.value || '');
  if (ragTopK) formData.append('topK', ragTopK.value || '');
  if (ragMinScore) formData.append('minScore', ragMinScore.value || '');
  if (ragPromptTemplate) formData.append('promptTemplate', ragPromptTemplate.value || '');
  if (provider === 'openai' && openAiKey) {
    formData.append('openaiApiKey', openAiKey);
  }

  try {
    const res = await fetch('/api/rag/reference', { method: 'POST', body: formData });
    const payload = await res.json().catch(() => null);

    if (!res.ok) {
      const message = payload?.error || 'Failed to build RAG reference.';
      ragStatus.textContent = message;
      ragTrace.innerHTML = renderError(message);
      ragRun.disabled = false;
      return;
    }

    ragStatus.textContent = `Ready · ${payload.summary.documentCount} docs · ${payload.summary.chunkCount} chunks`;
    renderTrace(payload);
    setViewCodeEnabled(true);
    setDiagnoseEnabled(true);
    refreshRagStatus();
    refreshReferenceHistory();
  } catch (err) {
    ragStatus.textContent = 'Network error.';
    ragTrace.innerHTML = renderError(err.message || 'Network error');
    showRagStatusError(err);
  } finally {
    updateRunState(files);
  }
}

async function refreshRagStatus(settingsOverride) {
  if (!ragChatStatus) return;
  if (settingsOverride) {
    applyRagStatus(settingsOverride, true);
    return;
  }

  try {
    const res = await fetch('/api/rag/status');
    const payload = await res.json().catch(() => null);
    if (!res.ok) throw new Error(payload?.error || 'Failed to load RAG status.');

    applyRagStatus(payload?.settings || {}, payload?.hasIndex);
    setDiagnoseEnabled(!!payload?.hasIndex);
  } catch (err) {
    showRagStatusError(err);
  }
}

function applyRagStatus(settings, hasIndex) {
  if (!ragChatStatus) return;
  const provider = (settings.embeddingProvider || 'local').toUpperCase();
  const topK = settings.topK ?? 0;
  const minScore = settings.minScore ?? '-';
  const chunker = settings.chunker ? settings.chunker.toUpperCase() : 'N/A';
  const statusLabel = hasIndex ? 'RAG: READY' : 'RAG: NOT INDEXED';

  ragChatStatus.textContent = `${statusLabel} · TopK=${topK} · MinScore=${minScore} · ${provider} · ${chunker}`;
  ragChatStatus.classList.toggle('active', !!hasIndex);
  ragChatStatus.classList.remove('error');
}

function showRagStatusError(err) {
  if (!ragChatStatus) return;
  ragChatStatus.textContent = `RAG: ERROR · ${err.message || 'Status unavailable'}`;
  ragChatStatus.classList.add('error');
  ragChatStatus.classList.remove('active');
}

async function refreshReferenceHistory() {
  if (!ragHistoryList) return;
  try {
    const res = await fetch('/api/rag/reference-history');
    const payload = await res.json().catch(() => null);
    if (!res.ok) {
      throw new Error(payload?.error || 'Failed to load history.');
    }

    const history = payload?.history || [];
    if (!history.length) {
      ragHistoryList.innerHTML = '<div class="rag-empty">No references yet.</div>';
      return;
    }

    ragHistoryList.innerHTML = history
    .map((entry) => {
      const sources = Array.isArray(entry.sources) && entry.sources.length
        ? entry.sources.join(', ')
        : 'Untitled';
      const createdAt = entry.createdAt ? new Date(entry.createdAt).toLocaleString() : 'Unknown time';
      const summary = entry.summary
        ? `${entry.summary.documentCount} docs · ${entry.summary.chunkCount} chunks`
        : '';
      const config = entry.config || {};
      const configLine = buildHistoryConfigLine(config);
      return `
        <div class="rag-history-item">
          <div class="rag-history-title-row">
            <span class="rag-history-sources">${escapeHtml(sources)}</span>
            <span class="rag-history-time">${escapeHtml(createdAt)}</span>
          </div>
          <div class="rag-history-meta">${escapeHtml(summary)}</div>
          ${configLine ? `<div class="rag-history-config">${escapeHtml(configLine)}</div>` : ''}
        </div>
      `;
    })
    .join('');
  } catch (err) {
    ragHistoryList.innerHTML = `<div class="rag-empty">${escapeHtml(err.message || 'Failed to load history.')}</div>`;
  }
}

function openRagCodeModal() {
  if (!codeModal || !codeModalContent) return;
  codeModal.classList.remove('hidden');
  codeModalContent.textContent = 'Loading...';
  if (codeCopyAll) codeCopyAll.textContent = 'Copy';

  fetch('/api/rag/code-snippet')
    .then(async (res) => {
      const data = await res.json().catch(() => null);
      if (!res.ok) {
        throw new Error(data?.error || 'Failed to load code snippet.');
      }
      return data;
    })
    .then((data) => {
      codeModalContent.textContent = data?.code || 'No code available';
      if (window.hljs) {
        delete codeModalContent.dataset.highlighted;
        window.hljs.highlightElement(codeModalContent);
      }
    })
    .catch((err) => {
      codeModalContent.textContent = `// ${err.message || 'Failed to load code snippet.'}`;
    });
}

let hasReferenceRun = false;
let autoChunkerFromFiles = false;

function setViewCodeEnabled(enabled) {
  hasReferenceRun = enabled;
  if (ragViewCode) {
    ragViewCode.disabled = !enabled;
  }
}

function markReferenceStale() {
  setViewCodeEnabled(false);
}

function saveInlineOpenAiKey() {
  if (!ragOpenAiKeyInput) return;
  const key = ragOpenAiKeyInput.value.trim();
  if (!key) return;

  providerKeys.OpenAI = key;
  saveKeysToStorage();
  refreshProviderGroup('OpenAI');

  if (ragOpenAiKeyStatus) {
    ragOpenAiKeyStatus.textContent = 'Key saved for OpenAI (localStorage).';
  }
  if (ragOpenAiKeySave) {
    ragOpenAiKeySave.disabled = true;
  }

  updateEmbeddingUI();
}

function renderTrace(trace) {
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
    <div class="rag-tree">
      ${renderTopSection('Documents', docNodes || '<div class="rag-empty">No documents.</div>')}
      ${renderTopSection('Chunks', chunkNodes || '<div class="rag-empty">No chunks.</div>')}
      ${renderTopSection('Embeddings', embeddingNodes || '<div class="rag-empty">No embeddings.</div>')}
      ${renderTopSection('Vector Table', recordNodes || '<div class="rag-empty">No vector records.</div>')}
      ${orphanNodes}
    </div>
  `;

  const summaryActions = ragTrace.querySelector('#rag-summary-actions');
  if (summaryActions && ragViewCode) {
    summaryActions.appendChild(ragViewCode);
  }
}

function renderTopSection(title, body) {
  return `
    <details class="rag-tree-section" open>
      <summary>
        <div>
          <div class="rag-node-title">${title}</div>
          <div class="rag-node-meta">Click to collapse</div>
        </div>
      </summary>
      <div class="rag-children">${body}</div>
    </details>`;
}

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
  return `
    <details class="rag-tree-node rag-tree-node--chunk" open>
      <summary>
        <div>
          <div class="rag-node-title">Chunk #${chunk.index}</div>
          <div class="rag-node-meta">${chunk.contentLength} chars · ${escapeHtml(chunk.id)}</div>
        </div>
      </summary>
      <div class="rag-node-body">
        <div class="rag-preview">${escapeHtml(truncate(chunk.content, 180))}</div>
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
  return `
    <div class="rag-leaf">
      <div class="rag-node-title">Vector Record</div>
      <div class="rag-node-meta">${rec.namespace || '(default)'} · ${rec.contentLength} chars</div>
      <div class="rag-preview">${escapeHtml(truncate(rec.content, 140))}</div>
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

function renderLoading() {
  return '<div class="rag-empty">Processing…</div>';
}

function renderError(message) {
  return `<div class="rag-empty rag-error">${escapeHtml(message)}</div>`;
}
