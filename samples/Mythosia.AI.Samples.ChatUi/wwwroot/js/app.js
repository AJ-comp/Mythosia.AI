// ═══════════════════════════════════════════════════════════════
// Mythosia.AI Chat UI — Entry Point
// ═══════════════════════════════════════════════════════════════

import { chatMessages } from './dom.js';
import { app, loadKeysFromStorage, isNearBottom } from './state.js';
import { loadModels, refreshProviderGroup, deselectModel } from './models.js';
import { initApiKeyModal } from './apikey-modal.js';
import { initChat } from './chat.js';
import { initSettings } from './settings.js';
import { initStatePanel } from './state-panel.js';
import { initCodeModal } from './code-modal.js';
import { initCustomSelects } from './custom-select.js';
import { initFunctionsPanel } from './functions-panel.js';
import { initRagReference } from './rag-reference.js';
import { initRagDiagnostics } from './rag-diagnostics.js';

// ── Scroll tracking ──────────────────────────────────────────
chatMessages.addEventListener('scroll', () => {
  if (app.isSending) app.shouldAutoScroll = isNearBottom();
});

// ── Boot ─────────────────────────────────────────────────────
// Load API keys BEFORE module init so that auto-reconnect flows
// (e.g., Postgres vector-store) can include the OpenAI key.
loadKeysFromStorage();

// ── Initialize all modules ───────────────────────────────────
initApiKeyModal(refreshProviderGroup, deselectModel);
initChat();
initSettings();
initStatePanel();
initCodeModal();
initCustomSelects();
initFunctionsPanel();
initRagReference();
initRagDiagnostics();

loadModels();
