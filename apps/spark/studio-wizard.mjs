export function createWizardSupport() {
  const styles = ['Sci-Fi Concept Art', 'High Fantasy', 'Cyberpunk', 'Historical / Period', 'Horror / Dark', 'Anime / Manga', 'Painterly / Fine Art', 'Pixel Art / Retro', 'Custom (describe below)'];
  const sizes = ['1024x1024', '1024x768', '768x1024', '1344x768', '768x1344'];
  const samplers = ['Euler', 'DPM++ 2M', 'DPM++ SDE', 'DPM++ 2M SDE', 'DPM++ 2M SDE Heun', 'DPM++ 3M SDE'];
  const common = new Set(('The This That These Those A An In On At To For With From By Is Are Was Were Has Have Had But And Or Not No It Its As If Each Every All Any Both Few More Most Other Some Such Than Too Very Can Will Just Should Now Also Into Over After Before Between Under Through During Without Within Along Following Across Behind Beyond Plus Except Up Out Around Down Off Above Near Here There Where When While They Them Their She He Her His We Our You Your Who What Which How Why Write Generate Create Make Style Scene Art').toLowerCase().split(' '));
  function glossary(story) {
    const words = String(story).split(/[ \n\r,.!?;:]+/).filter(Boolean);
    const terms = new Set();
    const trim = word => word.replace(/^["'()]+|["'()]+$/g, '');
    const proper = word => word.length >= 2 && /^\p{Lu}$/u.test(word[0]) && !common.has(word.toLowerCase());
    for (let i = 0; i < words.length; i++) {
      const word = trim(words[i]);
      if (!proper(word)) continue;
      const next = trim(words[i + 1] || '');
      if (proper(next)) { terms.add(word + ' ' + next); i++; } else terms.add(word);
    }
    return [...terms].slice(0, 30).join(', ');
  }
  function folderName(name) {
    const value = String(name).trim().replace(/[<>:"/\\|?*\u0000-\u001f]/g, '_').replace(/[. ]+$/g, '');
    if (!value || value === '.' || value === '..' || value.length > 128) throw new Error('Choose a project name of 1 to 128 characters.');
    return value;
  }
  function starterPrompts(draft) {
    const style = draft.style === 'Custom (describe below)' ? draft.styleNotes || 'cinematic concept art' : draft.style.toLowerCase();
    return `PROMPT 01 - "The World at First Glance"\nA sweeping establishing shot of the world of ${draft.name}. Show the environment from a high vantage point, revealing the scale and mood of the setting. Include key architectural or natural landmarks.\n\nRendered in ${style} style with dramatic lighting and rich atmospheric perspective.\n\nPROMPT 02 - "The Protagonist"\nA full character portrait of the main character. Show them in their signature outfit or armor, with characteristic pose and expression. The background hints at their origin or motivation.\n\n${style} style, painterly rendering with attention to material textures and lighting.\n\nPROMPT 03 - "A Moment of Conflict"\nA dynamic action scene capturing a pivotal confrontation. Multiple characters or forces clash in a visually striking composition. Include environmental storytelling details.\n\nHigh-energy composition with dramatic camera angle, ${style} style, cinematic color grading.\n`;
  }
  const effectivePrompts = draft => draft.prompts.trim() ? draft.prompts : starterPrompts(draft);
  const item = (label, promptAdd, negativeAdd = '', cfgNudge, stepsNudge) => ({ label, promptAdd, negativeAdd, cfgNudge, stepsNudge });
  function directions(style) {
    return { groups: [
      { category: 'Mood', emoji: '🌙', items: [item('Dark & Ominous', 'dark atmosphere, ominous mood, deep shadows, foreboding'), item('Bright & Hopeful', 'bright atmosphere, hopeful mood, warm golden light, uplifting', 'dark, gloomy'), item('Mysterious', 'mysterious atmosphere, ethereal fog, hidden details, enigmatic'), item('Epic & Grand', 'epic scale, grand vista, sweeping composition, awe-inspiring', 'small, cramped', 1, 5)] },
      { category: 'Lighting', emoji: '💡', items: [item('Golden Hour', 'golden hour lighting, warm sunset tones, long shadows'), item('Blue Hour', 'blue hour, twilight, cool ambient light, serene'), item('Dramatic Rim Light', 'dramatic rim lighting, backlit, silhouette edges, high contrast', 'flat lighting'), item('Neon / Artificial', 'neon lighting, artificial light sources, colorful reflections')] },
      { category: 'Composition', emoji: '📐', items: [item('Wide Establishing', 'wide establishing shot, panoramic composition, environmental storytelling', 'close-up'), item('Intimate Close-up', 'close-up shot, intimate framing, detailed textures, shallow depth of field'), item('Low Angle Hero', 'low angle shot, heroic perspective, imposing, powerful'), item("Bird's Eye", "bird's eye view, top-down perspective, map-like composition")] },
      { category: style, emoji: '🎨', items: [item('More Detail', 'highly detailed, intricate details, fine textures, sharp focus', 'blurry, simple', 1.5, 5), item('Painterly Loose', 'painterly style, loose brushstrokes, impressionistic, artistic', 'photorealistic', -1), item('Photorealistic', 'photorealistic, hyperrealistic, real photography, 8k', 'painting, illustration, cartoon', 2, 5), item('Stylized', 'stylized, graphic design, bold shapes, strong silhouettes')] }
    ] };
  }
  const pools = {
    colorThemes: ['bold saturated colors', 'muted earth tones', 'monochromatic palette', 'complementary color scheme', 'warm analogous palette', 'cool blue-green tones', 'high contrast black and gold', 'pastel dreamlike colors'],
    compositions: ['rule of thirds', 'centered symmetrical', 'diagonal dynamic', 'framing within frame', 'leading lines', 'wide panoramic', 'intimate close crop', 'layered depth planes'],
    inspirations: ['concept art style', 'matte painting look', 'digital illustration', 'oil painting aesthetic', 'watercolor wash', 'graphic novel style', 'studio ghibli inspired', 'art nouveau influenced'],
    moods: ['atmospheric and moody', 'bright and optimistic', 'dark and mysterious', 'serene and peaceful', 'tense and dramatic', 'whimsical and playful', 'melancholic and reflective', 'epic and awe-inspiring'],
    lightingSetups: ['dramatic side lighting', 'soft ambient occlusion', 'volumetric god rays', 'neon rim lighting', 'candlelight warmth', 'overcast diffused', 'harsh noon sun', 'bioluminescent glow'],
    samplerHints: ['', '', '', 'DPM++ 2M SDE', 'Euler a', '']
  };
  function files(draft) {
    folderName(draft.name);
    if (!String(draft.name).trim()) throw new Error('Project name is required.');
    if (!styles.includes(draft.style)) throw new Error('Choose a listed art style.');
    if (!sizes.includes(draft.size)) throw new Error('Choose a supported SDXL image size.');
    if (!samplers.includes(draft.sampler)) throw new Error('Choose a supported SDXL sampler.');
    const steps = Number(draft.steps), cfg = Number(draft.cfg);
    if (!Number.isInteger(steps) || steps < 1 || steps > 50) throw new Error('SDXL steps must be a whole number from 1 to 50.');
    if (!/^[0-9]+(?:\.[0-9]+)?$/.test(String(draft.cfg)) || String(draft.cfg).length > 6 || !Number.isFinite(cfg) || cfg < 0 || cfg > 30) throw new Error('CFG must be from 0 to 30.');
    if (draft.story.length > 16384 || draft.prompts.length > 131072 || draft.styleNotes.length > 8192 || draft.glossary.length > 4096) throw new Error('Wizard text exceeds its size limit.');
    const [width, height] = draft.size.split('x').map(Number);
    const summary = [...draft.story];
    const output = {
      'universe.json': JSON.stringify({ glossary: summary.slice(0, 200).join('') + (summary.length > 200 ? '…' : ''), properNouns: draft.glossary.split(',').map(s => s.trim()).filter(Boolean), storyFilePatterns: ['*.txt', '*.md'] }, null, 2)
    };
    if (draft.story) output['Story.md'] = '# ' + draft.name + '\n\n' + draft.story;
    output['art_directions.json'] = JSON.stringify(directions(draft.style), null, 2);
    output['creative_pools.json'] = JSON.stringify(pools, null, 2);
    output['ArtPrompts.txt'] = effectivePrompts(draft);
    output['spark_project.json'] = JSON.stringify({ version: 1, name: draft.name, outputDir: 'Concept', storyFiles: draft.story ? ['Story.md'] : [], promptsFile: 'ArtPrompts.txt', universeFile: 'universe.json', artDirectionsFile: 'art_directions.json', creativePoolsFile: 'creative_pools.json', refinePresetsFile: 'refine_presets.json', defaultSettings: { width, height, steps, cfgScale: cfg, sampler: draft.sampler, scheduler: 'karras' } }, null, 2);
    return output;
  }
  async function createProject(parent, draft, validate) {
    if (!parent || typeof parent.getDirectoryHandle !== 'function') throw new Error('Choose a writable parent folder first.');
    const output = files(draft), name = folderName(draft.name);
    const checked = validate(output['ArtPrompts.txt'], 0);
    if (!checked.ok) throw new Error(checked.error || 'Art prompts are invalid.');
    try { await parent.getDirectoryHandle(name); throw new Error('Project folder already exists: ' + name); }
    catch (error) { if (error.name !== 'NotFoundError') throw error; }
    const directory = await parent.getDirectoryHandle(name, { create: true });
    for await (const entry of directory.entries()) throw new Error('New project folder is not empty: ' + name);
    const written = [];
    try {
      for (const [path, text] of Object.entries(output)) {
        const handle = await directory.getFileHandle(path, { create: true });
        const writer = await handle.createWritable();
        try { await writer.write(text); await writer.close(); }
        catch (error) { try { await writer.abort(); } catch {} throw error; }
        written.push(path);
      }
    } catch (error) { throw new Error('Project creation incomplete in ' + name + '; ' + written.length + ' files written. ' + error.message); }
    return { directory, paths: written, count: checked.count };
  }
  return { styles, sizes, samplers, glossary, folderName, starterPrompts, effectivePrompts, files, createProject };
}

export function installStudioWizard(document, assistant, support, hooks) {
  const el = id => document.getElementById(id);
  const dialog = el('wizard');
  let step = 0, parent = null, writing = false;
  const titles = ['Project setup', 'Story and setting', 'Art style', 'Art prompts', 'Generation settings'];
  const read = () => ({ name: el('wizard-name').value.trim(), story: el('wizard-story').value, glossary: el('wizard-glossary').value, style: el('wizard-style').value, styleNotes: el('wizard-style-notes').value, prompts: el('wizard-prompts').value, size: el('wizard-size').value, steps: el('wizard-steps').value, cfg: el('wizard-cfg').value, sampler: el('wizard-sampler').value });
  const say = text => { el('wizard-status').textContent = text; if (!dialog.open) el('status').textContent = text; };
  const options = (id, values, selected) => {
    el(id).replaceChildren();
    for (const value of values) { const option = document.createElement('option'); option.value = value; option.textContent = value; el(id).appendChild(option); }
    el(id).value = selected;
  };
  options('wizard-style', support.styles, support.styles[0]);
  options('wizard-size', support.sizes, '1344x768');
  options('wizard-sampler', support.samplers, 'DPM++ 2M SDE');
  function refresh() {
    const busy = writing || assistant.busy();
    el('wizard-back').disabled = busy || step === 0;
    el('wizard-next').disabled = busy;
    el('wizard-next').style.display = step === 4 ? 'none' : '';
    el('wizard-create').style.display = step === 4 ? '' : 'none';
    el('wizard-create').disabled = busy || !parent;
    el('wizard-close').disabled = writing;
    el('wizard-folder').disabled = busy;
    el('wizard-story-draft').disabled = busy;
    el('wizard-prompts-draft').disabled = busy;
    el('wizard-starters').disabled = busy;
    el('new-project').disabled = busy;
    el('wizard-progress').textContent = 'Step ' + (step + 1) + ' of 5: ' + titles[step];
    for (let i = 0; i < 5; i++) {
      const section = el('wizard-step-' + i);
      section.style.display = i === step ? '' : 'none';
      for (const control of section.querySelectorAll('input,textarea,select')) control.disabled = writing;
    }
    el('wizard-assistant').style.display = step === 1 || step === 3 ? '' : 'none';
    if (step === 4) {
      try {
        const draft = read(), files = support.files(draft), valid = hooks.prompts(files['ArtPrompts.txt'], 0);
        if (!valid.ok) throw new Error(valid.error);
        el('wizard-review').textContent = 'Folder: ' + (parent?.name || '(choose parent in Setup)') + '/' + support.folderName(draft.name) + '\n' + valid.count + ' prompts\n\n' + Object.keys(files).join('\n');
      } catch (error) { el('wizard-review').textContent = error.message; el('wizard-create').disabled = true; }
    }
  }
  function modes(open) {
    for (const option of el('llm-action').options) {
      const wizard = option.value === 'synopsis' || option.value === 'prompts';
      option.disabled = open ? !wizard : wizard;
    }
    el('llm-action').value = open ? step === 3 ? 'prompts' : 'synopsis' : 'expand';
  }
  function show() {
    if (hooks.busy() || assistant.busy()) { say('Finish or cancel the current generation or project operation first.'); return; }
    assistant.discard();
    step = 0;
    el('wizard-assistant').appendChild(el('llm'));
    el('llm').open = true;
    el('llm-use-context').checked = false;
    modes(true);
    dialog.showModal();
    say('Draft or enter your text. Nothing is written until you choose Create project.');
    refresh();
  }
  function close() { if (!writing) { assistant.discard(); dialog.close(); } }
  async function chooseFolder() {
    if (writing || assistant.busy()) return;
    try { parent = await hooks.chooseFolder(); el('wizard-folder-name').textContent = 'Parent folder: ' + parent.name; say('A new named subfolder will be created here. Existing folders are refused.'); }
    catch (error) { say(error.name === 'AbortError' ? 'Folder selection cancelled; the previous choice is unchanged.' : 'Folder selection refused: ' + error.message); }
    refresh();
  }
  function next() {
    if (writing || assistant.busy()) return;
    try {
      const draft = read();
      if (step === 0) support.folderName(draft.name);
      if (step === 2 && draft.style === 'Custom (describe below)' && !draft.styleNotes.trim()) throw new Error('Describe your custom art style first.');
      if (step === 3) { const valid = hooks.prompts(support.effectivePrompts(draft), 0); if (!valid.ok) throw new Error(valid.error); }
      assistant.discard(); step = Math.min(4, step + 1); modes(true); refresh();
    } catch (error) { say(error.message); }
  }
  async function create() {
    if (writing || assistant.busy() || hooks.busy()) return;
    let made = null;
    try {
      const draft = read();
      writing = true; hooks.setBusy(true); refresh();
      say('Creating project files...');
      made = await support.createProject(parent, draft, hooks.prompts);
      await hooks.adopt(made.directory, made.paths, made.count);
      writing = false; hooks.setBusy(false); close();
      say('Created ' + draft.name + ' with ' + made.count + ' prompts. Image generation has not started.');
    } catch (error) { say((made ? 'Project files were created, but opening failed: ' : 'Project creation refused: ') + error.message); }
    finally { writing = false; hooks.setBusy(false); refresh(); assistant.refresh(); }
  }
  el('new-project').addEventListener('click', show);
  el('wizard-folder').addEventListener('click', chooseFolder);
  el('wizard-back').addEventListener('click', () => { if (!writing && !assistant.busy()) { assistant.discard(); step = Math.max(0, step - 1); modes(true); refresh(); } });
  el('wizard-next').addEventListener('click', next);
  el('wizard-close').addEventListener('click', close);
  el('wizard-create').addEventListener('click', create);
  el('wizard-story-draft').addEventListener('click', () => { el('llm-action').value = 'synopsis'; assistant.run(); });
  el('wizard-prompts-draft').addEventListener('click', () => { el('llm-action').value = 'prompts'; assistant.run(); });
  el('wizard-starters').addEventListener('click', () => {
    if (writing || assistant.busy()) return;
    el('wizard-prompts').value = support.starterPrompts(read()); el('wizard-count').value = '3'; say('Three starter prompts inserted. Edit them before creating the project.'); refresh();
  });
  for (const id of ['wizard-size', 'wizard-steps', 'wizard-cfg', 'wizard-sampler']) el(id).addEventListener('input', refresh);
  dialog.addEventListener('cancel', event => { if (writing) event.preventDefault(); else assistant.discard(); });
  dialog.addEventListener('close', () => { el('llm-home').appendChild(el('llm')); modes(false); assistant.refresh(); });
  refresh();
  return { show, close, chooseFolder, next, create, read, refresh, busy: () => writing };
}
