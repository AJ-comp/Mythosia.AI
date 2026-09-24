// Keep query rewriting on the same catalogue as chat, including provider key routing.
export function normalizeRewriterModel(model) {
  if (typeof model !== 'string') return model;
  return model.trim().toLowerCase() === 'deepseekchat' ? 'Flash' : model.trim();
}

export function selectRewriterModel(select, model) {
  if (!select || !model) return;
  const normalized = normalizeRewriterModel(model);
  const options = Array.from(select.options);
  const match = options.find(option => option.value === normalized ||
    option.dataset.modelId?.toLowerCase() === normalized.toLowerCase());
  if (match) {
    select.value = match.value;
    return;
  }
  // Preserve an explicitly saved custom model even when it is outside the catalogue.
  const option = document.createElement('option');
  option.value = normalized;
  option.textContent = normalized;
  select.appendChild(option);
  select.value = normalized;
}

export function populateRewriterModels(select, groups) {
  if (!select) return;
  const selected = normalizeRewriterModel(select.value);
  select.replaceChildren();
  for (const group of groups) {
    // The rewriter endpoint currently constructs adapters for these providers.
    if (!['OpenAI', 'Anthropic', 'Google', 'DeepSeek', 'xAI', 'Perplexity'].includes(group.provider)) continue;
    const optgroup = document.createElement('optgroup');
    optgroup.label = group.provider;
    for (const model of group.models) {
      const option = document.createElement('option');
      option.value = model.name;
      option.textContent = model.description;
      option.dataset.provider = group.provider;
      option.dataset.modelId = model.description;
      optgroup.appendChild(option);
    }
    select.appendChild(optgroup);
  }
  if (selected) selectRewriterModel(select, selected);
}

export function getRewriterModelProvider(select, model) {
  const normalized = normalizeRewriterModel(model);
  if (!normalized) return null;
  const entry = Array.from(select?.options || []).find(option => option.value === normalized);
  if (entry?.dataset.provider) return entry.dataset.provider;
  const id = normalized.toLowerCase();
  if (id === 'flash' || id === 'v4pro' || id.startsWith('deepseek')) return 'DeepSeek';
  if (/^(perplexity|openai\/|anthropic\/|google\/|xai\/)/.test(id)) return 'Perplexity';
  if (/^(gpt|chatgpt|o3)/.test(id)) return 'OpenAI';
  if (id.startsWith('claude')) return 'Anthropic';
  if (id.startsWith('gemini')) return 'Google';
  if (id.startsWith('grok')) return 'xAI';
  return null;
}
