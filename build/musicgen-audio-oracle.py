import argparse
import hashlib
import json
from pathlib import Path
from unittest.mock import patch

import numpy as np
import torch
from omegaconf import OmegaConf
from transformers import T5Tokenizer, T5EncoderModel
from audiocraft.models import builders
from audiocraft.models.loaders import load_compression_model
from audiocraft.modules.conditioners import ConditioningAttributes


def main():
    parser = argparse.ArgumentParser(description="Full local MusicGen T5/LM/EnCodec reference, CUDA f32 without TF32.")
    parser.add_argument("checkpoint", type=Path)
    parser.add_argument("compression", type=Path)
    parser.add_argument("text_encoder", type=Path)
    parser.add_argument("--out", required=True, type=Path)
    args = parser.parse_args()
    args.out.mkdir(parents=True, exist_ok=True)
    torch.set_num_threads(4)
    torch.backends.cuda.matmul.allow_tf32 = False
    torch.backends.cudnn.allow_tf32 = False
    package = torch.load(str(args.checkpoint), map_location="cpu", mmap=True, weights_only=True)
    config = OmegaConf.create(package["xp.cfg"])
    config.device = "cuda"
    config.dtype = "float32"
    config.transformer_lm.weight_init = None
    config.transformer_lm.depthwise_init = None
    config.transformer_lm.zero_bias_init = False
    config.conditioners.description.t5.autocast_dtype = None
    if config.conditioners.description.t5.name != "t5-base" or config.conditioners.description.t5.finetune or config.conditioners.description.t5.normalize_text:
        raise ValueError("Unexpected text conditioner contract")
    tok = T5Tokenizer.from_pretrained(str(args.text_encoder), local_files_only=True)
    text = T5EncoderModel.from_pretrained(str(args.text_encoder), local_files_only=True).eval()
    with patch("audiocraft.modules.conditioners.T5Tokenizer.from_pretrained", return_value=tok), patch("audiocraft.modules.conditioners.T5EncoderModel.from_pretrained", return_value=text):
        model = builders.get_lm_model(config).eval()
    model.load_state_dict(package["best_state"], strict=True)
    compression = load_compression_model(str(args.compression), device="cuda").float().eval()
    if compression.sample_rate != 32000 or compression.channels != 1 or model.two_step_cfg or model.pattern_provider.delays != [0, 1, 2, 3]:
        raise ValueError("Unexpected MusicGen audio profile")
    cases = []
    settings = [("music", "A punchy electronic drum groove with a deep bass line.", 50, 1234, 1.0, 3.0),
                ("sfx", "A short metallic bell ringing once.", 50, 1234, 1.0, 3.0),
                ("settings", "A punchy electronic drum groove with a deep bass line.", 8, 42, 0.8, 4.0)]
    for name, prompt, frames, seed, temperature, cfg in settings:
        torch.manual_seed(seed)
        with torch.no_grad():
            tokens = model.generate(conditions=[ConditioningAttributes(text={"description": prompt})], max_gen_len=frames,
                                    use_sampling=True, top_k=250, temp=temperature, cfg_coef=cfg, check=True)
            audio = compression.decode(tokens).float().cpu().numpy().astype("<f4")
        if audio.shape != (1, 1, frames * 640) or not np.isfinite(audio).all():
            raise ValueError("Invalid reference audio")
        file = name + ".f32"
        audio.tofile(args.out / file)
        cases.append({"name": name, "prompt": prompt, "frames": frames, "seed": str(seed), "temperature": temperature,
                      "cfg": cfg, "tokens": tokens[0].cpu().tolist(), "audio": file, "samples": audio.size,
                      "audio_sha256": hashlib.sha256((args.out / file).read_bytes()).hexdigest()})
        print(name, frames, "frames", audio.size, "samples", "peak", float(np.abs(audio).max()), flush=True)
    inputs = {"tokenizer": args.text_encoder / "tokenizer.json", "text": args.text_encoder / "model.safetensors",
              "model": args.checkpoint, "audio": args.compression}
    hashes = {}
    for key, path in inputs.items():
        with path.open("rb") as stream:
            hashes[key] = hashlib.file_digest(stream, "sha256").hexdigest()
    manifest = {"inputs": {k: str(p.resolve()) for k, p in inputs.items()}, "sha256": hashes, "torch": torch.__version__,
                "device": torch.cuda.get_device_name(), "dtype": "float32", "tf32": False, "sample_rate": 32000,
                "channels": 1, "dim": model.dim, "top_k": 250, "cases": cases}
    (args.out / "oracle.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")


if __name__ == "__main__":
    main()
