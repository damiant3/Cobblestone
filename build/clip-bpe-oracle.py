# clip-bpe-oracle.py -- the ids Diffusion Forge's own CLIP tokenizer gives each
# prompt of codex/test/apps/clip-bpe-forge, one line per prompt, the oracle
# for that test's .expected.
#
#   D:\AI\DiffusionForge\system\python\python.exe build/clip-bpe-oracle.py <tokenizer dir>
#
# The tokenizer is transformers' CLIPTokenizer loaded from the directory, the
# class Forge's loader builds (backend/loader.py:43-47) and called the way
# backend/text_processing/classic_engine.py:120 calls it: no special tokens,
# no truncation. The first line wraps "a photo of a cat" in BOS and EOS, which
# is Diffusion.md's stage 4a grade. The file's inputs are also checked here,
# because the guest builds its tables from the same two files: a merge whose
# result is not in vocab.json, or a pair ranked twice, would be a case the
# guest's id tables cannot represent.
import json, os, sys
from transformers import CLIPTokenizer

STYLE = 'painterly fantasy concept art, norse viking theme, warm firelight against cold blue night, cinematic lighting, rich detail, game key art, '
PROMPTS = [
    STYLE + 'a viking smithy at night where glowing runes are hammered into an amulet on an anvil, sparks flying, open book of runes on the bench, snowy mountains through an arched window',
    STYLE + 'four iron-bound wooden chests around a carpenter workbench, joined by glowing golden rune threads, inside a torch-lit longhouse',
    STYLE + 'a sunlit green meadow with wildflowers and a clear stream, small harmless lizard creatures basking on rocks, birch forest, soft morning light',
    STYLE + 'a vivid rainbow-coloured circumhorizontal arc across wispy cirrus clouds high above a viking longhouse on a green hill, bright midday sky',
    STYLE + 'a wild boar animal on four legs resting on straw beside a stone cooking fire, a small glowing golden hourglass floating in the air above the fire, cozy barn interior, no people',
    STYLE + 'three steaming bowls of stew, bread and roasted meat on a carved wooden shelf beside a glowing red health potion vial, warm hearth light',
    STYLE + 'perfectly straight rows of carrot and turnip seedlings in dark tilled soil beside a viking farmhouse, a wooden hoe, golden hour',
    STYLE + 'a blazing forge fire and anvil in a dark stone smithy, a glowing rune-carved amulet cooling in a quench trough, embers drifting',
    'text, letters, words, watermark, signature, logo, ui, blurry, deformed, ugly, lowres, jpeg artifacts, photo, photograph, modern',
    "  It's THEY'RE we'll I'd you've I'm ''s 'sun   don't  ",
    '1024x768 3.14 v2.0 (masterpiece:1.2) [bad] {x} <lora:foo:0.8> !!! ... ?! --- @#$%^&*_=+~`|\\/"',
    'a<|endoftext|>b <|startoftext|> Supercalifragilisticexpialidocious',
    'Caf\u00e9 \u00c9COLE \u00fcber \u0416\u0418\u0417\u041d\u042c na\u00efve \u2018quoted\u2019 \u201cdouble\u201d',
    '\u039f\u0394\u039f\u03a3 \u039b\u039f\u0393\u039f\u03a3, \u03a3 \u03a3\u0391\u03a3\u0391',
]

def main():
    d = sys.argv[1]
    tok = CLIPTokenizer.from_pretrained(d)
    vocab = json.load(open(os.path.join(d, 'vocab.json'), encoding='utf-8'))
    lines = open(os.path.join(d, 'merges.txt'), encoding='utf-8').read().strip().split('\n')[1:49152 - 256 - 2 + 1]
    pairs = [tuple(l.split()) for l in lines]
    bad = [p for p in pairs if len(p) != 2 or p[0] + p[1] not in vocab]
    if bad or len(set(pairs)) != len(pairs):
        print('REFUSE: %d merges not two parts with a vocab result, %d pairs repeated' % (len(bad), len(pairs) - len(set(pairs))))
        sys.exit(1)
    first = tok('a photo of a cat', add_special_tokens=True)['input_ids']
    print(' '.join(str(i) for i in first))
    for p in PROMPTS:
        ids = tok(p, truncation=False, add_special_tokens=False)['input_ids']
        print(' '.join(str(i) for i in ids))

main()
