import argparse
import hashlib
import json
from pathlib import Path
from unittest.mock import patch

import numpy as np
import torch
from omegaconf import OmegaConf
from audiocraft.models import builders


def main():
    parser = argparse.ArgumentParser(description="Original MusicGen LM forward reference on fixed codes and conditioning.")
    parser.add_argument("checkpoint", type=Path)
    parser.add_argument("conditioner", type=Path)
    parser.add_argument("--out", required=True, type=Path)
    parser.add_argument("--precision", action="store_true")
    args = parser.parse_args()
    args.out.mkdir(parents=True, exist_ok=True)
    torch.set_num_threads(4)
    package = torch.load(str(args.checkpoint), map_location="cpu", mmap=True, weights_only=True)
    config = OmegaConf.create(package["xp.cfg"])
    config.device = "cpu"
    config.dtype = "float32"
    config.transformer_lm.weight_init = None
    config.transformer_lm.depthwise_init = None
    config.transformer_lm.zero_bias_init = False
    with patch.object(builders, "get_conditioner_provider", return_value=torch.nn.Module()):
        model = builders.get_lm_model(config).eval()
    weights = {k: v for k, v in package["best_state"].items() if not k.startswith("condition_provider.")}
    model.load_state_dict(weights, strict=True)
    reference = json.loads(args.conditioner.read_text())["encoder"]
    with args.checkpoint.open("rb") as stream:
        checkpoint_hash = hashlib.file_digest(stream, "sha256").hexdigest()
    if checkpoint_hash != reference["sha256"]["checkpoint"]:
        raise ValueError("Conditioning reference belongs to another checkpoint")
    captured = {}
    hooks = [layer.register_forward_hook(lambda m, ins, out, i=i: captured.__setitem__(i, out.detach())) for i, layer in enumerate(model.transformer.layers)]

    def save(name, tensor):
        file = name + ".f32"
        value = tensor.detach().float().cpu().contiguous().numpy().astype("<f4")
        if not np.isfinite(value).all():
            raise ValueError("Nonfinite LM reference: " + name)
        value.tofile(args.out / file)
        return {"file": file, "shape": list(value.shape)}

    cases = []
    for condition_name in ["drums", "empty"]:
        condition = next(c for c in reference["cases"] if c["name"] == condition_name)
        cond = torch.from_numpy(np.fromfile(args.conditioner.parent / condition["output"]["file"], dtype="<f4").reshape(condition["output"]["shape"]))
        mask = torch.tensor([condition["mask"]])
        for steps in [1, 3, 7]:
            codes = torch.tensor([[[2048 if t == 0 else (t * 137 + k * 503) % 2048 for t in range(steps)] for k in range(4)]])
            name = f"{condition_name}-{steps}"
            with torch.no_grad():
                logits = model(codes, [], {"description": (cond, mask)})
            cases.append({"name": name, "steps": steps, "codes": codes[0].tolist(), "condition": save(name + "-condition", cond),
                          "blocks": [save(name + f"-block{i}", captured[i]) for i in range(len(model.transformer.layers))],
                          "logits": save(name + "-logits", logits)})
            print(name, list(logits.shape), flush=True)
    for hook in hooks:
        hook.remove()
    if args.precision:
        model.double()
        for case in cases:
            shape = case["condition"]["shape"]
            cond = torch.from_numpy(np.fromfile(args.out / case["condition"]["file"], dtype="<f4").reshape(shape)).double()
            codes = torch.tensor([case["codes"]])
            with torch.no_grad():
                wide = model(codes, [], {"description": (cond, torch.ones(shape[:2], dtype=torch.long))}).cpu().numpy()
            single = np.fromfile(args.out / case["logits"]["file"], dtype="<f4").reshape(wide.shape).astype(np.float64)
            error = np.abs(wide - single) / (1 + np.abs(single))
            if not np.isfinite(error).all():
                raise ValueError("Nonfinite LM precision reference")
            file = case["name"] + "-wide.f64"
            wide.astype("<f8").tofile(args.out / file)
            case["precision"] = {"file": file, "max_normalized": float(error.max()), "max_abs": float(np.abs(wide - single).max())}
            print("precision", case["name"], case["precision"], flush=True)
    manifest = {"checkpoint": str(args.checkpoint.resolve()), "sha256": checkpoint_hash, "torch": torch.__version__,
                "dtype": "float32", "config": OmegaConf.to_container(config.transformer_lm, resolve=True), "cases": cases}
    (args.out / "oracle.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")


if __name__ == "__main__":
    main()
