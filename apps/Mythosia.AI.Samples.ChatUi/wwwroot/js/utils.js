// ═══════════════════════════════════════════════════════════════
// Utility functions
// ═══════════════════════════════════════════════════════════════

export const $ = (sel) => document.querySelector(sel);
export const $$ = (sel) => document.querySelectorAll(sel);

export function escapeHtml(str) {
  if (!str) return '';
  return String(str)
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;');
}

export function truncate(str, max) {
  if (!str) return '';
  return str.length > max ? str.substring(0, max) + '...' : str;
}

// ── Markdown config ──────────────────────────────────────────
if (typeof globalThis.marked?.setOptions === 'function') {
  globalThis.marked.setOptions({
    breaks: true,
    gfm: true,
    highlight: function(code, lang) {
      const highlighter = globalThis.hljs;
      if (!highlighter) return escapeHtml(code);
      if (lang && highlighter.getLanguage(lang)) {
        return highlighter.highlight(code, { language: lang }).value;
      }
      return highlighter.highlightAuto(code).value;
    }
  });
}

export function renderMarkdown(raw) {
  const text = String(raw ?? '');
  const parser = globalThis.marked;
  const sanitizer = globalThis.DOMPurify;
  // Missing CDN scripts or an unsupported DOM must degrade to literal text.
  if (typeof parser?.parse !== 'function' || typeof sanitizer?.sanitize !== 'function'
      || sanitizer.isSupported === false) return escapeHtml(text);
  try {
    const parsed = parser.parse(text);
    if (typeof parsed !== 'string') return escapeHtml(text);
    return sanitizer.sanitize(parsed, {
      USE_PROFILES: { html: true },
      // A model response cannot style the console or impersonate its controls.
      FORBID_TAGS: ['style', 'form', 'input', 'button', 'textarea', 'select', 'option'],
      FORBID_ATTR: ['style'],
      ALLOW_DATA_ATTR: false,
      SANITIZE_NAMED_PROPS: true
    });
  } catch (_) {
    return escapeHtml(text);
  }
}

export function addCopyButtons(container) {
  container.querySelectorAll('pre code').forEach(block => {
    if (block.parentElement.querySelector('.code-copy-btn')) return;
    const btn = document.createElement('button');
    btn.className = 'code-copy-btn';
    btn.setAttribute('data-ui-localize', '');
    btn.textContent = 'Copy';
    btn.addEventListener('click', () => {
      navigator.clipboard.writeText(block.textContent).then(() => {
        btn.textContent = 'Copied!';
        setTimeout(() => btn.textContent = 'Copy', 1500);
      });
    });
    block.parentElement.style.position = 'relative';
    block.parentElement.appendChild(btn);
  });
}
