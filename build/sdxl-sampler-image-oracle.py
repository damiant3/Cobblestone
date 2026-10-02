import gc
import hashlib
import importlib.util
import importlib.metadata
import inspect
import json
import os
from pathlib import Path
import re
import sys
import time
import types

check_only = len(sys.argv) == 5 and sys.argv[4] == '--check-inputs'
if len(sys.argv) != 4 and not check_only:
    raise SystemExit('Usage: sdxl-sampler-image-oracle.py checkpoint.safetensors new-output-directory native-root-with-native-0-and-native-6 [--check-inputs]')
checkpoint, destination, native_root = map(Path, sys.argv[1:4])
if destination.exists():
    raise SystemExit('Refuse existing output directory')
destination.mkdir(parents=True)
sys.argv = sys.argv[:1]
repo = Path(__file__).resolve().parent.parent
forge = Path(r'D:\AI\DiffusionForge\webui')
sys.path[:0] = [str(forge), str(forge / 'repositories/huggingface_guess'), str(forge / 'packages_3rdparty')]

def digest(path):
    h = hashlib.sha256()
    with open(path, 'rb') as f:
        for block in iter(lambda: f.read(1048576), b''):
            h.update(block)
    return h.hexdigest()

reference = (repo / 'build/sdxl-browser-reference.codex').read_text(encoding='utf-8-sig')
prompt = re.search(r'let prompt = "([^"]+)"', reference).group(1)
negative = re.search(r'let negative = "([^"]+)"', reference).group(1)
for setting in ['ti-steps = 6', 'ti-cfg = 2.0', 'ti-seed = 7201', 'ti-width = 1024, ti-height = 1024', 'ti-scheduler = 2', 'ti-clip-skip = 1', 'ti-extras = ti-no-extras']:
    if setting not in reference:
        raise SystemExit('Reference request changed: ' + setting)

sources = ['k_diffusion/sampling.py', 'backend/modules/k_prediction.py', 'backend/nn/unet.py',
           'backend/nn/vae.py', 'backend/text_processing/classic_engine.py', 'backend/attention.py',
           'modules/sd_samplers_kdiffusion.py', 'modules/sd_samplers_common.py']
evidence = {'checkpoint': str(checkpoint.resolve()), 'checkpointSha256': digest(checkpoint),
            'oracleSha256': digest(__file__), 'referenceSourceSha256': digest(repo / 'build/sdxl-browser-reference.codex'),
            'forgeSources': {s: digest(forge / s) for s in sources}, 'prompt': prompt, 'negative': negative,
            'seed': 7201, 'steps': 6, 'cfg': 2.0, 'width': 1024, 'height': 1024,
            'scheduler': 'Karras', 'precision': 'float32', 'sgmNoiseMultiplier': False}
references = {}
for name, sampler in [('euler', 0), ('3m', 6)]:
    folder = native_root / f'native-{sampler}'
    record = json.loads((folder / 'reference-evidence.json').read_text(encoding='utf-8-sig'))
    image_name = 'euler-reference.png' if sampler == 0 else 'dpm-reference.png'
    if record['nativeExit'] != 0 or record['sampler'] != sampler or record['width'] != 1024 or record['height'] != 1024 or record['checkpointSha256'] != evidence['checkpointSha256'] or record['imageName'] != image_name:
        raise SystemExit('Native reference inputs differ')
    for filename, key in [(record['imageName'], 'imageSha256'), ('reference.cdx', 'nativeCdxSha256'), ('reference.codex', 'generatedSourceSha256'), ('clip.img', 'tokenizerDiskSha256')]:
        if digest(folder / filename) != record[key]:
            raise SystemExit('Native reference artifact differs: ' + filename)
    generated = (folder / 'reference.codex').read_text(encoding='utf-8-sig')
    label = 'Euler' if sampler == 0 else 'DPM++ 3M SDE'
    expected = reference.replace('ti-sampler = ti-euler', f'ti-sampler = {sampler}').replace('Sampler: Euler,', f'Sampler: {label},').replace('ti-out = "euler-reference.png"', f'ti-out = "{image_name}"')
    if generated != expected:
        raise SystemExit('Native reference request differs')
    references[name] = (folder, record)
evidence['nativeReferences'] = {name: {'evidenceSha256': digest(folder / 'reference-evidence.json'), 'imageSha256': r['imageSha256'], 'compilerSha256': r['compilerSha256']} for name, (folder, r) in references.items()}
if check_only:
    print('PASS native artifact identity and complete request preflight')
    raise SystemExit(0)

import numpy as np
import torch
from PIL import Image, PngImagePlugin
from safetensors import safe_open
from transformers import CLIPTokenizer

modules = types.ModuleType('modules')
shared = types.ModuleType('modules.shared')
shared.opts = types.SimpleNamespace(emphasis='Original')
modules.shared = shared
sys.modules['modules'] = modules
sys.modules['modules.shared'] = shared
from backend.text_processing.classic_engine import ClassicTextProcessingEngine
from backend.nn.unet import IntegratedUNet2DConditionModel, Timestep
from backend.nn.vae import IntegratedAutoencoderKL
from backend.modules.k_prediction import Prediction
from huggingface_guess import detection
from k_diffusion import sampling

torch.backends.cuda.matmul.allow_tf32 = False
torch.backends.cudnn.allow_tf32 = False
torch.cuda.reset_peak_memory_stats()
device = torch.cuda.get_device_properties(0)
if device.multi_processor_count * (device.max_threads_per_multi_processor // 256) != 204:
    raise SystemExit('Refuse GPU with a different native initial-noise launch geometry')
evidence.update(torch=torch.__version__, gpu=device.name, tf32=False, pngQuantization='truncate',
                packages={name: importlib.metadata.version(name) for name in ['numpy', 'torchsde', 'transformers', 'diffusers']})

helper = repo / 'build/clip-encoder-oracle.py'
spec = importlib.util.spec_from_file_location('clip_encoder_oracle', helper)
ceo = importlib.util.module_from_spec(spec)
spec.loader.exec_module(ceo)
ceo.CKPT = str(checkpoint)
evidence['clipHelperSha256'] = digest(helper)
evidence['tokenizers'] = {f'{sub}/{name}': digest(Path(ceo.BASE) / sub / name)
                         for sub in ['tokenizer', 'tokenizer_2'] for name in ['vocab.json', 'merges.txt']}
evidence['clipConfigs'] = {sub: digest(Path(ceo.BASE) / sub / 'config.json') for sub in ['text_encoder', 'text_encoder_2']}
for folder, record in references.values():
    if record['nativeSourceSha256'] != evidence['referenceSourceSha256'] or record['vocabSha256'] != evidence['tokenizers']['tokenizer/vocab.json'] or record['mergesSha256'] != evidence['tokenizers']['tokenizer/merges.txt']:
        raise SystemExit('Native tokenizer or request source differs')

class Encoder:
    def __init__(self, model):
        self.transformer = model

def text_engine(model, tokenizer, projected):
    return ClassicTextProcessingEngine(text_encoder=Encoder(model), tokenizer=tokenizer, embedding_dir=None,
        embedding_key='clip_g' if projected else 'clip_l', embedding_expected_shape=2048,
        emphasis_name='Original', text_projection=projected, minimal_clip_skip=2, clip_skip=2,
        return_pooled=projected, final_layer_norm=False)

started = time.perf_counter()
sd_l, sd_g = ceo.load_clip()
ml = ceo.build('text_encoder', sd_l, False).cuda()
mg = ceo.build('text_encoder_2', sd_g, True).cuda()
el = text_engine(ml, CLIPTokenizer.from_pretrained(str(Path(ceo.BASE) / 'tokenizer'), local_files_only=True), False)
eg = text_engine(mg, CLIPTokenizer.from_pretrained(str(Path(ceo.BASE) / 'tokenizer_2'), local_files_only=True), True)
contexts = []
with torch.inference_mode():
    size = torch.cat([Timestep(256)(torch.tensor([v], dtype=torch.float32))
                      for v in [1024, 1024, 0, 0, 1024, 1024]]).flatten()[None].cuda()
    for text in [prompt, negative]:
        cl = el([text])
        cg, pooled = eg([text])
        contexts.append((torch.cat([cl, cg], dim=2).detach(), torch.cat([pooled, size], dim=1).detach()))
del sd_l, sd_g, ml, mg, el, eg
gc.collect()
torch.cuda.empty_cache()
print('Forge text conditioning ready', flush=True)

prefix = 'model.diffusion_model.'
with safe_open(str(checkpoint), framework='pt') as f:
    keys = [k for k in f.keys() if k.startswith(prefix)]
    config = detection.detect_unet_config({k: torch.empty(f.get_slice(k).get_shape(), device='meta') for k in keys}, prefix)
    config.update(num_heads=-1, num_head_channels=64)
    accepted = inspect.signature(IntegratedUNet2DConditionModel.__init__).parameters
    config = {k: v for k, v in config.items() if k in accepted}
    with torch.device('cuda'):
        model = IntegratedUNet2DConditionModel(**config).float().eval()
    params = model.state_dict()
    if {k[len(prefix):] for k in keys} != set(params):
        raise SystemExit('UNet checkpoint keys differ')
    with torch.no_grad():
        for key in keys:
            params[key[len(prefix):]].copy_(f.get_tensor(key).to('cuda'))
prediction = Prediction(prediction_type='epsilon').cuda()
traces, latents = {}, {}

def guided(x, sigma, **kwargs):
    scaled = prediction.calculate_input(sigma, x)
    step = prediction.timestep(sigma).float()
    results = [prediction.calculate_denoised(sigma, model(scaled, timesteps=step, context=c, y=y), x)
               for c, y in contexts]
    return results[1] + 2.0 * (results[0] - results[1])

with torch.inference_mode():
    for name, count, sampler in [('euler', 6, sampling.sample_euler), ('3m', 7, sampling.sample_dpmpp_3m_sde)]:
        sigmas = sampling.get_sigmas_karras(count, prediction.sigma_min.item(), prediction.sigma_max.item()).cuda()
        if name == '3m':
            sigmas = torch.cat([sigmas[:-2], sigmas[-1:]])
        noise = torch.randn((1, 4, 128, 128), device='cuda', generator=torch.Generator('cuda').manual_seed(7201))
        noise.cpu().numpy().astype('<f4').tofile(destination / f'{name}-initial-noise.bin')
        sigmas.cpu().numpy().astype('<f4').tofile(destination / f'{name}-sigmas.bin')
        traces[name] = []
        def callback(row):
            item = {'step': row['i'], 'sigma': row['sigma'].item(), 'xRms': row['x'].square().mean().sqrt().item(),
                    'denoisedRms': row['denoised'].square().mean().sqrt().item()}
            traces[name].append(item)
            print(name, json.dumps(item), flush=True)
        extra = {}
        if name == '3m':
            tree = sampling.BrownianTreeNoiseSampler(noise, sigmas[sigmas > 0].min(), sigmas.max(), seed=[7201])
            draws = []
            def draw(a, b):
                z = tree(a, b)
                draws.append(z.cpu().numpy().astype('<f4'))
                return z
            extra['noise_sampler'] = draw
        z = sampler(guided, noise * sigmas[0], sigmas, callback=callback, disable=True, **extra)
        if not torch.isfinite(z).all():
            raise SystemExit('Nonfinite Forge latent')
        latents[name] = z.cpu()
        latents[name].numpy().astype('<f4').tofile(destination / f'{name}-latent.bin')
        if name == '3m':
            np.stack(draws).tofile(destination / '3m-noise.bin')
        del z
del params, model, contexts
gc.collect()
torch.cuda.empty_cache()

vae = IntegratedAutoencoderKL(block_out_channels=(128, 256, 512, 512), layers_per_block=2, latent_channels=4,
    down_block_types=('DownEncoderBlock2D',) * 4, up_block_types=('UpDecoderBlock2D',) * 4, scaling_factor=0.13025)
with safe_open(str(checkpoint), framework='pt') as f:
    state = {k[len('first_stage_model.'):]: f.get_tensor(k).float() for k in f.keys() if k.startswith('first_stage_model.')}
vae.load_state_dict(state, strict=True)
del state
vae = vae.cuda().eval()
images = []
with torch.inference_mode():
    for name, latent in latents.items():
        result = vae.decode(latent.cuda() / 0.13025)
        if not torch.isfinite(result).all():
            raise SystemExit('Nonfinite Forge decode')
        pixels = (((result[0].permute(1, 2, 0).float().cpu().numpy() + 1) / 2).clip(0, 1) * 255).astype(np.uint8)
        metadata = PngImagePlugin.PngInfo()
        label = 'Euler' if name == 'euler' else 'DPM++ 3M SDE'
        metadata.add_text('parameters', f'{prompt}\nNegative prompt: {negative}\nSteps: 6, Sampler: {label}, Schedule type: Karras, CFG scale: 2, Seed: 7201, Size: 1024x1024')
        path = destination / f'forge-{name}.png'
        Image.fromarray(pixels).save(path, pnginfo=metadata)
        folder, record = references[name]
        expected = np.asarray(Image.open(folder / record['imageName']).convert('RGB'))
        if expected.shape != pixels.shape:
            raise SystemExit('Native image shape differs')
        delta = np.abs(pixels.astype(np.int16) - expected.astype(np.int16))
        images.append({'sampler': name, 'file': path.name, 'sha256': digest(path), 'meanByte': float(pixels.mean()),
                       'zeroChannelFraction': float((pixels == 0).mean()), 'fullChannelFraction': float((pixels == 255).mean()),
                       'nativeComparison': {'mean': float(delta.mean()), 'max': int(delta.max()), 'limit': 4.0}})
evidence.update(images=images, traces=traces, elapsedSeconds=time.perf_counter()-started,
                peakAllocatedBytes=torch.cuda.max_memory_allocated(), peakReservedBytes=torch.cuda.max_memory_reserved())
if digest(checkpoint) != evidence['checkpointSha256'] or any(digest(forge / s) != h for s, h in evidence['forgeSources'].items()):
    raise SystemExit('Oracle inputs moved')
if digest(__file__) != evidence['oracleSha256'] or digest(helper) != evidence['clipHelperSha256'] or digest(repo / 'build/sdxl-browser-reference.codex') != evidence['referenceSourceSha256']:
    raise SystemExit('Oracle source moved')
if any(digest(Path(ceo.BASE) / path) != value for path, value in evidence['tokenizers'].items()):
    raise SystemExit('Tokenizer inputs moved')
if any(digest(Path(ceo.BASE) / sub / 'config.json') != value for sub, value in evidence['clipConfigs'].items()):
    raise SystemExit('CLIP configuration moved')
for name, (folder, record) in references.items():
    if digest(folder / 'reference-evidence.json') != evidence['nativeReferences'][name]['evidenceSha256']:
        raise SystemExit('Native evidence moved')
    for filename, key in [(record['imageName'], 'imageSha256'), ('reference.cdx', 'nativeCdxSha256'), ('reference.codex', 'generatedSourceSha256'), ('clip.img', 'tokenizerDiskSha256')]:
        if digest(folder / filename) != record[key]:
            raise SystemExit('Native artifact moved: ' + filename)
(destination / 'evidence.json').write_text(json.dumps(evidence, indent=2)+'\n', encoding='utf-8')
passed = all(row['nativeComparison']['mean'] < 4.0 for row in images)
print(('PASS' if passed else 'FAIL') + ' Forge/native image comparison ' + json.dumps(images), flush=True)
raise SystemExit(0 if passed else 2)
