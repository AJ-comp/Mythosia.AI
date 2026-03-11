// ═══════════════════════════════════════════════════════════════
// RAG Embedding Provider UI
// ═══════════════════════════════════════════════════════════════

import {
  ragEmbeddingProvider,
  ragEmbeddingBaseRow,
  ragEmbeddingBaseUrl,
  ragEmbeddingHint,
  ragOllamaModelRow,
  ragOllamaModel,
  ragOllamaTest,
  ragOllamaStatus,
  ragOpenAiModelRow,
  ragOpenAiModel,
  ragOpenAiKey,
  ragOpenAiKeyInput,
  ragOpenAiKeySave,
  ragOpenAiKeyStatus,
  ragPgDimension,
  ragQdrantDimension
} from './dom.js';
import { providerKeys, saveKeysToStorage } from './state.js';
import { refreshProviderGroup } from './models.js';
import { updateRunState } from './rag-shared.js';

export function getSelectedEmbeddingProvider() {
  return ragEmbeddingProvider?.value || 'openai';
}

export function getEmbeddingDefaults(provider) {
  const p = provider || getSelectedEmbeddingProvider();
  if (p === 'openai') {
    const model = ragOpenAiModel?.value || 'text-embedding-3-small';
    const dimsMap = {
      'text-embedding-3-small': 1536,
      'text-embedding-3-large': 3072,
      'text-embedding-ada-002': 1536
    };
    return { model, dims: dimsMap[model] || 1536 };
  }
  if (p === 'ollama') {
    const model = ragOllamaModel?.value || 'qwen3-embedding:4b';
    const dimsMap = {
      'qwen3-embedding:0.6b': 1024,
      'qwen3-embedding:4b':   2560,
      'qwen3-embedding:8b':   4096
    };
    return { model, dims: dimsMap[model] || 2560 };
  }
  return { model: 'text-embedding-3-small', dims: 1536 };
}

export function updateEmbeddingUI() {
  const provider = getSelectedEmbeddingProvider();
  const hasOpenAiKey = !!providerKeys?.OpenAI;

  if (ragOpenAiKey) {
    ragOpenAiKey.classList.toggle('hidden', provider !== 'openai' || hasOpenAiKey);
  }

  if (ragOllamaModelRow) {
    ragOllamaModelRow.classList.toggle('hidden', provider !== 'ollama');
  }

  if (ragOpenAiModelRow) {
    ragOpenAiModelRow.classList.toggle('hidden', provider !== 'openai');
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
      const ollamaModel = ragOllamaModel?.value || 'qwen3-embedding:4b';
      ragEmbeddingHint.textContent = `Ollama must be running. Model: ${ollamaModel}`;
    } else if (provider === 'openai') {
      const modelName = ragOpenAiModel?.value || 'text-embedding-3-small';
      ragEmbeddingHint.textContent = hasOpenAiKey
        ? `Using stored OpenAI API key (${modelName}).`
        : 'OpenAI API key required. Enter it below.';
    }
  }

  // Auto-sync vector store dimension fields with the selected embedding model
  const embDims = getEmbeddingDefaults(provider).dims;
  if (ragPgDimension) ragPgDimension.value = embDims;
  if (ragQdrantDimension) ragQdrantDimension.value = embDims;

  updateRunState();
}

export async function testOllamaConnection() {
  if (!ragOllamaTest) return;
  ragOllamaTest.disabled = true;
  if (ragOllamaStatus) ragOllamaStatus.textContent = 'Testing...';

  const baseUrl = ragEmbeddingBaseUrl?.value?.trim() || 'http://localhost:11434';
  const model = ragOllamaModel?.value || 'qwen3-embedding:4b';

  try {
    const res = await fetch('/api/rag/ollama-test', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ baseUrl, model })
    });
    const data = await res.json().catch(() => null);

    if (!res.ok) {
      if (ragOllamaStatus) ragOllamaStatus.textContent = data?.error || 'Connection failed.';
      return;
    }

    if (data.modelFound) {
      if (ragOllamaStatus) ragOllamaStatus.textContent = `✅ Connected · ${model} available`;
    } else {
      if (ragOllamaStatus) ragOllamaStatus.textContent = `⚠️ Ollama reachable but "${model}" not found. Run: ollama pull ${model}`;
    }
  } catch (err) {
    if (ragOllamaStatus) ragOllamaStatus.textContent = `❌ ${err.message || 'Network error'}`;
  } finally {
    ragOllamaTest.disabled = false;
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
  }
  if (ragOpenAiKeySave) {
    ragOpenAiKeySave.disabled = true;
  }

  updateEmbeddingUI();
}
