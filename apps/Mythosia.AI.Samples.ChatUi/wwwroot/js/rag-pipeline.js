// ═══════════════════════════════════════════════════════════════
// RAG Pipeline Settings
// ═══════════════════════════════════════════════════════════════

import {
  ragChunkSize,
  ragChunkOverlap,
  ragChunker,
  ragSettingsAlert,
  ragEmbeddingProvider,
  ragEmbeddingBaseUrl,
  ragVllmBaseUrl,
  ragOpenAiModel,
  ragVllmModel,
  ragOllamaModel,
  ragTopK,
  ragMinScore,
  ragPromptTemplate,
  ragQueryRewriter,
  ragRewriterMaxTokens,
  ragExtractKeywords,
  ragRewriterOverride,
  ragRewriterModelRow,
  ragRewriterModel,
  ragRewriterOptions,
  ragHybridSearch,
  ragHybridOptions,
  ragHybridWeight,
  ragHybridWeightVal,
  ragRerankEnabled,
  ragRerankOptions,
  ragRerankProvider,
  ragRerankVllmModelRow,
  ragRerankVllmModel,
  ragRerankVllmBaseUrlRow,
  ragRerankVllmBaseUrl,
  ragRerankVllmTest,
  ragRerankVllmStatus,
  ragRerankApiKeyRow,
  ragRerankApiKey,
  ragRetrievalMultiplier,
  ragRerankCandidateTopK,
  ragMinScoreDivider,
  ragRerankDerivedMinScore,
  ragRetrievalTopK,
  ragRetrievalMinScore,
  ragFinalSelectionMode,
  ragFinalSelectionWeightRow,
  ragFinalSelectionWeight,
  ragFinalSelectionWeightVal,
  ragSettingsSave,
  ragSettingsStatus,
  ragVectorStoreProvider,
  ragPgDimension,
  ragQdrantDimension
} from './dom.js';
import { providerKeys } from './state.js';
import { ragState, toInt, toFloatOrNull, setSelectValue, setStatusState } from './rag-shared.js';
import { getEmbeddingDefaults, getSelectedEmbeddingDimensions, setEmbeddingDimensions, updateEmbeddingUI } from './rag-embedding.js';
import { refreshRagStatus, showRagStatusError } from './rag-run.js';

const PIPELINE_SETTINGS_KEY = 'rag_pipeline_settings';

// ── Load / Apply / Save ──────────────────────────────────────
export async function loadPipelineSettings() {
  if (!ragChunkSize && !ragSettingsStatus) return;
  const cached = loadCachedPipelineSettings();
  if (cached) {
    applyPipelineSettings(cached);
    refreshRagStatus(cached);
    clearSettingsAlert();
    if (ragSettingsStatus) {
      ragSettingsStatus.textContent = '';
      setStatusState(ragSettingsStatus, null);
    }
    return;
  }
  clearSettingsAlert();
  if (ragSettingsStatus) {
    ragSettingsStatus.textContent = '';
    setStatusState(ragSettingsStatus, null);
  }
}

export function applyPipelineSettings(settings) {
  const finalFilter = settings.finalFilter || {};
  const retrievalDerivation = settings.retrievalDerivation || {};
  if (ragChunkSize && settings.chunkSize) ragChunkSize.value = settings.chunkSize;
  if (ragChunkOverlap && settings.chunkOverlap) ragChunkOverlap.value = settings.chunkOverlap;
  if (ragChunker && settings.chunker && !ragState.autoChunkerFromFiles) setSelectValue(ragChunker, settings.chunker);
  if (ragEmbeddingProvider && settings.embeddingProvider) setSelectValue(ragEmbeddingProvider, settings.embeddingProvider);
  if (settings.embeddingProvider === 'openai') {
    if (ragOpenAiModel && settings.embeddingModel) setSelectValue(ragOpenAiModel, settings.embeddingModel);
  } else if (settings.embeddingProvider === 'vllm') {
    if (ragVllmModel && settings.embeddingModel) setSelectValue(ragVllmModel, settings.embeddingModel);
    if (ragVllmBaseUrl && settings.embeddingBaseUrl) ragVllmBaseUrl.value = settings.embeddingBaseUrl;
  } else {
    if (ragOllamaModel && settings.embeddingModel) setSelectValue(ragOllamaModel, settings.embeddingModel);
    if (ragEmbeddingBaseUrl && settings.embeddingBaseUrl) ragEmbeddingBaseUrl.value = settings.embeddingBaseUrl;
  }
  if (ragTopK && finalFilter.topK) ragTopK.value = finalFilter.topK;
  if (ragMinScore && finalFilter.minScore != null) ragMinScore.value = finalFilter.minScore;
  if (ragPromptTemplate) ragPromptTemplate.value = settings.promptTemplate ?? '';
  if (ragQueryRewriter && typeof settings.queryRewriterEnabled === 'boolean') ragQueryRewriter.checked = settings.queryRewriterEnabled;
  if (ragRewriterMaxTokens && settings.queryRewriteMaxTokens) ragRewriterMaxTokens.value = settings.queryRewriteMaxTokens;
  if (ragExtractKeywords && typeof settings.extractKeywords === 'boolean') ragExtractKeywords.checked = settings.extractKeywords;
  if (ragRewriterOverride) ragRewriterOverride.checked = !!settings.rewriterModelOverride;
  if (ragRewriterModel && settings.rewriterModelOverride) ragRewriterModel.value = settings.rewriterModelOverride;
  if (ragHybridSearch && typeof settings.hybridSearchEnabled === 'boolean') ragHybridSearch.checked = settings.hybridSearchEnabled;
  if (ragHybridWeight && settings.hybridSearchVectorWeight != null) ragHybridWeight.value = settings.hybridSearchVectorWeight;
  if (ragRerankEnabled) ragRerankEnabled.checked = !!settings.rerankEnabled;
  if (ragRerankProvider && settings.rerankProvider) setSelectValue(ragRerankProvider, settings.rerankProvider);
  if (ragRerankVllmModel && settings.rerankModel) setSelectValue(ragRerankVllmModel, settings.rerankModel);
  if (ragRerankVllmBaseUrl && settings.rerankBaseUrl) ragRerankVllmBaseUrl.value = settings.rerankBaseUrl;
  if (ragRerankApiKey && settings.rerankApiKey) ragRerankApiKey.value = settings.rerankApiKey;
  if (ragRetrievalMultiplier && retrievalDerivation.topKMultiplier) ragRetrievalMultiplier.value = retrievalDerivation.topKMultiplier;
  if (ragMinScoreDivider && retrievalDerivation.minScoreDivider) ragMinScoreDivider.value = retrievalDerivation.minScoreDivider;

  const finalSelection = settings.finalSelection || {};
  if (ragFinalSelectionMode && finalSelection.mode) ragFinalSelectionMode.value = finalSelection.mode;
  if (ragFinalSelectionWeight && finalSelection.retrievalWeight != null) ragFinalSelectionWeight.value = finalSelection.retrievalWeight;

  updateRewriterUI();
  updateHybridUI();
  updateHybridWeightDisplay();
  updateRerankUI();
  updateFinalSelectionUI();
  updateFinalSelectionWeightDisplay();
  updateRerankCandidateTopKDisplay();
  updateRerankDerivedMinScoreDisplay();
  updateRetrievalParamsDisplay();
  updateEmbeddingUI();

  // Restore saved custom embedding dimensions (after updateEmbeddingUI sets defaults)
  if (settings.embeddingDimensions && settings.embeddingProvider) {
    setEmbeddingDimensions(settings.embeddingProvider, settings.embeddingDimensions);
  }
}

export function buildPipelineSettingsPayload() {
  const provider = ragEmbeddingProvider?.value?.trim();
  if (!provider) {
    throw new Error('Embedding provider is required.');
  }

  const { model: embeddingModel } = getEmbeddingDefaults(provider);
  const embeddingDimensions = getSelectedEmbeddingDimensions();
  const embeddingBaseUrl = provider === 'vllm'
    ? ragVllmBaseUrl?.value?.trim()
    : ragEmbeddingBaseUrl?.value?.trim();
  const rerankProvider = ragRerankProvider?.value?.trim();
  if (!rerankProvider) {
    throw new Error('Rerank provider is required.');
  }
  const rerankModel = rerankProvider === 'vllm'
    ? ragRerankVllmModel?.value?.trim()
    : rerankProvider === 'cohere'
      ? 'rerank-v3.5'
      : null;
  const rerankBaseUrl = rerankProvider === 'vllm'
    ? ragRerankVllmBaseUrl?.value?.trim()
    : '';
  const chunkSize = toInt(ragChunkSize?.value);
  const chunkOverlap = parseInt(ragChunkOverlap?.value, 10);
  const topK = toInt(ragTopK?.value);
  const finalMinScore = toFloatOrNull(ragMinScore?.value);
  const retrievalMultiplier = toInt(ragRetrievalMultiplier?.value);
  const minScoreDivider = toInt(ragMinScoreDivider?.value);
  const hybridSearchVectorWeight = ragHybridWeight ? parseFloat(ragHybridWeight.value) : null;

  if (!chunkSize) throw new Error('Chunk size is required.');
  if (!Number.isFinite(chunkOverlap) || chunkOverlap < 0) throw new Error('Chunk overlap must be zero or a positive integer.');
  if (!ragChunker?.value?.trim()) throw new Error('Chunker is required.');
  if (!topK) throw new Error('TopK is required.');
  if (!retrievalMultiplier) throw new Error('Retrieval multiplier is required.');
  if (!minScoreDivider) throw new Error('Min score divider is required.');
  if (!Number.isFinite(hybridSearchVectorWeight)) throw new Error('Hybrid search vector weight is required.');

  if (rerankProvider === 'vllm' && !rerankModel) {
    throw new Error('A valid vLLM rerank model must be selected.');
  }
  if (rerankProvider === 'vllm' && !rerankBaseUrl) {
    throw new Error('vLLM rerank base URL is required.');
  }
  if (!!ragRerankEnabled?.checked && rerankProvider === 'cohere' && !rerankModel) {
    throw new Error('Cohere rerank model is required when re-ranking is enabled.');
  }

  return {
    chunkSize,
    chunkOverlap,
    chunker: ragChunker.value.trim(),
    embeddingProvider: provider,
    embeddingModel,
    embeddingDimensions,
    embeddingBaseUrl: embeddingBaseUrl || '',
    finalFilter: {
      topK,
      minScore: finalMinScore
    },
    retrievalDerivation: {
      topKMultiplier: retrievalMultiplier,
      minScoreDivider: minScoreDivider
    },
    promptTemplate: ragPromptTemplate?.value?.trim() || null,
    queryRewriterEnabled: !!ragQueryRewriter?.checked,
    queryRewriteMaxTokens: toInt(ragRewriterMaxTokens?.value) || 250,
    extractKeywords: ragExtractKeywords?.checked ?? true,
    rewriterModelOverride: (ragRewriterOverride?.checked && ragRewriterModel?.value) ? ragRewriterModel.value : null,
    rewriterApiKey: (ragRewriterOverride?.checked && ragRewriterModel?.value) ? getApiKeyForRewriterModel(ragRewriterModel.value) : null,
    hybridSearchEnabled: !!ragHybridSearch?.checked,
    hybridSearchVectorWeight,
    rerankEnabled: !!ragRerankEnabled?.checked,
    rerankProvider,
    rerankModel,
    rerankBaseUrl,
    rerankApiKey: ragRerankApiKey?.value?.trim() || null,
    finalSelection: {
      mode: ragFinalSelectionMode?.value || 'RerankerOnly',
      retrievalWeight: ragFinalSelectionWeight ? parseFloat(ragFinalSelectionWeight.value) : 0.65
    }
  };
}

export function getPipelineSettingsForRequest() {
  return buildPipelineSettingsPayload();
}

export async function savePipelineSettings() {
  if (!ragSettingsSave) return;
  ragSettingsSave.disabled = true;
  clearSettingsAlert();
  if (ragSettingsStatus) {
    ragSettingsStatus.textContent = 'Saving...';
    setStatusState(ragSettingsStatus, null);
  }

  const payload = buildPipelineSettingsPayload();
  const dimensionWarning = getDimensionMismatchWarning(payload);

  try {
    saveCachedPipelineSettings(payload);
    if (dimensionWarning) {
      showSettingsAlert(dimensionWarning, 'warning');
    }
    if (ragSettingsStatus) {
      ragSettingsStatus.textContent = 'Settings saved locally.';
      setStatusState(ragSettingsStatus, 'success');
      setTimeout(() => {
        if (ragSettingsStatus.textContent === 'Settings saved locally.') {
          ragSettingsStatus.textContent = '';
          setStatusState(ragSettingsStatus, null);
        }
      }, 3000);
    }
    refreshRagStatus(payload);
  } catch (err) {
    if (ragSettingsStatus) {
      ragSettingsStatus.textContent = err.message || 'Failed to save settings.';
      setStatusState(ragSettingsStatus, 'error');
    }
    showRagStatusError(err);
  } finally {
    ragSettingsSave.disabled = false;
  }
}

// ── Query Rewriter UI ────────────────────────────────────────
export function updateRewriterUI() {
  const enabled = !!ragQueryRewriter?.checked;
  if (ragRewriterOptions) {
    ragRewriterOptions.classList.toggle('hidden', !enabled);
  }
  if (!enabled) {
    updateRewriterOverrideUI();
  }
}

export function updateRewriterOverrideUI() {
  const visible = !!ragQueryRewriter?.checked && !!ragRewriterOverride?.checked;
  if (ragRewriterModelRow) {
    ragRewriterModelRow.classList.toggle('hidden', !visible);
  }
}

// ── Hybrid Search UI ─────────────────────────────────────────
export function updateHybridUI() {
  const enabled = !!ragHybridSearch?.checked;
  if (ragHybridOptions) {
    ragHybridOptions.classList.toggle('hidden', !enabled);
  }
}

export function updateHybridWeightDisplay() {
  if (ragHybridWeightVal && ragHybridWeight) {
    ragHybridWeightVal.textContent = parseFloat(ragHybridWeight.value).toFixed(2);
  }
}

// ── Final Selection UI ───────────────────────────────────────
export function updateFinalSelectionUI() {
  const isBlend = ragFinalSelectionMode?.value === 'WeightedBlend';
  if (ragFinalSelectionWeightRow) {
    ragFinalSelectionWeightRow.classList.toggle('hidden', !isBlend);
  }
}

export function updateFinalSelectionWeightDisplay() {
  if (ragFinalSelectionWeightVal && ragFinalSelectionWeight) {
    ragFinalSelectionWeightVal.textContent = parseFloat(ragFinalSelectionWeight.value).toFixed(2);
  }
}

// ── Re-ranking UI ────────────────────────────────────────────
export function updateRerankUI() {
  const enabled = !!ragRerankEnabled?.checked;
  if (ragRerankOptions) {
    ragRerankOptions.classList.toggle('hidden', !enabled);
  }

  updateRerankCandidateTopKDisplay();
  updateRerankDerivedMinScoreDisplay();
  updateRetrievalParamsDisplay();

  const provider = ragRerankProvider?.value?.trim();
  const isVllm = enabled && provider === 'vllm';

  if (ragRerankVllmModelRow) {
    ragRerankVllmModelRow.classList.toggle('hidden', !isVllm);
  }

  if (ragRerankVllmBaseUrlRow) {
    ragRerankVllmBaseUrlRow.classList.toggle('hidden', !isVllm);
  }

  if (ragRerankApiKeyRow) {
    ragRerankApiKeyRow.classList.toggle('hidden', isVllm);
  }

  if (ragRerankVllmStatus && !isVllm) {
    ragRerankVllmStatus.textContent = 'Health check uses /health, reranking uses /v1/rerank.';
    setStatusState(ragRerankVllmStatus, null);
  }
}

export function updateRerankCandidateTopKDisplay() {
  if (!ragRerankCandidateTopK) return;

  const enabled = !!ragRerankEnabled?.checked;
  const topK = toInt(ragTopK?.value);
  const multiplier = toInt(ragRetrievalMultiplier?.value);
  const candidateTopK = topK && multiplier ? topK * multiplier : '';

  ragRerankCandidateTopK.value = candidateTopK ? String(candidateTopK) : '';
  ragRerankCandidateTopK.title = enabled
    ? (candidateTopK ? `Re-rank candidate pool: TopK ${topK} × multiplier ${multiplier} = ${candidateTopK}` : 'Re-rank candidate pool requires explicit TopK and retrieval multiplier values.')
    : (candidateTopK ? `If re-ranking is enabled, TopK ${topK} × multiplier ${multiplier} = ${candidateTopK}` : 'If re-ranking is enabled, explicit TopK and retrieval multiplier values are required.');
}

export function updateRerankDerivedMinScoreDisplay() {
  if (!ragRerankDerivedMinScore) return;

  const enabled = !!ragRerankEnabled?.checked;
  const minScore = toFloatOrNull(ragMinScore?.value);
  const divider = toInt(ragMinScoreDivider?.value);
  const derived = minScore != null && divider ? (minScore / divider).toFixed(2) : '';

  ragRerankDerivedMinScore.value = derived ? String(derived) : '';
  ragRerankDerivedMinScore.title = enabled
    ? (derived ? `Retrieval min score: ${minScore} ÷ ${divider} = ${derived}` : 'Derived min score requires explicit MinScore and divider values.')
    : (derived ? `If re-ranking is enabled, MinScore ${minScore} ÷ divider ${divider} = ${derived}` : 'If re-ranking is enabled, explicit MinScore and divider values are required.');
}

export function updateRetrievalParamsDisplay() {
  const rerankEnabled = !!ragRerankEnabled?.checked;
  const topK = toInt(ragTopK?.value);
  const minScore = toFloatOrNull(ragMinScore?.value);
  const multiplier = toInt(ragRetrievalMultiplier?.value);
  const divider = toInt(ragMinScoreDivider?.value);

  if (ragRetrievalTopK) {
    const val = rerankEnabled && topK && multiplier ? topK * multiplier : (topK || '');
    ragRetrievalTopK.value = val ? String(val) : '';
  }
  if (ragRetrievalMinScore) {
    const val = rerankEnabled && minScore != null && divider
      ? (minScore / divider).toFixed(2)
      : (minScore != null ? String(minScore) : '');
    ragRetrievalMinScore.value = val;
  }
}

export async function testVllmRerankConnection() {
  if (!ragRerankVllmTest) return;
  ragRerankVllmTest.disabled = true;

  if (ragRerankVllmStatus) {
    ragRerankVllmStatus.textContent = 'Testing...';
    setStatusState(ragRerankVllmStatus, null);
  }

  const baseUrl = ragRerankVllmBaseUrl?.value?.trim();
  const model = ragRerankVllmModel?.value?.trim();
  const apiKey = ragRerankApiKey?.value?.trim() || '';
  if (!baseUrl || !model) {
    if (ragRerankVllmStatus) {
      ragRerankVllmStatus.textContent = 'vLLM rerank base URL and model are required.';
      setStatusState(ragRerankVllmStatus, 'error');
    }
    ragRerankVllmTest.disabled = false;
    return;
  }

  try {
    const res = await fetch('/api/rag/vllm-rerank-test', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ baseUrl, model, apiKey })
    });
    const data = await res.json().catch(() => null);

    if (!res.ok) {
      if (ragRerankVllmStatus) {
        ragRerankVllmStatus.textContent = data?.error || 'Connection failed.';
        setStatusState(ragRerankVllmStatus, 'error');
      }
      return;
    }

    if (ragRerankVllmStatus) {
      ragRerankVllmStatus.textContent = `✅ Connected · ${model} ready for reranking`;
      setStatusState(ragRerankVllmStatus, 'success');
    }
  } catch (err) {
    if (ragRerankVllmStatus) {
      ragRerankVllmStatus.textContent = `❌ ${err.message || 'Network error'}`;
      setStatusState(ragRerankVllmStatus, 'error');
    }
  } finally {
    ragRerankVllmTest.disabled = false;
  }
}

function showSettingsAlert(message, state) {
  if (!ragSettingsAlert) return;
  ragSettingsAlert.textContent = message;
  ragSettingsAlert.classList.remove('hidden');
  setStatusState(ragSettingsAlert, state);
}

function clearSettingsAlert() {
  if (!ragSettingsAlert) return;
  ragSettingsAlert.textContent = '';
  ragSettingsAlert.classList.add('hidden');
  setStatusState(ragSettingsAlert, null);
}

function getDimensionMismatchWarning(payload) {
  const vectorStoreProvider = ragVectorStoreProvider?.value?.trim()?.toLowerCase();
  const embeddingDims = payload?.embeddingDimensions;
  if (!embeddingDims || !vectorStoreProvider || vectorStoreProvider === 'inmemory' || vectorStoreProvider === 'pinecone') {
    return null;
  }

  const vectorDims = vectorStoreProvider === 'postgres'
    ? toInt(ragPgDimension?.value)
    : vectorStoreProvider === 'qdrant'
      ? toInt(ragQdrantDimension?.value)
      : 0;

  if (!vectorDims || vectorDims === embeddingDims) {
    return null;
  }

  const providerLabel = vectorStoreProvider === 'postgres' ? 'PostgreSQL' : 'Qdrant';
  return `${providerLabel} vector dimension (${vectorDims}) does not match the selected embedding dimension (${embeddingDims}). Indexing/querying may fail until they match.`;
}

// ── Helpers ──────────────────────────────────────────────────
function loadCachedPipelineSettings() {
  try {
    const raw = localStorage.getItem(PIPELINE_SETTINGS_KEY);
    if (!raw) return null;
    const parsed = JSON.parse(raw);
    return parsed && typeof parsed === 'object' ? parsed : null;
  } catch {
    return null;
  }
}

export function getCachedPipelineSettings() {
  return loadCachedPipelineSettings();
}

function saveCachedPipelineSettings(settings) {
  try {
    if (!settings || typeof settings !== 'object') return;
    localStorage.setItem(PIPELINE_SETTINGS_KEY, JSON.stringify(settings));
  } catch {
    /* ignore storage errors */
  }
}

function getProviderForRewriterModel(modelEnum) {
  if (!modelEnum) return null;
  if (modelEnum.startsWith('Gpt') || modelEnum.startsWith('GPT')) return 'OpenAI';
  if (modelEnum.startsWith('Claude')) return 'Anthropic';
  if (modelEnum.startsWith('Gemini')) return 'Google';
  if (modelEnum.startsWith('Grok')) return 'xAI';
  if (modelEnum.startsWith('DeepSeek')) return 'DeepSeek';
  if (modelEnum.startsWith('Perplexity')) return 'Perplexity';
  return null;
}

function getApiKeyForRewriterModel(modelEnum) {
  const provider = getProviderForRewriterModel(modelEnum);
  if (!provider) return null;
  return providerKeys?.[provider] || null;
}

// ── Pipeline Settings PDF Export ─────────────────────────────
export async function exportPipelineSettingsPdf() {
  if (typeof window.html2canvas !== 'function' || !window.jspdf?.jsPDF) {
    alert('PDF libraries not loaded. Check your network connection.');
    return;
  }

  const btn = document.getElementById('rag-settings-export-pdf');
  if (!btn) return;
  const originalText = btn.innerHTML;
  btn.disabled = true;
  btn.innerHTML = '<span class="pipe-pdf-spinner"></span> Generating...';

  let settings;
  try {
    settings = buildPipelineSettingsPayload();
  } catch (err) {
    alert('Failed to read settings: ' + err.message);
    btn.disabled = false;
    btn.innerHTML = originalText;
    return;
  }

  // Read vector store settings from DOM directly (no import needed)
  const vsProvider = ragVectorStoreProvider?.value?.trim() || 'inmemory';
  const vsLabels = { inmemory: 'InMemory', postgres: 'PostgreSQL (pgvector)', qdrant: 'Qdrant', pinecone: 'Pinecone' };
  const vsLabel = vsLabels[vsProvider] || vsProvider;
  const pgDim = ragPgDimension?.value || '-';
  const qdrantDim = ragQdrantDimension?.value || '-';

  const esc = (s) => String(s ?? '').replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;');
  const now = new Date();
  const ff = settings.finalFilter || {};
  const rd = settings.retrievalDerivation || {};
  const fs = settings.finalSelection || {};

  // Derived values
  const rerankEnabled = !!settings.rerankEnabled;
  const candidateTopK = ff.topK && rd.topKMultiplier ? ff.topK * rd.topKMultiplier : '-';
  const derivedMinScore = ff.minScore != null && rd.minScoreDivider
    ? (ff.minScore / rd.minScoreDivider).toFixed(4) : '-';
  const effectiveTopK = rerankEnabled ? candidateTopK : (ff.topK || '-');
  const effectiveMinScore = rerankEnabled ? derivedMinScore : (ff.minScore ?? 'None');

  function kvRow(label, value, note) {
    return `<tr><td>${esc(label)}</td><td>${esc(String(value ?? '-'))}${note ? ` <span style="color:#94a3b8;font-size:11px">${esc(note)}</span>` : ''}</td></tr>`;
  }

  function sectionHeader(num, title, badge) {
    const badgeHtml = badge ? ` <span class="badge ${badge.cls}">${esc(badge.text)}</span>` : '';
    return `<h2><span class="ch">${num}</span> ${esc(title)}${badgeHtml}</h2>`;
  }

  const reportHtml = `
    <div class="rpt">
      <style>
        .rpt { font-family: 'Inter', 'Segoe UI', sans-serif; color: #1e293b; line-height: 1.5; padding: 40px; width: 800px; }
        .rpt h1 { font-size: 22px; font-weight: 700; color: #4f46e5; margin: 0 0 4px; }
        .rpt .rpt-sub { font-size: 12px; color: #94a3b8; margin-bottom: 28px; }
        .rpt h2 { font-size: 15px; font-weight: 700; color: #1e293b; margin: 24px 0 8px; padding-bottom: 6px; border-bottom: 2px solid #e2e8f0; display: flex; align-items: center; gap: 8px; }
        .rpt h2 .ch { display: inline-flex; align-items: center; justify-content: center; width: 24px; height: 24px; border-radius: 50%; background: #4f46e5; color: #fff; font-size: 12px; font-weight: 700; flex-shrink: 0; }
        .rpt h2 .badge { font-size: 11px; font-weight: 600; padding: 2px 8px; border-radius: 10px; margin-left: 4px; }
        .rpt h2 .b-on { background: #dbeafe; color: #2563eb; }
        .rpt h2 .b-off { background: #f1f5f9; color: #94a3b8; }
        .rpt table { width: 100%; border-collapse: collapse; font-size: 13px; margin: 6px 0 12px; }
        .rpt td { padding: 7px 10px; border-bottom: 1px solid #f1f5f9; vertical-align: top; }
        .rpt td:first-child { font-weight: 600; color: #64748b; width: 200px; white-space: nowrap; }
        .rpt td:last-child { color: #1e293b; }
        .rpt tr:nth-child(even) { background: #f8fafc; }
        .rpt .note { font-size: 11px; color: #94a3b8; font-style: italic; margin: 4px 0 10px; }
        .rpt .derived-box { background: #f0fdf4; border: 1px solid #bbf7d0; border-radius: 8px; padding: 12px 16px; margin: 10px 0 14px; }
        .rpt .derived-box h3 { font-size: 13px; font-weight: 700; color: #16a34a; margin: 0 0 8px; }
        .rpt .derived-box table { margin: 0; }
        .rpt .derived-box td:first-child { color: #15803d; }
        .rpt .prompt-box { background: #f8fafc; border: 1px solid #e2e8f0; border-radius: 8px; padding: 12px 16px; font-family: 'Consolas','Courier New',monospace; font-size: 11px; white-space: pre-wrap; word-break: break-all; line-height: 1.6; color: #475569; }
        .rpt hr { border: none; border-top: 1px solid #e2e8f0; margin: 16px 0; }
      </style>

      <h1>RAG Pipeline Settings Report</h1>
      <div class="rpt-sub">Generated: ${now.toLocaleString('ko-KR')} &nbsp;|&nbsp; Mythosia.AI</div>

      <!-- 1. Chunking -->
      ${sectionHeader(1, 'Chunking')}
      <table>
        ${kvRow('Chunker', settings.chunker?.toUpperCase())}
        ${kvRow('Chunk Size', settings.chunkSize, 'characters')}
        ${kvRow('Chunk Overlap', settings.chunkOverlap, 'characters')}
      </table>

      <!-- 2. Embedding -->
      ${sectionHeader(2, 'Embedding')}
      <table>
        ${kvRow('Provider', settings.embeddingProvider?.toUpperCase())}
        ${kvRow('Model', settings.embeddingModel)}
        ${kvRow('Dimensions', settings.embeddingDimensions)}
        ${settings.embeddingBaseUrl ? kvRow('Base URL', settings.embeddingBaseUrl) : ''}
      </table>

      <!-- 3. Query Rewrite -->
      ${sectionHeader(3, 'Query Rewrite', { text: settings.queryRewriterEnabled ? 'Enabled' : 'Disabled', cls: settings.queryRewriterEnabled ? 'b-on' : 'b-off' })}
      <table>
        ${kvRow('Enabled', settings.queryRewriterEnabled ? 'Yes' : 'No')}
        ${settings.queryRewriterEnabled ? kvRow('Max Tokens', settings.queryRewriteMaxTokens) : ''}
        ${settings.queryRewriterEnabled ? kvRow('Extract Keywords', settings.extractKeywords ? 'Yes' : 'No') : ''}
        ${settings.rewriterModelOverride ? kvRow('Model Override', settings.rewriterModelOverride) : ''}
      </table>

      <!-- 4. Hybrid Search -->
      ${sectionHeader(4, 'Hybrid Search', { text: settings.hybridSearchEnabled ? 'Enabled' : 'Disabled', cls: settings.hybridSearchEnabled ? 'b-on' : 'b-off' })}
      <table>
        ${kvRow('Enabled', settings.hybridSearchEnabled ? 'Yes' : 'No')}
        ${settings.hybridSearchEnabled ? kvRow('Vector Weight', settings.hybridSearchVectorWeight?.toFixed(2), `(BM25 Weight: ${(1 - (settings.hybridSearchVectorWeight || 0)).toFixed(2)})`) : ''}
      </table>

      <!-- 5. Vector Store -->
      ${sectionHeader(5, 'Vector Store')}
      <table>
        ${kvRow('Provider', vsLabel)}
        ${vsProvider === 'postgres' ? kvRow('Dimension', pgDim) : ''}
        ${vsProvider === 'qdrant' ? kvRow('Dimension', qdrantDim) : ''}
      </table>

      <!-- 6. Retrieval & Filtering -->
      ${sectionHeader(6, 'Retrieval & Filtering')}
      <table>
        ${kvRow('Final Top K', ff.topK)}
        ${kvRow('Final Min Score', ff.minScore ?? 'None')}
      </table>

      <!-- 7. Re-ranking -->
      ${sectionHeader(7, 'Re-ranking', { text: rerankEnabled ? 'Enabled' : 'Disabled', cls: rerankEnabled ? 'b-on' : 'b-off' })}
      <table>
        ${kvRow('Enabled', rerankEnabled ? 'Yes' : 'No')}
        ${rerankEnabled ? kvRow('Provider', settings.rerankProvider) : ''}
        ${rerankEnabled && settings.rerankModel ? kvRow('Model', settings.rerankModel) : ''}
        ${rerankEnabled && settings.rerankBaseUrl ? kvRow('Base URL', settings.rerankBaseUrl) : ''}
        ${rerankEnabled ? kvRow('Top K Multiplier', rd.topKMultiplier) : ''}
        ${rerankEnabled ? kvRow('Min Score Divider', rd.minScoreDivider) : ''}
      </table>

      <!-- 8. Final Selection -->
      ${sectionHeader(8, 'Final Selection')}
      <table>
        ${kvRow('Mode', fs.mode || 'RerankerOnly')}
        ${fs.mode === 'WeightedBlend' ? kvRow('Retrieval Weight', fs.retrievalWeight?.toFixed(2), `(Reranker: ${(1 - (fs.retrievalWeight || 0)).toFixed(2)})`) : ''}
      </table>

      <!-- Derived Parameters -->
      <div class="derived-box">
        <h3>Effective Retrieval Parameters</h3>
        <p class="note">These are the actual values used at search time, after applying multiplier/divider when re-ranking is enabled.</p>
        <table>
          ${kvRow('Retrieval Top K', effectiveTopK, rerankEnabled ? `(${ff.topK} x ${rd.topKMultiplier})` : '')}
          ${kvRow('Retrieval Min Score', effectiveMinScore, rerankEnabled && ff.minScore != null ? `(${ff.minScore} / ${rd.minScoreDivider})` : '')}
        </table>
      </div>

      <!-- 9. Prompt Template -->
      ${sectionHeader(9, 'Prompt Template')}
      ${settings.promptTemplate
        ? `<div class="prompt-box">${esc(settings.promptTemplate)}</div>`
        : '<p class="note">Using default prompt template.</p>'}
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
      scale: 2, useCORS: true, backgroundColor: '#ffffff', logging: false,
      width: rptEl.scrollWidth, height: rptEl.scrollHeight
    });

    const imgW = canvas.width, imgH = canvas.height;
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
    pdf.save(`RAG_Pipeline_Settings_${ts}.pdf`);

    btn.innerHTML = '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><polyline points="20 6 9 17 4 12"/></svg> Saved!';
    setTimeout(() => { btn.innerHTML = originalText; btn.disabled = false; }, 2000);
  } catch (err) {
    console.error('Settings PDF export failed:', err);
    btn.innerHTML = 'Export Failed';
    alert('PDF export failed: ' + (err.message || err));
    setTimeout(() => { btn.innerHTML = originalText; btn.disabled = false; }, 2000);
  } finally {
    container.remove();
  }
}
