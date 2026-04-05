// ═══════════════════════════════════════════════════════════════
// RAG Embedding Provider UI
// ═══════════════════════════════════════════════════════════════

import {
  ragEmbeddingProvider,
  ragEmbeddingBaseRow,
  ragEmbeddingBaseUrl,
  ragEmbeddingStatus,
  ragEmbeddingHint,
  ragOllamaModelRow,
  ragOllamaModel,
  ragOllamaDimensions,
  ragOllamaTest,
  ragOllamaStatus,
  ragVllmModelRow,
  ragVllmModel,
  ragVllmDimensions,
  ragVllmTest,
  ragVllmStatus,
  ragVllmBaseRow,
  ragVllmBaseUrl,
  ragOpenAiModelRow,
  ragOpenAiModel,
  ragOpenAiDimensions,
  ragOpenAiKey,
  ragOpenAiKeyInput,
  ragOpenAiKeySave,
  ragOpenAiKeyStatus,
  ragPgDimension,
  ragQdrantDimension
} from './dom.js';
import { providerKeys, saveKeysToStorage } from './state.js';
import { refreshProviderGroup } from './models.js';
import { setStatusState, updateRunState } from './rag-shared.js';

export function getSelectedEmbeddingProvider() {
  const provider = ragEmbeddingProvider?.value?.trim();
  if (!provider) {
    throw new Error('Embedding provider is required.');
  }
  return provider;
}

export function getEmbeddingDefaults(provider) {
  const p = provider || getSelectedEmbeddingProvider();
  if (p === 'ollama') {
    const model = ragOllamaModel?.value?.trim();
    const dimsMap = {
      'qwen3-embedding:0.6b': 1024,
      'qwen3-embedding:4b':   2560,
      'qwen3-embedding:8b':   4096
    };
    if (!model || !dimsMap[model]) {
      throw new Error('A valid Ollama embedding model must be selected.');
    }
    return { model, dims: dimsMap[model] };
  }
  if (p === 'vllm') {
    const model = ragVllmModel?.value?.trim();
    const dimsMap = {
      'Qwen/Qwen3-Embedding-0.6B': 1024,
      'Qwen/Qwen3-Embedding-4B': 2560,
      'Qwen/Qwen3-Embedding-8B': 4096
    };
    if (!model || !dimsMap[model]) {
      throw new Error('A valid vLLM embedding model must be selected.');
    }
    return { model, dims: dimsMap[model] };
  }
  if (p === 'openai') {
    const model = ragOpenAiModel?.value?.trim();
    const dimsMap = {
      'text-embedding-3-small': 1536,
      'text-embedding-3-large': 3072,
      'text-embedding-ada-002': 1536
    };
    if (!model || !dimsMap[model]) {
      throw new Error('A valid OpenAI embedding model must be selected.');
    }
    return { model, dims: dimsMap[model] };
  }
  throw new Error(`Unsupported embedding provider: ${p}`);
}

export function getSelectedEmbeddingDimensions() {
  const provider = getSelectedEmbeddingProvider();
  let input;
  if (provider === 'ollama') input = ragOllamaDimensions;
  else if (provider === 'vllm') input = ragVllmDimensions;
  else if (provider === 'openai') input = ragOpenAiDimensions;
  const val = parseInt(input?.value, 10);
  return Number.isFinite(val) && val > 0 ? val : getEmbeddingDefaults(provider).dims;
}

export function setEmbeddingDimensions(provider, dims) {
  if (provider === 'ollama' && ragOllamaDimensions) ragOllamaDimensions.value = dims;
  else if (provider === 'vllm' && ragVllmDimensions) ragVllmDimensions.value = dims;
  else if (provider === 'openai' && ragOpenAiDimensions) ragOpenAiDimensions.value = dims;
}

export function updateEmbeddingUI(resetDimensions = false) {
  const provider = getSelectedEmbeddingProvider();
  const hasOpenAiKey = !!providerKeys?.OpenAI;
  const providerLabel = provider ? provider.toUpperCase() : 'N/A';
  const openAiModel = ragOpenAiModel?.value?.trim() || '';
  const ollamaModel = ragOllamaModel?.value?.trim() || '';
  const vllmModel = ragVllmModel?.value?.trim() || '';
  const embeddingModel = provider === 'ollama'
    ? ollamaModel
    : provider === 'vllm'
      ? vllmModel
      : openAiModel;

  if (ragOpenAiKey) {
    ragOpenAiKey.classList.toggle('hidden', provider !== 'openai' || hasOpenAiKey);
  }

  if (ragOllamaModelRow) {
    ragOllamaModelRow.classList.toggle('hidden', provider !== 'ollama');
  }

  if (ragVllmModelRow) {
    ragVllmModelRow.classList.toggle('hidden', provider !== 'vllm');
  }

  if (ragOpenAiModelRow) {
    ragOpenAiModelRow.classList.toggle('hidden', provider !== 'openai');
  }

  if (ragEmbeddingBaseRow) {
    ragEmbeddingBaseRow.classList.toggle('hidden', provider !== 'ollama');
  }

  if (ragVllmBaseRow) {
    ragVllmBaseRow.classList.toggle('hidden', provider !== 'vllm');
  }

  if (ragOpenAiKeyInput && provider !== 'openai') {
    ragOpenAiKeyInput.value = '';
  }

  if (ragOpenAiKeyStatus) {
    ragOpenAiKeyStatus.textContent = hasOpenAiKey
      ? 'OpenAI key already saved in localStorage.'
      : 'Stored in localStorage for this browser.';
    setStatusState(ragOpenAiKeyStatus, hasOpenAiKey ? 'success' : null);
  }

  if (ragEmbeddingHint) {
    if (provider === 'ollama') {
      ragEmbeddingHint.textContent = `Ollama must be running. Model: ${ollamaModel}`;
    } else if (provider === 'vllm') {
      ragEmbeddingHint.textContent = `vLLM OpenAI-compatible embeddings. Model: ${vllmModel}`;
    } else if (provider === 'openai') {
      ragEmbeddingHint.textContent = hasOpenAiKey
        ? `Using stored OpenAI API key (${openAiModel}).`
        : 'OpenAI API key required. Enter it below.';
    }
  }

  if (ragEmbeddingStatus) {
    if (provider === 'openai' || provider === 'ollama' || provider === 'vllm') {
      ragEmbeddingStatus.textContent = `Embedding: ${providerLabel} · ${embeddingModel}`;
    } else {
      ragEmbeddingStatus.textContent = `Embedding: ${providerLabel}`;
    }
  }

  // Auto-sync embedding dimension fields with the selected model defaults.
  // When resetDimensions is true (model/provider change), reset the current
  // provider's dimension to its model default.  Otherwise, only populate empty fields
  // so that user-customised values (e.g. Matryoshka 2000) are preserved across UI refreshes.
  if (resetDimensions) {
    try {
      const defaults = getEmbeddingDefaults(provider);
      if (provider === 'openai' && ragOpenAiDimensions) ragOpenAiDimensions.value = defaults.dims;
      if (provider === 'ollama' && ragOllamaDimensions) ragOllamaDimensions.value = defaults.dims;
      if (provider === 'vllm' && ragVllmDimensions) ragVllmDimensions.value = defaults.dims;
    } catch { /* model not yet selected */ }
  }
  try {
    if (ragOpenAiDimensions && !ragOpenAiDimensions.value?.trim()) ragOpenAiDimensions.value = getEmbeddingDefaults('openai').dims;
  } catch { /* ignore */ }
  try {
    if (ragOllamaDimensions && !ragOllamaDimensions.value?.trim()) ragOllamaDimensions.value = getEmbeddingDefaults('ollama').dims;
  } catch { /* ignore */ }
  try {
    if (ragVllmDimensions && !ragVllmDimensions.value?.trim()) ragVllmDimensions.value = getEmbeddingDefaults('vllm').dims;
  } catch { /* ignore */ }

  // Set vector store dimension defaults (only if empty)
  const embDims = getEmbeddingDefaults(provider).dims;
  if (ragPgDimension && !ragPgDimension.value?.trim()) ragPgDimension.value = String(embDims);
  if (ragQdrantDimension && !ragQdrantDimension.value?.trim()) ragQdrantDimension.value = String(embDims);

  updateRunState();
}

export async function testOllamaConnection() {
  if (!ragOllamaTest) return;
  ragOllamaTest.disabled = true;
  if (ragOllamaStatus) {
    ragOllamaStatus.textContent = 'Testing...';
    setStatusState(ragOllamaStatus, null);
  }

  const baseUrl = ragEmbeddingBaseUrl?.value?.trim();
  const model = ragOllamaModel?.value?.trim();
  if (!baseUrl || !model) {
    if (ragOllamaStatus) {
      ragOllamaStatus.textContent = 'Ollama base URL and model are required.';
      setStatusState(ragOllamaStatus, 'error');
    }
    ragOllamaTest.disabled = false;
    return;
  }

  try {
    const res = await fetch('/api/rag/ollama-test', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ baseUrl, model })
    });
    const data = await res.json().catch(() => null);

    if (!res.ok) {
      if (ragOllamaStatus) {
        ragOllamaStatus.textContent = data?.error || 'Connection failed.';
        setStatusState(ragOllamaStatus, 'error');
      }
      return;
    }

    if (data.modelFound) {
      if (ragOllamaStatus) {
        ragOllamaStatus.textContent = `✅ Connected · ${model} available`;
        setStatusState(ragOllamaStatus, 'success');
      }
    } else {
      if (ragOllamaStatus) {
        ragOllamaStatus.textContent = `⚠️ Ollama reachable but "${model}" not found. Run: ollama pull ${model}`;
        setStatusState(ragOllamaStatus, 'warning');
      }
    }
  } catch (err) {
    if (ragOllamaStatus) {
      ragOllamaStatus.textContent = `❌ ${err.message || 'Network error'}`;
      setStatusState(ragOllamaStatus, 'error');
    }
  } finally {
    ragOllamaTest.disabled = false;
  }
}

export async function testVllmConnection() {
  if (!ragVllmTest) return;
  ragVllmTest.disabled = true;
  if (ragVllmStatus) {
    ragVllmStatus.textContent = 'Testing...';
    setStatusState(ragVllmStatus, null);
  }

  const baseUrl = ragVllmBaseUrl?.value?.trim();
  const model = ragVllmModel?.value?.trim();
  const dimensions = parseInt(ragVllmDimensions?.value, 10) || 0;
  if (!baseUrl || !model) {
    if (ragVllmStatus) {
      ragVllmStatus.textContent = 'vLLM base URL and model are required.';
      setStatusState(ragVllmStatus, 'error');
    }
    ragVllmTest.disabled = false;
    return;
  }

  try {
    const res = await fetch('/api/rag/vllm-test', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ baseUrl, model, dimensions })
    });
    const data = await res.json().catch(() => null);

    if (!res.ok) {
      if (ragVllmStatus) {
        ragVllmStatus.textContent = data?.error || 'Connection failed.';
        setStatusState(ragVllmStatus, 'error');
      }
      return;
    }

    if (ragVllmStatus) {
      ragVllmStatus.textContent = `✅ Connected · ${model} available`;
      setStatusState(ragVllmStatus, 'success');
    }
  } catch (err) {
    if (ragVllmStatus) {
      ragVllmStatus.textContent = `❌ ${err.message || 'Network error'}`;
      setStatusState(ragVllmStatus, 'error');
    }
  } finally {
    ragVllmTest.disabled = false;
  }
}

export function saveInlineOpenAiKey() {
  if (!ragOpenAiKeyInput) return;
  const key = ragOpenAiKeyInput.value.trim();
  if (!key) return;

  providerKeys.OpenAI = key;
  saveKeysToStorage();
  refreshProviderGroup('OpenAI');

  if (ragOpenAiKeyStatus) {
    ragOpenAiKeyStatus.textContent = 'Key saved for OpenAI (localStorage).';
    setStatusState(ragOpenAiKeyStatus, 'success');
  }
  if (ragOpenAiKeySave) {
    ragOpenAiKeySave.disabled = true;
  }

  updateEmbeddingUI();
}
