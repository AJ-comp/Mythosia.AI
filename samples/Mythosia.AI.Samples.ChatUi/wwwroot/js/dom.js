// ═══════════════════════════════════════════════════════════════
// DOM Element References
// ═══════════════════════════════════════════════════════════════

import { $, $$ } from './utils.js';

// Sidebar & Model list
export const modelListEl    = $('#model-list');
export const settingsArea   = $('#settings-area');
export const sidebarLeft    = $('#sidebar-left');

// Chat area
export const chatStatus     = $('#chat-status');
export const ragChatStatus  = $('#rag-chat-status');
export const chatMessages   = $('#chat-messages');
export const chatForm       = $('#chat-form');
export const chatInput      = $('#chat-input');
export const btnSend        = $('#btn-send');
export const btnClear       = $('#btn-clear');
export const btnDocReference = $('#btn-doc-reference');
export const btnRagSettings = $('#btn-rag-settings');

// Right panel
export const stateContainer = $('#state-container');
export const btnRefresh     = $('#btn-refresh-state');

// Code Modal
export const codeModal        = $('#code-modal');
export const codeModalContent = $('#code-modal-content');
export const codeModalClose   = $('#code-modal-close');
export const codeCopyAll      = $('#code-copy-all');

// API Key Modal
export const modalOverlay     = $('#apikey-modal');
export const modalTitle       = $('#modal-title');
export const modalProviderName = $('#modal-provider-name');
export const modalInput       = $('#modal-apikey-input');
export const modalToggle      = $('#modal-apikey-toggle');
export const modalKeyStatus   = $('#modal-key-status');
export const modalSave        = $('#modal-save');
export const modalCancel      = $('#modal-cancel');
export const modalClose       = $('#modal-close');
export const modalRemoveKey   = $('#modal-remove-key');

// RAG Reference Modal
export const ragModal        = $('#rag-modal');
export const ragModalClose   = $('#rag-modal-close');
export const ragSettingsModal = $('#rag-settings-modal');
export const ragSettingsClose = $('#rag-settings-close');
export const ragSettingsSave  = $('#rag-settings-save');
export const ragSettingsStatus = $('#rag-settings-status');
export const ragFiles        = $('#rag-files');
export const ragFileList     = $('#rag-file-list');
export const ragHistoryList  = $('#rag-history-list');
export const ragChunkSize    = $('#rag-chunk-size');
export const ragChunkOverlap = $('#rag-chunk-overlap');
export const ragChunker      = $('#rag-chunker');
export const ragEmbeddingProvider = $('#rag-embedding-provider');
export const ragEmbeddingModel = $('#rag-embedding-model');
export const ragEmbeddingDimensions = $('#rag-embedding-dimensions');
export const ragEmbeddingBaseRow = $('#rag-embedding-base-row');
export const ragEmbeddingBaseUrl = $('#rag-embedding-base-url');
export const ragEmbeddingSelect = $('#rag-embedding-select');
export const ragEmbeddingTrigger = $('#rag-embedding-trigger');
export const ragEmbeddingMenu = $('#rag-embedding-menu');
export const ragEmbeddingValue = $('#rag-embedding-value');
export const ragEmbeddingValueBadge = $('#rag-embedding-value-badge');
export const ragEmbeddingHint = $('#rag-embedding-hint');
export const ragOpenAiKey = $('#rag-openai-key');
export const ragOpenAiKeyInput = $('#rag-openai-key-input');
export const ragOpenAiKeySave = $('#rag-openai-key-save');
export const ragOpenAiKeyStatus = $('#rag-openai-key-status');
export const ragRun          = $('#rag-run');
export const ragViewCode     = $('#rag-view-code');
export const ragStatus       = $('#rag-status');
export const ragTrace        = $('#rag-trace');
export const ragTopK         = $('#rag-topk');
export const ragMinScore     = $('#rag-min-score');
export const ragPromptTemplate = $('#rag-prompt-template');

// RAG Diagnostics Modal
export const btnRagDiagnose   = $('#btn-rag-diagnose');
export const diagModal        = $('#rag-diag-modal');
export const diagModalClose   = $('#rag-diag-modal-close');
export const diagTabs         = $$('.diag-tab');
export const diagPanels       = $$('.diag-panel');
export const diagHealthBtn    = $('#diag-health-btn');
export const diagHealthResult = $('#diag-health-result');
export const diagWhyQuery     = $('#diag-why-query');
export const diagWhyExpected  = $('#diag-why-expected');
export const diagWhyBtn       = $('#diag-why-btn');
export const diagWhyResult    = $('#diag-why-result');
export const diagScoreQuery   = $('#diag-score-query');
export const diagScoreExpected = $('#diag-score-expected');
export const diagScoreBtn     = $('#diag-score-btn');
export const diagScoreResult  = $('#diag-score-result');

// Functions panel
export const functionsArea   = $('#functions-area');
export const fnList          = $('#fn-list');
export const fnPresetToggle  = $('#fn-preset-toggle');

// Settings
export const setSystem     = $('#set-system');
export const setTemp       = $('#set-temp');
export const setTopp       = $('#set-topp');
export const setMaxTokens  = $('#set-maxtokens');
export const setMaxMsg     = $('#set-maxmsg');
export const setStateless  = $('#set-stateless');
export const setReasoning  = $('#set-reasoning');
export const reasoningOpts = $('#reasoning-options');
export const reasoningLvls = $('#reasoning-levels');
export const tempVal       = $('#temp-val');
export const toppVal       = $('#topp-val');

// Summary Policy
export const setSummary         = $('#set-summary');
export const summaryOpts        = $('#summary-options');
export const summaryTriggerType = $('#summary-trigger-type');
export const summaryTriggerVal  = $('#summary-trigger-value');
export const summaryKeepVal     = $('#summary-keep-value');
export const summaryError       = $('#summary-error');
export const summaryCurrent     = $('#summary-current');
export const summaryText        = $('#summary-text');
export const summaryClear       = $('#summary-clear');
