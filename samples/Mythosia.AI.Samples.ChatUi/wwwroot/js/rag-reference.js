// ═══════════════════════════════════════════════════════════════
// RAG Reference Modal + Trace Viewer
// ═══════════════════════════════════════════════════════════════

import {
  btnDocReference,
  btnRagSettings,
  ragModal,
  ragModalClose,
  ragSettingsModal,
  ragSettingsClose,
  ragSettingsSave,
  ragSettingsStatus,
  ragFiles,
  ragFileList,
  ragChunkSize,
  ragChunkOverlap,
  ragChunker,
  ragEmbeddingProvider,
  ragEmbeddingBaseRow,
  ragEmbeddingBaseUrl,
  ragEmbeddingHint,
  ragOpenAiModelRow,
  ragOpenAiModel,
  ragOpenAiKey,
  ragOpenAiKeyInput,
  ragOpenAiKeySave,
  ragOpenAiKeyStatus,
  ragRun,
  ragViewCode,
  ragStatus,
  ragTrace,
  ragHistoryList,
  ragTopK,
  ragMinScore,
  ragPromptTemplate,
  ragChatStatus,
  ragVectorStoreProvider,
  ragVectorStoreHint,
  ragPgConfig,
  ragPgHost,
  ragPgPort,
  ragPgDatabase,
  ragPgUser,
  ragPgPassword,
  ragPgTable,
  ragPgSchema,
  ragPgDimension,
  ragPgEnsureSchema,
  ragPgConnect,
  ragPgDisconnect,
  ragPgStatus,
  codeModal,
  codeModalContent,
  codeCopyAll
} from './dom.js';
import { escapeHtml, truncate } from './utils.js';
import { providerKeys, saveKeysToStorage } from './state.js';
import { refreshProviderGroup } from './models.js';
import { setDiagnoseEnabled } from './rag-diagnostics.js';

export function initRagReference() {
  if (!btnDocReference || !ragModal) return;

  btnDocReference.addEventListener('click', () => openModal());
  btnRagSettings?.addEventListener('click', () => openSettingsModal());
  ragModalClose.addEventListener('click', () => closeModal());
  ragModal.addEventListener('click', (e) => {
    if (e.target === ragModal) closeModal();
  });
  ragSettingsClose?.addEventListener('click', () => closeSettingsModal());
  ragSettingsModal?.addEventListener('click', (e) => {
    if (e.target === ragSettingsModal) closeSettingsModal();
  });
  ragSettingsSave?.addEventListener('click', savePipelineSettings);

  ragFiles.addEventListener('change', updateFileList);
  ragEmbeddingProvider?.addEventListener('change', () => {
    updateEmbeddingUI();
    markReferenceStale();
  });
  ragOpenAiModel?.addEventListener('change', () => {
    updateEmbeddingUI();
    markReferenceStale();
  });
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

  // Vector Store provider
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

  updateFileList();
  updateEmbeddingUI();
  updateVectorStoreUI();
  loadVectorStoreConfig();
  loadPipelineSettings();
  refreshRagStatus();
  refreshReferenceHistory();
}

function buildHistoryConfigLine(config) {
  if (!config) return '';
  const topK = config.topK ?? '-';
  const minScore = config.minScore ?? '-';
  const chunkSize = config.chunkSize ?? '-';
  const overlap = config.chunkOverlap ?? '-';
  const chunker = config.chunker ? config.chunker.toString().toUpperCase() : 'N/A';
  const embed = config.embeddingProvider ? config.embeddingProvider.toString().toUpperCase() : 'N/A';
  const model = config.embeddingModel || 'N/A';
  const template = config.promptTemplate ? 'Template' : 'Default prompt';

  return `TopK ${topK} · MinScore ${minScore} · Chunk ${chunkSize}/${overlap} · ${chunker} · ${embed}:${model} · ${template}`;
}

async function loadPipelineSettings() {
  if (!ragChunkSize && !ragSettingsModal) return;
  try {
    const res = await fetch('/api/rag/pipeline-settings');
    const payload = await res.json().catch(() => null);
    if (!res.ok) throw new Error(payload?.error || 'Failed to load settings.');

    applyPipelineSettings(payload || {});
    refreshRagStatus(payload);
  } catch (err) {
    if (ragSettingsStatus) {
      ragSettingsStatus.textContent = err.message || 'Failed to load settings.';
    }
    showRagStatusError(err);
  }
}

function applyPipelineSettings(settings) {
  if (ragChunkSize && settings.chunkSize) ragChunkSize.value = settings.chunkSize;
  if (ragChunkOverlap && settings.chunkOverlap) ragChunkOverlap.value = settings.chunkOverlap;
  if (ragChunker && settings.chunker && !autoChunkerFromFiles) setSelectValue(ragChunker, settings.chunker);
  if (ragEmbeddingProvider && settings.embeddingProvider) setSelectValue(ragEmbeddingProvider, settings.embeddingProvider);
  if (ragOpenAiModel && settings.embeddingModel) setSelectValue(ragOpenAiModel, settings.embeddingModel);
  if (ragEmbeddingBaseUrl && settings.embeddingBaseUrl) ragEmbeddingBaseUrl.value = settings.embeddingBaseUrl;
  if (ragTopK && settings.topK) ragTopK.value = settings.topK;
  if (ragMinScore) ragMinScore.value = settings.minScore ?? '';
  if (ragPromptTemplate) ragPromptTemplate.value = settings.promptTemplate ?? '';

  updateEmbeddingUI();
}

async function savePipelineSettings() {
  if (!ragSettingsSave) return;
  ragSettingsSave.disabled = true;
  if (ragSettingsStatus) ragSettingsStatus.textContent = 'Saving...';

  const payload = {
    chunkSize: toInt(ragChunkSize?.value, 300),
    chunkOverlap: toInt(ragChunkOverlap?.value, 30),
    chunker: ragChunker?.value || 'character',
    embeddingProvider: ragEmbeddingProvider?.value || 'local',
    embeddingModel: getEmbeddingDefaults(ragEmbeddingProvider?.value).model,
    embeddingDimensions: getEmbeddingDefaults(ragEmbeddingProvider?.value).dims,
    embeddingBaseUrl: ragEmbeddingBaseUrl?.value?.trim() || '',
    topK: toInt(ragTopK?.value, 3),
    minScore: toFloatOrNull(ragMinScore?.value),
    promptTemplate: ragPromptTemplate?.value?.trim() || null
  };

  try {
    const res = await fetch('/api/rag/pipeline-settings', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(payload)
    });
    const data = await res.json().catch(() => null);
    if (!res.ok) throw new Error(data?.error || 'Failed to save settings.');

    if (ragSettingsStatus) ragSettingsStatus.textContent = 'Settings saved.';
    refreshRagStatus(data);
  } catch (err) {
    if (ragSettingsStatus) ragSettingsStatus.textContent = err.message || 'Failed to save settings.';
    showRagStatusError(err);
  } finally {
    ragSettingsSave.disabled = false;
  }
}

function setSelectValue(select, value) {
  if (!select) return;
  select.value = value;
  select.dispatchEvent(new Event('change', { bubbles: true }));
}

function toInt(value, fallback) {
  const parsed = parseInt(value, 10);
  return Number.isFinite(parsed) && parsed > 0 ? parsed : fallback;
}

function toFloatOrNull(value) {
  if (!value) return null;
  const parsed = parseFloat(value);
  return Number.isFinite(parsed) ? parsed : null;
}

function openModal() {
  ragModal.classList.remove('hidden');
  updateEmbeddingUI();
  setViewCodeEnabled(hasReferenceRun);
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

function updateFileList() {
  markReferenceStale();
  const files = Array.from(ragFiles.files || []);
  updateRunState(files);

  if (files.length > 0 && ragChunker) {
    const hasNonTxt = files.some((f) => !f.name.toLowerCase().endsWith('.txt'));
    setSelectValue(ragChunker, hasNonTxt ? 'markdown' : 'recursive');
    autoChunkerFromFiles = true;
  } else {
    autoChunkerFromFiles = false;
  }

  if (!ragFileList) return;
  if (files.length === 0) {
    ragFileList.innerHTML = '<div class="rag-empty">No files selected.</div>';
    return;
  }

  ragFileList.innerHTML = files
    .map((file) => {
      const size = `${Math.max(1, Math.round(file.size / 1024))} KB`;
      return `<div class="rag-file-row"><span>${escapeHtml(file.name)}</span><span class="rag-file-size">${size}</span></div>`;
    })
    .join('');
}

function getSelectedEmbeddingProvider() {
  return ragEmbeddingProvider?.value || 'openai';
}

function getEmbeddingDefaults(provider) {
  const p = provider || getSelectedEmbeddingProvider();
  if (p === 'openai') {
    const model = ragOpenAiModel?.value || 'text-embedding-3-small';
    const dimsMap = {
      'text-embedding-3-small': 1536,
      'text-embedding-3-large': 3072,
      'text-embedding-ada-002': 1536
    };
    return { model, dims: dimsMap[model] || 1536 };
  }
  const map = {
    ollama:  { model: 'qwen3-embedding:4b',     dims: 1024 }
  };
  return map[p] || { model: 'text-embedding-3-small', dims: 1536 };
}

function updateEmbeddingUI() {
  const provider = getSelectedEmbeddingProvider();
  const hasOpenAiKey = !!providerKeys?.OpenAI;

  if (ragOpenAiKey) {
    ragOpenAiKey.classList.toggle('hidden', provider !== 'openai' || hasOpenAiKey);
  }

  if (ragOpenAiModelRow) {
    ragOpenAiModelRow.classList.toggle('hidden', provider !== 'openai');
  }

  if (ragEmbeddingBaseRow) {
    ragEmbeddingBaseRow.classList.toggle('hidden', provider !== 'ollama');
  }

  if (ragOpenAiKeyInput && provider !== 'openai') {
    ragOpenAiKeyInput.value = '';
  }

  if (ragOpenAiKeyStatus) {
    ragOpenAiKeyStatus.textContent = hasOpenAiKey
      ? 'OpenAI key already saved in localStorage.'
      : 'Stored in localStorage for this browser.';
  }

  if (ragEmbeddingHint) {
    if (provider === 'ollama') {
      ragEmbeddingHint.textContent = 'Ollama must be installed and running on the base URL below.';
    } else if (provider === 'openai') {
      const modelName = ragOpenAiModel?.value || 'text-embedding-3-small';
      ragEmbeddingHint.textContent = hasOpenAiKey
        ? `Using stored OpenAI API key (${modelName}).`
        : 'OpenAI API key required. Enter it below.';
    }
  }

  updateRunState();
}

function updateRunState(files) {
  const fileCount = files ? files.length : (ragFiles.files ? ragFiles.files.length : 0);
  const provider = getSelectedEmbeddingProvider();
  const needsKey = provider !== 'ollama';
  const hasKey = !needsKey || !!providerKeys?.OpenAI;
  const vsProvider = ragVectorStoreProvider?.value || 'inmemory';
  const vsReady = vsProvider !== 'postgres' || pgConnected;
  ragRun.disabled = fileCount === 0 || !hasKey || !vsReady;
}

async function runReference() {
  const files = Array.from(ragFiles.files || []);
  if (files.length === 0) return;

  setViewCodeEnabled(false);

  const provider = getSelectedEmbeddingProvider();
  const openAiKey = providerKeys?.OpenAI;
  if (provider === 'openai' && !openAiKey) {
    ragStatus.textContent = 'OpenAI API key is required.';
    ragTrace.innerHTML = renderError('OpenAI API key is required.');
    updateRunState(files);
    return;
  }

  ragRun.disabled = true;
  ragStatus.textContent = 'Indexing documents...';
  ragTrace.innerHTML = renderLoading();

  const formData = new FormData();
  files.forEach((file) => formData.append('files', file));
  if (ragChunkSize) formData.append('chunkSize', ragChunkSize.value || '300');
  if (ragChunkOverlap) formData.append('chunkOverlap', ragChunkOverlap.value || '30');
  if (ragChunker) formData.append('chunker', ragChunker.value || 'character');
  formData.append('embeddingProvider', provider);
  const embDefaults = getEmbeddingDefaults(provider);
  formData.append('embeddingModel', embDefaults.model);
  formData.append('embeddingDimensions', String(embDefaults.dims));
  if (ragEmbeddingBaseUrl) formData.append('embeddingBaseUrl', ragEmbeddingBaseUrl.value || '');
  if (ragTopK) formData.append('topK', ragTopK.value || '');
  if (ragMinScore) formData.append('minScore', ragMinScore.value || '');
  if (ragPromptTemplate) formData.append('promptTemplate', ragPromptTemplate.value || '');
  if (provider === 'openai' && openAiKey) {
    formData.append('openaiApiKey', openAiKey);
  }

  try {
    const res = await fetch('/api/rag/reference', { method: 'POST', body: formData });
    const payload = await res.json().catch(() => null);

    if (!res.ok) {
      const message = payload?.error || 'Failed to build RAG reference.';
      ragStatus.textContent = message;
      ragTrace.innerHTML = renderError(message);
      ragRun.disabled = false;
      return;
    }

    ragStatus.textContent = `Ready · ${payload.summary.documentCount} docs · ${payload.summary.chunkCount} chunks`;
    renderTrace(payload);
    setViewCodeEnabled(true);
    setDiagnoseEnabled(true);
    refreshRagStatus();
    refreshReferenceHistory();
  } catch (err) {
    ragStatus.textContent = 'Network error.';
    ragTrace.innerHTML = renderError(err.message || 'Network error');
    showRagStatusError(err);
  } finally {
    updateRunState(files);
  }
}

async function refreshRagStatus(settingsOverride) {
  if (!ragChatStatus) return;
  if (settingsOverride) {
    applyRagStatus(settingsOverride, true);
    return;
  }

  try {
    const res = await fetch('/api/rag/status');
    const payload = await res.json().catch(() => null);
    if (!res.ok) throw new Error(payload?.error || 'Failed to load RAG status.');

    applyRagStatus(payload?.settings || {}, payload?.hasIndex);
    setDiagnoseEnabled(!!payload?.hasIndex);
  } catch (err) {
    showRagStatusError(err);
  }
}

function applyRagStatus(settings, hasIndex) {
  if (!ragChatStatus) return;
  const provider = (settings.embeddingProvider || 'local').toUpperCase();
  const topK = settings.topK ?? 0;
  const minScore = settings.minScore ?? '-';
  const chunker = settings.chunker ? settings.chunker.toUpperCase() : 'N/A';
  const statusLabel = hasIndex ? 'RAG: READY' : 'RAG: NOT INDEXED';

  ragChatStatus.textContent = `${statusLabel} · TopK=${topK} · MinScore=${minScore} · ${provider} · ${chunker}`;
  ragChatStatus.classList.toggle('active', !!hasIndex);
  ragChatStatus.classList.remove('error');
}

function showRagStatusError(err) {
  if (!ragChatStatus) return;
  ragChatStatus.textContent = `RAG: ERROR · ${err.message || 'Status unavailable'}`;
  ragChatStatus.classList.add('error');
  ragChatStatus.classList.remove('active');
}

async function refreshReferenceHistory() {
  if (!ragHistoryList) return;
  try {
    const res = await fetch('/api/rag/reference-history');
    const payload = await res.json().catch(() => null);
    if (!res.ok) {
      throw new Error(payload?.error || 'Failed to load history.');
    }

    const history = payload?.history || [];
    if (!history.length) {
      ragHistoryList.innerHTML = '<div class="rag-empty">No references yet.</div>';
      return;
    }

    ragHistoryList.innerHTML = history
    .map((entry) => {
      const sources = Array.isArray(entry.sources) && entry.sources.length
        ? entry.sources.join(', ')
        : 'Untitled';
      const createdAt = entry.createdAt ? new Date(entry.createdAt).toLocaleString() : 'Unknown time';
      const summary = entry.summary
        ? `${entry.summary.documentCount} docs · ${entry.summary.chunkCount} chunks`
        : '';
      const config = entry.config || {};
      const configLine = buildHistoryConfigLine(config);
      return `
        <div class="rag-history-item">
          <div class="rag-history-title-row">
            <span class="rag-history-sources">${escapeHtml(sources)}</span>
            <span class="rag-history-time">${escapeHtml(createdAt)}</span>
          </div>
          <div class="rag-history-meta">${escapeHtml(summary)}</div>
          ${configLine ? `<div class="rag-history-config">${escapeHtml(configLine)}</div>` : ''}
        </div>
      `;
    })
    .join('');
  } catch (err) {
    ragHistoryList.innerHTML = `<div class="rag-empty">${escapeHtml(err.message || 'Failed to load history.')}</div>`;
  }
}

function openRagCodeModal() {
  if (!codeModal || !codeModalContent) return;
  codeModal.classList.remove('hidden');
  codeModalContent.textContent = 'Loading...';
  if (codeCopyAll) codeCopyAll.textContent = 'Copy';

  fetch('/api/rag/code-snippet')
    .then(async (res) => {
      const data = await res.json().catch(() => null);
      if (!res.ok) {
        throw new Error(data?.error || 'Failed to load code snippet.');
      }
      return data;
    })
    .then((data) => {
      codeModalContent.textContent = data?.code || 'No code available';
      if (window.hljs) {
        delete codeModalContent.dataset.highlighted;
        window.hljs.highlightElement(codeModalContent);
      }
    })
    .catch((err) => {
      codeModalContent.textContent = `// ${err.message || 'Failed to load code snippet.'}`;
    });
}

let hasReferenceRun = false;
let autoChunkerFromFiles = false;
let pgConnected = false;

// ── Vector Store Provider ─────────────────────────────────────
function updateVectorStoreUI() {
  const provider = ragVectorStoreProvider?.value || 'inmemory';
  if (ragPgConfig) {
    ragPgConfig.classList.toggle('hidden', provider !== 'postgres');
  }
  if (ragVectorStoreHint) {
    ragVectorStoreHint.textContent = provider === 'postgres'
      ? 'PostgreSQL with pgvector. Configure and connect below.'
      : 'In-memory store. Data is lost on restart.';
  }
  updatePgConnectState();
  updateRunState();
}

function updatePgConnectState() {
  if (!ragPgConnect) return;
  const hasHost = !!ragPgHost?.value?.trim();
  const hasDb = !!ragPgDatabase?.value?.trim();
  ragPgConnect.disabled = !(hasHost && hasDb);
}

function buildConnectionString() {
  const host = ragPgHost?.value?.trim() || 'localhost';
  const port = ragPgPort?.value?.trim() || '5432';
  const db = ragPgDatabase?.value?.trim() || '';
  const user = ragPgUser?.value?.trim() || '';
  const pass = ragPgPassword?.value || '';
  let parts = [`Host=${host}`, `Port=${port}`];
  if (db) parts.push(`Database=${db}`);
  if (user) parts.push(`Username=${user}`);
  if (pass) parts.push(`Password=${pass}`);
  return parts.join(';');
}

function parseConnectionString(connStr) {
  const result = { host: 'localhost', port: '5432', database: '', username: '', password: '' };
  if (!connStr) return result;
  for (const part of connStr.split(';')) {
    const idx = part.indexOf('=');
    if (idx < 0) continue;
    const k = part.substring(0, idx).trim().toLowerCase();
    const v = part.substring(idx + 1).trim();
    if (k === 'host' || k === 'server') result.host = v;
    else if (k === 'port') result.port = v;
    else if (k === 'database' || k === 'db') result.database = v;
    else if (k === 'username' || k === 'user id' || k === 'userid') result.username = v;
    else if (k === 'password') result.password = v;
  }
  return result;
}

function applyPgFields(cfg) {
  if (!cfg) return;
  // Support both individual fields (new) and connectionString (old/server)
  let fields = cfg;
  if (!cfg.host && cfg.connectionString) {
    fields = parseConnectionString(cfg.connectionString);
  }
  if (ragPgHost) ragPgHost.value = fields.host || 'localhost';
  if (ragPgPort) ragPgPort.value = fields.port || '5432';
  if (ragPgDatabase) ragPgDatabase.value = fields.database || '';
  if (ragPgUser) ragPgUser.value = fields.username || '';
  if (ragPgPassword) ragPgPassword.value = fields.password || '';
  if (ragPgTable) ragPgTable.value = cfg.tableName || 'vectors';
  if (ragPgSchema) ragPgSchema.value = cfg.schemaName || 'public';
  if (ragPgDimension) ragPgDimension.value = cfg.dimension || 1536;
  if (ragPgEnsureSchema) ragPgEnsureSchema.checked = cfg.ensureSchema ?? true;
}

const PG_STORAGE_KEY = 'rag_pg_config';

function savePgToStorage(config) {
  try { localStorage.setItem(PG_STORAGE_KEY, JSON.stringify(config)); } catch { /* ignore */ }
}

function loadPgFromStorage() {
  try {
    const raw = localStorage.getItem(PG_STORAGE_KEY);
    return raw ? JSON.parse(raw) : null;
  } catch { return null; }
}

function clearPgFromStorage() {
  try { localStorage.removeItem(PG_STORAGE_KEY); } catch { /* ignore */ }
}

function updatePgDisconnectVisibility() {
  if (ragPgDisconnect) {
    ragPgDisconnect.classList.toggle('hidden', !pgConnected);
  }
}

async function connectPostgres() {
  if (!ragPgConnect) return;
  ragPgConnect.disabled = true;
  if (ragPgStatus) ragPgStatus.textContent = 'Connecting...';

  const payload = {
    provider: 'postgres',
    connectionString: buildConnectionString(),
    tableName: ragPgTable?.value?.trim() || 'vectors',
    schemaName: ragPgSchema?.value?.trim() || 'public',
    dimension: parseInt(ragPgDimension?.value, 10) || 1536,
    ensureSchema: ragPgEnsureSchema?.checked ?? true,
    openAiApiKey: providerKeys?.OpenAI || null
  };
  const storagePayload = {
    ...payload,
    host: ragPgHost?.value?.trim() || 'localhost',
    port: ragPgPort?.value?.trim() || '5432',
    database: ragPgDatabase?.value?.trim() || '',
    username: ragPgUser?.value?.trim() || '',
    password: ragPgPassword?.value || ''
  };

  try {
    const res = await fetch('/api/rag/vector-store', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(payload)
    });
    const data = await res.json().catch(() => null);
    if (!res.ok) throw new Error(data?.error || 'Connection failed.');

    pgConnected = true;
    savePgToStorage(storagePayload);
    const statusMsg = data.warning
      ? `Connected · ${data.schemaName}.${data.tableName} (dim=${data.dimension}) ⚠️ ${data.warning}`
      : `Connected · ${data.schemaName}.${data.tableName} (dim=${data.dimension})`;
    if (ragPgStatus) ragPgStatus.textContent = statusMsg;
    if (ragPgConnect) ragPgConnect.textContent = 'Reconnect';
    updatePgDisconnectVisibility();
    updateRunState();
    refreshRagStatus();
  } catch (err) {
    pgConnected = false;
    updatePgDisconnectVisibility();
    if (ragPgStatus) ragPgStatus.textContent = err.message || 'Connection failed.';
  } finally {
    updatePgConnectState();
  }
}

async function disconnectPostgres() {
  if (ragPgDisconnect) ragPgDisconnect.disabled = true;
  if (ragPgStatus) ragPgStatus.textContent = 'Disconnecting...';

  try {
    const res = await fetch('/api/rag/vector-store', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ provider: 'inmemory' })
    });
    await res.json().catch(() => null);

    pgConnected = false;
    clearPgFromStorage();
    if (ragVectorStoreProvider) setSelectValue(ragVectorStoreProvider, 'inmemory');
    if (ragPgConnect) ragPgConnect.textContent = 'Connect';
    if (ragPgStatus) ragPgStatus.textContent = '';
    updateVectorStoreUI();
    updatePgDisconnectVisibility();
    refreshRagStatus();
    markReferenceStale();
  } catch (err) {
    if (ragPgStatus) ragPgStatus.textContent = err.message || 'Failed to disconnect.';
  } finally {
    if (ragPgDisconnect) ragPgDisconnect.disabled = false;
  }
}

async function loadVectorStoreConfig() {
  // First check localStorage for saved Postgres config
  const saved = loadPgFromStorage();
  if (saved && saved.provider === 'postgres' && (saved.host || saved.connectionString)) {
    if (ragVectorStoreProvider) setSelectValue(ragVectorStoreProvider, 'postgres');
    applyPgFields(saved);
    updateVectorStoreUI();

    // Auto-reconnect to the server
    await connectPostgres();
    return;
  }

  // Fallback: check server state
  try {
    const res = await fetch('/api/rag/vector-store');
    const data = await res.json().catch(() => null);
    if (!res.ok) return;

    if (data?.provider === 'postgres') {
      if (ragVectorStoreProvider) setSelectValue(ragVectorStoreProvider, 'postgres');
      applyPgFields(data);
      pgConnected = true;
      if (ragPgConnect) ragPgConnect.textContent = 'Reconnect';
      if (ragPgStatus) ragPgStatus.textContent = `Connected · ${data.schemaName}.${data.tableName}`;
      updatePgDisconnectVisibility();
    }
    updateVectorStoreUI();
  } catch { /* ignore */ }
}

function setViewCodeEnabled(enabled) {
  hasReferenceRun = enabled;
  if (ragViewCode) {
    ragViewCode.disabled = !enabled;
  }
}

function markReferenceStale() {
  setViewCodeEnabled(false);
}

function saveInlineOpenAiKey() {
  if (!ragOpenAiKeyInput) return;
  const key = ragOpenAiKeyInput.value.trim();
  if (!key) return;

  providerKeys.OpenAI = key;
  saveKeysToStorage();
  refreshProviderGroup('OpenAI');

  if (ragOpenAiKeyStatus) {
    ragOpenAiKeyStatus.textContent = 'Key saved for OpenAI (localStorage).';
  }
  if (ragOpenAiKeySave) {
    ragOpenAiKeySave.disabled = true;
  }

  updateEmbeddingUI();
}

function renderTrace(trace) {
  const docIds = new Set(trace.documents.map((doc) => doc.id));
  const chunksByDoc = new Map();
  trace.chunks.forEach((chunk) => {
    if (!chunksByDoc.has(chunk.documentId)) {
      chunksByDoc.set(chunk.documentId, []);
    }
    chunksByDoc.get(chunk.documentId).push(chunk);
  });

  for (const list of chunksByDoc.values()) {
    list.sort((a, b) => a.index - b.index);
  }

  const embeddingsByChunk = new Map();
  trace.embeddings.forEach((emb) => {
    if (!embeddingsByChunk.has(emb.chunkId)) {
      embeddingsByChunk.set(emb.chunkId, []);
    }
    embeddingsByChunk.get(emb.chunkId).push(emb);
  });

  const recordsByChunk = new Map();
  trace.records.forEach((rec) => {
    if (!recordsByChunk.has(rec.id)) {
      recordsByChunk.set(rec.id, []);
    }
    recordsByChunk.get(rec.id).push(rec);
  });

  const docNodes = trace.documents
    .map((doc) => renderDocumentNode(doc, chunksByDoc.get(doc.id) || []))
    .join('');

  const orphanChunks = trace.chunks.filter((chunk) => !docIds.has(chunk.documentId));
  const orphanNodes = orphanChunks.length
    ? renderOrphanChunks(orphanChunks)
    : '';

  const chunkNodes = trace.chunks
    .map((chunk) => renderChunkNode(chunk))
    .join('');

  const embeddingNodes = trace.chunks
    .map((chunk) => renderEmbeddingGroupNode(chunk, embeddingsByChunk.get(chunk.id) || []))
    .join('');

  const recordNodes = trace.chunks
    .map((chunk) => renderRecordGroupNode(chunk, recordsByChunk.get(chunk.id) || []))
    .join('');

  const tabs = [
    { key: 'docs', label: 'Documents', count: trace.summary.documentCount, body: docNodes || '<div class="rag-empty">No documents.</div>' },
    { key: 'chunks', label: 'Chunks', count: trace.summary.chunkCount, body: chunkNodes || '<div class="rag-empty">No chunks.</div>' },
    { key: 'embeddings', label: 'Embeddings', count: trace.summary.embeddingCount, body: embeddingNodes || '<div class="rag-empty">No embeddings.</div>' },
    { key: 'vectors', label: 'Vector Table', count: trace.summary.recordCount, body: recordNodes || '<div class="rag-empty">No vector records.</div>' }
  ];

  const tabButtons = tabs
    .map((t, i) => `<button class="rag-trace-tab${i === 0 ? ' active' : ''}" data-panel="rag-tp-${t.key}" type="button">${t.label} <span class="rag-trace-tab-count">${t.count}</span></button>`)
    .join('');

  const tabPanels = tabs
    .map((t, i) => `<div class="rag-trace-panel${i === 0 ? ' active' : ''}" id="rag-tp-${t.key}"><div class="rag-tree">${t.body}</div></div>`)
    .join('');

  ragTrace.innerHTML = `
    <div class="rag-summary">
      <div class="rag-summary-stats">
        <div><strong>${trace.summary.documentCount}</strong> docs</div>
        <div><strong>${trace.summary.chunkCount}</strong> chunks</div>
        <div><strong>${trace.summary.embeddingCount}</strong> embeddings</div>
        <div><strong>${trace.summary.recordCount}</strong> records</div>
        <div><strong>${trace.summary.dimensions}</strong> dims</div>
      </div>
      <div class="rag-summary-actions" id="rag-summary-actions"></div>
    </div>
    <div class="rag-trace-tabs">${tabButtons}</div>
    <div class="rag-trace-panels">
      ${tabPanels}
      ${orphanNodes ? `<div class="rag-trace-orphans">${orphanNodes}</div>` : ''}
    </div>
  `;

  ragTrace.querySelectorAll('.rag-trace-tab').forEach((btn) => {
    btn.addEventListener('click', () => {
      ragTrace.querySelectorAll('.rag-trace-tab').forEach((b) => b.classList.remove('active'));
      ragTrace.querySelectorAll('.rag-trace-panel').forEach((p) => p.classList.remove('active'));
      btn.classList.add('active');
      const panel = ragTrace.querySelector(`#${btn.dataset.panel}`);
      if (panel) panel.classList.add('active');
    });
  });

  // Click-to-expand full content for truncated previews
  ragTrace.addEventListener('click', (e) => {
    const preview = e.target.closest('.rag-preview--clickable');
    if (preview && preview.dataset.fullContent) {
      openContentViewer(preview.dataset.fullTitle || '', preview.dataset.fullMeta || '', preview.dataset.fullContent);
    }
  });

  const summaryActions = ragTrace.querySelector('#rag-summary-actions');
  if (summaryActions && ragViewCode) {
    summaryActions.appendChild(ragViewCode);
  }
}

function renderTopSection(title, body) {
  return `
    <details class="rag-tree-section" open>
      <summary>
        <div>
          <div class="rag-node-title">${title}</div>
          <div class="rag-node-meta">Click to collapse</div>
        </div>
      </summary>
      <div class="rag-children">${body}</div>
    </details>`;
}

function renderDocumentNode(doc, chunks) {
  const chunkNodes = chunks.map((chunk) => renderChunkNode(chunk)).join('');
  return `
    <details class="rag-tree-node" open>
      <summary>
        <div>
          <div class="rag-node-title">Document · ${escapeHtml(doc.source || doc.id)}</div>
          <div class="rag-node-meta">${doc.contentLength} chars · ${escapeHtml(doc.id)}</div>
        </div>
      </summary>
      <div class="rag-node-body">
        <div class="rag-preview">${escapeHtml(truncate(doc.preview, 220))}</div>
        ${renderMetadata(doc.metadata)}
      </div>
      <div class="rag-children">
        ${chunkNodes || '<div class="rag-empty">No chunks.</div>'}
      </div>
    </details>`;
}

function renderChunkNode(chunk) {
  const needsExpand = chunk.content && chunk.content.length > 180;
  return `
    <details class="rag-tree-node rag-tree-node--chunk" open>
      <summary>
        <div>
          <div class="rag-node-title">Chunk #${chunk.index}</div>
          <div class="rag-node-meta">${chunk.contentLength} chars · ${escapeHtml(chunk.id)}</div>
        </div>
      </summary>
      <div class="rag-node-body">
        <div class="rag-preview${needsExpand ? ' rag-preview--clickable' : ''}"${needsExpand ? ` data-full-title="Chunk #${chunk.index}" data-full-meta="${chunk.contentLength} chars · ${escapeHtml(chunk.id)}" data-full-content="${escapeHtml(chunk.content).replace(/"/g, '&quot;')}"` : ''}>${escapeHtml(truncate(chunk.content, 180))}</div>
        ${renderMetadata(chunk.metadata)}
      </div>
    </details>`;
}

function renderEmbeddingGroupNode(chunk, embeddings) {
  const embeddingContent = embeddings.length
    ? embeddings.map((emb) => renderEmbeddingNode(emb)).join('')
    : '<div class="rag-leaf rag-leaf--empty">No embeddings.</div>';

  return `
    <details class="rag-tree-node rag-tree-node--chunk" open>
      <summary>
        <div>
          <div class="rag-node-title">Chunk #${chunk.index}</div>
          <div class="rag-node-meta">${chunk.contentLength} chars · ${escapeHtml(chunk.id)}</div>
        </div>
      </summary>
      <div class="rag-children">
        ${embeddingContent}
      </div>
    </details>`;
}

function renderEmbeddingNode(emb) {
  const sample = emb.sample.map((v) => v.toFixed(3)).join(', ');
  const vectorText = emb.vector.map((v) => v.toFixed(6)).join(', ');
  return `
    <details class="rag-leaf rag-leaf--expand" open>
      <summary>
        <div>
          <div class="rag-node-title">Embedding</div>
          <div class="rag-node-meta">${emb.dimensions} dims · ${escapeHtml(emb.chunkId)}</div>
        </div>
      </summary>
      <div class="rag-node-body">
        <div class="rag-preview">[${sample}]</div>
        <details class="rag-vector" open>
          <summary>Full vector</summary>
          <div class="rag-vector-body">${escapeHtml(vectorText)}</div>
        </details>
      </div>
    </details>`;
}

function renderRecordGroupNode(chunk, records) {
  const recordContent = records.length
    ? records.map((rec) => renderRecordNode(rec)).join('')
    : '<div class="rag-leaf rag-leaf--empty">No vector records.</div>';

  return `
    <details class="rag-tree-node rag-tree-node--chunk" open>
      <summary>
        <div>
          <div class="rag-node-title">Chunk #${chunk.index}</div>
          <div class="rag-node-meta">${chunk.contentLength} chars · ${escapeHtml(chunk.id)}</div>
        </div>
      </summary>
      <div class="rag-children">
        ${recordContent}
      </div>
    </details>`;
}

function renderRecordNode(rec) {
  const needsExpand = rec.content && rec.content.length > 140;
  return `
    <div class="rag-leaf">
      <div class="rag-node-title">Vector Record</div>
      <div class="rag-node-meta">${rec.namespace || '(default)'} · ${rec.contentLength} chars</div>
      <div class="rag-preview${needsExpand ? ' rag-preview--clickable' : ''}"${needsExpand ? ` data-full-title="Vector Record" data-full-meta="${rec.namespace || '(default)'} · ${rec.contentLength} chars" data-full-content="${escapeHtml(rec.content).replace(/"/g, '&quot;')}"` : ''}>${escapeHtml(truncate(rec.content, 140))}</div>
      ${renderMetadata(rec.metadata)}
    </div>`;
}

function renderOrphanChunks(orphanChunks) {
  const nodes = orphanChunks
    .map((chunk) => renderChunkNode(chunk))
    .join('');

  return `
    <details class="rag-tree-node rag-tree-node--orphan" open>
      <summary>
        <div>
          <div class="rag-node-title">Unlinked Chunks</div>
          <div class="rag-node-meta">${orphanChunks.length} chunks</div>
        </div>
      </summary>
      <div class="rag-children">
        ${nodes || '<div class="rag-empty">No chunks.</div>'}
      </div>
    </details>`;
}

function renderMetadata(metadata) {
  const entries = Object.entries(metadata || {});
  if (entries.length === 0) return '';
  const rows = entries
    .map(([key, value]) => `<div class="rag-meta-row"><span>${escapeHtml(key)}</span><span>${escapeHtml(String(value))}</span></div>`)
    .join('');
  return `<div class="rag-meta">${rows}</div>`;
}

function renderLoading() {
  return '<div class="rag-empty">Processing…</div>';
}

function renderError(message) {
  return `<div class="rag-empty rag-error">${escapeHtml(message)}</div>`;
}

// ── Full Content Viewer ──────────────────────────────────────
function openContentViewer(title, meta, content) {
  const existing = document.getElementById('rag-content-viewer');
  if (existing) existing.remove();

  const overlay = document.createElement('div');
  overlay.id = 'rag-content-viewer';
  overlay.className = 'modal-overlay';

  overlay.innerHTML = `
    <div class="modal-card content-viewer-card">
      <div class="modal-header">
        <div>
          <h3>${escapeHtml(title)}</h3>
          ${meta ? `<div class="content-viewer-meta">${escapeHtml(meta)}</div>` : ''}
        </div>
        <div style="display:flex;gap:6px;align-items:center">
          <button class="btn btn-ghost btn-sm" id="content-viewer-copy">Copy</button>
          <button class="btn-icon" id="content-viewer-close" title="Close">
            <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><line x1="18" y1="6" x2="6" y2="18"/><line x1="6" y1="6" x2="18" y2="18"/></svg>
          </button>
        </div>
      </div>
      <div class="modal-body content-viewer-body">
        <pre class="content-viewer-pre"><code class="content-viewer-code"></code></pre>
      </div>
    </div>
  `;

  document.body.appendChild(overlay);

  const codeEl = overlay.querySelector('.content-viewer-code');
  codeEl.textContent = content;

  overlay.querySelector('#content-viewer-close').addEventListener('click', () => overlay.remove());
  overlay.addEventListener('click', (e) => { if (e.target === overlay) overlay.remove(); });

  overlay.querySelector('#content-viewer-copy').addEventListener('click', (e) => {
    navigator.clipboard.writeText(content).then(() => {
      e.target.textContent = 'Copied!';
      setTimeout(() => { e.target.textContent = 'Copy'; }, 1500);
    });
  });
}
