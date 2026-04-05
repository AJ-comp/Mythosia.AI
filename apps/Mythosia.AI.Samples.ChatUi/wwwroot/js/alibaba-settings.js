// ═══════════════════════════════════════════════════════════════
// Alibaba Settings Modal
// ═══════════════════════════════════════════════════════════════

import {
  alibabaModal, alibabaModalClose, alibabaTabs, alibabaPanels,
  alibabaBaseUrl, alibabaPlatform, alibabaModelOverrideEnabled,
  alibabaModelOverrideFields, alibabaModelOverride,
  alibabaSave, alibabaRemove, alibabaStatus
} from './dom.js';
import { alibabaSettings, saveAlibabaSettings } from './state.js';

export function openAlibabaSettingsModal() {
  alibabaBaseUrl.value = alibabaSettings.baseUrl || '';
  alibabaPlatform.value = alibabaSettings.platform || '';
  alibabaModelOverrideEnabled.checked = !!alibabaSettings.modelOverrideEnabled;
  alibabaModelOverride.value = alibabaSettings.modelIdOverride || '';
  toggleOverrideFields();
  alibabaStatus.classList.add('hidden');
  alibabaStatus.className = 'modal-key-status hidden';
  validateInputs();
  alibabaRemove.style.display = alibabaSettings.baseUrl ? '' : 'none';

  // Ensure custom tab is active
  alibabaTabs.forEach(t => t.classList.toggle('active', t.dataset.tab === 'custom'));
  alibabaPanels.forEach(p => p.classList.toggle('active', p.id === 'alibaba-panel-custom'));

  alibabaModal.classList.remove('hidden');
  setTimeout(() => alibabaBaseUrl.focus(), 50);
}

function closeAlibabaModal() {
  alibabaModal.classList.add('hidden');
}

export function initAlibabaSettings(refreshProviderGroup, deselectModel) {
  // Tab switching
  alibabaTabs.forEach(tab => {
    tab.addEventListener('click', () => {
      if (tab.disabled) return;
      alibabaTabs.forEach(t => t.classList.remove('active'));
      alibabaPanels.forEach(p => p.classList.remove('active'));
      tab.classList.add('active');
      const panel = document.getElementById(`alibaba-panel-${tab.dataset.tab}`);
      if (panel) panel.classList.add('active');
    });
  });

  // Input validation
  alibabaBaseUrl.addEventListener('input', validateInputs);
  alibabaModelOverrideEnabled.addEventListener('change', () => {
    toggleOverrideFields();
    validateInputs();
  });
  alibabaModelOverride.addEventListener('input', validateInputs);

  // Close
  alibabaModalClose.addEventListener('click', closeAlibabaModal);
  alibabaModal.addEventListener('click', (e) => {
    if (e.target === alibabaModal) closeAlibabaModal();
  });

  // Remove
  alibabaRemove.addEventListener('click', () => {
    alibabaSettings.baseUrl = '';
    alibabaSettings.platform = '';
    alibabaSettings.modelOverrideEnabled = false;
    alibabaSettings.modelIdOverride = '';
    saveAlibabaSettings();
    refreshProviderGroup('Alibaba');
    if (app.selectedProvider === 'Alibaba') deselectModel();
    closeAlibabaModal();
  });

  // Save
  alibabaSave.addEventListener('click', () => {
    const baseUrl = alibabaBaseUrl.value.trim();
    const platform = alibabaPlatform.value.trim();
    if (!baseUrl || !platform) return;

    alibabaSettings.baseUrl = baseUrl;
    alibabaSettings.platform = platform;
    alibabaSettings.modelOverrideEnabled = alibabaModelOverrideEnabled.checked;
    alibabaSettings.modelIdOverride = alibabaModelOverride.value.trim();
    saveAlibabaSettings();
    refreshProviderGroup('Alibaba');

    alibabaStatus.textContent = 'Settings saved (localStorage)';
    alibabaStatus.className = 'modal-key-status success';
    alibabaRemove.style.display = '';
  });
}

// Need app reference for remove handler
import { app } from './state.js';

function toggleOverrideFields() {
  const enabled = alibabaModelOverrideEnabled.checked;
  alibabaModelOverrideFields.classList.toggle('hidden', !enabled);
}

function validateInputs() {
  const baseUrlOk = !!alibabaBaseUrl.value.trim();
  const platformOk = !!alibabaPlatform.value.trim();
  const overrideOk = !alibabaModelOverrideEnabled.checked || !!alibabaModelOverride.value.trim();
  alibabaSave.disabled = !(baseUrlOk && platformOk && overrideOk);
}
