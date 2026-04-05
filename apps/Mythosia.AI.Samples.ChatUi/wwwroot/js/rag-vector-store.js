// ═══════════════════════════════════════════════════════════════
// RAG Vector Store Management
// ═══════════════════════════════════════════════════════════════

import {
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
  ragPgWarnings,
  ragQdrantConfig,
  ragQdrantHost,
  ragQdrantPort,
  ragQdrantApiKey,
  ragQdrantDimension,
  ragQdrantCollection,
  ragQdrantUseTls,
  ragQdrantConnect,
  ragQdrantDisconnect,
  ragQdrantStatus,
  ragQdrantWarnings,
  ragPineconeConfig,
  ragPineconeIndexHost,
  ragPineconeApiKey,
  ragPineconeNamespace,
  ragPineconeConnect,
  ragPineconeDisconnect,
  ragPineconeStatus,
  ragPineconeWarnings,
  ragEmbeddingBaseUrl,
  ragVllmBaseUrl
} from './dom.js';
import { providerKeys } from './state.js';
import { ragState, setSelectValue, markReferenceStale, setStatusState, updateRunState } from './rag-shared.js';
import { refreshRagStatus, updateVectorDbStatus } from './rag-run.js';
import { getSelectedEmbeddingProvider, getEmbeddingDefaults, getSelectedEmbeddingDimensions } from './rag-embedding.js';

// ── Embedding Snapshot Helper ────────────────────────────────
function getEmbeddingSnapshot() {
  try {
    const provider = getSelectedEmbeddingProvider();
    const { model } = getEmbeddingDefaults(provider);
    const dimensions = getSelectedEmbeddingDimensions();
    const baseUrl = provider === 'vllm'
      ? ragVllmBaseUrl?.value?.trim() || ''
      : ragEmbeddingBaseUrl?.value?.trim() || '';
    return { embeddingProvider: provider, embeddingModel: model, embeddingDimensions: dimensions, embeddingBaseUrl: baseUrl };
  } catch {
    return {};
  }
}

// ── Schema Warning Helpers ───────────────────────────────────
function renderSchemaWarnings(container, warnings) {
  if (!container) return;
  container.innerHTML = '';
  if (!warnings || warnings.length === 0) {
    container.classList.add('hidden');
    return;
  }
  for (const msg of warnings) {
    const item = document.createElement('div');
    item.className = 'rag-schema-warning-item';
    item.textContent = msg;
    container.appendChild(item);
  }
  container.classList.remove('hidden');
}

function clearSchemaWarnings(container) {
  if (!container) return;
  container.innerHTML = '';
  container.classList.add('hidden');
}

// ── Active Provider Persistence ──────────────────────────────
const ACTIVE_PROVIDER_KEY = 'rag_active_provider';

function saveLastActiveProvider(provider) {
  try { localStorage.setItem(ACTIVE_PROVIDER_KEY, provider); } catch { /* ignore */ }
}

function loadLastActiveProvider() {
  try { return localStorage.getItem(ACTIVE_PROVIDER_KEY); } catch { return null; }
}

function clearLastActiveProvider() {
  try { localStorage.removeItem(ACTIVE_PROVIDER_KEY); } catch { /* ignore */ }
}

// ── Switch to InMemory ───────────────────────────────────────
export async function switchToInMemory() {
  try {
    await fetch('/api/rag/vector-store', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ provider: 'inmemory' })
    });
  } catch { /* best-effort */ }
  ragState.pgConnected = false;
  ragState.qdrantConnected = false;
  ragState.pineconeConnected = false;
  clearLastActiveProvider();
  updatePgDisconnectVisibility();
  updateQdrantDisconnectVisibility();
  updatePineconeDisconnectVisibility();
  if (ragPgConnect) ragPgConnect.textContent = 'Connect';
  if (ragPgStatus) {
    ragPgStatus.textContent = '';
    setStatusState(ragPgStatus, null);
  }
  clearSchemaWarnings(ragPgWarnings);
  if (ragQdrantConnect) ragQdrantConnect.textContent = 'Connect';
  if (ragQdrantStatus) {
    ragQdrantStatus.textContent = '';
    setStatusState(ragQdrantStatus, null);
  }
  clearSchemaWarnings(ragQdrantWarnings);
  if (ragPineconeConnect) ragPineconeConnect.textContent = 'Connect';
  if (ragPineconeStatus) {
    ragPineconeStatus.textContent = '';
    setStatusState(ragPineconeStatus, null);
  }
  clearSchemaWarnings(ragPineconeWarnings);
  updateVectorDbStatus();
  updateRunState();
  refreshRagStatus();
  markReferenceStale();
}

// ── Provider UI ──────────────────────────────────────────────
export function updateVectorStoreUI() {
  const provider = ragVectorStoreProvider?.value?.trim();
  updateVectorDbStatus();

  if (provider === 'inmemory' && (ragState.pgConnected || ragState.qdrantConnected || ragState.pineconeConnected)) {
    switchToInMemory();
  }

  if (ragPgConfig) {
    ragPgConfig.classList.toggle('hidden', provider !== 'postgres');
  }
  if (ragQdrantConfig) {
    ragQdrantConfig.classList.toggle('hidden', provider !== 'qdrant');
  }
  if (ragPineconeConfig) {
    ragPineconeConfig.classList.toggle('hidden', provider !== 'pinecone');
  }
  if (ragVectorStoreHint) {
    if (provider === 'postgres') {
      ragVectorStoreHint.textContent = 'PostgreSQL with pgvector. Configure and connect below.';
    } else if (provider === 'qdrant') {
      ragVectorStoreHint.textContent = 'Qdrant vector database. Configure and connect below.';
    } else if (provider === 'pinecone') {
      ragVectorStoreHint.textContent = 'Pinecone managed vector database. Configure index host/API key and connect.';
    } else {
      ragVectorStoreHint.textContent = 'In-memory store. Data is lost on restart.';
    }
  }
  updatePgConnectState();
  updateQdrantConnectState();
  updatePineconeConnectState();
  updateRunState();
}

// ── Load Config (auto-reconnect on boot) ─────────────────────
export async function loadVectorStoreConfig() {
  const savedPg = loadPgFromStorage();
  if (savedPg) applyPgFields(savedPg);
  const savedQd = loadQdrantFromStorage();
  if (savedQd) applyQdrantFields(savedQd);
  const savedPine = loadPineconeFromStorage();
  if (savedPine) applyPineconeFields(savedPine);

  const lastActive = loadLastActiveProvider();

  if (lastActive === 'postgres' && savedPg && (savedPg.host || savedPg.connectionString)) {
    if (ragVectorStoreProvider) setSelectValue(ragVectorStoreProvider, 'postgres');
    updateVectorStoreUI();
    await connectPostgres();
    return;
  }

  if (lastActive === 'qdrant' && savedQd && savedQd.host) {
    if (ragVectorStoreProvider) setSelectValue(ragVectorStoreProvider, 'qdrant');
    updateVectorStoreUI();
    await connectQdrant();
    return;
  }

  if (lastActive === 'pinecone' && savedPine && savedPine.indexHost) {
    if (ragVectorStoreProvider) setSelectValue(ragVectorStoreProvider, 'pinecone');
    updateVectorStoreUI();
    await connectPinecone();
    return;
  }
}

export function getVectorStoreConfigForRequest() {
  const provider = ragVectorStoreProvider?.value?.trim()?.toLowerCase();
  if (!provider) {
    throw new Error('Vector store provider is required.');
  }
  if (provider === 'postgres') {
    const dimension = parseInt(ragPgDimension?.value, 10);
    const tableName = ragPgTable?.value?.trim();
    const schemaName = ragPgSchema?.value?.trim();
    if (!Number.isFinite(dimension) || dimension <= 0) {
      throw new Error('PostgreSQL vector dimension is required.');
    }
    if (!tableName) {
      throw new Error('PostgreSQL table name is required.');
    }
    if (!schemaName) {
      throw new Error('PostgreSQL schema name is required.');
    }
    return {
      provider,
      connectionString: buildConnectionString(),
      tableName,
      schemaName,
      dimension,
      ensureSchema: !!ragPgEnsureSchema?.checked,
      openAiApiKey: providerKeys?.OpenAI || null
    };
  }
  if (provider === 'qdrant') {
    const rawHost = ragQdrantHost?.value?.trim();
    const port = parseInt(ragQdrantPort?.value, 10);
    if (!rawHost) {
      throw new Error('Qdrant host is required.');
    }
    const dimension = parseInt(ragQdrantDimension?.value, 10);
    const collectionName = ragQdrantCollection?.value?.trim();
    if (!Number.isFinite(dimension) || dimension <= 0) {
      throw new Error('Qdrant vector dimension is required.');
    }
    if (!Number.isFinite(port) || port <= 0) {
      throw new Error('Qdrant port is required.');
    }
    if (!collectionName) {
      throw new Error('Qdrant collection name is required.');
    }
    const host = rawHost
      .replace(/^https?:\/\//i, '')
      .replace(/\/.*$/, '');
    return {
      provider,
      qdrantHost: host,
      qdrantPort: port,
      qdrantApiKey: ragQdrantApiKey?.value?.trim() || null,
      qdrantUseTls: !!ragQdrantUseTls?.checked,
      dimension,
      qdrantCollectionName: collectionName,
      openAiApiKey: providerKeys?.OpenAI || null
    };
  }
  if (provider === 'pinecone') {
    const indexHost = ragPineconeIndexHost?.value?.trim();
    const apiKey = ragPineconeApiKey?.value?.trim();
    const pineconeNamespace = ragPineconeNamespace?.value?.trim();
    if (!indexHost) {
      throw new Error('Pinecone index host is required.');
    }
    if (!apiKey) {
      throw new Error('Pinecone API key is required.');
    }
    if (!pineconeNamespace) {
      throw new Error('Pinecone namespace is required.');
    }
    return {
      provider,
      pineconeIndexHost: indexHost,
      pineconeApiKey: apiKey,
      pineconeNamespace,
      openAiApiKey: providerKeys?.OpenAI || null
    };
  }
  return { provider: 'inmemory' };
}

// ═══════════════════════════════════════════════════════════════
// PostgreSQL
// ═══════════════════════════════════════════════════════════════

const PG_STORAGE_KEY = 'rag_pg_config';

export function updatePgConnectState() {
  if (!ragPgConnect) return;
  const hasHost = !!ragPgHost?.value?.trim();
  const hasDb = !!ragPgDatabase?.value?.trim();
  ragPgConnect.disabled = !(hasHost && hasDb);
}

function updatePgDisconnectVisibility() {
  if (ragPgDisconnect) {
    ragPgDisconnect.classList.toggle('hidden', !ragState.pgConnected);
  }
}

function buildConnectionString() {
  const host = ragPgHost?.value?.trim();
  const port = ragPgPort?.value?.trim();
  const db = ragPgDatabase?.value?.trim() || '';
  const user = ragPgUser?.value?.trim() || '';
  const pass = ragPgPassword?.value || '';
  if (!host) throw new Error('PostgreSQL host is required.');
  if (!port) throw new Error('PostgreSQL port is required.');
  let parts = [`Host=${host}`, `Port=${port}`];
  if (db) parts.push(`Database=${db}`);
  if (user) parts.push(`Username=${user}`);
  if (pass) parts.push(`Password=${pass}`);
  return parts.join(';');
}

function parseConnectionString(connStr) {
  const result = { host: '', port: '', database: '', username: '', password: '' };
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
  let fields = cfg;
  if (!cfg.host && cfg.connectionString) {
    fields = parseConnectionString(cfg.connectionString);
  }
  if (ragPgHost) ragPgHost.value = fields.host || '';
  if (ragPgPort) ragPgPort.value = fields.port || '';
  if (ragPgDatabase) ragPgDatabase.value = fields.database || '';
  if (ragPgUser) ragPgUser.value = fields.username || '';
  if (ragPgPassword) ragPgPassword.value = fields.password || '';
  if (ragPgTable) ragPgTable.value = cfg.tableName || '';
  if (ragPgSchema) ragPgSchema.value = cfg.schemaName || '';
  if (ragPgDimension) ragPgDimension.value = cfg.dimension || '';
  if (ragPgEnsureSchema && typeof cfg.ensureSchema === 'boolean') ragPgEnsureSchema.checked = cfg.ensureSchema;
}

function savePgToStorage(config) {
  try { localStorage.setItem(PG_STORAGE_KEY, JSON.stringify(config)); } catch { /* ignore */ }
}

function loadPgFromStorage() {
  try {
    const raw = localStorage.getItem(PG_STORAGE_KEY);
    return raw ? JSON.parse(raw) : null;
  } catch { return null; }
}

export async function connectPostgres() {
  if (!ragPgConnect) return;
  ragPgConnect.disabled = true;
  if (ragPgStatus) {
    ragPgStatus.textContent = 'Connecting...';
    setStatusState(ragPgStatus, null);
  }
  clearSchemaWarnings(ragPgWarnings);

  const dimension = parseInt(ragPgDimension?.value, 10);
  const tableName = ragPgTable?.value?.trim();
  const schemaName = ragPgSchema?.value?.trim();
  if (!Number.isFinite(dimension) || dimension <= 0) {
    if (ragPgStatus) {
      ragPgStatus.textContent = 'PostgreSQL vector dimension is required.';
      setStatusState(ragPgStatus, 'error');
    }
    updatePgConnectState();
    return;
  }
  if (!tableName || !schemaName) {
    if (ragPgStatus) {
      ragPgStatus.textContent = 'PostgreSQL table name and schema name are required.';
      setStatusState(ragPgStatus, 'error');
    }
    updatePgConnectState();
    return;
  }

  const payload = {
    provider: 'postgres',
    connectionString: buildConnectionString(),
    tableName,
    schemaName,
    dimension,
    ensureSchema: !!ragPgEnsureSchema?.checked,
    openAiApiKey: providerKeys?.OpenAI || null,
    ...getEmbeddingSnapshot()
  };
  const storagePayload = {
    ...payload,
    host: ragPgHost?.value?.trim() || '',
    port: ragPgPort?.value?.trim() || '',
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

    ragState.pgConnected = true;
    savePgToStorage(storagePayload);
    saveLastActiveProvider('postgres');
    const hasSchemaWarnings = data.schemaWarnings && data.schemaWarnings.length > 0;
    const statusMsg = data.warning
      ? `Connected · ${data.schemaName}.${data.tableName} (dim=${data.dimension}) ⚠️ ${data.warning}`
      : `Connected · ${data.schemaName}.${data.tableName} (dim=${data.dimension})`;
    if (ragPgStatus) {
      ragPgStatus.textContent = statusMsg;
      setStatusState(ragPgStatus, (data.warning || hasSchemaWarnings) ? 'warning' : 'success');
    }
    renderSchemaWarnings(ragPgWarnings, data.schemaWarnings);
    if (ragPgConnect) ragPgConnect.textContent = 'Reconnect';
    updatePgDisconnectVisibility();
    updateRunState();
    updateVectorDbStatus();
    refreshRagStatus();
  } catch (err) {
    ragState.pgConnected = false;
    updatePgDisconnectVisibility();
    updateVectorDbStatus();
    clearSchemaWarnings(ragPgWarnings);
    if (ragPgStatus) {
      ragPgStatus.textContent = err.message || 'Connection failed.';
      setStatusState(ragPgStatus, 'error');
    }
  } finally {
    updatePgConnectState();
  }
}

export async function disconnectPostgres() {
  if (ragPgDisconnect) ragPgDisconnect.disabled = true;
  if (ragPgStatus) {
    ragPgStatus.textContent = 'Disconnecting...';
    setStatusState(ragPgStatus, null);
  }

  try {
    const res = await fetch('/api/rag/vector-store', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ provider: 'inmemory' })
    });
    await res.json().catch(() => null);

    ragState.pgConnected = false;
    clearLastActiveProvider();
    if (ragVectorStoreProvider) setSelectValue(ragVectorStoreProvider, 'inmemory');
    if (ragPgConnect) ragPgConnect.textContent = 'Connect';
    if (ragPgStatus) {
      ragPgStatus.textContent = '';
      setStatusState(ragPgStatus, null);
    }
    clearSchemaWarnings(ragPgWarnings);
    updateVectorStoreUI();
    updatePgDisconnectVisibility();
    refreshRagStatus();
    markReferenceStale();
  } catch (err) {
    if (ragPgStatus) {
      ragPgStatus.textContent = err.message || 'Failed to disconnect.';
      setStatusState(ragPgStatus, 'error');
    }
  } finally {
    if (ragPgDisconnect) ragPgDisconnect.disabled = false;
  }
}

// ═══════════════════════════════════════════════════════════════
// Qdrant
// ═══════════════════════════════════════════════════════════════

const QDRANT_STORAGE_KEY = 'rag_qdrant_config';

export function updateQdrantConnectState() {
  if (!ragQdrantConnect) return;
  const hasHost = !!ragQdrantHost?.value?.trim();
  ragQdrantConnect.disabled = !hasHost;
}

function updateQdrantDisconnectVisibility() {
  if (ragQdrantDisconnect) {
    ragQdrantDisconnect.classList.toggle('hidden', !ragState.qdrantConnected);
  }
}

function applyQdrantFields(cfg) {
  if (!cfg) return;
  if (ragQdrantHost) ragQdrantHost.value = cfg.host || cfg.qdrantHost || '';
  if (ragQdrantPort) ragQdrantPort.value = cfg.port || cfg.qdrantPort || '';
  if (ragQdrantApiKey) ragQdrantApiKey.value = cfg.apiKey || cfg.qdrantApiKey || '';
  if (ragQdrantDimension) ragQdrantDimension.value = cfg.dimension || cfg.qdrantDimension || '';
  if (ragQdrantCollection) ragQdrantCollection.value = cfg.collectionName || cfg.qdrantCollectionName || '';
  if (ragQdrantUseTls) {
    if (typeof cfg.useTls === 'boolean') ragQdrantUseTls.checked = cfg.useTls;
    else if (typeof cfg.qdrantUseTls === 'boolean') ragQdrantUseTls.checked = cfg.qdrantUseTls;
  }
}

function saveQdrantToStorage(config) {
  try { localStorage.setItem(QDRANT_STORAGE_KEY, JSON.stringify(config)); } catch { /* ignore */ }
}

function loadQdrantFromStorage() {
  try {
    const raw = localStorage.getItem(QDRANT_STORAGE_KEY);
    return raw ? JSON.parse(raw) : null;
  } catch { return null; }
}

export async function connectQdrant() {
  if (!ragQdrantConnect) return;
  ragQdrantConnect.disabled = true;
  if (ragQdrantStatus) {
    ragQdrantStatus.textContent = 'Connecting...';
    setStatusState(ragQdrantStatus, null);
  }
  clearSchemaWarnings(ragQdrantWarnings);

  const rawHost = ragQdrantHost?.value?.trim();
  const port = parseInt(ragQdrantPort?.value, 10);
  const dimension = parseInt(ragQdrantDimension?.value, 10);
  const collectionName = ragQdrantCollection?.value?.trim();
  if (!rawHost) {
    if (ragQdrantStatus) {
      ragQdrantStatus.textContent = 'Qdrant host is required.';
      setStatusState(ragQdrantStatus, 'error');
    }
    updateQdrantConnectState();
    return;
  }
  if (!Number.isFinite(dimension) || dimension <= 0) {
    if (ragQdrantStatus) {
      ragQdrantStatus.textContent = 'Qdrant vector dimension is required.';
      setStatusState(ragQdrantStatus, 'error');
    }
    updateQdrantConnectState();
    return;
  }
  if (!Number.isFinite(port) || port <= 0) {
    if (ragQdrantStatus) {
      ragQdrantStatus.textContent = 'Qdrant port is required.';
      setStatusState(ragQdrantStatus, 'error');
    }
    updateQdrantConnectState();
    return;
  }
  if (!collectionName) {
    if (ragQdrantStatus) {
      ragQdrantStatus.textContent = 'Qdrant collection name is required.';
      setStatusState(ragQdrantStatus, 'error');
    }
    updateQdrantConnectState();
    return;
  }

  let host = rawHost
    .replace(/^https?:\/\//i, '')
    .replace(/\/.*$/, '');
  if (ragQdrantHost) ragQdrantHost.value = host;
  const payload = {
    provider: 'qdrant',
    qdrantHost: host,
    qdrantPort: port,
    qdrantApiKey: ragQdrantApiKey?.value?.trim() || null,
    qdrantUseTls: !!ragQdrantUseTls?.checked,
    dimension,
    qdrantCollectionName: collectionName,
    openAiApiKey: providerKeys?.OpenAI || null,
    ...getEmbeddingSnapshot()
  };
  const storagePayload = {
    provider: 'qdrant',
    host: payload.qdrantHost,
    port: payload.qdrantPort,
    apiKey: payload.qdrantApiKey,
    useTls: payload.qdrantUseTls,
    dimension: payload.dimension,
    collectionName: payload.qdrantCollectionName
  };

  try {
    const res = await fetch('/api/rag/vector-store', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(payload)
    });
    const data = await res.json().catch(() => null);
    if (!res.ok) throw new Error(data?.error || 'Connection failed.');

    ragState.qdrantConnected = true;
    saveQdrantToStorage(storagePayload);
    saveLastActiveProvider('qdrant');
    const hasSchemaWarnings = data.schemaWarnings && data.schemaWarnings.length > 0;
    const statusMsg = data.warning
      ? `Connected · ${data.host}:${data.port} (dim=${data.dimension}) ⚠️ ${data.warning}`
      : `Connected · ${data.host}:${data.port} (dim=${data.dimension})`;
    if (ragQdrantStatus) {
      ragQdrantStatus.textContent = statusMsg;
      setStatusState(ragQdrantStatus, (data.warning || hasSchemaWarnings) ? 'warning' : 'success');
    }
    renderSchemaWarnings(ragQdrantWarnings, data.schemaWarnings);
    if (ragQdrantConnect) ragQdrantConnect.textContent = 'Reconnect';
    updateQdrantDisconnectVisibility();
    updateRunState();
    updateVectorDbStatus();
    refreshRagStatus();
  } catch (err) {
    ragState.qdrantConnected = false;
    updateQdrantDisconnectVisibility();
    updateVectorDbStatus();
    clearSchemaWarnings(ragQdrantWarnings);
    if (ragQdrantStatus) {
      ragQdrantStatus.textContent = err.message || 'Connection failed.';
      setStatusState(ragQdrantStatus, 'error');
    }
  } finally {
    updateQdrantConnectState();
  }
}

export async function disconnectQdrant() {
  if (ragQdrantDisconnect) ragQdrantDisconnect.disabled = true;
  if (ragQdrantStatus) {
    ragQdrantStatus.textContent = 'Disconnecting...';
    setStatusState(ragQdrantStatus, null);
  }

  try {
    const res = await fetch('/api/rag/vector-store', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ provider: 'inmemory' })
    });
    await res.json().catch(() => null);

    ragState.qdrantConnected = false;
    clearLastActiveProvider();
    if (ragVectorStoreProvider) setSelectValue(ragVectorStoreProvider, 'inmemory');
    if (ragQdrantConnect) ragQdrantConnect.textContent = 'Connect';
    if (ragQdrantStatus) {
      ragQdrantStatus.textContent = '';
      setStatusState(ragQdrantStatus, null);
    }
    clearSchemaWarnings(ragQdrantWarnings);
    updateVectorStoreUI();
    updateQdrantDisconnectVisibility();
    refreshRagStatus();
    markReferenceStale();
  } catch (err) {
    if (ragQdrantStatus) {
      ragQdrantStatus.textContent = err.message || 'Failed to disconnect.';
      setStatusState(ragQdrantStatus, 'error');
    }
  } finally {
    if (ragQdrantDisconnect) ragQdrantDisconnect.disabled = false;
  }
}

// ═══════════════════════════════════════════════════════════════
// Pinecone
// ═══════════════════════════════════════════════════════════════

const PINECONE_STORAGE_KEY = 'rag_pinecone_config';

export function updatePineconeConnectState() {
  if (!ragPineconeConnect) return;
  const hasHost = !!ragPineconeIndexHost?.value?.trim();
  const hasApiKey = !!ragPineconeApiKey?.value?.trim();
  const hasNamespace = !!ragPineconeNamespace?.value?.trim();
  ragPineconeConnect.disabled = !(hasHost && hasApiKey && hasNamespace);
}

function updatePineconeDisconnectVisibility() {
  if (ragPineconeDisconnect) {
    ragPineconeDisconnect.classList.toggle('hidden', !ragState.pineconeConnected);
  }
}

function applyPineconeFields(cfg) {
  if (!cfg) return;
  if (ragPineconeIndexHost) ragPineconeIndexHost.value = cfg.indexHost || cfg.pineconeIndexHost || '';
  if (ragPineconeApiKey) ragPineconeApiKey.value = cfg.apiKey || cfg.pineconeApiKey || '';
  if (ragPineconeNamespace) ragPineconeNamespace.value = cfg.namespace || cfg.pineconeNamespace || '';
}

function savePineconeToStorage(config) {
  try { localStorage.setItem(PINECONE_STORAGE_KEY, JSON.stringify(config)); } catch { /* ignore */ }
}

function loadPineconeFromStorage() {
  try {
    const raw = localStorage.getItem(PINECONE_STORAGE_KEY);
    return raw ? JSON.parse(raw) : null;
  } catch { return null; }
}

export async function connectPinecone() {
  if (!ragPineconeConnect) return;
  ragPineconeConnect.disabled = true;
  if (ragPineconeStatus) {
    ragPineconeStatus.textContent = 'Connecting...';
    setStatusState(ragPineconeStatus, null);
  }
  clearSchemaWarnings(ragPineconeWarnings);

  const pineconeIndexHost = ragPineconeIndexHost?.value?.trim();
  const pineconeApiKey = ragPineconeApiKey?.value?.trim();
  const pineconeNamespace = ragPineconeNamespace?.value?.trim();
  if (!pineconeIndexHost || !pineconeApiKey || !pineconeNamespace) {
    if (ragPineconeStatus) {
      ragPineconeStatus.textContent = 'Pinecone index host, API key, and namespace are required.';
      setStatusState(ragPineconeStatus, 'error');
    }
    updatePineconeConnectState();
    return;
  }

  const payload = {
    provider: 'pinecone',
    pineconeIndexHost,
    pineconeApiKey,
    pineconeNamespace,
    openAiApiKey: providerKeys?.OpenAI || null,
    ...getEmbeddingSnapshot()
  };
  const storagePayload = {
    provider: 'pinecone',
    indexHost: payload.pineconeIndexHost,
    apiKey: payload.pineconeApiKey,
    namespace: payload.pineconeNamespace
  };

  try {
    const res = await fetch('/api/rag/vector-store', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(payload)
    });
    const data = await res.json().catch(() => null);
    if (!res.ok) throw new Error(data?.error || 'Connection failed.');

    ragState.pineconeConnected = true;
    savePineconeToStorage(storagePayload);
    saveLastActiveProvider('pinecone');
    const connectedNs = data?.namespace ?? payload.pineconeNamespace;
    const hasSchemaWarnings = data?.schemaWarnings && data.schemaWarnings.length > 0;
    const statusMsg = data?.warning
      ? `Connected · ${payload.pineconeIndexHost} · ns=${connectedNs} ⚠️ ${data.warning}`
      : `Connected · ${payload.pineconeIndexHost} · ns=${connectedNs}`;
    if (ragPineconeStatus) {
      ragPineconeStatus.textContent = statusMsg;
      setStatusState(ragPineconeStatus, (data?.warning || hasSchemaWarnings) ? 'warning' : 'success');
    }
    renderSchemaWarnings(ragPineconeWarnings, data?.schemaWarnings);
    if (ragPineconeConnect) ragPineconeConnect.textContent = 'Reconnect';
    updatePineconeDisconnectVisibility();
    updateRunState();
    updateVectorDbStatus();
    refreshRagStatus();
  } catch (err) {
    ragState.pineconeConnected = false;
    updatePineconeDisconnectVisibility();
    updateVectorDbStatus();
    clearSchemaWarnings(ragPineconeWarnings);
    if (ragPineconeStatus) {
      ragPineconeStatus.textContent = err.message || 'Connection failed.';
      setStatusState(ragPineconeStatus, 'error');
    }
  } finally {
    updatePineconeConnectState();
  }
}

export async function disconnectPinecone() {
  if (ragPineconeDisconnect) ragPineconeDisconnect.disabled = true;
  if (ragPineconeStatus) {
    ragPineconeStatus.textContent = 'Disconnecting...';
    setStatusState(ragPineconeStatus, null);
  }

  try {
    const res = await fetch('/api/rag/vector-store', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ provider: 'inmemory' })
    });
    await res.json().catch(() => null);

    ragState.pineconeConnected = false;
    clearLastActiveProvider();
    if (ragVectorStoreProvider) setSelectValue(ragVectorStoreProvider, 'inmemory');
    if (ragPineconeConnect) ragPineconeConnect.textContent = 'Connect';
    if (ragPineconeStatus) {
      ragPineconeStatus.textContent = '';
      setStatusState(ragPineconeStatus, null);
    }
    clearSchemaWarnings(ragPineconeWarnings);
    updateVectorStoreUI();
    updatePineconeDisconnectVisibility();
    refreshRagStatus();
    markReferenceStale();
  } catch (err) {
    if (ragPineconeStatus) {
      ragPineconeStatus.textContent = err.message || 'Failed to disconnect.';
      setStatusState(ragPineconeStatus, 'error');
    }
  } finally {
    if (ragPineconeDisconnect) ragPineconeDisconnect.disabled = false;
  }
}
