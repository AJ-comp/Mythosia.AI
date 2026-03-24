// ═══════════════════════════════════════════════════════════════
// RAG Reference — Main Init & Modals
// ═══════════════════════════════════════════════════════════════

import {
  btnDocReference,
  btnRagSettings,
  ragModal,
  ragModalClose,
  ragSettingsModal,
  ragSettingsBackdrop,
  ragSettingsClose,
  ragSettingsExportPdf,
  ragSettingsSave,
  ragFiles,
  ragChunkSize,
  ragChunkOverlap,
  ragChunker,
  ragEmbeddingProvider,
  ragEmbeddingBaseUrl,
  ragOllamaModel,
  ragOllamaTest,
  ragVllmBaseUrl,
  ragVllmModel,
  ragVllmTest,
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
  ragRerankProvider,
  ragRerankVllmModel,
  ragRerankVllmBaseUrl,
  ragRerankVllmTest,
  ragFinalSelectionMode,
  ragFinalSelectionWeight,
  ragRetrievalMultiplier,
  ragMinScoreDivider,
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
  ragPineconeDisconnect,
  ragEmbedResultModal,
  ragEmbedResultClose,
  ragResultViewCode,
  ragTracePanel,
  ragTraceBackdrop,
  ragTracePanelClose
} from './dom.js';
import { ragState, markReferenceStale, setViewCodeEnabled } from './rag-shared.js';
import { updateEmbeddingUI, testOllamaConnection, testVllmConnection, saveInlineOpenAiKey } from './rag-embedding.js';
import { updateFileList, runReference, refreshRagStatus, refreshReferenceHistory, openRagCodeModal, closeTracePanel } from './rag-run.js';
import { loadPipelineSettings, savePipelineSettings, exportPipelineSettingsPdf, testVllmRerankConnection, updateRewriterUI, updateRewriterOverrideUI, updateHybridUI, updateHybridWeightDisplay, updateRerankUI, updateFinalSelectionUI, updateFinalSelectionWeightDisplay, updateRerankCandidateTopKDisplay, updateRerankDerivedMinScoreDisplay, updateRetrievalParamsDisplay } from './rag-pipeline.js';
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
  ragSettingsBackdrop?.addEventListener('click', () => closeSettingsModal());
  ragSettingsSave?.addEventListener('click', savePipelineSettings);
  ragSettingsExportPdf?.addEventListener('click', exportPipelineSettingsPdf);

  // ── Embedding Result modal controls ─────────────────────────
  ragEmbedResultClose?.addEventListener('click', () => closeEmbedResultModal());
  ragEmbedResultModal?.addEventListener('click', (e) => {
    if (e.target === ragEmbedResultModal) closeEmbedResultModal();
  });
  ragResultViewCode?.addEventListener('click', openRagCodeModal);

  // ── History Trace slide panel controls ──────────────────────
  ragTracePanelClose?.addEventListener('click', closeTracePanel);
  ragTraceBackdrop?.addEventListener('click', closeTracePanel);

  // ── Pipeline settings controls ─────────────────────────────
  ragQueryRewriter?.addEventListener('change', updateRewriterUI);
  ragRewriterOverride?.addEventListener('change', updateRewriterOverrideUI);
  ragHybridSearch?.addEventListener('change', updateHybridUI);
  ragHybridWeight?.addEventListener('input', updateHybridWeightDisplay);
  ragRerankEnabled?.addEventListener('change', updateRerankUI);
  ragRerankProvider?.addEventListener('change', () => {
    updateRerankUI();
    markReferenceStale();
  });
  ragRerankVllmModel?.addEventListener('change', markReferenceStale);
  ragRerankVllmBaseUrl?.addEventListener('input', markReferenceStale);
  ragRerankVllmTest?.addEventListener('click', testVllmRerankConnection);
  ragFinalSelectionMode?.addEventListener('change', updateFinalSelectionUI);
  ragFinalSelectionWeight?.addEventListener('input', updateFinalSelectionWeightDisplay);

  // ── Embedding controls ─────────────────────────────────────
  ragFiles.addEventListener('change', updateFileList);
  ragEmbeddingProvider?.addEventListener('change', () => {
    updateEmbeddingUI(true);
    markReferenceStale();
  });
  ragOpenAiModel?.addEventListener('change', () => {
    updateEmbeddingUI(true);
    markReferenceStale();
  });
  ragOllamaModel?.addEventListener('change', () => {
    updateEmbeddingUI(true);
    markReferenceStale();
  });
  ragVllmModel?.addEventListener('change', () => {
    updateEmbeddingUI(true);
    markReferenceStale();
  });
  ragOllamaTest?.addEventListener('click', testOllamaConnection);
  ragVllmTest?.addEventListener('click', testVllmConnection);
  ragEmbeddingBaseUrl?.addEventListener('input', markReferenceStale);
  ragVllmBaseUrl?.addEventListener('input', markReferenceStale);
  ragTopK?.addEventListener('input', () => {
    updateRerankCandidateTopKDisplay();
    updateRetrievalParamsDisplay();
    markReferenceStale();
  });
  ragMinScore?.addEventListener('input', () => {
    updateRerankDerivedMinScoreDisplay();
    updateRetrievalParamsDisplay();
    markReferenceStale();
  });
  ragPromptTemplate?.addEventListener('input', markReferenceStale);
  ragRerankEnabled?.addEventListener('change', () => {
    updateRerankCandidateTopKDisplay();
    updateRerankDerivedMinScoreDisplay();
    updateRetrievalParamsDisplay();
  });
  ragRetrievalMultiplier?.addEventListener('input', () => {
    updateRerankCandidateTopKDisplay();
    updateRetrievalParamsDisplay();
    markReferenceStale();
  });
  ragMinScoreDivider?.addEventListener('input', () => {
    updateRerankDerivedMinScoreDisplay();
    updateRetrievalParamsDisplay();
    markReferenceStale();
  });
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

  // ── Accordion collapse for pipeline steps ──────────────────
  initPipelineAccordion();
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
  ragSettingsModal.classList.add('open');
  ragSettingsBackdrop?.classList.add('open');
  loadPipelineSettings();
}

function closeSettingsModal() {
  ragSettingsModal?.classList.remove('open');
  ragSettingsBackdrop?.classList.remove('open');
}

function closeModal() {
  ragModal.classList.add('hidden');
}

function closeEmbedResultModal() {
  ragEmbedResultModal?.classList.add('hidden');
}

// ── Pipeline accordion ──────────────────────────────────────
function initPipelineAccordion() {
  const panel = document.getElementById('rag-settings-modal');
  if (!panel) return;
  const steps = panel.querySelectorAll('.pipe-step');
  steps.forEach(step => {
    const header = step.querySelector('.pipe-step-header');
    if (!header) return;
    header.addEventListener('click', (e) => {
      if (e.target.closest('.rag-settings-section-toggle, input, label, select, button')) return;
      step.classList.toggle('collapsed');
    });
  });
}
