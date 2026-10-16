// Reusable version of the September 26 spotlight recording aid.
if (process.argv.includes('--help')) {
  console.log('Record a running local Playground. PLAYGROUND_URL defaults to http://127.0.0.1:50915/. Output: artifacts/playground-video/reuse/capture.');
  process.exit(0);
}
const { chromium } = require('playwright');
const fs = require('node:fs');
const path = require('node:path');
const common = require('./common.cjs');
const target = new URL(process.env.PLAYGROUND_URL || 'http://127.0.0.1:50915/');
if (!['http:', 'https:'].includes(target.protocol) ||
    !['localhost', '127.0.0.1', '[::1]'].includes(target.hostname) || target.username || target.password) {
  throw new Error('PLAYGROUND_URL must be a local HTTP(S) server without credentials.');
}
const output = common.output('capture');
const pause = ms => new Promise(resolve => setTimeout(resolve, ms));

(async () => {
  const browser = await chromium.launch({ headless:true });
  const context = await browser.newContext({ viewport:{width:1440,height:900}, locale:'en-US',
    colorScheme:'light', serviceWorkers:'block', recordVideo:{dir:path.join(output,'raw'),size:{width:1440,height:900}} });
  const page = await context.newPage();
  page.setDefaultTimeout(15000);
  const errors = [], requests = [], actions = [], captionStarts = [], zooms = [];
  let started, cursor = {x:740,y:760};
  const now = () => (Date.now()-started)/1000;
  page.on('pageerror', error => errors.push(error.message));
  await page.route('**/*', route => {
    const request = route.request();
    const url = new URL(request.url());
    if (url.pathname.startsWith('/api/')) requests.push({method:request.method(),url:url.pathname});
    return request.method() === 'GET' && url.origin === target.origin ? route.continue() : route.abort();
  });
  function chapter(label) { captionStarts.push(now()); console.log(`${now().toFixed(2)}s ${label}`); }
  function zoomStart(x,y) { zooms.push({start:now(),x,y,scale:1.3}); }
  function zoomEnd() { zooms.at(-1).end=now(); }
  async function frame(name) { await page.screenshot({path:path.join(output,name+'.png')}); }
  async function focus(selector) {
    const locator = page.locator(selector);
    await locator.scrollIntoViewIfNeeded();
    await pause(160);
    const box = await locator.boundingBox();
    if (!box || !box.width || !box.height) throw new Error('No focus target: '+selector);
    const target = {x:box.x+box.width/2,y:box.y+box.height/2};
    actions.push({kind:'focus',time:now(),selector,box});
    await page.evaluate(box => {
      const focus=document.getElementById('recording-focus');
      Object.assign(focus.style,{left:(box.x-7)+'px',top:(box.y-7)+'px',width:(box.width+14)+'px',height:(box.height+14)+'px',opacity:'1'});
    },box);
    const origin={...cursor};
    for(let i=1;i<=18;i++) {
      const t=i/18, eased=t*t*(3-2*t);
      await page.mouse.move(origin.x+(target.x-origin.x)*eased,origin.y+(target.y-origin.y)*eased);
      await pause(18);
    }
    cursor=target;
    await pause(950);
    return locator;
  }
  async function clearFocus() {
    await page.evaluate(()=>document.getElementById('recording-focus').style.opacity='0');
    await pause(300);
  }
  async function click(selector) {
    const locator=await focus(selector);
    actions.push({kind:'click',time:now(),selector,...cursor});
    await locator.click();
    await pause(250); await clearFocus(); await pause(200);
    return locator;
  }
  async function select(selector,value) {
    const locator=await focus(selector);
    actions.push({kind:'select',time:now(),selector,value,...cursor});
    await page.evaluate(({x,y})=>window.recordingRipple(x,y),cursor);
    await locator.selectOption(value);
    await pause(250); await clearFocus();
  }
  async function inspect(selector,hold) { await focus(selector); await pause(hold); await clearFocus(); }
  try {
    await page.goto(target.href,{waitUntil:'networkidle'});
    await page.waitForFunction(()=>document.querySelectorAll('.provider-group').length > 0);
    await page.locator('html[lang="en"]').waitFor();
    await page.evaluate(()=>{
      const style=document.createElement('style');
      style.textContent=`
        #recording-focus {position:fixed;left:0;top:0;box-sizing:border-box;border:2px solid #138763;border-radius:10px;box-shadow:0 0 0 3px #ffffffbb,0 0 0 9999px #092d282b;pointer-events:none;z-index:2147483644;opacity:0;transition:opacity .24s ease;}
        #recording-pointer {position:fixed;left:740px;top:760px;width:18px;height:18px;box-sizing:border-box;border:2px solid #147b60;border-radius:50%;background:#ffffffb0;box-shadow:0 1px 4px #123a2633;pointer-events:none;z-index:2147483647;transform:translate(-50%,-50%);}
        .recording-ripple {position:fixed;box-sizing:border-box;width:24px;height:24px;border:3px solid #168765;border-radius:50%;background:#2da48128;pointer-events:none;z-index:2147483646;animation:recording-pulse .8s ease-out forwards;}
        @keyframes recording-pulse {from{transform:translate(-50%,-50%) scale(.45);opacity:1}to{transform:translate(-50%,-50%) scale(2.4);opacity:0}}
      `;
      document.head.appendChild(style);
      for(const id of ['recording-focus','recording-pointer']) {const element=document.createElement('div');element.id=id;element.setAttribute('aria-hidden','true');document.body.appendChild(element);}
      document.addEventListener('mousemove',event=>{const pointer=document.getElementById('recording-pointer');pointer.style.left=event.clientX+'px';pointer.style.top=event.clientY+'px';});
      window.recordingRipple=(x,y)=>{const ripple=document.createElement('div');ripple.className='recording-ripple';ripple.style.left=x+'px';ripple.style.top=y+'px';ripple.setAttribute('aria-hidden','true');ripple.addEventListener('animationend',()=>ripple.remove(),{once:true});document.body.appendChild(ripple);};
      document.addEventListener('pointerdown',event=>window.recordingRipple(event.clientX,event.clientY));
      const leader=document.createElement('div');leader.id='recording-leader';leader.style.cssText='position:fixed;inset:0;background:#ff00ff;z-index:2147483647';document.body.appendChild(leader);
    });
    // A removable solid-color leader lets the encoder align the media timeline
    // to these timestamps within one recorded frame, without guessing startup lag.
    await pause(600);
    await page.evaluate(()=>document.getElementById('recording-leader').remove());
    started=Date.now();
    chapter('01 providers'); captionStarts[0]=0;
    await pause(4800);
    chapter('02 model name');
    await click('#model-search');
    await page.locator('#model-search').pressSequentially('gpt-6',{delay:180});
    await pause(2800);
    chapter('03 provider search');
    await click('#model-search-clear');
    await page.locator('#model-search').pressSequentially('Anthropic',{delay:140});
    await pause(2800);
    chapter('04 provider key');
    await click('.provider-group[data-provider="Anthropic"] .provider-key-btn');
    await page.locator('#apikey-modal:not(.hidden)').waitFor();
    await pause(3300);
    await click('#modal-cancel');
    await click('#model-search-clear');
    chapter('05 language');
    await select('#ui-language','ko');
    await page.locator('html[lang="ko"]').waitFor(); await pause(3000);
    await select('#ui-language','en');
    await page.locator('html[lang="en"]').waitFor(); await pause(1200);
    chapter('06 documents');
    await click('.workspace-tabs [data-open-documents]');
    await page.locator('#rag-modal:not(.hidden)').waitFor(); await pause(3000);
    chapter('07 splitting'); zoomStart(1160,350);
    await click('#rag-chunker-select .rag-select-trigger'); await pause(2300);
    await frame('splitter-menu');
    await click('#rag-chunker-select [data-value="markdown"]'); await pause(2500);
    zoomEnd(); await pause(1100); await click('#rag-modal-close');
    chapter('08 embedding');
    await click('.workspace-tabs [data-open-pipeline]');
    await page.locator('#rag-settings-modal.open').waitFor(); await pause(1800);
    zoomStart(1160,390);
    await click('#rag-embedding-trigger'); await pause(2800); await frame('embedding-menu');
    await click('#rag-embedding-menu [data-value="openai"]'); await pause(1800);
    zoomEnd(); await pause(1000);
    chapter('09 vector store');
    await click('#rag-vectorstore-select .rag-select-trigger'); await pause(2500); await frame('store-menu');
    await click('#rag-vectorstore-select [data-value="inmemory"]'); await pause(1800);
    chapter('10 hybrid search'); zoomStart(1160,630);
    await focus('#rag-hybrid-weight');
    await page.locator('#rag-hybrid-weight').focus();
    for(let i=0;i<3;i++){await page.keyboard.press('ArrowRight');await pause(220);}
    await pause(2600); await clearFocus();
    chapter('11 reranking');
    await click('label:has(#rag-rerank-enabled)'); await pause(2000);
    await inspect('#rag-rerank-provider',1800); await frame('reranking');
    chapter('12 final limits');
    await inspect('#rag-topk',1700); await inspect('#rag-min-score',1700);
    zoomEnd(); await pause(1000); await click('#rag-settings-close');
    chapter('13 closing');
    await page.mouse.move(760,760,{steps:20}); await pause(5000);
    const duration=now();
    if(errors.length) throw new Error(JSON.stringify(errors));
    if(requests.some(request=>request.method!=='GET')) throw new Error('Unexpected write request');
    fs.writeFileSync(path.join(output,'recording.json'),JSON.stringify({width:1440,height:900,duration,captionStarts,zooms,actions,requests,errors},null,2));
    await context.close();
    await page.video().saveAs(path.join(output,'walkthrough.webm'));
    console.log('Recorded '+duration.toFixed(2)+'s');
  } catch(error) {
    await frame('failure'); console.error(error); process.exitCode=1;
    await context.close();
  } finally { await browser.close(); }
})();
