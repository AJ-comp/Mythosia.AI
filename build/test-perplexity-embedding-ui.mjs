// Run with: node --experimental-vm-modules build/test-perplexity-embedding-ui.mjs
// Executes the real browser modules against a minimal DOM populated from the real HTML. No HTTP is sent.
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import vm from 'node:vm';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const web = path.join(root, 'apps/Mythosia.AI.Samples.ChatUi/wwwroot');
const html = fs.readFileSync(path.join(web, 'index.html'), 'utf8');
const domSource = fs.readFileSync(path.join(web, 'js/dom.js'), 'utf8');
const actual = new Set(['rag-embedding.js', 'rag-shared.js', 'rag-pipeline.js', 'rag-run.js', 'rag-vector-store.js']);
const sources = Object.fromEntries([...actual].map(name => [name, fs.readFileSync(path.join(web, 'js', name), 'utf8')]));
const requested = new Map();
for (const source of Object.values(sources)) {
  for (const match of source.matchAll(/import\s*\{([^}]+)\}\s*from\s*'\.\/([^']+)'/g)) {
    const names = requested.get(match[2]) || new Set();
    for (const name of match[1].split(',')) names.add(name.trim().split(/\s+as\s+/)[0]);
    requested.set(match[2], names);
  }
}

function element(id) {
  const escaped = id.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
  const start = html.match(new RegExp(`<([a-z0-9]+)\\b[^>]*\\bid="${escaped}"[^>]*>`, 'i'));
  assert.ok(start, `Actual HTML must contain #${id}`);
  const tag = start[0];
  let value = tag.match(/\bvalue="([^"]*)"/)?.[1] || '';
  if (start[1] === 'select') {
    const body = html.slice(start.index + tag.length).split('</select>')[0];
    const options = [...body.matchAll(/<option\b([^>]*)>/g)];
    const selected = options.find(option => /\bselected\b/.test(option[1])) || options[0];
    value = selected?.[1].match(/\bvalue="([^"]*)"/)?.[1] || '';
  }
  const classes = new Set((tag.match(/\bclass="([^"]*)"/)?.[1] || '').split(' '));
  const listeners = new Map();
  return {
    id, get value() { return value; }, set value(next) { value = String(next); },
    checked: /\bchecked\b/.test(tag), disabled: /\bdisabled\b/.test(tag), files: [], textContent: '', innerHTML: '',
    max: tag.match(/\bmax="([^"]*)"/)?.[1] || '', min: tag.match(/\bmin="([^"]*)"/)?.[1] || '', style: {},
    classList: {
      add(...names) { for (const name of names) classes.add(name); },
      remove(...names) { for (const name of names) classes.delete(name); },
      contains(name) { return classes.has(name); },
      toggle(name, force) { if (force ?? !classes.has(name)) classes.add(name); else classes.delete(name); }
    },
    addEventListener(name, action) { listeners.set(name, [...(listeners.get(name) || []), action]); },
    dispatchEvent(event) { for (const action of listeners.get(event.type) || []) action(event); },
    querySelectorAll() { return []; }, querySelector() { return null; }, setAttribute() {}
  };
}

const elements = {};
for (const name of requested.get('dom.js')) {
  const match = domSource.match(new RegExp(`export const ${name}\\s*=\\s*\\$\\('#([^']+)'\\)`));
  assert.ok(match, `Actual dom.js must export ${name}`);
  elements[name] = element(match[1]);
}
const providerKeys = {};
const stored = new Map();
const requests = [];
let savedKeys = 0;
const context = vm.createContext({
  console, Event, FormData, Blob, URL, setTimeout, clearTimeout,
  document: { createElement: () => ({ className: '', textContent: '' }) },
  localStorage: { getItem: name => stored.get(name) ?? null, setItem: (name, value) => stored.set(name, value), removeItem: name => stored.delete(name) },
  fetch: async (url, options) => {
    requests.push({ url, ...options });
    // Stop after request inspection without running follow-up UI refresh calls or invoking any server.
    return { ok: false, json: async () => ({ error: 'Synthetic offline response after request capture.' }) };
  }
});
const modules = new Map();
function load(name) {
  if (modules.has(name)) return modules.get(name);
  let module;
  if (actual.has(name)) module = new vm.SourceTextModule(sources[name], { context, identifier: name });
  else {
    const exports = [...(requested.get(name) || [])];
    module = new vm.SyntheticModule(exports, function () {
      for (const item of exports) {
        let value = () => '';
        if (name === 'dom.js') value = elements[item];
        if (name === 'state.js' && item === 'providerKeys') value = providerKeys;
        if (name === 'state.js' && item === 'saveKeysToStorage') value = () => { savedKeys++; };
        if (name === 'utils.js' && item === 'escapeHtml') value = text => String(text);
        this.setExport(item, value);
      }
    }, { context, identifier: name });
  }
  modules.set(name, module);
  return module;
}
const entry = load('rag-embedding.js');
await entry.link(specifier => load(specifier.replace('./', '')));
await entry.evaluate();
// Some modules are reached only through the pipeline/run cycle, not the embedding entry point.
for (const name of actual) {
  const module = load(name);
  if (module.status === 'unlinked') await module.link(specifier => load(specifier.replace('./', '')));
  if (module.status === 'linked') await module.evaluate();
}
const embedding = modules.get('rag-embedding.js').namespace;
const shared = modules.get('rag-shared.js').namespace;
const pipeline = modules.get('rag-pipeline.js').namespace;
const vector = modules.get('rag-vector-store.js').namespace;
const run = modules.get('rag-run.js').namespace;
const el = elements;

const modelSelect = html.match(/<select id="rag-perplexity-model"[^>]*>([\s\S]*?)<\/select>/)[1];
assert.deepEqual([...modelSelect.matchAll(/<option value="([^"]+)"/g)].map(match => match[1]), ['pplx-embed-v1-0.6b', 'pplx-embed-v1-4b']);
assert.equal(el.ragPerplexityDimensions.min, '128');
el.ragEmbeddingProvider.value = 'perplexity';
el.ragVectorStoreProvider.value = 'inmemory';
el.ragFiles.files = [new Blob(['Synthetic refund policy for this test only.'], { type: 'text/plain' })];
el.ragFiles.files[0].name = 'synthetic.txt';
providerKeys.OpenAI = 'openai-offline-key';
embedding.updateEmbeddingUI(true);
assert.equal(el.ragPerplexityModelRow.classList.contains('hidden'), false);
assert.equal(el.ragOpenAiModelRow.classList.contains('hidden'), true);
assert.equal(el.ragRun.disabled, true, 'OpenAI key must not unlock Perplexity embedding');
assert.equal(el.ragPerplexityKey.classList.contains('hidden'), false);
assert.equal(embedding.getSelectedEmbeddingDimensions(), 1024);

el.ragPerplexityKeyInput.value = 'perplexity-offline-key';
embedding.saveInlinePerplexityKey();
assert.equal(savedKeys, 1);
assert.equal(providerKeys.Perplexity, 'perplexity-offline-key');
assert.equal(el.ragPerplexityKeyInput.value, '');
assert.equal(el.ragRun.disabled, false);
assert.deepEqual(Object.keys(embedding.getEmbeddingCredentials()), ['perplexityApiKey']);

el.ragPerplexityModel.value = 'pplx-embed-v1-4b';
embedding.updateEmbeddingUI(true);
assert.equal(embedding.getSelectedEmbeddingDimensions(), 2560);
assert.equal(el.ragPerplexityDimensions.max, '2560');
el.ragPerplexityDimensions.value = '513';
embedding.updateEmbeddingUI();
assert.equal(embedding.getSelectedEmbeddingDimensions(), 513, 'Refresh must retain custom Matryoshka dimension');
el.ragPerplexityDimensions.value = '2561';
assert.throws(() => embedding.getSelectedEmbeddingDimensions(), /between 128 and 2560/);
el.ragPerplexityDimensions.value = '128.5';
assert.throws(() => embedding.getSelectedEmbeddingDimensions(), /integer/);
await pipeline.savePipelineSettings();
assert.equal(el.ragSettingsSave.disabled, false, 'Invalid dimensions must not leave Save permanently disabled');
assert.match(el.ragSettingsStatus.textContent, /integer/);
el.ragPerplexityDimensions.value = '513';

const settings = pipeline.buildPipelineSettingsPayload();
assert.equal(settings.embeddingProvider, 'perplexity');
assert.equal(settings.embeddingModel, 'pplx-embed-v1-4b');
assert.equal(settings.embeddingDimensions, 513);
assert.equal(settings.embeddingBaseUrl, '');
assert.equal(Object.hasOwn(settings, 'perplexityApiKey'), false, 'Embedding key stays separate from pipeline settings');
el.ragEmbeddingProvider.value = 'openai';
pipeline.applyPipelineSettings(settings);
assert.equal(el.ragEmbeddingProvider.value, 'perplexity');
assert.equal(el.ragPerplexityModel.value, 'pplx-embed-v1-4b');
assert.equal(embedding.getSelectedEmbeddingDimensions(), 513);
assert.match(el.ragEmbeddingStatus.textContent, /pplx-embed-v1-4b/);

el.ragVectorStoreProvider.value = 'qdrant';
el.ragQdrantHost.value = 'localhost'; el.ragQdrantPort.value = '6334';
el.ragQdrantCollection.value = 'synthetic'; el.ragQdrantDimension.value = '513';
shared.ragState.qdrantConnected = true;
const reconnect = vector.getVectorStoreConfigForRequest();
assert.equal(reconnect.perplexityApiKey, 'perplexity-offline-key');
assert.equal(Object.hasOwn(reconnect, 'openAiApiKey'), false);
await run.runReference();
assert.equal(requests.length, 1);
assert.equal(requests[0].url, '/api/rag/reference');
const form = requests[0].body;
assert.equal(form.get('perplexityApiKey'), 'perplexity-offline-key');
assert.equal(form.getAll('perplexityApiKey').length, 1, 'Key appears only once in form data');
assert.equal(form.has('openaiApiKey'), false);
assert.equal(form.get('embeddingProvider'), 'perplexity');
assert.equal(form.get('embeddingModel'), 'pplx-embed-v1-4b');
assert.equal(form.get('embeddingDimensions'), '513');
delete providerKeys.Perplexity;
await run.runReference();
assert.equal(requests.length, 1, 'Missing Perplexity key must stop submission');
assert.match(el.ragStatus.textContent, /Perplexity API key is required/);
assert.equal(el.ragRun.disabled, true);

for (const provider of ['ollama', 'vllm']) {
  el.ragEmbeddingProvider.value = provider;
  shared.updateRunState();
  assert.equal(el.ragRun.disabled, false, `${provider} does not require a hosted API key`);
  assert.deepEqual(Object.keys(embedding.getEmbeddingCredentials()), []);
}
console.log('PASS: real HTML and RAG modules validate Perplexity models, dimensions, key gating, restoration, reconnect and upload payloads; no HTTP sent.');
