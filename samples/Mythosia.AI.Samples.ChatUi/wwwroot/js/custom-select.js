// ═══════════════════════════════════════════════════════════════
// Custom Selects (Chat UI)
// ═══════════════════════════════════════════════════════════════

import { $$ } from './utils.js';

const selectInstances = [];
const initializedRoots = new WeakSet();
let activeInstance = null;
let globalListenersBound = false;

export function initCustomSelects() {
  $$('.rag-select').forEach((root) => {
    const instance = createInstance(root);
    if (instance) selectInstances.push(instance);
  });

  if (!globalListenersBound) {
    document.addEventListener('click', handleDocumentClick);
    document.addEventListener('keydown', handleDocumentKeydown);
    globalListenersBound = true;
  }
}

function createInstance(root) {
  if (initializedRoots.has(root)) return null;
  const trigger = root.querySelector('.rag-select-trigger');
  const menu = root.querySelector('.rag-select-menu');
  const native = root.querySelector('select');
  if (!trigger || !menu || !native) return null;

  const options = Array.from(menu.querySelectorAll('.rag-select-option'));
  const label = trigger.querySelector('.rag-select-label');
  const inlineBadge = trigger.querySelector('.rag-badge-inline');

  const instance = {
    root,
    trigger,
    menu,
    native,
    options,
    label,
    inlineBadge,
    highlightIndex: null
  };

  trigger.addEventListener('click', (event) => {
    event.preventDefault();
    toggleSelect(instance);
  });
  trigger.addEventListener('keydown', (event) => handleTriggerKeydown(event, instance));
  menu.addEventListener('click', (event) => handleMenuClick(event, instance));
  native.addEventListener('change', () => syncFromNative(instance));

  syncFromNative(instance);
  initializedRoots.add(root);
  return instance;
}

function handleMenuClick(event, instance) {
  const option = event.target.closest('.rag-select-option');
  if (!option || !instance.menu.contains(option)) return;
  selectOption(instance, option);
  closeSelect(instance);
}

function handleTriggerKeydown(event, instance) {
  const key = event.key;
  if (key === 'ArrowDown' || key === 'ArrowUp') {
    event.preventDefault();
    if (!isOpen(instance)) {
      openSelect(instance);
    }
    moveHighlight(instance, key === 'ArrowDown' ? 1 : -1);
    return;
  }

  if (key === 'Enter' || key === ' ') {
    event.preventDefault();
    if (!isOpen(instance)) {
      openSelect(instance);
    } else {
      selectHighlighted(instance);
      closeSelect(instance);
    }
    return;
  }

  if (key === 'Escape' && isOpen(instance)) {
    event.preventDefault();
    closeSelect(instance);
  }

  if (key === 'Tab' && isOpen(instance)) {
    closeSelect(instance);
  }
}

if (document.readyState === 'loading') {
  document.addEventListener('DOMContentLoaded', () => initCustomSelects());
} else {
  initCustomSelects();
}

function handleDocumentClick(event) {
  if (!activeInstance) return;
  if (!activeInstance.root.contains(event.target)) {
    closeSelect(activeInstance);
  }
}

function handleDocumentKeydown(event) {
  if (!activeInstance || event.key !== 'Escape') return;
  closeSelect(activeInstance);
  activeInstance.trigger?.focus();
}

function openSelect(instance) {
  if (activeInstance && activeInstance !== instance) {
    closeSelect(activeInstance);
  }
  instance.menu.classList.remove('hidden');
  instance.root.classList.add('open');
  instance.trigger.setAttribute('aria-expanded', 'true');
  activeInstance = instance;
  setHighlightIndex(instance, getSelectedIndex(instance));
}

function closeSelect(instance) {
  instance.menu.classList.add('hidden');
  instance.root.classList.remove('open');
  instance.trigger.setAttribute('aria-expanded', 'false');
  clearHighlight(instance);
  if (activeInstance === instance) {
    activeInstance = null;
  }
}

function toggleSelect(instance) {
  if (isOpen(instance)) {
    closeSelect(instance);
  } else {
    openSelect(instance);
  }
}

function isOpen(instance) {
  return !instance.menu.classList.contains('hidden');
}

function selectOption(instance, option) {
  const value = option.dataset.value;
  if (!value) return;
  instance.native.value = value;
  instance.native.dispatchEvent(new Event('change', { bubbles: true }));
}

function selectHighlighted(instance) {
  const index = getHighlightIndex(instance);
  const option = instance.options[index];
  if (option) {
    selectOption(instance, option);
  }
}

function moveHighlight(instance, delta) {
  if (instance.options.length === 0) return;
  const current = getHighlightIndex(instance);
  const next = (current + delta + instance.options.length) % instance.options.length;
  setHighlightIndex(instance, next);
}

function getHighlightIndex(instance) {
  if (typeof instance.highlightIndex === 'number') return instance.highlightIndex;
  return getSelectedIndex(instance);
}

function setHighlightIndex(instance, index) {
  instance.highlightIndex = index;
  instance.options.forEach((option, idx) => {
    option.classList.toggle('highlighted', idx === index);
    if (idx === index) {
      option.scrollIntoView({ block: 'nearest' });
    }
  });
}

function clearHighlight(instance) {
  instance.highlightIndex = null;
  instance.options.forEach((option) => option.classList.remove('highlighted'));
}

function getSelectedIndex(instance) {
  const value = instance.native.value;
  const index = instance.options.findIndex((option) => option.dataset.value === value);
  return index >= 0 ? index : 0;
}

function syncFromNative(instance) {
  const value = instance.native.value;
  const selectedOption = instance.options.find((option) => option.dataset.value === value);
  const labelText = selectedOption?.querySelector('.rag-select-option-label')?.textContent?.trim()
    || selectedOption?.textContent?.trim()
    || instance.native.selectedOptions?.[0]?.textContent
    || value;

  if (instance.label) {
    instance.label.textContent = labelText;
  }

  instance.options.forEach((option) => {
    const selected = option.dataset.value === value;
    option.classList.toggle('active', selected);
    option.setAttribute('aria-selected', selected ? 'true' : 'false');
  });

  if (instance.inlineBadge) {
    const badge = selectedOption?.querySelector('.rag-badge');
    if (badge) {
      instance.inlineBadge.textContent = badge.textContent.trim();
      instance.inlineBadge.classList.remove('hidden');
    } else {
      instance.inlineBadge.classList.add('hidden');
    }
  }
}
