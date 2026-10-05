import {spawn, execFileSync} from 'node:child_process';
import {createServer} from 'node:net';
import http from 'node:http';
import {mkdtempSync,readFileSync,writeFileSync,rmSync} from 'node:fs';
import {tmpdir} from 'node:os';
import {join,resolve,dirname} from 'node:path';
import {fileURLToPath} from 'node:url';

const repo=resolve(dirname(fileURLToPath(import.meta.url)),'../..');
const sleep=ms=>new Promise(r=>setTimeout(r,ms));
const port=()=>new Promise(r=>{const s=createServer();s.listen(0,'127.0.0.1',()=>{const n=s.address().port;s.close(()=>r(n));});});
const arms=[];
const ok=(name,pass,detail='')=>{arms.push(!!pass);console.log(`${pass?'ok':'FAIL'} ${name}${detail?': '+detail:''}`);};
let work,edge,server,ws;
try {
  work=mkdtempSync(join(tmpdir(),'spark-video-'));
  for(const [script,args] of [['build/bundle-app.ps1',['-Src',join(repo,'apps/spark/SparkStudioPage.codex'),'-Out',join(work,'page.codex')]],['codex/plugs/html/run.ps1',['-Src',join(work,'page.codex'),'-Out',join(work,'page.html')]]]) execFileSync('pwsh',['-NoProfile','-File',join(repo,script),...args],{stdio:'inherit'});
  const html=readFileSync(join(work,'page.html'),'utf8'),p=await port(),dbg=await port();
  server=http.createServer((q,r)=>{r.writeHead(200,{'Content-Type':'text/html'});r.end(html);});
  await new Promise(r=>server.listen(p,'127.0.0.1',r));
  edge=spawn('C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe',['--headless=new',`--remote-debugging-port=${dbg}`,`--user-data-dir=${join(work,'profile')}`,'--no-first-run','about:blank'],{stdio:'ignore'});
  let url;
  for(let i=0;i<80&&!url;i++){try{url=(await(await fetch(`http://127.0.0.1:${dbg}/json/list`)).json()).find(t=>t.type==='page')?.webSocketDebuggerUrl;}catch{}if(!url)await sleep(200);}
  if(!url)throw Error('no browser target');
  ws=new WebSocket(url);await new Promise((r,j)=>{ws.addEventListener('open',r);ws.addEventListener('error',j);});
  let id=0;const waiting=new Map(),errors=[];
  const send=(method,params={})=>new Promise((r,j)=>{const n=++id,t=setTimeout(()=>{waiting.delete(n);j(Error(method+' timed out'));},90000);waiting.set(n,m=>{clearTimeout(t);m.error?j(Error(JSON.stringify(m.error))):r(m.result);});ws.send(JSON.stringify({id:n,method,params}));});
  ws.addEventListener('message',e=>{const m=JSON.parse(e.data);if(m.id&&waiting.has(m.id)){waiting.get(m.id)(m);waiting.delete(m.id);}else if(m.method==='Runtime.exceptionThrown')errors.push(m.params.exceptionDetails);});
  const ev=async expression=>{const r=await send('Runtime.evaluate',{expression,awaitPromise:true,returnByValue:true,userGesture:true});if(r.exceptionDetails)throw Error(JSON.stringify(r.exceptionDetails));return r.result?.value;};
  await send('Page.enable');await send('Runtime.enable');await send('Page.navigate',{url:`http://127.0.0.1:${p}`});
  for(let i=0;i<100;i++){if(await ev('typeof _st!=="undefined"&&Number(_st.ready)===1'))break;await sleep(100);}
  await ev(`(async()=>{
    const storage=await navigator.storage.getDirectory();window.__project=await storage.getDirectoryHandle('VideoFixture',{create:true});
    window.__put=async(path,data)=>{const ps=path.split('/');let d=__project;for(const s of ps.slice(0,-1))d=await d.getDirectoryHandle(s,{create:true});const f=await d.getFileHandle(ps.at(-1),{create:true}),w=await f.createWritable();await w.write(data);await w.close();};
    window.__read=async(path)=>{const ps=path.split('/');let d=__project;for(const s of ps.slice(0,-1))d=await d.getDirectoryHandle(s);return (await(await d.getFileHandle(ps.at(-1))).getFile()).text();};
    await __put('ArtPrompts.txt','');await __put('Concept/catalog.json','[]');
    for(const [name,color] of [['red.png','#ff0000'],['blue.png','#0000ff']]){const c=document.createElement('canvas');c.width=320;c.height=180;const x=c.getContext('2d');x.fillStyle=color;x.fillRect(0,0,320,180);await __put('Concept/'+name,await new Promise(r=>c.toBlob(r)));}
    const a=new Uint8Array(44+8000*8*2),d=new DataView(a.buffer),t=(o,s)=>{for(let i=0;i<s.length;i++)a[o+i]=s.charCodeAt(i)};t(0,'RIFF');d.setUint32(4,a.length-8,true);t(8,'WAVEfmt ');d.setUint32(16,16,true);d.setUint16(20,1,true);d.setUint16(22,1,true);d.setUint32(24,8000,true);d.setUint32(28,16000,true);d.setUint16(32,2,true);d.setUint16(34,16,true);t(36,'data');d.setUint32(40,a.length-44,true);await __put('Music/silence.wav',a);
    window.showDirectoryPicker=async()=>__project;document.getElementById('open').click();return 1;
  })()`);
  const wait=async expr=>{for(let i=0;i<300;i++){if(await ev(expr))return true;await sleep(50);}return false;};
  ok('an empty project opens the composition editor',await wait('!!document.getElementById("video-add")'));
  const add=async(file,frames,fade)=>{await ev(`document.getElementById('video-image').value=${JSON.stringify(file)};document.getElementById('video-duration').value='${frames}';document.getElementById('video-fade').value='${fade}';document.getElementById('video-add').click();`);return wait(`document.getElementById('video-status').textContent==='Composition ready. Save to keep edits.'`);};
  await add('Concept/red.png',24,12);await add('Concept/blue.png',24,0);
  const seek=async frame=>ev(`(()=>{const s=document.getElementById('video-seek');s.value='${frame}';const t=performance.now();s.dispatchEvent(new Event('input',{bubbles:true}));const c=document.getElementById('video-canvas'),px=[...c.getContext('2d').getImageData(160,90,1,1).data];return {px,ms:performance.now()-t};})()`);
  const first=await seek(0),blend=await seek(18),second=await seek(24);
  const oracle=await ev(`(()=>{const c=document.createElement('canvas');c.width=1;c.height=1;const x=c.getContext('2d');x.fillStyle='#f00';x.fillRect(0,0,1,1);x.globalAlpha=0.5;x.fillStyle='#00f';x.fillRect(0,0,1,1);return [...x.getImageData(0,0,1,1).data];})()`);
  ok('clip boundaries select the correct still',first.px.join(',')==='255,0,0,255'&&second.px.join(',')==='0,0,255,255');
  ok('the suite crossfade agrees with browser Canvas within one channel level',blend.px.every((n,i)=>Math.abs(n-oracle[i])<=1),JSON.stringify({actual:blend.px,oracle,renderMs:blend.ms}));
  await ev('window.__blend=vc_blend_px;vc_blend_px=(dst,src,alpha)=>dst;');
  const sabotage=await seek(18);await ev('vc_blend_px=window.__blend;');
  ok('a disabled compositor blend fails the pixel comparison',sabotage.px.some((n,i)=>Math.abs(n-oracle[i])>1));
  await ev(`document.getElementById('video-soundtrack').value='Music/silence.wav';document.getElementById('video-soundtrack').dispatchEvent(new Event('input',{bubbles:true}));document.getElementById('video-audio').muted=true;`);
  ok('the selected soundtrack loads',await wait('document.getElementById("video-audio").readyState>=1'));
  await seek(12);const at=await ev('document.getElementById("video-audio").currentTime');
  ok('scrubbing seeks the soundtrack to the timeline time',Math.abs(at-0.5)<0.02,String(at));
  await seek(0);await ev('document.getElementById("video-play").click()');await sleep(450);await ev('document.getElementById("video-pause").click()');
  const paused=await ev('Number(_st["video-frame"])');await sleep(150);
  ok('play advances and pause cancels later ticks',paused>0&&await ev('Number(_st["video-frame"])')===paused,String(paused));
  await ev('document.getElementById("video-save").click()');await wait('document.getElementById("video-status").textContent==="Composition saved."');
  const saved=JSON.parse(await ev('__read("Video/timeline.json")'));
  ok('save writes paths, order, frame durations, crossfade and soundtrack',saved.version===1&&saved.fps===24&&saved.audio==='Music/silence.wav'&&saved.clips.map(c=>`${c.file}:${c.frames}:${c.fade}`).join(',')==='Concept/red.png:24:12,Concept/blue.png:24:0');
  await ev('document.getElementById("video-generate").click()');
  ok('generation refuses without changing the saved composition',await ev('document.getElementById("video-status").textContent.includes("no video model")')&&JSON.stringify(JSON.parse(await ev('__read("Video/timeline.json")')))===JSON.stringify(saved));
  await ev('document.getElementById("video-duration").value="1.5";document.getElementById("video-add").click()');
  ok('fractional frame input is refused without adding a clip',await ev('document.getElementById("video-status").textContent.includes("whole numbers")&&document.querySelectorAll("#video-clips li").length===2'));
  await ev('document.getElementById("open").click()');await wait('document.querySelectorAll("#video-clips li").length===2&&document.getElementById("video-status").textContent==="Composition ready. Save to keep edits."');
  const reopened=await seek(18);ok('reopening restores the composed pixels',reopened.px.every((n,i)=>Math.abs(n-oracle[i])<=1));
  await ev('document.getElementById("video-up1").click()');const moved=await seek(0);ok('Earlier moves the second still before the first',moved.px.join(',')==='0,0,255,255');
  await ev('document.getElementById("video-remove0").click()');const removed=await seek(0);ok('Remove changes the timeline without deleting its source',removed.px.join(',')==='255,0,0,255'&&await ev('(async()=>(await (await __project.getDirectoryHandle("Concept")).getFileHandle("blue.png")).name)()')==='blue.png');
  await ev('document.getElementById("video").scrollIntoView()');const screenshot=process.argv.indexOf('--screenshot');if(screenshot>=0){const shot=await send('Page.captureScreenshot',{format:'png'});writeFileSync(resolve(process.argv[screenshot+1]),Buffer.from(shot.data,'base64'));}
  await ev(`(async()=>{await __put('Video/timeline.json','{"version":99}');document.getElementById('open').click();return 1;})()`);await wait('document.getElementById("video-status").textContent.includes("Unsupported timeline")');
  ok('an unsupported project is refused without overwriting the file',await ev('__read("Video/timeline.json")')==='{"version":99}'&&await ev('!document.getElementById("video-save")'));
  const fractional={...saved,clips:[{file:'Concept/red.png',frames:1.5,fade:0}]};
  await ev(`(async()=>{await __put('Video/timeline.json',${JSON.stringify(JSON.stringify(fractional))});document.getElementById('open').click();return 1;})()`);await wait('document.getElementById("video-status").textContent.includes("Clip duration")');
  ok('fractional frame counts in stored timelines are refused',await ev('document.getElementById("video-status").textContent.includes("Clip duration")&&!document.getElementById("video-save")'));
  const once=await ev(`(()=>{const original=window.Image;let calls=0,size=0;window.Image=function(){const c=document.createElement('canvas');c.width=c.height=1;Object.defineProperty(c,'naturalWidth',{value:1});Object.defineProperty(c,'naturalHeight',{value:1});Object.defineProperty(c,'src',{set(){this.onload()}});return c;};try{image_pixels_then(Object.keys(_gfiles)[0],1n,1n,p=>{calls++;size=p.length;throw Error('fixture callback');});}catch(e){if(e.message!=='fixture callback')throw e;}finally{window.Image=original;}return calls===1&&size===1;})()`);
  ok('a throwing image callback is invoked exactly once',once);
  ok('no browser exceptions',errors.length===0,JSON.stringify(errors).slice(0,500));
}catch(e){ok('the grader ran',false,String(e.stack||e));}
finally{if(ws)ws.close();if(edge)edge.kill();if(server)server.close();if(work){try{execFileSync('pwsh',['-NoProfile','-Command',`Get-CimInstance Win32_Process -Filter "Name='msedge.exe'" | Where-Object { $_.CommandLine -like '*${work.replace(/'/g,"''")}*' } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }`]);}catch{}await sleep(500);try{rmSync(work,{recursive:true,force:true});}catch{}}}
const failed=arms.filter(x=>!x).length;console.log(failed?`FAIL: ${failed} of ${arms.length} arms`:`PASS: ${arms.length} arms`);process.exit(failed?1:0);
