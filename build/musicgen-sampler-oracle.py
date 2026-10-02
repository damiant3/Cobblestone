import argparse
import hashlib
import json
from pathlib import Path

import torch


def main():
    parser = argparse.ArgumentParser(description="Torch CUDA multinomial one-sample reference for MusicGen's four codebooks.")
    parser.add_argument("--out", required=True, type=Path)
    args = parser.parse_args()
    args.out.mkdir(parents=True, exist_ok=True)
    torch.set_num_threads(4)
    device = torch.cuda.get_device_properties(0)
    cases = []
    x = torch.arange(8192, device="cuda", dtype=torch.float32).reshape(4, 2048)
    inputs = {"uniform": torch.ones_like(x) / 2048,
              "spread": torch.softmax(((x * 17) % 101 - 50) / 7, dim=-1),
              "peaked": torch.softmax(-((x % 2048 - torch.tensor([21, 467, 1012, 1980], device="cuda")[:, None]) / 13) ** 2, dim=-1)}
    for kind, probs in inputs.items():
        probs_file = kind + "-probs.f32"
        probs.cpu().numpy().astype("<f4").tofile(args.out / probs_file)
        for seed in [0, 1234, 4294967301]:
            generator = torch.Generator(device="cuda").manual_seed(seed)
            for draw in range(3):
                before = generator.get_offset()
                probe = torch.Generator(device="cuda")
                probe.set_state(generator.get_state())
                exponential = torch.empty_like(probs).exponential_(1, generator=probe)
                sampled = torch.multinomial(probs, 1, generator=generator)
                if not torch.equal(sampled, (probs / exponential).argmax(-1, keepdim=True)) or not torch.equal(generator.get_state(), probe.get_state()):
                    raise ValueError("Torch multinomial does not follow the one-sample exponential path")
                name = f"{kind}-{seed}-{draw}"
                file = name + "-exp.f32"
                exponential.cpu().numpy().astype("<f4").tofile(args.out / file)
                cases.append({"name": name, "kind": kind, "seed": str(seed), "draw": draw, "probs": probs_file,
                              "exponential": file, "tokens": sampled.flatten().tolist(), "offset_before": before, "offset_after": generator.get_offset()})
                print(name, cases[-1]["tokens"], flush=True)
    manifest = {"torch": torch.__version__, "device": device.name, "sm_count": device.multi_processor_count,
                "shape": [4, 2048], "cases": cases,
                "sha256": {p.name: hashlib.sha256(p.read_bytes()).hexdigest() for p in args.out.glob("*.f32")}}
    (args.out / "oracle.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")


if __name__ == "__main__":
    main()
