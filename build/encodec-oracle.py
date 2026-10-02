import argparse
import json
from pathlib import Path

import numpy as np
import torch
from audiocraft.models.loaders import load_compression_model
from audiocraft.modules.conv import StreamableConv1d, StreamableConvTranspose1d


def save_tensor(directory, name, tensor):
    array = tensor.detach().float().cpu().contiguous().numpy().astype("<f4")
    array.tofile(directory / (name + ".f32"))
    return {"file": name + ".f32", "shape": list(array.shape)}


def convolution_cases(model, directory, device):
    cases = []
    for name, layer in model.decoder.named_modules():
        if not isinstance(layer, (StreamableConv1d, StreamableConvTranspose1d)):
            continue
        transpose = isinstance(layer, StreamableConvTranspose1d)
        conv = layer.convtr.convtr if transpose else layer.conv.conv
        if conv.groups != 1 or conv.dilation[0] != 1:
            raise ValueError(f"Unsupported decoder convolution: {name}")
        if transpose and layer.trim_right_ratio != 1:
            raise ValueError("Unexpected transposed-convolution trim")
        if not transpose and (conv.stride[0] != 1 or layer.pad_mode != "reflect"):
            raise ValueError("Unexpected decoder padding")
        prefix = "conv-" + name.replace(".", "-")
        shared = {"kind": "transpose" if transpose else "conv1d", "cin": conv.in_channels,
                  "cout": conv.out_channels, "kernel": conv.kernel_size[0], "stride": conv.stride[0],
                  "left": (0 if transpose else conv.kernel_size[0] - 1) if layer.causal else (conv.kernel_size[0] - conv.stride[0] + 1) // 2,
                  "v": save_tensor(directory, prefix + "-v", conv.weight_v),
                  "g": save_tensor(directory, prefix + "-g", conv.weight_g),
                  "bias": save_tensor(directory, prefix + "-bias", conv.bias)}
        for frames in [1, 3, 9]:
            x = ((torch.arange(conv.in_channels * frames, device=device) * 37) % 127 - 63).float().reshape(1, conv.in_channels, frames) / 64
            with torch.no_grad():
                y = layer(x)
            if frames == 1:
                shared["weight"] = save_tensor(directory, prefix + "-weight", conv.weight)
            label = prefix + "-" + str(frames)
            cases.append({**shared, "name": label, "frames": frames,
                          "input": save_tensor(directory, label + "-input", x),
                          "output": save_tensor(directory, label + "-output", y)})
        print(f"{prefix}: {shared['kind']} {conv.in_channels}/{conv.out_channels}, kernel {conv.kernel_size[0]}", flush=True)
    return cases


def lstm_cases(model, directory, device):
    lstm = model.decoder.model[1].lstm
    if lstm.num_layers != 2 or lstm.hidden_size != 1024 or lstm.input_size != 1024 or lstm.bidirectional or lstm.proj_size:
        raise ValueError("Unexpected decoder LSTM")
    weights = []
    for layer in range(2):
        weights.append({key: save_tensor(directory, f"lstm-{layer}-{key}", getattr(lstm, f"{key}_l{layer}"))
                        for key in ["weight_ih", "weight_hh", "bias_ih", "bias_hh"]})
    cases = []
    for frames in [1, 3, 9]:
        x = (((torch.arange(1024 * frames, device=device) * 29) % 127 - 63).float() / 64).reshape(1, 1024, frames)
        for state in ["zero", "spread"]:
            h0 = torch.zeros((2, 1, 1024), device=device)
            c0 = torch.zeros_like(h0)
            if state == "spread":
                h0 = ((torch.arange(2048, device=device) * 17) % 31 - 15).float().reshape(2, 1, 1024) / 32
                c0 = ((torch.arange(2048, device=device) * 13) % 47 - 23).float().reshape(2, 1, 1024) / 32
            with torch.no_grad():
                y, (hn, cn) = lstm(x.permute(2, 0, 1), (h0, c0))
                gates = torch.nn.functional.linear(x[:, :, 0], lstm.weight_ih_l0, lstm.bias_ih_l0) + torch.nn.functional.linear(h0[0], lstm.weight_hh_l0, lstm.bias_hh_l0)
            label = f"lstm-{state}-{frames}"
            cases.append({"name": label, "frames": frames,
                          "input": save_tensor(directory, label + "-input", x),
                          "h0": save_tensor(directory, label + "-h0", h0),
                          "c0": save_tensor(directory, label + "-c0", c0),
                          "gates": save_tensor(directory, label + "-gates", gates),
                          "output": save_tensor(directory, label + "-output", y.permute(1, 2, 0)),
                          "hn": save_tensor(directory, label + "-hn", hn),
                          "cn": save_tensor(directory, label + "-cn", cn)})
    return {"weights": weights, "cases": cases}


def main():
    parser = argparse.ArgumentParser(description="Independent audiocraft EnCodec fixed-token reference.")
    parser.add_argument("checkpoint", type=Path)
    parser.add_argument("--out", required=True, type=Path)
    parser.add_argument("--device", choices=["cpu", "cuda"], default="cpu")
    args = parser.parse_args()
    args.out.mkdir(parents=True, exist_ok=True)
    torch.set_num_threads(4)
    torch.backends.cuda.matmul.allow_tf32 = False
    torch.backends.cudnn.allow_tf32 = False
    model = load_compression_model(str(args.checkpoint), device=args.device).float().eval()
    layers = model.quantizer.vq.layers
    if len(layers) != 4 or any(not isinstance(layer.project_out, torch.nn.Identity) for layer in layers):
        raise ValueError("Reference checkpoint is not the four-book MusicGen EnCodec")
    books = torch.stack([layer._codebook.embed for layer in layers])
    manifest = {"torch": torch.__version__, "checkpoint": str(args.checkpoint.resolve()),
                "device": args.device, "sample_rate": int(model.sample_rate), "frame_rate": float(model.frame_rate),
                "books": save_tensor(args.out, "books", books), "cases": []}
    for frames in [1, 2, 7, 8, 17]:
        for pattern in ["zero", "spread"]:
            codes = torch.zeros((1, 4, frames), dtype=torch.long, device=args.device)
            if pattern == "spread":
                for book in range(4):
                    for frame in range(frames):
                        codes[0, book, frame] = (book * 503 + frame * 197 + 2047) % model.cardinality
            label = f"{pattern}-{frames}"
            with torch.no_grad():
                latent = model.decode_latent(codes)
                audio = model.decode(codes)
            case = {"name": label, "codes": codes.cpu().tolist(),
                    "latent": save_tensor(args.out, label + "-latent", latent),
                    "audio": save_tensor(args.out, label + "-audio", audio)}
            manifest["cases"].append(case)
            print(f"{label}: latent {list(latent.shape)}, audio {list(audio.shape)}", flush=True)
    manifest["convs"] = convolution_cases(model, args.out, args.device)
    manifest["lstm"] = lstm_cases(model, args.out, args.device)
    elu_input = torch.linspace(-16, 8, 1025, device=args.device)
    manifest["elu"] = {"input": save_tensor(args.out, "elu-input", elu_input),
                       "output": save_tensor(args.out, "elu-output", torch.nn.functional.elu(elu_input))}
    if args.device == "cuda":
        model.half()
        for case in manifest["cases"]:
            codes = torch.tensor(case["codes"], dtype=torch.long, device=args.device)
            with torch.no_grad():
                half = model.decode(codes).float().cpu().numpy()
            full = np.fromfile(args.out / case["audio"]["file"], dtype="<f4").reshape(case["audio"]["shape"])
            error = np.abs(half - full)
            if not np.isfinite(error).all():
                raise ValueError("Nonfinite reference precision comparison")
            case["f16_f32_max_abs"] = float(error.max())
            case["f16_f32_rms"] = float(np.sqrt(np.mean(error.astype(np.float64) ** 2)))
            print(f"{case['name']}: f16/f32 max {case['f16_f32_max_abs']:.9g}, rms {case['f16_f32_rms']:.9g}", flush=True)
    (args.out / "oracle.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")


if __name__ == "__main__":
    main()
