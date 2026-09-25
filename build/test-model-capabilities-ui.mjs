// Executes the actual settings module against HTML-derived controls. No network calls.
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import vm from 'node:vm';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const web = path.join(root, 'apps/Mythosia.AI.Samples.ChatUi/wwwroot');
const html = fs.readFileSync(path.join(web, 'index.html'), 'utf8');
const source = fs.readFileSync(path.join(web, 'js/settings.js'), 'utf8');
const domSource = fs.readFileSync(path.join(web, 'js/dom.js'), 'utf8');
const elements = new Map();
function create(id, tag = '') {
  const listeners = new Map(), classes = new Set(), children = [];
  let innerHTML = '', radio;
  return {
    tagName: tag.match(/^<([a-z]+)/i)?.[1]?.toUpperCase() || '', dataset: {},
    id, value: tag.match(/\bvalue="([^"]*)"/)?.[1] || '', checked: /\bchecked\b/.test(tag),
    disabled: /\bdisabled\b/.test(tag), textContent: '', title: '',
    classList: { add: name => classes.add(name), remove: name => classes.delete(name),
      contains: name => classes.has(name), toggle(name, on) { if (on ?? !classes.has(name)) classes.add(name); else classes.delete(name); } },
    addEventListener(type, action) { listeners.set(type, [...(listeners.get(type) || []), action]); },
    async fire(type) { for (const action of listeners.get(type) || []) await action(); },
    appendChild(child) { children.push(child); return child; },
    append(...items) { children.push(...items); },
    replaceChildren(...items) { children.splice(0, children.length, ...items); },
    setAttribute(name, value) { this[name] = String(value); },
    removeAttribute(name) { delete this[name]; },
    contains() { return false; },
    get options() { return children.filter(child => child.tagName === 'OPTION'); },
    get selectedOptions() { return this.options.filter(option => option.value === this.value); },
    get innerHTML() { return innerHTML; },
    set innerHTML(value) {
      innerHTML = value; children.length = 0;
      const input = value.match(/<input\b[^>]*>/)?.[0];
      radio = input ? create('', input) : null;
    },
    querySelectorAll() { return children.map(child => child.querySelector('input')).filter(Boolean); },
    querySelector(selector) {
      if (selector === 'details') return children.find(child => child.tagName === 'DETAILS') || null;
      return selector.includes(':checked') ? this.querySelectorAll().find(item => item.checked) : radio;
    }
  };
}
function node(id) {
  if (elements.has(id)) return elements.get(id);
  const tag = html.match(new RegExp(`<[^>]+\\bid="${id}"[^>]*>`))?.[0];
  assert.ok(tag, `Missing actual HTML control ${id}`);
  const value = create(id, tag);
  if (value.tagName === 'SELECT') {
    const selectHtml = html.match(new RegExp(`<select\\b[^>]*\\bid="${id}"[^>]*>([\\s\\S]*?)</select>`))?.[1] || '';
    for (const option of selectHtml.matchAll(/<option\b[^>]*>/g)) value.appendChild(create('', option[0]));
    value.value ||= value.options[0]?.value || '';
  }
  elements.set(id, value); return value;
}
const dom = {};
const modelsSource = fs.readFileSync(path.join(web, 'js/models.js'), 'utf8');
const stateSource = fs.readFileSync(path.join(web, 'js/state-panel.js'), 'utf8');
const imports = [...(source + '\n' + modelsSource + '\n' + stateSource).matchAll(/import\s*\{([^}]+)\}\s*from\s*'\.\/([^']+)'/g)];
for (const declaration of imports.filter(item => item[2] === 'dom.js'))
  for (const name of declaration[1].split(',').map(item => item.trim())) {
    const id = domSource.match(new RegExp(`export const ${name}\\s*=\\s*\\$\\('#([^']+)'\\)`))?.[1];
    assert.ok(id, `Missing actual dom.js export ${name}`); dom[name] = node(id);
  }

const app = { isConnected: false, selectedProvider: null, modelReasoningInfo: null, modelSamplingInfo: null };
const alibabaSettings = { baseUrl: 'http://localhost:11434', platform: 'Ollama' };
const providerKeys = { Alibaba: 'offline-endpoint' };
const requests = [];
const connectionControls = { reasoning: null, sampling: { temperature: false, topP: false }, maxOutputTokens: null };
let polledState;
let fetchOverride;
const document = {
  getElementById: node, createElement: tag => create('', `<${tag}>`), activeElement: null,
  querySelector: () => null
};
const context = vm.createContext({
  console, document,
  setTimeout, clearTimeout,
  fetch: async (url, options) => {
    if (fetchOverride) return fetchOverride(url, options);
    requests.push({ url, body: options ? JSON.parse(options.body) : null });
    if (url === '/api/state') return { json: async () => polledState };
    return { ok: true, json: async () => ({ provider: 'Alibaba', model: 'qwen3:8b', controls: connectionControls }) };
  }
});
const settingsModule = new vm.SourceTextModule(source, { context });
function synthetic(values) {
  return new vm.SyntheticModule(Object.keys(values), function () {
    for (const [key, value] of Object.entries(values)) this.setExport(key, value);
  }, { context });
}
await settingsModule.link(async name => synthetic(name === './dom.js' ? dom
  : name === './state.js' ? { app } : { refreshState() {} }));
await settingsModule.evaluate();
const modelsModule = new vm.SourceTextModule(modelsSource + '\nexport { onModelSelect };', { context });
await modelsModule.link(async name => {
  if (name === './settings.js') return settingsModule;
  if (name === './dom.js') return synthetic(dom);
  if (name === './state.js') return synthetic({
    app, providerKeys, alibabaSettings, enableChatInput() {}, disableChatInput() {}, autoScroll() {}
  });
  if (name === './utils.js') return synthetic({ $$: () => [], escapeHtml: String });
  if (name === './state-panel.js') return synthetic({ startStatePolling() {}, stopStatePolling() {}, refreshState() {} });
  if (name === './functions-panel.js') return synthetic({ refreshFunctions() {} });
  if (name === './apikey-modal.js') return synthetic({ openKeyModal() {} });
  if (name === './alibaba-settings.js') return synthetic({ openAlibabaSettingsModal() {} });
  if (name === './rag-rewriter-models.js') return synthetic({ populateRewriterModels() {} });
  throw new Error('Unexpected module: ' + name);
});
await modelsModule.evaluate();
// The catalogue entry describes the official cloud model. The selected connection
// is a custom deployment whose capabilities are intentionally Unknown.
modelsModule.namespace.onModelSelect('Qwen3_8B', 'Alibaba', 'qwen3-8b',
  { type: 'qwen_thinking', levels: [] }, 8192, { temperature: true, topP: true });
await new Promise(resolve => setImmediate(resolve));
assert.equal(requests[0].url, '/api/configure');
assert.equal(dom.setReasoning.disabled, true, 'Custom endpoint must replace cloud reasoning controls');
assert.equal(dom.setTemp.disabled, true, 'Connected sampling must replace catalogue sampling');
assert.equal(dom.setTopp.disabled, true);
assert.equal(app.modelReasoningInfo, null);

// A settings response must update controls without resetting the user's current
// reasoning choice or output budget when the choices themselves have not changed.
const ui = settingsModule.namespace;
app.modelReasoningInfo = { type: 'deepseek_thinking', levels: ['Auto', 'Low', 'High', 'Max'] };
app.modelSamplingInfo = { temperature: true, topP: false };
ui.updateReasoningUI();
dom.setReasoning.checked = true;
connectionControls.reasoning = app.modelReasoningInfo;
connectionControls.sampling = { temperature: false, topP: true };
app.isConnected = true;
ui.scheduleApplySettings(0);
await new Promise(resolve => setTimeout(resolve, 20));
assert.equal(dom.setTemp.disabled, true);
assert.equal(dom.setTopp.disabled, false);
assert.equal(dom.setReasoning.checked, true, 'Refreshing capabilities must retain enabled reasoning');
assert.equal(dom.setMaxTokens.value, 8192, 'Capability refresh must not overwrite the user budget');

const stateModule = new vm.SourceTextModule(stateSource, { context });
await stateModule.link(async name => {
  if (name === './settings.js') return settingsModule;
  if (name === './dom.js') return synthetic(dom);
  if (name === './state.js') return synthetic({ app });
  if (name === './utils.js') return synthetic({ escapeHtml: String, truncate: String });
  throw new Error('Unexpected state module: ' + name);
});
await stateModule.evaluate();
polledState = { configured: true, provider: 'Alibaba', modelEnum: 'Qwen3_8B',
  controls: { reasoning: null, sampling: { temperature: true, topP: false } } };
await stateModule.namespace.refreshState();
assert.equal(app.modelReasoningInfo, null, 'Polling must refresh active connection capabilities');
assert.equal(dom.setTemp.disabled, false);
assert.equal(dom.setTopp.disabled, true);
polledState.modelEnum = 'a-previously-selected-model';
polledState.controls = { reasoning: null, sampling: { temperature: false, topP: true } };
await stateModule.namespace.refreshState();
assert.equal(dom.setTemp.disabled, false, 'Stale polling must not replace current model controls');

const pending = [];
fetchOverride = (url, options) => new Promise(resolve =>
  pending.push({ url, body: options ? JSON.parse(options.body) : null, resolve }));
const reply = (operation, controls, ok = true) => operation.resolve({
  ok, json: async () => ({ provider: 'Alibaba', model: operation.body?.model,
    error: ok ? null : 'old connection failed', controls })
});
const waitTurn = () => new Promise(resolve => setTimeout(resolve, 10));
modelsModule.namespace.onModelSelect('Qwen3_4B', 'Alibaba', 'qwen3-4b', null, 8192, null);
modelsModule.namespace.onModelSelect('Qwen3_14B', 'Alibaba', 'qwen3-14b', null, 8192, null);
assert.equal(pending.length, 2);
reply(pending[1], connectionControls);
await waitTurn();
reply(pending[0], connectionControls, false);
await waitTurn();
assert.equal(app.isConnected, true, 'An obsolete failure must not disconnect the current model');
assert.equal(app.selectedModel, 'Qwen3_14B');

// Selecting the same name again is a different connection, so a name-only check is insufficient.
pending.length = 0;
modelsModule.namespace.onModelSelect('Qwen3_4B', 'Alibaba', 'qwen3-4b', null, 8192, null);
modelsModule.namespace.onModelSelect('Qwen3_14B', 'Alibaba', 'qwen3-14b', null, 8192, null);
modelsModule.namespace.onModelSelect('Qwen3_4B', 'Alibaba', 'qwen3-4b', null, 8192, null);
reply(pending[2], { reasoning: null, sampling: { temperature: true, topP: false } });
await waitTurn();
reply(pending[0], { reasoning: null, sampling: { temperature: false, topP: true } });
reply(pending[1], connectionControls, false);
await waitTurn();
assert.equal(dom.setTemp.disabled, false, 'An obsolete same-name connection must not replace controls');
assert.equal(app.isConnected, true);

// On then Off requests may finish in the reverse order.
pending.length = 0;
app.selectedProvider = 'DeepSeek';
app.modelReasoningInfo = { type: 'deepseek_thinking', levels: ['Auto', 'Low', 'High', 'Max'] };
ui.updateReasoningUI();
dom.setReasoning.checked = true;
ui.scheduleApplySettings(0);
await waitTurn();
dom.setReasoning.checked = false;
ui.scheduleApplySettings(0);
await waitTurn();
assert.equal(pending.length, 2);
reply(pending[1], { reasoning: app.modelReasoningInfo, sampling: { temperature: true, topP: false } });
await waitTurn();
reply(pending[0], { reasoning: app.modelReasoningInfo, sampling: { temperature: false, topP: true } });
await waitTurn();
assert.equal(dom.setReasoning.checked, false);
assert.equal(dom.setTemp.disabled, false, 'An obsolete settings response must not replace current controls');
assert.equal(dom.setTopp.disabled, true);

// Polling started before the latest edit must also be ignored when it finishes later.
pending.length = 0;
const oldPoll = stateModule.namespace.refreshState();
ui.scheduleApplySettings(0);
await waitTurn();
reply(pending[1], { reasoning: app.modelReasoningInfo, sampling: { temperature: true, topP: false } });
await waitTurn();
pending[0].resolve({ json: async () => ({ configured: true,
  provider: app.selectedProvider, modelEnum: app.selectedModel,
  controls: { reasoning: null, sampling: { temperature: false, topP: true } } }) });
await oldPoll;
assert.equal(dom.setTemp.disabled, false, 'An obsolete poll must not undo a newer settings response');
assert.notEqual(app.modelReasoningInfo, null);

// Opus 5.5 starts at the provider's Medium default; an unrelated settings edit
// must not silently lower its effort to the first radio option.
app.selectedProvider = 'Anthropic';
app.selectedModel = 'ClaudeOpus5_5';
app.modelReasoningInfo = { type: 'claude_always', levels: ['Low', 'Medium', 'High', 'XHigh', 'Max'], defaultLevel: 'Medium' };
app.modelSamplingInfo = { temperature: false, topP: false };
ui.updateReasoningUI();
ui.updateSamplingUI();
assert.equal(dom.setReasoning.checked, true);
assert.equal(dom.setReasoning.disabled, true);
assert.equal(dom.reasoningLvls.querySelector('input:checked').value, 'Medium');
requests.length = 0;
fetchOverride = async (url, options) => {
  requests.push({ url, body: JSON.parse(options.body) });
  return { ok: true, json: async () => ({ controls: {
    reasoning: app.modelReasoningInfo, sampling: app.modelSamplingInfo
  } }) };
};
ui.scheduleApplySettings(0);
await waitTurn();
assert.equal(requests.at(-1).body.reasoningType, 'claude_always');
assert.equal(requests.at(-1).body.reasoningEnabled, true);
assert.equal(requests.at(-1).body.reasoningLevel, 'Medium');
for (const radio of dom.reasoningLvls.querySelectorAll('input')) radio.checked = radio.value === 'High';
ui.scheduleApplySettings(0);
await waitTurn();
assert.equal(requests.at(-1).body.reasoningLevel, 'High');
assert.equal(dom.reasoningLvls.querySelector('input:checked').value, 'High');

app.modelReasoningInfo = { type: 'claude_always', levels: ['Low', 'Medium', 'High', 'XHigh', 'Max'] };
ui.updateReasoningUI();
assert.equal(dom.reasoningLvls.querySelector('input:checked').value, 'Low', 'Existing model defaults must remain unchanged');

// GPT-6 Sol/Luna expose None through the same effort controls as Astra. A
// server-confirmed effort change must refresh sampling without resetting None.
for (const model of ['Gpt6Sol', 'Gpt6Luna']) {
  app.selectedProvider = 'OpenAI';
  app.selectedModel = model;
  app.modelReasoningInfo = { type: 'gpt6', levels: ['Auto', 'None', 'Low', 'Medium', 'High', 'XHigh', 'Max'] };
  app.modelSamplingInfo = { temperature: false, topP: false };
  ui.updateReasoningUI();
  ui.updateSamplingUI();
  assert.equal(dom.setReasoning.checked, true);
  assert.equal(dom.setReasoning.disabled, true);
  assert.match(dom.reasoningLvls.innerHTML, /None disables reasoning/);
  assert.equal(dom.reasoningLvls.querySelector('input:checked').value, 'Auto');
  requests.length = 0;
  fetchOverride = async (url, options) => {
    const body = JSON.parse(options.body);
    requests.push({ url, body });
    const none = body.reasoningLevel === 'None';
    return { ok: true, json: async () => ({ controls: {
      reasoning: app.modelReasoningInfo, sampling: { temperature: none, topP: none }
    } }) };
  };
  let selected;
  for (const radio of dom.reasoningLvls.querySelectorAll('input')) {
    radio.checked = radio.value === 'None';
    if (radio.checked) selected = radio;
  }
  await selected.fire('change');
  await waitTurn();
  assert.equal(requests.at(-1).body.reasoningType, 'gpt6');
  assert.equal(requests.at(-1).body.reasoningEnabled, true);
  assert.equal(requests.at(-1).body.reasoningLevel, 'None');
  assert.equal(requests.at(-1).body.temperature, null, 'Sampling must wait for the active model capability response');
  assert.equal(dom.reasoningLvls.querySelector('input:checked').value, 'None');
  assert.equal(dom.setTemp.disabled, false);
  assert.equal(dom.setTopp.disabled, false);
  dom.setTemp.value = '0.3';
  dom.setTopp.value = '0.7';
  ui.scheduleApplySettings(0);
  await waitTurn();
  assert.equal(requests.at(-1).body.reasoningLevel, 'None', 'Unrelated settings must retain disabled reasoning');
  assert.equal(requests.at(-1).body.temperature, 0.3);
  assert.equal(requests.at(-1).body.topP, 0.7);

  for (const radio of dom.reasoningLvls.querySelectorAll('input')) {
    radio.checked = radio.value === 'High';
    if (radio.checked) selected = radio;
  }
  await selected.fire('change');
  await waitTurn();
  assert.equal(requests.at(-1).body.reasoningLevel, 'High');
  assert.equal(dom.setTemp.disabled, true);
  assert.equal(dom.setTopp.disabled, true);
  assert.equal(dom.reasoningLvls.querySelector('input:checked').value, 'High');
}
app.selectedModel = 'Gpt6Astra';
app.modelReasoningInfo = { type: 'gpt6', levels: ['Auto', 'Low', 'Medium', 'High', 'XHigh', 'Max'] };
ui.updateReasoningUI();
assert.match(dom.reasoningLvls.innerHTML, /Always on/);
assert.equal(dom.reasoningLvls.querySelectorAll('input').some(radio => radio.value === 'None'), false,
  'Sol/Luna controls must not leak a None option to Astra');

// Speed is opt-in, model/endpoint-derived, and round-trips through the real settings handler.
const speedControls = {
  reasoning: app.modelReasoningInfo, sampling: { temperature: false, topP: false },
  speed: { selected: 'ProviderDefault', standard: 'Supported', fast: 'Supported' },
  capabilities: { streaming: 'Supported', functionCalling: 'Supported', steering: 'Unknown' }
};
ui.updateModelControls(speedControls);
const speedSelect = node('set-speed');
assert.equal(speedSelect.value, 'ProviderDefault');
assert.match(node('speed-help').textContent, /No speed override/);
assert.equal(speedSelect.options.find(option => option.value === 'Fast').disabled, false);
const capabilityDetails = node('model-capabilities').querySelector('details');
capabilityDetails.open = true;
ui.updateModelControls(speedControls);
assert.equal(node('model-capabilities').querySelector('details'), capabilityDetails,
  'Polling identical capabilities must preserve the disclosure and focus');
assert.equal(capabilityDetails.open, true);
ui.initSettings();
fetchOverride = async (url, options) => {
  const body = JSON.parse(options.body);
  requests.push({ url, body });
  return { ok: true, json: async () => ({ controls: { ...speedControls,
    speed: { ...speedControls.speed, selected: body.speed } } }) };
};
speedSelect.value = 'Fast';
await speedSelect.fire('change');
await waitTurn();
assert.equal(requests.at(-1).body.speed, 'Fast');
assert.equal(speedSelect.value, 'Fast');
assert.match(node('speed-help').textContent, /cost more/);
ui.updateModelControls({ ...speedControls, speed: { selected: 'ProviderDefault', standard: 'Supported', fast: 'Unsupported' } });
assert.equal(speedSelect.value, 'ProviderDefault');
assert.equal(speedSelect.options.find(option => option.value === 'Fast').disabled, true);
assert.match(speedSelect.options.find(option => option.value === 'Fast').textContent, /unavailable/);
ui.updateModelControls({ ...speedControls, speed: { selected: 'ProviderDefault', standard: 'Unknown', fast: 'Unknown' } });
assert.equal(speedSelect.options.find(option => option.value === 'Standard').disabled, true);
assert.match(speedSelect.options.find(option => option.value === 'Fast').textContent, /not verified/);
console.log('Connection capability UI regression checks passed, including reversed responses and speed controls.');
