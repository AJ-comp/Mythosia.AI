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
  ragVllmBaseUrl,
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
import { ragState, setSelectValue, markReferenceStale, setStatusState, setViewCodeEnabled, updateRunState } from './rag-shared.js';
import { getSelectedEmbeddingProvider, getEmbeddingDefaults } from './rag-embedding.js';
import { getPipelineSettingsForRequest } from './rag-pipeline.js';
import { getVectorStoreConfigForRequest } from './rag-vector-store.js';
import { renderTrace as doRenderTrace, renderLoadingDetails, renderError } from './rag-trace.js';
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

  let settings;
  let provider;
  let embeddingModel;
  let embeddingDimensions;
  let chunker;
  let topK;
  let finalMinScore;
  let retrievalMultiplier;
  let promptTemplate;
  let embeddingBaseUrl;

  try {
    settings = getPipelineSettingsForRequest();
    if (!settings) {
      throw new Error('RAG pipeline settings are required.');
    }
    provider = settings.embeddingProvider.toLowerCase();
    ({ model: embeddingModel, dims: embeddingDimensions } = getEmbeddingDefaults(provider));
    chunker = settings.chunker;
    topK = settings.finalFilter?.topK;
    finalMinScore = settings.finalFilter?.minScore ?? ragMinScore?.value ?? '';
    retrievalMultiplier = settings.retrievalDerivation?.topKMultiplier;
    promptTemplate = settings.promptTemplate ?? ragPromptTemplate?.value ?? '';
    embeddingBaseUrl = settings.embeddingBaseUrl ?? '';

    if (!chunker) throw new Error('Chunker is required.');
    if (!topK) throw new Error('TopK is required.');
    if (provider === 'openai' && !providerKeys?.OpenAI) {
      throw new Error('OpenAI API key is required.');
    }
  } catch (err) {
    const message = err.message || 'Invalid RAG pipeline settings.';
    ragStatus.textContent = message;
    setStatusState(ragStatus, 'error');
    ragTrace.innerHTML = renderError(message);
    updateRunState(files);
    return;
  }

  ragRun.disabled = true;
  ragStatus.textContent = `Embedding ${files.length} file${files.length > 1 ? 's' : ''} with ${provider.toUpperCase()}...`;
  setStatusState(ragStatus, null);

  const formData = new FormData();
  files.forEach((file) => formData.append('files', file));
  const chunkSize = settings.chunkSize ?? ragChunkSize?.value;
  const chunkOverlap = settings.chunkOverlap ?? ragChunkOverlap?.value;

  formData.append('chunkSize', String(chunkSize));
  formData.append('chunkOverlap', String(chunkOverlap));
  formData.append('chunker', String(chunker));
  formData.append('embeddingProvider', provider);
  formData.append('embeddingModel', String(embeddingModel));
  formData.append('embeddingDimensions', String(embeddingDimensions));
  formData.append('embeddingBaseUrl', String(embeddingBaseUrl));
  formData.append('finalFilterTopK', String(topK));
  formData.append('finalFilterMinScore', String(finalMinScore));
  formData.append('retrievalDerivationMinScoreDivider', String(settings.retrievalDerivation?.minScoreDivider ?? ''));
  formData.append('promptTemplate', String(promptTemplate));
  formData.append('queryRewriterEnabled', String(settings.queryRewriterEnabled ?? ''));
  formData.append('rewriterModelOverride', settings.rewriterModelOverride ?? '');
  formData.append('rewriterApiKey', settings.rewriterApiKey ?? '');
  formData.append('hybridSearchEnabled', String(settings.hybridSearchEnabled ?? ''));
  formData.append('hybridSearchVectorWeight', String(settings.hybridSearchVectorWeight ?? ''));
  formData.append('rerankEnabled', String(settings.rerankEnabled ?? ''));
  formData.append('rerankProvider', settings.rerankProvider ?? '');
  formData.append('rerankModel', settings.rerankModel ?? '');
  formData.append('rerankBaseUrl', settings.rerankBaseUrl ?? '');
  formData.append('rerankApiKey', settings.rerankApiKey ?? '');
  formData.append('retrievalDerivationTopKMultiplier', String(retrievalMultiplier ?? ''));
  if (provider === 'openai' && providerKeys?.OpenAI) {
    formData.append('openaiApiKey', providerKeys.OpenAI);
  }

  const vectorStore = getVectorStoreConfigForRequest();
  ragTrace.innerHTML = renderLoadingDetails({
    files: files.map((file) => file.name),
    provider,
    embeddingModel,
    chunker,
    vectorStore: vectorStore.provider
  });
  Object.entries(vectorStore).forEach(([key, value]) => {
    if (key === 'openAiApiKey') return;
    if (value === undefined || value === null) return;
    formData.append(key, String(value));
  });

  try {
    const res = await fetch('/api/rag/reference', { method: 'POST', body: formData });
    const payload = await res.json().catch(() => null);

    if (!res.ok) {
      const message = payload?.error || 'Failed to build RAG reference.';
      ragStatus.textContent = message;
      setStatusState(ragStatus, 'error');
      ragTrace.innerHTML = renderError(message);
      ragRun.disabled = false;
      return;
    }

    ragStatus.textContent = `Ready · ${payload.summary.documentCount} docs · ${payload.summary.chunkCount} chunks`;
    setStatusState(ragStatus, 'success');
    doRenderTrace(ragTrace, payload);
    setViewCodeEnabled(true);
    setDiagnoseEnabled(true);
    refreshRagStatus();
    refreshReferenceHistory();
  } catch (err) {
    ragStatus.textContent = 'Network error.';
    setStatusState(ragStatus, 'error');
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

  const localSettings = getPipelineSettingsForRequest();

  try {
    const res = await fetch('/api/rag/status');
    const payload = await res.json().catch(() => null);
    if (!res.ok) throw new Error(payload?.error || 'Failed to load RAG status.');

    applyRagStatus(localSettings || payload?.settings || {}, payload?.hasIndex);
    setDiagnoseEnabled(!!payload?.hasIndex);
  } catch (err) {
    showRagStatusError(err);
  }
}

function applyRagStatus(settings, hasIndex) {
  if (!ragChatStatus) return;
  const provider = settings.embeddingProvider ? settings.embeddingProvider.toUpperCase() : 'UNSET';
  const topK = settings.finalFilter?.topK ?? 'UNSET';
  const minScore = settings.finalFilter?.minScore ?? 'UNSET';
  const chunker = settings.chunker ? settings.chunker.toUpperCase() : 'N/A';
  const statusLabel = hasIndex ? 'RAG: READY' : 'RAG: NOT INDEXED';

  ragChatStatus.textContent = `${statusLabel} · TopK=${topK} · MinScore=${minScore} · ${provider} · ${chunker}`;
  ragChatStatus.classList.toggle('active', !!hasIndex);
  ragChatStatus.classList.remove('error');
}

export function updateVectorDbStatus() {
  if (!vectordbChatStatus) return;
  const provider = ragVectorStoreProvider?.value?.trim();
  if (provider === 'postgres' && ragState.pgConnected) {
    const schema = ragPgSchema?.value?.trim() || 'UNSET';
    const table = ragPgTable?.value?.trim() || 'UNSET';
    vectordbChatStatus.textContent = `VectorDB: PostgreSQL · ${schema}.${table}`;
    vectordbChatStatus.classList.add('active');
  } else if (provider === 'qdrant' && ragState.qdrantConnected) {
    const host = ragQdrantHost?.value?.trim() || 'UNSET';
    const col = ragQdrantCollection?.value?.trim() || 'UNSET';
    vectordbChatStatus.textContent = `VectorDB: Qdrant · ${host} · collection=${col}`;
    vectordbChatStatus.classList.add('active');
  } else if (provider === 'pinecone' && ragState.pineconeConnected) {
    const host = ragPineconeIndexHost?.value?.trim() || 'UNSET';
    const ns = ragPineconeNamespace?.value?.trim() || 'UNSET';
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
  const topK = config.finalFilter?.topK ?? '-';
  const minScore = config.finalFilter?.minScore ?? '-';
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
