import argparse
import contextlib
import json
from pathlib import Path
from unittest.mock import patch

import torch


def table(path):
    offsets = {}
    current = []
    original = torch.serialization._open_zipfile_reader

    class Reader:
        def __init__(self, reader):
            self.reader = reader

        def __getattr__(self, name):
            return getattr(self.reader, name)

        def get_record_offset(self, name):
            offset = self.reader.get_record_offset(name)
            current[:] = [offset]
            return offset

    @contextlib.contextmanager
    def reader(path):
        with original(path) as opened:
            yield Reader(opened)

    def locate(storage, location):
        if not current:
            raise RuntimeError("torch did not report the mapped storage offset")
        offsets[storage.data_ptr()] = current[0]
        return storage

    with patch.object(torch.serialization, "_open_zipfile_reader", reader):
        checkpoint = torch.load(str(path), map_location=locate, mmap=True, weights_only=True)
    state = checkpoint.get("best_state", checkpoint)
    rows = []
    with path.open("rb") as source:
        for name, tensor in state.items():
            if not isinstance(tensor, torch.Tensor) or not tensor.is_contiguous():
                raise ValueError(f"{name}: expected a contiguous tensor")
            start = offsets[tensor.untyped_storage().data_ptr()] + tensor.storage_offset() * tensor.element_size()
            length = tensor.numel() * tensor.element_size()
            source.seek(start)
            raw = source.read(length)
            if raw != tensor.detach().cpu().numpy().tobytes():
                raise ValueError(f"{name}: mapped byte range differs from torch.load tensor")
            rows.append({"name": name, "dtype": str(tensor.dtype).removeprefix("torch."),
                         "shape": list(tensor.shape), "start": start, "end": start + length})
    return {"path": str(path.resolve()), "bytes": path.stat().st_size, "tensors": rows}


def main():
    parser = argparse.ArgumentParser(description="Grade TorchZip against torch.load on trusted local checkpoints.")
    parser.add_argument("checkpoints", nargs="+", type=Path)
    parser.add_argument("--out", required=True, type=Path)
    args = parser.parse_args()
    results = []
    for path in args.checkpoints:
        result = table(path)
        results.append(result)
        print(f"{path}: {len(result['tensors'])} tensors; every byte range equals torch.load", flush=True)
    args.out.write_text(json.dumps({"torch": torch.__version__, "checkpoints": results}, indent=2) + "\n", encoding="utf-8")


if __name__ == "__main__":
    main()
