// ═══════════════════════════════════════════════════════════════
// Shared Application State
// ═══════════════════════════════════════════════════════════════

import { chatMessages, chatInput, btnSend, sidebarLeft } from './dom.js';

// Mutable state — shared across all modules via this object
export const app = {
  selectedModel: null,
  selectedProvider: null,
  isConnected: false,
  isSending: false,
  statePollingTimer: null,
  modelReasoningInfo: null,
  shouldAutoScroll: true,
  modalTargetProvider: null,
};

// ── Provider API Keys ────────────────────────────────────────
export const STORAGE_KEY = 'mythosia_ai_keys';
export const ALIBABA_STORAGE_KEY = 'mythosia_alibaba_settings';
export const providerKeys = {};
export const alibabaSettings = {
  baseUrl: '',
  platform: '',
  modelIdOverride: '',
  modelOverrideEnabled: false
};

export function saveKeysToStorage() {
  try { localStorage.setItem(STORAGE_KEY, JSON.stringify(providerKeys)); } catch(_) {}
}

export function loadKeysFromStorage() {
  try {
    const raw = localStorage.getItem(STORAGE_KEY);
    if (raw) Object.assign(providerKeys, JSON.parse(raw));
  } catch(_) {}
  try {
    const raw = localStorage.getItem(ALIBABA_STORAGE_KEY);
    if (raw) {
      Object.assign(alibabaSettings, JSON.parse(raw));
      if (!('modelIdOverride' in alibabaSettings)) alibabaSettings.modelIdOverride = '';
      if (!('modelOverrideEnabled' in alibabaSettings)) alibabaSettings.modelOverrideEnabled = false;
      if (!('platform' in alibabaSettings)) alibabaSettings.platform = '';
      if (alibabaSettings.baseUrl) {
        providerKeys['Alibaba'] = '__custom_endpoint__';
      }
    }
  } catch(_) {}
}

export function saveAlibabaSettings() {
  try { localStorage.setItem(ALIBABA_STORAGE_KEY, JSON.stringify(alibabaSettings)); } catch(_) {}
  if (alibabaSettings.baseUrl) {
    providerKeys['Alibaba'] = '__custom_endpoint__';
  } else {
    delete providerKeys['Alibaba'];
  }
}

// ── Auto-scroll ──────────────────────────────────────────────
export function isNearBottom(threshold = 60) {
  return chatMessages.scrollHeight - chatMessages.scrollTop - chatMessages.clientHeight < threshold;
}

export function autoScroll() {
  if (app.shouldAutoScroll) chatMessages.scrollTop = chatMessages.scrollHeight;
}

// ── Chat input helpers ───────────────────────────────────────
export function enableChatInput() {
  chatInput.disabled = false;
  btnSend.disabled = false;
  chatInput.focus();
}

export function disableChatInput() {
  chatInput.disabled = true;
  btnSend.disabled = true;
}

// ── Sidebar disable during streaming ─────────────────────────
export function updateSidebarDisabled(disabled) {
  sidebarLeft.classList.toggle('disabled', disabled);
}
