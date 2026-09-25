// Workspace navigation and accessible overlays, independent of provider requests.
export function initConsole() {
  const left = document.getElementById('sidebar-left');
  const right = document.getElementById('sidebar-right');
  const backdrop = document.getElementById('workspace-backdrop');
  const shell = document.querySelector('.app');
  const header = document.querySelector('.workspace-header');
  const modelToggle = document.getElementById('toggle-models');
  const inspectorToggle = document.getElementById('toggle-inspector');
  const narrow = window.matchMedia('(max-width: 860px)');
  const compact = window.matchMedia('(max-width: 1240px)');
  let drawer = null;
  let drawerTrigger = null;
  let dialogs = [];
  const focusable = 'button:not(:disabled), a[href], input:not(:disabled), select:not(:disabled), textarea:not(:disabled), [tabindex="0"]';
  const candidates = panel => [...panel.querySelectorAll(focusable)].filter(el => !el.closest('[inert]') && el.getClientRects().length);

  function syncDrawers() {
    if (drawer === left && !narrow.matches || drawer === right && !compact.matches) closeDrawer();
    const leftHidden = narrow.matches && drawer !== left;
    const rightHidden = compact.matches && drawer !== right;
    left.inert = leftHidden || left.classList.contains('disabled') || (drawer && drawer !== left);
    right.inert = rightHidden || (drawer && drawer !== right);
    left.setAttribute('aria-hidden', String(leftHidden));
    right.setAttribute('aria-hidden', String(rightHidden));
    document.querySelector('.chat-area').inert = !!drawer;
    backdrop.classList.toggle('hidden', !drawer);
    modelToggle.setAttribute('aria-expanded', String(drawer === left));
    inspectorToggle.setAttribute('aria-expanded', String(drawer === right));
  }
  function closeDrawer(restore = true) {
    if (!drawer) return;
    drawer.classList.remove('drawer-open');
    drawer = null;
    syncDrawers();
    if (restore && drawerTrigger?.isConnected) drawerTrigger.focus();
  }
  function openDrawer(panel, trigger) {
    if (drawer === panel) { closeDrawer(); return; }
    closeDrawer(false);
    drawerTrigger = trigger || document.activeElement;
    if (panel === left && !narrow.matches || panel === right && !compact.matches) {
      (panel === left ? document.getElementById('model-search') : candidates(panel)[0])?.focus();
      return;
    }
    drawer = panel;
    panel.classList.add('drawer-open');
    syncDrawers();
    (panel === left ? document.getElementById('model-search') : candidates(panel)[0])?.focus();
  }
  modelToggle.addEventListener('click', () => openDrawer(left, modelToggle));
  inspectorToggle.addEventListener('click', () => openDrawer(right, inspectorToggle));
  backdrop.addEventListener('click', () => closeDrawer());
  document.querySelectorAll('[data-close-drawer]').forEach(button => button.addEventListener('click', () => closeDrawer()));
  narrow.addEventListener('change', syncDrawers);
  compact.addEventListener('change', syncDrawers);
  syncDrawers();

  document.addEventListener('click', event => {
    if (event.target.closest('[data-choose-model]')) openDrawer(left, event.target.closest('button'));
    if (event.target.closest('[data-open-documents]')) document.getElementById('btn-doc-reference').click();
    if (event.target.closest('[data-open-pipeline]')) document.getElementById('btn-rag-settings').click();
  });

  // Closed off-canvas panels must not remain in the tab order or accessibility tree.
  const overlays = [...document.querySelectorAll('.modal-overlay, .rag-slide-panel')];
  const returnFocus = new WeakMap();
  function updateOverlays() {
    const newlyOpened = overlays.filter(panel => !dialogs.includes(panel) && (panel.matches('.rag-slide-panel') ? panel.classList.contains('open') : !panel.classList.contains('hidden')));
    const closed = dialogs.filter(panel => panel.matches('.rag-slide-panel') ? !panel.classList.contains('open') : panel.classList.contains('hidden'));
    dialogs = dialogs.filter(panel => !closed.includes(panel));
    newlyOpened.forEach(panel => {
      returnFocus.set(panel, document.activeElement);
      closeDrawer(false);
      dialogs.push(panel);
    });
    const top = dialogs.at(-1);
    overlays.forEach(panel => {
      const open = dialogs.includes(panel);
      panel.inert = !open || panel !== top;
      panel.setAttribute('aria-hidden', String(!open));
      panel.setAttribute('aria-modal', String(panel === top));
    });
    shell.inert = !!top;
    header.inert = !!top;
    if (top && newlyOpened.includes(top)) (candidates(top)[0] || top).focus();
    else if (closed.length) {
      const target = returnFocus.get(closed.at(-1));
      if (target?.isConnected && !target.closest('[inert]') && target.getClientRects().length) target.focus();
      else if (top) (candidates(top)[0] || top).focus();
      else if (narrow.matches && target?.closest('#sidebar-left')) modelToggle.focus();
    }
  }
  overlays.forEach((panel, index) => {
    panel.setAttribute('role', 'dialog');
    panel.setAttribute('tabindex', '-1');
    const title = panel.querySelector('h2, h3');
    if (title) {
      if (!title.id) title.id = `workspace-dialog-title-${index}`;
      panel.setAttribute('aria-labelledby', title.id);
    }
    new MutationObserver(updateOverlays).observe(panel, { attributes: true, attributeFilter: ['class'] });
  });
  updateOverlays();
  document.addEventListener('keydown', event => {
    if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 'k' && !dialogs.length) {
      event.preventDefault(); openDrawer(left, document.activeElement); return;
    }
    const panel = dialogs.at(-1) || drawer;
    if (!panel) return;
    if (event.key === 'Escape') {
      event.preventDefault();
      if (dialogs.length) {
        // Use existing feature close handlers, including their backdrop/state cleanup.
        panel.querySelector('button[id$="-close"], button[id="modal-close"]')?.click();
      } else closeDrawer();
    }
    if (event.key === 'Tab') {
      const elements = candidates(panel);
      if (!elements.length) { event.preventDefault(); panel.focus(); return; }
      const first = elements[0], last = elements.at(-1);
      if (event.shiftKey && (document.activeElement === first || !panel.contains(document.activeElement))) { event.preventDefault(); last.focus(); }
      else if (!event.shiftKey && (document.activeElement === last || !panel.contains(document.activeElement))) { event.preventDefault(); first.focus(); }
    }
  });

  // Associate the existing settings labels without changing their DOM hooks.
  document.querySelectorAll('.setting-row').forEach(row => {
    const label = row.querySelector(':scope > label');
    const control = row.querySelector('input[id], textarea[id], select[id]');
    if (label && control && !label.htmlFor && !label.contains(control)) label.htmlFor = control.id;
  });
  document.querySelectorAll('button.btn-icon[title]').forEach(button => {
    if (!button.hasAttribute('aria-label')) button.setAttribute('aria-label', button.title);
  });
  const chatStatus = document.getElementById('chat-status');
  new MutationObserver(() => {
    if (chatStatus.classList.contains('connected')) {
      closeDrawer(false);
      document.getElementById('chat-input').placeholder = 'Ask a question, test an idea, explore your documents…';
    }
  }).observe(chatStatus, { attributes: true, attributeFilter: ['class'], childList: true });
  const ragStatus = document.getElementById('rag-chat-status');
  new MutationObserver(() => document.getElementById('document-status-dot').classList.toggle('indexed', ragStatus.classList.contains('active'))).observe(ragStatus, { attributes: true, attributeFilter: ['class'] });

  loadWorkspaceVersions();
}

export async function loadWorkspaceVersions() {
  const label = document.getElementById('workspace-version');
  try {
    const response = await fetch('/api/testbed');
    if (!response.ok) throw new Error('Version metadata unavailable');
    const info = await response.json();
    const packages = Array.isArray(info.packages) ? info.packages : [];
    const core = packages.find(item => item.name === 'Mythosia.AI');
    if (!core) throw new Error('Core version unavailable');
    label.textContent = `Core ${core.version}`;
    label.title = packages.map(item => `${item.name} ${item.version}`).join('\n');
  } catch {
    label.textContent = 'Local development';
    label.title = 'Package version metadata is unavailable.';
  }
}
