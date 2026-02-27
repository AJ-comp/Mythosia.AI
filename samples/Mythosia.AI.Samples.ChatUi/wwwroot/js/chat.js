// ═══════════════════════════════════════════════════════════════
// Chat Messaging, Thinking Bubbles, Function Call Cards
// ═══════════════════════════════════════════════════════════════

import { escapeHtml, renderMarkdown, addCopyButtons } from './utils.js';
import { chatMessages, chatForm, chatInput, btnSend, btnClear } from './dom.js';
import { app, autoScroll, updateSidebarDisabled } from './state.js';
import { refreshState } from './state-panel.js';
import { addViewCodeButton } from './code-modal.js';

// ── Chat form event listeners ────────────────────────────────
export function initChat() {
  chatForm.addEventListener('submit', (e) => {
    e.preventDefault();
    sendMessage();
  });

  chatInput.addEventListener('keydown', (e) => {
    if (e.key === 'Enter' && !e.shiftKey) {
      e.preventDefault();
      sendMessage();
    }
  });

  chatInput.addEventListener('input', () => {
    chatInput.style.height = 'auto';
    chatInput.style.height = Math.min(chatInput.scrollHeight, 120) + 'px';
  });

  btnClear.addEventListener('click', async () => {
    if (!app.isConnected) return;
    try {
      await fetch('/api/clear', { method: 'POST' });
      chatMessages.innerHTML = '';
      refreshState();
    } catch (e) { /* ignore */ }
  });
}

// ── Send message (SSE streaming) ─────────────────────────────
async function sendMessage() {
  const text = chatInput.value.trim();
  if (!text || app.isSending || !app.isConnected) return;

  app.isSending = true;
  app.shouldAutoScroll = true;
  btnSend.disabled = true;
  updateSidebarDisabled(true);

  appendMessage('user', text);
  chatInput.value = '';
  chatInput.style.height = 'auto';

  const typingEl = showTyping();

  try {
    const res = await fetch('/api/chat', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ message: text })
    });

    removeTyping(typingEl);

    const contentType = res.headers.get('Content-Type') || '';

    // Non-stream response (error JSON)
    if (!contentType.includes('text/event-stream')) {
      const data = await res.json();
      const el = appendMessage('assistant', res.ok ? (data.response || JSON.stringify(data)) : `Error: ${data.error}`);
      if (res.ok && el) addViewCodeButton(el, text);
      refreshState();
      return;
    }

    // SSE streaming — enforce order: FC → Thinking → Text
    const reader = res.body.getReader();
    const decoder = new TextDecoder();
    let buffer = '';
    let fullText = '';
    let reasoningText = '';
    let thinkingEl = null;
    let thinkingContent = null;
    let msgDiv = null;
    let contentSpan = null;
    let gotText = false;
    let fcCardEl = null;

    // Summarizing indicator
    let summaryIndicator = null;

    // Response container — events appended in arrival order
    const responseContainer = createResponseContainer();

    while (true) {
      const { done, value } = await reader.read();
      if (done) break;

      buffer += decoder.decode(value, { stream: true });
      const lines = buffer.split('\n');
      buffer = lines.pop();

      for (const line of lines) {
        const trimmed = line.trim();
        if (!trimmed.startsWith('data: ')) continue;
        const payload = trimmed.substring(6);
        if (payload === '[DONE]') continue;
        try {
          const parsed = JSON.parse(payload);

          if (parsed.type === 'summary_start') {
            summaryIndicator = createSummaryBubble(responseContainer);
          }
          else if (parsed.type === 'summary_end') {
            if (summaryIndicator) completeSummaryBubble(summaryIndicator, parsed.summary);
            summaryIndicator = null;
          }
          else if (parsed.type === 'summary_error') {
            if (summaryIndicator) failSummaryBubble(summaryIndicator, parsed.content);
            summaryIndicator = null;
          }
          else if (parsed.type === 'function_call') {
            if (thinkingEl) collapseThinking(thinkingEl);
            thinkingEl = null;
            thinkingContent = null;
            reasoningText = '';
            fcCardEl = createFunctionCallCard(parsed.name, parsed.arguments || parsed.content, responseContainer);
          }
          else if (parsed.type === 'function_result') {
            if (fcCardEl) {
              completeFunctionCallCard(fcCardEl, parsed.content, parsed.name, parsed.arguments);
              fcCardEl = null;
            }
            thinkingEl = null;
            thinkingContent = null;
            reasoningText = '';
          }
          else if (parsed.type === 'reasoning' && parsed.content != null) {
            reasoningText += parsed.content;
            if (!thinkingEl) {
              thinkingEl = createThinkingBubble(responseContainer);
              thinkingContent = thinkingEl.querySelector('.thinking-content');
            }
            thinkingContent.textContent = reasoningText;
            thinkingContent.scrollTop = thinkingContent.scrollHeight;
          }
          else if (parsed.type === 'text' && parsed.content != null) {
            if (!gotText) {
              gotText = true;
              if (thinkingEl) collapseThinking(thinkingEl);
              msgDiv = createMessageElement('assistant', responseContainer);
              contentSpan = msgDiv.querySelector('.msg-content');
            }
            fullText += parsed.content;
            contentSpan.innerHTML = renderMarkdown(fullText);
            addCopyButtons(contentSpan);
          }
          else if (parsed.type === 'error') {
            fullText += '\nError: ' + (parsed.content || 'Unknown error');
            if (!msgDiv) {
              msgDiv = createMessageElement('assistant', responseContainer);
              contentSpan = msgDiv.querySelector('.msg-content');
            }
            contentSpan.innerHTML = renderMarkdown(fullText);
          }
        } catch (_) {}
      }

      autoScroll();
    }

    if (fcCardEl) completeFunctionCallCard(fcCardEl, '(no result)', '', null);

    if (!gotText && !fullText) {
      if (thinkingEl) collapseThinking(thinkingEl);
      const fallbackDiv = createMessageElement('assistant', responseContainer);
      const fallbackSpan = fallbackDiv.querySelector('.msg-content');
      fallbackSpan.innerHTML = renderMarkdown(reasoningText || '(empty response)');
      addViewCodeButton(fallbackDiv, text);
    } else if (msgDiv && !fullText) {
      contentSpan.innerHTML = '<p>(empty response)</p>';
    }
    if (msgDiv) addViewCodeButton(msgDiv, text);
    if (thinkingEl) {
      const hdr = thinkingEl.querySelector('.thinking-header');
      if (hdr) hdr.classList.add('done');
    }
    // Remove container if completely empty
    if (!responseContainer.hasChildNodes()) responseContainer.remove();
    refreshState();
  } catch (e) {
    removeTyping(typingEl);
    appendMessage('assistant', `Network error: ${e.message}`);
  } finally {
    app.isSending = false;
    app.shouldAutoScroll = true;
    btnSend.disabled = false;
    updateSidebarDisabled(false);
    chatInput.focus();
  }
}

// ── Response container (sequential, arrival order) ─────────────
function createResponseContainer() {
  const empty = chatMessages.querySelector('.empty-state');
  if (empty) empty.remove();

  const container = document.createElement('div');
  container.className = 'response-container';
  chatMessages.appendChild(container);
  return container;
}

// ── Message DOM helpers ──────────────────────────────────────
function createMessageElement(role, target) {
  const empty = chatMessages.querySelector('.empty-state');
  if (empty) empty.remove();

  const div = document.createElement('div');
  div.className = `msg ${role}`;
  div.innerHTML = `<span class="msg-role">${role}</span><div class="msg-content"></div>`;
  (target || chatMessages).appendChild(div);
  autoScroll();
  return div;
}

export function appendMessage(role, content) {
  const el = createMessageElement(role);
  const span = el.querySelector('.msg-content');
  if (role === 'assistant') {
    span.innerHTML = renderMarkdown(content);
    addCopyButtons(span);
  } else {
    span.textContent = content;
  }
  return el;
}

function showTyping() {
  const div = document.createElement('div');
  div.className = 'typing';
  div.innerHTML = '<span></span><span></span><span></span>';
  chatMessages.appendChild(div);
  autoScroll();
  return div;
}

function removeTyping(el) {
  if (el && el.parentNode) el.parentNode.removeChild(el);
}

// ── Thinking bubble ──────────────────────────────────────────
function createThinkingBubble(target) {
  const empty = chatMessages.querySelector('.empty-state');
  if (empty) empty.remove();

  const div = document.createElement('div');
  div.className = 'msg-thinking';
  div.innerHTML = `
    <div class="thinking-header">
      <span class="thinking-icon"></span>
      <span>Thinking</span>
      <span class="thinking-arrow open">&#9654;</span>
    </div>
    <div class="thinking-content"></div>`;
  (target || chatMessages).appendChild(div);

  const hdr = div.querySelector('.thinking-header');
  const content = div.querySelector('.thinking-content');
  const arrow = div.querySelector('.thinking-arrow');
  hdr.addEventListener('click', () => {
    content.classList.toggle('collapsed');
    arrow.classList.toggle('open');
  });

  autoScroll();
  return div;
}

function collapseThinking(el) {
  if (!el) return;
  const content = el.querySelector('.thinking-content');
  const arrow = el.querySelector('.thinking-arrow');
  if (content) content.classList.add('collapsed');
  if (arrow) arrow.classList.remove('open');
  const hdr = el.querySelector('.thinking-header');
  if (hdr) hdr.classList.add('done');
}

// ── Function Call Card ───────────────────────────────────────
function createFunctionCallCard(name, args, target) {
  const empty = chatMessages.querySelector('.empty-state');
  if (empty) empty.remove();

  const card = document.createElement('div');
  card.className = 'fc-card';
  card.innerHTML = `
    <div class="fc-card-header">
      <div class="fc-header-left">
        <span class="fc-icon-wrap">
          <svg class="fc-icon-svg" width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
            <path d="M14.7 6.3a1 1 0 0 0 0 1.4l1.6 1.6a1 1 0 0 0 1.4 0l3.77-3.77a6 6 0 0 1-7.94 7.94l-6.91 6.91a2.12 2.12 0 0 1-3-3l6.91-6.91a6 6 0 0 1 7.94-7.94l-3.76 3.76z"/>
          </svg>
        </span>
        <span class="fc-name">${escapeHtml(name || 'function')}</span>
      </div>
      <div class="fc-header-right">
        <span class="fc-status fc-status-running">
          <span class="fc-status-dot"></span>
          Running
        </span>
        <svg class="fc-chevron" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
          <polyline points="6 9 12 15 18 9"/>
        </svg>
      </div>
    </div>
    <div class="fc-card-body">
      <div class="fc-section">
        <div class="fc-section-label">Arguments</div>
        <pre class="fc-args">${escapeHtml(formatFcJson(args))}</pre>
      </div>
      <div class="fc-result-section" style="display:none">
        <div class="fc-section-label">Result</div>
        <pre class="fc-result"></pre>
      </div>
    </div>`;
  (target || chatMessages).appendChild(card);

  const header = card.querySelector('.fc-card-header');
  const body = card.querySelector('.fc-card-body');
  const chevron = card.querySelector('.fc-chevron');
  header.addEventListener('click', () => {
    const collapsed = body.classList.toggle('collapsed');
    chevron.classList.toggle('fc-chevron-collapsed', collapsed);
  });

  autoScroll();
  return card;
}

function completeFunctionCallCard(card, result, name, args) {
  if (!card) return;
  card.classList.add('fc-done');
  const status = card.querySelector('.fc-status');
  status.innerHTML = `<svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="3" stroke-linecap="round" stroke-linejoin="round"><polyline points="20 6 9 17 4 12"/></svg> Done`;
  status.className = 'fc-status fc-status-done';

  if (args) {
    const argsPre = card.querySelector('.fc-args');
    if (argsPre) argsPre.textContent = formatFcJson(args);
  }

  const resultSection = card.querySelector('.fc-result-section');
  const resultPre = card.querySelector('.fc-result');
  resultSection.style.display = '';
  resultPre.textContent = formatFcJson(result);

  const body = card.querySelector('.fc-card-body');
  const chevron = card.querySelector('.fc-chevron');
  body.classList.add('collapsed');
  if (chevron) chevron.classList.add('fc-chevron-collapsed');
}

// ── Summary Bubble (thinking-style collapsible) ────────────
function createSummaryBubble(target) {
  const el = document.createElement('div');
  el.className = 'msg-summary';
  el.innerHTML = `
    <div class="summary-header">
      <span class="summary-icon"></span>
      <span>Summarizing</span>
      <span class="summary-arrow open">&#9654;</span>
    </div>
    <div class="summary-content">Condensing previous messages...</div>`;
  (target || chatMessages).appendChild(el);

  const hdr = el.querySelector('.summary-header');
  const content = el.querySelector('.summary-content');
  const arrow = el.querySelector('.summary-arrow');
  hdr.addEventListener('click', () => {
    content.classList.toggle('collapsed');
    arrow.classList.toggle('open');
  });

  autoScroll();
  return el;
}

function completeSummaryBubble(el, summary) {
  if (!el) return;
  el.classList.add('summary-done');
  const hdr = el.querySelector('.summary-header');
  if (hdr) hdr.classList.add('done');
  const content = el.querySelector('.summary-content');
  content.textContent = summary || '(no summary generated)';
  // Auto-collapse after completion
  content.classList.add('collapsed');
  const arrow = el.querySelector('.summary-arrow');
  if (arrow) arrow.classList.remove('open');
}

function failSummaryBubble(el, error) {
  if (!el) return;
  el.classList.add('summary-failed');
  const hdr = el.querySelector('.summary-header');
  if (hdr) hdr.classList.add('done');
  hdr.querySelector('span:nth-child(2)').textContent = 'Summary failed';
  const content = el.querySelector('.summary-content');
  content.textContent = error || 'Unknown error';
}

function formatFcJson(str) {
  if (!str) return '(empty)';
  try {
    const obj = typeof str === 'string' ? JSON.parse(str) : str;
    return JSON.stringify(obj, null, 2);
  } catch(_) {
    return String(str);
  }
}
