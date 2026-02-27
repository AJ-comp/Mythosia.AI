// ═══════════════════════════════════════════════════════════════
// DOM Element References
// ═══════════════════════════════════════════════════════════════

import { $ } from './utils.js';

// Sidebar & Model list
export const modelListEl    = $('#model-list');
export const settingsArea   = $('#settings-area');
export const sidebarLeft    = $('#sidebar-left');

// Chat area
export const chatStatus     = $('#chat-status');
export const chatMessages   = $('#chat-messages');
export const chatForm       = $('#chat-form');
export const chatInput      = $('#chat-input');
export const btnSend        = $('#btn-send');
export const btnClear       = $('#btn-clear');

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
