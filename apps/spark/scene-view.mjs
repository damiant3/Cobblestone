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
  work=mkdtempSync(join(tmpdir(),'spark-scene-'));
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
  ok('the project opens a scene editor',await wait('!!document.getElementById("scene-add")'));
  const set=async(id,value)=>ev('document.getElementById('+JSON.stringify(id)+').value='+JSON.stringify(String(value))+';1');
  const click=async id=>ev('document.getElementById('+JSON.stringify(id)+').click();1');
  const picture=()=>ev('document.getElementById("scene-canvas").toDataURL()');
  const apply=async(texture,x=0,y=0,z=0)=>{
    const before=await ev('Number(_st["scene-rendered"])');
    for(const [id,v] of [['scene-texture',texture],['scene-x',x],['scene-y',y],['scene-z',z]])await set(id,v);
    await click('scene-apply');
    return wait('Number(_st["scene-rendered"])>'+before);
  };
  await click('scene-add');
  await apply('Concept/red.png');
  const colors=()=>ev('(()=>{const a=document.getElementById("scene-canvas").getContext("2d").getImageData(0,0,320,240).data;let red=0,blue=0,ink=0;for(let i=0;i<a.length;i+=4){if(a[i]===255&&a[i+1]===0&&a[i+2]===0)red++;if(a[i]===0&&a[i+1]===0&&a[i+2]===255)blue++;if(a[i]!==16||a[i+1]!==26||a[i+2]!==40)ink++;}return {red,blue,ink};})()');
  const red=await colors();
  ok('a project still is mapped onto the cube',red.red>1000&&red.red===red.ink,JSON.stringify(red));
  await apply('Concept/blue.png');
  const blue=await colors();
  ok('changing the texture changes every rendered surface',blue.blue>1000&&blue.blue===blue.ink,JSON.stringify(blue));
  const original=await picture();
  await set('scene-yaw',65);await ev('document.getElementById("scene-yaw").dispatchEvent(new Event("input",{bubbles:true}));1');
  ok('orbit changes the projected geometry',await picture()!==original);
  const orbit=await picture();
  await set('scene-distance',7);await ev('document.getElementById("scene-distance").dispatchEvent(new Event("input",{bubbles:true}));1');
  ok('distance changes the projected size',await picture()!==orbit);
  const beforeMove=await picture();await apply('Concept/blue.png',1,0,0);
  ok('object position moves the mesh',await picture()!==beforeMove);
  await click('scene-save');await wait('document.getElementById("scene-status").textContent==="Scene saved."');
  const saved=JSON.parse(await ev('__read("Scene/scene.json")')),beforeOpen=await picture();
  ok('save records camera, shape, position and the relative texture path',saved.version===1&&saved.camera.yaw===65&&saved.camera.distance===7&&saved.objects.length===1&&saved.objects[0].shape==='cube'&&saved.objects[0].x===1&&saved.objects[0].texture==='Concept/blue.png');
  const seq=await ev('Number(_st["scene-rendered"])');await click('open');await wait('Number(_st["scene-rendered"])>'+seq);
  ok('reopening reproduces the same framebuffer',await picture()===beforeOpen);
  await set('scene-shape','sphere');await click('scene-add');await apply('Concept/red.png',-1,0,0);
  await set('scene-shape','plane');await click('scene-add');await apply('',0,-1,0);
  ok('cube, sphere and plane share a scene',await ev('document.querySelectorAll("#scene-objects li").length')===3&&(await colors()).red>0);
  await click('scene-generate');
  ok('generation refuses without changing the saved scene',await ev('document.getElementById("scene-status").textContent.includes("no 3D model")')&&JSON.stringify(JSON.parse(await ev('__read("Scene/scene.json")')))===JSON.stringify(saved));
  const foreign=()=>{
    const canvas=document.createElement('canvas');canvas.width=canvas.height=64;
    const gl=canvas.getContext('webgl',{antialias:false,preserveDrawingBuffer:true});
    if(!gl)throw Error('WebGL reference unavailable');
    const shader=(type,source)=>{const s=gl.createShader(type);gl.shaderSource(s,source);gl.compileShader(s);if(!gl.getShaderParameter(s,gl.COMPILE_STATUS))throw Error(gl.getShaderInfoLog(s));return s;};
    const program=gl.createProgram();
    gl.attachShader(program,shader(gl.VERTEX_SHADER,'attribute vec4 position;attribute vec2 uv;varying vec2 t;void main(){gl_Position=position;t=uv;}'));
    gl.attachShader(program,shader(gl.FRAGMENT_SHADER,'precision highp float;varying vec2 t;uniform sampler2D image;void main(){gl_FragColor=texture2D(image,t);}'));
    gl.linkProgram(program);if(!gl.getProgramParameter(program,gl.LINK_STATUS))throw Error(gl.getProgramInfoLog(program));
    gl.useProgram(program);gl.disable(gl.DITHER);gl.enable(gl.DEPTH_TEST);gl.depthFunc(gl.LESS);
    const pos=gl.getAttribLocation(program,'position'),uv=gl.getAttribLocation(program,'uv');
    const buffer=gl.createBuffer();gl.bindBuffer(gl.ARRAY_BUFFER,buffer);
    gl.enableVertexAttribArray(pos);gl.enableVertexAttribArray(uv);
    gl.vertexAttribPointer(pos,4,gl.FLOAT,false,24,0);gl.vertexAttribPointer(uv,2,gl.FLOAT,false,24,16);
    const texture=gl.createTexture();gl.bindTexture(gl.TEXTURE_2D,texture);
    gl.texParameteri(gl.TEXTURE_2D,gl.TEXTURE_MIN_FILTER,gl.NEAREST);gl.texParameteri(gl.TEXTURE_2D,gl.TEXTURE_MAG_FILTER,gl.NEAREST);
    gl.texParameteri(gl.TEXTURE_2D,gl.TEXTURE_WRAP_S,gl.CLAMP_TO_EDGE);gl.texParameteri(gl.TEXTURE_2D,gl.TEXTURE_WRAP_T,gl.CLAMP_TO_EDGE);
    const palette=[0xff0000,0x00ff00,0x0000ff,0xffff00];
    const perspective=[[-.8,-.8,0,1,0,0],[1.6,-1.6,0,2,1,0],[0,.8,0,1,.5,1]];
    const near=[[-.8,-.8,-2,1,0,0],[.8,-.8,0,1,1,0],[0,.8,0,1,.5,1]];
    const invisible=near.map(v=>[...v.slice(0,2),-2,...v.slice(3)]);
    const tri=z=>[[-.8,-.8,z,1,0,0],[.8,-.8,z,1,1,0],[0,.8,z,1,.5,1]];
    const cases=[
      ['perspective',[[perspective,palette]],true],
      ['near clipping',[[near,palette]],true],
      ['behind near plane',[[invisible,palette]],false],
      ['depth forward',[[tri(.5),[255,255,255,255]],[tri(-.5),[0xff0000,0xff0000,0xff0000,0xff0000]]],true],
      ['depth reverse',[[tri(-.5),[0xff0000,0xff0000,0xff0000,0xff0000]],[tri(.5),[255,255,255,255]]],true]
    ];
    const out=[];
    for(const [name,draws,visible] of cases){
      const rt=render_target_new(64n,64n,0n);
      gl.clearColor(0,0,0,1);gl.clearDepth(1);gl.clear(gl.COLOR_BUFFER_BIT|gl.DEPTH_BUFFER_BIT);
      for(const [verts,colors] of draws){
        const bytes=[];
        for(const i of [2,3,0,1])bytes.push(colors[i]>>16&255,colors[i]>>8&255,colors[i]&255,255);
        gl.texImage2D(gl.TEXTURE_2D,0,gl.RGBA,2,2,0,gl.RGBA,gl.UNSIGNED_BYTE,new Uint8Array(bytes));
        gl.bufferData(gl.ARRAY_BUFFER,new Float32Array(verts.flat()),gl.STATIC_DRAW);gl.drawArrays(gl.TRIANGLES,0,3);
        const xs=verts.map(v=>({tx:v[0],ty:v[1],tz:v[2],tw:v[3],tu:v[4],tv:v[5]}));
        const tex={tex_width:2n,tex_height:2n,tex_pixels:colors.map(BigInt),tex_wrap:{_tag:'WrapClamp'},tex_filter:{_tag:'FilterNearest'}};
        stx_fan(rt,stx_near(xs,xs[2],0n,[]),tex,1n);
      }
      if(gl.getError()!==gl.NO_ERROR)throw Error('WebGL reference error');
      const rgba=new Uint8Array(64*64*4);gl.readPixels(0,0,64,64,gl.RGBA,gl.UNSIGNED_BYTE,rgba);
      const pixels=[];for(let y=0;y<64;y++)for(let x=0;x<64;x++){const i=((63-y)*64+x)*4;pixels.push(rgba[i]*65536+rgba[i+1]*256+rgba[i+2]);}
      const actual=rt.rt_fb.fb_pixels.map(Number);let checked=0,bad=0,ink=0;
      for(let y=1;y<63;y++)for(let x=1;x<63;x++){const i=y*64+x,p=pixels[i];if(p)ink++;if(p&&[i-1,i+1,i-64,i+64].every(k=>pixels[k]===p)){checked++;if(actual[i]!==p)bad++;}}
      out.push({name,checked,bad,ink,pass:visible?checked>100&&bad===0:ink===0&&actual.every(p=>p===0)});
    }
    return out;
  };
  const reference=await ev('('+foreign.toString()+')()');
  for(const r of reference)ok('software texture raster agrees with WebGL: '+r.name,r.pass,JSON.stringify(r));
  await ev('window.__textureSample=texture_sample;texture_sample=()=>0n;1');
  const sabotage=await ev('('+foreign.toString()+')()');await ev('texture_sample=window.__textureSample;1');
  ok('a disabled texture sampler fails the WebGL comparison',sabotage.some(r=>r.name==='perspective'&&!r.pass));
  await ev('document.getElementById("scene").scrollIntoView();1');
  const screenshot=process.argv.indexOf('--screenshot');
  if(screenshot>=0){const clip=await ev('(()=>{const r=document.getElementById("scene").getBoundingClientRect();return {x:r.x+scrollX,y:r.y+scrollY,width:r.width,height:r.height,scale:1};})()');const shot=await send('Page.captureScreenshot',{format:'png',captureBeyondViewport:true,clip});writeFileSync(resolve(process.argv[screenshot+1]),Buffer.from(shot.data,'base64'));}
  await click('scene-remove2');ok('Remove removes only the selected scene object',await ev('document.querySelectorAll("#scene-objects li").length')===2);
  const missing={...saved,objects:[{...saved.objects[0],texture:'Concept/missing.png'}]};
  const mseq=await ev('Number(_st["scene-rendered"])');
  await ev('(async()=>{await __put("Scene/scene.json",'+JSON.stringify(JSON.stringify(missing))+');document.getElementById("open").click();return 1;})()');
  await wait('Number(_st["scene-rendered"])>'+mseq);
  await set('scene-x',2);await click('scene-apply');await click('scene-save');await wait('document.getElementById("scene-status").textContent==="Scene saved."');
  const retained=JSON.parse(await ev('__read("Scene/scene.json")'));
  ok('moving and saving an object preserves its missing texture reference',retained.objects[0].texture==='Concept/missing.png'&&retained.objects[0].x===2&&await ev('document.getElementById("scene-objects").textContent.includes("missing or unreadable")'));
  await ev('_st.writable=0n;document.getElementById("scene-save").click();1');
  ok('a read-only project refuses Save without changing the file',await ev('document.getElementById("scene-status").textContent.includes("read-only")')&&JSON.stringify(JSON.parse(await ev('__read("Scene/scene.json")')))===JSON.stringify(retained));
  await ev('(async()=>{await __put("Scene/scene.json",\'{"version":99}\');document.getElementById("open").click();return 1;})()');
  await wait('document.getElementById("scene-status").textContent.includes("Unsupported scene")');
  ok('an unsupported scene is refused without rewriting it',await ev('__read("Scene/scene.json")')==='{"version":99}'&&await ev('!document.getElementById("scene-save")'));
  await ev('(async()=>{await __put("Scene/scene.json","");document.getElementById("open").click();return 1;})()');
  await wait('document.getElementById("scene-status").textContent.includes("not valid JSON")');
  ok('an existing empty scene file is refused, not treated as a new scene',await ev('document.getElementById("scene-status").textContent.includes("not valid JSON")&&!document.getElementById("scene-save")')&&await ev('__read("Scene/scene.json")')==='');
  const badTexture={...saved,objects:[{...saved.objects[0],texture:42}]};
  await ev('(async()=>{await __put("Scene/scene.json",'+JSON.stringify(JSON.stringify(badTexture))+');document.getElementById("open").click();return 1;})()');
  await wait('document.getElementById("scene-status").textContent.includes("Texture paths must be text")');
  ok('non-text texture paths are refused',await ev('document.getElementById("scene-status").textContent.includes("Texture paths must be text")&&!document.getElementById("scene-save")'));
  ok('no browser exceptions',errors.length===0,JSON.stringify(errors).slice(0,1000));
}catch(e){ok('the grader ran',false,String(e.stack||e));}
finally{if(ws)ws.close();if(edge)edge.kill();if(server)server.close();if(work){try{execFileSync('pwsh',['-NoProfile','-Command',`Get-CimInstance Win32_Process -Filter "Name='msedge.exe'" | Where-Object { $_.CommandLine -like '*${work.replace(/'/g,"''")}*' } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }`]);}catch{}await sleep(500);try{rmSync(work,{recursive:true,force:true});}catch{}}}
const failed=arms.filter(x=>!x).length;console.log(failed?`FAIL: ${failed} of ${arms.length} arms`:`PASS: ${arms.length} arms`);process.exit(failed?1:0);
