// ═══════════════════════════════════════════════════════════════
// RAG Reference — Main Init & Modals
// ═══════════════════════════════════════════════════════════════

import {
  btnDocReference,
  btnRagSettings,
  ragModal,
  ragModalClose,
  ragSettingsModal,
  ragSettingsClose,
  ragSettingsSave,
  ragFiles,
  ragChunkSize,
  ragChunkOverlap,
  ragChunker,
  ragEmbeddingProvider,
  ragEmbeddingBaseUrl,
  ragOllamaModel,
  ragOllamaTest,
  ragOpenAiModel,
  ragOpenAiKeyInput,
  ragOpenAiKeySave,
  ragRun,
  ragViewCode,
  ragTopK,
  ragMinScore,
  ragPromptTemplate,
  ragQueryRewriter,
  ragRewriterOverride,
  ragHybridSearch,
  ragHybridWeight,
  ragRerankEnabled,
  ragVectorStoreProvider,
  ragPgHost,
  ragPgPort,
  ragPgDatabase,
  ragPgUser,
  ragPgPassword,
  ragPgConnect,
  ragPgDisconnect,
  ragQdrantHost,
  ragQdrantPort,
  ragQdrantConnect,
  ragQdrantDisconnect,
  ragPineconeIndexHost,
  ragPineconeApiKey,
  ragPineconeConnect,
  ragPineconeDisconnect
} from './dom.js';
import { ragState, markReferenceStale, setViewCodeEnabled } from './rag-shared.js';
import { updateEmbeddingUI, testOllamaConnection, saveInlineOpenAiKey } from './rag-embedding.js';
import { updateFileList, runReference, refreshRagStatus, refreshReferenceHistory, openRagCodeModal } from './rag-run.js';
import { loadPipelineSettings, savePipelineSettings, updateRewriterUI, updateRewriterOverrideUI, updateHybridUI, updateHybridWeightDisplay, updateRerankUI } from './rag-pipeline.js';
import { updateVectorStoreUI, loadVectorStoreConfig, updatePgConnectState, updateQdrantConnectState, connectPostgres, disconnectPostgres, connectQdrant, disconnectQdrant, updatePineconeConnectState, connectPinecone, disconnectPinecone } from './rag-vector-store.js';

export function initRagReference() {
  if (!btnDocReference || !ragModal) return;

  // ── Modal controls ─────────────────────────────────────────
  btnDocReference.addEventListener('click', () => openModal());
  btnRagSettings?.addEventListener('click', () => openSettingsModal());
  ragModalClose.addEventListener('click', () => closeModal());
  ragModal.addEventListener('click', (e) => {
    if (e.target === ragModal) closeModal();
  });
  ragSettingsClose?.addEventListener('click', () => closeSettingsModal());
  ragSettingsSave?.addEventListener('click', savePipelineSettings);

  // ── Pipeline settings controls ─────────────────────────────
  ragQueryRewriter?.addEventListener('change', updateRewriterUI);
  ragRewriterOverride?.addEventListener('change', updateRewriterOverrideUI);
  ragHybridSearch?.addEventListener('change', updateHybridUI);
  ragHybridWeight?.addEventListener('input', updateHybridWeightDisplay);
  ragRerankEnabled?.addEventListener('change', updateRerankUI);

  // ── Embedding controls ─────────────────────────────────────
  ragFiles.addEventListener('change', updateFileList);
  ragEmbeddingProvider?.addEventListener('change', () => {
    updateEmbeddingUI();
    markReferenceStale();
  });
  ragOpenAiModel?.addEventListener('change', () => {
    updateEmbeddingUI();
    markReferenceStale();
  });
  ragOllamaModel?.addEventListener('change', () => {
    updateEmbeddingUI();
    markReferenceStale();
  });
  ragOllamaTest?.addEventListener('click', testOllamaConnection);
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

  // ── Vector Store controls ──────────────────────────────────
  ragVectorStoreProvider?.addEventListener('change', () => {
    updateVectorStoreUI();
    markReferenceStale();
  });
  ragPgHost?.addEventListener('input', updatePgConnectState);
  ragPgPort?.addEventListener('input', updatePgConnectState);
  ragPgDatabase?.addEventListener('input', updatePgConnectState);
  ragPgUser?.addEventListener('input', updatePgConnectState);
  ragPgPassword?.addEventListener('input', updatePgConnectState);
  ragPgConnect?.addEventListener('click', connectPostgres);
  ragPgDisconnect?.addEventListener('click', disconnectPostgres);

  ragQdrantHost?.addEventListener('input', updateQdrantConnectState);
  ragQdrantPort?.addEventListener('input', updateQdrantConnectState);
  ragQdrantConnect?.addEventListener('click', connectQdrant);
  ragQdrantDisconnect?.addEventListener('click', disconnectQdrant);

  ragPineconeIndexHost?.addEventListener('input', updatePineconeConnectState);
  ragPineconeApiKey?.addEventListener('input', updatePineconeConnectState);
  ragPineconeConnect?.addEventListener('click', connectPinecone);
  ragPineconeDisconnect?.addEventListener('click', disconnectPinecone);

  // ── Initial state ──────────────────────────────────────────
  updateFileList();
  updateEmbeddingUI();
  updateVectorStoreUI();
  loadVectorStoreConfig();
  loadPipelineSettings();
  refreshRagStatus();
  refreshReferenceHistory();
}

// ── Modal helpers ────────────────────────────────────────────
function openModal() {
  ragModal.classList.remove('hidden');
  updateEmbeddingUI();
  setViewCodeEnabled(ragState.hasReferenceRun);
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
