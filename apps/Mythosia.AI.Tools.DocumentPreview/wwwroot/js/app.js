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
const markdownOutput  = document.getElementById('markdownOutput');
const markdownPreview = document.getElementById('markdownPreview');
const markdownMeta    = document.getElementById('markdownMeta');
const btnMdSource     = document.getElementById('btnMdSource');
const btnMdPreview    = document.getElementById('btnMdPreview');
const btnAlgoGrid     = document.getElementById('btnAlgoGrid');
const btnAlgoSemantic = document.getElementById('btnAlgoSemantic');
const chunksOutput   = document.getElementById('chunksOutput');
const chunksMeta     = document.getElementById('chunksMeta');
const treeOutput      = document.getElementById('treeOutput');
const treeMeta        = document.getElementById('treeMeta');
const btnCopyTree     = document.getElementById('btnCopyTree');
const semanticOutput  = document.getElementById('semanticOutput');
const semanticMeta    = document.getElementById('semanticMeta');

let selectedFile = null;
let rawTree = null;
let rawMarkdown = '';
let rawMarkdownSemantic = '';
let rawChunksGrid = [];
let rawChunksSemantic = [];
let useSemanticAlgo = false;
let currentMd = '';  // 현재 화면에 표시 중인 마크다운

btnAlgoGrid.addEventListener('click', () => {
  useSemanticAlgo = false;
  btnAlgoGrid.classList.add('active');
  btnAlgoSemantic.classList.remove('active');
  applyMarkdown(rawMarkdown);
  renderChunks(rawChunksGrid);
});

btnAlgoSemantic.addEventListener('click', () => {
  useSemanticAlgo = true;
  btnAlgoSemantic.classList.add('active');
  btnAlgoGrid.classList.remove('active');
  applyMarkdown(rawMarkdownSemantic);
  renderChunks(rawChunksSemantic);
});

function applyMarkdown(md) {
  currentMd = md;
  markdownOutput.textContent = md;
  markdownMeta.textContent = `${md.length.toLocaleString()} chars`;
  if (!markdownPreview.hidden)
    markdownPreview.innerHTML = marked.parse(md);
}

btnMdSource.addEventListener('click', () => {
  btnMdSource.classList.add('active');
  btnMdPreview.classList.remove('active');
  markdownOutput.hidden = false;
  markdownPreview.hidden = true;
});

btnMdPreview.addEventListener('click', () => {
  btnMdPreview.classList.add('active');
  btnMdSource.classList.remove('active');
  markdownOutput.hidden = true;
  markdownPreview.hidden = false;
  markdownPreview.innerHTML = marked.parse(currentMd);
});

btnCopyTree.addEventListener('click', () => {
  if (!rawTree) return;
  navigator.clipboard.writeText(JSON.stringify(rawTree, null, 2)).then(() => {
    const prev = btnCopyTree.textContent;
    btnCopyTree.textContent = '복사됨!';
    setTimeout(() => btnCopyTree.textContent = prev, 1500);
  });
});

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
  rawMarkdown = data.markdown ?? '';
  rawMarkdownSemantic = data.markdownSemantic ?? rawMarkdown;
  const activeMd = useSemanticAlgo ? rawMarkdownSemantic : rawMarkdown;
  currentMd = activeMd;
  markdownOutput.textContent = activeMd;
  markdownMeta.textContent   = `${activeMd.length.toLocaleString()} chars`;
  // Reset to source view on new result
  btnMdSource.classList.add('active');
  btnMdPreview.classList.remove('active');
  markdownOutput.hidden = false;
  markdownPreview.hidden = true;

  // Tree panel
  rawTree = data.tree;
  renderTree(data.tree);

  // Semantic tables panel
  renderSemanticTables(data.semanticTables ?? []);

  // Chunks panel
  rawChunksGrid = data.chunks ?? [];
  rawChunksSemantic = data.chunksSemantic ?? rawChunksGrid;
  renderChunks(useSemanticAlgo ? rawChunksSemantic : rawChunksGrid);

  panels.hidden = false;
}

function renderChunks(chunks) {
  const warnThreshold = parseInt(warnThresholdEl.value, 10) || 800;
  const overCount = chunks.filter(c => c.length > warnThreshold).length;

  chunksMeta.textContent = overCount > 0
    ? `${chunks.length}개 · ⚠ ${overCount}개 경고`
    : `${chunks.length}개`;

  chunksOutput.innerHTML = '';
  for (const chunk of chunks) {
    chunksOutput.appendChild(renderChunkCard(chunk, warnThreshold));
  }
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
    ${isWarn ? `<span class="chunk-warn-badge">⚠ 경고</span>` : ''}
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
  if (props.rows !== undefined) {
    const cellArr = Array.isArray(props.cells) ? props.cells : [];
    const headerCount = cellArr.filter(c => c.columnHeader).length;
    const rowHeaderCount = cellArr.filter(c => c.rowHeader).length;
    let cellInfo = `<b>${props.rows}×${props.cols}</b>  ${cellArr.length}cells`;
    if (headerCount > 0) cellInfo += `  <span style="color:#059669">colHeader:${headerCount}</span>`;
    if (rowHeaderCount > 0) cellInfo += `  <span style="color:#7c3aed">rowHeader:${rowHeaderCount}</span>`;
    parts.push(cellInfo);
  }
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

// ── Semantic Tables ───────────────────────────────────────────
function renderSemanticTables(tables) {
  semanticOutput.innerHTML = '';
  const tableCount = tables.filter(t => t.rows > 0).length;
  semanticMeta.textContent = `${tableCount}개`;

  for (const t of tables) {
    semanticOutput.appendChild(renderSemanticCard(t));
  }
}

function renderSemanticCard(t) {
  const card = document.createElement('div');
  card.className = 'sem-table-card';

  // Header row
  const header = document.createElement('div');
  header.className = 'sem-table-header';
  header.innerHTML = `
    <span class="sem-table-index">#${t.tableIndex + 1}</span>
    <span class="sem-table-dim">${t.rows}×${t.cols}</span>
    <span class="sem-badge ${t.isFormStyle ? 'sem-badge-form' : 'sem-badge-grid'}">
      ${t.isFormStyle ? 'Form' : 'Grid'}
    </span>
    <span class="sem-table-arrow">▶</span>`;

  // Body
  const body = document.createElement('div');
  body.className = 'sem-table-body';

  // Column headers
  if (t.headerRows && t.headerRows.length > 0) {
    const lbl = document.createElement('div');
    lbl.className = 'sem-section-label';
    lbl.textContent = 'Column Headers';
    body.appendChild(lbl);

    for (const row of t.headerRows) {
      const rowEl = document.createElement('div');
      rowEl.className = 'sem-header-row';
      for (const cell of row) {
        const cellEl = document.createElement('div');
        cellEl.className = 'sem-header-cell' + (cell === '' ? ' empty' : '');
        cellEl.textContent = cell || '(empty)';
        rowEl.appendChild(cellEl);
      }
      body.appendChild(rowEl);
    }
  }

  // Groups — tree style: header → data rows
  if (t.groups && t.groups.length > 0) {
    const lbl = document.createElement('div');
    lbl.className = 'sem-section-label';
    lbl.textContent = 'Groups';
    body.appendChild(lbl);

    for (const g of t.groups) {
      // Header row
      const groupEl = document.createElement('div');
      groupEl.className = 'sem-group';

      const groupLbl = document.createElement('div');
      groupLbl.className = 'sem-group-label' + (g.rowLabel ? '' : ' no-label');
      groupLbl.textContent = g.rowLabel || '(레이블 없음)';
      groupEl.appendChild(groupLbl);

      // Children (data rows) indented below header
      const childrenEl = document.createElement('div');
      childrenEl.className = 'sem-group-children';

      for (const row of g.dataRows) {
        const rowEl = document.createElement('div');
        rowEl.className = 'sem-group-row';

        const arrow = document.createElement('span');
        arrow.className = 'sem-row-arrow';
        arrow.textContent = '→';
        rowEl.appendChild(arrow);

        // Show non-empty cells joined by ·
        const cells = row.filter(c => c !== '');
        const cellEl = document.createElement('span');
        cellEl.className = 'sem-row-content';
        cellEl.textContent = cells.length > 0 ? cells.join('  ·  ') : '(empty)';
        if (cells.length === 0) cellEl.classList.add('empty');
        rowEl.appendChild(cellEl);

        childrenEl.appendChild(rowEl);
      }

      groupEl.appendChild(childrenEl);
      body.appendChild(groupEl);
    }
  }

  header.addEventListener('click', () => {
    card.classList.toggle('open');
    const open = card.classList.contains('open');
    header.querySelector('.sem-table-arrow').textContent = open ? '▼' : '▶';
  });

  card.appendChild(header);
  card.appendChild(body);
  return card;
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
