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
    id, value: tag.match(/\bvalue="([^"]*)"/)?.[1] || '', checked: /\bchecked\b/.test(tag),
    disabled: /\bdisabled\b/.test(tag), textContent: '', title: '',
    classList: { add: name => classes.add(name), remove: name => classes.delete(name),
      contains: name => classes.has(name), toggle(name, on) { if (on ?? !classes.has(name)) classes.add(name); else classes.delete(name); } },
    addEventListener(type, action) { listeners.set(type, [...(listeners.get(type) || []), action]); },
    async fire(type) { for (const action of listeners.get(type) || []) await action(); },
    appendChild(child) { children.push(child); }, contains() { return false; },
    get innerHTML() { return innerHTML; },
    set innerHTML(value) {
      innerHTML = value; children.length = 0;
      const input = value.match(/<input\b[^>]*>/)?.[0];
      radio = input ? create('', input) : null;
    },
    querySelectorAll() { return children.map(child => child.querySelector('input')).filter(Boolean); },
    querySelector(selector) { return selector.includes(':checked') ? this.querySelectorAll().find(item => item.checked) : radio; }
  };
}
function node(id) {
  if (elements.has(id)) return elements.get(id);
  const tag = html.match(new RegExp(`<[^>]+\\bid="${id}"[^>]*>`))?.[0];
  assert.ok(tag, `Missing actual HTML control ${id}`);
  const value = create(id, tag); elements.set(id, value); return value;
}
const dom = {};
const imports = [...source.matchAll(/import\s*\{([^}]+)\}\s*from\s*'\.\/([^']+)'/g)];
for (const declaration of imports.filter(item => item[2] === 'dom.js'))
  for (const name of declaration[1].split(',').map(item => item.trim())) {
    const id = domSource.match(new RegExp(`export const ${name}\\s*=\\s*\\$\\('#([^']+)'\\)`))?.[1];
    assert.ok(id, `Missing actual dom.js export ${name}`); dom[name] = node(id);
  }
const app = { isConnected: true, selectedProvider: 'Perplexity',
  modelReasoningInfo: { type: 'perplexity', levels: ['Auto', 'Minimal', 'Low', 'Medium', 'High', 'XHigh', 'Max'] } };
const requests = [], timers = new Map();
let sequence = 0, failRequest = false;
const context = vm.createContext({
  console, document: { getElementById: node, createElement: () => create(''), activeElement: null },
  setTimeout(action) { timers.set(++sequence, action); return sequence; }, clearTimeout(id) { timers.delete(id); },
  fetch: async (url, options) => { requests.push({ url, body: JSON.parse(options.body) }); return {
    ok: !failRequest, json: async () => failRequest ? { error: 'Unsupported effort for this model.' } : { ok: true }
  }; }
});
const module = new vm.SourceTextModule(source, { context });
await module.link(async name => {
  const values = name === './dom.js' ? dom : name === './state.js' ? { app } : { refreshState() {} };
  return new vm.SyntheticModule(Object.keys(values), function () { for (const [key, value] of Object.entries(values)) this.setExport(key, value); }, { context });
});
await module.evaluate();
const ui = module.namespace;
async function flush() { for (const [id, action] of [...timers]) { timers.delete(id); await action(); } }
ui.initSettings(); ui.updateReasoningUI();
assert.equal(node('perplexity-options').classList.contains('hidden'), false);
assert.equal(dom.setReasoning.checked, false);
assert.deepEqual(dom.reasoningLvls.querySelectorAll().map(item => item.value), app.modelReasoningInfo.levels);
ui.updatePerplexitySettings({ reasoning: { type: 'perplexity', preset: 'WideResearch', maxSteps: 9, webSearch: true, enabled: true, effort: 'High' } });
assert.equal(node('set-perplexity-preset').value, 'WideResearch');
assert.equal(node('set-perplexity-search').disabled, true);
assert.equal(dom.reasoningLvls.querySelector('input:checked').value, 'High');
ui.scheduleApplySettings(0); await flush();
assert.deepEqual(Object.fromEntries(Object.entries(requests.at(-1).body).filter(([name]) => name.startsWith('perplexity') || name.startsWith('reasoning'))), {
  reasoningEnabled: true, reasoningLevel: 'High', reasoningType: 'perplexity',
  perplexityPreset: 'WideResearch', perplexityMaxSteps: 9, perplexityWebSearch: true
});
node('set-perplexity-preset').value = 'Model'; await node('set-perplexity-preset').fire('change');
assert.equal(node('set-perplexity-search').disabled, false);
node('set-perplexity-search').checked = false; await node('set-perplexity-search').fire('change');
dom.setReasoning.checked = false; await dom.setReasoning.fire('change'); await flush();
assert.equal(requests.at(-1).body.perplexityWebSearch, false);
assert.equal(requests.at(-1).body.reasoningEnabled, false);
failRequest = true; ui.scheduleApplySettings(0); await flush();
assert.equal(node('perplexity-settings-error').textContent, 'Unsupported effort for this model.');
// A locally unknown reasoning contract must not hide independent Agent controls or
// offer a guessed union of model effort levels.
app.modelReasoningInfo = null; ui.updateReasoningUI();
assert.equal(node('perplexity-options').classList.contains('hidden'), false);
assert.equal(dom.setReasoning.checked, false);
ui.scheduleApplySettings(0); await flush();
assert.equal(requests.at(-1).body.reasoningEnabled, null);
assert.equal(requests.at(-1).body.reasoningType, null);
assert.equal('perplexityPreset' in requests.at(-1).body, true);
app.selectedProvider = 'OpenAI'; app.modelReasoningInfo = null; ui.updateReasoningUI();
assert.equal(node('perplexity-options').classList.contains('hidden'), true);
ui.scheduleApplySettings(0); await flush();
assert.equal('perplexityPreset' in requests.at(-1).body, false);
console.log('Perplexity Agent UI smoke passed: controls, restore, wire values, search gating, error display and provider isolation.');
