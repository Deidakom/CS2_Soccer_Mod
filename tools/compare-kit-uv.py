"""Read-only comparison of retained shirt UVs across two model variants."""
import importlib.util
from pathlib import Path
import numpy as np

spec = importlib.util.spec_from_file_location("inspection", Path(__file__).with_name("inspect-kit-mesh.py"))
m = importlib.util.module_from_spec(spec)
spec.loader.exec_module(m)
root = Path(__file__).resolve().parents[1]
a = m.inspect(root / "artifacts/gearless-inspection/home-flat.dmx")[47]["uv_triangles"].reshape(-1, 2)
b = m.inspect(root / "artifacts/gearless-inspection/away-flat.dmx")[27]["uv_triangles"].reshape(-1, 2)
for x, y, label in [(a,b,"a to b"), (b,a,"b to a")]:
    x, y = np.unique(x, axis=0), np.unique(y, axis=0)
    distances = np.concatenate([np.sqrt(((chunk[:, None] - y[None]) ** 2).sum(axis=2).min(axis=1)) for chunk in np.array_split(x, 20)])
    print(label, "UV unique", len(x), "max distance in 2048px atlas", distances.max()*2048,
          "p95", np.percentile(distances,95)*2048, "exact fraction", np.mean(distances < 1e-6))
