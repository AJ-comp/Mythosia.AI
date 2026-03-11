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
  ragPineconeConfig,
  ragPineconeIndexHost,
  ragPineconeApiKey,
  ragPineconeNamespace,
  ragPineconeConnect,
  ragPineconeDisconnect,
  ragPineconeStatus
} from './dom.js';
import { providerKeys } from './state.js';
import { ragState, setSelectValue, markReferenceStale, updateRunState } from './rag-shared.js';
import { refreshRagStatus, updateVectorDbStatus } from './rag-run.js';

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
  if (ragPgStatus) ragPgStatus.textContent = '';
  if (ragQdrantConnect) ragQdrantConnect.textContent = 'Connect';
  if (ragQdrantStatus) ragQdrantStatus.textContent = '';
  if (ragPineconeConnect) ragPineconeConnect.textContent = 'Connect';
  if (ragPineconeStatus) ragPineconeStatus.textContent = '';
  updateVectorDbStatus();
  updateRunState();
  refreshRagStatus();
  markReferenceStale();
}

// ── Provider UI ──────────────────────────────────────────────
export function updateVectorStoreUI() {
  const provider = ragVectorStoreProvider?.value || 'inmemory';
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

  // Fallback: check server state
  try {
    const res = await fetch('/api/rag/vector-store');
    const data = await res.json().catch(() => null);
    if (!res.ok) return;

    if (data?.provider === 'postgres') {
      if (ragVectorStoreProvider) setSelectValue(ragVectorStoreProvider, 'postgres');
      applyPgFields(data);
      ragState.pgConnected = true;
      if (ragPgConnect) ragPgConnect.textContent = 'Reconnect';
      if (ragPgStatus) ragPgStatus.textContent = `Connected · ${data.schemaName}.${data.tableName}`;
      updatePgDisconnectVisibility();
    } else if (data?.provider === 'qdrant') {
      if (ragVectorStoreProvider) setSelectValue(ragVectorStoreProvider, 'qdrant');
      applyQdrantFields(data);
      ragState.qdrantConnected = true;
      if (ragQdrantConnect) ragQdrantConnect.textContent = 'Reconnect';
      if (ragQdrantStatus) ragQdrantStatus.textContent = `Connected · ${data.qdrantHost}:${data.qdrantPort}`;
      updateQdrantDisconnectVisibility();
    } else if (data?.provider === 'pinecone') {
      if (ragVectorStoreProvider) setSelectValue(ragVectorStoreProvider, 'pinecone');
      applyPineconeFields(data);
      ragState.pineconeConnected = true;
      if (ragPineconeConnect) ragPineconeConnect.textContent = 'Reconnect';
      if (ragPineconeStatus) ragPineconeStatus.textContent = `Connected · ${data.pineconeIndexHost} · ns=${data.pineconeNamespace || 'default'}`;
      updatePineconeDisconnectVisibility();
    }
    updateVectorStoreUI();
  } catch { /* ignore */ }
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

    ragState.pgConnected = true;
    savePgToStorage(storagePayload);
    saveLastActiveProvider('postgres');
    const statusMsg = data.warning
      ? `Connected · ${data.schemaName}.${data.tableName} (dim=${data.dimension}) ⚠️ ${data.warning}`
      : `Connected · ${data.schemaName}.${data.tableName} (dim=${data.dimension})`;
    if (ragPgStatus) ragPgStatus.textContent = statusMsg;
    if (ragPgConnect) ragPgConnect.textContent = 'Reconnect';
    updatePgDisconnectVisibility();
    updateRunState();
    updateVectorDbStatus();
    refreshRagStatus();
  } catch (err) {
    ragState.pgConnected = false;
    updatePgDisconnectVisibility();
    updateVectorDbStatus();
    if (ragPgStatus) ragPgStatus.textContent = err.message || 'Connection failed.';
  } finally {
    updatePgConnectState();
  }
}

export async function disconnectPostgres() {
  if (ragPgDisconnect) ragPgDisconnect.disabled = true;
  if (ragPgStatus) ragPgStatus.textContent = 'Disconnecting...';

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
  if (ragQdrantHost) ragQdrantHost.value = cfg.host || cfg.qdrantHost || 'localhost';
  if (ragQdrantPort) ragQdrantPort.value = cfg.port || cfg.qdrantPort || 6334;
  if (ragQdrantApiKey) ragQdrantApiKey.value = cfg.apiKey || cfg.qdrantApiKey || '';
  if (ragQdrantDimension) ragQdrantDimension.value = cfg.dimension || cfg.qdrantDimension || 1536;
  if (ragQdrantCollection) ragQdrantCollection.value = cfg.collectionName || cfg.qdrantCollectionName || 'default';
  if (ragQdrantUseTls) ragQdrantUseTls.checked = cfg.useTls ?? cfg.qdrantUseTls ?? false;
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
  if (ragQdrantStatus) ragQdrantStatus.textContent = 'Connecting...';

  let host = (ragQdrantHost?.value?.trim() || 'localhost')
    .replace(/^https?:\/\//i, '')
    .replace(/\/.*$/, '');
  if (ragQdrantHost) ragQdrantHost.value = host;
  const payload = {
    provider: 'qdrant',
    qdrantHost: host,
    qdrantPort: parseInt(ragQdrantPort?.value, 10) || 6334,
    qdrantApiKey: ragQdrantApiKey?.value?.trim() || null,
    qdrantUseTls: ragQdrantUseTls?.checked ?? false,
    dimension: parseInt(ragQdrantDimension?.value, 10) || 1536,
    qdrantCollectionName: ragQdrantCollection?.value?.trim() || 'default',
    openAiApiKey: providerKeys?.OpenAI || null
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
    const statusMsg = data.warning
      ? `Connected · ${data.host}:${data.port} (dim=${data.dimension}) ⚠️ ${data.warning}`
      : `Connected · ${data.host}:${data.port} (dim=${data.dimension})`;
    if (ragQdrantStatus) ragQdrantStatus.textContent = statusMsg;
    if (ragQdrantConnect) ragQdrantConnect.textContent = 'Reconnect';
    updateQdrantDisconnectVisibility();
    updateRunState();
    updateVectorDbStatus();
    refreshRagStatus();
  } catch (err) {
    ragState.qdrantConnected = false;
    updateQdrantDisconnectVisibility();
    updateVectorDbStatus();
    if (ragQdrantStatus) ragQdrantStatus.textContent = err.message || 'Connection failed.';
  } finally {
    updateQdrantConnectState();
  }
}

export async function disconnectQdrant() {
  if (ragQdrantDisconnect) ragQdrantDisconnect.disabled = true;
  if (ragQdrantStatus) ragQdrantStatus.textContent = 'Disconnecting...';

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
    if (ragQdrantStatus) ragQdrantStatus.textContent = '';
    updateVectorStoreUI();
    updateQdrantDisconnectVisibility();
    refreshRagStatus();
    markReferenceStale();
  } catch (err) {
    if (ragQdrantStatus) ragQdrantStatus.textContent = err.message || 'Failed to disconnect.';
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
  ragPineconeConnect.disabled = !(hasHost && hasApiKey);
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
  if (ragPineconeNamespace) ragPineconeNamespace.value = cfg.namespace || cfg.pineconeNamespace || 'default';
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
  if (ragPineconeStatus) ragPineconeStatus.textContent = 'Connecting...';

  const payload = {
    provider: 'pinecone',
    pineconeIndexHost: ragPineconeIndexHost?.value?.trim() || '',
    pineconeApiKey: ragPineconeApiKey?.value?.trim() || '',
    pineconeNamespace: ragPineconeNamespace?.value?.trim() || 'default',
    openAiApiKey: providerKeys?.OpenAI || null
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
    const connectedNs = data?.namespace || payload.pineconeNamespace;
    const statusMsg = data?.warning
      ? `Connected · ${payload.pineconeIndexHost} · ns=${connectedNs} ⚠️ ${data.warning}`
      : `Connected · ${payload.pineconeIndexHost} · ns=${connectedNs}`;
    if (ragPineconeStatus) ragPineconeStatus.textContent = statusMsg;
    if (ragPineconeConnect) ragPineconeConnect.textContent = 'Reconnect';
    updatePineconeDisconnectVisibility();
    updateRunState();
    updateVectorDbStatus();
    refreshRagStatus();
  } catch (err) {
    ragState.pineconeConnected = false;
    updatePineconeDisconnectVisibility();
    updateVectorDbStatus();
    if (ragPineconeStatus) ragPineconeStatus.textContent = err.message || 'Connection failed.';
  } finally {
    updatePineconeConnectState();
  }
}

export async function disconnectPinecone() {
  if (ragPineconeDisconnect) ragPineconeDisconnect.disabled = true;
  if (ragPineconeStatus) ragPineconeStatus.textContent = 'Disconnecting...';

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
    if (ragPineconeStatus) ragPineconeStatus.textContent = '';
    updateVectorStoreUI();
    updatePineconeDisconnectVisibility();
    refreshRagStatus();
    markReferenceStale();
  } catch (err) {
    if (ragPineconeStatus) ragPineconeStatus.textContent = err.message || 'Failed to disconnect.';
  } finally {
    if (ragPineconeDisconnect) ragPineconeDisconnect.disabled = false;
  }
}
