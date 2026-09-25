// Test the production renderer with its exact vendored sanitizer and a real DOM.
// Install test-only dependencies first: npm ci --prefix build/ui-tests --ignore-scripts
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import fs from 'node:fs';
import { createRequire } from 'node:module';
import path from 'node:path';
import vm from 'node:vm';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const require = createRequire(path.join(root, 'build/ui-tests/package.json'));
const { JSDOM } = require('jsdom');
const { Marked } = require('marked');
const web = path.join(root, 'apps/Mythosia.AI.Samples.ChatUi/wwwroot');
const source = fs.readFileSync(path.join(web, 'js/utils.js'), 'utf8');
const vendorBytes = fs.readFileSync(path.join(web, 'lib/purify.min.js'));
assert.equal(createHash('sha256').update(vendorBytes).digest('hex'),
  '2c90a9b46d6463f26038a29b686e82bc91de01fdac9d5229e7cfe3b360134ea2',
  'Upgrade DOMPurify and its provenance/integrity assertion together');
assert.match(fs.readFileSync(path.join(web, 'lib/DOMPurify.LICENSE'), 'utf8'), /Apache License/);
const html = fs.readFileSync(path.join(web, 'index.html'), 'utf8');
const sanitizerScript = html.match(/<script\b[^>]*src="\/?lib\/purify\.min\.js"[^>]*><\/script>/)?.[0];
const appScript = html.match(/<script\b[^>]*src="\/?js\/app\.js"[^>]*><\/script>/)?.[0];
assert.ok(sanitizerScript, 'The UI must load the local sanitizer');
assert.ok(appScript, 'The UI must load the app');
assert.ok(html.indexOf(sanitizerScript) < html.indexOf(appScript), 'Load the sanitizer before the app');

async function renderer({ parser = true, sanitizer = true, configure } = {}) {
  // outside-only does not execute content scripts or load subresources. The DOM
  // still parses the actual output, including malformed markup and SVG/MathML.
  const dom = new JSDOM('<!doctype html><main id="output"></main>', {
    url: 'https://chat-ui.test/', runScripts: 'outside-only'
  });
  if (parser) dom.window.marked = new Marked();
  if (sanitizer) dom.window.eval(vendorBytes.toString('utf8'));
  configure?.(dom.window);
  const module = new vm.SourceTextModule(source, { context: dom.getInternalVMContext() });
  await module.link(() => { throw new Error('Unexpected renderer import'); });
  await module.evaluate();
  return { dom, render: module.namespace.renderMarkdown, escape: module.namespace.escapeHtml };
}

const current = await renderer();
assert.equal(current.dom.window.DOMPurify.version, '3.4.16');
const target = current.dom.window.document.getElementById('output');
const hostile = [
  '<img src=x onerror="window.compromised=true"><script>window.compromised=true</script>',
  '[Unsafe link](javascript:alert%281%29)',
  '<a href="jav&#x61;script:alert(1)" onclick="alert(2)">encoded link</a>',
  '<iframe srcdoc="<script>alert(1)</script>"></iframe><object data="javascript:alert(1)"></object>',
  '<svg><g/onload=alert(1)//<p>text</p></svg>',
  '<math><mtext><table><mglyph><style><!--</style><img title="--><img src=x onerror=alert(1)>">',
  '<style>body { display:none }</style><p style="position:fixed;inset:0" data-command="clear">text</p>',
  '<form action="/api/clear" method="post"><input name="apiKey"><button>Save key</button></form>',
  '<a href="data:text/html,<script>alert(1)</script>">data link</a>',
  '<img src=x onerror=alert(1)><textarea autofocus onfocus=alert(2)>text</textarea>',
  '<a id="chat-form" name="DOMPurify">collision</a>'
];
for (const raw of hostile) {
  target.innerHTML = current.render(raw);
  assert.equal(target.querySelector('script,iframe,object,embed,svg,math,style,form,input,button,textarea,select'), null,
    `Executable or impersonating markup survived: ${raw}`);
  for (const element of target.querySelectorAll('*')) {
    for (const attribute of element.attributes) {
      assert.ok(!/^on/i.test(attribute.name), `Event handler survived: ${raw}`);
      assert.notEqual(attribute.name, 'style');
      assert.ok(!attribute.name.startsWith('data-'));
      if (['href', 'src', 'action', 'formaction', 'xlink:href'].includes(attribute.name)) {
        assert.ok(!/^(?:javascript|vbscript):/i.test(attribute.value.trim()), `Executable URL survived: ${raw}`);
      }
    }
  }
}
assert.equal(target.querySelector('[id="chat-form"],[name="DOMPurify"]'), null,
  'Model HTML must not shadow UI names/IDs');

const safe = '# Title\n\n**Strong** and [docs](https://example.com/docs).\n\n'
  + '- one\n- two\n\n| A | B |\n| - | - |\n| 1 | 2 |\n\n'
  + '```html\n<img onerror="example()">\n```';
target.innerHTML = current.render(safe);
assert.equal(target.querySelector('h1').textContent, 'Title');
assert.equal(target.querySelector('strong').textContent, 'Strong');
assert.equal(target.querySelector('a').href, 'https://example.com/docs');
assert.equal(target.querySelectorAll('li').length, 2);
assert.ok(target.querySelector('table'));
assert.ok(target.querySelector('pre code').textContent.includes('<img onerror="example()">'));
assert.equal(target.querySelector('pre img'), null, 'Code examples must remain literal');
current.dom.window.close();

const raw = '<img src=x onerror="alert(1)"> & **hello**';
// The module itself must also initialize when a CDN script is unavailable.
for (const options of [
  { parser: false },
  { sanitizer: false },
  { parser: false, sanitizer: false },
  { configure: window => { window.DOMPurify.isSupported = false; } },
  { configure: window => { window.marked.parse = () => { throw new Error('parser failed'); }; } },
  { configure: window => { window.DOMPurify.sanitize = () => { throw new Error('sanitizer failed'); }; } },
  { configure: window => { window.marked.parse = () => Promise.resolve('<img onerror="alert(1)">'); } }
]) {
  const fallback = await renderer(options);
  const result = fallback.render(raw);
  assert.equal(result, fallback.escape(raw), 'A failed renderer must never fall back to raw HTML');
  const output = fallback.dom.window.document.getElementById('output');
  output.innerHTML = result;
  assert.equal(output.querySelector('img'), null);
  assert.equal(output.textContent, raw);
  fallback.dom.window.close();
}
console.log('PASS: pinned DOMPurify, malicious Markdown/HTML, safe formatting, missing-library/error fallbacks');
