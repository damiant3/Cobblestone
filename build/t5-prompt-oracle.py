# t5-prompt-oracle.py -- Diffusion Forge's T5 prompt chunks (Flux), printed as
# the Codex list literals codex/test/apps/t5-prompt-forge grades
# apps/diffusion/T5Prompt against.
#
#   D:\AI\DiffusionForge\system\python\python.exe build/t5-prompt-oracle.py
#
# Forge's own T5TextProcessingEngine.tokenize_line (backend/text_processing/
# t5_engine.py) with its parsing.parse_prompt_attention and the Original
# emphasis, over FLUX.1-schnell's tokenizer_2 through transformers'
# T5TokenizerFast; modules.shared is stubbed with opts.emphasis "Original",
# since the engine reads nothing else from it on this path. The encoder is
# never built. __call__'s padding of every chunk to the longest, which runs
# beside the encoder there, is restated below line for line. Each prompt
# prints as a flat list: the chunk count, the chunk length, every chunk's ids,
# then every chunk's weights as f64 bits.
import os, struct, sys, types

WEBUI = r'D:\AI\DiffusionForge\webui'
sys.path[:0] = [WEBUI, os.path.join(WEBUI, 'packages_3rdparty'), os.path.join(WEBUI, 'repositories', 'huggingface_guess')]
DIR = os.path.join(WEBUI, r'backend\huggingface\black-forest-labs\FLUX.1-schnell\tokenizer_2')

shared = types.ModuleType('modules.shared')
shared.opts = types.SimpleNamespace(emphasis='Original')
sys.modules['modules.shared'] = shared
if 'modules' not in sys.modules:
    pkg = types.ModuleType('modules'); pkg.__path__ = []; sys.modules['modules'] = pkg
sys.modules['modules'].shared = shared

from transformers import T5TokenizerFast
from backend.text_processing import emphasis
from backend.text_processing.t5_engine import T5TextProcessingEngine

STYLE = 'painterly fantasy concept art, norse viking theme, warm firelight against cold blue night, cinematic lighting, rich detail, game key art, '
PROMPTS = [
    STYLE + 'a viking smithy at night where glowing runes are hammered into an amulet on an anvil, sparks flying',
    'a (red:1.3) apple on a [wooden] table, ((sharp focus)), (soft light:0.8)',
    'first part BREAK second part',
    'BREAK',
    '',
    'a very detailed castle on a hill, ' * 40,
    'a (castle on a hill, ' * 30 + 'BREAK a moat',
    '(unclosed paren and [unclosed square',
]

def bits(x):
    return struct.unpack('<q', struct.pack('<d', float(x)))[0]

def main():
    eng = T5TextProcessingEngine.__new__(T5TextProcessingEngine)
    eng.tokenizer = T5TokenizerFast.from_pretrained(DIR)
    eng.emphasis = emphasis.EmphasisOriginal()
    eng.min_length = 256
    eng.id_end = 1
    eng.id_pad = 0
    rows = []
    for p in PROMPTS:
        chunks, _ = eng.tokenize_line(p)
        max_tokens = max(len(c.tokens) for c in chunks)
        for c in chunks:
            remaining_count = max_tokens - len(c.tokens)
            if remaining_count > 0:
                c.tokens += [eng.id_pad] * remaining_count
                c.multipliers += [1.0] * remaining_count
        flat = [len(chunks), max_tokens]
        for c in chunks: flat += c.tokens
        for c in chunks: flat += [bits(m) for m in c.multipliers]
        rows.append(flat)
    print('  t5p-prompts : List Text = [%s]' % ', '.join('"%s"' % p for p in PROMPTS))
    for k, r in enumerate(rows):
        print('  t5p-ref-%d : List Integer = [%s]' % (k, ', '.join(str(v) for v in r)))
    print('  t5p-refs : List (List Integer) = [%s]' % ', '.join('t5p-ref-%d' % k for k in range(len(rows))))

main()
