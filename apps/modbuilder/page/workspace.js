'use strict';
document.title = 'Cobblestone ModBuilder';
const diskMode = location.protocol === 'file:';
let launcherParams = new URLSearchParams(location.hash.slice(1));
document.querySelector('.nav').remove();
document.querySelector('.header').id = 'workspace';
document.querySelector('.header h1').textContent = 'Cobblestone ModBuilder';
document.querySelector('.header .sub').textContent = 'Choose your features. Make them yours. Head back into the world.';
const gameBanner=document.createElement('div');gameBanner.className='mb-game-banner';
gameBanner.style.backgroundImage='url('+window.__MOD_ART.landscape+')';
gameBanner.innerHTML='<span class="mb-game-label">Selected game</span><img alt="Valheim" class="mb-game-logo"><span class="mb-game-edition">WINDOWS · SOLO PLAY</span>';
gameBanner.querySelector('img').src=window.__MOD_ART.logo;document.querySelector('.header').append(gameBanner);
document.querySelector('.footer').textContent = 'Your mod. Your world. Built locally in your browser.';
document.getElementById('lib-panel').hidden=true;
document.getElementById('note').remove();
const outputPanel=document.getElementById('pills-code').closest('.panel');
outputPanel.querySelector('.title').textContent='Export Valheim C# source';
const outputHelp=document.createElement('p');outputHelp.id='mb-output-help';
outputHelp.textContent='Generate Valheim C# to review or save for your own C# build and loader workflow. This source export does not build a DLL. To get a ready-to-deploy mod DLL without Visual Studio or a developer SDK, use Compile and build above; the embedded IL emitter runs locally in this page.';
outputPanel.querySelector('.panel-bar').after(outputHelp);
goBtn.textContent='Generate C#';
const finishSourcePreview=endBusy;
endBusy=()=>{finishSourcePreview();goBtn.textContent='Generate C#';};
document.getElementById('saveout').textContent='Save C# source';
OUT_EXT.unity = 'cs';
const unityPill = document.createElement('button');
unityPill.className = 'pill'; unityPill.dataset.plug = 'unity'; unityPill.textContent = 'Valheim C# source';
document.getElementById('pills-code').prepend(unityPill);
function chooseUnity() {
  document.querySelector('[data-tab="code"]').click();
  document.querySelectorAll('#pills-code .pill').forEach(p => p.classList.toggle('active', p === unityPill));
  lens = { plug: 'unity' }; updateRun();
}
unityPill.addEventListener('click', chooseUnity);
chooseUnity();
const actions = document.createElement('div'); actions.className = 'mb-actions';
actions.innerHTML = '<button class="go" id="mb-install">Compile, test &amp; install project</button>';
document.querySelector('.header').append(actions);
const message = document.createElement('div'); message.id = 'mb-message'; message.setAttribute('role', 'status');
document.querySelector('.header').append(message);
srcEl.addEventListener('input', () => { message.textContent = ''; });
templatesEl.addEventListener('change', () => { message.textContent = ''; chooseUnity(); });
const setup = document.createElement('details'); setup.className = 'mb-setup';
setup.id = 'features';
setup.open = true;
setup.innerHTML = '<summary>Build &amp; play</summary><iframe id="mb-forge" title="Step-by-step Valheim setup and installation"></iframe>';
const forgeFrame = setup.querySelector('#mb-forge');
let featurePickerVisible = false;
function takeLauncher(w) {
  const token = launcherParams.get('bridge');
  if (!token) return;
  const current = new URLSearchParams(location.hash.slice(1));
  for (const key of ['bridge', 'bridgeUrl', 'profile']) current.delete(key);
  const rest = current.toString();
  history.replaceState(null, '', location.pathname + location.search + (rest ? '#' + rest : ''));
  if (!/^[A-Za-z0-9_-]+$/.test(token)) throw new Error('The launcher token was refused.');
  const address = new URL(launcherParams.get('bridgeUrl') || 'http://127.0.0.1:8787');
  if (address.protocol !== 'http:' || address.hostname !== '127.0.0.1' || address.username || address.password || address.pathname !== '/' || address.search || address.hash) throw new Error('The launcher helper address must be a local loopback address.');
  w.local_storage_set('prism.bridge.url', address.origin);
  w.local_storage_set('prism.bridge.token', token);
  w.mba_set('b-url', address.origin); w.mba_set('b-token', token);
  const profile = launcherParams.get('profile');
  if (profile && address.port !== '8789') w.mba_take_profile(profile);
  w.mba_probe(0n);
  for (const key of ['bridge','bridgeUrl','profile']) launcherParams.delete(key);
  return true;
}
forgeFrame.srcdoc = window.__FORGE_HTML;
const container = document.querySelector('.container');
const advanced = document.createElement('details'); advanced.className = 'mb-code'; advanced.id = 'mb-code';
advanced.innerHTML = '<summary>Advanced: edit mod source and inspect generated C#</summary>';
container.before(setup, advanced); advanced.append(container);
forgeFrame.addEventListener('load', () => {
  try {
    const d = forgeFrame.contentDocument;
    const style = d.createElement('style');
    style.textContent = '.hero,.mb-project .game,footer{display:none}#mbw{margin:0}.forge{margin-top:16px}.mb-project #a-emit,.mb-project #a-build{display:none}';
    d.body.classList.toggle('mb-project', !featurePickerVisible);
    d.head.append(style);
    const w = forgeFrame.contentWindow;
    d.getElementById('a-forge').addEventListener('click', event => {
      if (d.body.classList.contains('mb-project')) { event.stopImmediatePropagation(); document.getElementById('mb-install').click(); }
    }, true);
    if (takeLauncher(w)) w.mbw.check();
    const resize = new ResizeObserver(() => { forgeFrame.style.height = Math.ceil(d.querySelector('main').getBoundingClientRect().height + 32) + 'px'; });
    resize.observe(d.querySelector('main'));
  } catch (error) { message.textContent = String(error.message || error); }
});
if (diskMode) {
  for (const link of document.querySelectorAll('.mb-features')) link.addEventListener('click', event => {
    event.preventDefault();
    featurePickerVisible = true;
    setup.open = true;
    if (forgeFrame.contentDocument?.body) forgeFrame.contentDocument.body.classList.remove('mb-project');
    if (forgeFrame.contentWindow.mbw) forgeFrame.contentWindow.mbw.show(3);
    setup.scrollIntoView({ block: 'start' });
  });
}
const installProject = document.getElementById('mb-install');
advanced.insertBefore(installProject, container);
installProject.textContent = 'Build edited project';
installProject.addEventListener('click', async () => {
  if (goBtn.disabled) return;
  installProject.disabled = true; goBtn.disabled = true;
  message.textContent = 'Compiling the current project...';
  try {
    const w = forgeFrame.contentWindow;
    if (!w || typeof w.mba_start !== 'function') throw new Error('Game setup is still loading. Try again when the setup form appears.');
    featurePickerVisible = false;
    w.document.body.classList.add('mb-project');
    if (w.mba_busy(0n)) throw new Error('An installation is already running. Wait for the receipt.');
    await w.mbw.prepare();
    const f = activePath && fileByPath(activePath); if (f) f.text = srcEl.value;
    const unit = await resolveUnit(assembleUnit());
    if (unit.missing.length) throw new Error('Missing chapters: ' + unit.missing.join(', '));
    const compiled = await runW(await moduleBytes('codex-compiler.wasm'), 'IR-UNI decks=125\n' + unit.text);
    const lines = compiled.text.split('\n'), first = lines.indexOf('IR-BEGIN'), last = lines.indexOf('IR-END');
    if (first < 0 || last <= first) throw new Error(compiled.text.split('\n').filter(line => /error CDX|CODEGEN/.test(line)).join('\n') || 'The compiler produced no IR. Open the build log after pressing Compile for diagnostics.');
    const emitted = await runW(await moduleBytes('unity-stdio.wasm'), plugInput('unity', lines.slice(first + 1, last).join('\n')));
    if (/^(REFUSED|\[UNSUPPORTED\]|!EXC|OUT OF MEMORY|!WASM)/m.test(emitted.text) || !emitted.text.includes('public static class PrismUnityEntry')) throw new Error(emitted.text || 'The Unity plug produced no mod entry.');
    const ids = window.__VALHEIM.features.filter(feature => emitted.text.includes(feature.marker + '.Install')).map(feature => feature.id);
    if (!ids.length) throw new Error('The project has no supported Valheim feature entry. Start from a Valheim preset.');
    chooseUnity(); lastRun = { body: emitted.text.split('\n') }; render(); updateOutActs();
    w.mba_set_sel(',' + ids.join(',') + ',');
    w.state_set_text('code', emitted.text);
    w.state_set_text('artifact', '');
    setup.open = true;
    w.mba_start('forge');
    message.textContent = 'Project compiled. Follow the build, test and installation receipts in Game setup below.';
  } catch (e) { message.textContent = String(e.message || e); setup.open = true; }
  finally { installProject.disabled = false; goBtn.disabled = false; }
});
function initModBuilder() {
  const query = new URLSearchParams(location.hash.slice(1));
  const preset = query.get('preset');
  if (preset && window.__TEMPLATES[preset]) startProject(window.__TEMPLATES[preset]);
  if (preset) {
    query.delete('preset');
    const rest = query.toString();
    history.replaceState(null, '', location.pathname + location.search + (rest ? '#' + rest : ''));
  }
  chooseUnity();
}
if (window.__prismReady) initModBuilder();
else window.addEventListener('prism-ready', initModBuilder, { once: true });
window.addEventListener('hashchange', () => {
  launcherParams = new URLSearchParams(location.hash.slice(1));
  try {
    const w = forgeFrame.contentWindow;
    if (w.mbw && takeLauncher(w)) w.mbw.check();
    if (launcherParams.has('preset')) initModBuilder();
  } catch (error) { message.textContent = String(error.message || error); setup.open = true; }
});
ModBuilderNative.attach();
