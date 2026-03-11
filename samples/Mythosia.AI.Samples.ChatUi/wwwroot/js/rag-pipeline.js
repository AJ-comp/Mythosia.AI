// ═══════════════════════════════════════════════════════════════
// RAG Pipeline Settings
// ═══════════════════════════════════════════════════════════════

import {
  ragChunkSize,
  ragChunkOverlap,
  ragChunker,
  ragEmbeddingProvider,
  ragEmbeddingBaseUrl,
  ragOpenAiModel,
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
  ragRerankApiKey,
  ragRetrievalMultiplier,
  ragSettingsSave,
  ragSettingsStatus
} from './dom.js';
import { providerKeys } from './state.js';
import { ragState, toInt, toFloatOrNull, setSelectValue } from './rag-shared.js';
import { getEmbeddingDefaults, updateEmbeddingUI } from './rag-embedding.js';
import { refreshRagStatus, showRagStatusError } from './rag-run.js';

// ── Load / Apply / Save ──────────────────────────────────────
export async function loadPipelineSettings() {
  if (!ragChunkSize && !ragSettingsStatus) return;
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

export function applyPipelineSettings(settings) {
  if (ragChunkSize && settings.chunkSize) ragChunkSize.value = settings.chunkSize;
  if (ragChunkOverlap && settings.chunkOverlap) ragChunkOverlap.value = settings.chunkOverlap;
  if (ragChunker && settings.chunker && !ragState.autoChunkerFromFiles) setSelectValue(ragChunker, settings.chunker);
  if (ragEmbeddingProvider && settings.embeddingProvider) setSelectValue(ragEmbeddingProvider, settings.embeddingProvider);
  if (ragOpenAiModel && settings.embeddingModel) setSelectValue(ragOpenAiModel, settings.embeddingModel);
  if (ragEmbeddingBaseUrl && settings.embeddingBaseUrl) ragEmbeddingBaseUrl.value = settings.embeddingBaseUrl;
  if (ragTopK && settings.topK) ragTopK.value = settings.topK;
  if (ragMinScore) ragMinScore.value = settings.minScore ?? '';
  if (ragPromptTemplate) ragPromptTemplate.value = settings.promptTemplate ?? '';
  if (ragQueryRewriter) ragQueryRewriter.checked = settings.queryRewriterEnabled !== false;
  if (ragRewriterOverride) ragRewriterOverride.checked = !!settings.rewriterModelOverride;
  if (ragRewriterModel && settings.rewriterModelOverride) ragRewriterModel.value = settings.rewriterModelOverride;
  if (ragHybridSearch) ragHybridSearch.checked = settings.hybridSearchEnabled !== false;
  if (ragHybridWeight) ragHybridWeight.value = settings.hybridSearchVectorWeight ?? 0.5;
  if (ragRerankEnabled) ragRerankEnabled.checked = !!settings.rerankEnabled;
  if (ragRerankProvider && settings.rerankProvider) setSelectValue(ragRerankProvider, settings.rerankProvider);
  if (ragRerankApiKey && settings.rerankApiKey) ragRerankApiKey.value = settings.rerankApiKey;
  if (ragRetrievalMultiplier && settings.retrievalMultiplier) ragRetrievalMultiplier.value = settings.retrievalMultiplier;

  updateRewriterUI();
  updateHybridUI();
  updateHybridWeightDisplay();
  updateRerankUI();
  updateEmbeddingUI();
}

export async function savePipelineSettings() {
  if (!ragSettingsSave) return;
  ragSettingsSave.disabled = true;
  if (ragSettingsStatus) ragSettingsStatus.textContent = 'Saving...';

  const payload = {
    chunkSize: toInt(ragChunkSize?.value, 300),
    chunkOverlap: toInt(ragChunkOverlap?.value, 30),
    chunker: ragChunker?.value || 'character',
    embeddingProvider: ragEmbeddingProvider?.value || 'local',
    embeddingModel: getEmbeddingDefaults(ragEmbeddingProvider?.value).model,
    embeddingDimensions: getEmbeddingDefaults(ragEmbeddingProvider?.value).dims,
    embeddingBaseUrl: ragEmbeddingBaseUrl?.value?.trim() || '',
    topK: toInt(ragTopK?.value, 3),
    minScore: toFloatOrNull(ragMinScore?.value),
    promptTemplate: ragPromptTemplate?.value?.trim() || null,
    queryRewriterEnabled: ragQueryRewriter?.checked ?? true,
    rewriterModelOverride: (ragRewriterOverride?.checked && ragRewriterModel?.value) ? ragRewriterModel.value : null,
    rewriterApiKey: (ragRewriterOverride?.checked && ragRewriterModel?.value) ? getApiKeyForRewriterModel(ragRewriterModel.value) : null,
    hybridSearchEnabled: ragHybridSearch?.checked ?? true,
    hybridSearchVectorWeight: ragHybridWeight ? parseFloat(ragHybridWeight.value) : 0.5,
    rerankEnabled: ragRerankEnabled?.checked ?? false,
    rerankProvider: ragRerankProvider?.value || 'cohere',
    rerankApiKey: ragRerankApiKey?.value?.trim() || null,
    retrievalMultiplier: toInt(ragRetrievalMultiplier?.value, 3)
  };

  try {
    const res = await fetch('/api/rag/pipeline-settings', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(payload)
    });
    const data = await res.json().catch(() => null);
    if (!res.ok) throw new Error(data?.error || 'Failed to save settings.');

    if (ragSettingsStatus) {
      ragSettingsStatus.textContent = 'Settings saved.';
      setTimeout(() => {
        if (ragSettingsStatus.textContent === 'Settings saved.') ragSettingsStatus.textContent = '';
      }, 3000);
    }
    refreshRagStatus(data);
  } catch (err) {
    if (ragSettingsStatus) ragSettingsStatus.textContent = err.message || 'Failed to save settings.';
    showRagStatusError(err);
  } finally {
    ragSettingsSave.disabled = false;
  }
}

// ── Query Rewriter UI ────────────────────────────────────────
export function updateRewriterUI() {
  const enabled = ragQueryRewriter?.checked ?? true;
  if (ragRewriterOptions) {
    ragRewriterOptions.classList.toggle('hidden', !enabled);
  }
  if (!enabled) {
    updateRewriterOverrideUI();
  }
}

export function updateRewriterOverrideUI() {
  const visible = (ragQueryRewriter?.checked ?? true) && (ragRewriterOverride?.checked ?? false);
  if (ragRewriterModelRow) {
    ragRewriterModelRow.classList.toggle('hidden', !visible);
  }
}

// ── Hybrid Search UI ─────────────────────────────────────────
export function updateHybridUI() {
  const enabled = ragHybridSearch?.checked ?? true;
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
  const enabled = ragRerankEnabled?.checked ?? false;
  if (ragRerankOptions) {
    ragRerankOptions.classList.toggle('hidden', !enabled);
  }
}

// ── Helpers ──────────────────────────────────────────────────
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
