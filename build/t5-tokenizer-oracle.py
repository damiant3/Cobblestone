# t5-tokenizer-oracle.py -- Flux's T5 tokenizer (FLUX.1-schnell tokenizer_2) as
# Forge runs it, written as the files codex/test/apps/t5-tokenizer-forge reads.
#
#   D:\AI\DiffusionForge\system\python\python.exe build/t5-tokenizer-oracle.py <out dir>
#
# From tokenizer.json, unchanged in content: vocab.bin, the piece count as u32,
# every piece's score as f32, then every piece as a u8 byte length and its UTF-8
# bytes, in id order; charsmap.bin, the Precompiled normalizer's charsmap
# decoded from base64 (a u32 trie size, the darts-clone trie, the string pool).
# prompts.bin: the count as u32, then each prompt as a u32 length and UTF-8.
# ids.bin: per prompt, the id count as u32 and the ids as u32, from
# transformers' T5TokenizerFast over the same tokenizer.json with its
# special tokens (the EOS 1 Forge's T5 engine appends, t5_engine.py:81).
import base64, json, os, struct, sys
from transformers import T5TokenizerFast

DIR = r'D:\AI\DiffusionForge\webui\backend\huggingface\black-forest-labs\FLUX.1-schnell\tokenizer_2'
STYLE = 'painterly fantasy concept art, norse viking theme, warm firelight against cold blue night, cinematic lighting, rich detail, game key art, '
PROMPTS = [STYLE + p for p in [
    'a viking smithy at night where glowing runes are hammered into an amulet on an anvil, sparks flying, open book of runes on the bench, snowy mountains through an arched window',
    'four iron-bound wooden chests around a carpenter workbench, joined by glowing golden rune threads, inside a torch-lit longhouse',
    'a sunlit green meadow with wildflowers and a clear stream, small harmless lizard creatures basking on rocks, birch forest, soft morning light',
    'a vivid rainbow-coloured circumhorizontal arc across wispy cirrus clouds high above a viking longhouse on a green hill, bright midday sky',
    'a wild boar animal on four legs resting on straw beside a stone cooking fire, a small glowing golden hourglass floating in the air above the fire, cozy barn interior, no people',
    'three steaming bowls of stew, bread and roasted meat on a carved wooden shelf beside a glowing red health potion vial, warm hearth light',
    'perfectly straight rows of carrot and turnip seedlings in dark tilled soil beside a viking farmhouse, a wooden hoe, golden hour',
    'a blazing forge fire and anvil in a dark stone smithy, a glowing rune-carved amulet cooling in a quench trough, embers drifting',
]] + [
    'text, letters, words, watermark, signature, logo, ui, blurry, deformed, ugly, lowres, jpeg artifacts, photo, photograph, modern',
    '',
    '  two leading spaces,  doubled  spaces and trailing ones   ',
    'UPPER lower MiXeD 123 4.56 (brackets) [square] {curly} <angle> #hash @at 50% a+b=c',
    'caf\u00e9 na\u00efve \u00c5ngstr\u00f6m \u00fcber se\u00f1or',
    '\uff46\uff55\uff4c\uff4c\uff0d\uff57\uff49\uff44\uff54\uff48 \uff21\uff22\uff23 \ufb01ne ligature \u2460 circled',
    'tab\there, newline\nthere, \u4e2d\u6587 and \u65e5\u672c\u8a9e unknown, emoji \U0001F600 end',
]

def main():
    out = sys.argv[1]
    t = json.load(open(os.path.join(DIR, 'tokenizer.json'), encoding='utf-8'))
    vocab = t['model']['vocab']
    with open(os.path.join(out, 'vocab.bin'), 'wb') as f:
        f.write(struct.pack('<I', len(vocab)))
        f.write(struct.pack('<%dd' % len(vocab), *[s for _, s in vocab]))
        for piece, _ in vocab:
            b = piece.encode('utf-8')
            f.write(struct.pack('<B', len(b)) + b)
    with open(os.path.join(out, 'charsmap.bin'), 'wb') as f:
        f.write(base64.b64decode(t['normalizer']['normalizers'][0]['precompiled_charsmap']))
    tok = T5TokenizerFast.from_pretrained(DIR)
    with open(os.path.join(out, 'prompts.bin'), 'wb') as f, open(os.path.join(out, 'ids.bin'), 'wb') as g:
        f.write(struct.pack('<I', len(PROMPTS)))
        for p in PROMPTS:
            b = p.encode('utf-8')
            f.write(struct.pack('<I', len(b)) + b)
            ids = tok(p, add_special_tokens=True)['input_ids']
            g.write(struct.pack('<I%dI' % len(ids), len(ids), *ids))
            print(len(ids), ids[:12])

main()
