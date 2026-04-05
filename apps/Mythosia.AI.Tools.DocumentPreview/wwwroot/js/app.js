// ── Elements ──────────────────────────────────────────────────
const uploadArea     = document.getElementById('uploadArea');
const fileInput      = document.getElementById('fileInput');
const uploadPrompt   = document.getElementById('uploadPrompt');
const uploadSelected = document.getElementById('uploadSelected');
const selectedFileName = document.getElementById('selectedFileName');
const btnClear       = document.getElementById('btnClear');
const btnAnalyze     = document.getElementById('btnAnalyze');
const chunkSizeEl     = document.getElementById('chunkSize');
const warnThresholdEl = document.getElementById('warnThreshold');
const errorBar       = document.getElementById('errorBar');
const panels         = document.getElementById('panels');
const loading        = document.getElementById('loading');
const markdownOutput = document.getElementById('markdownOutput');
const markdownMeta   = document.getElementById('markdownMeta');
const chunksOutput   = document.getElementById('chunksOutput');
const chunksMeta     = document.getElementById('chunksMeta');
const treeOutput     = document.getElementById('treeOutput');
const treeMeta       = document.getElementById('treeMeta');

let selectedFile = null;

// ── Upload ────────────────────────────────────────────────────
uploadArea.addEventListener('click', (e) => {
  if (e.target === btnClear) return;
  fileInput.click();
});

fileInput.addEventListener('change', () => {
  if (fileInput.files[0]) setFile(fileInput.files[0]);
});

uploadArea.addEventListener('dragover', (e) => {
  e.preventDefault();
  uploadArea.classList.add('drag-over');
});
uploadArea.addEventListener('dragleave', () => uploadArea.classList.remove('drag-over'));
uploadArea.addEventListener('drop', (e) => {
  e.preventDefault();
  uploadArea.classList.remove('drag-over');
  const f = e.dataTransfer.files[0];
  if (f) setFile(f);
});

btnClear.addEventListener('click', (e) => {
  e.stopPropagation();
  clearFile();
});

function setFile(file) {
  selectedFile = file;
  selectedFileName.textContent = file.name;
  uploadPrompt.hidden = true;
  uploadSelected.hidden = false;
  btnAnalyze.disabled = false;
  hideError();
  panels.hidden = true;
}

function clearFile() {
  selectedFile = null;
  fileInput.value = '';
  uploadPrompt.hidden = false;
  uploadSelected.hidden = true;
  btnAnalyze.disabled = true;
  panels.hidden = true;
  hideError();
}

// ── Analyze ───────────────────────────────────────────────────
btnAnalyze.addEventListener('click', analyze);

async function analyze() {
  if (!selectedFile) return;

  hideError();
  panels.hidden = true;
  loading.hidden = false;
  btnAnalyze.disabled = true;

  const form = new FormData();
  form.append('file', selectedFile);
  form.append('chunkSize', chunkSizeEl.value);

  try {
    const res = await fetch('/api/preview', { method: 'POST', body: form });
    const data = await res.json().catch(() => null);

    if (!res.ok) {
      showError(data?.detail || data?.error || `서버 오류 (${res.status})`);
      return;
    }

    renderResults(data);
  } catch (err) {
    showError(err.message || '요청 실패');
  } finally {
    loading.hidden = true;
    btnAnalyze.disabled = false;
  }
}

// ── Render ────────────────────────────────────────────────────
function renderResults(data) {
  // Markdown panel
  markdownOutput.textContent = data.markdown ?? '';
  markdownMeta.textContent   = `${(data.markdownLength ?? 0).toLocaleString()} chars`;

  // Tree panel
  renderTree(data.tree);

  // Chunks panel
  const warnThreshold = parseInt(warnThresholdEl.value, 10) || 800;
  const chunks = data.chunks ?? [];
  const overCount = chunks.filter(c => c.length > warnThreshold).length;

  chunksMeta.textContent = overCount > 0
    ? `${chunks.length}개 · ⚠ ${overCount}개 초과`
    : `${chunks.length}개`;

  chunksOutput.innerHTML = '';
  for (const chunk of chunks) {
    chunksOutput.appendChild(renderChunkCard(chunk, warnThreshold));
  }

  panels.hidden = false;
}

function renderChunkCard(chunk, warnThreshold) {
  const isWarn = chunk.length > warnThreshold;

  const card = document.createElement('div');
  card.className = 'chunk-card' + (isWarn ? ' warn' : '');

  const header = document.createElement('div');
  header.className = 'chunk-header';
  header.innerHTML = `
    <span class="chunk-index">#${chunk.index + 1}</span>
    <span class="chunk-length">${chunk.length.toLocaleString()} chars</span>
    ${isWarn ? `<span class="chunk-warn-badge">⚠ 초과</span>` : ''}
    <span class="chunk-arrow">▶</span>`;

  const body = document.createElement('div');
  body.className = 'chunk-body';
  body.textContent = chunk.content;

  header.addEventListener('click', () => card.classList.toggle('open'));

  card.appendChild(header);
  card.appendChild(body);
  return card;
}

// ── Document Tree ─────────────────────────────────────────────
const TYPE_LABELS = {
  TitleItem:         'Title',
  SectionHeaderItem: 'Heading',
  TextItem:          'Text',
  DocListItem:       'ListItem',
  CodeItem:          'Code',
  FormulaItem:       'Formula',
  TableItem:         'Table',
  PictureItem:       'Picture',
  GroupItem:         'Group',
};

function renderTree(root) {
  treeOutput.innerHTML = '';
  if (!root) return;

  const total = countNodes(root) - 1; // exclude root body node itself
  treeMeta.textContent = `${total}개 노드`;

  // Render body's children directly (body is just a container)
  for (const child of (root.children || [])) {
    treeOutput.appendChild(renderTreeNode(child, 0));
  }
}

function countNodes(node) {
  return 1 + (node.children || []).reduce((s, c) => s + countNodes(c), 0);
}

function renderTreeNode(node, depth) {
  const hasChildren = node.children && node.children.length > 0;

  const div = document.createElement('div');

  const row = document.createElement('div');
  row.className = 'tree-row';
  row.style.paddingLeft = `${depth * 16 + 8}px`;

  // Toggle arrow
  const toggle = document.createElement('span');
  toggle.className = 'tree-toggle';
  toggle.textContent = hasChildren ? '▶' : '';
  row.appendChild(toggle);

  // Type badge
  const badge = document.createElement('span');
  badge.className = `tree-type tree-type-${node.type}`;
  badge.textContent = TYPE_LABELS[node.type] ?? node.type;
  row.appendChild(badge);

  // Props
  const propsEl = document.createElement('span');
  propsEl.className = 'tree-props';
  propsEl.innerHTML = formatTreeProps(node.type, node.props);
  row.appendChild(propsEl);

  // selfRef
  const ref = document.createElement('span');
  ref.className = 'tree-ref';
  ref.textContent = node.selfRef;
  row.appendChild(ref);

  div.appendChild(row);

  // Children (depth 0 = expanded, deeper = collapsed)
  if (hasChildren) {
    const childrenEl = document.createElement('div');
    const expanded = depth === 0;
    childrenEl.hidden = !expanded;
    if (expanded) {
      toggle.textContent = '▼';
      row.classList.add('open');
    }

    for (const child of node.children) {
      childrenEl.appendChild(renderTreeNode(child, depth + 1));
    }
    div.appendChild(childrenEl);

    row.addEventListener('click', () => {
      childrenEl.hidden = !childrenEl.hidden;
      const open = !childrenEl.hidden;
      toggle.textContent = open ? '▼' : '▶';
      row.classList.toggle('open', open);
    });
  }

  return div;
}

function formatTreeProps(type, props) {
  if (!props) return '';
  const parts = [];

  if (props.level !== undefined)
    parts.push(`<b>h${props.level}</b>`);
  if (props.rows !== undefined)
    parts.push(`<b>${props.rows}×${props.cols}</b>  ${props.cells}cells`);
  if (props.enumerated !== undefined)
    parts.push(props.enumerated ? 'ordered' : 'unordered');
  if (props.language)
    parts.push(`<b>${props.language}</b>`);
  if (props.label && type === 'TextItem')
    parts.push(props.label);
  if (props.name && type === 'GroupItem')
    parts.push(`"${props.name}"`);
  if (props.groupLabel)
    parts.push(props.groupLabel);
  if (props.text !== undefined)
    parts.push(`<span class="prop-text">"${escHtml(props.text)}"</span>`);

  return parts.join('  ');
}

function escHtml(str) {
  return str.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
}

// ── Helpers ───────────────────────────────────────────────────
function showError(msg) {
  errorBar.textContent = msg;
  errorBar.hidden = false;
}

function hideError() {
  errorBar.hidden = true;
  errorBar.textContent = '';
}
