# lora-tag-oracle.py -- what Diffusion Forge makes of <name:args> tags in a
# prompt, the reference for the tag arms of codex/test/apps/clip-lora-tags.
#
#   D:\AI\DiffusionForge\system\python\python.exe build/lora-tag-oracle.py
#
# The tags are stripped by Forge's own modules/extra_networks.py parse_prompt
# (processing.py:511 calls it on the positive prompt only) and split by its
# ExtraNetworkParams; a lora tag's weights are then read as
# extensions-builtin/sd_forge_lora/extra_networks_lora.py:30-36 reads them. Per
# prompt: the stripped prompt, then each lora as name, te and unet, a weight as
# the signed 64-bit integer of its float's bits, then each other kind's name.
import importlib.util, struct, sys, types

sys.modules['modules'] = types.ModuleType('modules')
sys.modules['modules.errors'] = types.ModuleType('modules.errors')
sys.modules['modules'].errors = sys.modules['modules.errors']
spec = importlib.util.spec_from_file_location('extra_networks', r'D:\AI\DiffusionForge\webui\modules\extra_networks.py')
en = importlib.util.module_from_spec(spec)
spec.loader.exec_module(en)

PROMPTS = [
    'a castle, <lora:detailTweaker:0.8> sunset',
    '<lora:styleA:1.2:0.6> portrait <lora:styleB> of a knight',
    '<lora:x:te=0.5:unet=1.5> forest <hypernet:hn:1> river',
    'no tags here (red:1.2) <not a tag> <lora:> <:x> a<b',
    '<lora:name with space:-.25><LORA:Upper:0.3> end <lora:z:2:dyn=4>',
]

def bits(v):
    return struct.unpack('<q', struct.pack('<d', v))[0]

for p in PROMPTS:
    stripped, res = en.parse_prompt(p)
    print('prompt: ' + stripped)
    for params in res.get('lora', []):
        te = float(params.positional[1]) if len(params.positional) > 1 else 1.0
        te = float(params.named.get('te', te))
        unet = float(params.positional[2]) if len(params.positional) > 2 else te
        unet = float(params.named.get('unet', unet))
        print('lora: %s te %d unet %d' % (params.positional[0], bits(te), bits(unet)))
    for kind in sorted(k for k in res if k != 'lora'):
        print('other: %s x%d' % (kind, len(res[kind])))
