// UI-only localization. Provider IDs, request values, documents and model output
// are data, never translation input. English phrases are the resource keys.
export const SUPPORTED_LOCALES = ['en', 'ko', 'ja', 'zh-Hans', 'zh-Hant', 'de', 'es', 'fr', 'pt', 'ru', 'uk', 'vi', 'th'];
const storageKey = 'mythosia.ui.language';
const normalize = value => String(value).replace(/\s+/g, ' ').trim();
const excluded = 'script, style, svg, pre, code, textarea, [translate="no"], [data-i18n-ignore], .msg-content, .thinking-content, .summary-content, .state-msg-content, .state-val, .state-func-desc, .state-func-name, .fn-desc, .fn-name, .fn-item-desc, .fn-item-name, .rag-preview, .pipe-preview, .pipe-detail-content, .diag-preview, .diag-detail-content, .content-viewer-code, .provider-name, .provider-glyph, .model-item, .model-switch-provider, .model-switch-event strong, #summary-text, #modal-provider-name, #state-message-json-content, #rag-file-list .rag-file-name';

// Only app-owned dynamic labels are eligible. Do not translate a returned value
// merely because it happens to equal an English button label such as "Save".
const dynamicLabels = [
  '#model-count', '#model-search-empty', '.model-search-empty', '#retry-models', '.provider-key-btn',
  '#chat-status', '#workspace-version', '#speed-help', '#settings-error', '#perplexity-settings-error',
  '#model-capabilities', '#reasoning-levels', '#set-speed option', '.modal-header h3', '#modal-key-status',
  '#alibaba-status', '.state-section-title', '.state-key', '#state-container .empty-state',
  '.fn-empty', '.rag-empty', '.rag-hint', '.rag-select-label', '.rag-select-badge', '.rag-select-inline-badge',
  '.rag-loading-card__eyebrow', '.rag-loading-card__title', '.rag-loading-card__meta',
  '.rag-loading-step__title', '.rag-loading-step__meta', '.rag-trace-tab', '.rag-stat-label',
  '.rag-leaf--empty', '.rag-node-title', '.rag-vector > summary',
  '.pipe-label', '.pipe-title', '.pipe-step-title', '.pipe-scores-table th', '.pipe-page-btn',
  '.diag-section-title', '.diag-stat-label', '.diag-scores-table th', '.diag-loading', '.diag-empty', '.diag-badge', '.diag-suggestions-title',
  '.fc-status', '.fc-section-label', '.thinking-header', '.summary-header', '.rag-progress-title',
  '.rag-progress-content', '.chat-notice-cancelled', '[data-ui-localize]', '.msg-code-btn', '.msg-rag-diagnose-btn',
  '#code-copy-all', '#state-message-json-copy', '#rag-status', '#rag-settings-status', '#rag-settings-alert',
  '#rag-embedding-hint', '#rag-openai-key-status', '#rag-perplexity-key-status', '#rag-ollama-status',
  '#rag-vllm-status', '#embedding-chat-status', '#vectordb-chat-status', '#rag-chat-status'
].join(',');

const patterns = [
  [/^(\d+) matching models$/, '{count} matching models', ['count']],
  [/^(\d+) providers · (\d+) models$/, '{providers} providers · {count} models', ['providers', 'count']],
  [/^Connecting to (.+)\.\.\.$/, 'Connecting to {model}...', ['model']],
  [/^Connected: (.+) \((.+)\)$/, 'Connected: {model} ({provider})', ['model', 'provider']],
  [/^Error: ([\s\S]+)$/, 'Error: {message}', ['message']],
  [/^(.+) API Key$/, '{provider} API Key', ['provider']],
  [/^(.+) API key$/, '{provider} API key', ['provider']],
  [/^(.+) connection settings$/, '{provider} connection settings', ['provider']],
  [/^Key saved for (.+) \(localStorage\)$/, 'Key saved for {provider} (localStorage)', ['provider']],
  [/^Core (.+)$/, 'Core {version}', ['version']],
  [/^Embedding: (.+)$/, 'Embedding: {details}', ['details']],
  [/^VectorDB: (.+)$/, 'VectorDB: {details}', ['details']],
  [/^Tokens: (\d+) input · (\d+) output$/, 'Tokens: {input} input · {output} output', ['input', 'output']]
];

function matchLocale(value) {
  const candidate = String(value || '').trim().toLowerCase().replace(/_/g, '-');
  const parts = candidate.split('-');
  if (parts[0] === 'zh') {
    if (parts.includes('hant')) return 'zh-Hant';
    if (parts.includes('hans')) return 'zh-Hans';
    return parts.some(part => ['tw', 'hk', 'mo'].includes(part)) ? 'zh-Hant' : 'zh-Hans';
  }
  return SUPPORTED_LOCALES.find(locale => locale.toLowerCase() === candidate)
    || SUPPORTED_LOCALES.find(locale => locale === parts[0]);
}

export function resolveLocale(value) {
  return matchLocale(value) || 'en';
}

export function formatMessage(template, values = {}) {
  return template.replace(/\{(\w+)\}/g, (match, key) => Object.hasOwn(values, key) ? String(values[key]) : match);
}

export function initI18n() {
  const picker = document.getElementById('ui-language');
  const status = document.getElementById('language-status');
  const bindings = new Set();
  const textBindings = new WeakMap();
  const attributeBindings = new WeakMap();
  const staticParents = new WeakSet();
  const catalogues = new Map();
  let catalogue = {};
  let locale = 'en';
  let revision = 0;
  let disposed = false;
  let observer;
  let reverse = new Map();
  let resourcePatterns = [];

  function translate(source) {
    if (Object.hasOwn(catalogue, source)) return catalogue[source];
    for (const [pattern, key, names] of patterns) {
      const match = source.match(pattern);
      if (match) return formatMessage(catalogue[key] || key, Object.fromEntries(names.map((name, index) => [name, match[index + 1]])));
    }
    for (const [pattern, key, names] of resourcePatterns) {
      const match = source.match(pattern);
      if (match) return formatMessage(catalogue[key], Object.fromEntries(names.map((name, index) => [name, match[index + 1]])));
    }
    const rag = source.match(/^(RAG: (?:NOT INDEXED|READY|ERROR))( · .+)$/);
    return rag ? (catalogue[rag[1]] || rag[1]) + rag[2] : source;
  }

  function read(binding) { return binding.attribute ? binding.node.getAttribute(binding.attribute) : binding.node.nodeValue; }
  function write(binding, value) {
    if (binding.attribute) binding.node.setAttribute(binding.attribute, value);
    else binding.node.nodeValue = value;
  }
  function paint(binding) {
    if (!binding.node.isConnected) { bindings.delete(binding); return; }
    const actual = read(binding);
    if (actual == null) return;
    // Application updates replace the canonical English source. Our own writes
    // must not become sources when another language is selected.
    if (actual !== binding.rendered) {
      const source = normalize(actual);
      binding.source = Object.hasOwn(catalogue, source) ? source : reverse.get(source) || source;
      binding.prefix = actual.match(/^\s*/)[0];
      binding.suffix = actual.match(/\s*$/)[0];
    }
    const rendered = binding.prefix + translate(binding.source) + binding.suffix;
    binding.rendered = rendered;
    if (actual !== rendered) write(binding, rendered);
  }
  function bind(node, attribute = null) {
    const map = attribute ? attributeBindings : textBindings;
    let binding;
    if (attribute) {
      if (!map.has(node)) map.set(node, new Map());
      binding = map.get(node).get(attribute);
    } else binding = map.get(node);
    if (!binding) {
      const raw = attribute ? node.getAttribute(attribute) : node.nodeValue;
      if (!raw || !normalize(raw)) return;
      const source = normalize(raw);
      binding = { node, attribute, source: Object.hasOwn(catalogue, source) ? source : reverse.get(source) || source, rendered: raw, prefix: raw.match(/^\s*/)[0], suffix: raw.match(/\s*$/)[0] };
      if (attribute) map.get(node).set(attribute, binding); else map.set(node, binding);
    }
    // A previously translated node can be detached and reattached by a panel.
    bindings.add(binding);
    paint(binding);
  }
  function visit(node, initial = false) {
    if (node.nodeType === Node.TEXT_NODE) {
      const parent = node.parentElement;
      if (!parent || (parent.closest(excluded) && !parent.closest('[data-ui-localize]'))) return;
      if (initial || staticParents.has(parent) || parent.closest(dynamicLabels)) {
        if (initial && normalize(node.nodeValue)) staticParents.add(parent);
        bind(node);
      }
      return;
    }
    if (node.nodeType !== Node.ELEMENT_NODE) return;
    // A control's accessible label may be translated while its option names
    // (native language names) and its submitted value stay unchanged.
    const skip = node.closest(excluded) && !node.closest('[data-ui-localize]');
    if ((!skip || node === picker || (node.matches('textarea') && !node.parentElement?.closest(excluded))) &&
        (initial || staticParents.has(node) || node.matches('button, input, select, textarea') || node.closest(dynamicLabels))) {
      for (const attribute of ['aria-label', 'title', 'placeholder']) if (node.hasAttribute(attribute)) bind(node, attribute);
    }
    if (skip) {
      // A protected data panel can contain an explicit app-owned loading label
      // or copy control. Traverse only those markers, never the panel's data.
      for (const label of node.querySelectorAll('[data-ui-localize]')) visit(label, initial);
      return;
    }
    for (const child of node.childNodes) visit(child, initial);
  }
  function refresh() {
    for (const binding of bindings) paint(binding);
  }
  visit(document.body, true);
  observer = new MutationObserver(records => {
    for (const record of records) {
      if (record.type === 'characterData') visit(record.target);
      else if (record.type === 'childList') {
        for (const node of record.addedNodes) visit(node);
        if (record.removedNodes.length) for (const binding of bindings) if (!binding.node.isConnected) bindings.delete(binding);
      } else visit(record.target);
    }
  });
  observer.observe(document.body, { subtree: true, childList: true, characterData: true, attributes: true, attributeFilter: ['aria-label', 'title', 'placeholder'] });

  async function setLocale(requested, persist = true) {
    const selected = resolveLocale(requested);
    const currentRevision = ++revision;
    if (picker) picker.value = selected;
    try {
      if (!catalogues.has(selected)) {
        const response = await fetch(`/js/locales/${selected}.json`);
        if (!response.ok) throw new Error('Language resource unavailable');
        const data = await response.json();
        if (!data || Array.isArray(data) || typeof data !== 'object' || Object.values(data).some(value => typeof value !== 'string')) throw new Error('Invalid language resource');
        catalogues.set(selected, data);
      }
      if (disposed || currentRevision !== revision) return false;
      // Flush pending source changes with the previous dictionary before replacing it.
      for (const binding of bindings) paint(binding);
      catalogue = catalogues.get(selected);
      resourcePatterns = Object.keys(catalogue).filter(key => /\{\w+\}/.test(key)).map(key => {
        const names = [];
        const segments = key.split(/(\{\w+\})/);
        const expression = segments.map(segment => {
          if (/^\{\w+\}$/.test(segment)) { names.push(segment.slice(1, -1)); return '(.+?)'; }
          return segment.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
        }).join('');
        return [new RegExp(`^${expression}$`), key, names];
      });
      locale = selected;
      document.documentElement.lang = selected;
      refresh();
      reverse = new Map(Object.entries(catalogue).map(([source, translated]) => [translated, source]));
      if (picker) picker.value = selected;
      if (status) { status.textContent = ''; status.hidden = true; }
      if (persist) { try { localStorage.setItem(storageKey, selected); } catch { /* storage can be disabled */ } }
      document.dispatchEvent(new CustomEvent('ui-language-change', { detail: { locale } }));
      return true;
    } catch {
      if (disposed || currentRevision !== revision) return false;
      if (picker) picker.value = locale;
      if (status) { status.textContent = translate('Unable to load the selected language.'); status.hidden = false; }
      return false;
    }
  }
  const onChange = () => setLocale(picker.value);
  picker?.addEventListener('change', onChange);
  let preferred;
  try { preferred = localStorage.getItem(storageKey); } catch { /* use browser language */ }
  if (!SUPPORTED_LOCALES.includes(preferred)) {
    // Try supported browser preferences in order before applying the English
    // fallback. Keep automatic selection separate from a saved manual choice.
    const languages = Array.isArray(navigator.languages) ? navigator.languages : [];
    preferred = languages.map(matchLocale).find(Boolean) || matchLocale(navigator.language) || 'en';
  }
  const ready = setLocale(preferred, false);
  return { ready, setLocale, refresh, getLocale: () => locale, dispose() { disposed = true; observer.disconnect(); picker?.removeEventListener('change', onChange); bindings.clear(); } };
}
