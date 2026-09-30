(() => {
  function attach(card,complete,next) {
    card.body.innerHTML='<fieldset class="mb-route-options"><legend>How would you like to deploy?</legend><label><input type="radio" name="mb-deploy-mode" value="website" checked> Use the website and helper</label><label><input type="radio" name="mb-deploy-mode" value="manual"> Save the DLL and deploy manually</label></fieldset><section id="mb-manual-deployment" hidden><p>Save PrismMod.dll for your own deployment workflow. No running helper is needed to build or save the DLL.</p><p>Valheim needs a compatible loader to start this managed DLL. Copying PrismMod.dll into the game folder alone will not load the mod. The website deployment packages the DLL into its winhttp.dll loader; the loader source is available below for your own packaging.</p><div class="mb-actions"><button id="mb-manual-save" disabled>Save PrismMod.dll...</button><button id="mb-loader-source">Loader source ZIP</button></div><p id="mb-manual-status" role="status"></p></section><section id="mb-assisted-deployment"><p>The helper installs your build into a separate Valheim target and backs up the previous loader.</p><fieldset class="mb-route-options"><legend>Deployment target</legend><label><input type="radio" name="mb-target-choice" value="existing"> Use an existing target</label><label><input type="radio" name="mb-target-choice" value="new" checked> Create a target</label></fieldset><div id="mb-existing-target"><p>Choose an existing full or lightweight target. Existing saves are kept.</p><button id="mb-deploy-choose">Choose existing target</button></div><div id="mb-new-target" hidden><label for="mb-copy-type">Target type</label> <select id="mb-copy-type"><option value="light">Lightweight: share Steam game data</option><option value="full">Full: copy all Valheim game files</option></select><p id="mb-copy-description"></p><p>New targets start with empty saves. Existing characters and worlds are not moved. Choose or create an empty folder in the folder dialog, or use the default location below.</p><p id="mb-default-folder"></p><div class="mb-actions"><button id="mb-deploy-create-folder">Choose or create a new folder...</button><button id="mb-deploy-create">Create in default folder</button></div></div><p><strong>Target:</strong> <span id="mb-deploy-destination">Choose a destination above.</span></p><button id="mb-deploy-check">Check again</button><p><label for="mb-deploy-file">Or select a saved PrismMod.dll</label> <input id="mb-deploy-file" type="file" accept=".dll"></p><p id="mb-deploy-artifact">Build a DLL above or choose the copy you saved.</p><p id="mb-redeploy-note"></p><button id="mb-deploy-now" disabled>Deploy this DLL</button><p id="mb-deploy-status" role="status">Connect the updated helper to enable deployment.</p><p id="mb-deploy-receipt"></p></section>';
    const id=name=>document.getElementById(name),status=id('mb-deploy-status'),button=id('mb-deploy-now');
    const update=document.createElement('button');update.id='mb-deploy-update';update.textContent='Update helper';update.onclick=()=>ModBuilderNative.showSource(false);status.after(update);
    next.body.innerHTML='<p id="mb-launch-build">Deploy a mod to see its build number here.</p><p>The game opens from the selected play copy with its separate Saves folder. Compare this build number with the value shown in the game.</p><div class="mb-actions"><button id="mb-launch-now" disabled>Launch Valheim</button><button id="mb-launch-check">Check again</button></div><p id="mb-launch-status" role="status"></p>';
    if(next.element===card.element)id('mb-assisted-deployment').append(next.body);
    let artifact=null,plan=null,capable=false,canLaunch=false,canLight=false,canFolders=false,busy=false,launchRequested=false,refreshId=0,installedAt='',targetChosen=false,connectionToken='';
    const manual=()=>document.querySelector('input[name="mb-deploy-mode"]:checked').value==='manual';
    const resetReceipt=()=>{installedAt='';card.element.classList.remove('done');id('mb-deploy-receipt').textContent='';id('mb-manual-status').textContent='';};
    const paint=()=>{
      const isManual=manual(),creating=document.querySelector('input[name="mb-target-choice"]:checked').value==='new',light=id('mb-copy-type').value==='light';
      id('mb-manual-deployment').hidden=!isManual;id('mb-assisted-deployment').hidden=isManual;id('mb-existing-target').hidden=creating;id('mb-new-target').hidden=!creating;
      id('mb-copy-description').textContent=light?'Shares the large Steam data folders through directory links. Keep Steam’s Valheim installation available; Steam updates affect the shared data.':'Copies the complete Valheim game folder. Uses more disk space and keeps separate game files.';
      if(creating&&light&&!canLight)id('mb-copy-description').textContent+=' Compile and run the updated helper in Setup helper to enable this option.';
      for(const input of card.body.querySelectorAll('input[type=radio],select'))input.disabled=busy;
      update.hidden=capable&&canLaunch&&canFolders&&(!creating||!light||canLight);button.disabled=busy||isManual||creating||!capable||!plan?.ready||plan.gameRunning||!artifact;
      id('mb-deploy-choose').disabled=busy||!capable;id('mb-deploy-create').disabled=busy||!capable||(light&&!canLight);id('mb-manual-save').disabled=busy||!artifact;
      id('mb-deploy-create-folder').disabled=busy||!capable||!canFolders||(light&&!canLight);
      id('mb-default-folder').textContent=plan?.defaultFolder?'Default location: '+plan.defaultFolder+(light?'\\Valheim-Light':'\\Valheim'):'The helper will use the ModBuilder folder beside the Steam library.';
      id('mb-redeploy-note').textContent=!creating&&(plan?.hasDeployment||plan?.buildNumber)?'This target already has '+(plan.buildNumber?'build '+plan.buildNumber:'a ModBuilder deployment')+'. Deploying replaces that deployment and backs up the previous loader. Your saves are kept.':'';
      for(const name of ['mb-deploy-check','mb-deploy-file','mb-launch-check'])id(name).disabled=busy;
      id('mb-launch-now').disabled=busy||creating||launchRequested||!canLaunch||!capable||!plan?.ready||!plan.buildNumber||plan.gameRunning;
      id('mb-launch-build').textContent=plan?.buildNumber?'Installed build: '+plan.buildNumber+' | '+plan.destination:'Deploy a mod with the updated helper to see its build number here.';
      next.result.textContent=launchRequested?'Launch requested':plan?.gameRunning?'Valheim is running':plan?.buildNumber?'Build '+plan.buildNumber:'Deploy a mod first';
    };
    const request=async(path,options={})=>{
      const frame=forgeFrame.contentWindow,token=frame?.mba_token(0n);
      if(!token)throw new Error('Connect the helper in Step 1 first.');
      const response=await fetch(frame.mba_bridge_url(0n).replace(/\/$/,'')+path,{...options,headers:{'X-Bridge-Token':token,...options.headers},signal:AbortSignal.timeout(600000)});
      const result=await response.json();if(!response.ok||!result.ok)throw new Error(result.err||'The helper refused this operation.');return result;
    };
    const refresh=async()=>{
      if(manual()){paint();return;}
      const ticket=++refreshId;
      try{
        const health=await request('/health');if(ticket!==refreshId)return;
        capable=health.tokenOk&&health.bridge==='modbuilder-native'&&health.capabilities?.install===true;
        canLaunch=health.capabilities?.launch===true;
        canLight=health.capabilities?.lightCopy===true;
        canFolders=health.capabilities?.targetFolders===true;
        id('mb-launch-status').textContent=canLaunch?'':'Update the helper in Step 1 to enable launch.';
        if(!capable)throw new Error('This helper cannot deploy yet. In Step 1, compile and run the updated helper, then return here.');
        const result=await request('/deployment');if(ticket!==refreshId)return;plan=result;launchRequested=false;
        const token=forgeFrame.contentWindow.mba_token(0n);
        if(token!==connectionToken){connectionToken=token;targetChosen=false;}
        if(!targetChosen)document.querySelector('input[name="mb-target-choice"][value="'+(plan.ready?'existing':'new')+'"]').checked=true;
        if(plan.steamReady===false)id('mb-launch-status').textContent='Steam must be running and signed in. Launch Valheim will open Steam first if needed.';
        id('mb-deploy-destination').textContent=plan.destination?(plan.targetType?plan.targetType==='light'?'Lightweight target: ':'Full target: ':'')+plan.destination:'Choose an existing target, or create a full or lightweight one.';
        status.textContent=plan.gameRunning?'Close Valheim before deploying, then choose Check again.':plan.message||'Destination verified. Ready to deploy your DLL.';
        card.result.textContent=plan.gameRunning?'Close Valheim':plan.ready?'Choose a DLL and deploy':'Choose a game copy';
        if(installedAt===plan.destination&&card.element.classList.contains('done')){card.result.textContent='Deployed to '+installedAt;status.textContent=plan.gameRunning?'Deployment complete. Close Valheim before deploying another build.':'Deployment complete. The installed file and backup are shown below.';}
      }catch(error){if(ticket!==refreshId)return;capable=false;canLaunch=false;plan=null;status.textContent=String(error.message||error);card.result.textContent='Helper update or connection needed';}
      paint();
    };
    const select=async path=>{
      if(busy)return;resetReceipt();busy=true;paint();status.textContent=path.includes('/create')?'Preparing the selected target type...':'Choose the game folder in the helper’s folder dialog...';
      try{await request(path,{method:'POST'});targetChosen=true;document.querySelector('input[name="mb-target-choice"][value="existing"]').checked=true;await refresh();}catch(error){status.textContent=String(error.message||error);}finally{busy=false;paint();}
    };
    id('mb-deploy-choose').onclick=()=>select('/deployment/select');id('mb-deploy-create').onclick=()=>select(id('mb-copy-type').value==='light'?'/deployment/create-light':'/deployment/create');id('mb-deploy-check').onclick=refresh;
    id('mb-deploy-create-folder').onclick=()=>select(id('mb-copy-type').value==='light'?'/deployment/create-folder-light':'/deployment/create-folder');
    for(const input of card.body.querySelectorAll('input[name="mb-deploy-mode"]'))input.onchange=()=>{paint();if(manual())card.result.textContent='Save DLL for manual deployment';else refresh();};
    for(const input of card.body.querySelectorAll('input[name="mb-target-choice"]'))input.onchange=()=>{targetChosen=true;paint();};
    id('mb-copy-type').onchange=paint;
    id('mb-loader-source').onclick=()=>document.getElementById('mb-helper-source-zip').click();
    id('mb-manual-save').onclick=async()=>{
      if(!artifact||busy)return;const selected=artifact;busy=true;paint();
      try{await ModBuilderManaged.save(selected.bytes,()=>artifact===selected&&(!selected.current||selected.current()));id('mb-manual-status').textContent='Saved PrismMod.dll. Build '+selected.build+'. Use your compatible loader and deployment workflow.';card.result.textContent='DLL saved for manual deployment';}
      catch(error){id('mb-manual-status').textContent=error.name==='AbortError'?'Save cancelled.':String(error.message||error);}
      finally{busy=false;paint();}
    };
    id('mb-deploy-file').onchange=async event=>{
      resetReceipt();artifact=null;paint();const file=event.target.files[0];if(!file)return;
      try{if(file.size<512||file.size>16777216)throw new Error('Choose the PrismMod.dll produced by Compile and build.');const bytes=new Uint8Array(await file.arrayBuffer()),build=ModBuilderManaged.buildNumber(bytes);artifact={bytes,name:file.name,kind:'file',build};id('mb-deploy-artifact').textContent='Selected saved DLL: '+file.name+' | Build '+build;await refresh();}catch(error){status.textContent=String(error.message||error);}paint();
    };
    button.onclick=async()=>{
      if(button.disabled||!artifact)return;const selected=artifact;
      if(selected.current&&!selected.current()){artifact=null;status.textContent='The project changed. Build it again or choose a saved DLL.';paint();return;}
      busy=true;++refreshId;paint();status.textContent='Confirm deployment in the helper dialog. The previous loader will be backed up.';
      try{
        const result=await request('/deployment',{method:'POST',headers:{'Content-Type':'application/octet-stream'},body:selected.bytes});
        if(!result.installed)throw new Error('The helper did not confirm installation.');
        installedAt=result.destination;
        if(result.buildNumber&&result.buildNumber!==selected.build)throw new Error('The installed build identifier differs from the selected DLL. Check the deployment before launching.');
        plan={...plan,destination:result.destination,buildNumber:result.buildNumber,hasDeployment:true,ready:true,gameRunning:false};
        id('mb-deploy-receipt').textContent='Build '+(result.buildNumber||selected.build)+' | Installed: '+result.artifact+' | Backup: '+result.backup;
        status.textContent='Deployment complete. Your original Steam installation and saves were kept.';complete(card,'Deployed to '+result.destination,next);
      }catch(error){status.textContent=String(error.message||error);card.result.textContent='Deployment stopped';card.element.open=true;}
      finally{busy=false;paint();}
    };
    id('mb-launch-now').onclick=async()=>{
      if(id('mb-launch-now').disabled)return;busy=true;++refreshId;paint();id('mb-launch-status').textContent='Starting Valheim...';
      try{const result=await request('/launch',{method:'POST'});if(!result.launched)throw new Error('The helper did not confirm launch.');launchRequested=true;plan={...plan,buildNumber:result.buildNumber};id('mb-launch-status').textContent='Launch requested for build '+result.buildNumber+'. Compare it with the build shown in the game.';next.element.classList.add('done');}
      catch(error){id('mb-launch-status').textContent=String(error.message||error);next.element.classList.remove('done');}
      finally{busy=false;paint();}
    };
    id('mb-launch-check').onclick=refresh;
    if(next.element!==card.element)next.element.addEventListener('toggle',()=>{if(next.element.open&&!busy)refresh();});
    window.addEventListener('modbuilder-connection',event=>{if(event.detail.connected&&event.detail.native)refresh();else{capable=false;plan=null;paint();}});
    card.element.addEventListener('toggle',()=>{if(card.element.open&&!busy)refresh();});
    paint();
    return {
      manual(){document.querySelector('input[name="mb-deploy-mode"][value="manual"]').checked=true;card.result.textContent='Save DLL for manual deployment';paint();},
      built(bytes,current){resetReceipt();const build=ModBuilderManaged.buildNumber(bytes);artifact={bytes,current,kind:'build',name:'PrismMod.dll',build};id('mb-deploy-file').value='';id('mb-deploy-artifact').textContent='Ready to deploy: Build '+build+' from Compile and build.';paint();},
      invalidate(){if(artifact?.kind==='build'){artifact=null;id('mb-deploy-artifact').textContent='Project changed. Build a new DLL or choose a saved one.';}resetReceipt();paint();}
    };
  }
  window.ModBuilderDeployment={attach};
})();
