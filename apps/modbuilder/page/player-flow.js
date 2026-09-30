(() => {
  const flow = document.createElement('section'); flow.id = 'mb-player-flow';
  setup.before(flow); setup.id = 'mb-developer-setup'; setup.open = false;
  setup.hidden = true;
  setup.insertBefore(installProject, forgeFrame);
  actions.remove();
  const card = (id, number, title, note) => {
    const d = document.createElement('details'); d.id = id; d.className = 'mb-step-card';
    const summary = document.createElement('summary');
    summary.innerHTML = '<span class="mb-step-number">' + number + '</span><strong>' + title + '</strong><span class="mb-step-result">' + note + '</span>';
    const body = document.createElement('div'); body.className = 'mb-step-body'; d.append(summary, body); flow.append(d);
    return {element:d, body, result:summary.querySelector('.mb-step-result')};
  };
  const helper = card('mb-setup-card',1,'Setup helper','Review, compile, save and run'); helper.element.open = true;
  helper.body.innerHTML = '<p id="mb-connection-status" role="status">Checking for a connected helper...</p><div class="mb-actions"><button id="mb-build-helper">Build the helper</button></div><ol class="mb-helper-stages"><li>Review source</li><li>Compile code</li><li>Save EXE</li><li>Run, discover &amp; connect</li></ol><p>No Git, Visual Studio or SDK is needed. Open the saved helper EXE and allow setup. The helper finds Valheim and opens a connected page automatically.</p><div id="mb-native-mount"></div>';
  document.getElementById('mb-native-mount').append(document.getElementById('mb-native-source'));
  const manualBuild=document.createElement('button');manualBuild.id='mb-build-without-helper';manualBuild.textContent='Build without a helper for manual deployment';helper.body.querySelector('.mb-actions').append(manualBuild);
  document.getElementById('mb-build-helper').onclick=()=>ModBuilderNative.showSource(false);
  const features = card('features',2,'Choose your prebuilts','Create an integrated project');
  features.body.innerHTML = '<p id="mb-features-note">Connect the helper, or choose Build without a helper for manual deployment.</p><fieldset id="mb-feature-options" disabled></fieldset><div class="mb-actions"><button id="mb-create-project" disabled>Create project with selected features</button><button id="mb-use-current" disabled>Continue current project</button></div><p id="mb-feature-status" role="status"></p>';
  const projectCard = card('mb-project-card',3,'Compile and build','Choose features first');
  projectCard.body.innerHTML = '<p>Build PrismMod.dll locally with the embedded IL emitter. No Visual Studio or developer SDK is needed. To export Valheim C# and build it yourself, open Project files and generated output below.</p><div class="mb-actions"><button id="mb-compile-project" disabled>Compile and build</button><button id="mb-save-binary" disabled>Save DLL as...</button></div><p id="mb-project-status" role="status"></p>';
  advanced.querySelector('summary').textContent = 'Project files and generated output'; projectCard.body.append(advanced);
  const binary = {element:projectCard.element,body:document.createElement('div'),result:projectCard.result};
  binary.body.innerHTML = '<p id="mb-binary-status" role="status"></p>';projectCard.body.append(binary.body);
  ModBuilderManaged.sourcePanel(binary.body);
  const deploy = card('mb-deploy-card',4,'Deploy and launch','Choose a game copy');
  const launch = {element:deploy.element,body:document.createElement('section'),result:document.createElement('span')};
  launch.body.id='mb-launch-card';launch.body.className='mb-launch-section';launch.result.className='mb-launch-result';
  const options = document.getElementById('mb-feature-options'), create = document.getElementById('mb-create-project'), keep = document.getElementById('mb-use-current'), compileButton = document.getElementById('mb-compile-project');
  const saveBinary=document.getElementById('mb-save-binary');
  const choices = new Map(); let connected = false, manualMode=false, created = false, compiledSnapshot = null, compiledBinary=null, binaryBusy=false;
  const sourceSnapshot = () => JSON.stringify(project.files.filter(f=>isUnitFile(f.path)).map(f=>[f.path,f.path===activePath?srcEl.value:f.text]));
  let selected = ['hud'];
  try { const saved = JSON.parse(localStorage.getItem('modbuilder-valheim.features') || 'null'); if(Array.isArray(saved))selected=saved; } catch {}
  for (const feature of window.__VALHEIM.features) {
    const label = document.createElement('label'); label.className = 'mb-feature-choice';
    const box = document.createElement('input'); box.type = 'checkbox'; box.value = feature.id; box.checked = selected.includes(feature.id);
    const text = document.createElement('span'), title = document.createElement('strong'), description = document.createElement('span');
    title.textContent = feature.title; description.textContent = feature.blurb; text.append(title, description); label.append(box, text); options.append(label); choices.set(feature.id,box);
    box.onchange = () => {
      try { localStorage.setItem('modbuilder-valheim.features',JSON.stringify([...choices].filter(([,b])=>b.checked).map(([id])=>id))); } catch {}
      features.element.classList.remove('done'); features.result.textContent = 'Selection changed; recreate the project to apply';
    };
  }
  const complete = (step, text, next) => { step.element.classList.add('done'); step.result.textContent = text; step.element.open = next?.element===step.element; if (next) next.element.open = true; };
  const deployment=ModBuilderDeployment.attach(deploy,complete,launch);
  launch.body.prepend(launch.result);
  manualBuild.onclick=()=>{manualMode=true;options.disabled=false;create.disabled=false;keep.disabled=false;deployment.manual();document.getElementById('mb-connection-status').textContent='Manual deployment selected. A helper is not required to build or save your DLL.';document.getElementById('mb-features-note').textContent='Choose your features, then build and save the DLL.';complete(helper,'Manual DLL build',features);};
  const readyProject = text => { created = true; invalidateProject(); compileButton.disabled = false; complete(features,text,projectCard); projectCard.result.textContent = 'Ready to build'; advanced.open = false; };
  create.onclick = () => {
    const status = document.getElementById('mb-feature-status');
    if (!connected&&!manualMode) { status.textContent = 'Connect the helper, or choose Build without a helper for manual deployment.'; return; }
    const picked = window.__VALHEIM.features.filter(f=>choices.get(f.id).checked);
    if (!picked.length) { status.textContent = 'Select at least one feature.'; return; }
    const files = {};
    for(const feature of picked)for(const path of feature.sources)files[path]=window.__TEMPLATES[feature.id].files[path];
    files[window.__VALHEIM.entry] = 'Chapter: ' + window.__VALHEIM.entryChapter + '\n\nSection: Engine Entry\n\n  effect Process where\n' + picked.map(f=>'    '+f.effect).join('\n') + '\n\nSection: Entry\n\n  opening : [Process] Nothing = act\n' + picked.map(f=>'    '+f.start).join('\n') + '\n  end\n';
    const prior = project.files; startProject({title:'My Valheim pack',main:window.__VALHEIM.entry,files});
    if(project.files===prior){status.textContent='Project creation cancelled.';return;}
    chooseUnity(); status.textContent = ''; readyProject(picked.map(f=>f.title).join(', '));
  };
  keep.onclick = () => { if((connected||manualMode) && project.files.length){chooseUnity();readyProject('Continuing the current project');} };
  const invalidateProject = () => { compiledSnapshot=null;compiledBinary=null;deployment.invalidate();saveBinary.disabled=true;projectCard.element.classList.remove('done');projectCard.result.textContent=created?'Ready to build':'Choose features first';document.getElementById('mb-project-status').textContent='Compile and build the current project.';document.getElementById('mb-binary-status').textContent=''; };
  srcEl.addEventListener('input',invalidateProject); templatesEl.addEventListener('change',invalidateProject);
  new MutationObserver(()=>{if(compiledSnapshot!==null&&compiledSnapshot!==sourceSnapshot())invalidateProject();}).observe(treeEl,{childList:true,subtree:true,characterData:true});
  const compiledProject = snapshot => {
    compiledSnapshot=snapshot;document.getElementById('mb-project-status').textContent='Project checked. Building the DLL...';
    compiledBinary=null;saveBinary.disabled=true;binary.element.classList.remove('done');binary.result.textContent='Building';
  };
  const buildMod=async()=>{
    if(binaryBusy||compiledSnapshot===null||compiledSnapshot!==sourceSnapshot())return;
    const status=document.getElementById('mb-binary-status'),snapshot=compiledSnapshot;
    binaryBusy=true;compiledBinary=null;saveBinary.disabled=true;
    status.textContent='Building the mod DLL in your browser...';binary.result.textContent='Building';
    try{
      const file=activePath&&fileByPath(activePath);if(file)file.text=srcEl.value;
      const bytes=await ModBuilderManaged.compile(assembleUnit().text);
      if(snapshot!==compiledSnapshot||snapshot!==sourceSnapshot())throw new Error('The project changed during the build. Compile the current source again.');
      compiledBinary={bytes,snapshot};saveBinary.disabled=false;binary.result.textContent='DLL ready to save';
      deployment.built(bytes,()=>compiledSnapshot===snapshot&&sourceSnapshot()===snapshot);
      const build=ModBuilderManaged.buildNumber(bytes);
      status.textContent='Build '+build+'. This is the identifier shown in the game. Save a copy or deploy below.';
      document.getElementById('mb-project-status').textContent='PrismMod.dll is ready.';
      complete(projectCard,'Build '+build,deploy);
    }catch(error){status.textContent=String(error.message||error);binary.result.textContent='Build failed';binary.element.open=true;}
    finally{binaryBusy=false;}
  };
  saveBinary.onclick=async()=>{
    if(!compiledBinary)return;
    const artifact=compiledBinary,status=document.getElementById('mb-binary-status');saveBinary.disabled=true;
    const current=()=>compiledBinary===artifact&&artifact.snapshot===sourceSnapshot();
    try{
      await ModBuilderManaged.save(artifact.bytes,current);
      if(current()){status.textContent='Saved PrismMod.dll. Build '+ModBuilderManaged.buildNumber(artifact.bytes)+'. Keep it anywhere convenient; Deploy and launch handles installation.';complete(binary,'Build '+ModBuilderManaged.buildNumber(artifact.bytes),deploy);}
      else status.textContent='Saved the previous build. The project changed; compile and build the current source again.';
    }catch(error){status.textContent=error.name==='AbortError'?'Save cancelled. The compiled DLL is still ready.':String(error.message||error);}
    finally{saveBinary.disabled=!compiledBinary;}
  };
  let editorCompiling=false;
  new MutationObserver(()=>{
    if(!created)return;
    if(goBtn.classList.contains('busy')&&!editorCompiling){editorCompiling=true;invalidateProject();}
    else if(editorCompiling&&!goBtn.classList.contains('busy')){editorCompiling=false;}
  }).observe(goBtn,{attributes:true,attributeFilter:['class']});
  compileButton.onclick = async () => {
    if (!created || goBtn.disabled) return;
    const status = document.getElementById('mb-project-status'); compileButton.disabled = true; goBtn.disabled = true;
    invalidateProject(); const snapshot=sourceSnapshot();
    status.textContent = 'Compiling the integrated project...';
    try {
      const f=activePath && fileByPath(activePath);if(f)f.text=srcEl.value;
      const unit=await resolveUnit(assembleUnit());if(unit.missing.length)throw new Error('Missing chapters: '+unit.missing.join(', '));
      const result=await runW(await moduleBytes('codex-compiler.wasm'),'IR-UNI decks=125\n'+unit.text);
      const lines=result.text.split('\n'),first=lines.indexOf('IR-BEGIN'),last=lines.indexOf('IR-END');
      if(first<0||last<=first)throw new Error(result.text.slice(0,4000));
      const emitted=await runW(await moduleBytes('unity-stdio.wasm'),plugInput('unity',lines.slice(first+1,last).join('\n')));
      if(/^(REFUSED|\[UNSUPPORTED\]|!EXC|OUT OF MEMORY|!WASM)/m.test(emitted.text)||!emitted.text.includes('public static class PrismUnityEntry'))throw new Error(emitted.text||'The Unity plug emitted no mod entry.');
      if(snapshot!==sourceSnapshot())throw new Error('The project changed during compilation. Compile the current source again.');
      chooseUnity();lastRun={body:emitted.text.split('\n')};render();updateOutActs();
      compiledProject(snapshot);
      await buildMod();
    } catch(e){status.textContent=String(e.message||e);projectCard.result.textContent='Compilation failed';projectCard.element.classList.remove('done');projectCard.element.open=true;}
    finally{compileButton.disabled=false;goBtn.disabled=false;}
  };
  let checkingHelper=false;
  const checkHelper=async()=>{
    const w=forgeFrame.contentWindow;if(checkingHelper||!w?.mbw||document.hidden)return;
    checkingHelper=true;
    try{await w.mbw.check();}finally{checkingHelper=false;}
  };
  window.addEventListener('focus',checkHelper);
  document.addEventListener('visibilitychange',()=>{if(!document.hidden)checkHelper();});
  window.addEventListener('modbuilder-helper-compiled',e=>{if(!e.detail.uninstall)helper.body.querySelectorAll('.mb-helper-stages li').forEach((li,i)=>li.classList.toggle('done',i<2));});
  window.addEventListener('modbuilder-helper-saved',e=>{if(!e.detail.uninstall)helper.body.querySelectorAll('.mb-helper-stages li').forEach((li,i)=>li.classList.toggle('done',i<3));});
  window.addEventListener('modbuilder-connection',e=>{
    const state=e.detail, wasConnected=connected;connected=state.connected&&state.native&&state.inventory?.found===true;
    options.disabled=!(connected||manualMode);create.disabled=!(connected||manualMode);keep.disabled=!(connected||manualMode);
    const status=document.getElementById('mb-connection-status');
    if(connected){status.textContent='Valheim found: '+state.inventory.gamePath;helper.body.querySelectorAll('.mb-helper-stages li').forEach(li=>li.classList.add('done'));if(!wasConnected)complete(helper,'Valheim discovered; helper connected',features);document.getElementById('mb-features-note').textContent='Choose your features. Create project combines their source and entry points.';}
    else if(manualMode){helper.result.textContent='Manual DLL build';status.textContent='The helper is optional for manual deployment. You can build and save the DLL without connecting.';}
    else{helper.element.classList.remove('done');helper.result.textContent=state.connected?(state.native?'Valheim was not found':'Native helper not connected'):'Helper not connected';status.textContent=state.error||(state.connected?(state.native?'Valheim was not found in Steam. Locate the game in Steam and reopen the helper.':'The saved connection is the older developer bridge. Compile and open the native helper to connect this setup card.'):'Helper not connected. Build and open the helper to connect automatically.');}
  });
  for(const link of document.querySelectorAll('.mb-features'))link.addEventListener('click',e=>{e.preventDefault();e.stopImmediatePropagation();features.element.open=true;features.element.scrollIntoView({block:'start'});},true);
  checkHelper();
})();
