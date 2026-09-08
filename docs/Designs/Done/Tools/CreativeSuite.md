# Planar Exchange Creative Suite

**Status:** Design (2026-05-26). Card Designer operational; remaining
tools in design.

## What This Is

Six AI-powered content creation tools for game assets, all served from
the same local web app (the SD Explorer at `localhost:8888`). They share
a cache directory, an SD/ComfyUI backend, and a TTS backend. Every tool
produces assets that feed directly into the Planar Exchange game
pipeline: card art, item icons, character portraits, battlefield
environments, composited card renders, and voice clips.

The tools:

| # | Tool | Status | Produces |
|---|------|--------|----------|
| 1 | Card Designer | Working | TCG card art from prompt exploration |
| 2 | Item Designer | Design | Weapon/armor/artifact icons for inventory UI |
| 3 | Character Designer | Design | Portraits, concept art, expression sheets, voice |
| 4 | Setting Designer | Design | Landscapes, dungeons, battlefields, town squares |
| 5 | ComfyUI Workflow Engine | Design | Layered card composites via ComfyUI JSON workflows |
| 6 | Voice Studio | Design | TTS dialogue, narration, ability callouts |

## Architecture

```
localhost:8888                          localhost:7860       localhost:8188
 SD Explorer Web App                    SD WebUI API        ComfyUI API
+----------------------------------+   +---------------+   +---------------+
| Tab: Card Designer               |   |               |   |               |
| Tab: Item Designer               |-->| txt2img       |   | /prompt       |
| Tab: Character Designer          |   | img2img       |   | /queue        |
| Tab: Setting Designer            |   | /sdapi/v1/*   |   | /history      |
| Tab: ComfyUI Workflows           |-->|               |-->|               |
| Tab: Voice Studio                |   +---------------+   +---------------+
+----------------------------------+
        |           |
        v           v
 SdExplorer.codex   server.ps1          TTS Backend (TBD)
 (CDX in VM,        (HTTP bridge,       +---------------+
  routing, state)    file I/O,          | Coqui XTTS    |
                     SD/ComfyUI/TTS     | or Bark        |
                     API proxy,         | localhost:????  |
                     cache mgmt)        +---------------+
        |
        v
 D:\Projects\CodexMagic\explorer\cache\{prompt_hash}\{params}.{ext}
```

### Component Roles

**SdExplorer.codex** (`apps/works/SdExplorer.codex`) -- the CDX binary
running bare-metal in codex-vm. Handles request routing, parameter
validation, state management, and JSON response construction. All six
tools share this single server; the tool is determined by the URL path.

**server.ps1** (`tools/web/explorer/server.ps1`) -- PowerShell HTTP
bridge. Accepts browser requests on port 8888, translates them into
serial-line protocol for the CDX, proxies SD/ComfyUI/TTS API calls,
manages the image/audio cache on disk, and serves static HTML/CSS/JS
for each tool's page.

**Cache** -- `D:\Projects\CodexMagic\explorer\cache\{prompt_hash}\{params}.png`
(or `.wav` for voice). Prompt hash is the first 12 hex chars of SHA-256
of the prompt text. Parameter encoding in the filename captures model,
sampler, steps, CFG, seed, and LoRA so the same prompt with different
settings produces separate cached files.

### API Endpoints

All served at `localhost:8888`.

| Method | Path | Tool | Description |
|--------|------|------|-------------|
| GET | `/` | All | Landing page with tool tabs |
| GET | `/card` | Card Designer | Card art explorer page |
| POST | `/card/generate` | Card Designer | Generate card art |
| GET | `/card/history` | Card Designer | Prompt history |
| GET | `/item` | Item Designer | Item icon designer page |
| POST | `/item/generate` | Item Designer | Generate item icon |
| GET | `/character` | Character Designer | Character creation page |
| POST | `/character/generate` | Character Designer | Generate portrait/concept |
| POST | `/character/voice` | Character Designer | Generate voice profile |
| GET | `/setting` | Setting Designer | Environment designer page |
| POST | `/setting/generate` | Setting Designer | Generate environment art |
| GET | `/comfyui` | Workflow Engine | Workflow editor page |
| POST | `/comfyui/execute` | Workflow Engine | Send workflow to ComfyUI |
| GET | `/comfyui/status` | Workflow Engine | Poll ComfyUI queue |
| GET | `/voice` | Voice Studio | Voice studio page |
| POST | `/voice/generate` | Voice Studio | Generate voice clip |
| GET | `/voice/profiles` | Voice Studio | List saved voice profiles |
| GET | `/cache/{hash}/{file}` | All | Serve cached image/audio |

## Tool 1: Card Designer (Working)

The original tool. Prompt-based exploration of Stable Diffusion
parameter space for TCG card art.

### Dimensions

Five parameter dimensions, each independently explorable:

| Dimension | Values | Effect |
|-----------|--------|--------|
| Model | SD 1.5, SDXL, checkpoints | Base aesthetic |
| Sampler | Euler a, DPM++ 2M Karras, DDIM, ... | Convergence character |
| Steps | 15-50 | Detail vs speed |
| CFG | 3-15 | Prompt adherence |
| LoRA | Per-model LoRA list | Style specialization |

### Pin-and-Cascade

Selecting (pinning) a value in one dimension locks it and cascades
the selection down. Example: pin a model, then explore samplers within
that model, then explore step counts within that sampler. Each level
narrows the parameter space. Unpinning a dimension reopens exploration
above it.

### Prompt History

A dropdown of previously used prompts, keyed by prompt text. Selecting
a history entry restores the prompt and all pinned parameters from that
session. History persists across server restarts via a JSON file in the
cache directory.

### Cache Strategy

Images are cached by prompt hash + parameter string:

```
cache/
  a1b2c3d4e5f6/                    # SHA-256(prompt)[:12]
    sdxl_euler-a_25_7.0_none.png   # model_sampler_steps_cfg_lora
    sdxl_euler-a_30_7.0_none.png
    sd15_dpm2m_20_9.0_fantasy.png
```

Cache hit skips the SD API call entirely and serves the file directly.

## Tool 2: Item Designer

Weapons, armor, potions, artifacts with stats and rarity tiers. Outputs
item icons suitable for inventory UI on transparent or dark backgrounds.

### Controls

**Item Type** -- determines silhouette and prompt framing:

| Type | Prompt Prefix | Aspect |
|------|---------------|--------|
| Sword | `game icon, fantasy sword,` | 1:1 |
| Axe | `game icon, fantasy battle axe,` | 1:1 |
| Staff | `game icon, ornate magic staff,` | 2:3 |
| Shield | `game icon, ornate shield,` | 1:1 |
| Helm | `game icon, fantasy helmet,` | 1:1 |
| Ring | `game icon, enchanted ring,` | 1:1 |
| Potion | `game icon, glowing potion bottle,` | 2:3 |
| Scroll | `game icon, ancient magic scroll,` | 2:3 |

**Rarity** -- controls color accent, glow intensity, and detail level:

| Rarity | Color | Glow | Detail Boost |
|--------|-------|------|--------------|
| Common | Green (#4CAF50) | None | +0 steps |
| Uncommon | Blue (#2196F3) | Subtle | +5 steps |
| Rare | Purple (#9C27B0) | Medium | +5 steps, +1 CFG |
| Mythic | Orange (#FF9800) | Strong | +10 steps, +2 CFG |
| Legendary-Mythic | Red (#F44336) | Intense | +15 steps, +3 CFG, upscale |

**Material** -- injected into the prompt as a material descriptor:

| Material | Prompt Fragment |
|----------|-----------------|
| Iron | `forged iron, dark metal,` |
| Mithril | `gleaming mithril, silver-blue metal,` |
| Crystal | `crystalline, translucent gemstone,` |
| Bone | `carved bone, ivory, skeletal,` |
| Living Wood | `living wood, vines, bark texture, druidic,` |

**Enchantment Glow** -- optional colored glow overlay. Applied as an
additional prompt fragment (`magical glow, enchanted aura, {color}
energy`) and as a post-processing compositing step (radial gradient
overlay in the glow color at 30% opacity).

### Record

```
ItemDesignParams = record {
  item-type : ItemType,
  rarity : Rarity,
  material : Material,
  enchant-glow : Maybe Color,
  custom-prompt : Text,         -- user additions to base prompt
  seed : Integer                -- -1 for random
}

ItemType =
  | Sword | Axe | Staff | Shield
  | Helm | Ring | Potion | Scroll

Material =
  | Iron | Mithril | Crystal | Bone | LivingWood
```

### Output

- 512x512 PNG (or 1024x1024 for Mythic+), transparent background
- Dark-background variant for preview
- Metadata JSON alongside: type, rarity, material, prompt used, seed

## Tool 3: Character Designer

RPG character creation producing portrait renders, full-body concepts,
expression sheets, and (future) voice profiles.

### Controls

**Race:**

| Race | Prompt Traits |
|------|---------------|
| Human | `human, realistic features,` |
| Elf | `elf, pointed ears, slender, ethereal,` |
| Dwarf | `dwarf, stocky build, broad shoulders, braided beard,` |
| Halfling | `halfling, small stature, youthful face, curly hair,` |

**Class:**

| Class | Prompt Traits | Costume Elements |
|-------|---------------|------------------|
| Warrior | `armored, battle-scarred,` | Plate armor, greatsword |
| Mage | `robed, arcane symbols,` | Staff, tome, glowing runes |
| Rogue | `hooded, leather armor, shadows,` | Daggers, cloak, lockpicks |
| Cleric | `holy vestments, divine light,` | Mace, holy symbol, shield |

**Personality Traits** -- three sliders (or discrete pickers), each a
spectrum:

| Trait | Low | High |
|-------|-----|------|
| Temperament | Calm, stoic | Fierce, passionate |
| Morality | Shadowy, pragmatic | Radiant, righteous |
| Demeanor | Grim, serious | Jovial, lighthearted |

Personality traits affect expression rendering (brow angle, mouth set,
eye intensity) and future voice profile generation (pitch, cadence,
tone).

**Backstory** -- free-text textarea. Injected into the art prompt as
thematic context. A backstory mentioning "raised by wolves in the
frozen north" shifts the palette toward cool blues and adds fur/wild
elements.

**Appearance** -- structured fields for hair color, eye color, skin tone,
distinguishing features (scars, tattoos, jewelry), age bracket
(young/mature/elder).

### Record

```
CharacterDesignParams = record {
  race : CharRace,
  class : CharClass,
  temperament : Integer,        -- 0-100 scale
  morality : Integer,           -- 0-100 scale
  demeanor : Integer,           -- 0-100 scale
  backstory : Text,
  hair-color : Text,
  eye-color : Text,
  skin-tone : Text,
  features : Text,              -- scars, tattoos, etc.
  age-bracket : AgeBracket,
  seed : Integer
}

CharRace = | Human | Elf | Dwarf | Halfling
CharClass = | Warrior | Mage | Rogue | Cleric
AgeBracket = | Young | Mature | Elder
```

### Output Suite

Three renders per character, all cached under the same prompt hash:

1. **Portrait** (2:3) -- head and shoulders, card-frame style, suitable
   for General avatar or card art. Background matches class/backstory
   theme.

2. **Full-body concept** (2:3) -- full figure with equipment, neutral
   pose, showing costume and silhouette. Background: gradient or
   environment hint.

3. **Expression sheet** (3:1 or grid) -- 4-6 facial expressions:
   neutral, happy, angry, sad, surprised, determined. Same face,
   different expressions. Useful for dialogue UI.

### Future: Voice Profile

Once the Voice Studio is operational, character creation gains a
"Generate Voice" button. Personality traits map to TTS parameters:

- Temperament slider -> pitch range and cadence variation
- Morality slider -> warmth (bright vs dark timbre)
- Demeanor slider -> speech speed and pause frequency
- Race/class -> accent hints and vocabulary style

The generated voice profile is saved and linked to the character,
enabling dialogue generation from the Voice Studio.

## Tool 4: Setting Designer

Landscapes, dungeons, battlefields, and town squares for use as
battlefield themes, loading screens, and dungeon crawl backdrops.

### Controls

**Biome:**

| Biome | Key Elements |
|-------|--------------|
| Forest | Dense canopy, dappled light, moss, ancient trees |
| Desert | Sand dunes, harsh sun, oasis, ruins half-buried |
| Cavern | Stalactites, bioluminescence, underground river |
| Ruins | Crumbling stone, overgrown, forgotten civilization |
| Volcanic | Lava flows, obsidian, ash clouds, ember rain |
| Tundra | Snow plains, ice formations, aurora, frozen lake |
| Ocean | Waves, coral reefs, sea cliffs, lighthouse |
| Urban | Medieval city, cobblestone, market stalls, towers |

**Time of Day:**

| Time | Lighting | Palette Shift |
|------|----------|---------------|
| Dawn | Warm golden, long shadows, mist | Amber/rose |
| Day | Bright, high contrast, clear | Full spectrum |
| Dusk | Orange-purple, dramatic clouds | Warm/violet |
| Night | Moonlight, cool blue, stars, torchlight | Blue/silver |

**Weather:**

| Weather | Visual Effect |
|---------|---------------|
| Clear | None -- pure biome/time rendering |
| Rain | Wet surfaces, puddles, falling rain, overcast |
| Fog | Reduced visibility, atmospheric depth, mystery |
| Storm | Lightning, dark clouds, wind-blown elements |
| Snow | Falling snow, frost, muted colors, cold breath |

**Scale:**

| Scale | Framing | Use Case |
|-------|---------|----------|
| Intimate | Close-up, single room or clearing | Encounter scenes |
| Medium | Building or grove, 30m field of view | Standard battlefield |
| Epic | Vast landscape, mountain range, city skyline | Loading screens, panoramas |

**Mood:**

| Mood | Prompt Modifiers |
|------|------------------|
| Peaceful | `serene, tranquil, idyllic, gentle light,` |
| Tense | `foreboding, shadows, something watching,` |
| Battle | `destruction, fire, combat aftermath, arrows,` |
| Mysterious | `enigmatic, ancient, runes, hidden paths,` |

### Aspect Ratio Presets

| Preset | Ratio | Resolution | Use Case |
|--------|-------|------------|----------|
| Background | 16:9 | 1920x1080 | Battlefield themes, loading screens |
| Token | 1:1 | 512x512 | Map tokens, tile art |
| Dungeon Map | 2:3 | 768x1152 | Vertical dungeon maps, encounter cards |

### Record

```
SettingDesignParams = record {
  biome : Biome,
  time-of-day : TimeOfDay,
  weather : Weather,
  scale : SettingScale,
  mood : Mood,
  aspect : AspectPreset,
  custom-prompt : Text,
  seed : Integer
}

Biome = | Forest | Desert | Cavern | Ruins | Volcanic | Tundra | Ocean | Urban
TimeOfDay = | Dawn | Day | Dusk | Night
Weather = | Clear | Rain | Fog | Storm | Snow
SettingScale = | Intimate | Medium | Epic
Mood = | Peaceful | Tense | Battle | Mysterious
AspectPreset = | Background16x9 | Token1x1 | DungeonMap2x3
```

### Output

Single image at the selected aspect ratio. Cached by prompt hash.
Metadata JSON records all parameters for reproducibility.

**Example outputs:**

- *Castle Blackstone at dusk, storm, epic, tense* -- a vast fortress on
  volcanic cliffs, lightning splitting the sky, dark spires against
  purple-orange clouds.
- *Elvish Tree City, day, clear, medium, peaceful* -- sunlit platforms
  woven through ancient trees, bridges of living wood, dappled green
  light.

## Tool 5: ComfyUI Workflow Engine

Layered compositing for card rendering. Instead of generating a
complete card image in a single SD pass, the workflow engine produces
each visual layer independently and composites them. Each layer can use
different models, steps, and prompts.

### Why Layers

A TCG card has distinct visual regions with different requirements:

| Layer | Content | Best Model/Settings |
|-------|---------|---------------------|
| Base Art | The illustration | High-step, artistic model |
| Card Frame | Border, frame shape | Crisp edges, img2img from template |
| Text Overlay | Card name, rules text | Rendered text (not SD) |
| Stat Badges | P/T/D numbers | Rendered (not SD) |
| Mana Symbols | Cost icons | Icon set or inpainted symbols |
| Rarity Border Glow | Edge glow effect | Post-processing (Gaussian + color) |

Generating all of this in one pass conflates "paint a dragon" with
"render crisp text" -- two tasks that want different settings. The
workflow engine separates them.

### Workflow Structure

A workflow is a JSON document describing a directed acyclic graph of
nodes. Each node is a ComfyUI operation (load model, sample, VAE decode,
composite, save). The engine constructs these graphs from layer
definitions.

```
CardWorkflow = record {
  card-name : Text,
  layers : List LayerDef,
  output-size : Dimensions,
  output-format : ImageFormat
}

LayerDef = record {
  layer-type : LayerType,
  z-order : Integer,
  source : LayerSource,
  blend-mode : BlendMode,
  opacity : Float,
  mask : Maybe MaskDef
}

LayerType =
  | BaseArt
  | CardFrame
  | TextOverlay
  | StatBadge
  | ManaSymbol
  | RarityBorderGlow

LayerSource =
  | SdGenerate SdParams          -- generate via txt2img
  | SdInpaint SdParams MaskDef   -- inpaint a region
  | ImageFile FilePath            -- load a pre-made asset (frame template)
  | RenderText TextRenderParams   -- render text to image
  | PostProcess PostProcessOp     -- glow, blur, color grade

BlendMode = | Normal | Multiply | Screen | Overlay | SoftLight
```

### Workflow Execution

1. User configures layers in the UI (or loads a preset workflow).
2. The engine constructs a ComfyUI-compatible JSON workflow.
3. POST to `localhost:8188/prompt` with the workflow JSON.
4. Poll `localhost:8188/history/{prompt_id}` for completion.
5. Retrieve output images, composite final result.
6. Cache the final composite and all intermediate layers.

### Preset Workflows

| Preset | Layers | Description |
|--------|--------|-------------|
| Standard Card | Base + Frame + Text + Stats + Mana + Glow | Full card render |
| Art Only | Base | Just the illustration, no frame |
| Frame Test | Frame + Glow | Test frame design without art |
| Premium Foil | Base + Frame + Text + Stats + Mana + Holo overlay | Foil/premium variant |

### Page Layout

The ComfyUI Workflow Engine is a second page (tab) in the same app.
Left panel: layer list with drag-to-reorder, per-layer settings
(source, blend, opacity). Right panel: live preview composite. Bottom:
workflow JSON view (read-only, for debugging or export).

## Tool 6: Voice Studio

TTS integration for character dialogue, narrator voice-over, and card
ability callouts.

### Backend

Local TTS model -- Coqui XTTS or Bark, running as an HTTP API (port
TBD). The PS1 bridge proxies requests the same way it proxies SD API
calls.

| Backend | Strengths | Limitations |
|---------|-----------|-------------|
| Coqui XTTS | Voice cloning, multilingual, fast | Requires voice samples for cloning |
| Bark | Expressiveness, non-speech audio, music | Slower, less consistent |

### Controls

**Character Voice Profile:**

```
VoiceProfile = record {
  profile-id : Hash,
  character-name : Text,
  pitch : Float,          -- 0.5 (deep) to 2.0 (high)
  speed : Float,          -- 0.5 (slow) to 2.0 (fast)
  tone : VoiceTone,
  reference-clip : Maybe FilePath  -- for XTTS voice cloning
}

VoiceTone =
  | Warm | Cold | Gravelly | Smooth
  | Ethereal | Commanding | Sly | Gentle
```

**Text Input** -- the dialogue or narration text to speak.

**Emotion Selector** -- modifies delivery:

| Emotion | Effect |
|---------|--------|
| Neutral | Baseline delivery |
| Excited | Faster pace, higher energy, wider pitch range |
| Angry | Louder, clipped, lower pitch |
| Sad | Slower, softer, falling intonation |
| Fearful | Breathy, faster, rising pitch |
| Commanding | Authoritative, measured, strong projection |
| Whispering | Quiet, breathy, intimate |

### Voice Profiles and Character Linking

Voice profiles are persistent. When created from the Character Designer
(via the "Generate Voice" button), the profile is linked to that
character's design params. When created standalone in the Voice Studio,
the profile is independent.

Profiles are stored as JSON in the cache:

```
cache/voices/
  {profile_id}.json      -- VoiceProfile record
  {profile_id}_ref.wav   -- reference clip (if XTTS)
```

Generated clips are cached alongside images:

```
cache/{prompt_hash}/
  voice_{emotion}_{seed}.wav
```

### Use Cases

| Use Case | Source | Output |
|----------|--------|--------|
| General match commentary | Narrator profile | "Your General rallies the troops!" |
| NPC dialogue | Character profile | In-character lines for story events |
| Dungeon crawl narrator | Narrator profile | "You enter a dimly lit chamber..." |
| Card ability callouts | Per-card profile | "Lightning bolt!" on cast |

### Output

WAV audio clips. Cached by prompt hash of the text + voice profile ID +
emotion + seed. Playable in-browser from the Voice Studio page with a
waveform visualizer.

## Shared Infrastructure

### Tab Navigation

All six tools are tabs in a single-page app at `localhost:8888`.
Navigation bar at top:

```
[ Card ] [ Item ] [ Character ] [ Setting ] [ ComfyUI ] [ Voice ]
```

Active tab highlighted. Each tab loads its control panel and output
gallery. Switching tabs preserves state (selections, history) in
browser `sessionStorage`.

### Prompt Construction

Each tool builds its SD prompt programmatically from the selected
parameters, then appends the user's custom prompt text. The final
prompt is what gets hashed for caching.

```
build-prompt : ToolParams -> Text
build-prompt (params) =
  let prefix = tool-prefix params
  let modifiers = param-modifiers params
  let custom = custom-text params
  in prefix & modifiers & custom
```

Negative prompts are tool-specific defaults (e.g., items always include
`text, watermark, signature, blurry` in the negative).

### Cache Management

All tools share one cache root:
`D:\Projects\CodexMagic\explorer\cache\`

```
cache/
  {hash}/             -- prompt hash directories (images)
    {params}.png
    {params}.json     -- metadata
  voices/             -- voice profiles
    {id}.json
    {id}_ref.wav
  history/            -- prompt history per tool
    card.json
    item.json
    character.json
    setting.json
  workflows/          -- saved ComfyUI workflows
    {name}.json
```

Cache cleanup is manual (no auto-eviction). Disk space is cheap; losing
a good generation is not.

### SD API Proxy

The PS1 bridge proxies all SD API calls through a single function
(`Invoke-SdApi`). Each tool's generate endpoint:

1. Builds the prompt from parameters.
2. Computes the prompt hash.
3. Checks cache -- if hit, returns cached image path immediately.
4. If miss, calls SD API (`/sdapi/v1/txt2img` or `/sdapi/v1/img2img`).
5. Saves result to cache directory.
6. Returns the cached path.

ComfyUI calls go through a separate proxy (`Invoke-ComfyApi`) targeting
`localhost:8188`.

TTS calls go through `Invoke-TtsApi` (endpoint TBD).

## File Plan

### Existing Files (modify)

| File | Changes |
|------|---------|
| `apps/works/SdExplorer.codex` | Add route handlers for /item, /character, /setting, /comfyui, /voice |
| `tools/web/explorer/server.ps1` | Add ComfyUI proxy, TTS proxy, voice profile storage, workflow file I/O |

### New Files

| File | Purpose |
|------|---------|
| `tools/web/explorer/index.html` | Main page with tab navigation, all six tool UIs |
| `tools/web/explorer/style.css` | Shared styles, tool-specific sections |
| `tools/web/explorer/app.js` | Tab switching, API calls, gallery rendering, history |
| `tools/web/explorer/comfyui.js` | Workflow editor, layer management, ComfyUI API client |
| `tools/web/explorer/voice.js` | Voice studio UI, waveform display, profile management |

### Static Assets

| File | Purpose |
|------|---------|
| `tools/web/explorer/assets/frames/` | Card frame templates (PNG) for ComfyUI compositing |
| `tools/web/explorer/assets/icons/` | Mana symbols, stat badges, rarity borders |

## Game Pipeline Integration

Every asset produced by the Creative Suite feeds into the Planar
Exchange game:

| Tool Output | Game Use |
|-------------|----------|
| Card art (Card Designer) | Card face illustration |
| Item icons (Item Designer) | Loot drops, inventory, crafting UI |
| Portraits (Character Designer) | General avatars, NPC faces |
| Full-body concepts (Character Designer) | Character select, story scenes |
| Environments (Setting Designer) | Battlefield themes, loading screens |
| Composited cards (Workflow Engine) | Final card renders for packs |
| Voice clips (Voice Studio) | Match commentary, NPC dialogue, narration |

The pipeline flow: designer tools produce raw assets -> QA review ->
approved assets enter the content pool -> the card generation pipeline
(see [CardGeneration.md](../CodexMagic/CardGeneration.md)) references
the pool when assembling card templates -> on-chain minting locks the
final asset permanently.

## Open Questions

1. **TTS backend choice.** Coqui XTTS gives better voice cloning but
   requires reference clips. Bark gives more expressiveness out of the
   box. Decision: start with Bark for ease of setup, add XTTS when
   voice cloning becomes a priority.

2. **TTS API port.** No standard. Need to pick a port and document it.
   Candidate: `localhost:7862` (one above SD img2img default).

3. **ComfyUI availability.** ComfyUI is optional -- not every developer
   will have it running. The Workflow Engine tab should degrade
   gracefully: show a "ComfyUI not detected" message and offer to
   export workflow JSON for later use.

4. **Frame templates.** Who designs the card frame PNGs? These are not
   AI-generated -- they need pixel-perfect edges and transparency. Either
   hand-designed or generated once and frozen.

5. **Voice profile sharing.** Should voice profiles be exportable and
   importable between developers? If so, the profile JSON + reference
   clip need a bundle format.

6. **Batch generation.** The Item Designer and Setting Designer would
   benefit from batch mode -- generate all rarities of a sword, or all
   times-of-day for a forest. Requires a queue system in the PS1 bridge.

7. **Upscaling pipeline.** Mythic+ items and epic-scale settings want
   higher resolution than base SD output. Should upscaling happen via
   SD extras API, a separate Real-ESRGAN call, or a ComfyUI upscale
   node?

8. **Expression sheet consistency.** Generating multiple expressions of
   the same face is notoriously inconsistent in SD. Options: use
   img2img with low denoising from the base portrait, use ControlNet
   with a face mesh, or use a dedicated face model. Needs
   experimentation.
