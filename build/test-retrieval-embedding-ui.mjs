// Real HTML and RAG modules in jsdom. Every HTTP response is a local fixture.
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import vm from 'node:vm';
import { createRequire } from 'node:module';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const require = createRequire(path.join(root, 'build/ui-tests/package.json'));
const { JSDOM } = require('jsdom');
const web = path.join(root, 'apps/Mythosia.AI.Samples.ChatUi/wwwroot');
const real = new Set(['dom.js', 'utils.js', 'state.js', 'rag-embedding.js', 'rag-shared.js',
  'rag-pipeline.js', 'rag-run.js', 'rag-vector-store.js', 'rag-trace.js', 'rag-rewriter-models.js']);
const turn = () => new Promise(resolve => setImmediate(resolve));

async function fixture() {
  const dom = new JSDOM(fs.readFileSync(path.join(web, 'index.html'), 'utf8'), {
    url: 'https://retrieval-embedding.test/', runScripts: 'outside-only'
  });
  const { window } = dom;
  const sources = new Map([...real].map(name => [name, fs.readFileSync(path.join(web, 'js', name), 'utf8')]));
  const requested = new Map();
  for (const source of sources.values()) {
    for (const match of source.matchAll(/import\s*\{([^}]+)\}\s*from\s*'\.\/([^']+)'/g)) {
      const names = requested.get(match[2]) || new Set();
      for (const name of match[1].split(',')) names.add(name.trim().split(/\s+as\s+/)[0]);
      requested.set(match[2], names);
    }
  }
  const requests = [];
  const reply = (body, ok = true) => ({ ok, json: async () => body });
  let transport = async url => {
    if (url === '/api/rag/reference-history') return reply({ history: [] });
    if (url === '/api/rag/status') return reply({ hasIndex: false });
    return reply({ error: 'Synthetic offline rejection after capture.' }, false);
  };
  window.fetch = (url, options = {}) => {
    assert.ok(String(url).startsWith('/api/'), 'No remote provider request is allowed');
    requests.push({ url, ...options });
    return transport(url, options);
  };
  const modules = new Map();
  function load(name) {
    if (modules.has(name)) return modules.get(name);
    let module;
    if (real.has(name)) {
      module = new vm.SourceTextModule(sources.get(name), { context: dom.getInternalVMContext(), identifier: name });
    } else {
      const names = [...(requested.get(name) || [])];
      module = new vm.SyntheticModule(names, function () {
        for (const name of names) this.setExport(name, () => {});
      }, { context: dom.getInternalVMContext(), identifier: name });
    }
    modules.set(name, module);
    return module;
  }
  const entry = load('rag-run.js');
  await entry.link(specifier => load(specifier.replace('./', '')));
  await entry.evaluate();
  for (const name of real) {
    const module = load(name);
    if (module.status === 'unlinked') await module.link(specifier => load(specifier.replace('./', '')));
    if (module.status === 'linked') await module.evaluate();
  }
  const el = id => {
    const node = window.document.getElementById(id);
    assert.ok(node, `Missing real HTML control #${id}`);
    return node;
  };
  Object.defineProperty(el('rag-files'), 'files', { configurable: true,
    value: [new window.File(['Synthetic refund policy.'], 'policy.txt', { type: 'text/plain' })] });
  el('rag-vectorstore-provider').value = 'inmemory';
  return { dom, window, el, requests, reply,
    api: name => modules.get(name).namespace,
    transport: handler => { transport = handler; }
  };
}

// Assertions below exercise browser controls, request payloads, cancellation,
// stored settings and document/vector association together.
const f = await fixture();
try {
  const { el, requests, window } = f;
  const embedding = f.api('rag-embedding.js');
  const pipeline = f.api('rag-pipeline.js');
  const vector = f.api('rag-vector-store.js');
  const shared = f.api('rag-shared.js');
  const run = f.api('rag-run.js');
  const { providerKeys } = f.api('state.js');
  const uploads = () => requests.filter(request => request.url === '/api/rag/reference');
  const selectProvider = (provider, model) => {
    el('rag-embedding-provider').value = provider;
    if (model) el(`rag-${provider}-model`).value = model;
    embedding.updateEmbeddingUI(true);
  };
  const settingsByProvider = new Map();
  providerKeys.OpenAI = 'synthetic-openai';
  for (const [provider, model, dimensions, keyName, field] of [
    ['voyage', 'voyage-context-4', 1024, 'Voyage', 'voyageApiKey'],
    ['gemini', 'gemini-embedding-2', 1536, 'Google', 'geminiApiKey'],
    ['perplexity', 'pplx-embed-context-v1-0.6b', 1024, 'Perplexity', 'perplexityApiKey'],
    ['perplexity', 'pplx-embed-context-v1-4b', 2560, 'Perplexity', 'perplexityApiKey']
  ]) {
    delete providerKeys[keyName];
    selectProvider(provider, model);
    assert.equal(embedding.getSelectedEmbeddingDimensions(), dimensions);
    assert.equal(el('rag-run').disabled, true, 'An unrelated OpenAI key must not unlock another provider');
    const before = uploads().length;
    await run.runReference();
    assert.equal(uploads().length, before, 'Missing key is rejected before upload');
    providerKeys[keyName] = `synthetic-${provider}`;
    embedding.updateEmbeddingUI();
    assert.equal(el('rag-run').disabled, false);
    assert.deepEqual(Object.keys(embedding.getEmbeddingCredentials()), [field]);
    assert.equal(embedding.getEmbeddingCredentials()[field], providerKeys[keyName]);
    el('rag-embedding-timeout').value = '45';
    el('rag-embedding-concurrency').value = '2';
    const settings = pipeline.buildPipelineSettingsPayload();
    assert.equal(settings.embeddingTimeoutSeconds, 45);
    if (provider === 'gemini') assert.equal(settings.embeddingMaxConcurrency, 2);
    assert.equal(settings.embeddingProvider, provider);
    assert.equal(settings.embeddingModel, model);
    assert.equal(settings.embeddingDimensions, dimensions);
    assert.ok(!JSON.stringify(settings).includes('synthetic-'), 'No provider credentials in cached pipeline settings');
    await pipeline.savePipelineSettings();
    const cached = JSON.parse(window.localStorage.getItem('rag_pipeline_settings'));
    assert.equal(cached.embeddingTimeoutSeconds, 45);
    selectProvider('openai');
    pipeline.applyPipelineSettings(settings);
    assert.equal(el('rag-embedding-provider').value, provider);
    assert.equal(el(`rag-${provider}-model`).value, model);
    assert.equal(embedding.getSelectedEmbeddingDimensions(), dimensions);
    assert.equal(el('rag-embedding-timeout').value, '45');
    settingsByProvider.set(provider, settings);
    await run.runReference();
    const request = uploads().at(-1);
    assert.equal(uploads().length, before + 1);
    assert.equal(request.body.get('embeddingProvider'), provider);
    assert.equal(request.body.get('embeddingModel'), model);
    assert.equal(request.body.get('embeddingDimensions'), String(dimensions));
    assert.equal(request.body.get('embeddingTimeoutSeconds'), '45');
    if (provider === 'gemini') assert.equal(request.body.get('embeddingMaxConcurrency'), '2');
    assert.equal(request.body.get(field), providerKeys[keyName]);
    assert.equal(request.body.getAll(field).length, 1);
    for (const other of ['openaiApiKey', 'perplexityApiKey', 'geminiApiKey', 'voyageApiKey']) {
      if (other !== field) assert.equal(request.body.has(other), false, `Do not transmit unrelated ${other}`);
    }
    assert.ok(request.signal, 'Every indexing request is cancellable');
    assert.equal(el('rag-run').disabled, false, 'Provider failure leaves the action usable');
  }

  for (const [provider, valid, invalid] of [
    ['voyage', [256, 512, 1024, 2048], [128, 513, 2049, 1024.5]],
    ['gemini', [128, 1536, 3072], [127, 3073, 1536.5]],
    ['perplexity', [128, 513, 2560], [127, 2561, 513.5]]
  ]) {
    pipeline.applyPipelineSettings(settingsByProvider.get(provider));
    for (const dimensions of valid) {
      el(`rag-${provider}-dimensions`).value = String(dimensions);
      assert.equal(embedding.getSelectedEmbeddingDimensions(), dimensions);
    }
    for (const dimensions of invalid) {
      el(`rag-${provider}-dimensions`).value = String(dimensions);
      assert.throws(() => pipeline.buildPipelineSettingsPayload(), /dimension/i);
    }
  }
  pipeline.applyPipelineSettings(settingsByProvider.get('gemini'));
  for (const value of ['', '0', '601', '1.5']) {
    el('rag-embedding-timeout').value = value;
    assert.throws(() => embedding.getEmbeddingExecutionSettings(), /timeout|time limit/i);
  }
  el('rag-embedding-timeout').value = '600';
  for (const value of ['', '0', '17', '1.5']) {
    el('rag-embedding-concurrency').value = value;
    assert.throws(() => embedding.getEmbeddingExecutionSettings(), /concurren|request/i);
  }
  el('rag-embedding-concurrency').value = '16';
  assert.equal(embedding.getEmbeddingExecutionSettings().embeddingMaxConcurrency, 16);

  // A hidden Gemini-only value must not prevent another provider from running.
  for (const hiddenValue of ['', '0', '17', '1.5']) {
    for (const provider of ['voyage', 'openai', 'perplexity']) {
      selectProvider('gemini');
      el('rag-embedding-concurrency').value = hiddenValue;
      assert.throws(() => embedding.getEmbeddingExecutionSettings(), /concurren|request/i);
      selectProvider(provider);
      assert.equal(el('rag-embedding-concurrency-row').classList.contains('hidden'), true);
      assert.equal(pipeline.buildPipelineSettingsPayload().embeddingMaxConcurrency, 4,
        'Non-Gemini requests use the valid default and ignore the hidden Gemini field');
      const beforeUpload = uploads().length;
      await run.runReference();
      assert.equal(uploads().length, beforeUpload + 1);
      assert.equal(uploads().at(-1).body.get('embeddingMaxConcurrency'), '4');
    }
  }

  // Equal dimensions do not make vectors from different models compatible.
  pipeline.applyPipelineSettings(settingsByProvider.get('voyage'));
  assert.equal(embedding.updateEmbeddingReindexWarning({ provider: 'gemini', model: 'gemini-embedding-2',
    dimensions: 1024, baseUrl: '' }, true), true);
  assert.equal(el('rag-embedding-reindex-warning').classList.contains('hidden'), false);
  assert.equal(embedding.updateEmbeddingReindexWarning({ provider: 'voyage', model: 'voyage-context-4',
    dimensions: 1024, baseUrl: '' }, true), false);
  el('rag-embedding-timeout').value = '90';
  assert.equal(embedding.updateEmbeddingReindexWarning(), false, 'Execution timeouts do not change the vector space');
  embedding.updateEmbeddingReindexWarning(null, false);

  const legacySettings = { ...settingsByProvider.get('perplexity') };
  delete legacySettings.embeddingTimeoutSeconds;
  delete legacySettings.embeddingMaxConcurrency;
  pipeline.applyPipelineSettings(legacySettings);
  assert.equal(el('rag-embedding-timeout').value, '120', 'Older saved settings receive the default timeout');
  assert.equal(el('rag-embedding-concurrency').value, '4', 'Older saved settings receive the default concurrency');

  // Reconnection and upload must use the same selected vector space, credentials and execution settings.
  for (const provider of ['voyage', 'gemini']) {
    pipeline.applyPipelineSettings(settingsByProvider.get(provider));
    el('rag-vectorstore-provider').value = 'qdrant';
    el('rag-qdrant-host').value = 'localhost';
    el('rag-qdrant-port').value = '6334';
    el('rag-qdrant-collection').value = 'synthetic';
    const dimensions = embedding.getSelectedEmbeddingDimensions();
    el('rag-qdrant-dimension').value = String(dimensions);
    shared.ragState.qdrantConnected = true;
    const snapshot = vector.getVectorStoreConfigForRequest();
    assert.equal(snapshot[`${provider}ApiKey`], `synthetic-${provider}`);
    assert.equal(Object.hasOwn(snapshot, 'openAiApiKey'), false);
    const before = uploads().length;
    el('rag-qdrant-dimension').value = String(dimensions === 1024 ? 1536 : 1024);
    await run.runReference();
    assert.equal(uploads().length, before, 'A connected DB must not silently overwrite the requested embedding dimensions');
    assert.match(el('rag-status').textContent, /dimension/i);
    assert.equal(el(`rag-${provider}-dimensions`).value, String(dimensions));
    el('rag-qdrant-dimension').value = String(dimensions);
    f.transport(async url => f.reply(url === '/api/rag/vector-store'
      ? { dimension: dimensions, collectionName: 'synthetic' } : { hasIndex: false }));
    await vector.connectQdrant();
    const connection = JSON.parse(requests.filter(request => request.url === '/api/rag/vector-store').at(-1).body);
    assert.equal(connection.embeddingProvider, provider);
    assert.equal(connection.embeddingModel, settingsByProvider.get(provider).embeddingModel);
    assert.equal(connection.embeddingDimensions, dimensions);
    assert.equal(connection.embeddingTimeoutSeconds, 45);
    assert.equal(connection[`${provider}ApiKey`], `synthetic-${provider}`);
    if (provider === 'gemini') assert.equal(connection.embeddingMaxConcurrency, 2);
    assert.equal(window.localStorage.getItem('rag_qdrant_config').includes(`synthetic-${provider}`), false);
    el('rag-vectorstore-provider').value = 'inmemory';
  }

  // Stop before headers, block double submissions, and recover without entering a network handler.
  pipeline.applyPipelineSettings(settingsByProvider.get('voyage'));
  f.transport((url, options) => {
    if (url !== '/api/rag/reference') return Promise.resolve(f.reply({ hasIndex: false }));
    return new Promise((resolve, reject) => options.signal.addEventListener('abort',
      () => reject(new window.DOMException('Canceled fixture', 'AbortError')), { once: true }));
  });
  const beforeCancel = uploads().length;
  const pending = run.runReference();
  assert.equal(el('rag-run').disabled, true);
  assert.equal(el('rag-embed-cancel').classList.contains('hidden'), false);
  await run.runReference();
  assert.equal(uploads().length, beforeCancel + 1, 'Only one indexing request can be active');
  run.cancelReference();
  await pending;
  assert.equal(uploads().at(-1).signal.aborted, true);
  assert.match(el('rag-status').textContent, /cancel|stop/i);
  assert.equal(el('rag-run').disabled, false);
  assert.equal(el('rag-embed-progress').classList.contains('hidden'), true);

  // A late JSON payload after cancellation cannot install a stale successful result.
  let releaseBody;
  f.transport(async url => url === '/api/rag/reference'
    ? { ok: true, json: () => new Promise(resolve => { releaseBody = resolve; }) }
    : f.reply(url === '/api/rag/reference-history' ? { history: [] } : { hasIndex: false }));
  const late = run.runReference();
  await turn();
  assert.ok(releaseBody);
  run.cancelReference();
  releaseBody({ summary: { documentCount: 1, chunkCount: 0, embeddingCount: 0, recordCount: 0, dimensions: 1024 },
    documents: [], chunks: [], embeddings: [], records: [] });
  await late;
  assert.match(el('rag-status').textContent, /cancel|stop/i);
  assert.equal(shared.ragState.hasReferenceRun, false);

  f.transport(async () => f.reply({ error: 'Synthetic provider failure.' }, false));
  await run.runReference();
  assert.match(el('rag-status').textContent, /Synthetic provider failure/);
  assert.equal(el('rag-run').disabled, false);

  // Document counts follow IDs, not identical filenames, and chunks keep source order.
  const trace = {
    summary: { documentCount: 2, chunkCount: 3, embeddingCount: 3, recordCount: 2, dimensions: 1024 },
    documents: ['a', 'b'].map(id => ({ id, source: 'policy<img src=x>.txt', contentLength: 100 })),
    chunks: [
      { id: 'a-1', documentId: 'a', index: 1, content: 'second', metadata: {} },
      { id: 'b-0', documentId: 'b', index: 0, content: 'other document', metadata: {} },
      { id: 'a-0', documentId: 'a', index: 0, content: 'first', metadata: {} }
    ],
    embeddings: ['a-0', 'a-1', 'b-0'].map(chunkId => ({ chunkId, dimensions: 1024, sample: [0.5], vector: [0.5] })),
    records: ['a-0', 'b-0'].map(id => ({ id, content: id, metadata: {}, vector: [0.5], dimensions: 1024 }))
  };
  const panel = window.document.createElement('div');
  f.api('rag-trace.js').renderTrace(panel, trace);
  const docNodes = panel.querySelectorAll('#rag-tp-docs > .rag-tree > .rag-tree-node');
  assert.equal(docNodes.length, 2);
  assert.deepEqual([...docNodes[0].querySelectorAll('.rag-document-counts strong')].map(n => n.textContent), ['2', '2', '1']);
  assert.deepEqual([...docNodes[1].querySelectorAll('.rag-document-counts strong')].map(n => n.textContent), ['1', '1', '1']);
  assert.ok(docNodes[0].textContent.indexOf('first') < docNodes[0].textContent.indexOf('second'));
  assert.equal(panel.querySelector('img'), null, 'Document filenames remain escaped');

  f.transport(async url => f.reply(url === '/api/rag/reference' ? trace
    : url === '/api/rag/reference-history' ? { history: [] } : { hasIndex: true,
      indexedEmbedding: { provider: 'voyage', model: 'voyage-context-4', dimensions: 1024, baseUrl: '' } }));
  await run.runReference();
  await turn();
  assert.equal(shared.ragState.hasReferenceRun, true, 'Successful indexing works after cancellation and failure');
  assert.equal(el('rag-run').disabled, false);

  // An older status request must not overwrite a newer completed index snapshot.
  let releaseOldStatus;
  let statusReads = 0;
  const currentIdentity = { provider: 'voyage', model: 'voyage-context-4', dimensions: 1024, baseUrl: '' };
  f.transport(async url => {
    assert.equal(url, '/api/rag/status');
    if (++statusReads === 1) return { ok: true, json: () => new Promise(resolve => { releaseOldStatus = resolve; }) };
    return f.reply({ hasIndex: true, indexedEmbedding: currentIdentity });
  });
  const oldStatus = run.refreshRagStatus();
  await turn();
  await run.refreshRagStatus();
  assert.equal(shared.ragState.hasIndex, true);
  releaseOldStatus({ hasIndex: false, indexedEmbedding: null });
  await oldStatus;
  assert.equal(shared.ragState.hasIndex, true, 'A stale status response cannot erase the newly indexed embedding identity');
  assert.deepEqual(JSON.parse(JSON.stringify(shared.ragState.indexedEmbedding)), currentIdentity);
} finally { f.dom.window.close(); }

console.log('PASS: retrieval embedding controls, isolated keys, dimensions, saved settings, reconnect payloads and cancellable indexing; no remote HTTP sent.');
