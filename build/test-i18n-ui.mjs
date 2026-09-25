// Real HTML/localization module, controlled local resource transport, no provider calls.
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
const english = JSON.parse(fs.readFileSync(path.join(web, 'js/locales/en.json'), 'utf8'));
const ko = { ...english, 'Choose a model': '모델 선택', 'Interface language': '화면 언어', 'Save': '저장',
  'Send message': '메시지 전송', 'Search models…': '모델 검색…', 'Streaming': '스트리밍', 'Supported': '지원',
  '{count} matching models': '{count}개 모델 검색됨', '{providers} providers · {count} models': '공급자 {providers}개 · 모델 {count}개',
  'Connected: {model} ({provider})': '연결됨: {model} ({provider})', 'RAG: NOT INDEXED': 'RAG: 색인 없음' };
const tick = () => new Promise(resolve => setImmediate(resolve));
async function fixture({ saved, languages = ['en-US'], language = languages?.[0], blocked = false } = {}) {
  const dom = new JSDOM(fs.readFileSync(path.join(web, 'index.html'), 'utf8'), { url: 'https://i18n.test/', runScripts: 'outside-only' });
  const { window } = dom;
  Object.defineProperty(window.navigator, 'languages', { value: languages });
  Object.defineProperty(window.navigator, 'language', { value: language });
  if (saved) window.localStorage.setItem('mythosia.ui.language', saved);
  if (blocked) Object.defineProperty(window, 'localStorage', { get() { throw new Error('Storage blocked'); } });
  const replies = new Map([['en', english], ['ko', ko], ['ja', { ...english, 'Choose a model': 'モデルを選択' }]]);
  window.fetch = async url => {
    assert.match(url, /^\/js\/locales\/[\w-]+\.json$/, 'Only local UI resources may be fetched');
    const data = replies.get(url.split('/').at(-1).replace('.json', ''));
    if (typeof data === 'function') return data();
    if (data instanceof Error) throw data;
    return { ok: data !== undefined, json: async () => data };
  };
  const module = new vm.SourceTextModule(fs.readFileSync(path.join(web, 'js/i18n.js'), 'utf8'), { context: dom.getInternalVMContext() });
  await module.link(() => { throw new Error('Unexpected import'); }); await module.evaluate();
  return { dom, window, document: window.document, api: module.namespace, replies };
}
const f = await fixture();
try {
  const { document: d, window: w, api } = f;
  d.getElementById('chat-input').value = 'Save';
  d.getElementById('set-system').value = 'Choose a model';
  d.getElementById('modal-apikey-input').value = 'private-test-key';
  const data = d.createElement('div');
  data.innerHTML = '<div class="msg-content">Save <button title="Copy">Copy</button></div><div class="thinking-content">Streaming</div><div class="state-msg-content">Save</div><span class="state-val">Supported</span><pre>Save</pre><div class="pipe-preview">Save</div><div class="diag-detail-content">Save</div><span class="provider-name">OpenAI</span><button class="model-item" data-model="Gpt6Sol">gpt-6-sol</button>';
  d.body.append(data); const original = data.innerHTML;
  const inputs = [...d.querySelectorAll('input,textarea,select:not(#ui-language)')].map(el => [el, el.value]);
  let changes = 0; inputs.forEach(([el]) => el.addEventListener('change', () => changes++));
  const ui = api.initI18n(); await ui.ready;
  assert.equal(ui.getLocale(), 'en');
  await ui.setLocale('ko'); await tick();
  assert.equal(d.documentElement.lang, 'ko');
  assert.equal(d.querySelector('.sidebar-header h2').textContent, '모델 선택');
  assert.equal(d.getElementById('model-search').placeholder, '모델 검색…');
  assert.equal(d.getElementById('btn-send').getAttribute('aria-label'), '메시지 전송');
  assert.equal(d.getElementById('ui-language').getAttribute('aria-label'), '화면 언어');
  assert.equal(data.innerHTML, original, 'User data, model IDs and code must remain unchanged');
  const copy = d.createElement('button'); copy.textContent = 'Save'; copy.setAttribute('data-ui-localize', '');
  data.querySelector('pre').append(copy); await tick();
  assert.equal(copy.textContent, '저장', 'App-owned controls can be localized beside protected code');
  copy.remove();
  for (const [el, value] of inputs) assert.equal(el.value, value);
  assert.equal(changes, 0, 'Language changes must not resubmit settings');
  assert.equal(w.localStorage.getItem('mythosia.ui.language'), 'ko');
  assert.equal(d.querySelector('#ui-language option[value="ja"]').textContent, '日本語');
  d.getElementById('model-count').textContent = '7 providers · 87 models';
  d.getElementById('chat-status').textContent = 'Connected: gpt-6-sol (OpenAI)';
  d.getElementById('rag-chat-status').textContent = 'RAG: NOT INDEXED · TopK=8 · MinScore=0.4';
  d.getElementById('model-capabilities').innerHTML = '<details><summary>Model capabilities</summary><dl><dt>Streaming</dt><dd>Supported</dd></dl></details>';
  await tick();
  assert.equal(d.getElementById('model-count').textContent, '공급자 7개 · 모델 87개');
  assert.equal(d.getElementById('chat-status').textContent, '연결됨: gpt-6-sol (OpenAI)');
  assert.equal(d.querySelector('#model-capabilities dt').textContent, '스트리밍');
  assert.equal(d.getElementById('rag-chat-status').textContent, 'RAG: 색인 없음 · TopK=8 · MinScore=0.4');
  await ui.setLocale('en'); await tick();
  assert.equal(d.getElementById('model-count').textContent, '7 providers · 87 models');
  assert.equal(d.getElementById('chat-status').textContent, 'Connected: gpt-6-sol (OpenAI)');
  await ui.setLocale('ko'); d.getElementById('model-count').textContent = '3 matching models'; await tick();
  assert.equal(d.getElementById('model-count').textContent, '3개 모델 검색됨');
  const heading = d.querySelector('.sidebar-header h2');
  const headingParent = heading.parentElement;
  heading.remove(); await tick();
  await ui.setLocale('en'); headingParent.append(heading); await tick();
  assert.equal(heading.textContent, 'Choose a model');
  await ui.setLocale('ko'); await tick();
  assert.equal(heading.textContent, '모델 선택', 'Reattached labels remain subscribed to later language changes');
  f.replies.set('fr', { ...english, 'Choose a model': '<img src=x onerror=alert(1)>', 'Connected: {model} ({provider})': '{provider}: {model}' });
  await ui.setLocale('fr'); d.getElementById('chat-status').textContent = 'Connected: <img src=x> (OpenAI)'; await tick();
  assert.equal(d.querySelector('.sidebar-header h2').textContent, '<img src=x onerror=alert(1)>');
  assert.equal(d.querySelector('.sidebar-header h2 img'), null);
  assert.equal(d.querySelector('#chat-status img'), null);
  assert.equal(data.innerHTML, original);
  let resolveGerman;
  f.replies.set('de', () => new Promise(resolve => { resolveGerman = resolve; }));
  const old = ui.setLocale('de'); await ui.setLocale('ja');
  resolveGerman({ ok: true, json: async () => ({ ...english, 'Choose a model': 'Modell auswählen' }) }); await old;
  assert.equal(ui.getLocale(), 'ja'); assert.equal(w.localStorage.getItem('mythosia.ui.language'), 'ja');
  f.replies.set('es', { invalid: 42 }); assert.equal(await ui.setLocale('es'), false);
  assert.equal(d.documentElement.lang, 'ja'); assert.equal(d.getElementById('ui-language').value, 'ja');
  assert.equal(d.getElementById('language-status').hidden, false);
  await ui.setLocale('en'); assert.equal(d.getElementById('language-status').hidden, true);
  for (const [input, expected] of [['zh-TW','zh-Hant'], ['zh-CN','zh-Hans'], ['ja-JP','ja'], ['unsupported','en']]) assert.equal(api.resolveLocale(input), expected);
  ui.dispose();
} finally { f.dom.window.close(); }
for (const [options, expected] of [
  [{saved:'ja',languages:['ko-KR']},'ja'],
  [{saved:'en',languages:['ko-KR']},'en'],
  [{saved:'invalid',languages:['ko-KR']},'ko'],
  [{languages:['ko-KR','en-US']},'ko'],
  [{languages:['en-US','ko-KR']},'en'],
  [{languages:['ar','ja-JP','ko-KR']},'ja'],
  [{languages:['ar','ko-KR'],blocked:true},'ko'],
  [{languages:['ar','fi-FI']},'en'],
  [{languages:[],language:'ko-KR'},'ko'],
  [{languages:null,language:'ja-JP'},'ja'],
  [{languages:[],language:undefined},'en'],
  [{languages:['ko_KR','en-US']},'ko'],
  [{languages:['pt-BR','en-US']},'pt'],
  [{languages:['zh-TW','en-US']},'zh-Hant'],
  [{languages:['zh-CN','en-US']},'zh-Hans'],
  [{languages:['zh-Hans-TW','en-US']},'zh-Hans']
]) {
  const f = await fixture(options);
  try {
    if (!f.replies.has(expected)) f.replies.set(expected, JSON.parse(fs.readFileSync(path.join(web, 'js/locales', `${expected}.json`), 'utf8')));
    const ui = f.api.initI18n(); await ui.ready;
    assert.equal(ui.getLocale(), expected);
    assert.equal(f.document.documentElement.lang, expected);
    assert.equal(f.document.getElementById('ui-language').value, expected);
    if (!options.blocked) assert.equal(f.window.localStorage.getItem('mythosia.ui.language'), options.saved || null, 'Automatic detection must not persist an override of future browser preferences');
    ui.dispose();
  }
  finally { f.dom.window.close(); }
}
if (!process.argv.includes('--runtime-only')) {
  const placeholders = text => [...text.matchAll(/\{(\w+)\}/g)].map(match => match[1]).sort();
  const actual = await fixture();
  const ui = actual.api.initI18n(); await ui.ready;
  actual.document.getElementById('chat-input').value = 'My original message';
  for (const locale of ['en','ko','ja','zh-Hans','zh-Hant','de','es','fr','pt','ru','uk','vi','th']) {
    const data = JSON.parse(fs.readFileSync(path.join(web, 'js/locales', `${locale}.json`), 'utf8'));
    assert.deepEqual(Object.keys(data).sort(), Object.keys(english).sort(), `${locale}: complete key set`);
    for (const [key, value] of Object.entries(data)) {
      assert.equal(typeof value, 'string'); assert.ok(value.trim(), `${locale}: empty ${key}`);
      assert.deepEqual(placeholders(value), placeholders(key), `${locale}: placeholders in ${key}`);
    }
    if (locale !== 'en') assert.notEqual(data['Choose a model'], english['Choose a model']);
    actual.replies.set(locale, data);
    assert.equal(await ui.setLocale(locale), true);
    await tick();
    assert.equal(actual.document.documentElement.lang, locale);
    assert.equal(actual.document.querySelector('.sidebar-header h2').textContent, data['Choose a model']);
    assert.equal(actual.document.getElementById('chat-input').value, 'My original message');
  }
  await ui.setLocale('ko');
  const korean = actual.replies.get('ko');
  const d = actual.document;
  d.getElementById('reasoning-levels').innerHTML = '<label><input type="radio" value="High" checked><span>High</span></label>';
  const summary = d.createElement('div'); summary.className = 'summary-content';
  summary.innerHTML = '<span data-ui-localize>Condensing previous messages...</span>';
  d.body.append(summary);
  const source = d.createElement('summary'); source.setAttribute('data-ui-localize', ''); source.textContent = 'Sources';
  const usage = d.createElement('small'); usage.setAttribute('data-ui-localize', ''); usage.textContent = 'Tokens: 42 input · 20 output';
  d.getElementById('chat-messages').append(source, usage);
  await tick();
  assert.equal(d.querySelector('#reasoning-levels span').textContent, korean.High);
  assert.equal(d.querySelector('#reasoning-levels input').value, 'High');
  assert.equal(source.textContent, korean.Sources);
  assert.equal(usage.textContent, actual.api.formatMessage(korean['Tokens: {input} input · {output} output'], { input:42, output:20 }));
  assert.equal(summary.textContent, korean['Condensing previous messages...']);
  summary.textContent = 'Save'; await tick();
  assert.equal(summary.textContent, 'Save', 'Completed model summaries must remain original text');
  const cache = new Map();
  const getModule = file => {
    if (!cache.has(file)) cache.set(file, new vm.SourceTextModule(fs.readFileSync(file, 'utf8'), { context: actual.dom.getInternalVMContext(), identifier: file }));
    return cache.get(file);
  };
  const traceModule = getModule(path.join(web, 'js/rag-trace.js'));
  await traceModule.link((specifier, ref) => getModule(path.resolve(path.dirname(ref.identifier), specifier)));
  await traceModule.evaluate();
  const trace = d.createElement('div'); d.body.append(trace);
  traceModule.namespace.renderTrace(trace, {
    documents:[{id:'d1',source:'Save.txt',contentLength:4,preview:'Save',metadata:{ Save:'High' }}],
    chunks:[{id:'c1',documentId:'d1',index:1,contentLength:4,content:'Save'}, {id:'c2',documentId:'d1',index:2,contentLength:4,content:'Save'}],
    embeddings:[{chunkId:'c1',dimensions:2,sample:[0.1,0.2],vector:[0.1,0.2]}], records:[],
    summary:{documentCount:1,chunkCount:2,embeddingCount:1,recordCount:0,dimensions:2}
  });
  await tick();
  assert.ok([...trace.querySelectorAll('.rag-leaf--empty')].some(el => el.textContent === korean['No embeddings.']));
  assert.ok([...trace.querySelectorAll('.rag-node-title')].some(el => el.textContent === korean.Embedding));
  assert.equal(trace.querySelector('.rag-vector > summary').textContent, korean['Full vector']);
  assert.equal(trace.querySelector('.rag-node-title').textContent, actual.api.formatMessage(korean['Document · {source}'], {source:'Save.txt'}));
  assert.equal(trace.querySelector('.rag-preview').textContent, 'Save');
  assert.equal(trace.querySelector('.rag-meta-row').textContent, 'SaveHigh');
  assert.equal(trace.querySelector('.rag-vector-body').textContent, '0.100000, 0.200000');
  ui.dispose(); actual.dom.window.close();
}
console.log('PASS: language switching, dynamic UI, data preservation, locale fallback, loading races and resource contracts');
