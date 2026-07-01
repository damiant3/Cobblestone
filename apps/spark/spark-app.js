const VERT_COUNT_ADDR=256,CAM_STATE_ADDR=352,SCENE_ADDR=0x180000,VERT_ADDR=0x200000;
const OBJ_STRIDE=128,OUTLINER_ADDR=0x300000,OUTLINER_ENTRY=20;
const LAYOUT_ADDR=0x320000,PANEL_STRIDE=28,APP_STATE_ADDR=0x330000;

const MODE_NAMES=['MODEL','ANIMATE','RENDER','IMAGE','AUDIO','STAGE'];
const TOOL_NAMES=['Select','Move','Rotate','Scale','Extrude','Brush','Eraser','Fill','Eyedropper'];
const TYPE_NAMES=['Cube','Sphere','Pyramid','Cylinder','Cone','Plane','Torus','Custom'];
const TYPE_ICONS=['■','●','▲','▮','△','▭','◯','◈'];
const BONE_ADDR=0x5C0000,BONE_STRIDE=64,CAM_PATH_ADDR=0x5A0000;
const SPECTRUM_ADDR=0x5E0000,SYNTH_ADDR=0x5F0000,HISTOGRAM_ADDR=0x580000;
// Panel kinds: 0=viewport, 1=outliner, 2=properties, 3=timeline, 4=assets, 5=scene-settings
const PANEL_TITLES=['VIEWPORT','OUTLINER','PROPERTIES','TIMELINE','ASSETS','SCENE'];

let wasmMem=null,wasmExports={};
let angle=0,speed=1,paused=false;
let fc=0,lastT=0,fps=0,selectedObj=-1;
let dragBtn=-1,dragShift=false,dragLastX=0,dragLastY=0;
let outlinerFilter='';

function rd(dv,a){return dv.getInt32(a,true);}
function readExportText(){const len=Number(call('get_export_length',0)||0n);let s='';for(let i=0;i<len;i++)s+=String.fromCharCode(Number(call('get_export_byte',i)||0n)&0xFF);return s;}
function wasmHtml(fn,...args){call(fn,...args);return readExportText();}
function wasmExportFile(wasmFn,filename,mime){call(wasmFn,0);const bytes=readExportBytes();const blob=new Blob([bytes],{type:mime});const a=document.createElement('a');a.href=URL.createObjectURL(blob);a.download=filename;a.click();document.getElementById('st-msg').textContent='Exported '+filename+' ('+bytes.length+' bytes)';}
function readExportBytes(){const len=Number(call('get_export_length',0)||0n);const b=new Uint8Array(len);for(let i=0;i<len;i++)b[i]=Number(call('get_export_byte',i)||0n)&0xFF;return b;}
function readObjName(dv,si){const len=Number(call('get_obj_name_len',si)||0n);if(len<=0)return TYPE_NAMES[rd(dv,SCENE_ADDR+4+si*OBJ_STRIDE)]||'Object';let s='';for(let i=0;i<len;i++)s+=String.fromCharCode(Number(call('get_obj_name_char',si,i)||0n)&0xFF);return s||'Object';}
function getSelected(){const r=call('get_cam_selected',0);return r!==undefined?Number(r):-1;}
function renameObj(si,name){
  if(!wasmMem)return;
  // Write name chars to a scratch area in WASM memory, then call rename_object
  const scratch=0x700000+60000; // use end of project buffer as scratch
  const dv=new DataView(wasmMem.buffer);
  const len=Math.min(name.length,55);
  for(let i=0;i<len;i++)dv.setInt32(scratch+i*4,name.charCodeAt(i),true);
  call('rename_object',si,scratch,len);
  rebuildOutliner();
}
function importImage(){
  const inp=document.createElement('input');inp.type='file';inp.accept='image/*,.bmp,.ppm,.tga';
  inp.onchange=async()=>{
    const file=inp.files[0];if(!file||!wasmMem)return;
    const ext=file.name.split('.').pop().toLowerCase();
    if(ext==='bmp'||ext==='ppm'||ext==='tga'){
      const buf=await file.arrayBuffer();const bytes=new Uint8Array(buf);
      const dv=new DataView(wasmMem.buffer);
      const maxLen=Math.min(bytes.length,500000);
      for(let i=0;i<maxLen;i++)dv.setInt32(0x700000+4+i,bytes[i],true);
      dv.setInt32(0x700000,maxLen,true);
      let result;
      if(ext==='bmp')result=call('import_bmp',maxLen);
      else if(ext==='ppm')result=call('import_ppm',maxLen);
      else result=call('import_tga',maxLen);
      if(result!==undefined&&Number(result)>=0){document.getElementById('st-msg').textContent='Imported '+file.name;updateImageMode();}
      else document.getElementById('st-msg').textContent='Import failed (unsupported format)';
    }else{
      const img=new Image();const url=URL.createObjectURL(file);
      img.onload=()=>{
        const cv2=document.createElement('canvas');cv2.width=Math.min(img.width,1024);cv2.height=Math.min(img.height,1024);
        const ctx2=cv2.getContext('2d');ctx2.drawImage(img,0,0,cv2.width,cv2.height);
        const imgData=ctx2.getImageData(0,0,cv2.width,cv2.height);
        const dv=new DataView(wasmMem.buffer);
        for(let i=0;i<imgData.data.length;i++)dv.setInt32(0x700000+4+i,imgData.data[i],true);
        call('load_rgba_to_canvas',cv2.width,cv2.height);
        URL.revokeObjectURL(url);
        document.getElementById('st-msg').textContent='Imported '+file.name+' ('+cv2.width+'x'+cv2.height+')';
        updateImageMode();
      };img.src=url;
    }
  };inp.click();
}
function exportPPM(){wasmExportFile('export_ppm','spark-canvas.ppm','application/octet-stream');}
function exportTGA(){wasmExportFile('export_tga','spark-canvas.tga','application/octet-stream');}
function exportPNG_wasm(){wasmExportFile('export_png_canvas','spark-canvas.png','image/png');}
function exportBMP_wasm(){wasmExportFile('export_bmp_canvas','spark-canvas.bmp','image/bmp');}
function exportQOI(){wasmExportFile('export_qoi_canvas','spark-canvas.qoi','application/octet-stream');}
function exportJPEG(){wasmExportFile('export_jpeg_canvas','spark-canvas.jpg','image/jpeg');}
function exportTIFF(){wasmExportFile('export_tiff_canvas','spark-canvas.tiff','image/tiff');}
function exportWAV(){wasmExportFile('export_wav_mono','spark-audio.wav','audio/wav');}
function exportProjectJSON(){call('project_to_json',0);const t=readExportText();const b=new Blob([t],{type:'application/json'});const a=document.createElement('a');a.href=URL.createObjectURL(b);a.download='spark-project.json';a.click();document.getElementById('st-msg').textContent='Exported JSON';}
const BATCH_OPS=[
  {id:1,name:'Grayscale'},{id:2,name:'Sepia'},{id:3,name:'Blur',p1:2},{id:4,name:'Sharpen'},
  {id:5,name:'Edge Detect'},{id:6,name:'Emboss'},{id:7,name:'Threshold',p1:128},{id:8,name:'Dither'},
  {id:9,name:'Posterize',p1:4},{id:10,name:'Pixelate',p1:8},{id:11,name:'Solarize'},{id:12,name:'Vignette',p1:500,p2:300},
  {id:13,name:'Flip H'},{id:14,name:'Flip V'},{id:15,name:'Rotate 90'},
  {id:16,name:'Hue +30',p1:30,p2:1000},{id:17,name:'Red Boost',p1:30,p2:0,p3:0},
  {id:20,name:'Noise Fill',p1:0,p2:100,p3:42},{id:30,name:'Plasma',p1:42,p2:10},
];
let batchQueue=[];
function batchAdd(opId){
  const op=BATCH_OPS.find(o=>o.id===opId);if(!op)return;
  batchQueue.push(op);
  document.getElementById('st-msg').textContent='Batch: '+batchQueue.length+' ops queued';
  updateBatchUI();
}
function batchRun(){
  if(!wasmMem||batchQueue.length===0)return;
  call('batch_clear');
  batchQueue.forEach(op=>{call('batch_add_op',op.id,op.p1||0,op.p2||0,op.p3||0);});
  call('batch_run');
  document.getElementById('st-msg').textContent='Batch: '+batchQueue.length+' ops applied';
  updateImageMode();
}
function batchClear(){batchQueue=[];document.getElementById('st-msg').textContent='Batch cleared';updateBatchUI();}
function updateBatchUI(){
  const el=document.getElementById('batch-list');if(!el)return;
  el.innerHTML=batchQueue.map((op,i)=>`<span style="font-size:0.45em;color:var(--accent);margin-right:3px">${op.name}</span>`).join('')||'<span style="font-size:0.45em;color:var(--dim)">empty</span>';
}
function importOBJ(){
  const inp=document.createElement('input');inp.type='file';inp.accept='.obj';
  inp.onchange=async()=>{
    const file=inp.files[0];if(!file||!wasmMem)return;
    const text=await file.text();
    const dv=new DataView(wasmMem.buffer);
    const maxLen=Math.min(text.length,500000);
    for(let i=0;i<maxLen;i++)dv.setInt32(0x700000+4+i,text.charCodeAt(i),true);
    dv.setInt32(0x700000,maxLen,true);
    call('undo_push');
    const result=call('import_obj',maxLen);
    if(result!==undefined&&Number(result)>=0){
      document.getElementById('st-msg').textContent='Imported OBJ: '+file.name+' ('+maxLen+' bytes)';
      rebuildOutliner();updateProps();
    }else{
      document.getElementById('st-msg').textContent='Import failed (no vertices found)';
    }
  };inp.click();
}
function exportOBJ(){call('export_obj_scaled');const t=readExportText();const b=new Blob([t],{type:'text/plain'});const a=document.createElement('a');a.href=URL.createObjectURL(b);a.download='spark-export.obj';a.click();document.getElementById('st-msg').textContent='Exported OBJ';}
async function renderFrameSequence(){
  const dv=new DataView(wasmMem.buffer);
  const rangeIn=rd(dv,TL_ADDR+32),rangeOut=rd(dv,TL_ADDR+36);
  const total=rangeOut-rangeIn;
  if(total<=0){document.getElementById('st-msg').textContent='Set range first ([/])';return;}
  document.getElementById('st-msg').textContent='Rendering '+total+' frames...';
  const wasPaused=paused;paused=true;
  for(let f=rangeIn;f<=rangeOut;f++){
    call('timeline_set_frame',f);
    // Wait one frame for render
    await new Promise(r=>requestAnimationFrame(()=>requestAnimationFrame(r)));
    const c=document.getElementById('c');if(!c)break;
    const blob=await new Promise(r=>c.toBlob(r));
    const a=document.createElement('a');a.href=URL.createObjectURL(blob);
    a.download=`frame-${String(f).padStart(4,'0')}.png`;a.click();
    URL.revokeObjectURL(a.href);
    document.getElementById('st-msg').textContent=`Frame ${f-rangeIn+1}/${total+1}`;
  }
  paused=wasPaused;
  document.getElementById('st-msg').textContent='Render complete ('+total+' frames)';
}
function exportAnimData(){
  call('export_animation');const text=readExportText();
  const blob=new Blob([text],{type:'text/plain'});
  const a=document.createElement('a');a.href=URL.createObjectURL(blob);a.download='spark-animation.csv';a.click();
  document.getElementById('st-msg').textContent='Exported animation ('+len+' bytes)';
}
function screenshotViewport(){
  const c=document.getElementById('c');if(!c)return;
  c.toBlob(blob=>{if(!blob)return;const a=document.createElement('a');a.href=URL.createObjectURL(blob);a.download='spark-screenshot.png';a.click();
    document.getElementById('st-msg').textContent='Screenshot saved';
  });
}
let projectName='Untitled';
function promptProjectName(){const n=prompt('Project name:',projectName);if(n){projectName=n;document.getElementById('project-name').textContent=n;}}
function exportSTL(){call('export_stl');const t=readExportText();const b=new Blob([t],{type:'text/plain'});const a=document.createElement('a');a.href=URL.createObjectURL(b);a.download='spark-export.stl';a.click();document.getElementById('st-msg').textContent='Exported STL';}
function exportPLY(){call('export_ply');const t=readExportText();const b=new Blob([t],{type:'text/plain'});const a=document.createElement('a');a.href=URL.createObjectURL(b);a.download='spark-export.ply';a.click();document.getElementById('st-msg').textContent='Exported PLY';}
function exportDXF(){call('export_dxf');const t=readExportText();const b=new Blob([t],{type:'application/dxf'});const a=document.createElement('a');a.href=URL.createObjectURL(b);a.download='spark-export.dxf';a.click();document.getElementById('st-msg').textContent='Exported DXF';}
function importSTL(){const inp=document.createElement('input');inp.type='file';inp.accept='.stl';inp.onchange=async()=>{const f=inp.files[0];if(!f)return;const buf=await f.arrayBuffer();const dv2=new DataView(wasmMem.buffer);const bytes=new Uint8Array(buf);for(let i=0;i<bytes.length&&i<500000;i++)dv2.setInt32(0x700000+i*4,bytes[i],true);call('import_stl',bytes.length);rebuildOutliner();updateProps();document.getElementById('st-msg').textContent='Imported STL ('+bytes.length+' bytes)';};inp.click();}
function exportCanvasImage(){
  const c2d=document.getElementById('c2d');if(!c2d)return;
  c2d.toBlob(blob=>{const a=document.createElement('a');a.href=URL.createObjectURL(blob);a.download='spark-canvas.png';a.click();
    document.getElementById('st-msg').textContent='Exported PNG';
  });
}
function setBrushColor(hex){
  const r=parseInt(hex.slice(1,3),16),g=parseInt(hex.slice(3,5),16),b=parseInt(hex.slice(5,7),16);
  call('set_brush_from_rgb',r,g,b);
  updateProps();
}
function setObjColor(si,hex){const r=parseInt(hex.slice(1,3),16),g=parseInt(hex.slice(3,5),16),b=parseInt(hex.slice(5,7),16);call('set_obj_color_rgb',si,r,g,b);}
function call(name,...args){const fn=wasmExports[name];if(fn)return fn(...args.map(a=>typeof a==='number'?BigInt(a):a));}
function imgTab(t){document.querySelectorAll('.img-tab-body').forEach(e=>e.classList.remove('active'));document.querySelectorAll('.img-tab').forEach(e=>e.classList.remove('active'));const el=document.getElementById('img-t-'+t);if(el)el.classList.add('active');document.querySelectorAll('.img-tab').forEach(e=>{if(e.textContent.toLowerCase()===t)e.classList.add('active');});}
function cadTab(t){call('cad_set_tab',t);updateScenePanel();}
let sketchTool='line';
function sketchExtrude(){call('shape_build_extrude',6,1000,1000,0);}
function sketchRevolve(){call('shape_build_lathe',12,6283);}
const UNIT_NAMES=['mm','cm','in'];
function fmtDim(v){const s=Number(call('cad_unit_scale')||1000n);const u=Number(call('cad_units_mode')||0n);return (v/s).toFixed(s>1000?2:1)+' '+UNIT_NAMES[u];}
function cadSetProp(obj,field,val){call('set_object_prop',obj,field,Math.round(val));updateProps();}

// ---- Header mode tabs (generated by Codex gen-mode-tabs) ----
function buildModeTabs(){const el=document.getElementById('mode-tabs');if(el)el.innerHTML=wasmHtml('gen_mode_tabs',0);}

// ---- Command palette (Ctrl+P) ----
// Commands loaded from WASM gen_commands_json at init time
let COMMANDS=[];
function loadCommands(){try{call('gen_commands_json',0);const raw=readExportText();const parsed=JSON.parse(raw.replace(/'/g,'"').replace(/n:/g,'"name":').replace(/a:/g,'"action":'));COMMANDS=parsed.map(c=>({name:c.name,action:()=>{try{eval(c.action)}catch(e){console.warn('cmd:',e)}}}));}catch(e){console.warn('loadCommands:',e);}}
const COMMANDS_FALLBACK=[{name:'About',action:()=>showAbout()}];
function showCommandPalette(){
  let old=document.getElementById('cmd-palette');if(old){old.remove();return;}
  const pal=document.createElement('div');pal.id='cmd-palette';
  pal.style.cssText='position:fixed;top:60px;left:50%;transform:translateX(-50%);width:400px;max-height:400px;background:var(--surface);border:1px solid var(--accent);border-radius:8px;z-index:2000;box-shadow:0 8px 32px rgba(0,0,0,0.6);overflow:hidden;display:flex;flex-direction:column';
  const input=document.createElement('input');input.className='search-input';input.style.cssText='margin:8px;font-size:0.7em;padding:6px 10px';input.placeholder='Type a command...';
  const list=document.createElement('div');list.style.cssText='overflow-y:auto;max-height:340px';
  function render(filter){
    list.innerHTML='';const q=filter.toLowerCase();
    COMMANDS.filter(c=>!q||c.name.toLowerCase().includes(q)).forEach(c=>{
      const item=document.createElement('div');
      item.style.cssText='padding:6px 12px;font-size:0.62em;cursor:pointer;color:var(--text)';
      item.textContent=c.name;
      item.onmouseover=()=>item.style.background='rgba(0,212,255,0.08)';
      item.onmouseout=()=>item.style.background='transparent';
      item.onclick=()=>{c.action();pal.remove();};
      list.appendChild(item);
    });
  }
  input.oninput=()=>render(input.value);
  input.onkeydown=e=>{if(e.key==='Escape')pal.remove();};
  pal.appendChild(input);pal.appendChild(list);
  document.body.appendChild(pal);input.focus();render('');
  const dismiss=e=>{if(!pal.contains(e.target)){pal.remove();document.removeEventListener('mousedown',dismiss);}};
  setTimeout(()=>document.addEventListener('mousedown',dismiss),0);
}

// ---- Image tool shortcuts ----
function setImgTool(t){call('set_active_tool',t);updateScenePanel();updateProps();}

// ---- About dialog ----
function showAbout(){
  let old=document.getElementById('about-dlg');if(old){old.remove();return;}
  const dlg=document.createElement('div');dlg.id='about-dlg';
  dlg.style.cssText='position:fixed;top:50%;left:50%;transform:translate(-50%,-50%);background:var(--surface);border:1px solid var(--border);border-radius:8px;padding:24px 32px;z-index:2000;box-shadow:0 8px 32px rgba(0,0,0,0.6);max-width:400px;text-align:center';
  dlg.innerHTML=`<div style="font-size:1.2em;font-weight:300;letter-spacing:0.2em;background:linear-gradient(135deg,var(--accent),var(--accent2));-webkit-background-clip:text;-webkit-text-fill-color:transparent;margin-bottom:12px">CODEX SPARK</div>
    <div style="font-size:0.6em;color:var(--dim);line-height:1.6">
    Creative suite built in Codex, running as WASM + WebGPU.<br><br>
    85+ Codex modules | 160+ WASM exports<br>
    7 primitives + CSG booleans + procedural shapes<br>
    6 editor modes | PBR rendering | Armature system<br>
    Timeline + camera paths | Noise textures | Synth + spectrum<br><br>
    <span style="opacity:0.5">codex-lang.org</span>
    </div>
    <button class="scene-btn" style="margin-top:16px;padding:6px 20px" onclick="document.getElementById('about-dlg').remove()">Close</button>`;
  document.body.appendChild(dlg);
}

// ---- Keyboard shortcuts help ----
function showShortcuts(){
  let old=document.getElementById('shortcuts-dlg');if(old){old.remove();return;}
  const dlg=document.createElement('div');dlg.id='shortcuts-dlg';
  dlg.style.cssText='position:fixed;top:50%;left:50%;transform:translate(-50%,-50%);background:var(--surface);border:1px solid var(--border);border-radius:8px;padding:20px 28px;z-index:2000;box-shadow:0 8px 32px rgba(0,0,0,0.6);max-width:500px;max-height:80vh;overflow-y:auto';
  const shortcuts=[
    ['F','Focus selected'],['D','Duplicate'],['Del','Delete'],['K','Keyframe all channels'],
    ['W','Toggle wireframe'],['G','Toggle grid'],['H','Hide/show selected'],['Alt+H','Show all'],
    ['L','Toggle lock'],['B','Brush (Image mode)'],['E','Eraser'],['I','Eyedropper'],
    ['1','Camera: Front'],['3','Camera: Right'],['7','Camera: Top'],['0','Camera: Perspective'],
    ['Ctrl+P','Command palette'],['Ctrl+S','Save project'],['Ctrl+O','Load project'],['Ctrl+Z','Undo'],
    ['Space','Pause/resume'],['+ / -','Speed up/down'],['Enter','Fill selection (Image)'],['Esc','Clear selection / close dialog'],
    ['Shift+Click','Multi-select'],['Shift+Drag','Pan camera / selection rect (Image)'],
    ['Ctrl+1-4','Save camera bookmark'],['Alt+1-4','Load camera bookmark'],
    ['Ctrl+C','Copy object'],['Ctrl+V','Paste object'],
    ['X','Toggle X-Ray'],['N','Toggle normals display'],
    ['?','About'],
  ];
  let h='<div style="font-size:0.8em;color:var(--accent);margin-bottom:12px;letter-spacing:0.1em">KEYBOARD SHORTCUTS</div>';
  shortcuts.forEach(([key,desc])=>{h+=`<div style="display:flex;justify-content:space-between;padding:2px 0;font-size:0.58em"><span style="color:var(--accent);min-width:90px">${key}</span><span style="color:var(--dim)">${desc}</span></div>`;});
  h+='<button class="scene-btn" style="margin-top:12px;padding:6px 16px" onclick="document.getElementById(\'shortcuts-dlg\').remove()">Close</button>';
  dlg.innerHTML=h;document.body.appendChild(dlg);
}

// ---- Histogram viewer ----
function showHistogram(){
  if(!wasmMem)return;
  let old=document.getElementById('hist-dlg');if(old){old.remove();return;}
  const dlg=document.createElement('div');dlg.id='hist-dlg';
  dlg.style.cssText='position:fixed;top:50%;left:50%;transform:translate(-50%,-50%);background:var(--surface);border:1px solid var(--border);border-radius:8px;padding:16px 20px;z-index:2000;box-shadow:0 8px 32px rgba(0,0,0,0.6)';
  const cv=document.createElement('canvas');cv.width=256;cv.height=100;cv.style.cssText='background:#000;border:1px solid var(--border);border-radius:4px';
  const dv=new DataView(wasmMem.buffer);
  let maxV=1;for(let i=0;i<256;i++){const v=dv.getInt32(HISTOGRAM_ADDR+i*4,true);if(v>maxV)maxV=v;}
  const ctx2=cv.getContext('2d');ctx2.fillStyle='#111';ctx2.fillRect(0,0,256,100);
  ctx2.fillStyle='#00d4ff';
  for(let i=0;i<256;i++){const v=dv.getInt32(HISTOGRAM_ADDR+i*4,true);const h=v*98/maxV;ctx2.fillRect(i,100-h,1,h);}
  dlg.innerHTML='<div style="font-size:0.7em;color:var(--accent);margin-bottom:8px;letter-spacing:0.1em">HISTOGRAM</div>';
  dlg.appendChild(cv);
  dlg.innerHTML+='<div style="display:flex;gap:4px;margin-top:8px"><button class="scene-btn" onclick="call(\'apply_levels\',0,200,100);updateImageMode();document.getElementById(\'hist-dlg\').remove()">Darken</button><button class="scene-btn" onclick="call(\'apply_levels\',50,255,100);updateImageMode();document.getElementById(\'hist-dlg\').remove()">Brighten</button><button class="scene-btn" onclick="document.getElementById(\'hist-dlg\').remove()">Close</button></div>';
  document.body.appendChild(dlg);
}

// ---- Spectrum viewer ----
function showSpectrum(){
  if(!wasmMem)return;
  let old=document.getElementById('spec-dlg');if(old)old.remove();
  const dlg=document.createElement('div');dlg.id='spec-dlg';
  dlg.style.cssText='position:fixed;bottom:80px;right:20px;background:var(--surface);border:1px solid var(--border);border-radius:8px;padding:12px 16px;z-index:1500;box-shadow:0 4px 16px rgba(0,0,0,0.4)';
  const cv=document.createElement('canvas');cv.width=320;cv.height=80;cv.style.cssText='background:#000;border:1px solid var(--border);border-radius:4px';
  const dv=new DataView(wasmMem.buffer);
  const count=dv.getInt32(SPECTRUM_ADDR,true);
  const ctx2=cv.getContext('2d');ctx2.fillStyle='#080818';ctx2.fillRect(0,0,320,80);
  const bw=Math.max(1,Math.floor(300/Math.max(count,1)));
  for(let i=0;i<count;i++){
    const mag=dv.getInt32(SPECTRUM_ADDR+4+i*8+4,true);
    const h2=mag*76/1000;
    const hue=Math.round(i*270/count);
    ctx2.fillStyle=`hsl(${hue},80%,60%)`;
    ctx2.fillRect(10+i*bw,80-h2,Math.max(bw-1,1),h2);
  }
  dlg.innerHTML='<div style="font-size:0.6em;color:var(--accent);margin-bottom:6px;letter-spacing:0.1em">SPECTRUM</div>';
  dlg.appendChild(cv);
  dlg.innerHTML+='<button class="scene-btn" style="margin-top:6px" onclick="document.getElementById(\'spec-dlg\').remove()">Close</button>';
  document.body.appendChild(dlg);
}

// ---- Audio synth preview ----
let audioCtx=null, audioWave='sine';
function playNote(midiNote){const freq=Number(call('get_note_freq',midiNote)||440n);playTestTone(freq,0.5);call('audio_gen_tone',freq,Number(call('get_synth_val',24)||0n),500,44100);document.getElementById('st-msg').textContent='Note '+midiNote;}
function showWaveform(){if(!wasmMem)return;let old=document.getElementById('waveform-dlg');if(old)old.remove();const dlg=document.createElement('div');dlg.id='waveform-dlg';dlg.style.cssText='position:fixed;bottom:80px;left:20px;background:var(--surface);border:1px solid var(--border);border-radius:8px;padding:12px 16px;z-index:1500';const cv=document.createElement('canvas');cv.width=512;cv.height=100;cv.style.cssText='background:#000;border:1px solid var(--border);border-radius:4px';const count=Number(call('get_waveform_count',0)||0n);const ctx2=cv.getContext('2d');ctx2.fillStyle='#080818';ctx2.fillRect(0,0,512,100);ctx2.strokeStyle='#00d4ff';ctx2.lineWidth=1;ctx2.beginPath();for(let i=0;i<Math.min(count,512);i++){const s=Number(call('get_waveform_sample',i)||0n);const y=50-s*48/16000;if(i===0)ctx2.moveTo(i,y);else ctx2.lineTo(i,y);}ctx2.stroke();dlg.innerHTML='<div style="font-size:0.6em;color:var(--accent);margin-bottom:6px">WAVEFORM ('+count+' samples)</div>';dlg.appendChild(cv);dlg.innerHTML+='<button class="scene-btn" style="margin-top:6px" onclick="document.getElementById(\'waveform-dlg\').remove()">Close</button>';document.body.appendChild(dlg);}
function playTestTone(freq,dur){
  if(!audioCtx)audioCtx=new AudioContext();
  const osc=audioCtx.createOscillator();const gain=audioCtx.createGain();
  osc.type=audioWave;osc.frequency.value=freq;
  let vol=0.3,atk=0.05,dec=0.2,sus=0.7,rel=0.3;
  if(wasmMem){const sdv=new DataView(wasmMem.buffer);
    vol=rd(sdv,SYNTH_ADDR+20)/1000;atk=rd(sdv,SYNTH_ADDR+4)/1000;
    dec=rd(sdv,SYNTH_ADDR+8)/1000;sus=rd(sdv,SYNTH_ADDR+12)/1000;
    rel=rd(sdv,SYNTH_ADDR+16)/1000;
  }
  const t=audioCtx.currentTime;
  gain.gain.setValueAtTime(0,t);
  gain.gain.linearRampToValueAtTime(vol,t+atk);
  gain.gain.linearRampToValueAtTime(vol*sus,t+atk+dec);
  gain.gain.setValueAtTime(vol*sus,t+dur-rel);
  gain.gain.exponentialRampToValueAtTime(0.001,t+dur);
  osc.connect(gain);gain.connect(audioCtx.destination);
  osc.start();osc.stop(t+dur);
}

// ---- Scene presets ----
function loadScenePreset(name){
  call('undo_push');
  call('scene_new');
  if(name==='basic'){
    call('add_scene_object',0);call('add_scene_object',1);call('add_scene_object',5);
    call('set_object_prop',2,1,-500);call('set_object_prop',2,5,6000);
  }else if(name==='gallery'){
    for(let i=0;i<7;i++){call('add_scene_object',i);call('set_object_prop',i,0,(i-3)*2200);}
  }
  rebuildOutliner();
}

// ---- Context menu ----
function showContextMenu(x,y,idx){
  let old=document.getElementById('ctx-menu');if(old)old.remove();
  const menu=document.createElement('div');menu.id='ctx-menu';
  menu.style.cssText=`position:fixed;left:${x}px;top:${y}px;background:var(--surface);border:1px solid var(--border);border-radius:4px;padding:4px 0;z-index:1000;min-width:140px;box-shadow:0 4px 16px rgba(0,0,0,0.5)`;
  const items=[
    {label:'Focus',action:()=>call('focus_selected')},
    {label:'Duplicate',action:()=>{call('undo_push');call('duplicate_selected');rebuildOutliner();}},
    {label:'Snap to Grid',action:()=>{call('snap_selected_to_grid');updateProps();}},
    {label:'Toggle Lock',action:()=>{call('toggle_object_lock',idx);updateProps();}},
    {label:'Hide',action:()=>toggleVis(idx)},
    {label:'Isolate',action:()=>{call('isolate_selected');rebuildOutliner();}},
    {label:'Rotate +90°',action:()=>{call('undo_push');call('rotate_by',idx,90);updateProps();}},
    {label:'Copy Transform',action:()=>{call('copy_transform',idx);document.getElementById('st-msg').textContent='Copied';}},
    {label:'Paste Transform',action:()=>{call('undo_push');call('paste_transform',idx);updateProps();}},
    {label:'Flatten (zero rotation+anim)',action:()=>{call('undo_push');call('flatten_selected');updateProps();}},
    {label:'Reset Transform',action:()=>{call('undo_push');call('reset_transform',idx);updateProps();}},
    {label:'Center Pivot',action:()=>{call('undo_push');call('center_pivot',idx);updateProps();}},
    {label:'Create Instance',action:()=>{call('create_instance',idx);rebuildOutliner();}},
    {label:'Mirror X',action:()=>{call('undo_push');call('mirror_object',idx,0);updateProps();}},
    {label:'Radial (6x)',action:()=>{call('undo_push');call('radial_duplicate',6,3000);rebuildOutliner();}},
    {label:'Random Color',action:()=>{call('random_color',Date.now()&0x7FFFFFFF);updateProps();}},
    {label:'Group',action:()=>{call('undo_push');call('group_selected');rebuildOutliner();}},
    {label:'Delete',action:()=>{call('undo_push');call('delete_scene_object',idx);rebuildOutliner();},warn:true},
  ];
  items.forEach(it=>{
    const btn=document.createElement('div');
    btn.style.cssText=`padding:4px 12px;font-size:0.58em;cursor:pointer;color:${it.warn?'var(--warn)':'var(--text)'}`;
    btn.textContent=it.label;
    btn.onmouseover=()=>btn.style.background='rgba(255,255,255,0.05)';
    btn.onmouseout=()=>btn.style.background='transparent';
    btn.onclick=()=>{it.action();menu.remove();};
    menu.appendChild(btn);
  });
  document.body.appendChild(menu);
  const dismiss=()=>{menu.remove();document.removeEventListener('click',dismiss);};
  setTimeout(()=>document.addEventListener('click',dismiss),0);
}

// ---- Image mode ----
const CANVAS_BUF=0x400000, CANVAS_STATE=0x500000;
let painting=false,lastPaintX=-1,lastPaintY=-1;
let canvasZoom=1,canvasPanX=0,canvasPanY=0;
function updateImageMode(){
  const c3d=document.getElementById('c'),c2d=document.getElementById('c2d');
  if(!c3d||!c2d||!wasmMem)return;
  const dv=new DataView(wasmMem.buffer);
  const mode=rd(dv,APP_STATE_ADDR);
  if(mode===3){ // IMAGE mode
    c3d.style.display='none';c2d.style.display='block';
    const cw=rd(dv,CANVAS_STATE),ch=rd(dv,CANVAS_STATE+4);
    c2d.width=cw;c2d.height=ch;
    c2d.style.width=Math.round(cw*canvasZoom)+'px';
    c2d.style.height=Math.round(ch*canvasZoom)+'px';
    c2d.style.imageRendering='pixelated';
    c2d.style.margin='auto';c2d.style.display='block';
    const ctx2=c2d.getContext('2d');
    const imgData=ctx2.createImageData(cw,ch);
    for(let i=0;i<cw*ch;i++){
      const px=rd(dv,CANVAS_BUF+i*4);
      imgData.data[i*4]=(px>>16)&0xFF;
      imgData.data[i*4+1]=(px>>8)&0xFF;
      imgData.data[i*4+2]=px&0xFF;
      imgData.data[i*4+3]=255;
    }
    ctx2.putImageData(imgData,0,0);
    // Draw selection rect if active
    const selActive=rd(dv,CANVAS_STATE+48);
    if(selActive){
      const sx0=rd(dv,CANVAS_STATE+52),sy0=rd(dv,CANVAS_STATE+56);
      const sx1=rd(dv,CANVAS_STATE+60),sy1=rd(dv,CANVAS_STATE+64);
      ctx2.strokeStyle='#00d4ff';ctx2.lineWidth=1;ctx2.setLineDash([4,4]);
      ctx2.strokeRect(Math.min(sx0,sx1),Math.min(sy0,sy1),Math.abs(sx1-sx0),Math.abs(sy1-sy0));
      ctx2.setLineDash([]);
    }
  }else{
    c3d.style.display='block';c2d.style.display='none';
  }
}
document.addEventListener('pointerdown',e=>{
  if(e.target.id==='c2d'){
    if(e.shiftKey){
      // Selection rect mode
      const c2d=document.getElementById('c2d');const rect=c2d.getBoundingClientRect();
      const dv=new DataView(wasmMem.buffer);
      const cw=rd(dv,CANVAS_STATE),ch=rd(dv,CANVAS_STATE+4);
      const x=Math.floor((e.clientX-rect.left)/rect.width*cw);
      const y=Math.floor((e.clientY-rect.top)/rect.height*ch);
      call('canvas_sel_start',x,y);
      painting='select';
    } else {
      painting=true;paintAt(e);
    }
    e.preventDefault();
  }
});
document.addEventListener('pointermove',e=>{
  if(painting==='select'&&e.target.id==='c2d'){
    const c2d=document.getElementById('c2d');const rect=c2d.getBoundingClientRect();
    const dv=new DataView(wasmMem.buffer);
    const cw=rd(dv,CANVAS_STATE),ch=rd(dv,CANVAS_STATE+4);
    const x=Math.floor((e.clientX-rect.left)/rect.width*cw);
    const y=Math.floor((e.clientY-rect.top)/rect.height*ch);
    call('canvas_sel_update',x,y);updateImageMode();
  } else if(painting&&e.target.id==='c2d')paintAt(e);
});
document.addEventListener('pointerup',e=>{if(painting){painting=false;lastPaintX=-1;lastPaintY=-1;}});
function paintAt(e){
  const c2d=document.getElementById('c2d');if(!c2d||!wasmMem)return;
  const rect=c2d.getBoundingClientRect();
  const dv=new DataView(wasmMem.buffer);
  const cw=rd(dv,CANVAS_STATE),ch=rd(dv,CANVAS_STATE+4);
  const x=Math.floor((e.clientX-rect.left)/rect.width*cw);
  const y=Math.floor((e.clientY-rect.top)/rect.height*ch);
  const tool=rd(dv,CANVAS_STATE+28);
  if(tool===3){call('canvas_eyedrop',x,y);updateProps();}
  else if(tool===2){call('canvas_flood_fill',x,y);updateImageMode();}
  else if(tool===1){
    if(lastPaintX>=0){for(let si2=0;si2<=Math.max(Math.abs(x-lastPaintX),Math.abs(y-lastPaintY));si2++){const t2=si2/(Math.max(Math.abs(x-lastPaintX),Math.abs(y-lastPaintY))||1);call('canvas_erase',Math.round(lastPaintX+(x-lastPaintX)*t2),Math.round(lastPaintY+(y-lastPaintY)*t2));}}
    else call('canvas_erase',x,y);
    updateImageMode();
  }
  else if(lastPaintX>=0){call('canvas_stroke',lastPaintX,lastPaintY,x,y);updateImageMode();}
  else{call('canvas_paint',x,y);updateImageMode();}
  lastPaintX=x;lastPaintY=y;
}

// ---- Panels from WASM layout ----
function buildPanels(){
  if(!wasmMem)return;
  const dv=new DataView(wasmMem.buffer);
  const count=rd(dv,LAYOUT_ADDR);
  const container=document.getElementById('panels');
  container.innerHTML='';
  for(let i=0;i<count;i++){
    const base=LAYOUT_ADDR+4+i*PANEL_STRIDE;
    const kind=rd(dv,base),x=rd(dv,base+4),y=rd(dv,base+8),w=rd(dv,base+12),h=rd(dv,base+16);
    if(w<=0||h<=0)continue;
    const panel=document.createElement('div');
    panel.className='panel';panel.id='panel-'+i;panel.dataset.kind=kind;
    panel.style.cssText=`left:${x}px;top:${y}px;width:${w}px;height:${h}px`;
    const head=document.createElement('div');head.className='panel-head';head.textContent=PANEL_TITLES[kind]||'PANEL';
    const body=document.createElement('div');body.className='panel-body';body.id='pbody-'+i;
    panel.appendChild(head);panel.appendChild(body);

    if(kind===0){ // Viewport
      body.className+=' vp-body';
      body.innerHTML='<div class="toolbar" id="toolbar"></div><canvas id="c"></canvas><canvas id="c2d" style="display:none;position:absolute;top:24px;left:0;cursor:crosshair"></canvas><div id="error"></div><div id="vp-overlay" style="position:absolute;top:28px;left:8px;font-size:0.5em;color:var(--dim);opacity:0.5;pointer-events:none;line-height:1.4"></div><div class="vp-hints">orbit: right-drag | zoom: scroll | pan: shift+drag<br>F focus | D dup | Del delete | K key | R random color<br>W wire | G grid | H hide | L lock | ? shortcuts<br>Ctrl+P palette | Ctrl+1-4 save cam | Alt+1-4 load cam</div>';
      body.style.position='relative';
    } else if(kind===1){ // Outliner
      body.innerHTML='<div class="search-box"><input class="search-input" placeholder="Search objects..." oninput="outlinerFilter=this.value.toLowerCase();rebuildOutliner()"></div><div class="ol-list" id="outliner-list"></div>';
    } else if(kind===2){ // Properties
      body.id='props';
    } else if(kind===3){ // Timeline
      body.innerHTML=buildTimelinePanel();body.id='timeline-panel';
    } else if(kind===5){ // Scene settings (right panel)
      body.innerHTML=buildScenePanel();body.id='scene-panel';
    }
    container.appendChild(panel);
  }
}

function buildScenePanel(){
  const dv=new DataView(wasmMem.buffer);
  const mode=rd(dv,APP_STATE_ADDR);
  const rm=rd(dv,APP_STATE_ADDR+8),grid=rd(dv,APP_STATE_ADDR+12);
  let h='';

  // Mode-specific content
  if(mode===3){ // IMAGE
    h+=wasmHtml('gen_image_panel',0);
  } else if(mode===4){ // AUDIO
    h+=wasmHtml('gen_audio_full',0);
  } else if(mode===5){ // STAGE
    h+=wasmHtml('gen_stage_panel',0);
  } else if(mode===2){ // RENDER
    h+=wasmHtml('gen_render_panel',0);
  } else { // MODEL, ANIMATE
    h+=wasmHtml('gen_cad_panel',0);
    h+=wasmHtml('gen_material_panel',0);
    h+=wasmHtml('gen_light_panel',0);
    h+=wasmHtml('gen_uv_panel',0);
  }

  if(mode===1) h+=wasmHtml('gen_animate_panel',0);
  h+=wasmHtml('gen_common_controls',0);
  h+=wasmHtml('gen_export_buttons',0);
  return h;
}
function updateScenePanel(){const el=document.getElementById('scene-panel');if(el)el.innerHTML=buildScenePanel();}

// ---- Timeline panel (generated by Codex gen-timeline-panel) ----
const TL_ADDR=0x350000;
function buildTimelinePanel(){
  if(!wasmMem)return'<div class="timeline-ruler">Timeline</div>';
  return wasmHtml('gen_timeline_panel',0);
}
function updateTimeline(){const el=document.getElementById('timeline-panel');if(el)el.innerHTML=buildTimelinePanel();}
function scrubTimeline(e){
  const ruler=document.getElementById('tl-ruler');if(!ruler||!wasmMem)return;
  const rect=ruler.getBoundingClientRect();const pct=(e.clientX-rect.left)/rect.width;
  const dv=new DataView(wasmMem.buffer);
  const fs=rd(dv,TL_ADDR+4),fe=rd(dv,TL_ADDR+8);
  const frame=Math.round(fs+pct*(fe-fs));
  call('timeline_set_frame',frame);updateTimeline();
}
const CHAN_NAMES=['Pos X','Pos Y','Pos Z','Rot Y','Scale'];
const CHAN_OFFSETS=[20,24,28,32,40]; // legacy; prefer get_obj_channel_val
function addKeyframeAtPlayhead(chan){
  const si=getSelected();if(si<0||!wasmMem)return;
  const dv=new DataView(wasmMem.buffer);
  const frame=rd(dv,TL_ADDR);
  if(chan===undefined){
    // Key all channels
    for(let c=0;c<5;c++){const val=Number(call('get_obj_channel_val',si,c)||0n);call('timeline_add_keyframe',si,c,frame,val);}
  }else{
    const val=Number(call('get_obj_channel_val',si,chan)||0n);
    call('timeline_add_keyframe',si,chan,frame,val);
  }
  updateTimeline();
}
function deleteKeyframeAtPlayhead(chan){
  const si=getSelected();if(si<0||!wasmMem)return;
  const dv=new DataView(wasmMem.buffer);
  const frame=rd(dv,TL_ADDR);
  if(chan===undefined){for(let c=0;c<5;c++)call('timeline_delete_keyframe',si,c,frame);}
  else call('timeline_delete_keyframe',si,chan,frame);
  updateTimeline();
}

// ---- Project save/load via IndexedDB ----
const PROJECT_BUF=0x700000;
async function saveProject(){
  call('undo_push'); call('project_serialize');
  if(!wasmMem)return;
  const dv=new DataView(wasmMem.buffer);
  const len=rd(dv,PROJECT_BUF);
  let text='';for(let i=0;i<len;i++)text+=String.fromCharCode(rd(dv,PROJECT_BUF+4+i)&0xFF);
  try{
    const db=await openDB();const tx=db.transaction('projects','readwrite');
    tx.objectStore('projects').put({id:'current',data:text,saved:Date.now()});
    await tx.complete;
    document.getElementById('st-msg').textContent='Project saved';
  }catch(e){
    const blob=new Blob([text],{type:'text/plain'});
    const a=document.createElement('a');a.href=URL.createObjectURL(blob);a.download='spark-project.txt';a.click();
    document.getElementById('st-msg').textContent='Downloaded as file';
  }
}
async function loadProject(){
  try{
    const db=await openDB();const tx=db.transaction('projects','readonly');
    const req=tx.objectStore('projects').get('current');
    req.onsuccess=()=>{
      if(!req.result){document.getElementById('st-msg').textContent='No saved project';return;}
      const text=req.result.data;
      if(wasmMem&&text){
        const dv2=new DataView(wasmMem.buffer);
        for(let i=0;i<text.length;i++)dv2.setInt32(0x700000+4+i,text.charCodeAt(i),true);
        call('scene_new');
        call('project_deserialize',text.length);
      }
      document.getElementById('st-msg').textContent='Project loaded ('+new Date(req.result.saved).toLocaleTimeString()+')';
      rebuildOutliner();updateProps();
    };
  }catch(e){document.getElementById('st-msg').textContent='Load failed';}
}
function openDB(){return new Promise((resolve,reject)=>{
  const req=indexedDB.open('SparkStudio',1);
  req.onupgradeneeded=()=>req.result.createObjectStore('projects',{keyPath:'id'});
  req.onsuccess=()=>resolve(req.result);req.onerror=()=>reject(req.error);
});}

// ---- KvStore JS bridge ----
const KV_TMP_KEY=8380000,KV_TMP_VAL=8384000;
function kvPut(key,val){
  if(!wasmMem)return;const dv=new DataView(wasmMem.buffer);
  for(let i=0;i<key.length;i++)dv.setInt32(KV_TMP_KEY+i*4,key.charCodeAt(i),true);
  dv.setInt32(KV_TMP_VAL,val,true);
  call('kv_put',KV_TMP_KEY,key.length,KV_TMP_VAL,1);
}
function kvGet(key){
  if(!wasmMem)return 0;const dv=new DataView(wasmMem.buffer);
  for(let i=0;i<key.length;i++)dv.setInt32(KV_TMP_KEY+i*4,key.charCodeAt(i),true);
  const addr=Number(call('kv_get',KV_TMP_KEY,key.length)||0n);
  return addr?dv.getInt32(addr,true):0;
}
function kvDelete(key){
  if(!wasmMem)return;const dv=new DataView(wasmMem.buffer);
  for(let i=0;i<key.length;i++)dv.setInt32(KV_TMP_KEY+i*4,key.charCodeAt(i),true);
  call('kv_delete',KV_TMP_KEY,key.length);
}
function kvCount(){return Number(call('kv_count')||0n);}

function repositionPanels(){
  if(!wasmMem)return;
  const dv=new DataView(wasmMem.buffer);
  const count=rd(dv,LAYOUT_ADDR);
  for(let i=0;i<count;i++){
    const base=LAYOUT_ADDR+4+i*PANEL_STRIDE;
    const el=document.getElementById('panel-'+i);if(!el)continue;
    el.style.left=rd(dv,base+4)+'px';el.style.top=rd(dv,base+8)+'px';
    el.style.width=rd(dv,base+12)+'px';el.style.height=rd(dv,base+16)+'px';
  }
}

// ---- Toolbar ----
function buildToolbar(){const el=document.getElementById('toolbar');if(el)el.innerHTML=wasmHtml('gen_toolbar',0);}

// ---- Outliner (generated by Codex gen-outliner) ----
function outlinerClick(ev,scIdx){if(ev.shiftKey){call('multi_sel_toggle',scIdx);}else{call('multi_sel_clear');call('select_scene_object',scIdx);selectedObj=scIdx;}rebuildOutliner();updateProps();}
function outlinerCtx(ev,scIdx){ev.preventDefault();call('select_scene_object',scIdx);selectedObj=scIdx;showContextMenu(ev.clientX,ev.clientY,scIdx);}
function rebuildOutliner(){
  const el=document.getElementById('outliner-list');if(!el||!wasmMem)return;
  el.innerHTML=wasmHtml('gen_outliner',0);
  updateStatus();updateProps();
}
function toggleVis(i){call('toggle_obj_visible',i);rebuildOutliner();}

// ---- Properties (generated by Codex gen-props-panel) ----
function updateProps(){const el=document.getElementById('props');if(el&&wasmMem)el.innerHTML=wasmHtml('gen_props_panel',0);}

// ---- Status ----
function updateStatus(){
  if(!wasmMem)return;
  const mode=Number(call('get_editor_mode',0)||0n),tool=Number(call('get_active_tool_id',0)||0n);
  const verts=Number(call('get_total_verts',0)||0n),count=Number(call('get_scene_info',0)||0n);
  document.getElementById('st-mode').textContent=MODE_NAMES[mode]||'MODEL';
  document.getElementById('st-tool').textContent=TOOL_NAMES[tool]||'Select';
  const visO=Number(call('get_visible_obj_count',0)||0n);
  document.getElementById('st-objects').textContent=count+' obj ('+visO+' vis)';
  document.getElementById('st-verts').textContent=verts+' verts';
}

// ---- MVP (float, JS) ----
function buildMVP(dv,aspect){
  const tx=rd(dv,CAM_STATE_ADDR)/1000,ty=rd(dv,CAM_STATE_ADDR+4)/1000,tz=rd(dv,CAM_STATE_ADDR+8)/1000;
  const yaw=rd(dv,CAM_STATE_ADDR+12)/1000,pitch=rd(dv,CAM_STATE_ADDR+16)/1000;
  const dist=rd(dv,CAM_STATE_ADDR+20)/1000;
  const sy=Math.sin(yaw),cy=Math.cos(yaw),sp=Math.sin(pitch),cp=Math.cos(pitch);
  const ex=tx+sy*cp*dist,ey=ty+sp*dist,ez=tz+cy*cp*dist;
  const dx=tx-ex,dy=ty-ey,dz2=tz-ez;
  const fl=Math.sqrt(dx*dx+dy*dy+dz2*dz2)||1;
  const fx=dx/fl,fy=dy/fl,fz=dz2/fl;
  const zx=-fx,zy=-fy,zz=-fz;
  const upY=cp>=0?1:-1;
  let xx=upY*zz,xy=0,xz=-upY*zx;
  const xl=Math.sqrt(xx*xx+xz*xz)||1;xx/=xl;xz/=xl;
  const yx=zy*xz-zz*xy,yy=zz*xx-zx*xz,yz=zx*xy-zy*xx;
  const view=new Float32Array([xx,yx,zx,0,xy,yy,zy,0,xz,yz,zz,0,-(xx*ex+xy*ey+xz*ez),-(yx*ex+yy*ey+yz*ez),-(zx*ex+zy*ey+zz*ez),1]);
  const fov=60*Math.PI/180,near=0.1,far=200;
  const f2=1/Math.tan(fov/2),ri=1/(near-far);
  const proj=new Float32Array([f2/aspect,0,0,0,0,f2,0,0,0,0,far*ri,-1,0,0,far*near*ri,0]);
  const mvp=new Float32Array(16);
  for(let c=0;c<4;c++)for(let r=0;r<4;r++){let s=0;for(let k=0;k<4;k++)s+=proj[k*4+r]*view[c*4+k];mvp[c*4+r]=s;}
  return{mvp,eye:[ex,ey,ez]};
}

// ---- Events ----
document.addEventListener('contextmenu',e=>{if(e.target.tagName==='CANVAS')e.preventDefault();});
document.addEventListener('pointerdown',e=>{if(e.target.tagName!=='CANVAS')return;if(e.button===2||e.button===1){dragBtn=e.button;dragShift=e.shiftKey;dragLastX=e.clientX;dragLastY=e.clientY;e.target.setPointerCapture(e.pointerId);e.preventDefault();}});
document.addEventListener('pointermove',e=>{if(dragBtn<0)return;const dx=e.clientX-dragLastX,dy=e.clientY-dragLastY;dragLastX=e.clientX;dragLastY=e.clientY;if(dx===0&&dy===0)return;call('mouse_event',(dragBtn===2&&dragShift)||dragBtn===1?3:2,0,0,0,dx,dy);});
document.addEventListener('pointerup',e=>{if(e.button===dragBtn){dragBtn=-1;try{e.target.releasePointerCapture(e.pointerId);}catch(ex){}}});
document.addEventListener('wheel',e=>{
  if(e.target.id==='c2d'){
    // Canvas zoom in image mode
    const f=e.deltaY>0?0.9:1.1;canvasZoom=Math.max(0.25,Math.min(8,canvasZoom*f));updateImageMode();e.preventDefault();
  } else if(e.target.tagName==='CANVAS'){call('mouse_event',1,0,0,0,0,Math.round(e.deltaY));e.preventDefault();}
},{passive:false});
document.addEventListener('keydown',e=>{
  if(e.target.tagName==='INPUT')return;
  if(e.ctrlKey&&e.key==='z'){call('undo_pop');rebuildOutliner();e.preventDefault();}
  else if(e.ctrlKey&&e.key==='s'){saveProject();e.preventDefault();}
  else if(e.ctrlKey&&e.key==='o'){loadProject();e.preventDefault();}
  else if(e.ctrlKey&&(e.key==='p'||e.key==='P')){showCommandPalette();e.preventDefault();}
  else if(e.key===' '){paused=!paused;updateScenePanel();e.preventDefault();}
  else if(e.key==='+'||e.key==='='){speed=Math.min(speed+1,6);updateScenePanel();}
  else if(e.key==='-'){speed=Math.max(speed-1,0);updateScenePanel();}
  else if(e.key==='f'||e.key==='F')call('focus_selected');
  else if(e.key==='w'||e.key==='W'){call('toggle_render_mode');updateScenePanel();}
  else if(e.key==='g'||e.key==='G'){call('toggle_grid');updateScenePanel();}
  else if(e.key==='d'||e.key==='D'){call('undo_push');call('duplicate_selected');rebuildOutliner();}
  else if(e.key==='1'){call('set_cam_preset',0);}
  else if(e.key==='3'){call('set_cam_preset',2);}
  else if(e.key==='7'){call('set_cam_preset',1);}
  else if(e.key==='0'){call('set_cam_preset',3);}
  else if(e.key==='k'||e.key==='K'){addKeyframeAtPlayhead();}
  else if(e.key==='h'&&!e.altKey){const s2=getSelected();if(s2>=0){toggleVis(s2);}}
  else if(e.key==='h'&&e.altKey){call('show_all_objects');rebuildOutliner();}
  else if(e.key==='l'||e.key==='L'){const s3=getSelected();if(s3>=0){call('toggle_object_lock',s3);updateProps();}}
  else if(e.key==='b'||e.key==='B'){setImgTool(0);}
  else if(e.key==='e'||e.key==='E'){setImgTool(1);}
  else if(e.key==='i'||e.key==='I'){setImgTool(3);}
  else if(e.key==='?'){showAbout();}
  else if(e.key==='r'&&!e.ctrlKey&&!e.altKey){call('random_color',Date.now()&0x7FFFFFFF);updateProps();}
  else if(e.ctrlKey&&e.key>='1'&&e.key<='4'){call('save_bookmark',parseInt(e.key)-1);document.getElementById('st-msg').textContent='Bookmark '+e.key+' saved';e.preventDefault();}
  else if(e.altKey&&e.key>='1'&&e.key<='4'){call('load_bookmark',parseInt(e.key)-1);document.getElementById('st-msg').textContent='Bookmark '+e.key+' loaded';e.preventDefault();}
  else if(e.key==='Enter'){call('canvas_sel_fill');call('canvas_sel_clear');updateImageMode();}
  else if(e.key==='Escape'){call('canvas_sel_clear');updateImageMode();['cmd-palette','shortcuts-dlg','about-dlg','ctx-menu'].forEach(id=>{let el2=document.getElementById(id);if(el2)el2.remove();});}
  else if(e.key==='/'||e.key==='?'){showShortcuts();}
  else if(e.ctrlKey&&e.key==='c'){call('clipboard_copy',getSelected());document.getElementById('st-msg').textContent='Copied';e.preventDefault();}
  else if(e.ctrlKey&&e.key==='v'){call('undo_push');call('clipboard_paste');rebuildOutliner();e.preventDefault();}
  else if(e.key==='x'&&!e.ctrlKey){call('toggle_xray');updateScenePanel();}
  else if(e.key==='n'&&!e.ctrlKey){call('toggle_show_normals');updateScenePanel();}
  else if(e.ctrlKey&&e.key==='c'){call('clipboard_copy',getSelected());document.getElementById('st-msg').textContent='Copied';e.preventDefault();}
  else if(e.ctrlKey&&e.key==='v'){call('undo_push');call('clipboard_paste');rebuildOutliner();e.preventDefault();}
  else if(e.key==='Delete'||e.key==='Backspace'){call('undo_push');const s=getSelected();if(s>=0){call('delete_scene_object',s);rebuildOutliner();}}
});

// ---- Shader ----
const SHADER = `
struct Uniforms { mvp: mat4x4f, eye: vec3f, pad: f32 };
@group(0) @binding(0) var<uniform> u: Uniforms;
struct VIn { @location(0) pos: vec3f, @location(1) norm: vec3f, @location(2) col: vec3f, @location(3) mat: vec3f };
struct VOut { @builtin(position) pos: vec4f, @location(0) norm: vec3f, @location(1) col: vec3f, @location(2) wpos: vec3f, @location(3) mat: vec3f };
@vertex fn vs(v: VIn) -> VOut {
  var o: VOut; o.pos = u.mvp * vec4f(v.pos, 1.0); o.norm = v.norm; o.col = v.col; o.wpos = v.pos; o.mat = v.mat; return o;
}
@fragment fn fs(@location(0) norm: vec3f, @location(1) col: vec3f, @location(2) wpos: vec3f, @location(3) mat: vec3f) -> @location(0) vec4f {
  let n = normalize(norm); let vd = normalize(u.eye - wpos);
  let metallic = mat.x; let roughness = mat.y; let emission = mat.z;
  let l1 = normalize(vec3f(-0.4, 0.7, -0.5)); let l2 = normalize(vec3f(0.5, 0.2, 0.4));
  let diff = max(dot(n,l1),0.0)*0.45 + max(dot(n,l2),0.0)*0.15 + max(dot(n,normalize(vec3f(0,-0.4,-0.3))),0.0)*0.08;
  let diffuse = col * (1.0 - metallic*0.85) * (vec3f(0.25) + diff);
  let shin = mix(16.0, 512.0, 1.0 - roughness);
  let spec = (pow(max(dot(n,normalize(l1+vd)),0.0),shin) + pow(max(dot(n,normalize(l2+vd)),0.0),shin*0.5)*0.4) * mix(0.06,0.8,metallic) * (1.0-roughness*0.7);
  let sc = mix(vec3f(1.0), col, metallic);
  let refl = reflect(-vd,n); let ey = refl.y*0.5+0.5;
  let env = mix(mix(vec3f(0.1,0.08,0.06),vec3f(0.3,0.25,0.2),max(0.0,-refl.y)), mix(vec3f(0.08,0.08,0.12),vec3f(0.5,0.6,0.8),ey), step(0.0,refl.y));
  let reflection = col * env * metallic * (1.0-roughness*0.6) * 1.5;
  let fresnel = pow(1.0-max(dot(n,vd),0.0),4.0) * mix(0.04,0.6,metallic);
  let emitted = col * emission * emission * 3.0;
  return vec4f(diffuse + sc*spec + reflection + sc*fresnel + emitted, 1.0);
}
@fragment fn fs_wire(@location(0) norm: vec3f, @location(1) col: vec3f, @location(2) wpos: vec3f, @location(3) mat: vec3f) -> @location(0) vec4f {
  return vec4f(col * 1.3, 1.0);
}
`;

// ---- WebGPU init ----
async function init(){
  if(!navigator.gpu){document.getElementById('error').style.display='block';return;}
  const adapter=await navigator.gpu.requestAdapter();if(!adapter)return;
  const device=await adapter.requestDevice();
  const resp=await fetch('spark-webgpu.wasm');const bytes=await resp.arrayBuffer();let mem=null;
  const imports={wasi_snapshot_preview1:{fd_write(fd,iovs,n,nw){const v=new DataView(mem.buffer);let t=0;for(let i=0;i<n;i++)t+=v.getUint32(iovs+i*8+4,true);v.setUint32(nw,t,true);return 0;}},env:{blit_framebuf(){},on_key(){return 0;}}};
  const wmod=await WebAssembly.compile(bytes);const inst=await WebAssembly.instantiate(wmod,imports);
  mem=inst.exports.memory;wasmMem=mem;wasmExports=inst.exports;
  if(inst.exports._start)try{inst.exports._start();}catch(e){console.warn('_start:',e.message);}
  try{call('wasm_init',0);}catch(e){console.warn('wasm_init:',e.message);}
  call('resize_layout',window.innerWidth,window.innerHeight);
  loadCommands();if(COMMANDS.length===0)COMMANDS=COMMANDS_FALLBACK;
  buildPanels();buildModeTabs();rebuildOutliner();buildToolbar();updateStatus();

  // Auto-save every 60 seconds
  setInterval(()=>{saveProject().catch(()=>{});},60000);

  const canvas=document.getElementById('c');if(!canvas)return;
  const ctx=canvas.getContext('webgpu');const fmt=navigator.gpu.getPreferredCanvasFormat();
  function resize(){const p=canvas.parentElement;if(!p)return;canvas.width=Math.max(Math.floor(p.clientWidth*devicePixelRatio),1);canvas.height=Math.max(Math.floor((p.clientHeight-24)*devicePixelRatio),1);ctx.configure({device,format:fmt,alphaMode:'opaque'});}
  resize();
  window.addEventListener('resize',()=>{call('resize_layout',window.innerWidth,window.innerHeight);repositionPanels();resize();depthTex=mkD();});

  const mod=device.createShaderModule({code:SHADER});
  const vl=[{arrayStride:48,attributes:[{shaderLocation:0,offset:0,format:'float32x3'},{shaderLocation:1,offset:12,format:'float32x3'},{shaderLocation:2,offset:24,format:'float32x3'},{shaderLocation:3,offset:36,format:'float32x3'}]}];
  const solidPL=device.createRenderPipeline({layout:'auto',vertex:{module:mod,entryPoint:'vs',buffers:vl},fragment:{module:mod,entryPoint:'fs',targets:[{format:fmt}]},primitive:{topology:'triangle-list',cullMode:'none'},depthStencil:{format:'depth24plus',depthWriteEnabled:true,depthCompare:'less'}});
  const wirePL=device.createRenderPipeline({layout:'auto',vertex:{module:mod,entryPoint:'vs',buffers:vl},fragment:{module:mod,entryPoint:'fs_wire',targets:[{format:fmt}]},primitive:{topology:'line-list',cullMode:'none'},depthStencil:{format:'depth24plus',depthWriteEnabled:false,depthCompare:'always'}});
  function mkD(){return device.createTexture({size:[canvas.width,canvas.height],format:'depth24plus',usage:GPUTextureUsage.RENDER_ATTACHMENT});}
  let depthTex=mkD();
  const uBuf=device.createBuffer({size:256,usage:GPUBufferUsage.UNIFORM|GPUBufferUsage.COPY_DST});
  const solidBG=device.createBindGroup({layout:solidPL.getBindGroupLayout(0),entries:[{binding:0,resource:{buffer:uBuf}}]});
  const wireBG=device.createBindGroup({layout:wirePL.getBindGroupLayout(0),entries:[{binding:0,resource:{buffer:uBuf}}]});
  let vBuf=device.createBuffer({size:4,usage:GPUBufferUsage.VERTEX|GPUBufferUsage.COPY_DST}),vBufSz=4;
  lastT=performance.now();

  function loop(){
    try{
      if(!paused)angle+=speed;
      if(wasmExports.__heap_reset)wasmExports.__heap_reset();
      wasmExports.render_frame(BigInt(angle));
      const dv=new DataView(mem.buffer);
      const vertCount=rd(dv,VERT_COUNT_ADDR);
      if(vertCount<=0){requestAnimationFrame(loop);return;}
      const aspect=canvas.width/canvas.height;
      const cam=buildMVP(dv,aspect);
      const uData=new Float32Array(20);uData.set(cam.mvp);
      uData[16]=cam.eye[0];uData[17]=cam.eye[1];uData[18]=cam.eye[2];uData[19]=0;
      device.queue.writeBuffer(uBuf,0,uData);
      const selExtra=rd(dv,260),gridExtra=rd(dv,264),partExtra=rd(dv,268),gizExtra=rd(dv,272);
      const totalVerts=vertCount+selExtra+gridExtra+partExtra+gizExtra;
      const FPV=12,fc2=totalVerts*FPV;const vd=new Float32Array(fc2);
      for(let i=0;i<fc2;i++)vd[i]=rd(dv,VERT_ADDR+i*4)/1000.0;
      const needed=vertCount*48;
      if(needed>vBufSz){vBuf.destroy();vBufSz=needed*2;vBuf=device.createBuffer({size:vBufSz,usage:GPUBufferUsage.VERTEX|GPUBufferUsage.COPY_DST});}
      device.queue.writeBuffer(vBuf,0,vd);
      // Background color from WASM
      const BG_ADDR=3342380;
      const bgR=rd(dv,BG_ADDR)/255,bgG=rd(dv,BG_ADDR+4)/255,bgB=rd(dv,BG_ADDR+8)/255;
      const tex=ctx.getCurrentTexture();const enc=device.createCommandEncoder();
      const pass=enc.beginRenderPass({colorAttachments:[{view:tex.createView(),clearValue:{r:bgR,g:bgG,b:bgB,a:1},loadOp:'clear',storeOp:'store'}],depthStencilAttachment:{view:depthTex.createView(),depthClearValue:1.0,depthLoadOp:'clear',depthStoreOp:'store'}});
      const rm=rd(dv,APP_STATE_ADDR+8);
      if(rm===1){
        const tc=Math.floor(vertCount/3),lv=tc*6,ld=new Float32Array(lv*FPV);
        for(let t=0;t<tc;t++){const si2=t*3*FPV,di=t*6*FPV;
          for(let f=0;f<FPV;f++){ld[di+f]=vd[si2+f];ld[di+FPV+f]=vd[si2+FPV+f];}
          for(let f=0;f<FPV;f++){ld[di+2*FPV+f]=vd[si2+FPV+f];ld[di+3*FPV+f]=vd[si2+2*FPV+f];}
          for(let f=0;f<FPV;f++){ld[di+4*FPV+f]=vd[si2+2*FPV+f];ld[di+5*FPV+f]=vd[si2+f];}
        }
        const lb=lv*48;if(lb>vBufSz){vBuf.destroy();vBufSz=lb*2;vBuf=device.createBuffer({size:vBufSz,usage:GPUBufferUsage.VERTEX|GPUBufferUsage.COPY_DST});}
        device.queue.writeBuffer(vBuf,0,ld);pass.setPipeline(wirePL);pass.setBindGroup(0,wireBG);pass.setVertexBuffer(0,vBuf);pass.draw(lv);
      }else{pass.setPipeline(solidPL);pass.setBindGroup(0,solidBG);pass.setVertexBuffer(0,vBuf);pass.draw(vertCount);}
      // Selection + grid + particles vertex counts
      const selVerts=rd(dv,260);
      const gridVerts=rd(dv,264), partVerts=rd(dv,268), gizVerts=rd(dv,272);
      const overlayVerts=gridVerts+partVerts+gizVerts;
      if(overlayVerts>0){
        const overlayStart=vertCount+selVerts;
        const overlayFPV=12, overlayFC=overlayVerts*overlayFPV;
        // Grid lines as line-list, particles as line-list
        const tc3=Math.floor(overlayVerts/3),lv3=tc3*6,ld3=new Float32Array(lv3*overlayFPV);
        for(let t=0;t<tc3;t++){const si4=overlayStart*overlayFPV+t*3*overlayFPV,di3=t*6*overlayFPV;
          for(let f=0;f<overlayFPV;f++){ld3[di3+f]=vd[si4+f];ld3[di3+overlayFPV+f]=vd[si4+overlayFPV+f];}
          for(let f=0;f<overlayFPV;f++){ld3[di3+2*overlayFPV+f]=vd[si4+overlayFPV+f];ld3[di3+3*overlayFPV+f]=vd[si4+2*overlayFPV+f];}
          for(let f=0;f<overlayFPV;f++){ld3[di3+4*overlayFPV+f]=vd[si4+2*overlayFPV+f];ld3[di3+5*overlayFPV+f]=vd[si4+f];}
        }
        if(lv3>0){
          const ob=device.createBuffer({size:ld3.byteLength,usage:GPUBufferUsage.VERTEX|GPUBufferUsage.COPY_DST});
          device.queue.writeBuffer(ob,0,ld3);pass.setPipeline(wirePL);pass.setBindGroup(0,wireBG);pass.setVertexBuffer(0,ob);pass.draw(lv3);
        }
      }
      // Selection wireframe overlay (drawn after grid, always on top)
      if(selVerts>0){
        const selStart=vertCount,selFPV=12;
        const tc2=Math.floor(selVerts/3),lv2=tc2*6,ld2=new Float32Array(lv2*selFPV);
        for(let t=0;t<tc2;t++){const si3=selStart*selFPV+t*3*selFPV,di2=t*6*selFPV;
          for(let f=0;f<selFPV;f++){ld2[di2+f]=vd[si3+f];ld2[di2+selFPV+f]=vd[si3+selFPV+f];}
          for(let f=0;f<selFPV;f++){ld2[di2+2*selFPV+f]=vd[si3+selFPV+f];ld2[di2+3*selFPV+f]=vd[si3+2*selFPV+f];}
          for(let f=0;f<selFPV;f++){ld2[di2+4*selFPV+f]=vd[si3+2*selFPV+f];ld2[di2+5*selFPV+f]=vd[si3+f];}
        }
        const sb2=device.createBuffer({size:ld2.byteLength,usage:GPUBufferUsage.VERTEX|GPUBufferUsage.COPY_DST});
        device.queue.writeBuffer(sb2,0,ld2);pass.setPipeline(wirePL);pass.setBindGroup(0,wireBG);pass.setVertexBuffer(0,sb2);pass.draw(lv2);
      }
      pass.end();device.queue.submit([enc.finish()]);
    }catch(e){console.error('frame:',e);}
    fc++;const now=performance.now();if(now-lastT>=1000){fps=fc;fc=0;lastT=now;
      try{
        document.getElementById('stats').textContent=fps+' fps';updateTimeline();updateStatus();
        if(Number(call('get_editor_mode',0)||0n)===1)updateProps();
        const ov=document.getElementById('vp-overlay');if(ov&&wasmMem){const dv3=new DataView(wasmMem.buffer);const m2=rd(dv3,APP_STATE_ADDR);
          const liveObj=rd(dv3,APP_STATE_ADDR+32),visObj=rd(dv3,APP_STATE_ADDR+36);
          let info=Number(call('get_total_verts',0)||0n)+' verts | '+liveObj+' objects ('+visObj+' visible)';
          if(m2===1)info='Frame '+rd(dv3,TL_ADDR)+'/'+rd(dv3,TL_ADDR+16)+(rd(dv3,TL_ADDR+12)?' ▶':' ❚❚')+'<br>'+info;
          if(m2===2)info='RENDER MODE<br>'+info;
          const sel2=getSelected();if(sel2>=0&&sel2<64){try{const sn=readObjName(dv3,sel2);info+=`<br><span style="color:var(--accent)">${sn}</span>`;}catch(e){}}
          ov.innerHTML=info;}
      }catch(e){}
    }
    requestAnimationFrame(loop);
  }
  loop();
}
init().catch(e=>{console.error(e);});
