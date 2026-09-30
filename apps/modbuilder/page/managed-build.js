(() => {
  function buildNumber(bytes) {
    const view=new DataView(bytes.buffer,bytes.byteOffset,bytes.byteLength),n=bytes.length;
    const u16=p=>view.getUint16(p,true),u32=p=>view.getUint32(p,true);
    try {
      const pe=u32(60),opt=pe+24,magic=u16(opt),sections=opt+u16(pe+20);
      if(u16(0)!==0x5a4d||u32(pe)!==0x4550||![267,523].includes(magic))throw 0;
      const offset=(rva,size)=>{for(let i=0;i<u16(pe+6);i++){const s=sections+i*40,delta=rva-u32(s+12),raw=u32(s+20);if(delta>=0&&delta+size<=u32(s+16)&&raw+delta+size<=n)return raw+delta;}throw 0;};
      const cli=offset(u32(opt+(magic===267?96:112)+14*8),16),length=u32(cli+12),meta=offset(u32(cli+8),length),end=meta+length;
      if(u32(meta)!==0x424a5342)throw 0;
      let pos=meta+16+u32(meta+12);const count=u16(pos+2);pos+=4;
      for(let i=0;i<count;i++){
        if(pos+8>end)throw 0;const start=meta+u32(pos),size=u32(pos+4);pos+=8;let name='';
        for(let j=0;j<32;j++){if(pos>=end)throw 0;const c=bytes[pos++];if(!c)break;name+=String.fromCharCode(c);if(j===31)throw 0;}
        pos=meta+Math.ceil((pos-meta)/4)*4;
        if(name==='#GUID'){
          if(size!==16||start<meta||start+16>end)throw 0;
          return [3,2,1,0,5,4,7,6,8,9,10,11,12,13,14,15].map(i=>bytes[start+i].toString(16).padStart(2,'0')).join('');
        }
      }
    }catch{}
    throw new Error('This DLL has no supported ModBuilder build identifier. Build it again in this page.');
  }
  async function compile(source) {
    if(!window.__prismReady)throw new Error('The compiler is still loading.');
    const unit=await resolveUnit({text:source,regions:[]});
    if(unit.missing.length)throw new Error('Missing chapters: '+unit.missing.join(', '));
    const compiled=await runW(await moduleBytes('codex-compiler.wasm'),'IR-UNI passes=text-plug\n'+unit.text);
    const lines=compiled.text.split('\n'),first=lines.indexOf('IR-BEGIN'),last=lines.indexOf('IR-END');
    if(first<0||last<=first)throw new Error(compiled.text.slice(0,4000)||'The compiler produced no model.');
    const ir=lines.slice(first+1,last).join('\n');
    if(!EMBED['PrismRuntime.dll'])throw new Error('The bundled Valheim adapter is missing.');
    const emitted=await runW(await moduleBytes('cil-stdio.wasm'),'CIL-UNITY\n'+EMBED['PrismRuntime.dll']+'\n'+ir);
    if(emitted.bytes[0]!==77||emitted.bytes[1]!==90)throw new Error(emitted.text.slice(0,4000)||'The managed compiler produced no DLL.');
    return emitted.bytes;
  }
  async function save(bytes, current) {
    if(typeof window.showSaveFilePicker!=='function')throw new Error('Saving the DLL requires Edge or Chrome with Save As support.');
    const handle=await window.showSaveFilePicker({suggestedName:'PrismMod.dll',startIn:'downloads',types:[{description:'Valheim managed mod',accept:{'application/octet-stream':['.dll']}}]});
    if(!current())throw new Error('The project changed. Build the current source before saving.');
    const writer=await handle.createWritable();
    try {
      if(!current())throw new Error('The project changed. Build the current source before saving.');
      await writer.write(bytes);await writer.close();
    } catch(error){try{await writer.abort();}catch{}throw error;}
  }
  function sourcePanel(host) {
    const files=window.__MOD_COMPILER_SOURCE||{};
    const panel=document.createElement('details');panel.id='mb-compiler-source';panel.className='mb-code';
    panel.innerHTML='<summary>Compiler and adapter source</summary><p>Review the IL emitter and bundled Valheim adapter used by Compile and build. Select a file to read its source.</p><div class="mb-source-workspace"><nav id="mb-compiler-source-file" aria-label="Compiler and adapter files"></nav><section class="mb-source-preview"><div id="mb-compiler-source-name"></div><pre id="mb-compiler-source-code" tabindex="0" aria-label="Source preview"></pre></section></div><p id="mb-compiler-source-status" role="status"></p><div class="mb-actions"><button id="mb-compiler-source-copy">Copy full source</button><button id="mb-compiler-source-save">Save source ZIP</button></div>';
    const list=panel.querySelector('nav'),text=panel.querySelector('pre'),status=panel.querySelector('[role=status]');
    let selected='';
    const show=path=>{
      selected=path;const source=files[path]||'';
      panel.querySelector('#mb-compiler-source-name').textContent=path;
      for(const button of list.children)button.setAttribute('aria-pressed',String(button.dataset.path===path));
      const preview=source.slice(0,HL_PLAIN).split('\n').slice(0,PAINT_MAX).join('\n');
      if(path.endsWith('.codex'))text.innerHTML=preview.split('\n').map(line=>line.length>4096?esc(line):highlightLine(line)).join('\n');
      else if(/\.(cs|ps1)$/.test(path)){
        const tokens=/(\/\/[^\n]*|\/\*[\s\S]*?\*\/|#[^\n]*|@?"(?:""|\\[\s\S]|[^"\\])*"|'(?:\\[\s\S]|[^'\\])*'|\b\d+(?:\.\d+)?\b|\b(?:public|private|internal|static|class|struct|enum|using|namespace|void|var|new|return|if|else|for|foreach|while|switch|case|break|try|catch|finally|throw|true|false|null|function|param|in|let|const|bool|int|string|long|byte)\b)/g;
        let html='',start=0;
        for(const token of preview.matchAll(tokens)){html+=esc(preview.slice(start,token.index));const value=token[0],kind=/^(\/\/|\/\*|#)/.test(value)?'prose':/^[@"']/.test(value)?'str':/^\d/.test(value)?'num':'kw';html+='<span class="k-'+kind+'">'+esc(value)+'</span>';start=token.index+value.length;}
        text.innerHTML=html+esc(preview.slice(start));
      }else text.textContent=preview;
      text.scrollTop=0;text.scrollLeft=0;
      status.textContent=preview.length<source.length?'Preview shortened for responsiveness. Copy full source and Save source ZIP include the complete file.':'';
    };
    for(const path of Object.keys(files)){const button=document.createElement('button');button.type='button';button.dataset.path=path;button.textContent=path;button.onclick=()=>show(path);list.append(button);}
    show(files['codex/plugs/cil/CilEmitter.codex']?'codex/plugs/cil/CilEmitter.codex':Object.keys(files)[0]);
    panel.querySelector('#mb-compiler-source-copy').onclick=async()=>{try{await navigator.clipboard.writeText(files[selected]||'');status.textContent='Copied the complete source file.';}catch{status.textContent='Clipboard access was refused. Save source ZIP to get the complete file.';}};
    panel.querySelector('#mb-compiler-source-save').onclick=()=>{
      const url=URL.createObjectURL(new Blob([makeZip(Object.entries(files).map(([path,text])=>({path,text})))],{type:'application/zip'}));
      const link=document.createElement('a');link.href=url;link.download='ModBuilder-compiler-source.zip';document.body.append(link);link.click();link.remove();setTimeout(()=>URL.revokeObjectURL(url),30000);
    };
    host.append(panel);
  }
  window.ModBuilderManaged={compile,save,sourcePanel,buildNumber};
})();
