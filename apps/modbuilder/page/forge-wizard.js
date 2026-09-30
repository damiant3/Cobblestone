(() => {
  if (window.mbw) return;
  const byId = id => document.getElementById(id);
  const command = 'pwsh apps/modbuilder/run.ps1 -Workspace';
  const native = window.ModBuilderNative || (window.parent !== window && window.parent.ModBuilderNative);
  const nativeLimit = 'Use the workspace Build mod DLL and Deploy mod cards with the native helper. This older forge uses the developer bridge.';
  const fetchBridge = window.fetch.bind(window);
  window.fetch = async (input, options) => {
    try { return await fetchBridge(input, options); }
    catch (error) {
      if (String(input) === mba_bridge_url(0n) + '/target') return new Response(JSON.stringify({ ok: false, err: 'Local helper connection lost. Open ModBuilder.exe again and reconnect.' }), { headers: { 'Content-Type': 'application/json' } });
      throw error;
    }
  };
  const wizard = document.createElement('section'); wizard.id = 'mbw';
  wizard.innerHTML = `<h2>Get ready to play</h2><p class="mbw-intro">Complete the setup, then build and test your mod before installation.</p>
    <ol class="mbw-steps" aria-label="Mod setup progress">${['Connect helper','Choose game','Build','Test','Install'].map((name,i)=>`<li><span class="mbw-number">${i+1}</span><span>${name}</span></li>`).join('')}</ol>
    <section id="mbw-connect"><h3>1. Set up the helper</h3><p>Review the helper source, compile in your browser, save the EXE, then open the saved file. Allow setup to find Valheim and connect the workspace.</p>
    <div class="acts"><button id="mbw-source" type="button">Review source &amp; compile helper</button><button id="mbw-uninstall" type="button">Compile uninstaller</button></div><p class="hint">No Git, Visual Studio or SDK is needed for setup. Native preview: game discovery and connection are ready; playable-mod binaries are still in development. Windows can show a warning for the unsigned executable.</p>
    <div id="mbw-helper-status" role="status"></div><div class="acts"><button id="mbw-check" type="button">Check connection &amp; continue</button><button id="mbw-explore" type="button">Explore features first</button></div>
    <details id="mbw-connection"><summary>Advanced: existing helper or developer checkout</summary><div id="mbw-connection-fields"></div><div class="mbw-command"><code>${command}</code><button id="mbw-copy" type="button">Copy developer command</button></div><p class="hint">The older developer bridge builds mods using a checkout and installed Windows build tools.</p></details></section>
    <section id="mbw-game" hidden><h3>2. Choose your Valheim setup</h3><p>Check the original game folder, separate test saves and play copy. The original game stays unchanged.</p><div id="mbw-profile-fields"></div>
    <div id="mbw-profile-actions"></div><div id="mbw-game-status" role="status"></div><div class="acts"><button id="mbw-back" type="button">Back</button><button id="mbw-next" type="button">Use this game &amp; continue</button></div></section>
    <section id="mbw-build" hidden><div class="acts"><button id="mbw-edit-setup" type="button">Change helper or game</button></div><div id="mbw-build-content"></div></section>`;
  const style = document.createElement('style');
  style.textContent = `#mbw{margin:24px 0;padding:24px;border:1px solid var(--line);border-radius:12px;background:var(--stone)}#mbw h2{font-size:32px}#mbw h3{font-size:27px;margin:20px 0 12px}#mbw p{max-width:72ch;color:var(--ash);margin:10px 0}#mbw [hidden]{display:none!important}.mbw-steps{display:flex;list-style:none;gap:10px;margin:24px 0;padding:0}.mbw-steps li{flex:1;border-top:3px solid var(--line);padding-top:12px;color:var(--ash);font-size:15px}.mbw-steps li.current{border-color:var(--gold);color:var(--parch)}.mbw-steps li.done{border-color:var(--ok);color:var(--ok)}.mbw-number{display:block;font-weight:bold;font-size:20px}.mbw-command{display:flex;gap:12px;align-items:center;flex-wrap:wrap;padding:14px;border:1px solid var(--line);background:var(--ink);border-radius:8px}.mbw-command code{overflow-wrap:anywhere;font:14px var(--mono)}#mbw button{cursor:pointer}#mbw-copy{padding:8px 12px;background:var(--gold);color:var(--ink);border:0;border-radius:5px}#mbw details{margin-top:20px}#mbw summary{cursor:pointer;color:var(--gold)}#mbw-helper-status,#mbw-game-status{white-space:pre-wrap;margin-top:14px}#mbw input[aria-invalid=true]{border-color:var(--err)}#mbw .game{padding-top:20px}#mbw .forge{margin:24px 0}.hero{min-height:240px}.hero h1{font-size:clamp(38px,7vw,72px)}@media(max-width:640px){#mbw{padding:16px}.mbw-steps{gap:6px}.mbw-steps li{font-size:12px}.mbw-number{font-size:17px}}`;
  document.head.append(style);
  document.querySelector('main').prepend(wizard);
  const moveRow = (id, target) => byId(target).append(byId(id).closest('.row'));
  for (const id of ['b-url','b-token']) moveRow(id,'mbw-connection-fields');
  const oldHint = byId('b-state').closest('.hint'); byId('mbw-connection-fields').append(byId('b-state')); oldHint.remove();
  for (const id of ['p-saved','p-id','p-gamePath','p-testSavePath','p-installPath','p-groupLimit','p-links']) moveRow(id,'mbw-profile-fields');
  for (const [id,text] of Object.entries({
    'p-gamePath':'The original Valheim folder containing valheim.exe. The helper reads the game files here.',
    'p-testSavePath':'A separate folder outside the original game. The test run creates isolated test saves here.',
    'p-installPath':'An existing separate Valheim play copy containing valheim.exe. Make the copy before installing; the installer does not create a game copy.'
  })) { const hint=document.createElement('p');hint.className='hint';hint.textContent=text;byId(id).closest('.row').after(hint); }
  byId('mbw-profile-actions').append(byId('p-save').parentElement);
  const more = byId('more'); more.querySelector('summary').textContent = 'Advanced build actions and receipts';
  more.querySelectorAll('h3').forEach(h => { if (h.textContent !== 'Single steps') h.remove(); });
  for (const element of [document.querySelector('.game'),byId('forge'),more]) byId('mbw-build-content').append(element);
  byId('forge').querySelector('h2').textContent = 'Build your mod';
  byId('a-forge').querySelector('span:nth-child(2)').textContent = 'Build, test & install';
  let step = 1, connected = false, gameReady = false, checkId = 0, nativeConnected = false, inventory = null;
  const steps = [...wizard.querySelectorAll('.mbw-steps li')];
  function paint() {
    ['mbw-connect','mbw-game','mbw-build'].forEach((id,i) => { byId(id).hidden = step !== i+1; });
    let active = step;
    if (step === 3) { if (byId('pip-test').classList.contains('run')) active=4; if (byId('pip-install').classList.contains('run') || byId('pip-install').classList.contains('done')) active=5; }
    const done = [connected,gameReady,...['package','test','install'].map(id=>byId('pip-'+id).classList.contains('done'))];
    steps.forEach((node,i)=>{node.classList.toggle('current',i+1===active);node.classList.toggle('done',done[i]);if(i+1===active)node.setAttribute('aria-current','step');else node.removeAttribute('aria-current');});
  }
  function show(next) { step=next;paint(); }
  function profileProblem() {
    if(nativeConnected)return inventory?.found ? '' : 'Valheim was not found in the Steam libraries. Install or locate Valheim in Steam, then open the helper again.';
    const fields=['p-id','p-gamePath','p-testSavePath','p-installPath'];
    const missing=fields.filter(id=>!byId(id).value.trim());
    fields.forEach(id=>byId(id).setAttribute('aria-invalid',String(missing.includes(id))));
    return missing.length ? 'Your game setup is incomplete. Open the workspace with the launcher above, import a saved game profile, or fill the highlighted fields.' : mba_form_refusal(0n);
  }
  async function check() {
    const current=++checkId; connected=false;paint();
    let error='';byId('mbw-helper-status').textContent='Checking the local helper...';
    try {
      const token=mba_token(0n);
      if(!token)throw new Error('Helper not connected. Compile the helper source, save the EXE, then open the saved file.');
      let response;
      try { response=await fetch(mba_bridge_url(0n).replace(/\/$/,'')+'/health',{headers:{'X-Bridge-Token':token},signal:AbortSignal.timeout(4000)}); }
      catch(_){throw new Error('No helper connection was found. Open the saved helper EXE. Allow local-network access if your browser asks; connection is checked automatically when you return to this page.');}
      const health=await response.json();
      if(!response.ok || !health.ok || health.tokenOk!==true)throw new Error('The helper did not accept the saved connection. Open the saved helper EXE again to reconnect automatically.');
      if(current!==checkId)return false;
      nativeConnected=health.bridge==='modbuilder-native';inventory=health.inventory;
      if(nativeConnected && inventory){mba_set('p-id','valheim-native');mba_set('p-gamePath',inventory.gamePath||'');}
      byId('mbw-profile-actions').hidden=nativeConnected;
      for(const id of ['p-saved','p-id','p-testSavePath','p-installPath','p-groupLimit','p-links']){const row=byId(id).closest('.row');row.hidden=nativeConnected;if(row.nextElementSibling?.classList.contains('hint'))row.nextElementSibling.hidden=nativeConnected;}
      byId('p-gamePath').readOnly=nativeConnected;
      byId('mbw-game').querySelector('h3').textContent=nativeConnected?(inventory?.found?'2. Valheim found on your computer':'2. Valheim was not found'):'2. Choose your Valheim setup';
      byId('mbw-game').querySelector('p').textContent=nativeConnected?'Review the discovered game folder. Setup has not changed your game or saves.':'Check the original game folder, separate test saves and play copy.';
      byId('mbw-game-status').textContent=nativeConnected?(profileProblem()||nativeLimit):'';
      if(nativeConnected)show(2);
    } catch(e){error=String(e.message||e);}
    if(current!==checkId)return connected;
    connected=!error;byId('mbw-helper-status').textContent=error||'Connected. The local helper is ready.';paint();
    if(window.parent!==window)window.parent.dispatchEvent(new window.parent.CustomEvent('modbuilder-connection',{detail:{connected,native:nativeConnected,inventory,error}}));
    return connected;
  }
  async function prepare() {
    if(!await check()){show(1);throw new Error(byId('mbw-helper-status').textContent);}
    if(nativeConnected){byId('mbw-game-status').textContent=nativeLimit;show(2);throw new Error(nativeLimit);}
    const problem=profileProblem();
    if(problem){gameReady=false;byId('mbw-game-status').textContent=problem;show(2);throw new Error(problem);}
    gameReady=true;show(3);
  }
  byId('mbw-copy').onclick=async()=>{try{await navigator.clipboard.writeText(command);byId('mbw-copy').textContent='Copied';}catch(_){byId('mbw-helper-status').textContent='Select and copy the command above, then run it in PowerShell.';}};
  byId('mbw-source').onclick=()=>native?native.showSource(false):location.assign('workspace.html#helper-source');
  byId('mbw-uninstall').onclick=()=>native?native.showSource(true):location.assign('workspace.html#helper-source&uninstall=1');
  byId('mbw-check').onclick=async()=>{if(await check())show(2);};
  byId('mbw-explore').onclick=()=>show(3);
  byId('mbw-back').onclick=()=>show(1);
  byId('mbw-edit-setup').onclick=()=>show(1);
  byId('mbw-next').onclick=()=>{const problem=profileProblem();byId('mbw-game-status').textContent=problem|| (nativeConnected?nativeLimit:'');if(!problem){gameReady=true;if(!nativeConnected)byId('p-save').click();show(3);}};
  const start=mba_start;
  window.mba_start=job=>{
    if(job==='emit')return start(job);
    if(mba_busy(0n))return 0n;
    state_set('busy',1n);
    prepare().then(()=>{state_set('busy',0n);start(job);},error=>{state_set('busy',0n);mba_say(String(error.message||error),'err');});
    return 0n;
  };
  const observer=new MutationObserver(paint);
  for(const id of ['package','test','install'])observer.observe(byId('pip-'+id),{attributes:true,attributeFilter:['class']});
  window.mbw={prepare,check,show,profileProblem};paint();
  check();
})();
