// ═══════════════════════════════════════════════════════════════
// Code Viewer Modal
// ═══════════════════════════════════════════════════════════════

import { codeModal, codeModalContent, codeModalClose, codeCopyAll } from './dom.js';

function openCodeModal(userMessage) {
  codeModal.classList.remove('hidden');
  codeModalContent.textContent = 'Loading...';
  codeCopyAll.textContent = 'Copy';

  fetch('/api/code-snippet', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ userMessage })
  })
  .then(r => r.json())
  .then(data => {
    codeModalContent.textContent = data.code || 'No code available';
    hljs.highlightElement(codeModalContent);
  })
  .catch(() => {
    codeModalContent.textContent = '// Failed to load code snippet';
  });
}

function closeCodeModal() {
  codeModal.classList.add('hidden');
  codeModalContent.textContent = '';
}

export function addViewCodeButton(msgDiv, userMessage) {
  const actions = document.createElement('div');
  actions.className = 'msg-actions';
  const btn = document.createElement('button');
  btn.className = 'msg-code-btn';
  btn.innerHTML = '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><polyline points="16 18 22 12 16 6"/><polyline points="8 6 2 12 8 18"/></svg> View Code';
  btn.addEventListener('click', () => openCodeModal(userMessage));
  actions.appendChild(btn);
  msgDiv.appendChild(actions);
}

export function initCodeModal() {
  codeModalClose.addEventListener('click', closeCodeModal);
  codeModal.addEventListener('click', (e) => {
    if (e.target === codeModal) closeCodeModal();
  });
  codeCopyAll.addEventListener('click', () => {
    navigator.clipboard.writeText(codeModalContent.textContent).then(() => {
      codeCopyAll.textContent = 'Copied!';
      setTimeout(() => codeCopyAll.textContent = 'Copy', 1500);
    });
  });
}
