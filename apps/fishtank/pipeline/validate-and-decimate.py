"""Validate a TripoSR GLB mesh and decimate it for real-time rendering.

Sanity checks:
  - Has vertices and faces (not degenerate)
  - Has vertex colors (COLOR_0)
  - Bounding box is not flat (no collapsed axis)
  - Aspect ratio is fish-like (longest > 1.3x shortest)
  - Vertex count is in expected range

Decimation:
  - Quadric edge collapse to target face count
  - Vertex colors preserved via nearest-vertex mapping

Usage:
  python validate-and-decimate.py input.glb output.glb [--target-faces 2000]
"""
import sys
import numpy as np
import trimesh
import fast_simplification
from scipy.spatial import cKDTree

def validate(mesh):
    issues = []
    if len(mesh.vertices) < 10:
        issues.append(f"too few vertices ({len(mesh.vertices)})")
    if len(mesh.faces) < 10:
        issues.append(f"too few faces ({len(mesh.faces)})")

    bb = mesh.bounding_box.extents
    for i, axis in enumerate("xyz"):
        if bb[i] < 0.01:
            issues.append(f"collapsed on {axis} axis (extent={bb[i]:.4f})")

    aspect = max(bb) / (min(bb) + 1e-6)
    if aspect < 1.3:
        issues.append(f"too spherical (aspect={aspect:.1f}, want >1.3)")

    has_colors = mesh.visual.kind == "vertex"
    if not has_colors:
        issues.append("no vertex colors")

    return issues, {
        "verts": len(mesh.vertices),
        "faces": len(mesh.faces),
        "extents": [round(float(x), 3) for x in bb],
        "aspect": round(float(aspect), 1),
        "has_colors": has_colors,
    }

def decimate(mesh, target_faces):
    has_colors = mesh.visual.kind == "vertex"
    colors = None
    if has_colors:
        colors = mesh.visual.vertex_colors[:, :3].astype(np.float64) / 255.0

    verts_out, faces_out = fast_simplification.simplify(
        mesh.vertices.astype(np.float64),
        mesh.faces,
        target_reduction=max(0.0, 1.0 - target_faces / len(mesh.faces)),
    )

    vc = None
    if colors is not None:
        tree = cKDTree(mesh.vertices)
        _, idx = tree.query(verts_out)
        rgb = (colors[idx] * 255).astype(np.uint8)
        vc = np.column_stack([rgb, np.full(len(rgb), 255, dtype=np.uint8)])

    return trimesh.Trimesh(vertices=verts_out, faces=faces_out, vertex_colors=vc)

def main():
    if len(sys.argv) < 3:
        print("Usage: validate-and-decimate.py input.glb output.glb [--target-faces N]")
        sys.exit(1)

    input_path = sys.argv[1]
    output_path = sys.argv[2]
    target_faces = 2000
    if "--target-faces" in sys.argv:
        idx = sys.argv.index("--target-faces")
        target_faces = int(sys.argv[idx + 1])

    mesh = trimesh.load(input_path, force="mesh")
    issues, stats = validate(mesh)
    print(f"Input: {stats}")
    if issues:
        print(f"WARNINGS: {issues}")
        if any("too few" in i or "collapsed" in i for i in issues):
            print("FAIL: mesh is degenerate, skipping")
            sys.exit(2)

    if len(mesh.faces) <= target_faces:
        print(f"Already under target ({len(mesh.faces)} <= {target_faces}), copying as-is")
        mesh.export(output_path)
    else:
        result = decimate(mesh, target_faces)
        result.export(output_path)
        print(f"Output: verts={len(result.vertices)} faces={len(result.faces)}")

    import os
    print(f"Size: {os.path.getsize(output_path)} bytes")
    print("OK")

if __name__ == "__main__":
    main()
