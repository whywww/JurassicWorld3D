# -*- coding: utf-8 -*-
"""Subpixel ramp-centroid tracking of the sky seam -> precise tilt angle."""
import numpy as np
from PIL import Image

P1 = r"C:\Users\qqq\AppData\Roaming\QoderCN\SharedClientCache\cache\images\task-ff5\2iz7oqdj-4a796cc6.png"
P2 = r"C:\Users\qqq\AppData\Roaming\QoderCN\SharedClientCache\cache\images\task-ff5\eatlif50-17375436.png"


def load(path):
    return np.asarray(Image.open(path).convert("RGB")).astype(np.float32)


def tilt(tag, path, x0, y_lo, y_hi):
    a = load(path)
    H, W, _ = a.shape
    gray = a.mean(2)
    r, g, b = a[..., 0], a[..., 1], a[..., 2]
    sky = (gray > 145) & ~((g > r + 12) & (g > b + 12))
    xs = np.arange(x0 - 8, x0 + 9)
    centers, rows = [], []
    for y in range(y_lo, y_hi + 1):
        row = gray[y, xs]
        if not sky[y, xs].all():
            continue
        d = np.abs(np.diff(row))
        wsum = d.sum()
        if wsum < 6.0:
            continue
        c = (d * (xs[:-1] + 0.5)).sum() / wsum
        centers.append(c)
        rows.append(y)
    centers = np.array(centers)
    rows = np.array(rows)
    A = np.vstack([rows, np.ones_like(rows)]).T
    slope, icept = np.linalg.lstsq(A, centers, rcond=None)[0]
    angle = np.degrees(np.arctan(slope))
    resid = centers - (slope * rows + icept)
    print(f"[{tag}] seam x0={x0}: rows used={len(rows)} (y {rows.min()}..{rows.max()}), "
          f"slope={slope*100:+.2f} px/100rows, tilt={angle:+.2f} deg from vertical "
          f"(negative = leans LEFT going down), residual std={resid.std():.2f}px")
    print(f"    center at top y={rows.min()}: x={slope*rows.min()+icept:.1f} ({((slope*rows.min()+icept))/W*100:.2f}%W); "
          f"at bottom y={rows.max()}: x={slope*rows.max()+icept:.1f} ({((slope*rows.max()+icept))/W*100:.2f}%W)")


tilt("IMG1_cubemap", P1, 152, 51, 121)
tilt("IMG2_panoramic", P2, 127, 69, 147)
# wider row range using weaker threshold rows as well
tilt("IMG1_cubemap(loose)", P1, 152, 10, 150)
tilt("IMG2_panoramic(loose)", P2, 127, 10, 150)
