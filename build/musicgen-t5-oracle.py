import argparse
import hashlib
import json
from pathlib import Path
from unittest.mock import patch

from transformers import T5Tokenizer
import transformers


PROMPTS = [
    ("drums", "A punchy electronic drum groove with a deep bass line."),
    ("melody", "Gentle acoustic guitar, warm piano, and a slow jazz melody."),
    ("empty", ""),
    ("whitespace", " \t\n "),
    ("spaces", "  two leading spaces,  doubled  spaces and trailing ones   "),
    ("punctuation", "FUNK 123 4.56 (brackets) [square] {curly} #hash @at 50% a+b=c"),
    ("latin", "caf\u00e9 na\u00efve \u00c5ngstr\u00f6m \u00fcber se\u00f1or"),
    ("fullwidth", "\uff46\uff55\uff4c\uff4c\uff0d\uff57\uff49\uff44\uff54\uff48 \uff21\uff22\uff23 \ufb01ne ligature \u2460 circled"),
    ("unicode", "tab\there, newline\nthere, \u4e2d\u6587 and \u65e5\u672c\u8a9e, emoji \U0001f600 end"),
    ("special", "<extra_id_0> hello </s> <extra_id_99>"),
    ("explicit-eos", "hello </s>"),
    ("special-adjacent", "bass<extra_id_0>line <unk><pad>"),
    ("combining", "cafe\u0301 A\u030angstro\u0308m"),
]


def encoder_reference(snapshot, checkpoint, directory, precision=False):
    import torch
    import numpy as np
    from omegaconf import OmegaConf
    from transformers import T5EncoderModel
    from transformers.models.t5.modeling_t5 import T5Attention
    from audiocraft.modules.conditioners import T5Conditioner

    torch.set_num_threads(4)
    pkg = torch.load(str(checkpoint), map_location="cpu", mmap=True, weights_only=True)
    config = OmegaConf.create(pkg["xp.cfg"])
    args = OmegaConf.to_container(config.conditioners.description.t5, resolve=True)
    if args["name"] != "t5-base" or args.get("normalize_text", False):
        raise ValueError("Expected MusicGen's unnormalized t5-base conditioner")
    prefix = "condition_provider.conditioners.description.output_proj."
    projection = {key: pkg["best_state"][prefix + key] for key in ["weight", "bias"]}
    output_dim = projection["weight"].shape[0]
    tokenizer = T5Tokenizer.from_pretrained(str(snapshot), local_files_only=True)
    encoder = T5EncoderModel.from_pretrained(str(snapshot), local_files_only=True).eval()
    with patch("audiocraft.modules.conditioners.T5Tokenizer.from_pretrained", return_value=tokenizer), patch("audiocraft.modules.conditioners.T5EncoderModel.from_pretrained", return_value=encoder):
        conditioner = T5Conditioner(output_dim=output_dim, device="cpu", **args).eval()
    conditioner.output_proj.load_state_dict(projection)
    captured = {}
    hooks = [block.register_forward_hook(lambda module, inputs, outputs, i=i: captured.__setitem__(f"block{i}", outputs[0].detach())) for i, block in enumerate(encoder.encoder.block)]
    hooks.append(encoder.register_forward_hook(lambda module, inputs, outputs: captured.__setitem__("hidden", outputs.last_hidden_state.detach())))

    def save(name, value):
        filename = name + ".f32"
        value.float().cpu().contiguous().numpy().astype("<f4").tofile(directory / filename)
        return {"file": filename, "shape": list(value.shape)}

    cases = []
    for name, prompt in PROMPTS + [("long", "steady drums and a soft melody " * 24)]:
        with torch.no_grad():
            inputs = conditioner.tokenize([prompt])
            output, mask = conditioner(inputs)
        cases.append({"name": name, "prompt": prompt, "ids": inputs["input_ids"][0].tolist(),
                      "mask": mask[0].tolist(), "output": save(name + "-condition", output),
                      "hidden": save(name + "-hidden", captured["hidden"]),
                      "blocks": [save(name + f"-block{i}", captured[f"block{i}"]) for i in range(12)]})
        print(f"encoder {name}: {list(output.shape)}", flush=True)
    if precision:
        encoder.double()
        conditioner.output_proj.double()
        original_mask = encoder.encoder.get_extended_attention_mask
        def float_mask(attention_mask, input_shape, device=None, dtype=None):
            return original_mask(attention_mask, input_shape, device=device, dtype=torch.float32)
        for case in cases:
            with torch.no_grad(), patch.object(encoder.encoder, "get_extended_attention_mask", float_mask):
                output, _ = conditioner(conditioner.tokenize([case["prompt"]]))
            values = [captured[f"block{i}"] for i in range(12)] + [captured["hidden"], output]
            descriptors = case["blocks"] + [case["hidden"], case["output"]]
            case["precision"] = []
            for part, (value, descriptor) in enumerate(zip(values, descriptors)):
                wide = value.detach().cpu().numpy().astype("<f8")
                single = np.fromfile(directory / descriptor["file"], dtype="<f4").reshape(wide.shape).astype(np.float64)
                error = np.abs(wide - single) / (1 + np.abs(single))
                if not np.isfinite(error).all():
                    raise ValueError("Nonfinite precision probe")
                filename = case["name"] + f"-wide-{part}.f64"
                wide.tofile(directory / filename)
                case["precision"].append({"file": filename, "max_normalized": float(error.max()), "max_abs": float(np.abs(wide - single).max())})
            print(f"precision {case['name']}: raw max {max(p['max_normalized'] for p in case['precision'][:12]):.9g}; condition {case['precision'][13]['max_normalized']:.9g}", flush=True)
    for hook in hooks:
        hook.remove()
    hashes = {}
    for name, path in [("weights", snapshot / "model.safetensors"), ("checkpoint", checkpoint)]:
        with path.open("rb") as stream:
            hashes[name] = hashlib.file_digest(stream, "sha256").hexdigest()
    return {"checkpoint": str(checkpoint.resolve()), "weights": str(snapshot.resolve() / "model.safetensors"), "sha256": hashes,
            "output_dim": output_dim, "dtype": "float32", "cases": cases,
            "precision_mode": "float64 parameters/matmul; mask, variance and softmax remain float32" if precision else None,
            "buckets": T5Attention._relative_position_bucket(torch.arange(-256, 257)).tolist()}


def main():
    parser = argparse.ArgumentParser(description="MusicGen T5 tokenizer reference using audiocraft's tokenizer class.")
    parser.add_argument("tokenizer", type=Path)
    parser.add_argument("--out", required=True, type=Path)
    parser.add_argument("--lm-checkpoint", type=Path)
    parser.add_argument("--precision", action="store_true")
    args = parser.parse_args()
    tokenizer = T5Tokenizer.from_pretrained(str(args.tokenizer), local_files_only=True)
    cases = []
    for name, prompt in PROMPTS:
        result = tokenizer([prompt], return_tensors="pt", padding=True)
        if prompt == "":
            result["attention_mask"][:] = 0
        cases.append({"name": name, "prompt": prompt, "ids": result["input_ids"][0].tolist(),
                      "mask": result["attention_mask"][0].tolist()})
        print(name, cases[-1]["ids"], flush=True)
    hashes = {name: hashlib.sha256((args.tokenizer / name).read_bytes()).hexdigest() for name in ["tokenizer.json", "spiece.model"]}
    manifest = {"tokenizer": str(args.tokenizer.resolve() / "tokenizer.json"),
                "transformers": transformers.__version__, "legacy": tokenizer.legacy,
                "sha256": hashes, "cases": cases}
    if args.lm_checkpoint:
        args.out.parent.mkdir(parents=True, exist_ok=True)
        manifest["encoder"] = encoder_reference(args.tokenizer, args.lm_checkpoint, args.out.parent, args.precision)
    args.out.write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")


if __name__ == "__main__":
    main()
