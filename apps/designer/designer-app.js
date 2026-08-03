let wasmMem=null,wasmExports={};
const STATE_ADDR=0x220000,WIDGET_BASE=0x200000,WIDGET_STRIDE=128,STRING_POOL=0x210000;
const RENDER_BUF=0x700000;
const TYPE_NAMES=['Section','Button','Toggle','PropRow','TextInput','Slider','ColorPick','Grid','TabGroup','Panel','Spacer'];
const TYPE_ICONS=['H','B','T','P','I','S','C','G','Tb','Pn','--'];
const TYPE_COLORS=['#4848a0','#00d4ff','#febc2e','#888','#28c840','#7b61ff','#ff5f57','#7b61ff','#28c840','#00d4ff','#444'];

function rd(dv,a){return dv.getInt32(a,true);}
function call(name,...args){const fn=wasmExports[name];if(fn)return fn(...args.map(a=>typeof a==='number'?BigInt(a):a));}

function readRenderBuf(){
  const dv=new DataView(wasmMem.buffer);
  const len=rd(dv,RENDER_BUF);
  let s='';for(let i=0;i<len;i++)s+=String.fromCharCode(rd(dv,RENDER_BUF+4+i)&0xFF);
  return s;
}

// ---- All rendering delegated to WASM ----
function renderCanvas(){call('render_canvas',0);document.getElementById('canvas').innerHTML=readRenderBuf();bindCanvasEvents();}
function renderPreview(){call('render_preview',0);document.getElementById('preview').innerHTML=readRenderBuf();}
function renderTree(){call('render_tree',0);document.getElementById('tree').innerHTML=readRenderBuf();bindTreeEvents();}
function renderProps(){call('render_props',0);document.getElementById('props').innerHTML=readRenderBuf();}
function renderCode(){call('export_codex');document.getElementById('code-text').textContent=readRenderBuf();}
function renderAll(){renderCanvas();renderPreview();renderTree();renderProps();renderCode();}

// ---- Thin event layer ----
let dragIdx=-1,dragOx=0,dragOy=0,resizeIdx=-1;

function bindCanvasEvents(){
  document.getElementById('canvas').querySelectorAll('.dsg-cw').forEach(w=>{
    w.addEventListener('pointerdown',e=>{
      if(e.target.classList.contains('dsg-resize')){
        resizeIdx=parseInt(e.target.dataset.ridx);
        e.target.setPointerCapture(e.pointerId);
        dragOx=e.clientX;dragOy=e.clientY;
        e.stopPropagation();return;
      }
      const idx=parseInt(w.dataset.idx);
      call('select_widget',idx);
      dragIdx=idx;dragOx=e.clientX;dragOy=e.clientY;
      w.setPointerCapture(e.pointerId);
      e.stopPropagation();
      renderAll();
    });
  });
}

function bindTreeEvents(){
  document.getElementById('tree').querySelectorAll('.dsg-tree-item').forEach(ti=>{
    ti.addEventListener('click',()=>{call('select_widget',parseInt(ti.dataset.idx));renderAll();});
    ti.addEventListener('dragover',e=>e.preventDefault());
    ti.addEventListener('drop',e=>{
      e.preventDefault();
      const t=e.dataTransfer.getData('text/plain');
      if(t.length<=2){
        const parentIdx=parseInt(ti.dataset.idx);
        const newIdx=Number(call('add_widget',parseInt(t),0,0)||0n);
        if(newIdx>=0)call('reparent_widget',newIdx,parentIdx);
        renderAll();
      }
    });
  });
}

document.addEventListener('pointermove',e=>{
  if(dragIdx>=0){call('move_widget',dragIdx,e.clientX-dragOx,e.clientY-dragOy);dragOx=e.clientX;dragOy=e.clientY;renderCanvas();}
  if(resizeIdx>=0){call('resize_widget',resizeIdx,e.clientX-dragOx,e.clientY-dragOy);dragOx=e.clientX;dragOy=e.clientY;renderCanvas();renderProps();}
});
document.addEventListener('pointerup',()=>{if(dragIdx>=0||resizeIdx>=0)renderAll();dragIdx=-1;resizeIdx=-1;});

// ---- Canvas drop (new widgets from palette) ----
const canvasEl=document.getElementById('canvas');
canvasEl.addEventListener('dragover',e=>e.preventDefault());
canvasEl.addEventListener('drop',e=>{
  e.preventDefault();
  const t=parseInt(e.dataTransfer.getData('text/plain'));
  if(isNaN(t))return;
  const rect=canvasEl.getBoundingClientRect();
  call('add_widget',t,Math.round(e.clientX-rect.left+canvasEl.scrollLeft),Math.round(e.clientY-rect.top+canvasEl.scrollTop));
  renderAll();
  document.getElementById('dsg-status').textContent=TYPE_NAMES[t]+' added';
});
canvasEl.addEventListener('pointerdown',e=>{if(e.target===canvasEl){call('select_widget',-1);renderAll();}});

// ---- Widget property setters (called from WASM-rendered HTML onclick attributes) ----
function setWidgetText(idx,val){
  const enc=new TextEncoder();const bytes=enc.encode(val);const dv=new DataView(wasmMem.buffer);
  const pp=rd(dv,STATE_ADDR+8);
  for(let i=0;i<bytes.length;i++)dv.setInt32(STRING_POOL+pp+i,bytes[i],true);
  dv.setInt32(WIDGET_BASE+idx*WIDGET_STRIDE+32,pp,true);
  dv.setInt32(WIDGET_BASE+idx*WIDGET_STRIDE+36,bytes.length,true);
  dv.setInt32(STATE_ADDR+8,pp+bytes.length,true);
  renderAll();
}
function setWidgetOnclick(idx,val){
  const enc=new TextEncoder();const bytes=enc.encode(val);const dv=new DataView(wasmMem.buffer);
  const pp=rd(dv,STATE_ADDR+8);
  for(let i=0;i<bytes.length;i++)dv.setInt32(STRING_POOL+pp+i,bytes[i],true);
  dv.setInt32(WIDGET_BASE+idx*WIDGET_STRIDE+40,pp,true);
  dv.setInt32(WIDGET_BASE+idx*WIDGET_STRIDE+44,bytes.length,true);
  dv.setInt32(STATE_ADDR+8,pp+bytes.length,true);
  renderAll();
}
function setProp(idx,prop,val){
  const dv=new DataView(wasmMem.buffer);
  dv.setInt32(WIDGET_BASE+idx*WIDGET_STRIDE+({x:4,y:8,w:12,h:16}[prop]),parseInt(val),true);
  renderAll();
}
function dupWidget(idx){
  const dv=new DataView(wasmMem.buffer);
  const b=WIDGET_BASE+idx*WIDGET_STRIDE;
  const t=rd(dv,b),x=rd(dv,b+4),y=rd(dv,b+8);
  const newIdx=Number(call('add_widget',t,x+20,y+20)||0n);
  if(newIdx>=0){
    const toff=rd(dv,b+32),tlen=rd(dv,b+36);
    let text='';for(let i=0;i<tlen;i++)text+=String.fromCharCode(rd(dv,STRING_POOL+toff+i)&0xFF);
    setWidgetText(newIdx,text);
  }
}
function deleteSelected(){const dv=new DataView(wasmMem.buffer);const s=rd(dv,STATE_ADDR+4);if(s>=0)call('delete_widget',s);renderAll();}
function clearAll(){const dv=new DataView(wasmMem.buffer);dv.setInt32(STATE_ADDR,0,true);dv.setInt32(STATE_ADDR+4,-1,true);dv.setInt32(STATE_ADDR+8,0,true);renderAll();}

// ---- Palette (static -- the only JS-rendered UI) ----
function buildPalette(){
  let h='<div class="dsg-pal-head">WIDGETS</div>';
  TYPE_NAMES.forEach((n,i)=>{h+=`<button class="dsg-pal-item" draggable="true" data-type="${i}"><span class="dsg-pal-icon" style="border-color:${TYPE_COLORS[i]};color:${TYPE_COLORS[i]}">${TYPE_ICONS[i]}</span>${n}</button>`;});
  h+='<div class="dsg-pal-head">ACTIONS</div>';
  h+=`<button class="dsg-pal-item" onclick="deleteSelected()"><span class="dsg-pal-icon" style="border-color:var(--warn);color:var(--warn)">X</span>Delete</button>`;
  h+=`<button class="dsg-pal-item" onclick="clearAll()"><span class="dsg-pal-icon" style="border-color:var(--dim)">C</span>Clear All</button>`;
  document.getElementById('palette').innerHTML=h;
  document.getElementById('palette').querySelectorAll('[draggable]').forEach(btn=>{
    btn.addEventListener('dragstart',e=>{e.dataTransfer.setData('text/plain',btn.dataset.type);});
  });
}

// ---- Keyboard ----
document.addEventListener('keydown',e=>{
  if(e.target.tagName==='INPUT'||e.target.tagName==='TEXTAREA'||e.target.tagName==='SELECT')return;
  if(e.key==='Delete'||e.key==='Backspace'){deleteSelected();e.preventDefault();}
  if(e.key==='d'&&e.ctrlKey){const dv=new DataView(wasmMem.buffer);dupWidget(rd(dv,STATE_ADDR+4));e.preventDefault();}
});

// ---- Code font ----
let codeFontSize=0.72;
function adjustCodeFont(d){codeFontSize=Math.max(0.4,Math.min(1.6,codeFontSize+d));document.getElementById('code-text').style.fontSize=codeFontSize+'em';}
document.getElementById('copy-btn').addEventListener('click',()=>{
  navigator.clipboard.writeText(document.getElementById('code-text').textContent).then(()=>{document.getElementById('dsg-status').textContent='Copied';});
});

// ---- Init ----
async function init(){
  const resp=await fetch('designer.wasm');const bytes=await resp.arrayBuffer();let mem=null;
  const imports={wasi_snapshot_preview1:{fd_write(fd,iovs,n,nw){const v=new DataView(mem.buffer);let t=0;for(let i=0;i<n;i++)t+=v.getUint32(iovs+i*8+4,true);v.setUint32(nw,t,true);return 0;}},env:{blit_framebuf(){},on_key(){return 0;}}};
  const wmod=await WebAssembly.compile(bytes);const inst=await WebAssembly.instantiate(wmod,imports);
  mem=inst.exports.memory;wasmMem=mem;wasmExports=inst.exports;
  if(inst.exports._start)try{inst.exports._start();}catch(e){console.warn('_start:',e.message);}
  call('designer_init');
  buildPalette();renderAll();
  document.getElementById('dsg-status').textContent='Codex Designer -- all rendering via WASM';
}
init().catch(e=>{console.error(e);document.getElementById('dsg-status').textContent='Error: '+e.message;});
