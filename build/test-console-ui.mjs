// Actual HTML and console/model modules in jsdom. Transport is entirely local.
// jsdom does not implement layout, media queries, or inert; the fixture supplies
// those browser primitives. CSS appearance and viewport layout need browser QA.
import assert from 'node:assert/strict';
import fs from 'node:fs';
import { createRequire } from 'node:module';
import path from 'node:path';
import vm from 'node:vm';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const require = createRequire(path.join(root, 'build/ui-tests/package.json'));
const { JSDOM } = require('jsdom');
const web = path.join(root, 'apps/Mythosia.AI.Samples.ChatUi/wwwroot');
const html = fs.readFileSync(path.join(web, 'index.html'), 'utf8');
const turn = () => new Promise(resolve => setImmediate(resolve));
const catalogue = [
  { provider: 'OpenAI', models: [
    { name: 'GPT6Sol', description: 'GPT-6 Sol', maxOutputTokens: 8192 },
    { name: 'GPT6Luna', description: 'GPT-6 Luna', maxOutputTokens: 4096 },
    ...Array.from({ length: 21 }, (_, index) => ({ name: `OpenAiFixture${index}`, description: `OpenAI fixture model ${index}` }))
  ] },
  { provider: 'Anthropic', models: [{ name: 'ClaudeOpus55', description: 'Claude Opus 5.5' }] },
  { provider: 'Google', models: [{ name: 'Gemini38Flash', description: 'Gemini 3.8 Flash' }] },
  { provider: 'xAI', models: [{ name: 'Grok47', description: 'Grok 4.7' }] },
  { provider: 'DeepSeek', models: [{ name: 'DeepSeekFlash', description: 'DeepSeek Flash' }] },
  { provider: 'Perplexity', models: [{ name: 'Sonar', description: 'Sonar' }] },
  { provider: 'Alibaba', models: [{ name: 'QwenCustom', description: 'Qwen <custom endpoint>' }] }
];

async function fixture(width = 1440) {
  const dom = new JSDOM(html, { url: 'https://chat-ui.test/', runScripts: 'outside-only' });
  const { window } = dom;
  const { document } = window;
  let viewport = width;
  const media = new Map();
  window.matchMedia = query => {
    if (!media.has(query)) {
      const limit = Number(query.match(/max-width:\s*(\d+)px/)?.[1]);
      const listeners = new Set();
      media.set(query, {
        media: query, matches: viewport <= limit, limit,
        addEventListener(type, listener) { assert.equal(type, 'change'); listeners.add(listener); },
        change() { for (const listener of listeners) listener({ matches: this.matches, media: query }); }
      });
    }
    return media.get(query);
  };
  Object.defineProperty(window.HTMLElement.prototype, 'inert', {
    configurable: true,
    get() { return this.hasAttribute('inert'); },
    set(value) { this.toggleAttribute('inert', Boolean(value)); }
  });
  window.HTMLElement.prototype.getClientRects = function () {
    if (!this.isConnected) return [];
    for (let element = this; element; element = element.parentElement) {
      if (element.hidden || element.classList.contains('hidden') || element.style.display === 'none') return [];
      if (element.matches('.provider-models:not(.open), .rag-slide-panel:not(.open)')) return [];
      if (element.id === 'sidebar-left' && viewport <= 860 && !element.classList.contains('drawer-open')) return [];
      if (element.id === 'sidebar-right' && viewport <= 1240 && !element.classList.contains('drawer-open')) return [];
    }
    return [{ x: 0, y: 0, width: 100, height: 20 }];
  };
  const nativeFocus = window.HTMLElement.prototype.focus;
  window.HTMLElement.prototype.focus = function (...args) {
    if (!this.closest('[inert]') && this.getClientRects().length) nativeFocus.apply(this, args);
  };
  const requests = [];
  let modelReply = { ok: true, body: catalogue };
  let metadataReply = { ok: true, body: { packages: [
    { name: 'Mythosia.AI', version: '8.1.0' }, { name: 'Mythosia.AI.Rag', version: '8.1.1' }
  ] } };
  window.fetch = async (url, options) => {
    requests.push({ url, body: options?.body ? JSON.parse(options.body) : null });
    const reply = url === '/api/models' ? modelReply : url === '/api/testbed' ? metadataReply : null;
    if (reply instanceof Error) throw reply;
    if (reply) return { ok: reply.ok, json: async () => reply.body };
    if (url === '/api/configure') return { ok: true, json: async () => ({
      provider: 'OpenAI', model: 'GPT-6 Sol', controls: { maxOutputTokens: 8192, currentMaxTokens: 2048 }
    }) };
    throw new Error('Unexpected request: ' + url);
  };
  const context = dom.getInternalVMContext();
  const modules = new Map();
  function synthetic(exports) {
    return new vm.SyntheticModule(Object.keys(exports), function () {
      for (const [key, value] of Object.entries(exports)) this.setExport(key, value);
    }, { context });
  }
  let alibabaOpened = 0;
  const mocks = {
    'alibaba-settings.js': { openAlibabaSettingsModal() { alibabaOpened++; } },
    'settings.js': { updateReasoningUI() {}, updateSamplingUI() {}, updateModelControls() {} },
    'state-panel.js': { startStatePolling() {}, stopStatePolling() {}, refreshState() {} },
    'functions-panel.js': { refreshFunctions() {} },
    'rag-rewriter-models.js': { populateRewriterModels() {} }
  };
  async function load(name) {
    if (modules.has(name)) return modules.get(name);
    const module = mocks[name] ? synthetic(mocks[name]) : new vm.SourceTextModule(
      fs.readFileSync(path.join(web, 'js', name), 'utf8'), { context, identifier: name });
    modules.set(name, module);
    await module.link(specifier => load(specifier.replace(/^\.\//, '')));
    return module;
  }
  // Evaluate dependencies before models.js imports the same modules concurrently.
  for (const name of ['utils.js', 'dom.js', 'state.js', 'apikey-modal.js', 'models.js', 'console.js']) {
    const module = await load(name);
    if (module.status !== 'evaluated') await module.evaluate();
  }
  const state = modules.get('state.js').namespace;
  const models = modules.get('models.js').namespace;
  const consoleUi = modules.get('console.js').namespace;
  modules.get('apikey-modal.js').namespace.initApiKeyModal(models.refreshProviderGroup, models.deselectModel);
  // Existing feature modules own these close actions. Exercise the console's
  // delegation to those actions without loading network-heavy RAG diagnostics.
  document.getElementById('code-modal-close').addEventListener('click', () => document.getElementById('code-modal').classList.add('hidden'));
  document.getElementById('rag-settings-close').addEventListener('click', () => document.getElementById('rag-settings-modal').classList.remove('open'));
  consoleUi.initConsole();
  await turn();
  const get = id => document.getElementById(id);
  return {
    dom, window, document, get, state, models, consoleUi, requests,
    setModels(reply) { modelReply = reply; },
    setMetadata(reply) { metadataReply = reply; },
    get alibabaOpened() { return alibabaOpened; },
    key(key, properties = {}) {
      const event = new window.KeyboardEvent('keydown', { key, bubbles: true, cancelable: true, ...properties });
      document.activeElement.dispatchEvent(event);
      return event;
    },
    resize(newWidth) {
      viewport = newWidth;
      const changed = [...media.values()].filter(item => item.matches !== (viewport <= item.limit));
      for (const item of changed) item.matches = viewport <= item.limit;
      for (const item of changed) item.change();
    },
    close() { window.close(); }
  };
}

const desktop = await fixture();
try {
  const { get, document, window, models, state } = desktop;
  await models.loadModels();
  const groups = [...get('model-list').querySelectorAll('.provider-group')];
  assert.equal(groups.length, 7);
  assert.equal(get('model-count').textContent, '7 providers · 29 models');
  assert.ok(groups.every(group => !group.hidden), 'Every provider must be visible without a key');
  assert.ok(groups.every(group => group.querySelector('.provider-toggle').getAttribute('aria-expanded') === 'false'),
    'Even a large OpenAI catalogue must start collapsed so other providers remain visible');
  assert.ok(groups.every(group => !group.querySelector('.provider-models').classList.contains('open')));
  assert.equal(get('model-search-clear').hidden, true);
  assert.equal(document.querySelector('[data-model="QwenCustom"]').textContent, 'Qwen <custom endpoint>');

  // The real search input handler opens matches, hides nonmatches, and restores
  // pre-search expansion when its value is cleared.
  const search = value => { get('model-search').value = value; get('model-search').dispatchEvent(new window.Event('input')); };
  search(' CLAUDE ');
  assert.equal(get('model-search-clear').hidden, false);
  assert.equal(groups[0].hidden, true);
  assert.equal(groups[1].hidden, false);
  assert.equal(groups[1].querySelector('.provider-toggle').getAttribute('aria-expanded'), 'true');
  assert.equal(get('model-count').textContent, '1 matching models');
  search('nothing-matches-this');
  assert.ok(groups.every(group => group.hidden));
  assert.equal(get('model-search-empty').classList.contains('hidden'), false);
  get('model-search-clear').click();
  assert.equal(get('model-search').value, '');
  assert.equal(document.activeElement, get('model-search'));
  assert.equal(get('model-search-clear').hidden, true);
  assert.ok(groups.every(group => !group.hidden));
  assert.ok(groups.every(group => !group.querySelector('.provider-models').classList.contains('open')),
    'Clearing a search must restore the collapsed provider overview');
  assert.equal(get('model-search-empty').classList.contains('hidden'), true);
  search('gpt6sol');
  assert.equal(get('model-count').textContent, '1 matching models');
  search('');

  const googleToggle = groups.find(group => group.dataset.provider === 'Google').querySelector('.provider-toggle');
  googleToggle.click();
  search('deepseek');
  assert.equal(groups.find(group => group.dataset.provider === 'DeepSeek').querySelector('.provider-toggle').getAttribute('aria-expanded'), 'true');
  get('model-search-clear').click();
  assert.equal(googleToggle.getAttribute('aria-expanded'), 'true', 'Clear restores a manually opened provider');
  assert.equal(groups.find(group => group.dataset.provider === 'DeepSeek').querySelector('.provider-toggle').getAttribute('aria-expanded'), 'false');
  googleToggle.click();

  // Keys gate connecting, not browsing. Selecting a model without a key opens
  // the key dialog without issuing a configure/API request.
  const anthropicToggle = groups[1].querySelector('.provider-toggle');
  anthropicToggle.click();
  assert.equal(anthropicToggle.getAttribute('aria-expanded'), 'true');
  const anthropicModel = groups[1].querySelector('.model-item');
  anthropicModel.focus();
  anthropicModel.click();
  await turn();
  assert.equal(get('apikey-modal').classList.contains('hidden'), false);
  assert.equal(state.app.modalTargetProvider, 'Anthropic');
  assert.equal(desktop.requests.some(request => request.url === '/api/configure'), false);
  assert.equal(get('apikey-modal').getAttribute('role'), 'dialog');
  assert.equal(get('apikey-modal').getAttribute('aria-modal'), 'true');
  assert.equal(document.querySelector('.app').inert, true);
  assert.equal(document.querySelector('.workspace-header').inert, true);
  desktop.key('Escape');
  await turn();
  assert.equal(get('apikey-modal').classList.contains('hidden'), true);
  assert.equal(document.activeElement, anthropicModel, 'Closing a desktop modal restores its initiating control');
  assert.equal(document.querySelector('.app').inert, false);
  groups.find(group => group.dataset.provider === 'Alibaba').querySelector('.provider-key-btn').click();
  assert.equal(desktop.alibabaOpened, 1, 'Alibaba uses endpoint settings instead of an OpenAI-style key dialog');

  // A failed catalogue has an actionable retry, and keeps the current search
  // when that retry succeeds. Invalid payloads use the same recovery path.
  for (const reply of [{ ok: false, body: {} }, { ok: true, body: {} }, new Error('offline')]) {
    desktop.setModels(reply);
    await models.loadModels();
    assert.equal(get('model-count').textContent, 'Connection unavailable');
    assert.ok(get('model-list').querySelector('[role="alert"]'));
    get('model-search').value = 'luna';
    desktop.setModels({ ok: true, body: catalogue });
    get('retry-models').click();
    await turn();
    assert.equal(get('retry-models'), null);
    assert.equal(get('model-count').textContent, '1 matching models');
    assert.equal(document.querySelector('[data-model="GPT6Luna"]').hidden, false);
  }
  models.filterModels('');
  state.providerKeys.OpenAI = 'test-only-key';
  models.refreshProviderGroup('OpenAI');
  const openAiGroup = document.querySelector('[data-provider="OpenAI"].provider-group');
  assert.equal(openAiGroup.classList.contains('disabled'), false);
  assert.equal(openAiGroup.querySelector('.provider-key-btn').classList.contains('has-key'), true);
  const model = document.querySelector('[data-model="GPT6Sol"]');
  model.click();
  await turn();
  assert.equal(model.getAttribute('aria-pressed'), 'true');
  assert.equal(state.app.isConnected, true);
  assert.equal(get('set-maxtokens').value, '2048', 'Initial budget uses the server setting, not the model maximum');
  assert.equal(get('chat-input').disabled, false);
  assert.equal(get('chat-status').classList.contains('connected'), true);
  state.app.isSending = true;
  document.querySelector('[data-model="GPT6Luna"]').click();
  assert.equal(state.app.selectedModel, 'GPT6Sol', 'A running request must not change models');
  state.app.isSending = false;

  assert.equal(get('workspace-version').textContent, 'Core 8.1.0');
  assert.ok(get('workspace-version').title.includes('Mythosia.AI.Rag 8.1.1'));
  for (const reply of [{ ok: false, body: {} }, { ok: true, body: { packages: [] } }, { ok: true, body: null }, new Error('offline')]) {
    desktop.setMetadata(reply);
    await desktop.consoleUi.loadWorkspaceVersions();
    assert.equal(get('workspace-version').textContent, 'Local development');
  }
  assert.equal(document.querySelector('label[for="set-system"]').htmlFor, 'set-system');
  assert.equal(get('modal-close').getAttribute('aria-label'), 'Close');
} finally {
  desktop.close();
}

const mobile = await fixture(390);
try {
  const { get, document } = mobile;
  await mobile.models.loadModels();
  const left = get('sidebar-left'), right = get('sidebar-right');
  const modelToggle = get('toggle-models'), inspectorToggle = get('toggle-inspector');
  assert.equal(left.inert, true);
  assert.equal(left.getAttribute('aria-hidden'), 'true');
  assert.equal(right.inert, true);
  modelToggle.focus();
  modelToggle.click();
  assert.equal(left.classList.contains('drawer-open'), true);
  assert.equal(left.getAttribute('aria-hidden'), 'false');
  assert.equal(left.inert, false);
  assert.equal(document.querySelector('.chat-area').inert, true);
  assert.equal(modelToggle.getAttribute('aria-expanded'), 'true');
  assert.equal(document.activeElement, get('model-search'));
  const focusable = [...left.querySelectorAll('button:not(:disabled), input:not(:disabled), select:not(:disabled), textarea:not(:disabled), a[href], [tabindex="0"]')]
    .filter(element => !element.closest('[inert]') && element.getClientRects().length);
  focusable.at(-1).focus();
  assert.equal(mobile.key('Tab').defaultPrevented, true);
  assert.equal(document.activeElement, focusable[0]);
  assert.equal(mobile.key('Tab', { shiftKey: true }).defaultPrevented, true);
  assert.equal(document.activeElement, focusable.at(-1));
  mobile.key('Escape');
  assert.equal(left.classList.contains('drawer-open'), false);
  assert.equal(left.inert, true);
  assert.equal(modelToggle.getAttribute('aria-expanded'), 'false');
  assert.equal(document.activeElement, modelToggle);
  assert.equal(document.querySelector('.chat-area').inert, false);

  mobile.key('k', { ctrlKey: true });
  assert.equal(left.classList.contains('drawer-open'), true);
  get('workspace-backdrop').click();
  assert.equal(left.classList.contains('drawer-open'), false);
  assert.equal(document.activeElement, modelToggle);
  inspectorToggle.focus();
  inspectorToggle.click();
  assert.equal(right.classList.contains('drawer-open'), true);
  assert.equal(right.getAttribute('aria-hidden'), 'false');
  assert.equal(left.inert, true);
  assert.ok(right.contains(document.activeElement));
  mobile.key('Escape');
  assert.equal(document.activeElement, inspectorToggle);
  assert.equal(right.inert, true);

  // A modal opened from the model drawer closes that drawer. Closing the modal
  // returns to the visible Models button, not a now-hidden model row.
  modelToggle.click();
  left.querySelector('.provider-toggle').click();
  const model = left.querySelector('.model-item');
  model.focus();
  model.click();
  await turn();
  const keyDialog = get('apikey-modal');
  assert.equal(left.classList.contains('drawer-open'), false);
  assert.equal(left.inert, true);
  assert.equal(keyDialog.inert, false);
  assert.ok(keyDialog.contains(document.activeElement));
  const input = get('modal-apikey-input');
  input.focus();
  get('code-modal').classList.remove('hidden');
  await turn();
  assert.equal(keyDialog.inert, true, 'Only the top modal accepts focus');
  assert.equal(keyDialog.getAttribute('aria-modal'), 'false');
  assert.equal(get('code-modal').getAttribute('aria-modal'), 'true');
  mobile.key('Escape');
  await turn();
  assert.equal(keyDialog.inert, false);
  assert.equal(document.activeElement, input, 'Closing a nested modal returns focus to its parent');
  mobile.key('Escape');
  await turn();
  assert.equal(document.activeElement, modelToggle);
  assert.equal(document.querySelector('.app').inert, false);
  assert.equal(document.querySelector('.workspace-header').inert, false);

  modelToggle.click();
  mobile.resize(1440);
  assert.equal(left.classList.contains('drawer-open'), false);
  assert.equal(left.inert, false);
  assert.equal(right.inert, false);
  assert.equal(left.getAttribute('aria-hidden'), 'false');
  assert.equal(right.getAttribute('aria-hidden'), 'false');
  mobile.resize(1000);
  assert.equal(left.inert, false, 'The compact layout keeps model settings on screen');
  assert.equal(right.inert, true, 'The compact inspector remains behind its toggle');

  get('rag-chat-status').classList.add('active');
  await turn();
  assert.equal(get('document-status-dot').classList.contains('indexed'), true);
} finally {
  mobile.close();
}
console.log('PASS: model search/browsing/retry, key prompts, metadata fallback, drawers and modal focus/ARIA');
