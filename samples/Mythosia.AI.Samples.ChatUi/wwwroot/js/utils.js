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
marked.setOptions({
  breaks: true,
  gfm: true,
  highlight: function(code, lang) {
    if (lang && hljs.getLanguage(lang)) {
      return hljs.highlight(code, { language: lang }).value;
    }
    return hljs.highlightAuto(code).value;
  }
});

export function renderMarkdown(raw) {
  try { return marked.parse(raw); }
  catch(_) { return escapeHtml(raw); }
}

export function addCopyButtons(container) {
  container.querySelectorAll('pre code').forEach(block => {
    if (block.parentElement.querySelector('.code-copy-btn')) return;
    const btn = document.createElement('button');
    btn.className = 'code-copy-btn';
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
