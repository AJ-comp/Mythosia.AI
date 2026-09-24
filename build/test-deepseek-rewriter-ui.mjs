// Run with: node --experimental-vm-modules build/test-deepseek-rewriter-ui.mjs
// Executes real rewriter selection and persistence code without sending HTTP requests.
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import vm from 'node:vm';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const web = path.join(root, 'apps/Mythosia.AI.Samples.ChatUi/wwwroot');
const html = fs.readFileSync(path.join(web, 'index.html'), 'utf8');
const source = fs.readFileSync(path.join(web, 'js/rag-rewriter-models.js'), 'utf8');
const pipelineSource = fs.readFileSync(path.join(web, 'js/rag-pipeline.js'), 'utf8');

function element(tag) {
  const children = [];
  let selected = '';
  return {
    tag, dataset: {}, textContent: '', label: '',
    get options() { return children.flatMap(child => child.tag === 'option' ? [child] : child.options); },
    get value() { return tag === 'select' ? selected || this.options[0]?.value || '' : selected; },
    set value(value) { selected = tag !== 'select' || this.options.some(option => option.value === value) ? value : ''; },
    appendChild(child) { children.push(child); },
    replaceChildren() { children.length = 0; selected = ''; }
  };
}

const select = element('select');
const oldOption = element('option'); oldOption.value = 'DeepSeekChat'; select.appendChild(oldOption);
const stored = new Map();
const providerKeys = { DeepSeek: 'deepseek-offline', OpenAI: 'openai-offline', Perplexity: 'perplexity-offline' };
const context = vm.createContext({
  console,
  document: { createElement: element },
  localStorage: { getItem: key => stored.get(key) ?? null, setItem: (key, value) => stored.set(key, value) },
  fetch() { throw new Error('Offline tests must never make an HTTP request.'); }
});
const rewriterModule = new vm.SourceTextModule(source, { context });
await rewriterModule.link(() => { throw new Error('Unexpected dependency'); });
await rewriterModule.evaluate();
const rewriter = rewriterModule.namespace;
const groups = [
  { provider: 'OpenAI', models: [{ name: 'Gpt4oMini', description: 'gpt-4o-mini' }] },
  { provider: 'DeepSeek', models: [{ name: 'Flash', description: 'deepseek-flash' }, { name: 'V4Pro', description: 'deepseek-v4-pro' }] },
  { provider: 'Perplexity', models: [{ name: 'PerplexityDeepSeekV4Flash0731', description: 'perplexity/deepseek-v4-flash-0731' }] },
  { provider: 'Alibaba', models: [{ name: 'QwenMax', description: 'qwen-max' }] }
];
rewriter.populateRewriterModels(select, groups);
assert.equal(select.value, 'Flash', 'A selected legacy label migrates even when catalogue loading finishes later.');
assert.deepEqual(select.options.map(option => option.value), ['Gpt4oMini', 'Flash', 'V4Pro', 'PerplexityDeepSeekV4Flash0731']);
assert.equal(rewriter.getRewriterModelProvider(select, 'Flash'), 'DeepSeek');
assert.equal(rewriter.getRewriterModelProvider(select, 'V4Pro'), 'DeepSeek');
assert.equal(rewriter.getRewriterModelProvider(select, 'PerplexityDeepSeekV4Flash0731'), 'Perplexity');
rewriter.selectRewriterModel(select, 'DEEPSEEK-V4-PRO');
assert.equal(select.value, 'V4Pro');
rewriter.selectRewriterModel(select, ' DeepSeekChat ');
assert.equal(select.value, 'Flash');
rewriter.selectRewriterModel(select, 'deepseek-custom-deployment');
assert.equal(select.value, 'deepseek-custom-deployment');
rewriter.populateRewriterModels(select, groups);
assert.equal(select.value, 'deepseek-custom-deployment', 'A catalogue refresh must preserve custom model IDs.');
assert.equal(rewriter.getRewriterModelProvider(select, select.value), 'DeepSeek');
rewriter.selectRewriterModel(select, 'custom/model-name');
assert.equal(select.value, 'custom/model-name');
assert.equal(rewriter.getRewriterModelProvider(select, select.value), null);

const requested = new Map();
for (const match of pipelineSource.matchAll(/import\s*\{([^}]+)\}\s*from\s*'([^']+)'/g))
  requested.set(match[2], match[1].split(',').map(name => name.trim()));
const pipeline = new vm.SourceTextModule(pipelineSource + '\nexport { getApiKeyForRewriterModel };', { context });
await pipeline.link(name => {
  if (name === './rag-rewriter-models.js') return rewriterModule;
  const names = requested.get(name);
  return new vm.SyntheticModule(names, function () {
    for (const exportName of names) {
      let value = () => {};
      if (name === './dom.js') value = exportName === 'ragRewriterModel' ? select : null;
      if (exportName === 'providerKeys') value = providerKeys;
      if (exportName === 'ragState') value = {};
      this.setExport(exportName, value);
    }
  }, { context });
});
await pipeline.evaluate();
stored.set('rag_pipeline_settings', JSON.stringify({ rewriterModelOverride: 'DeepSeekChat', queryRewriterEnabled: true }));
assert.equal(pipeline.namespace.getCachedPipelineSettings().rewriterModelOverride, 'Flash');
assert.equal(JSON.parse(stored.get('rag_pipeline_settings')).rewriterModelOverride, 'Flash', 'Migration is persisted.');
assert.equal(pipeline.namespace.getApiKeyForRewriterModel('Flash'), providerKeys.DeepSeek);
assert.equal(pipeline.namespace.getApiKeyForRewriterModel('V4Pro'), providerKeys.DeepSeek);
assert.equal(pipeline.namespace.getApiKeyForRewriterModel('deepseek-custom-deployment'), providerKeys.DeepSeek);
assert.equal(pipeline.namespace.getApiKeyForRewriterModel('PerplexityDeepSeekV4Flash0731'), providerKeys.Perplexity);
stored.set('rag_pipeline_settings', JSON.stringify({ rewriterModelOverride: 'custom/model-name' }));
assert.equal(pipeline.namespace.getCachedPipelineSettings().rewriterModelOverride, 'custom/model-name');
const fallback = html.match(/<select id="rag-rewriter-model">([\s\S]*?)<\/select>/)[1];
assert.match(fallback, /value="Flash"/);
assert.match(fallback, /value="V4Pro"/);
assert.doesNotMatch(fallback, /value="DeepSeekChat"/);
assert.match(fs.readFileSync(path.join(web, 'js/models.js'), 'utf8'), /populateRewriterModels\(ragRewriterModel, groups\)/);
console.log('DeepSeek rewriter catalogue, provider keys, saved migration and custom model checks passed.');
