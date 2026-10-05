import argparse
import hashlib
import json
from pathlib import Path
from unittest.mock import patch

import numpy as np
import torch
from omegaconf import OmegaConf
from audiocraft.models import builders
from audiocraft.modules.conditioners import ConditioningAttributes


class FixedConditioning(torch.nn.Module):
    def __init__(self, prompt, tensor):
        super().__init__()
        self.prompt = prompt
        self.tensor = tensor

    def tokenize(self, attributes):
        return attributes

    def forward(self, attributes):
        rows = []
        masks = []
        for attribute in attributes:
            text = attribute.text["description"]
            if text == self.prompt:
                rows.append(self.tensor[0])
                masks.append(torch.ones(self.tensor.shape[1], device="cuda", dtype=torch.long))
            elif text is None or text == "":
                rows.append(torch.zeros_like(self.tensor[0]))
                masks.append(torch.zeros(self.tensor.shape[1], device="cuda", dtype=torch.long))
            else:
                raise ValueError("Unexpected conditioning prompt")
        return {"description": (torch.stack(rows), torch.stack(masks))}


def main():
    parser = argparse.ArgumentParser(description="Actual audiocraft streaming generation with fixed independently encoded text conditioning.")
    parser.add_argument("checkpoint", type=Path)
    parser.add_argument("conditioner", type=Path)
    parser.add_argument("--out", required=True, type=Path)
    args = parser.parse_args()
    args.out.mkdir(parents=True, exist_ok=True)
    torch.set_num_threads(4)
    torch.backends.cuda.matmul.allow_tf32 = False
    torch.backends.cudnn.allow_tf32 = False
    reference = json.loads(args.conditioner.read_text())["encoder"]
    condition = next(c for c in reference["cases"] if c["name"] == "drums")
    cond = torch.from_numpy(np.fromfile(args.conditioner.parent / condition["output"]["file"], dtype="<f4").reshape(condition["output"]["shape"])).cuda()
    package = torch.load(str(args.checkpoint), map_location="cpu", mmap=True, weights_only=True)
    with args.checkpoint.open("rb") as stream:
        checkpoint_hash = hashlib.file_digest(stream, "sha256").hexdigest()
    if checkpoint_hash != reference["sha256"]["checkpoint"]:
        raise ValueError("Conditioning belongs to another checkpoint")
    config = OmegaConf.create(package["xp.cfg"])
    config.device = "cpu"
    config.dtype = "float32"
    config.transformer_lm.weight_init = None
    config.transformer_lm.depthwise_init = None
    config.transformer_lm.zero_bias_init = False
    provider = FixedConditioning(condition["prompt"], cond)
    with patch.object(builders, "get_conditioner_provider", return_value=provider):
        model = builders.get_lm_model(config).eval()
    model.load_state_dict({k: v for k, v in package["best_state"].items() if not k.startswith("condition_provider.")}, strict=True)
    model.cuda()
    if model.two_step_cfg:
        raise ValueError("Generation oracle requires combined CFG batching")
    if model.pattern_provider.delays != [0, 1, 2, 3] or model.pattern_provider.flatten_first or model.pattern_provider.empty_initial:
        raise ValueError("Unexpected MusicGen delay pattern")
    cond.cpu().numpy().astype("<f4").tofile(args.out / "condition.f32")
    cases = []
    for sampling in [False, True]:
        for frames in [8, 17, 50, 129]:
            name = f"{'sampled' if sampling else 'greedy'}-{frames}"
            seed = 1234
            steps = []
            def capture(module, inputs, output):
                if output.shape[0] != 2:
                    raise ValueError("Expected conditional/null CFG batch")
                guided = output[1:2] + (output[:1] - output[1:2]) * 3.0
                steps.append(guided[0, :, -1].detach().cpu().numpy().astype("<f4"))
            hook = model.register_forward_hook(capture)
            torch.manual_seed(seed)
            before = torch.cuda.default_generators[0].get_offset()
            with torch.no_grad():
                tokens = model.generate(conditions=[ConditioningAttributes(text={"description": condition["prompt"]})],
                                        max_gen_len=frames, use_sampling=sampling, top_k=250 if sampling else 1,
                                        temp=1.0, cfg_coef=3.0, check=True)
            after = torch.cuda.default_generators[0].get_offset()
            hook.remove()
            logits_file = name + "-logits.f32"
            all_logits = np.stack(steps)
            if not np.isfinite(all_logits).all():
                raise ValueError("Nonfinite reference logits")
            all_logits.tofile(args.out / logits_file)
            cases.append({"name": name, "frames": frames, "sampling": sampling, "seed": str(seed),
                          "offset_before": before, "offset_after": after, "tokens": tokens[0].cpu().tolist(),
                          "logits": logits_file, "steps": len(steps)})
            print(name, "steps", len(steps), "offset", before, after, "first", tokens[0, :, :3].tolist(), flush=True)
    manifest = {"checkpoint": str(args.checkpoint.resolve()), "sha256": checkpoint_hash, "torch": torch.__version__,
                "device": torch.cuda.get_device_name(), "dtype": "float32", "tf32": False, "prompt": condition["prompt"],
                "condition": {"file": "condition.f32", "shape": list(cond.shape)}, "dim": model.dim,
                "delays": model.pattern_provider.delays, "cfg": 3.0, "temperature": 1.0, "top_k": 250, "cases": cases,
                "fixture_sha256": {p.name: hashlib.sha256(p.read_bytes()).hexdigest() for p in args.out.glob("*.f32")}}
    (args.out / "oracle.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")


if __name__ == "__main__":
    main()
