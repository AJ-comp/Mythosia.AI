// ═══════════════════════════════════════════════════════════════
// RAG Shared State & Utilities
// ═══════════════════════════════════════════════════════════════

import {
  ragFiles,
  ragRun,
  ragViewCode,
  ragEmbeddingProvider,
  ragVectorStoreProvider
} from './dom.js';
import { providerKeys } from './state.js';

// ── Shared mutable state ─────────────────────────────────────
export const ragState = {
  pgConnected: false,
  qdrantConnected: false,
  pineconeConnected: false,
  hasReferenceRun: false,
  autoChunkerFromFiles: false
};

// ── Helpers ──────────────────────────────────────────────────
export function toInt(value, fallback) {
  const parsed = parseInt(value, 10);
  return Number.isFinite(parsed) && parsed > 0 ? parsed : fallback;
}

export function toFloatOrNull(value) {
  if (!value) return null;
  const parsed = parseFloat(value);
  return Number.isFinite(parsed) ? parsed : null;
}

export function setSelectValue(select, value) {
  if (!select) return;
  select.value = value;
  select.dispatchEvent(new Event('change', { bubbles: true }));
}

// ── View Code toggle ─────────────────────────────────────────
export function setViewCodeEnabled(enabled) {
  ragState.hasReferenceRun = enabled;
  if (ragViewCode) {
    ragViewCode.disabled = !enabled;
  }
}

export function markReferenceStale() {
  setViewCodeEnabled(false);
}

// ── Run button state ─────────────────────────────────────────
export function updateRunState(files) {
  const fileCount = files ? files.length : (ragFiles.files ? ragFiles.files.length : 0);
  const provider = ragEmbeddingProvider?.value || 'openai';
  const needsKey = provider !== 'ollama';
  const hasKey = !needsKey || !!providerKeys?.OpenAI;
  const vsProvider = ragVectorStoreProvider?.value || 'inmemory';
  const vsReady = vsProvider === 'inmemory'
    || (vsProvider === 'postgres' && ragState.pgConnected)
    || (vsProvider === 'qdrant' && ragState.qdrantConnected)
    || (vsProvider === 'pinecone' && ragState.pineconeConnected);
  ragRun.disabled = fileCount === 0 || !hasKey || !vsReady;
}
