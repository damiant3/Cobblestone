# Codex Vivarium -- Roadmap

From aquarium to terrarium to game content pipeline.

## Where We Are (2026-06-10)

The FishTank is the proof of concept for a general-purpose creature
content pipeline. Today it does:

- **Structured scene data in Codex** -- species, body profiles, shaders,
  tank bounds, spawn orders all defined as typed records in `.codex`
  files, compiled through the CDX pipeline to generate HTML pages.
- **AI asset generation** -- Stable Diffusion Forge generates reference
  images and textures via API; TripoSR converts reference images to
  3D GLB meshes in ~2 seconds.
- **Creature database** -- JSON-backed record store keyed by species
  name, tracking pipeline state per creature (planned → reference →
  mesh → texture → rig → bake → export → ready).
- **WebGPU rendering** -- GLB meshes load into the browser with
  ABZU-style vertex-shader swim animation, normal-based lighting,
  depth fog. 8 species at 120fps.
- **Pipeline automation** -- `run-pipeline.ps1` orchestrates plan →
  reference → mesh → texture with zero manual steps. Forge and
  ComfyUI auto-launch.

### What Works

| Component | Status | Notes |
|-----------|--------|-------|
| FishTankEmit (Codex → HTML) | Working | CDX binary, serial output, CCE→UTF-8 |
| Forge reference images | Working | RealisticVision v5, white-bg isolation |
| Forge texture generation | Working | 6 maps per species (diffuse, normal, alpha) |
| TripoSR mesh generation | Working | GLB output, ~2s per species, PyMCubes fallback |
| GLB → WebGPU rendering | Working | Flat-color test confirmed geometry is correct |
| Vertex shader animation | Working | 4-layer ABZU-style: sway, wave, pivot, twist |
| Creature DB + pipeline | Working | plan/reference/mesh/texture stages automated |

### What's Next (immediate)

1. **Mesh scale normalization** -- TripoSR outputs in [0,1] space;
   need to normalize + scale to species dimensions during upload.
2. **Vertex color rendering** -- TripoSR bakes color into vertices;
   re-enable by fixing the CPU/CUDA device mismatch in the texture
   baking path, then read vertex colors in the shader.
3. **Better reference images** -- regenerate all 8 species from proper
   Forge prompts (isolated specimen on white), feed through TripoSR
   for clean 3D shapes instead of the current embossed-sprite GLBs.
4. **Texture atlas from vertex colors** -- project TripoSR's vertex
   colors onto a UV atlas so the existing texture pipeline works.

## Phase 2: The Terrarium

Same pipeline, different biome. A glass terrarium with:

### Inhabitants

| Creature | Kind | Animation | Challenge |
|----------|------|-----------|-----------|
| Leopard Gecko | Reptile | Walk cycle, tongue flick, tail wave, eye blink | Legs (4-bone IK per limb), belly crawl gait |
| Crested Gecko | Reptile | Climbing, jumping, licking eyeball | Vertical surfaces, toe-pad grip |
| Ball Python | Snake | Slither, coil, strike, tongue flick | Spline-based body (20+ bones), no limbs |
| Dart Frog | Amphibian | Hop, sit, throat pulse | Jump physics, landing squash |
| Hermit Crab | Crustacean | Walk, retract into shell, shell swap | Shell as separate mesh, 8 legs |
| Isopod | Bug | Scurry, roll into ball | Many legs, pill-bug curl |

### Environment

| Element | Type | Notes |
|---------|------|-------|
| Cork bark hide | Static mesh | Textured log with hollow interior |
| Live moss | Animated plant | Vertex-displaced carpet, responds to movement |
| Pothos vine | Semi-static | Draped geometry with leaf alpha planes |
| Water dish | Dynamic | Small fluid sim or animated normal map |
| Heat lamp spot | Lighting | Warm gradient on basking area, UVB glow |
| Substrate | Ground plane | Layered coconut fiber + leaf litter texture |
| Misting | Particle system | Periodic fog/droplet burst |

### What Changes From the Aquarium

- **Locomotion** replaces boids. Geckos don't school -- they patrol,
  bask, hunt, hide. Need a state machine (idle → explore → bask →
  hunt → sleep) instead of flocking.
- **Terrain interaction** -- creatures walk ON surfaces, not through
  volume. Need collision/navmesh on the terrarium floor and walls.
- **Legs and IK** -- fish are spine-only; reptiles need inverse
  kinematics for foot placement on uneven substrate.
- **Day/night cycle** drives behavior -- basking under the lamp by
  day, hiding in cork bark at night.

### What Stays the Same

- The creature database schema (CreatureRecord, SubUnit, TextureSpec,
  BoidParams becomes BehaviorParams).
- The pipeline scripts (plan → reference → mesh → texture → rig).
- The Forge + TripoSR toolchain.
- The WebGPU rendering architecture (instanced meshes, vertex shader
  animation, texture atlas).
- The Codex emitter pattern (structured data → compiled CDX → HTML).

## Phase 3: The Hamster Habitat

A multi-level hamster cage with tubes, wheels, and bedding.

### Inhabitants

| Creature | Animation | Behavior |
|----------|-----------|----------|
| Syrian Hamster | Run, dig, stuff cheeks, groom, sleep curled | Wheel running (procedural foot sync), tunnel navigation |
| Dwarf Hamster | Same but faster, more erratic | Pair housing, play-fighting |

### Environment Pieces

- Exercise wheel (procedural rotation synced to hamster run cycle)
- Tube system (spline-follow pathfinding through connected segments)
- Bedding heap (deformable -- hamster digs, creates tunnels)
- Food dish + water bottle (hamster approaches, eats, cheek-stuffs)
- Nesting area (hamster gathers bedding, builds nest shape)

### New Technical Challenges

- **Procedural fur** -- short-hair shell rendering or fin-like alpha
  planes for hamster fluff.
- **Deformable terrain** -- bedding that responds to digging.
- **Tube pathfinding** -- spline-follow through arbitrary tube
  connections (graph traversal + spline interpolation).
- **Object interaction** -- pick up food, stuff in cheeks (morph
  target for cheek pouch), carry to nest.

## Phase 4: The Game Content Pipeline

At this point we have:

1. **A creature definition schema** that works across biomes -- fish,
   reptiles, mammals, invertebrates.
2. **An automated asset pipeline** -- text description → reference
   image → 3D mesh → textured model → rigged + animated → deployed.
3. **A real-time renderer** capable of dozens of animated creatures
   with environment interaction.
4. **A database** tracking every creature, every texture, every mesh,
   every animation, queryable and resumable.

This IS a game content pipeline. The jump from "vivarium simulator"
to "game asset factory" is:

### What We Build

| Capability | Vivarium Foundation | Game Extension |
|------------|-------------------|----------------|
| Creature generation | TripoSR from reference | LoRA-trained species-specific models |
| Animation | Vertex shader + bones | Full skeletal with blend trees |
| Behavior | State machines | Behavior trees + utility AI |
| Environment | Static + particle | Procedural terrain + destructible |
| Multiplayer | Single viewer | Networked state sync |
| Economy | Feed/care | Breeding, trading, evolution |

### The Search Box Vision

The aquarium search box becomes the game's creature spawner:

1. Player types "mandarin dragonet"
2. DB lookup -- miss
3. LLM generates CreatureRecord (species data, prompts, body plan,
   behavior params) in ~2 seconds
4. Pipeline runs: reference image (~10s) → mesh (~2s) → texture
   bake (~15s) → rig (~5s) = ~30 seconds total
5. Creature appears in the world, fully animated, with correct
   colors, proportions, and behavior

No pre-made asset library. Every creature is generated on demand
from its biological description. The database caches results so
the same species is instant on second request.

### Target Games

| Game Type | Vivarium Tech Used | Additional Needs |
|-----------|-------------------|------------------|
| Aquarium tycoon | Everything | Economy, guest AI, tank building |
| Pet sim | Terrarium + hamster | Emotion system, aging, breeding |
| Creature collector | All biomes | Overworld, capture mechanic, evolution |
| Zoo builder | All biomes + terrain | Large-scale terrain, visitor pathfinding |
| Fishing game | Aquarium + rod physics | Water physics, catch mechanic, lure system |
| Survival crafting | All creatures as fauna | Player character, inventory, crafting |

## Technical Debt / Known Issues

- TripoSR texture baking has CPU/CUDA device mismatch when using
  PyMCubes fallback. Fix: move marching_cubes output to input device.
- Body diffuse texture prompts generate pictures of fish, not flat
  UV texture maps. Need better prompt engineering or img2img from
  the reference with ControlNet depth.
- The fishtank compiler pipeline can't handle source files > ~65KB
  due to a lexer issue (crashes during validate-escapes-loop at a
  specific content threshold). FishTankBridge (69KB of JS text
  constants) works around this by loading as separate `<script src>`.
- ComfyUI is installed but not yet integrated into the pipeline
  (Forge API is used directly). ComfyUI would enable multi-step
  workflows (reference → depth → normal → texture in one graph).
- No mesh decimation step -- TripoSR outputs ~30K vertex meshes,
  should be reduced to ~600 for instanced rendering at scale.

## File Map

```
apps/fishtank/
  pipeline/
    creature-db.ps1         -- DB helpers (save/load/find/plan)
    plan-creature.ps1       -- Create a creature record
    generate-reference.ps1  -- Forge API → reference images
    generate-mesh.ps1       -- TripoSR → GLB mesh
    generate-textures.ps1   -- Forge API → per-sub-unit textures
    run-pipeline.ps1        -- Full pipeline orchestrator
    batch-aquarium.ps1      -- Run all 12 default species
    launch-and-run.ps1      -- Auto-start Forge + ComfyUI + run
  creature-db/
    index.json              -- Species index for search
    <species>/
      reference/            -- side.png, top.png, front.png
      mesh/                 -- GLB from TripoSR
      textures/             -- Per-sub-unit texture maps
  FishTankTypes.codex       -- CreatureRecord schema + scene types
  FishTankScene.codex       -- 8 species instances
  FishTankEmit.codex        -- CDX page emitter
  FishTankShaders.codex     -- WGSL shaders (bg, sprite, particle, fish3d)
  web/
    fishtank.html           -- Generated HTML (from CDX pipeline)
    fishtank.js             -- WebGPU runtime (GLB loader, boids, render)
    models/                 -- GLB meshes per species
    assets/                 -- Sprite textures (v2 renderer)
```

## No Dates

The order above is the priority order. Each phase produces a
working demo. The pipeline generalizes with each biome.
