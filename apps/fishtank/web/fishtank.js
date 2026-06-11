// fishtank-v2.js — Codex Aquarium: Photorealistic Reef Tank
// Panoramic reef backdrop + billboarded fish sprites + foreground parallax.
// Designed to look like a video of a real reef aquarium.

'use strict';

// ============================================================
// 1. CONSTANTS
// ============================================================

const TANK = { xMin:-5, xMax:5, yMin:-4, yMax:3, zMin:-3, zMax:3 };
const MAX_FISH = 256;
const MAX_PARTICLES = 512;

const SPECIES = [
  { name:'Clownfish',  tex:'fish-clownfish',  w:3.0, h:2.0, speed:1.2,turn:3.5,school:3, sep:2.0,align:4.0,coh:5.0,solo:false },
  { name:'Angelfish',  tex:'fish-angelfish',   w:3.5, h:3.5, speed:0.6,turn:1.8,school:1, sep:3.0,align:0,  coh:0,  solo:true },
  { name:'Neon Tetra', tex:'fish-neontetra',   w:1.4, h:0.7, speed:1.8,turn:5.0,school:12,sep:0.8,align:2.5,coh:3.5,solo:false },
  { name:'Blue Tang',  tex:'fish-tang',        w:3.0, h:2.2, speed:0.9,turn:2.8,school:4, sep:2.0,align:4.0,coh:5.0,solo:false },
  { name:'Discus',     tex:'fish-discus',      w:3.0, h:3.0, speed:0.5,turn:1.5,school:2, sep:2.5,align:3.5,coh:5.0,solo:false },
  { name:'Guppy',      tex:'fish-guppy',       w:1.2, h:0.6, speed:2.2,turn:6.0,school:8, sep:0.6,align:2.0,coh:3.0,solo:false },
  { name:'Seahorse',   tex:'creature-seahorse', w:1.8,h:2.8, speed:0.3,turn:1.0,school:1, sep:2.5,align:0,  coh:0,  solo:true },
  { name:'Shrimp',     tex:'creature-shrimp',   w:1.4,h:0.7, speed:1.0,turn:4.0,school:2, sep:1.0,align:2.0,coh:3.0,solo:false },
];

// Foreground decoration sprites (parallax layers) — big, lush, overlapping
const FG_SPRITES = [
  // Far edges — tall coral framing the scene
  { tex:'fg-coral-left',   x:-5.5, y:-0.5, z:1.8, w:4.0, h:6.0 },
  { tex:'fg-coral-right',  x: 5.5, y:-0.5, z:1.8, w:4.0, h:6.0 },
  // Side rocks
  { tex:'fg-rock-left',    x:-4.0, y:-2.5, z:2.2, w:3.0, h:3.0 },
  { tex:'fg-rock-right',   x: 4.0, y:-2.5, z:2.2, w:3.0, h:3.0 },
  // Center-bottom coral cluster
  { tex:'fg-coral-center', x: 0.0, y:-2.5, z:3.0, w:5.0, h:3.5 },
  // Mid-ground fill
  { tex:'fg-coral-left',   x:-2.5, y:-2.0, z:1.2, w:2.5, h:3.5 },
  { tex:'fg-coral-right',  x: 2.5, y:-2.0, z:1.2, w:2.5, h:3.5 },
  { tex:'fg-rock-left',    x:-1.0, y:-3.0, z:2.5, w:2.0, h:1.5 },
  { tex:'fg-rock-right',   x: 1.0, y:-3.0, z:2.5, w:2.0, h:1.5 },
  // Extra fill for density
  { tex:'fg-coral-center', x:-3.5, y:-2.8, z:2.8, w:3.0, h:2.5 },
  { tex:'fg-coral-center', x: 3.5, y:-2.8, z:2.8, w:3.0, h:2.5 },
];

// ============================================================
// 2. FISH STATE
// ============================================================

let fishCount = 0;
const fishX = new Float32Array(MAX_FISH);
const fishY = new Float32Array(MAX_FISH);
const fishZ = new Float32Array(MAX_FISH);
const fishVX = new Float32Array(MAX_FISH);
const fishVY = new Float32Array(MAX_FISH);
const fishVZ = new Float32Array(MAX_FISH);
const fishAngle = new Float32Array(MAX_FISH);
const fishFlex = new Float32Array(MAX_FISH);
const fishSpecies = new Uint8Array(MAX_FISH);

let bubbleCount = 0, foodCount = 0, speckCount = 0;
const partX = new Float32Array(MAX_PARTICLES);
const partY = new Float32Array(MAX_PARTICLES);
const partZ = new Float32Array(MAX_PARTICLES);
const partVY = new Float32Array(MAX_PARTICLES);
const partSize = new Float32Array(MAX_PARTICLES);
const partAlpha = new Float32Array(MAX_PARTICLES);
const partType = new Float32Array(MAX_PARTICLES);
const partPhase = new Float32Array(MAX_PARTICLES);

let scatterTimer = 0, lightOn = 1.0, timeOfDay = 0.25;

// ============================================================
// 3. SHADERS
// ============================================================

// Background shader: draws fullscreen quad with god rays overlay
const SHADER_BG = `
struct U { time:f32, resX:f32, resY:f32, lightOn:f32, tod:f32, p1:f32, p2:f32, p3:f32 };
@group(0) @binding(0) var<uniform> u:U;
@group(0) @binding(1) var tex:texture_2d<f32>;
@group(0) @binding(2) var smp:sampler;

@vertex fn vs(@builtin(vertex_index) i:u32) -> @builtin(position) vec4f {
  var p = array<vec2f,3>(vec2f(-1,-1),vec2f(3,-1),vec2f(-1,3));
  return vec4f(p[i],0,1);
}

@fragment fn fs(@builtin(position) pos:vec4f) -> @location(0) vec4f {
  let uv = pos.xy / vec2f(u.resX, u.resY);

  // Sample the reef backdrop panorama — scroll very slowly for life
  let scroll = u.time * 0.003;
  let backUV = vec2f(fract(uv.x * 0.8 + 0.1 + scroll), uv.y);
  var col = textureSample(tex, smp, backUV).rgb;

  // Darken top area (water column above reef)
  let waterFade = smoothstep(0.0, 0.45, uv.y);
  let night = smoothstep(0.6, 1.0, abs(u.tod - 0.75) * 4.0);
  let dayAmt = 1.0 - night * 0.5;
  let waterCol = vec3f(0.02, 0.06, 0.15) * dayAmt;
  col = mix(col * dayAmt, waterCol, waterFade);

  // God rays from above
  var rays = 0.0;
  for (var i = 0u; i < 5u; i++) {
    let freq = 1.0 + f32(i) * 0.7;
    let ph = u.time * (0.15 + f32(i) * 0.06) + f32(i) * 1.3;
    let r = sin(uv.x * freq * 6.28 + ph) * 0.5 + 0.5;
    rays += pow(r, 12.0 + f32(i) * 6.0) * uv.y;
  }
  rays *= 0.08 * u.lightOn * dayAmt;
  col += vec3f(0.4, 0.6, 0.9) * rays;

  // Vignette
  let vig = 1.0 - length((uv - 0.5) * vec2f(1.2, 1.0)) * 0.6;
  col *= clamp(vig, 0.5, 1.0);

  return vec4f(col, 1);
}
`;

// Sprite shader: textured billboards for fish and foreground decorations
const SHADER_SPRITE = `
struct Cam { vp:mat4x4f, time:f32, fogR:f32, fogG:f32, fogB:f32, fogDens:f32, p1:f32, p2:f32, p3:f32 };
@group(0) @binding(0) var<uniform> cam:Cam;
@group(0) @binding(1) var tex:texture_2d<f32>;
@group(0) @binding(2) var smp:sampler;

struct VOut { @builtin(position) pos:vec4f, @location(0) uv:vec2f, @location(1) depth:f32 };

@vertex fn vs(
  @builtin(vertex_index) vi:u32,
  @location(0) ipos:vec3f,
  @location(1) ipar:vec4f,
  @location(2) iuv:vec4f
) -> VOut {
  var qx = array<f32,6>(-0.5, 0.5,-0.5, 0.5,-0.5, 0.5);
  var qy = array<f32,6>(-0.5,-0.5, 0.5,-0.5, 0.5, 0.5);
  var qu = array<f32,6>(0.0, 1.0, 0.0, 1.0, 0.0, 1.0);
  var qv = array<f32,6>(1.0, 1.0, 0.0, 1.0, 0.0, 0.0);

  // Fish wiggle: sine wave along body, amplitude grows toward tail
  let swimPhase = cam.time * 7.0 + ipar.w;
  let wiggle = sin(swimPhase + qx[vi] * 5.0) * max(0.4 - qx[vi], 0.0) * 0.08;

  let lx = qx[vi] * ipar.y;
  let ly = qy[vi] * ipar.z + wiggle;

  // Billboard: face camera but rotate by heading angle on Y axis
  let ca = cos(ipar.x); let sa = sin(ipar.x);
  let wp = ipos + vec3f(lx * ca, ly, lx * sa);

  var o:VOut;
  o.pos = cam.vp * vec4f(wp, 1);

  // Map into atlas region; flip U when fish faces left (cos < 0)
  var texU = qu[vi];
  if (ca < 0.0) { texU = 1.0 - texU; }
  o.uv = iuv.xy + vec2f(texU, qv[vi]) * iuv.zw;
  o.depth = length(wp - vec3f(0, 0, 8));
  return o;
}

@fragment fn fs(@location(0) uv:vec2f, @location(1) depth:f32) -> @location(0) vec4f {
  let c = textureSample(tex, smp, uv);
  if (c.a < 0.2) { discard; }

  // Keep fish vivid — boost colors slightly, subtle underwater tint
  var fc = c.rgb * 1.1;

  // Very gentle depth fog for far-away fish only
  let fog = 1.0 - exp(-depth * cam.fogDens * 0.15);
  let fogCol = vec3f(cam.fogR, cam.fogG, cam.fogB);
  fc = mix(fc, fogCol, clamp(fog, 0.0, 0.4));

  // Gentle blue underwater shift
  fc = fc * 0.95 + vec3f(0.008, 0.015, 0.03);

  return vec4f(fc, c.a * 0.95);
}
`;

// Particle shader (unchanged)
const SHADER_PARTICLE = `
struct Cam { vp:mat4x4f, time:f32, p1:f32, p2:f32, p3:f32, p4:f32, p5:f32, p6:f32, p7:f32 };
@group(0) @binding(0) var<uniform> cam:Cam;
struct VOut { @builtin(position) pos:vec4f, @location(0) co:vec2f, @location(1) alpha:f32, @location(2) ptype:f32 };
@vertex fn vs(@builtin(vertex_index) vi:u32, @location(0) ipos:vec3f, @location(1) ipar:vec4f) -> VOut {
  var qx=array<f32,6>(-1.0,1.0,-1.0,1.0,-1.0,1.0);var qy=array<f32,6>(-1.0,-1.0,1.0,-1.0,1.0,1.0);
  let sz=ipar.x;let wobble=sin(cam.time*3.0+ipar.w)*sz*0.2;
  let wp=ipos+vec3f(qx[vi]*sz+wobble,qy[vi]*sz,0.0);
  var o:VOut;o.pos=cam.vp*vec4f(wp,1);o.co=vec2f(qx[vi],qy[vi]);o.alpha=ipar.y;o.ptype=ipar.z;return o;}
@fragment fn fs(@location(0) co:vec2f,@location(1) alpha:f32,@location(2) ptype:f32)->@location(0) vec4f{
  let d=length(co);if(d>1.0){discard;}
  if(ptype<0.5){let ring=smoothstep(0.65,0.8,d)*(1.0-smoothstep(0.8,1.0,d));let hl=smoothstep(0.0,0.35,1.0-length(co-vec2f(-0.3,0.3)));return vec4f(0.7,0.88,1.0,(ring*0.5+hl*0.5)*alpha);}
  else if(ptype<1.5){let c=1.0-smoothstep(0.5,1.0,d);return vec4f(0.85,0.55,0.2,c*alpha);}
  else{let c=1.0-smoothstep(0.2,1.0,d);return vec4f(0.6,0.7,0.85,c*alpha*0.25);}}
`;

// ============================================================
// 3b. FISH MESH SHADER (ABZU-style vertex animation, flat color test)
// ============================================================

const SHADER_FISH3D = `
struct Cam { vp:mat4x4f, time:f32, fogR:f32, fogG:f32, fogB:f32, fogDens:f32, p1:f32, p2:f32, p3:f32 };
@group(0) @binding(0) var<uniform> cam:Cam;

struct VOut { @builtin(position) pos:vec4f, @location(0) uv:vec2f, @location(1) norm:vec3f, @location(2) depth:f32, @location(3) col:vec3f };

@vertex fn vs(
  @location(0) mpos:vec3f,
  @location(1) mnorm:vec3f,
  @location(2) muv:vec2f,
  @location(3) spine_t:f32,
  @location(4) ipos:vec3f,
  @location(5) ipar:vec4f,
  @location(6) vcol:vec3f
) -> VOut {
  var p = mpos;

  let phase = cam.time * 6.0 * ipar.w + ipar.z;
  let mask = smoothstep(0.2, 0.8, spine_t);

  p.z = p.z + sin(phase * 0.7) * 0.02;

  let wave = sin(phase + spine_t * 8.0) * mask * 0.06;
  p.z = p.z + wave;

  let pivot = sin(phase * 0.5) * 0.04;
  let cp = cos(pivot); let sp = sin(pivot);
  let px = p.x * cp - p.z * sp;
  let pz = p.x * sp + p.z * cp;
  p.x = px; p.z = pz;

  let twist = sin(phase * 0.9) * mask * 0.03;
  let ct = cos(twist); let st = sin(twist);
  let ty = p.y * ct - p.z * st;
  let tz = p.y * st + p.z * ct;
  p.y = ty; p.z = tz;

  p = p * ipar.y;

  let ca = cos(ipar.x); let sa = sin(ipar.x);
  let rx = p.x * ca - p.z * sa;
  let rz = p.x * sa + p.z * ca;
  let wp = ipos + vec3f(rx, p.y, rz);

  let nx = mnorm.x * ca - mnorm.z * sa;
  let nz = mnorm.x * sa + mnorm.z * ca;

  var o:VOut;
  o.pos = cam.vp * vec4f(wp, 1);
  o.uv = muv;
  o.norm = vec3f(nx, mnorm.y, nz);
  o.depth = length(wp - vec3f(0, 0, 8));
  o.col = vcol;
  return o;
}

@fragment fn fs(@location(0) uv:vec2f, @location(1) norm:vec3f, @location(2) depth:f32, @location(3) col:vec3f) -> @location(0) vec4f {
  let n = normalize(norm);
  let lightDir = normalize(vec3f(0.3, 1.0, 0.5));
  let facing = dot(n, lightDir) * 0.5 + 0.5;
  var lit = col * (0.5 + facing * 0.5);
  lit = lit + vec3f(0.02, 0.04, 0.08) * max(dot(n, vec3f(0, 1, 0)), 0.0);
  let fog = 1.0 - exp(-depth * cam.fogDens * 0.2);
  let fogCol = vec3f(cam.fogR, cam.fogG, cam.fogB);
  let fc = mix(lit, fogCol, clamp(fog, 0.0, 0.5));
  return vec4f(fc, 1.0);
}
`;

// ============================================================
// 3c. PROCEDURAL FISH MESH GENERATOR
// ============================================================

// Per-species body profiles: array of {t, radiusY, radiusZ} stations along spine
// t: 0=nose, 1=tail. radiusY=height, radiusZ=depth (lateral thickness)
const FISH_BODY_PROFILES = {
  'Clownfish': [
    {t:0,ry:0.01,rz:0.01},{t:0.08,ry:0.10,rz:0.08},{t:0.20,ry:0.18,rz:0.12},
    {t:0.35,ry:0.22,rz:0.14},{t:0.50,ry:0.20,rz:0.12},{t:0.65,ry:0.15,rz:0.09},
    {t:0.80,ry:0.06,rz:0.03},{t:0.90,ry:0.02,rz:0.01},{t:1.0,ry:0.12,rz:0.005}
  ],
  'Angelfish': [
    {t:0,ry:0.01,rz:0.01},{t:0.10,ry:0.15,rz:0.05},{t:0.25,ry:0.35,rz:0.07},
    {t:0.40,ry:0.40,rz:0.08},{t:0.55,ry:0.35,rz:0.07},{t:0.70,ry:0.20,rz:0.05},
    {t:0.85,ry:0.04,rz:0.02},{t:0.92,ry:0.02,rz:0.01},{t:1.0,ry:0.18,rz:0.004}
  ],
  'Neon Tetra': [
    {t:0,ry:0.01,rz:0.01},{t:0.10,ry:0.06,rz:0.05},{t:0.25,ry:0.10,rz:0.08},
    {t:0.40,ry:0.12,rz:0.09},{t:0.55,ry:0.10,rz:0.07},{t:0.70,ry:0.06,rz:0.04},
    {t:0.85,ry:0.02,rz:0.015},{t:0.93,ry:0.01,rz:0.005},{t:1.0,ry:0.06,rz:0.003}
  ],
  'Blue Tang': [
    {t:0,ry:0.01,rz:0.01},{t:0.08,ry:0.12,rz:0.06},{t:0.22,ry:0.28,rz:0.09},
    {t:0.38,ry:0.32,rz:0.10},{t:0.55,ry:0.28,rz:0.08},{t:0.70,ry:0.15,rz:0.05},
    {t:0.85,ry:0.03,rz:0.015},{t:0.93,ry:0.015,rz:0.008},{t:1.0,ry:0.14,rz:0.004}
  ],
  'Discus': [
    {t:0,ry:0.01,rz:0.01},{t:0.08,ry:0.12,rz:0.04},{t:0.20,ry:0.35,rz:0.06},
    {t:0.40,ry:0.42,rz:0.06},{t:0.60,ry:0.35,rz:0.05},{t:0.75,ry:0.15,rz:0.03},
    {t:0.88,ry:0.03,rz:0.01},{t:0.94,ry:0.015,rz:0.008},{t:1.0,ry:0.10,rz:0.003}
  ],
  'Guppy': [
    {t:0,ry:0.01,rz:0.01},{t:0.10,ry:0.05,rz:0.04},{t:0.25,ry:0.08,rz:0.06},
    {t:0.40,ry:0.09,rz:0.07},{t:0.55,ry:0.07,rz:0.05},{t:0.70,ry:0.04,rz:0.03},
    {t:0.82,ry:0.02,rz:0.01},{t:0.90,ry:0.01,rz:0.008},{t:1.0,ry:0.10,rz:0.003}
  ],
  'Seahorse': [
    {t:0,ry:0.01,rz:0.01},{t:0.10,ry:0.08,rz:0.06},{t:0.25,ry:0.12,rz:0.08},
    {t:0.40,ry:0.10,rz:0.07},{t:0.55,ry:0.08,rz:0.06},{t:0.70,ry:0.05,rz:0.04},
    {t:0.85,ry:0.03,rz:0.02},{t:0.93,ry:0.02,rz:0.015},{t:1.0,ry:0.01,rz:0.01}
  ],
  'Shrimp': [
    {t:0,ry:0.01,rz:0.01},{t:0.10,ry:0.04,rz:0.04},{t:0.25,ry:0.06,rz:0.06},
    {t:0.40,ry:0.07,rz:0.07},{t:0.55,ry:0.06,rz:0.06},{t:0.70,ry:0.04,rz:0.04},
    {t:0.85,ry:0.03,rz:0.03},{t:0.93,ry:0.015,rz:0.015},{t:1.0,ry:0.05,rz:0.003}
  ],
};

// Fin definitions per species: {startT, endT, height, side:'top'|'bottom'|'both'}
const FISH_FINS = {
  'Clownfish':  { dorsal:{s:0.25,e:0.65,h:0.06}, anal:{s:0.45,e:0.70,h:0.04}, tail:{h:0.12,fork:0.03} },
  'Angelfish':  { dorsal:{s:0.15,e:0.70,h:0.15}, anal:{s:0.35,e:0.75,h:0.12}, tail:{h:0.18,fork:0.06} },
  'Neon Tetra': { dorsal:{s:0.35,e:0.55,h:0.03}, anal:{s:0.50,e:0.65,h:0.02}, tail:{h:0.06,fork:0.02} },
  'Blue Tang':  { dorsal:{s:0.15,e:0.75,h:0.10}, anal:{s:0.40,e:0.75,h:0.08}, tail:{h:0.14,fork:0.04} },
  'Discus':     { dorsal:{s:0.15,e:0.70,h:0.08}, anal:{s:0.35,e:0.70,h:0.06}, tail:{h:0.10,fork:0.02} },
  'Guppy':      { dorsal:{s:0.35,e:0.55,h:0.02}, anal:{s:0.50,e:0.65,h:0.015}, tail:{h:0.10,fork:0.01} },
  'Seahorse':   { dorsal:{s:0.30,e:0.55,h:0.03}, anal:null, tail:null },
  'Shrimp':     { dorsal:null, anal:null, tail:{h:0.05,fork:0.01} },
};

function lerpProfile(profile, t) {
  if (t <= profile[0].t) return { ry: profile[0].ry, rz: profile[0].rz };
  if (t >= profile[profile.length-1].t) return { ry: profile[profile.length-1].ry, rz: profile[profile.length-1].rz };
  for (let i = 0; i < profile.length - 1; i++) {
    if (t >= profile[i].t && t <= profile[i+1].t) {
      const f = (t - profile[i].t) / (profile[i+1].t - profile[i].t);
      const sf = f * f * (3 - 2 * f); // smoothstep
      return {
        ry: profile[i].ry + (profile[i+1].ry - profile[i].ry) * sf,
        rz: profile[i].rz + (profile[i+1].rz - profile[i].rz) * sf
      };
    }
  }
  return { ry: 0, rz: 0 };
}

function buildFishMesh(speciesName) {
  const profile = FISH_BODY_PROFILES[speciesName];
  const fins = FISH_FINS[speciesName];
  if (!profile) return null;

  const stationsN = 20; // stations along spine
  const ringN = 12;     // vertices per cross-section ring
  const verts = [], norms = [], uvs = [], spineTs = [], idxs = [];

  // Body tube
  for (let si = 0; si <= stationsN; si++) {
    const t = si / stationsN; // 0=nose, 1=tail
    const x = (0.5 - t);     // x: +0.5 at nose, -0.5 at tail
    const { ry, rz } = lerpProfile(profile, t);

    for (let ri = 0; ri <= ringN; ri++) {
      const angle = (ri / ringN) * Math.PI * 2;
      const y = Math.cos(angle) * ry;
      const z = Math.sin(angle) * rz;

      // Normal (inverse scale for ellipse normal)
      const ny = Math.cos(angle) / (ry || 0.001);
      const nz = Math.sin(angle) / (rz || 0.001);
      const nx = (si === 0 ? 1 : si === stationsN ? -1 : 0) * 0.2;
      const nl = Math.hypot(nx, ny, nz) || 1;

      verts.push(x, y, z);
      norms.push(nx/nl, ny/nl, nz/nl);

      // UV: project side-view texture onto the tube
      // Both sides of the fish show the same side-view image
      // V maps vertical position: top of fish -> top of texture
      const normY = ry > 0.001 ? (y / ry) : 0; // -1 to 1 normalized height
      const projV = 0.5 - normY * 0.45;
      uvs.push(1 - t, projV);
      spineTs.push(t);
    }
  }

  // Body indices
  for (let si = 0; si < stationsN; si++) {
    for (let ri = 0; ri < ringN; ri++) {
      const a = si * (ringN + 1) + ri;
      const b = a + ringN + 1;
      idxs.push(a, b, a+1, a+1, b, b+1);
    }
  }

  // Dorsal fin (top)
  if (fins && fins.dorsal) {
    const d = fins.dorsal;
    const nSteps = 8;
    const base = verts.length / 3;
    for (let i = 0; i <= nSteps; i++) {
      const t = d.s + (d.e - d.s) * (i / nSteps);
      const x = 0.5 - t;
      const { ry } = lerpProfile(profile, t);
      // Base vertex (on body)
      verts.push(x, ry, 0); norms.push(0, 0, 1); uvs.push(1-t, 0.05); spineTs.push(t);
      // Tip vertex (extended upward)
      const tipH = d.h * Math.sin((i/nSteps) * Math.PI); // taller in middle
      verts.push(x, ry + tipH, 0); norms.push(0, 0, 1); uvs.push(1-t, 0.01); spineTs.push(t);
    }
    for (let i = 0; i < nSteps; i++) {
      const a = base + i*2, b = a+2;
      idxs.push(a, b, a+1, a+1, b, b+1);
      idxs.push(a, a+1, b, a+1, b+1, b); // back face
    }
  }

  // Anal fin (bottom)
  if (fins && fins.anal) {
    const d = fins.anal;
    const nSteps = 6;
    const base = verts.length / 3;
    for (let i = 0; i <= nSteps; i++) {
      const t = d.s + (d.e - d.s) * (i / nSteps);
      const x = 0.5 - t;
      const { ry } = lerpProfile(profile, t);
      verts.push(x, -ry, 0); norms.push(0, 0, 1); uvs.push(1-t, 0.95); spineTs.push(t);
      const tipH = d.h * Math.sin((i/nSteps) * Math.PI);
      verts.push(x, -ry - tipH, 0); norms.push(0, 0, 1); uvs.push(1-t, 0.99); spineTs.push(t);
    }
    for (let i = 0; i < nSteps; i++) {
      const a = base + i*2, b = a+2;
      idxs.push(a, a+1, b, a+1, b+1, b);
      idxs.push(a, b, a+1, a+1, b, b+1);
    }
  }

  // Caudal (tail) fin
  if (fins && fins.tail) {
    const tf = fins.tail;
    const base = verts.length / 3;
    const tx = -0.5; // tail tip x
    // Fork points
    verts.push(tx, 0, 0);             norms.push(-1,0,0); uvs.push(0.02, 0.5); spineTs.push(1.0);
    verts.push(tx-0.08, tf.h, 0);     norms.push(-0.5,0.7,0.5); uvs.push(0, 0.15); spineTs.push(1.0);
    verts.push(tx-0.04, tf.fork, 0);  norms.push(-1,0,0); uvs.push(0.01, 0.45); spineTs.push(1.0);
    verts.push(tx-0.08, -tf.h, 0);    norms.push(-0.5,-0.7,0.5); uvs.push(0, 0.85); spineTs.push(1.0);
    verts.push(tx-0.04, -tf.fork, 0); norms.push(-1,0,0); uvs.push(0.01, 0.55); spineTs.push(1.0);
    // Upper lobe
    idxs.push(base, base+1, base+2);
    idxs.push(base, base+2, base+1);
    // Lower lobe
    idxs.push(base, base+4, base+3);
    idxs.push(base, base+3, base+4);
  }

  // Pectoral fins (small side fins)
  for (const side of [1, -1]) {
    const base = verts.length / 3;
    const pt = 0.25; // position along body
    const { ry, rz } = lerpProfile(profile, pt);
    const px = 0.5 - pt;
    verts.push(px, 0, side*rz);          norms.push(0,0,side); uvs.push(1-pt, 0.5); spineTs.push(pt);
    verts.push(px-0.05, -0.03, side*(rz+0.04)); norms.push(0,-0.5,side); uvs.push(1-pt-0.05, 0.55); spineTs.push(pt+0.05);
    verts.push(px+0.02, -0.02, side*(rz+0.03)); norms.push(0,-0.3,side); uvs.push(1-pt+0.02, 0.52); spineTs.push(pt-0.02);
    idxs.push(base, base+1, base+2);
    idxs.push(base, base+2, base+1);
  }

  return {
    positions: new Float32Array(verts),
    normals: new Float32Array(norms),
    uvs: new Float32Array(uvs),
    spineTs: new Float32Array(spineTs),
    indices: new Uint16Array(idxs),
    vertexCount: verts.length / 3,
    indexCount: idxs.length
  };
}

function uploadFishMesh(device, mesh) {
  const vc = mesh.vertexCount;
  const data = new Float32Array(vc * 12);
  for (let i = 0; i < vc; i++) {
    data[i*12+0] = mesh.positions[i*3]; data[i*12+1] = mesh.positions[i*3+1]; data[i*12+2] = mesh.positions[i*3+2];
    data[i*12+3] = mesh.normals[i*3];   data[i*12+4] = mesh.normals[i*3+1];   data[i*12+5] = mesh.normals[i*3+2];
    data[i*12+6] = mesh.uvs[i*2];       data[i*12+7] = mesh.uvs[i*2+1];
    data[i*12+8] = mesh.spineTs[i];
    data[i*12+9] = 1.0; data[i*12+10] = 1.0; data[i*12+11] = 1.0;
  }
  const vb = device.createBuffer({size:data.byteLength, usage:GPUBufferUsage.VERTEX|GPUBufferUsage.COPY_DST});
  device.queue.writeBuffer(vb, 0, data);
  const ib = device.createBuffer({size:mesh.indices.byteLength, usage:GPUBufferUsage.INDEX|GPUBufferUsage.COPY_DST});
  device.queue.writeBuffer(ib, 0, mesh.indices);
  return { vertBuf:vb, idxBuf:ib, indexCount:mesh.indexCount };
}

const speciesMeshGPU = [];
let fish3dPipeline = null, fish3dBindGroup = null, fish3dUniformBuf = null;
let meshInstanceBuf = null;
let use3DMeshes = false;

// Old GLB loader kept but unused
async function loadGLB(url) {
  const resp = await fetch(url);
  if (!resp.ok) return null;
  const buf = await resp.arrayBuffer();
  const view = new DataView(buf);

  // GLB header: magic(4) version(4) length(4)
  const magic = view.getUint32(0, true);
  if (magic !== 0x46546C67) return null; // 'glTF'

  // Chunk 0: JSON
  const jsonLen = view.getUint32(12, true);
  const jsonBytes = new Uint8Array(buf, 20, jsonLen);
  const json = JSON.parse(new TextDecoder().decode(jsonBytes));

  // Chunk 1: BIN
  const binOffset = 20 + jsonLen;
  const binLen = view.getUint32(binOffset, true);
  const binData = new Uint8Array(buf, binOffset + 8, binLen);

  // Extract first mesh primitive
  const mesh = json.meshes[0];
  const prim = mesh.primitives[0];

  function getAccessorData(idx) {
    const acc = json.accessors[idx];
    const bv = json.bufferViews[acc.bufferView];
    const offset = (bv.byteOffset || 0) + (acc.byteOffset || 0);
    const count = acc.count;
    const compType = acc.componentType;
    const type = acc.type;
    const components = type === 'VEC3' ? 3 : type === 'VEC4' ? 4 : type === 'VEC2' ? 2 : 1;
    const normalized = acc.normalized || false;

    if (compType === 5126) { // float32
      return { data: new Float32Array(binData.buffer, binData.byteOffset + offset, count * components), count, components, normalized: false };
    } else if (compType === 5121) { // uint8 (vertex colors)
      const raw = new Uint8Array(binData.buffer, binData.byteOffset + offset, count * components);
      if (normalized) {
        const floats = new Float32Array(count * components);
        for (let i = 0; i < floats.length; i++) floats[i] = raw[i] / 255.0;
        return { data: floats, count, components, normalized: true };
      }
      return { data: raw, count, components, normalized: false };
    } else if (compType === 5123) { // uint16
      return { data: new Uint16Array(binData.buffer, binData.byteOffset + offset, count * components), count, components, normalized: false };
    } else if (compType === 5125) { // uint32
      return { data: new Uint32Array(binData.buffer, binData.byteOffset + offset, count * components), count, components, normalized: false };
    }
    return null;
  }

  const positions = getAccessorData(prim.attributes.POSITION);
  const normals = prim.attributes.NORMAL !== undefined ? getAccessorData(prim.attributes.NORMAL) : null;
  const colors = prim.attributes.COLOR_0 !== undefined ? getAccessorData(prim.attributes.COLOR_0) : null;
  const indices = prim.indices !== undefined ? getAccessorData(prim.indices) : null;

  return { positions, normals, colors, indices, vertexCount: positions.count, indexCount: indices ? indices.count : 0 };
}

function computeNormals(positions, indices) {
  const vc = positions.length / 3;
  const normals = new Float32Array(vc * 3);
  const ic = indices ? indices.length : 0;
  for (let i = 0; i < ic; i += 3) {
    const a = indices[i], b = indices[i+1], c = indices[i+2];
    const ax=positions[a*3],ay=positions[a*3+1],az=positions[a*3+2];
    const bx=positions[b*3]-ax, by=positions[b*3+1]-ay, bz=positions[b*3+2]-az;
    const cx=positions[c*3]-ax, cy=positions[c*3+1]-ay, cz=positions[c*3+2]-az;
    const nx=by*cz-bz*cy, ny=bz*cx-bx*cz, nz=bx*cy-by*cx;
    for (const vi of [a,b,c]) { normals[vi*3]+=nx; normals[vi*3+1]+=ny; normals[vi*3+2]+=nz; }
  }
  for (let i = 0; i < vc; i++) {
    const o=i*3, len=Math.hypot(normals[o],normals[o+1],normals[o+2])||1;
    normals[o]/=len; normals[o+1]/=len; normals[o+2]/=len;
  }
  return normals;
}

function uploadGLBMesh(device, glb) {
  if (!glb) return null;

  const vc = glb.vertexCount;
  const pos = glb.positions.data;

  let nrm;
  if (glb.normals) { nrm = glb.normals.data; }
  else if (glb.indices) { nrm = computeNormals(pos, glb.indices.data); }
  else { nrm = new Float32Array(vc * 3); for (let i = 0; i < vc; i++) { nrm[i*3+1] = 1; } }

  const hasColor = glb.colors && glb.colors.data;
  const col = hasColor ? glb.colors.data : null;
  const colComponents = hasColor ? glb.colors.components : 3;

  let minX=Infinity,minY=Infinity,minZ=Infinity,maxX=-Infinity,maxY=-Infinity,maxZ=-Infinity;
  for (let i = 0; i < vc; i++) {
    const x=pos[i*3],y=pos[i*3+1],z=pos[i*3+2];
    if(x<minX)minX=x;if(x>maxX)maxX=x;
    if(y<minY)minY=y;if(y>maxY)maxY=y;
    if(z<minZ)minZ=z;if(z>maxZ)maxZ=z;
  }
  const cx=(minX+maxX)*0.5, cy=(minY+maxY)*0.5, cz=(minZ+maxZ)*0.5;
  const span = Math.max(maxX-minX, maxY-minY, maxZ-minZ) || 1;
  const s = span * 3;

  // pos(3) + normal(3) + uv(2) + spineT(1) + color(3) = 12 floats = 48 bytes
  const stride = 12;
  const interleaved = new Float32Array(vc * stride);
  for (let i = 0; i < vc; i++) {
    const nx=(pos[i*3]-cx)/s, ny=(pos[i*3+1]-cy)/s, nz2=(pos[i*3+2]-cz)/s;
    interleaved[i*stride+0] = nx; interleaved[i*stride+1] = ny; interleaved[i*stride+2] = nz2;
    interleaved[i*stride+3] = nrm[i*3]; interleaved[i*stride+4] = nrm[i*3+1]; interleaved[i*stride+5] = nrm[i*3+2];
    const rangeX = (maxX-minX)||1, rangeY = (maxY-minY)||1;
    interleaved[i*stride+6] = (pos[i*3] - minX) / rangeX;
    interleaved[i*stride+7] = (pos[i*3+1] - minY) / rangeY;
    interleaved[i*stride+8] = (pos[i*3] - minX) / rangeX;
    if (col) {
      interleaved[i*stride+9]  = col[i*colComponents];
      interleaved[i*stride+10] = col[i*colComponents+1];
      interleaved[i*stride+11] = col[i*colComponents+2];
    } else {
      interleaved[i*stride+9] = 1; interleaved[i*stride+10] = 0.5; interleaved[i*stride+11] = 0.15;
    }
  }

  const vertBuf = device.createBuffer({ size: interleaved.byteLength, usage: GPUBufferUsage.VERTEX | GPUBufferUsage.COPY_DST });
  device.queue.writeBuffer(vertBuf, 0, interleaved);

  let idxBuf = null, indexCount = 0, indexFormat = 'uint16';
  if (glb.indices) {
    indexCount = glb.indices.count;
    if (vc > 65535 || glb.indices.data instanceof Uint32Array) {
      const idxData = glb.indices.data instanceof Uint32Array ? glb.indices.data : new Uint32Array(glb.indices.data);
      idxBuf = device.createBuffer({ size: idxData.byteLength, usage: GPUBufferUsage.INDEX | GPUBufferUsage.COPY_DST });
      device.queue.writeBuffer(idxBuf, 0, idxData);
      indexFormat = 'uint32';
    } else {
      const idxData = glb.indices.data instanceof Uint16Array ? glb.indices.data : new Uint16Array(indexCount);
      if (!(glb.indices.data instanceof Uint16Array)) for (let i = 0; i < indexCount; i++) idxData[i] = glb.indices.data[i];
      idxBuf = device.createBuffer({ size: idxData.byteLength, usage: GPUBufferUsage.INDEX | GPUBufferUsage.COPY_DST });
      device.queue.writeBuffer(idxBuf, 0, idxData);
    }
  }

  return { vertBuf, idxBuf, indexCount, indexFormat, vertexCount: vc };
}

// (old GLB mesh vars removed — now using procedural meshes above)

// ============================================================
// 4. MATH
// ============================================================

function mat4Perspective(fov,a,n,f){const t=1/Math.tan(fov/2),nf=1/(n-f);return new Float32Array([t/a,0,0,0,0,t,0,0,0,0,(f+n)*nf,-1,0,0,2*f*n*nf,0]);}
function mat4LookAt(e,c,u){const zx=e[0]-c[0],zy=e[1]-c[1],zz=e[2]-c[2];let l=Math.hypot(zx,zy,zz);const fz=[zx/l,zy/l,zz/l];const sx=u[1]*fz[2]-u[2]*fz[1],sy=u[2]*fz[0]-u[0]*fz[2],sz=u[0]*fz[1]-u[1]*fz[0];l=Math.hypot(sx,sy,sz);const fs=[sx/l,sy/l,sz/l];const ux=fz[1]*fs[2]-fz[2]*fs[1],uy=fz[2]*fs[0]-fz[0]*fs[2],uz=fz[0]*fs[1]-fz[1]*fs[0];return new Float32Array([fs[0],ux,fz[0],0,fs[1],uy,fz[1],0,fs[2],uz,fz[2],0,-(fs[0]*e[0]+fs[1]*e[1]+fs[2]*e[2]),-(ux*e[0]+uy*e[1]+uz*e[2]),-(fz[0]*e[0]+fz[1]*e[1]+fz[2]*e[2]),1]);}
function mat4Mul(a,b){const o=new Float32Array(16);for(let i=0;i<4;i++)for(let j=0;j<4;j++){o[j*4+i]=a[i]*b[j*4]+a[4+i]*b[j*4+1]+a[8+i]*b[j*4+2]+a[12+i]*b[j*4+3];}return o;}

// ============================================================
// 5. TEXTURE LOADING
// ============================================================

const fishTexNames = ['fish-clownfish','fish-angelfish','fish-neontetra','fish-tang','fish-discus','fish-guppy','creature-seahorse','creature-shrimp'];
const fgTexNames = ['fg-coral-left','fg-coral-right','fg-coral-center','fg-rock-left','fg-rock-right'];
const allTexNames = fishTexNames.concat(fgTexNames);

let atlasRegions = {}, atlasTexture = null, backdropTexture = null;

async function loadImage(name) {
  return new Promise(resolve => {
    const img = new Image();
    img.onload = () => resolve(img);
    img.onerror = () => { console.warn('Missing:', name); resolve(null); };
    img.src = 'assets/' + name + '.png';
  });
}

async function loadAllImages() {
  const imgs = {};
  const names = allTexNames.concat(['reef-backdrop']);
  await Promise.all(names.map(async n => { imgs[n] = await loadImage(n); }));
  return imgs;
}

function buildAtlas(device, imgs) {
  const SIZE = 2048;
  const canvas = document.createElement('canvas');
  canvas.width = SIZE; canvas.height = SIZE;
  const ctx = canvas.getContext('2d', { willReadFrequently: true });
  // Clear to fully transparent black — NOT opaque black
  ctx.clearRect(0, 0, SIZE, SIZE);

  let sx = 0, sy = 0, sh = 0;
  const regions = {};
  for (const name of allTexNames) {
    const img = imgs[name];
    if (!img) { regions[name] = {u:0,v:0,w:0.001,h:0.001}; continue; }
    const iw = Math.min(img.width, 512);
    const ih = Math.min(img.height, 512);
    if (sx + iw > SIZE) { sy += sh; sx = 0; sh = 0; }
    // Clear the target rect first to avoid blending with previous content
    ctx.clearRect(sx, sy, iw, ih);
    ctx.drawImage(img, 0, 0, img.width, img.height, sx, sy, iw, ih);
    regions[name] = { u: sx/SIZE, v: sy/SIZE, w: iw/SIZE, h: ih/SIZE };
    sx += iw; sh = Math.max(sh, ih);
  }

  const tex = device.createTexture({ size:[SIZE,SIZE], format:'rgba8unorm', usage:GPUTextureUsage.TEXTURE_BINDING|GPUTextureUsage.COPY_DST|GPUTextureUsage.RENDER_ATTACHMENT });
  const data = ctx.getImageData(0, 0, SIZE, SIZE);
  device.queue.writeTexture({texture:tex}, data.data, {bytesPerRow:SIZE*4, rowsPerImage:SIZE}, [SIZE,SIZE]);
  return { texture: tex, regions };
}

function buildTextureFromImage(device, img) {
  const w = img.width, h = img.height;
  const canvas = document.createElement('canvas');
  canvas.width = w; canvas.height = h;
  const ctx = canvas.getContext('2d');
  ctx.drawImage(img, 0, 0);
  const data = ctx.getImageData(0, 0, w, h);
  const tex = device.createTexture({ size:[w,h], format:'rgba8unorm', usage:GPUTextureUsage.TEXTURE_BINDING|GPUTextureUsage.COPY_DST });
  device.queue.writeTexture({texture:tex}, data.data, {bytesPerRow:w*4}, [w,h]);
  return tex;
}

// ============================================================
// 6. BOIDS
// ============================================================

function spawnFish(sid) {
  if (fishCount >= MAX_FISH) return;
  const i = fishCount++;
  fishSpecies[i] = sid;
  fishX[i] = (Math.random()-0.5) * 8;
  fishY[i] = -2 + Math.random() * 4;
  fishZ[i] = (Math.random()-0.5) * 4;
  const a = Math.random() * Math.PI * 2;
  const sp = SPECIES[sid].speed * 0.5;
  fishVX[i] = Math.cos(a)*sp; fishVY[i] = (Math.random()-0.5)*0.1; fishVZ[i] = Math.sin(a)*sp;
  fishAngle[i] = a; fishFlex[i] = 0;
}
function spawnSchool(sid, n) { for (let i = 0; i < n; i++) spawnFish(sid); }

function updateFish(dt) {
  const dt2 = Math.min(dt, 0.05);
  for (let i = 0; i < fishCount; i++) {
    const sp = SPECIES[fishSpecies[i]];
    let sx=0,sy=0,sz=0, ax=0,ay=0,az=0, cx=0,cy=0,cz=0, nc=0;
    for (let j = 0; j < fishCount; j++) {
      if (i===j) continue;
      if (fishSpecies[j]!==fishSpecies[i]) continue; // strictly same-species only
      const dx=fishX[i]-fishX[j],dy=fishY[i]-fishY[j],dz=fishZ[i]-fishZ[j];
      const dist=Math.sqrt(dx*dx+dy*dy+dz*dz);
      if (dist<0.01) continue;
      if (dist<sp.sep){const f=1/dist;sx+=dx*f;sy+=dy*f;sz+=dz*f;}
      if (dist<sp.align&&sp.align>0){ax+=fishVX[j];ay+=fishVY[j];az+=fishVZ[j];nc++;}
      if (dist<sp.coh&&sp.coh>0){cx+=fishX[j];cy+=fishY[j];cz+=fishZ[j];}
    }
    // Cross-species collision avoidance — must be larger than fish size
    const mySize = sp.w * 0.6;
    for (let j = 0; j < fishCount; j++) {
      if (i===j || fishSpecies[j]===fishSpecies[i]) continue;
      const otherSize = SPECIES[fishSpecies[j]].w * 0.6;
      const minDist = mySize + otherSize;
      const dx=fishX[i]-fishX[j],dy=fishY[i]-fishY[j],dz=fishZ[i]-fishZ[j];
      const dist=Math.sqrt(dx*dx+dy*dy+dz*dz);
      if (dist < minDist && dist > 0.01) {
        const push = (minDist - dist) / dist * 3.0;
        sx+=dx*push; sy+=dy*push; sz+=dz*push;
      }
    }
    let stX=sx*1.8,stY=sy*1.8,stZ=sz*1.8;
    if(nc>0){stX+=(ax/nc-fishVX[i]);stY+=(ay/nc-fishVY[i]);stZ+=(az/nc-fishVZ[i]);stX+=(cx/nc-fishX[i])*0.4;stY+=(cy/nc-fishY[i])*0.4;stZ+=(cz/nc-fishZ[i])*0.4;}
    const m=0.8;
    if(fishX[i]<TANK.xMin+m)stX+=2.5*(m-(fishX[i]-TANK.xMin));if(fishX[i]>TANK.xMax-m)stX-=2.5*(m-(TANK.xMax-fishX[i]));
    if(fishY[i]<TANK.yMin+m)stY+=2.5*(m-(fishY[i]-TANK.yMin));if(fishY[i]>TANK.yMax-m)stY-=2.5*(m-(TANK.yMax-fishY[i]));
    if(fishZ[i]<TANK.zMin+m)stZ+=2.5*(m-(fishZ[i]-TANK.zMin));if(fishZ[i]>TANK.zMax-m)stZ-=2.5*(m-(TANK.zMax-fishZ[i]));
    stY+=(-0.5-fishY[i])*0.1;
    if(foodCount>0){let nd=999,ni=-1;for(let p=0;p<bubbleCount+foodCount+speckCount;p++){if(partType[p]!==1)continue;const fd=Math.hypot(fishX[i]-partX[p],fishY[i]-partY[p],fishZ[i]-partZ[p]);if(fd<nd){nd=fd;ni=p;}}if(ni>=0&&nd<3){stX+=(partX[ni]-fishX[i])*2;stY+=(partY[ni]-fishY[i])*2;stZ+=(partZ[ni]-fishZ[i])*2;if(nd<0.15)partAlpha[ni]=0;}}
    if(scatterTimer>0){stX+=(Math.random()-0.5)*10;stY+=(Math.random()-0.5)*5;stZ+=(Math.random()-0.5)*10;}
    fishVX[i]+=stX*dt2;fishVY[i]+=stY*dt2;fishVZ[i]+=stZ*dt2;
    const vel=Math.sqrt(fishVX[i]**2+fishVY[i]**2+fishVZ[i]**2);
    if(vel>sp.speed){const s=sp.speed/vel;fishVX[i]*=s;fishVY[i]*=s;fishVZ[i]*=s;}
    else if(vel<sp.speed*0.2&&vel>0.001){const s=sp.speed*0.2/vel;fishVX[i]*=s;fishVY[i]*=s;fishVZ[i]*=s;}
    fishX[i]+=fishVX[i]*dt2;fishY[i]+=fishVY[i]*dt2;fishZ[i]+=fishVZ[i]*dt2;

    // Hard collision: directly push apart any overlapping fish
    for (let j = 0; j < fishCount; j++) {
      if (i >= j) continue; // only check each pair once
      const minD = (SPECIES[fishSpecies[i]].w + SPECIES[fishSpecies[j]].w) * 0.35;
      const dx=fishX[i]-fishX[j], dy=fishY[i]-fishY[j], dz=fishZ[i]-fishZ[j];
      const dist=Math.sqrt(dx*dx+dy*dy+dz*dz);
      if (dist < minD && dist > 0.001) {
        const push = (minD - dist) * 0.5 / dist;
        fishX[i]+=dx*push; fishY[i]+=dy*push; fishZ[i]+=dz*push;
        fishX[j]-=dx*push; fishY[j]-=dy*push; fishZ[j]-=dz*push;
      }
    }
    const tgt=Math.atan2(fishVZ[i],fishVX[i]);
    let diff=tgt-fishAngle[i];while(diff>Math.PI)diff-=Math.PI*2;while(diff<-Math.PI)diff+=Math.PI*2;
    fishAngle[i]+=diff*Math.min(sp.turn*dt2,1);
    fishFlex[i]=i*1.7+diff*0.5;
  }
  if(scatterTimer>0)scatterTimer-=dt;
}

// ============================================================
// 7. PARTICLES
// ============================================================

const BUBBLE_SRC=[[-3.5,-3.8,0.5],[2,-3.8,-1],[0,-3.8,2]];
function initParticles(){for(let i=0;i<80;i++){const idx=bubbleCount+foodCount+speckCount++;partX[idx]=(Math.random()-0.5)*10;partY[idx]=TANK.yMin+Math.random()*(TANK.yMax-TANK.yMin);partZ[idx]=(Math.random()-0.5)*6;partVY[idx]=(Math.random()-0.5)*0.02;partSize[idx]=0.01+Math.random()*0.015;partAlpha[idx]=0.15+Math.random()*0.15;partType[idx]=2;partPhase[idx]=Math.random()*Math.PI*2;}}
function updateParticles(dt){const tot=bubbleCount+foodCount+speckCount;for(const src of BUBBLE_SRC){if(Math.random()<0.03&&tot<MAX_PARTICLES-10){const idx=tot;if(idx<MAX_PARTICLES){partX[idx]=src[0]+(Math.random()-0.5)*0.3;partY[idx]=src[1];partZ[idx]=src[2]+(Math.random()-0.5)*0.3;partVY[idx]=0.5+Math.random()*0.5;partSize[idx]=0.03+Math.random()*0.04;partAlpha[idx]=0.6+Math.random()*0.3;partType[idx]=0;partPhase[idx]=Math.random()*Math.PI*2;bubbleCount++;}}}for(let i=0;i<tot;i++){partY[i]+=partVY[i]*dt;if(partType[i]===2){partX[i]+=(Math.random()-0.5)*0.01;partZ[i]+=(Math.random()-0.5)*0.01;}if(partType[i]===1){partVY[i]-=0.15*dt;partAlpha[i]-=0.02*dt;}if(partType[i]===0&&partY[i]>TANK.yMax)partAlpha[i]=0;}let w=0;bubbleCount=0;foodCount=0;speckCount=0;for(let i=0;i<tot;i++){if(partAlpha[i]>0.01){if(w!==i){partX[w]=partX[i];partY[w]=partY[i];partZ[w]=partZ[i];partVY[w]=partVY[i];partSize[w]=partSize[i];partAlpha[w]=partAlpha[i];partType[w]=partType[i];partPhase[w]=partPhase[i];}if(partType[w]===0)bubbleCount++;else if(partType[w]===1)foodCount++;else speckCount++;w++;}}}
function spawnFood(){const c=15+Math.floor(Math.random()*10);for(let i=0;i<c;i++){const t=bubbleCount+foodCount+speckCount;if(t>=MAX_PARTICLES)break;partX[t]=(Math.random()-0.5)*6;partY[t]=TANK.yMax-0.2;partZ[t]=(Math.random()-0.5)*4;partVY[t]=-0.1;partSize[t]=0.02+Math.random()*0.02;partAlpha[t]=0.9;partType[t]=1;partPhase[t]=Math.random()*Math.PI*2;foodCount++;}}

// ============================================================
// 8. WEBGPU INIT
// ============================================================

let device, context, fmt, depthTex;
let bgPipeline, spritePipeline, particlePipeline;
let bgUniformBuf, bgBindGroup;
let spriteUniformBuf, spriteBindGroup;
let particleUniformBuf, particleBindGroup;
let fishInstanceBuf, fgInstanceBuf, particleInstanceBuf;
let sampler, startTime = 0;

async function init() {
  const canvas = document.getElementById('c');
  if (!navigator.gpu) { document.getElementById('status').textContent = 'WebGPU not available.'; return; }
  const adapter = await navigator.gpu.requestAdapter();
  device = await adapter.requestDevice();
  context = canvas.getContext('webgpu');
  fmt = navigator.gpu.getPreferredCanvasFormat();
  context.configure({device, format:fmt, alphaMode:'opaque'});

  const imgs = await loadAllImages();
  const atlas = buildAtlas(device, imgs);
  atlasRegions = atlas.regions; atlasTexture = atlas.texture;
  backdropTexture = imgs['reef-backdrop'] ? buildTextureFromImage(device, imgs['reef-backdrop']) : atlasTexture;
  sampler = device.createSampler({magFilter:'linear',minFilter:'linear',mipmapFilter:'linear',addressModeU:'repeat',addressModeV:'clamp-to-edge'});

  const bgMod = device.createShaderModule({code:SHADER_BG});
  const spriteMod = device.createShaderModule({code:SHADER_SPRITE});
  const partMod = device.createShaderModule({code:SHADER_PARTICLE});

  bgUniformBuf = device.createBuffer({size:32,usage:GPUBufferUsage.UNIFORM|GPUBufferUsage.COPY_DST});
  spriteUniformBuf = device.createBuffer({size:96,usage:GPUBufferUsage.UNIFORM|GPUBufferUsage.COPY_DST});
  particleUniformBuf = device.createBuffer({size:96,usage:GPUBufferUsage.UNIFORM|GPUBufferUsage.COPY_DST});
  fishInstanceBuf = device.createBuffer({size:MAX_FISH*48,usage:GPUBufferUsage.VERTEX|GPUBufferUsage.COPY_DST});
  fgInstanceBuf = device.createBuffer({size:32*48,usage:GPUBufferUsage.VERTEX|GPUBufferUsage.COPY_DST});
  particleInstanceBuf = device.createBuffer({size:MAX_PARTICLES*32,usage:GPUBufferUsage.VERTEX|GPUBufferUsage.COPY_DST});

  // Background pipeline (fullscreen, uses backdrop texture)
  bgPipeline = device.createRenderPipeline({layout:'auto',vertex:{module:bgMod,entryPoint:'vs'},fragment:{module:bgMod,entryPoint:'fs',targets:[{format:fmt}]},primitive:{topology:'triangle-list'},depthStencil:{format:'depth24plus',depthWriteEnabled:false,depthCompare:'always'}});
  bgBindGroup = device.createBindGroup({layout:bgPipeline.getBindGroupLayout(0),entries:[{binding:0,resource:{buffer:bgUniformBuf}},{binding:1,resource:backdropTexture.createView()},{binding:2,resource:sampler}]});

  // Sprite pipeline (fish + fg decorations)
  const spriteLayout = [{arrayStride:48,stepMode:'instance',attributes:[{shaderLocation:0,offset:0,format:'float32x3'},{shaderLocation:1,offset:12,format:'float32x4'},{shaderLocation:2,offset:28,format:'float32x4'}]}];
  spritePipeline = device.createRenderPipeline({layout:'auto',vertex:{module:spriteMod,entryPoint:'vs',buffers:spriteLayout},fragment:{module:spriteMod,entryPoint:'fs',targets:[{format:fmt,blend:{color:{srcFactor:'src-alpha',dstFactor:'one-minus-src-alpha'},alpha:{srcFactor:'one',dstFactor:'one-minus-src-alpha'}}}]},primitive:{topology:'triangle-list'},depthStencil:{format:'depth24plus',depthWriteEnabled:false,depthCompare:'less'}});
  spriteBindGroup = device.createBindGroup({layout:spritePipeline.getBindGroupLayout(0),entries:[{binding:0,resource:{buffer:spriteUniformBuf}},{binding:1,resource:atlasTexture.createView()},{binding:2,resource:sampler}]});

  // Particle pipeline
  const partLayout = [{arrayStride:32,stepMode:'instance',attributes:[{shaderLocation:0,offset:0,format:'float32x3'},{shaderLocation:1,offset:12,format:'float32x4'}]}];
  particlePipeline = device.createRenderPipeline({layout:'auto',vertex:{module:partMod,entryPoint:'vs',buffers:partLayout},fragment:{module:partMod,entryPoint:'fs',targets:[{format:fmt,blend:{color:{srcFactor:'src-alpha',dstFactor:'one'},alpha:{srcFactor:'one',dstFactor:'one'}}}]},primitive:{topology:'triangle-list'},depthStencil:{format:'depth24plus',depthWriteEnabled:false,depthCompare:'less'}});
  particleBindGroup = device.createBindGroup({layout:particlePipeline.getBindGroupLayout(0),entries:[{binding:0,resource:{buffer:particleUniformBuf}}]});

  // Build procedural 3D fish meshes (ABZU-style body profiles + fins)
  const fish3dMod = device.createShaderModule({code:SHADER_FISH3D});
  fish3dUniformBuf = device.createBuffer({size:96,usage:GPUBufferUsage.UNIFORM|GPUBufferUsage.COPY_DST});
  meshInstanceBuf = device.createBuffer({size:MAX_FISH*32,usage:GPUBufferUsage.VERTEX|GPUBufferUsage.COPY_DST});

  const fish3dVertLayout = [
    { arrayStride:48, stepMode:'vertex', attributes: [
      {shaderLocation:0,offset:0,format:'float32x3'},
      {shaderLocation:1,offset:12,format:'float32x3'},
      {shaderLocation:2,offset:24,format:'float32x2'},
      {shaderLocation:3,offset:32,format:'float32'},
      {shaderLocation:6,offset:36,format:'float32x3'},
    ]},
    { arrayStride:32, stepMode:'instance', attributes: [
      {shaderLocation:4,offset:0,format:'float32x3'},
      {shaderLocation:5,offset:16,format:'float32x4'},
    ]}
  ];
  fish3dPipeline = device.createRenderPipeline({layout:'auto',
    vertex:{module:fish3dMod,entryPoint:'vs',buffers:fish3dVertLayout},
    fragment:{module:fish3dMod,entryPoint:'fs',targets:[{format:fmt,
      blend:{color:{srcFactor:'src-alpha',dstFactor:'one-minus-src-alpha'},
             alpha:{srcFactor:'one',dstFactor:'one-minus-src-alpha'}}}]},
    primitive:{topology:'triangle-list',cullMode:'none'},
    depthStencil:{format:'depth24plus',depthWriteEnabled:true,depthCompare:'less'}
  });
  fish3dBindGroup = device.createBindGroup({layout:fish3dPipeline.getBindGroupLayout(0),entries:[
    {binding:0,resource:{buffer:fish3dUniformBuf}}
  ]});

  for (const sp of SPECIES) {
    const glbName = sp.tex;
    let gpuMesh = null;
    const glb = await loadGLB('models/' + glbName + '.glb');
    if (glb) {
      gpuMesh = uploadGLBMesh(device, glb);
      if (gpuMesh) console.log(sp.name + ' (GLB): ' + gpuMesh.vertexCount + ' verts');
    }
    if (!gpuMesh) {
      const mesh = buildFishMesh(sp.name);
      if (mesh) {
        const r = atlasRegions[sp.tex] || {u:0,v:0,w:1,h:1};
        for (let i = 0; i < mesh.vertexCount; i++) {
          mesh.uvs[i*2+0] = r.u + mesh.uvs[i*2+0] * r.w;
          mesh.uvs[i*2+1] = r.v + mesh.uvs[i*2+1] * r.h;
        }
        gpuMesh = uploadFishMesh(device, mesh);
        if (gpuMesh) console.log(sp.name + ' (procedural): ' + mesh.vertexCount + ' verts');
      }
    }
    speciesMeshGPU.push(gpuMesh);
  }
  use3DMeshes = speciesMeshGPU.some(m => m !== null);

  resize(); window.addEventListener('resize', resize);

  // Spawn lots of fish
  spawnSchool(2, 18); spawnSchool(0, 5); spawnSchool(5, 12);
  spawnSchool(3, 6); spawnFish(1); spawnFish(1);
  spawnFish(4); spawnFish(4); spawnFish(4);
  spawnFish(6); spawnFish(6); spawnSchool(7, 4);

  initParticles();
  startTime = performance.now()/1000;
  requestAnimationFrame(frame);
  document.getElementById('status').textContent = fishCount + ' fish swimming';
}

function resize() {
  const c = document.getElementById('c');
  const dpr = window.devicePixelRatio || 1;
  c.width = Math.floor(c.clientWidth * dpr);
  c.height = Math.floor(c.clientHeight * dpr);
  if (depthTex) depthTex.destroy();
  depthTex = device.createTexture({size:[c.width,c.height],format:'depth24plus',usage:GPUTextureUsage.RENDER_ATTACHMENT});
}

// ============================================================
// 9. RENDER LOOP
// ============================================================

let lastTime=0, frameCount=0, fpsTime=0, fps=0;

function frame(timestamp) {
  const t=timestamp/1000, dt=lastTime?t-lastTime:0.016; lastTime=t;
  frameCount++; if(t-fpsTime>1){fps=frameCount;frameCount=0;fpsTime=t;}
  const time = t - startTime;

  updateFish(dt); updateParticles(dt);
  timeOfDay = (timeOfDay + dt/300) % 1.0;

  const camX=Math.sin(time*0.12)*0.6, camY=Math.sin(time*0.08)*0.2-0.3;
  const c=document.getElementById('c');
  const view=mat4LookAt([camX,camY,8],[0,-0.5,0],[0,1,0]);
  const proj=mat4Perspective(Math.PI/3,c.width/c.height,0.1,50);
  const vp=mat4Mul(proj,view);
  const dayAmt=1.0-Math.max(0,Math.abs(timeOfDay-0.75)*4-1.4)*0.6;

  // Uniforms
  device.queue.writeBuffer(bgUniformBuf,0,new Float32Array([time,c.width,c.height,lightOn,timeOfDay,0,0,0]));
  const su=new Float32Array(24); su.set(vp); su[16]=time; su[17]=0.02*dayAmt; su[18]=0.06*dayAmt; su[19]=0.14*dayAmt; su[20]=0.06;
  device.queue.writeBuffer(spriteUniformBuf,0,su);
  const pu=new Float32Array(24); pu.set(vp); pu[16]=time;
  device.queue.writeBuffer(particleUniformBuf,0,pu);

  // Fish sprite instance data (only needed if NOT using 3D meshes)
  if (!use3DMeshes) {
    const fd = new Float32Array(fishCount*12);
    for(let i=0;i<fishCount;i++){const sp=SPECIES[fishSpecies[i]];const r=atlasRegions[sp.tex]||{u:0,v:0,w:0.01,h:0.01};const o=i*12;fd[o]=fishX[i];fd[o+1]=fishY[i];fd[o+2]=fishZ[i];fd[o+3]=fishAngle[i];fd[o+4]=sp.w;fd[o+5]=sp.h;fd[o+6]=i*1.7+fishFlex[i]*0.5;fd[o+7]=r.u;fd[o+8]=r.v;fd[o+9]=r.w;fd[o+10]=r.h;fd[o+11]=0;}
    device.queue.writeBuffer(fishInstanceBuf,0,fd,0,fishCount*12);
  }

  // FG sprite instance data
  const fgd = new Float32Array(FG_SPRITES.length*12);
  for(let i=0;i<FG_SPRITES.length;i++){const d=FG_SPRITES[i];const r=atlasRegions[d.tex]||{u:0,v:0,w:0.01,h:0.01};const o=i*12;
    const freq=0.2+i*0.07; const phase=i*2.3+d.x*1.7+d.z*0.9;
    const sway=Math.sin(time*freq+phase)*0.02+Math.sin(time*freq*1.7+phase*0.6)*0.008;
    fgd[o]=d.x;fgd[o+1]=d.y;fgd[o+2]=d.z;fgd[o+3]=sway;fgd[o+4]=d.w;fgd[o+5]=d.h;fgd[o+6]=0;fgd[o+7]=r.u;fgd[o+8]=r.v;fgd[o+9]=r.w;fgd[o+10]=r.h;fgd[o+11]=0;}
  device.queue.writeBuffer(fgInstanceBuf,0,fgd);

  // Particles
  const totalPart=bubbleCount+foodCount+speckCount;
  const pd=new Float32Array(totalPart*8);
  for(let i=0;i<totalPart;i++){const o=i*8;pd[o]=partX[i];pd[o+1]=partY[i];pd[o+2]=partZ[i];pd[o+3]=partSize[i];pd[o+4]=partAlpha[i];pd[o+5]=partType[i];pd[o+6]=partPhase[i];pd[o+7]=0;}
  if(totalPart>0)device.queue.writeBuffer(particleInstanceBuf,0,pd,0,totalPart*8);

  // RENDER
  const encoder=device.createCommandEncoder();
  const colorView=context.getCurrentTexture().createView();
  const depthView=depthTex.createView();
  const pass=encoder.beginRenderPass({colorAttachments:[{view:colorView,loadOp:'clear',storeOp:'store',clearValue:{r:0.01,g:0.04,b:0.12,a:1}}],depthStencilAttachment:{view:depthView,depthLoadOp:'clear',depthStoreOp:'store',depthClearValue:1.0}});

  // 1. Background (reef panorama + god rays)
  pass.setPipeline(bgPipeline); pass.setBindGroup(0,bgBindGroup); pass.draw(3);

  // 2. Fish — 3D procedural meshes with ABZU-style animation
  if (use3DMeshes) {
    device.queue.writeBuffer(fish3dUniformBuf, 0, su);
    pass.setPipeline(fish3dPipeline);
    pass.setBindGroup(0, fish3dBindGroup);

    // Pre-build ALL instance data into one buffer with offsets per species
    const allInstData = new Float32Array(fishCount * 8);
    const speciesOffsets = []; // [{offset, count}]
    let totalWritten = 0;

    for (let si = 0; si < SPECIES.length; si++) {
      const sm = speciesMeshGPU[si];
      if (!sm) { speciesOffsets.push({offset:0,count:0}); continue; }

      const sp = SPECIES[si];
      const startIdx = totalWritten;
      for (let i = 0; i < fishCount; i++) {
        if (fishSpecies[i] !== si) continue;
        const o = totalWritten * 8;
        allInstData[o] = fishX[i]; allInstData[o+1] = fishY[i]; allInstData[o+2] = fishZ[i];
        allInstData[o+3] = 0;
        allInstData[o+4] = fishAngle[i];
        allInstData[o+5] = sp.w;
        allInstData[o+6] = i * 1.7 + fishFlex[i] * 0.5;
        allInstData[o+7] = sp.speed;
        totalWritten++;
      }
      speciesOffsets.push({offset: startIdx, count: totalWritten - startIdx});
    }

    // Single write of all instance data
    device.queue.writeBuffer(meshInstanceBuf, 0, allInstData, 0, totalWritten * 8);

    // Draw each species from its offset in the buffer
    for (let si = 0; si < SPECIES.length; si++) {
      const sm = speciesMeshGPU[si];
      const so = speciesOffsets[si];
      if (!sm || so.count === 0) continue;

      pass.setVertexBuffer(0, sm.vertBuf);
      pass.setVertexBuffer(1, meshInstanceBuf, so.offset * 32);
      if (sm.idxBuf) {
        pass.setIndexBuffer(sm.idxBuf, sm.indexFormat || 'uint16');
        pass.drawIndexed(sm.indexCount, so.count);
      } else {
        pass.draw(sm.vertexCount, so.count);
      }
    }
  } else {
    pass.setPipeline(spritePipeline); pass.setBindGroup(0,spriteBindGroup);
    pass.setVertexBuffer(0,fishInstanceBuf); pass.draw(6,fishCount);
  }

  // 3. Foreground decoration sprites (in front of fish for parallax)
  pass.setPipeline(spritePipeline); pass.setBindGroup(0,spriteBindGroup);
  pass.setVertexBuffer(0,fgInstanceBuf); pass.draw(6,FG_SPRITES.length);

  // 4. Particles
  if(totalPart>0){pass.setPipeline(particlePipeline);pass.setBindGroup(0,particleBindGroup);pass.setVertexBuffer(0,particleInstanceBuf);pass.draw(6,totalPart);}

  pass.end();
  device.queue.submit([encoder.finish()]);
  updateHUD();
  requestAnimationFrame(frame);
}

// ============================================================
// 10. CONTROLS & HUD
// ============================================================

function feedFish(){spawnFood();}
function tapGlass(){scatterTimer=2.0;}
function toggleLight(){lightOn=lightOn>0.5?0.0:1.0;}
function addFishByName(name){const idx=SPECIES.findIndex(s=>s.name===name);if(idx>=0)spawnFish(idx);updateHUD();}

function updateHUD(){
  const counts=new Array(SPECIES.length).fill(0);
  for(let i=0;i<fishCount;i++)counts[fishSpecies[i]]++;
  let s='';for(let i=0;i<SPECIES.length;i++){if(counts[i]>0)s+=SPECIES[i].name+': '+counts[i]+'  ';}
  const el=document.getElementById('species');if(el)el.textContent=s;
  const fc=document.getElementById('fish-count');if(fc)fc.textContent=fishCount+' fish';
  const temp=document.getElementById('temp');if(temp)temp.textContent=(76.5+Math.sin(lastTime*0.1)*0.8).toFixed(1)+'°F';
  const todNames=['Night','Dawn','Day','Dusk'];
  const tod=document.getElementById('tod');if(tod)tod.textContent=todNames[Math.floor(timeOfDay*4)%4];
  const fpsEl=document.getElementById('fps');if(fpsEl)fpsEl.textContent=fps+' fps';
}

window.addEventListener('DOMContentLoaded', init);
