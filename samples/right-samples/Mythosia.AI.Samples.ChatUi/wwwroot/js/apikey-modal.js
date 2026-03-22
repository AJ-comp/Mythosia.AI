// ═══════════════════════════════════════════════════════════════
// API Key Modal
// ═══════════════════════════════════════════════════════════════

import {
  modalOverlay, modalTitle, modalProviderName, modalInput,
  modalToggle, modalKeyStatus, modalSave, modalCancel,
  modalClose, modalRemoveKey
} from './dom.js';
import { app, providerKeys, saveKeysToStorage } from './state.js';

export function openKeyModal(provider) {
  app.modalTargetProvider = provider;
  modalTitle.textContent = `${provider} API Key`;
  modalProviderName.textContent = provider;
  modalInput.value = providerKeys[provider] || '';
  modalInput.type = 'password';
  modalKeyStatus.classList.add('hidden');
  modalKeyStatus.className = 'modal-key-status hidden';
  modalSave.disabled = !(modalInput.value.trim());
  modalRemoveKey.style.display = providerKeys[provider] ? '' : 'none';
  modalOverlay.classList.remove('hidden');
  setTimeout(() => modalInput.focus(), 50);
}

export function closeKeyModal() {
  modalOverlay.classList.add('hidden');
  app.modalTargetProvider = null;
  modalInput.value = '';
}

export function maskApiKey(key) {
  if (!key || key.length <= 8) return '••••••••';
  return key.substring(0, 4) + '••••••••' + key.substring(key.length - 4);
}

// ── Event listeners ──────────────────────────────────────────
export function initApiKeyModal(refreshProviderGroup, deselectModel) {
  modalInput.addEventListener('input', () => {
    modalSave.disabled = !modalInput.value.trim();
  });

  modalToggle.addEventListener('click', () => {
    modalInput.type = modalInput.type === 'password' ? 'text' : 'password';
  });

  modalClose.addEventListener('click', closeKeyModal);
  modalCancel.addEventListener('click', closeKeyModal);
  modalOverlay.addEventListener('click', (e) => {
    if (e.target === modalOverlay) closeKeyModal();
  });

  modalRemoveKey.addEventListener('click', () => {
    if (!app.modalTargetProvider) return;
    delete providerKeys[app.modalTargetProvider];
    saveKeysToStorage();
    refreshProviderGroup(app.modalTargetProvider);
    if (app.selectedProvider === app.modalTargetProvider) {
      deselectModel();
    }
    closeKeyModal();
  });

  modalSave.addEventListener('click', () => {
    const key = modalInput.value.trim();
    if (!key || !app.modalTargetProvider) return;
    providerKeys[app.modalTargetProvider] = key;
    saveKeysToStorage();
    refreshProviderGroup(app.modalTargetProvider);
    modalKeyStatus.textContent = `Key saved for ${app.modalTargetProvider} (localStorage)`;
    modalKeyStatus.className = 'modal-key-status success';
    modalRemoveKey.style.display = '';
  });
}
