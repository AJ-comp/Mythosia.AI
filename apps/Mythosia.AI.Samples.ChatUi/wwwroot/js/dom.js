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
export const ragEmbeddingStatus = $('#rag-embedding-status');
export const vectordbChatStatus = $('#vectordb-chat-status');
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

// State message JSON modal
export const stateMessageJsonModal = $('#state-message-json-modal');
export const stateMessageJsonContent = $('#state-message-json-content');
export const stateMessageJsonClose = $('#state-message-json-close');
export const stateMessageJsonCopy = $('#state-message-json-copy');

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

// Alibaba Settings Modal
export const alibabaModal        = $('#alibaba-settings-modal');
export const alibabaModalClose   = $('#alibaba-modal-close');
export const alibabaTabs         = $$('.alibaba-tab');
export const alibabaPanels       = $$('.alibaba-panel');
export const alibabaBaseUrl      = $('#alibaba-base-url');
export const alibabaPlatform     = $('#alibaba-platform');
export const alibabaModelOverrideEnabled = $('#alibaba-model-override-enabled');
export const alibabaModelOverrideFields = $('#alibaba-model-override-fields');
export const alibabaModelOverride = $('#alibaba-model-override');
export const alibabaSave         = $('#alibaba-save');
export const alibabaRemove       = $('#alibaba-remove');
export const alibabaStatus       = $('#alibaba-status');

// RAG Reference Modal
export const ragModal        = $('#rag-modal');
export const ragModalClose   = $('#rag-modal-close');
export const ragSettingsModal = $('#rag-settings-modal');
export const ragSettingsBackdrop = $('#rag-settings-backdrop');
export const ragSettingsClose = $('#rag-settings-close');
export const ragSettingsExportPdf = $('#rag-settings-export-pdf');
export const ragSettingsSave  = $('#rag-settings-save');
export const ragSettingsAlert = $('#rag-settings-alert');
export const ragSettingsStatus = $('#rag-settings-status');
export const ragFiles        = $('#rag-files');
export const ragFileList     = $('#rag-file-list');
export const ragHistoryList  = $('#rag-history-list');
export const ragChunkSize    = $('#rag-chunk-size');
export const ragChunkOverlap = $('#rag-chunk-overlap');
export const ragChunker      = $('#rag-chunker');
export const ragEmbeddingProvider = $('#rag-embedding-provider');
export const ragEmbeddingBaseRow = $('#rag-embedding-base-row');
export const ragEmbeddingBaseUrl = $('#rag-embedding-base-url');
export const ragVllmBaseRow = $('#rag-vllm-base-row');
export const ragVllmBaseUrl = $('#rag-vllm-base-url');
export const ragEmbeddingSelect = $('#rag-embedding-select');
export const ragEmbeddingTrigger = $('#rag-embedding-trigger');
export const ragEmbeddingMenu = $('#rag-embedding-menu');
export const ragEmbeddingValue = $('#rag-embedding-value');
export const ragEmbeddingValueBadge = $('#rag-embedding-value-badge');
export const ragEmbeddingHint = $('#rag-embedding-hint');
export const ragOllamaModelRow = $('#rag-ollama-model-row');
export const ragOllamaModel = $('#rag-ollama-model');
export const ragOllamaDimensions = $('#rag-ollama-dimensions');
export const ragOllamaTest = $('#rag-ollama-test');
export const ragOllamaStatus = $('#rag-ollama-status');
export const ragVllmModelRow = $('#rag-vllm-model-row');
export const ragVllmModel = $('#rag-vllm-model');
export const ragVllmDimensions = $('#rag-vllm-dimensions');
export const ragVllmTest = $('#rag-vllm-test');
export const ragVllmStatus = $('#rag-vllm-status');
export const ragOpenAiModelRow = $('#rag-openai-model-row');
export const ragOpenAiModel = $('#rag-openai-model');
export const ragOpenAiDimensions = $('#rag-openai-dimensions');
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
export const ragQueryRewriter  = $('#rag-query-rewriter');
export const ragRewriterMaxTokens   = $('#rag-rewriter-max-tokens');
export const ragExtractKeywords     = $('#rag-extract-keywords');
export const ragRewriterOverride    = $('#rag-rewriter-override');
export const ragRewriterOverrideRow = $('#rag-rewriter-override-row');
export const ragRewriterModelRow    = $('#rag-rewriter-model-row');
export const ragRewriterModel       = $('#rag-rewriter-model');
export const ragRewriterOptions     = $('#rag-rewriter-options');
export const ragHybridSearch        = $('#rag-hybrid-search');
export const ragHybridOptions       = $('#rag-hybrid-options');
export const ragHybridWeight        = $('#rag-hybrid-weight');
export const ragHybridWeightVal     = $('#rag-hybrid-weight-val');
export const ragRerankEnabled       = $('#rag-rerank-enabled');
export const ragRerankOptions       = $('#rag-rerank-options');
export const ragRerankProvider      = $('#rag-rerank-provider');
export const ragRerankVllmModelRow  = $('#rag-rerank-vllm-model-row');
export const ragRerankVllmModel     = $('#rag-rerank-vllm-model');
export const ragRerankVllmBaseUrlRow = $('#rag-rerank-vllm-baseurl-row');
export const ragRerankVllmBaseUrl   = $('#rag-rerank-vllm-baseurl');
export const ragRerankVllmTest      = $('#rag-rerank-vllm-test');
export const ragRerankVllmStatus    = $('#rag-rerank-vllm-status');
export const ragRerankApiKeyRow     = $('#rag-rerank-apikey-row');
export const ragRerankApiKey        = $('#rag-rerank-apikey');
export const ragRetrievalMultiplier = $('#rag-retrieval-multiplier');
export const ragRerankCandidateTopK = $('#rag-rerank-candidate-topk');
export const ragMinScoreDivider     = $('#rag-min-score-divider');
export const ragRerankDerivedMinScore = $('#rag-rerank-derived-min-score');
export const ragRetrievalTopK       = $('#rag-retrieval-topk');
export const ragRetrievalMinScore   = $('#rag-retrieval-min-score');
export const ragFinalSelectionMode  = $('#rag-final-selection-mode');
export const ragFinalSelectionWeightRow = $('#rag-final-selection-weight-row');
export const ragFinalSelectionWeight = $('#rag-final-selection-weight');
export const ragFinalSelectionWeightVal = $('#rag-final-selection-weight-val');

// RAG Vector Store
export const ragVectorStoreProvider = $('#rag-vectorstore-provider');
export const ragVectorStoreHint = $('#rag-vectorstore-hint');
export const ragPgConfig     = $('#rag-pg-config');
export const ragPgHost       = $('#rag-pg-host');
export const ragPgPort       = $('#rag-pg-port');
export const ragPgDatabase   = $('#rag-pg-database');
export const ragPgUser       = $('#rag-pg-user');
export const ragPgPassword   = $('#rag-pg-password');
export const ragPgTable      = $('#rag-pg-table');
export const ragPgSchema     = $('#rag-pg-schema');
export const ragPgDimension  = $('#rag-pg-dimension');
export const ragPgEnsureSchema = $('#rag-pg-ensure-schema');
export const ragPgConnect    = $('#rag-pg-connect');
export const ragPgDisconnect = $('#rag-pg-disconnect');
export const ragPgStatus     = $('#rag-pg-status');
export const ragPgWarnings   = $('#rag-pg-warnings');
export const ragQdrantConfig    = $('#rag-qdrant-config');
export const ragQdrantHost      = $('#rag-qdrant-host');
export const ragQdrantPort      = $('#rag-qdrant-port');
export const ragQdrantApiKey    = $('#rag-qdrant-apikey');
export const ragQdrantDimension = $('#rag-qdrant-dimension');
export const ragQdrantCollection = $('#rag-qdrant-collection');
export const ragQdrantUseTls    = $('#rag-qdrant-usetls');
export const ragQdrantConnect   = $('#rag-qdrant-connect');
export const ragQdrantDisconnect = $('#rag-qdrant-disconnect');
export const ragQdrantStatus    = $('#rag-qdrant-status');
export const ragQdrantWarnings  = $('#rag-qdrant-warnings');
export const ragPineconeConfig     = $('#rag-pinecone-config');
export const ragPineconeIndexHost  = $('#rag-pinecone-index-host');
export const ragPineconeApiKey     = $('#rag-pinecone-apikey');
export const ragPineconeNamespace  = $('#rag-pinecone-namespace');
export const ragPineconeConnect    = $('#rag-pinecone-connect');
export const ragPineconeDisconnect = $('#rag-pinecone-disconnect');
export const ragPineconeStatus     = $('#rag-pinecone-status');
export const ragPineconeWarnings   = $('#rag-pinecone-warnings');

// RAG Embedding Progress & Result
export const ragEmbedProgress        = $('#rag-embed-progress');
export const ragEmbedProgressContent = $('#rag-embed-progress-content');
export const ragEmbedResultModal     = $('#rag-embed-result-modal');
export const ragEmbedResultClose     = $('#rag-embed-result-close');
export const ragEmbedResultTrace     = $('#rag-embed-result-trace');
export const ragResultViewCode       = $('#rag-result-view-code');

// RAG History Trace Slide Panel
export const ragTracePanel           = $('#rag-trace-panel');
export const ragTraceBackdrop        = $('#rag-trace-backdrop');
export const ragTracePanelClose      = $('#rag-trace-panel-close');
export const ragTracePanelTitle      = $('#rag-trace-panel-title');
export const ragTracePanelContent    = $('#rag-trace-panel-content');

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
