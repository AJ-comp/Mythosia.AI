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
  ragSettingsSave,
  ragSettingsStatus,
  ragVectorStoreProvider,
  ragPgDimension,
  ragQdrantDimension
} from './dom.js';
import { providerKeys } from './state.js';
import { ragState, toInt, toFloatOrNull, setSelectValue, setStatusState } from './rag-shared.js';
import { getEmbeddingDefaults, updateEmbeddingUI } from './rag-embedding.js';
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

  updateRewriterUI();
  updateHybridUI();
  updateHybridWeightDisplay();
  updateRerankUI();
  updateRerankCandidateTopKDisplay();
  updateRerankDerivedMinScoreDisplay();
  updateRetrievalParamsDisplay();
  updateEmbeddingUI();
}

export function buildPipelineSettingsPayload() {
  const provider = ragEmbeddingProvider?.value?.trim();
  if (!provider) {
    throw new Error('Embedding provider is required.');
  }

  const { model: embeddingModel, dims: embeddingDimensions } = getEmbeddingDefaults(provider);
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
    rewriterModelOverride: (ragRewriterOverride?.checked && ragRewriterModel?.value) ? ragRewriterModel.value : null,
    rewriterApiKey: (ragRewriterOverride?.checked && ragRewriterModel?.value) ? getApiKeyForRewriterModel(ragRewriterModel.value) : null,
    hybridSearchEnabled: !!ragHybridSearch?.checked,
    hybridSearchVectorWeight,
    rerankEnabled: !!ragRerankEnabled?.checked,
    rerankProvider,
    rerankModel,
    rerankBaseUrl,
    rerankApiKey: ragRerankApiKey?.value?.trim() || null
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
