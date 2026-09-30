(() => {
  const bundle = window.__NATIVE_HELPER;
  const returnUrl = () => { const url = new URL(location.href); url.hash = ''; return url.href; };
  function saveSource(files) {
    const url = URL.createObjectURL(new Blob([makeZip(files)], {type:'application/zip'}));
    const a = document.createElement('a'); a.href = url; a.download = 'ModBuilder-source.zip'; document.body.append(a); a.click(); a.remove();
    setTimeout(() => URL.revokeObjectURL(url), 30000);
  }
  async function saveExecutable(name, output, current) {
    if (typeof window.showSaveFilePicker !== 'function') throw new Error('Saving the compiled EXE requires a current Edge or Chrome browser with Save As support.');
    // File-origin blob downloads can lose their source path and acquire ZoneId=4.
    // The picker retains browser write/security checks and requires a user gesture.
    const handle = await window.showSaveFilePicker({suggestedName:name, startIn:'downloads', types:[{description:'Windows application', accept:{'application/octet-stream':['.exe']}}]});
    if(!current())throw new Error('The source changed. Compile this program again before saving.');
    const writer = await handle.createWritable();
    try { if(!current())throw new Error('The source changed. Compile this program again before saving.');await writer.write(output); await writer.close(); }
    catch (error) { try { await writer.abort(); } catch {} throw error; }
  }
  async function compile(source, url = returnUrl(), uninstall = false) {
    if (!window.__prismReady) throw new Error('The compiler and library are still loading. Try Compile again shortly.');
    const unit = await resolveUnit({text:source, regions:[]});
    if (unit.missing.length) throw new Error('Missing chapters: ' + unit.missing.join(', '));
    const result = await runW(await moduleBytes('codex-compiler.wasm'), 'CDX map hosted-windows passes=none\n' + unit.text);
    const cdx = cdxPayload(result.bytes);
    if (!cdx) throw new Error(result.text.slice(0, 4000));
    const wire = new Uint8Array(cdx.length + 1); wire[0] = 3; wire.set(cdx, 1);
    const pe = await runW(await moduleBytes('pe-bytes.wasm'), wire);
    return ModBuilderWinHost.specialize(pe.bytes, result.text.slice(result.text.lastIndexOf('MAP:')), false, {returnUrl:url, uninstall,loader:bundle.loader?Uint8Array.from(atob(bundle.loader),c=>c.charCodeAt(0)):undefined});
  }
  function showSource(uninstall = false) {
    const panel = document.getElementById('mb-native-source');
    if (!panel) { location.href = 'workspace.html#helper-source' + (uninstall ? '&uninstall=1' : ''); return; }
    panel.querySelector(uninstall?'#mb-uninstaller-tab':'#mb-helper-tab').click();
    panel.open = true;
    const card = document.getElementById('mb-setup-card'); if (card) card.open = true;
    panel.scrollIntoView({block:'start'});
  }
  function attach() {
    const workspace = document.getElementById('mb-code');
    if (!workspace) return;
    const panel = document.createElement('details'); panel.id = 'mb-native-source'; panel.className = 'mb-code';
    panel.innerHTML = '<summary>Helper and uninstaller source</summary><p>Review or edit either program below. Compile the program, save its EXE, then open the saved file. Both compile locally and work offline.</p><div class="mb-native-code-view"><div class="mb-native-toolbar"><button id="mb-helper-source-zip">Save source ZIP</button><div role="tablist" aria-label="Program source"><button id="mb-helper-tab" role="tab" aria-controls="mb-helper-source-panel" aria-selected="true">Helper</button><button id="mb-uninstaller-tab" role="tab" aria-controls="mb-uninstaller-source-panel" aria-selected="false" tabindex="-1">Uninstaller</button></div></div><div id="mb-native-editors"></div></div><div id="mb-native-build-rows"></div>';
    const mount = document.getElementById('mb-native-mount'); if (mount) mount.append(panel); else workspace.before(panel);
    const source=bundle.sources['WindowsHost.codex']+'\n'+(bundle.sources['Deploy.codex']||'')+'\n'+(bundle.sources['Cleanup.codex']||'')+'\n'+bundle.sources['Helper.codex'];
    const programs=[];let busy='';
    const paint=()=>{for(const p of programs){p.build.disabled=!!busy;p.save.disabled=!!busy||!p.compiled;p.editor.disabled=busy==='compile';}};
    const select=p=>{for(const other of programs){const active=other===p;other.tab.setAttribute('aria-selected',String(active));other.tab.tabIndex=active?0:-1;other.view.hidden=!active;}p.highlight();};
    for(const kind of ['helper','uninstaller']){
      const uninstall=kind==='uninstaller',label=uninstall?'Uninstaller':'Helper',prefix='mb-'+kind;
      const view=document.createElement('div');view.id=prefix+'-source-panel';view.setAttribute('role','tabpanel');view.setAttribute('aria-labelledby',prefix+'-tab');view.hidden=uninstall;
      view.innerHTML='<p>'+(uninstall?'Stops the helper and removes its setup. Game copies, mods and saves are kept.':'Finds Valheim, connects this page, and handles deployment after your confirmation.')+'</p><div class="mb-native-editor"><pre aria-hidden="true"></pre><textarea id="'+prefix+'-code" aria-label="'+label+' source" spellcheck="false" wrap="off"></textarea></div>';
      panel.querySelector('#mb-native-editors').append(view);
      const row=document.createElement('div');row.className='mb-native-build-row';
      row.innerHTML='<div class="mb-actions"><button id="'+prefix+'-build">Compile '+kind+'</button><button id="'+prefix+'-save" aria-label="Save '+kind+' EXE as" disabled>Save EXE as...</button></div><p id="'+prefix+'-build-status" role="status">Compile '+kind+' before saving its EXE.</p>';
      panel.querySelector('#mb-native-build-rows').append(row);
      const p={view,tab:panel.querySelector('#'+prefix+'-tab'),editor:view.querySelector('textarea'),pre:view.querySelector('pre'),build:row.querySelector('#'+prefix+'-build'),save:row.querySelector('#'+prefix+'-save'),status:row.querySelector('[role=status]'),compiled:null};programs.push(p);
      p.editor.value=uninstall?source.replace('let uninstall = peek-qword 1054768 0 == 1 | text-contains command " --uninstall"','let uninstall = True'):source;
      p.highlight=()=>{const text=p.editor.value;p.pre.innerHTML=text.length>HL_PLAIN?esc(text):text.split('\n').map(line=>line.length>4096?esc(line):highlightLine(line)).join('\n');p.pre.append(document.createTextNode('\n'));p.pre.scrollTop=p.editor.scrollTop;p.pre.scrollLeft=p.editor.scrollLeft;};
      p.editor.addEventListener('scroll',()=>{p.pre.scrollTop=p.editor.scrollTop;p.pre.scrollLeft=p.editor.scrollLeft;});
      p.editor.addEventListener('input',()=>{p.compiled=null;p.status.textContent='Source changed. Compile '+kind+' before saving.';p.highlight();paint();});
      p.tab.onclick=()=>select(p);
      p.tab.onkeydown=event=>{if(['ArrowLeft','ArrowRight','Home','End'].includes(event.key)){event.preventDefault();const next=event.key==='Home'?programs[0]:event.key==='End'?programs[1]:programs.find(other=>other!==p);select(next);next.tab.focus();}};
      p.build.onclick=async()=>{
        if(busy)return;select(p);panel.open=true;p.compiled=null;busy='compile';paint();const text=p.editor.value;
        p.status.textContent='Compiling '+kind+' locally...';
        try{const bytes=await compile(text,returnUrl(),uninstall);if(text!==p.editor.value)throw new Error('Source changed during compilation. Compile again.');p.compiled={name:uninstall?'Uninstall-ModBuilder-v6.exe':'ModBuilder-Setup-v6.exe',bytes,source:text};p.status.textContent='Compiled. Save EXE as..., then open '+p.compiled.name+'.';window.dispatchEvent(new CustomEvent('modbuilder-helper-compiled',{detail:{uninstall}}));}
        catch(error){p.status.textContent=String(error.message||error);}
        finally{busy='';paint();}
      };
      p.save.onclick=async()=>{
        if(busy||!p.compiled)return;const artifact=p.compiled;busy='save';paint();
        try{await saveExecutable(artifact.name,artifact.bytes,()=>p.compiled===artifact&&p.editor.value===artifact.source);p.status.textContent='Saved. Open '+artifact.name+' from the location you chose.';window.dispatchEvent(new CustomEvent('modbuilder-helper-saved',{detail:{uninstall}}));}
        catch(error){p.status.textContent=error.name==='AbortError'?'Save cancelled. The compiled EXE is still ready.':String(error.message||error);}
        finally{busy='';paint();}
      };
    }
    select(programs[0]);
    panel.querySelector('#mb-helper-source-zip').onclick=()=>saveSource([...Object.entries(bundle.sources).map(([path,text])=>({path,text})),...programs.map((p,i)=>({path:i?'Edited-Uninstaller.codex':'Edited-Helper.codex',text:p.editor.value}))]);
    const query = new URLSearchParams(location.hash.slice(1));
    if (query.has('helper-source')) showSource(query.get('uninstall') === '1');
  }
  window.ModBuilderNative = {compile, showSource, attach};
})();
