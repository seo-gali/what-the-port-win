import fs from 'node:fs/promises';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import {spawn} from 'node:child_process';
const project = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
// Reproduce GitHub project Pages, including nested 404 URLs.
const base='http://127.0.0.1:4173/what-the-port-win';
await fs.mkdir(path.join(project,'artifacts'),{recursive:true});
const profile=await fs.mkdtemp(path.join(project,'artifacts/site-browser-qa-'));
const browser=spawn('C:/Program Files/Google/Chrome/Application/chrome.exe',['--headless=new','--no-sandbox','--disable-gpu','--disable-software-rasterizer','--no-first-run','--no-default-browser-check','--remote-debugging-port=0','--user-data-dir='+profile,'about:blank'],{windowsHide:true,stdio:'ignore'});
let devtools;
for(let i=0;i<60;i++){try{const port=(await fs.readFile(path.join(profile,'DevToolsActivePort'),'utf8')).split('\n')[0].trim();devtools='http://127.0.0.1:'+port;await fetch(devtools+'/json/version');break;}catch{await new Promise(r=>setTimeout(r,250));}}
if(!devtools){browser.kill();throw new Error('Test browser did not start.');}
const target=await (await fetch(devtools+'/json/new?about:blank',{method:'PUT'})).json();
const socket=new WebSocket(target.webSocketDebuggerUrl);
await new Promise((resolve,reject)=>{socket.addEventListener('open',resolve,{once:true});socket.addEventListener('error',reject,{once:true});});
let sequence=0;const pending=new Map();
socket.addEventListener('message',event=>{const value=JSON.parse(event.data);if(!value.id)return;const item=pending.get(value.id);if(!item)return;pending.delete(value.id);value.error?item.reject(new Error(value.error.message)):item.resolve(value.result);});
function call(method,params={}){return new Promise((resolve,reject)=>{const id=++sequence;pending.set(id,{resolve,reject});socket.send(JSON.stringify({id,method,params}));});}
async function evaluate(expression){const r=await call('Runtime.evaluate',{expression,returnByValue:true,awaitPromise:true});if(r.exceptionDetails)throw new Error(JSON.stringify(r.exceptionDetails));return r.result.value;}
async function navigate(route,width,height){await call('Emulation.setDeviceMetricsOverride',{width,height,deviceScaleFactor:1,mobile:width<=430});await call('Emulation.setTouchEmulationEnabled',{enabled:width<=430});await call('Page.navigate',{url:base+route});for(let i=0;i<30;i++){await new Promise(r=>setTimeout(r,100));if(await evaluate('document.readyState === "complete" && !!document.querySelector("main")'))break;}await evaluate('document.fonts.ready');await evaluate('Promise.all([...document.images].filter(i=>i.hasAttribute("src")).map(i=>{i.loading="eager";return i.decode().catch(()=>{});})).then(()=>true)');}
async function key(key,code,vk){await call('Input.dispatchKeyEvent',{type:'keyDown',key,code,windowsVirtualKeyCode:vk,text:key==='Enter'?'\r':undefined});await call('Input.dispatchKeyEvent',{type:'keyUp',key,code,windowsVirtualKeyCode:vk});}
async function captureViewport(name){const screenshot=await call('Page.captureScreenshot',{format:'png',captureBeyondViewport:false});await fs.mkdir(path.join(project,'artifacts/site-screenshots'),{recursive:true});await fs.writeFile(path.join(project,'artifacts/site-screenshots',name+'.png'),Buffer.from(screenshot.data,'base64'));}
let passed=0;const results=[];
function check(name,ok){results.push({name,passed:!!ok});if(!ok)throw new Error('FAIL '+name);passed++;console.log('PASS '+name);}
try{
await call('Page.enable');await call('Runtime.enable');await call('Accessibility.enable');
await navigate('/',390,1000);
const hero=await evaluate('(()=>{const i=document.querySelector(".server-art");return {src:i.currentSrc,width:i.naturalWidth,height:i.naturalHeight,loading:i.loading,priority:i.fetchPriority};})()');
const heroResponse=await fetch(hero.src);const heroBytes=(await heroResponse.arrayBuffer()).byteLength;
check('hero image stays within 1 MiB with an image MIME',heroResponse.ok&&heroResponse.headers.get('content-type')?.startsWith('image/')&&heroBytes<=1024*1024);
check('hero keeps its dimensions and loads with first-screen priority',hero.width===1448&&hero.height===1086&&hero.loading!=='lazy'&&hero.priority==='high');
await call('Emulation.setEmulatedMedia',{features:[{name:'prefers-reduced-motion',value:'reduce'}]});
for(const width of [1440,1024,768,430,390,360,320])for(const route of ['/','/guide.html','/license.html','/missing/deep/page']){
  await navigate(route,width,1000);
  const info=await evaluate(`({width:innerWidth,scroll:document.documentElement.scrollWidth,body:document.body.scrollWidth,title:document.title,lang:document.documentElement.lang,main:!!document.querySelector('main'),h1:document.querySelectorAll('h1').length,description:!!document.querySelector('meta[name=description]')?.content,images:[...document.images].filter(i=>i.hasAttribute('src')).every(i=>i.hasAttribute('alt')&&i.complete&&i.naturalWidth>0)})`);
  check(route+' no horizontal overflow at '+width,info.scroll<=width&&info.body<=width);
  check(route+' semantic page and loaded alt images at '+width,info.lang==='ko'&&info.main&&info.h1===1&&info.description&&info.images&&info.title.includes('Windows'));
  await evaluate('window.scrollTo({top:600,behavior:"instant"})');
  check(route+' header follows scroll at '+width,await evaluate('Math.abs(document.querySelector(".site-header").getBoundingClientRect().top)<1'));
  await evaluate('window.scrollTo({top:0,behavior:"instant"})');
  if(width===1440||width===390){
    const layout=await call('Page.getLayoutMetrics');
    const screenshot=await call('Page.captureScreenshot',{format:'png',captureBeyondViewport:true,clip:{x:0,y:0,width,height:layout.cssContentSize.height,scale:1}});
    await fs.mkdir(path.join(project,'artifacts/site-screenshots'),{recursive:true});
    const page=route==='/'?'home':route.includes('missing')?'404':path.basename(route,'.html');
    await fs.writeFile(path.join(project,'artifacts/site-screenshots',`${page}-${width}.png`),Buffer.from(screenshot.data,'base64'));
    await captureViewport(`${page}-${width}-top`);
  }
}
for (const width of [1440,390]) {
  await navigate('/',width,900);
  check('hero has no falsely selected navigation at '+width,await evaluate('!document.querySelector("#main-nav [aria-current]")'));
  for (const id of ['preview','features','download','features','preview']) {
    await evaluate(`window.scrollTo({top:document.getElementById('${id}').getBoundingClientRect().top+scrollY-document.querySelector('.site-header').offsetHeight-24,behavior:'instant'})`);
    await evaluate('new Promise(resolve=>requestAnimationFrame(()=>requestAnimationFrame(resolve)))');
    check('scroll follows '+id+' at '+width,await evaluate(`document.querySelectorAll('#main-nav [aria-current]').length===1 && document.querySelector('#main-nav [aria-current]')?.hash==='#${id}'`));
    check('scroll keeps URL and focus at '+width,await evaluate('location.hash === "" && document.activeElement.tagName === "BODY"'));
  }
  if(width===1440)await captureViewport('home-current-section-desktop');
  if(width===390){
    await evaluate('document.querySelector(".menu-toggle").click()');
    await evaluate('new Promise(resolve=>requestAnimationFrame(()=>requestAnimationFrame(resolve)))');
    check('mobile menu exposes current section',await evaluate('document.querySelector("#main-nav").classList.contains("is-open") && document.querySelector("#main-nav [aria-current]")?.hash==="#preview"'));
    await captureViewport('home-current-section-mobile');
    await evaluate('document.querySelector("#main-nav a[href=\\"#download\\"]").click()');
    await evaluate('new Promise(resolve=>setTimeout(resolve,150))');
    check('mobile anchor closes menu and updates current section',await evaluate('!document.querySelector("#main-nav").classList.contains("is-open") && document.querySelector("#main-nav [aria-current]")?.hash==="#download" && document.getElementById("download").getBoundingClientRect().top>=document.querySelector(".site-header").getBoundingClientRect().bottom'));
  }
  await evaluate('window.scrollTo({top:0,behavior:"instant"})');
  await evaluate('new Promise(resolve=>requestAnimationFrame(()=>requestAnimationFrame(resolve)))');
  check('returning to hero clears selected navigation at '+width,await evaluate('!document.querySelector("#main-nav [aria-current]")'));
}
await navigate('/#features',1440,900);
await evaluate('new Promise(resolve=>requestAnimationFrame(()=>requestAnimationFrame(resolve)))');
check('direct anchor URL selects matching navigation',await evaluate('document.querySelector("#main-nav [aria-current]")?.hash==="#features"'));
await navigate('/',390,844);
await key('Tab','Tab',9);check('first keyboard stop is skip link',await evaluate('document.activeElement.classList.contains("skip-link")'));
await key('Enter','Enter',13);check('skip link moves to main',await evaluate('document.activeElement.id === "main"'));
await evaluate('document.querySelector(".menu-toggle").focus()');await key('Enter','Enter',13);
check('mobile menu opens with Enter',await evaluate('document.querySelector(".menu-toggle").getAttribute("aria-expanded")==="true" && getComputedStyle(document.getElementById("main-nav")).display!=="none"'));
await key('Tab','Tab',9);check('mobile nav is keyboard reachable',await evaluate('document.getElementById("main-nav").contains(document.activeElement)'));
await key('Escape','Escape',27);check('Escape closes menu and restores focus',await evaluate('document.activeElement.classList.contains("menu-toggle") && document.activeElement.getAttribute("aria-expanded")==="false"'));
await evaluate('document.getElementById("tab-list").focus()');await key('ArrowDown','ArrowDown',40);
check('arrow key selects next preview tab and panel',await evaluate('document.activeElement.id==="tab-detail" && document.getElementById("tab-detail").getAttribute("aria-selected")==="true" && !document.getElementById("panel-detail").hidden && document.getElementById("panel-list").hidden'));
await key('End','End',35);check('End selects last preview tab',await evaluate('document.activeElement.id==="tab-settings"'));
const tree=await call('Accessibility.getFullAXTree');
check('screen reader tree exposes menu and screenshot text',tree.nodes.some(n=>n.role?.value==='button'&&n.name?.value.includes('메뉴'))&&tree.nodes.some(n=>n.role?.value==='image'&&n.name?.value.includes('설정 화면')));
await navigate('/guide.html',390,844);
check('guide keeps its page navigation selected',await evaluate('document.querySelector("#main-nav [aria-current=page]")?.getAttribute("href")==="guide.html"'));
await evaluate('document.querySelector(".guide-toc a[href$=links]").click()');
await new Promise(r=>setTimeout(r,650));
check('guide anchors clear the sticky header',await evaluate('document.getElementById("links").getBoundingClientRect().top >= document.querySelector(".site-header").getBoundingClientRect().bottom'));
await captureViewport('guide-links-mobile');
check('guide selection survives scrolling',await evaluate('document.querySelector("#main-nav [aria-current=page]")?.getAttribute("href")==="guide.html"'));
await evaluate('document.querySelector("#links [data-lightbox]").focus()');await key('Enter','Enter',13);
check('mobile screenshot opens at full size with contained horizontal scrolling',await evaluate('document.querySelector("dialog").open && document.activeElement.classList.contains("lightbox-close") && document.documentElement.scrollWidth<=innerWidth'));
await evaluate('document.querySelector("dialog img").decode()');
check('enlarged screenshot keeps its readable native width',await evaluate('document.querySelector("dialog img").getBoundingClientRect().width===document.querySelector("dialog img").naturalWidth'));
await captureViewport('guide-image-mobile');
await key('Escape','Escape',27);
check('screenshot Escape restores focus to its link',await evaluate('!document.querySelector("dialog").open && document.activeElement.matches("#links [data-lightbox]")'));
await navigate('/license.html',390,844);
const legal=await fs.readFile(path.join(project,'LICENSE'),'utf8');
check('styled license preserves the complete original text',(await evaluate('document.querySelector(".license-copy").textContent')).replace(/\r\n/g,'\n').trim()===legal.replace(/\r\n/g,'\n').trim());
for(const route of ['/','/guide.html','/license.html','/404.html']){
 const response=await fetch(base+route);const html=await response.text();
 check(route+' restrictive preview headers',response.headers.get('content-security-policy')?.includes("object-src 'none'")&&response.headers.get('x-content-type-options')==='nosniff');
 const references=[...html.matchAll(/(?:href|src)="([^"]+)"/g)].map(m=>m[1]);
 for(const reference of [...new Set(references)]){
  const url=new URL(reference,base+route);if(url.origin!==new URL(base).origin)continue;
  const checkResponse=await fetch(url,{method:'HEAD'});check('local link '+route+' → '+reference,checkResponse.ok);
  if(url.hash){const body=await(await fetch(url)).text();check('anchor '+reference,body.includes('id="'+decodeURIComponent(url.hash.slice(1))+'"'));}
 }
}
check('custom error page returns HTTP 404',(await fetch(base+'/missing/page')).status===404);
check('preview rejects mutation methods',(await fetch(base+'/',{method:'POST'})).status===405);
check('encoded path traversal is blocked',(await fetch(base+'/%2e%2e%2fREADME.md')).status===403);
check('backslash traversal is blocked',(await fetch(base+'/%2e%2e%5cREADME.md')).status===403);
check('ZIP endpoint returns an attachment',(await fetch(base+'/downloads/WhatThePort-Windows-x64.zip',{method:'HEAD'})).headers.get('content-disposition')?.includes('attachment'));
const {createHash}=await import('node:crypto');
const archive=Buffer.from(await(await fetch(base+'/downloads/WhatThePort-Windows-x64.zip')).arrayBuffer());
const checksum=await(await fetch(base+'/downloads/SHA256SUMS.txt')).text();
check('download matches published SHA-256',checksum.startsWith(createHash('sha256').update(archive).digest('hex')));
const exeResponse=await fetch(base+'/downloads/WhatThePort.exe');
const executable=Buffer.from(await exeResponse.arrayBuffer());
const exeInfo=await(await fetch(base+'/downloads/EXE-INFO.json')).json();
check('EXE endpoint returns an executable attachment',exeResponse.ok&&exeResponse.headers.get('content-disposition')?.includes('WhatThePort.exe')&&exeResponse.headers.get('content-type')==='application/octet-stream'&&executable.subarray(0,2).toString()==='MZ');
check('standalone EXE matches checksum and published information',exeInfo.executable.sha256===createHash('sha256').update(executable).digest('hex')&&exeInfo.executable.bytes===executable.length&&checksum.includes(exeInfo.executable.sha256+'  WhatThePort.exe'));
await navigate('/',390,844);
check('homepage primary downloads use standalone EXE',await evaluate('[...document.querySelectorAll("main a.button[download]")].length===2 && [...document.querySelectorAll("main a.button[download]")].every(a=>a.getAttribute("href")==="downloads/WhatThePort.exe")'));
await navigate('/guide.html',390,844);
check('guide separates standalone GUI from optional CLI ZIP',await evaluate('document.querySelector("#start ol a").getAttribute("href")==="downloads/WhatThePort.exe" && !!document.querySelector("#cli a[download]")'));
const packageInfo=await(await fetch(base+'/downloads/PACKAGE-INFO.json')).json();
check('published package information matches ZIP',packageInfo.archive.sha256===createHash('sha256').update(archive).digest('hex')&&packageInfo.archive.bytes===archive.length);
check('package information uses JSON content type',(await fetch(base+'/downloads/PACKAGE-INFO.json',{method:'HEAD'})).headers.get('content-type')?.includes('application/json'));
for(const file of (await fs.readdir(path.join(project,'docs'))).filter(file=>/^google[0-9a-f]+\.html$/.test(file))){
 const verification=await fetch(base+'/'+file);
 check('Google ownership file remains exact '+file,verification.ok&&(await verification.text()).trim()==='google-site-verification: '+file);
}
check('Pages .nojekyll marker exists',(await fetch(base+'/.nojekyll')).ok);
check('local root remains previewable',(await fetch('http://127.0.0.1:4173/')).ok);
await fs.writeFile(path.join(project,'artifacts/site-checks.json'),JSON.stringify({passed,results},null,2));
console.log(`${passed} site checks passed`);
}finally{socket.close();try{await fetch(devtools+'/json/close/'+target.id);}finally{browser.kill();}}
