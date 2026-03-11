// ═══════════════════════════════════════════════════════════════
// RAG Run, Status & History
// ═══════════════════════════════════════════════════════════════

import {
  ragFiles,
  ragFileList,
  ragChunkSize,
  ragChunkOverlap,
  ragChunker,
  ragEmbeddingBaseUrl,
  ragRun,
  ragStatus,
  ragTrace,
  ragHistoryList,
  ragTopK,
  ragMinScore,
  ragPromptTemplate,
  ragChatStatus,
  vectordbChatStatus,
  ragVectorStoreProvider,
  ragPgSchema,
  ragPgTable,
  ragQdrantHost,
  ragQdrantCollection,
  ragPineconeIndexHost,
  ragPineconeNamespace,
  codeModal,
  codeModalContent,
  codeCopyAll
} from './dom.js';
import { escapeHtml } from './utils.js';
import { providerKeys } from './state.js';
import { ragState, setSelectValue, markReferenceStale, setViewCodeEnabled, updateRunState } from './rag-shared.js';
import { getSelectedEmbeddingProvider, getEmbeddingDefaults } from './rag-embedding.js';
import { renderTrace as doRenderTrace, renderLoading, renderError } from './rag-trace.js';
import { setDiagnoseEnabled } from './rag-diagnostics.js';

// ── File List ────────────────────────────────────────────────
export function updateFileList() {
  markReferenceStale();
  const files = Array.from(ragFiles.files || []);
  updateRunState(files);

  if (files.length > 0 && ragChunker) {
    const hasNonTxt = files.some((f) => !f.name.toLowerCase().endsWith('.txt'));
    setSelectValue(ragChunker, hasNonTxt ? 'markdown' : 'recursive');
    ragState.autoChunkerFromFiles = true;
  } else {
    ragState.autoChunkerFromFiles = false;
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

// ── Run Reference ────────────────────────────────────────────
export async function runReference() {
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
  const embDefaults = getEmbeddingDefaults(provider);
  formData.append('embeddingModel', embDefaults.model);
  formData.append('embeddingDimensions', String(embDefaults.dims));
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
    doRenderTrace(ragTrace, payload);
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

// ── Status ───────────────────────────────────────────────────
export async function refreshRagStatus(settingsOverride) {
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

export function updateVectorDbStatus() {
  if (!vectordbChatStatus) return;
  const provider = ragVectorStoreProvider?.value || 'inmemory';
  if (provider === 'postgres' && ragState.pgConnected) {
    const schema = ragPgSchema?.value || 'public';
    const table = ragPgTable?.value || 'vectors';
    vectordbChatStatus.textContent = `VectorDB: PostgreSQL · ${schema}.${table}`;
    vectordbChatStatus.classList.add('active');
  } else if (provider === 'qdrant' && ragState.qdrantConnected) {
    const host = ragQdrantHost?.value || 'localhost';
    const col = ragQdrantCollection?.value || 'default';
    vectordbChatStatus.textContent = `VectorDB: Qdrant · ${host} · collection=${col}`;
    vectordbChatStatus.classList.add('active');
  } else if (provider === 'pinecone' && ragState.pineconeConnected) {
    const host = ragPineconeIndexHost?.value || '';
    const ns = ragPineconeNamespace?.value || 'default';
    vectordbChatStatus.textContent = `VectorDB: Pinecone · ${host} · namespace=${ns}`;
    vectordbChatStatus.classList.add('active');
  } else {
    vectordbChatStatus.textContent = provider === 'inmemory' ? 'VectorDB: InMemory' : '';
    vectordbChatStatus.classList.remove('active');
  }
}

export function showRagStatusError(err) {
  if (!ragChatStatus) return;
  ragChatStatus.textContent = `RAG: ERROR · ${err.message || 'Status unavailable'}`;
  ragChatStatus.classList.add('error');
  ragChatStatus.classList.remove('active');
}

// ── History ──────────────────────────────────────────────────
export async function refreshReferenceHistory() {
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

// ── Code Modal ───────────────────────────────────────────────
export function openRagCodeModal() {
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
