// Execute the actual chat/state modules with a controlled transport. No external API calls.
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import vm from 'node:vm';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const web = path.join(root, 'apps/Mythosia.AI.Samples.ChatUi/wwwroot');
const domSource = fs.readFileSync(path.join(web, 'js/dom.js'), 'utf8');
const html = fs.readFileSync(path.join(web, 'index.html'), 'utf8');

class Element {
  constructor(tag = 'div') {
    this.tagName = tag;
    this.children = [];
    this.listeners = new Map();
    this.attributes = {};
    this.style = {};
    this.value = '';
    this.disabled = false;
    this.className = '';
    this.scrollHeight = this.clientHeight = this.scrollTop = 0;
    this.classList = {
      contains: name => this.className.split(/\s+/).includes(name),
      add: name => { if (!this.classList.contains(name)) this.className += ' ' + name; },
      remove: name => { this.className = this.className.split(/\s+/).filter(value => value !== name).join(' '); },
      toggle: (name, on) => {
        const enabled = on ?? !this.classList.contains(name);
        this.classList[enabled ? 'add' : 'remove'](name);
        return enabled;
      }
    };
  }
  set innerHTML(value) {
    this.markup = value;
    this.children = [];
    for (const match of value.matchAll(/<([a-z]+)\b[^>]*class="([^"]+)"[^>]*>/g)) {
      const child = new Element(match[1]);
      child.className = match[2];
      this.appendChild(child);
    }
  }
  get innerHTML() { return this.markup || ''; }
  set textContent(value) { this.text = String(value); this.markup = ''; this.children = []; }
  get textContent() { return (this.text || this.markup || '') + this.children.map(child => child.textContent).join(''); }
  appendChild(child) { child.parentNode = this; this.children.push(child); return child; }
  removeChild(child) { this.children = this.children.filter(value => value !== child); child.parentNode = null; }
  remove() { this.parentNode?.removeChild(this); }
  hasChildNodes() { return this.children.length > 0; }
  setAttribute(name, value) { this.attributes[name] = value; }
  focus() { this.focused = true; }
  querySelectorAll(selector) {
    const matches = child => selector.startsWith('.') ? child.classList.contains(selector.slice(1)) : child.tagName === selector;
    return this.children.flatMap(child => [...(matches(child) ? [child] : []), ...child.querySelectorAll(selector)]);
  }
  querySelector(selector) { return this.querySelectorAll(selector)[0] || null; }
  addEventListener(type, callback) {
    this.listeners.set(type, [...(this.listeners.get(type) || []), callback]);
  }
  async fire(type, extra = {}) {
    for (const callback of this.listeners.get(type) || []) await callback({ preventDefault() {}, ...extra });
  }
}

const dom = {};
for (const name of ['chatMessages', 'chatForm', 'chatInput', 'btnSend', 'btnStop', 'btnClear', 'sidebarLeft']) {
  const id = domSource.match(new RegExp(`export const ${name}\\s*=\\s*\\$\\('#([^']+)'\\)`))?.[1];
  assert.ok(id, `Missing DOM export ${name}`);
  assert.ok(html.includes(`id="${id}"`), `Missing HTML control ${id}`);
  dom[name] = new Element();
}
dom.btnStop.className = 'hidden';
const requests = [];
let transport;
let refreshCount = 0;
const context = vm.createContext({
  console, AbortController, DOMException, TextDecoder, URL,
  document: { createElement: tag => new Element(tag) },
  fetch: (url, options) => {
    requests.push({ url, options });
    return transport(url, options);
  }
});
function synthetic(exports) {
  return new vm.SyntheticModule(Object.keys(exports), function () {
    for (const [name, value] of Object.entries(exports)) this.setExport(name, value);
  }, { context });
}
const state = new vm.SourceTextModule(fs.readFileSync(path.join(web, 'js/state.js'), 'utf8'), { context });
await state.link(() => synthetic(dom));
await state.evaluate();
const { app } = state.namespace;
const chat = new vm.SourceTextModule(fs.readFileSync(path.join(web, 'js/chat.js'), 'utf8'), { context });
await chat.link(name => {
  if (name === './state.js') return state;
  if (name === './dom.js') return synthetic(dom);
  if (name === './utils.js') return synthetic({ escapeHtml: String, renderMarkdown: String, addCopyButtons() {} });
  if (name === './state-panel.js') return synthetic({ refreshState() { refreshCount++; } });
  if (name === './code-modal.js') return synthetic({ addViewCodeButton() {} });
  if (name === './rag-pipeline.js') return synthetic({ getPipelineSettingsForRequest: () => ({}) });
  if (name === './rag-vector-store.js') return synthetic({ getVectorStoreConfigForRequest: () => ({}) });
  throw new Error('Unexpected import ' + name);
});
await chat.evaluate();
chat.namespace.initChat();
app.isConnected = true;
const turn = () => new Promise(resolve => setImmediate(resolve));
const start = message => { dom.chatInput.value = message; return dom.chatForm.fire('submit'); };
const visibleStop = () => !dom.btnStop.classList.contains('hidden');
const encoded = value => new TextEncoder().encode(value);
const response = read => ({ ok: true, headers: { get: () => 'text/event-stream' }, body: { getReader: () => ({ read }) } });

// Abort before response headers: cancel the actual transport and keep a clear status.
transport = (url, options) => new Promise((resolve, reject) => {
  options.signal.addEventListener('abort', () => reject(new DOMException('aborted', 'AbortError')), { once: true });
});
const waiting = start('Cancel this request');
assert.equal(app.isSending, true);
assert.equal(dom.sidebarLeft.inert, true, 'Keyboard interaction must also be blocked during the request');
assert.equal(dom.btnSend.disabled, true);
assert.equal(visibleStop(), true);
await dom.btnClear.fire('click');
await start('Do not start a second concurrent request');
assert.equal(requests.length, 1);
await dom.btnStop.fire('click');
await waiting;
assert.equal(requests[0].options.signal.aborted, true);
assert.equal(app.isSending, false);
assert.equal(dom.sidebarLeft.inert, false);
assert.equal(visibleStop(), false);
assert.equal(dom.btnSend.disabled, false);
assert.ok(dom.chatMessages.textContent.includes('Response stopped.'));
assert.ok(!dom.chatMessages.textContent.includes('Network error:'));

// A buffered chunk arriving after cancellation must not alter the partial answer.
let reads = 0;
transport = async (url, options) => response(() => {
  if (++reads === 1) return Promise.resolve({ done: false, value: encoded('data: {"type":"text","content":"Partial answer"}\n\n') });
  return new Promise(resolve => options.signal.addEventListener('abort', () => resolve({
    done: false, value: encoded('data: {"type":"text","content":"MUST NOT APPEAR"}\n\n')
  }), { once: true }));
});
const streaming = start('Stream a response');
await turn();
assert.ok(dom.chatMessages.textContent.includes('Partial answer'));
await dom.btnStop.fire('click');
await streaming;
assert.ok(dom.chatMessages.textContent.includes('Partial answer'));
assert.ok(!dom.chatMessages.textContent.includes('MUST NOT APPEAR'));
assert.equal(dom.chatMessages.querySelectorAll('.chat-notice-cancelled').length, 2);

// A new request works after stopping; older servers' final error frames are visible,
// including when their final frame has no trailing newline.
reads = 0;
transport = async () => response(async () => ++reads === 1
  ? { done: false, value: encoded('data: {"error":"Provider rejected the request"}') }
  : { done: true });
await start('Show the server error');
assert.ok(dom.chatMessages.textContent.includes('Error: Provider rejected the request'));
assert.ok(!dom.chatMessages.textContent.includes('(empty response)'));
assert.equal(app.isSending, false);

// Typed errors and successful streams remain supported.
for (const event of [{ type: 'error', content: 'Typed error' }, { type: 'text', content: 'Successful answer' }]) {
  reads = 0;
  transport = async () => response(async () => ++reads === 1
    ? { done: false, value: encoded(`data: ${JSON.stringify(event)}\n\n`) } : { done: true });
  await start('Next request');
  assert.ok(dom.chatMessages.textContent.includes(event.content));
}

// Multiple agent rounds must preserve text → tool → text (or error) arrival order.
for (const finalEvent of [{ type: 'text', content: 'Final answer' }, { type: 'error', content: 'Round failed' }, null]) {
  reads = 0;
  const events = [
    { type: 'text', content: 'Before the tool' },
    { type: 'function_call', name: 'weather', arguments: '{}' },
    { type: 'function_result', name: 'weather', content: 'Sunny' },
    ...(finalEvent ? [finalEvent] : [])
  ];
  transport = async () => response(async () => ++reads === 1
    ? { done: false, value: encoded(events.map(event => `data: ${JSON.stringify(event)}\n\n`).join('')) }
    : { done: true });
  await start('Use the weather tool');
  const responseContainer = dom.chatMessages.querySelectorAll('.response-container').at(-1);
  const children = responseContainer.children;
  assert.equal(children.length, finalEvent ? 3 : 2);
  assert.equal(children[0].querySelector('.msg-content').innerHTML, 'Before the tool');
  assert.ok(children[1].classList.contains('fc-card'));
  if (finalEvent) assert.ok(children[2].querySelector('.msg-content').innerHTML.includes(finalEvent.content));
  assert.ok(!responseContainer.textContent.includes('(empty response)'));
}

transport = async () => { throw new Error('connection lost'); };
await start('Network failure');
assert.ok(dom.chatMessages.textContent.includes('Network error: connection lost'));

// Disconnecting while awaiting a response must not re-enable Send in finally.
transport = (url, options) => new Promise((resolve, reject) => options.signal.addEventListener('abort',
  () => reject(new DOMException('aborted', 'AbortError')), { once: true }));
const disconnecting = start('Disconnect');
app.isConnected = false;
await dom.btnStop.fire('click');
await disconnecting;
assert.equal(dom.btnSend.disabled, true);
assert.equal(dom.btnClear.disabled, true);

// Enter that commits a Korean/Japanese IME composition must not send a prompt.
app.isConnected = true;
const beforeComposition = requests.length;
dom.chatInput.value = '입력 중';
await dom.chatInput.fire('keydown', { key: 'Enter', isComposing: true });
await dom.chatInput.fire('keydown', { key: 'Enter', keyCode: 229 });
assert.equal(requests.length, beforeComposition);
assert.ok(refreshCount > 0);
console.log('PASS: chat cancellation, partial output, busy guards, streamed errors, agent event order, recovery, IME input');
