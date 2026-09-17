# -*- coding: utf-8 -*-
"""Final checks: cloud-cut both-sides test + full-angle diagonal filament scan."""
import numpy as np
from PIL import Image

P1 = r"C:\Users\qqq\AppData\Roaming\QoderCN\SharedClientCache\cache\images\task-ff5\2iz7oqdj-4a796cc6.png"
P2 = r"C:\Users\qqq\AppData\Roaming\QoderCN\SharedClientCache\cache\images\task-ff5\eatlif50-17375436.png"
OUT = r"d:\OneDrive\Documents\work\Unity\JurassicWorld\Temp\skyline"


def load(path):
    im = Image.open(path).convert("RGB")
    return im, np.asarray(im).astype(np.float32)


def masks(a):
    r, g, b = a[..., 0], a[..., 1], a[..., 2]
    gray = a.mean(2)
    sky = (gray > 145) & ~((g > r + 12) & (g > b + 12))
    return gray, sky


def cloud_cut_test(a, x0, y0, y1, tag):
    gray, sky = masks(a)
    H, W, _ = a.shape
    r, g, b = a[..., 0], a[..., 1], a[..., 2]
    cloudish = sky & (b - r < 35) & (gray > 160)
    dL = np.arange(max(0, x0 - 24), max(0, x0 - 3))
    dR = np.arange(x0 + 4, min(W, x0 + 25))
    print(f"\n[{tag}] cloud-cut test at x0={x0}, strong rows {y0}..{y1}:")
    for name, xx in (("left strip", dL), ("right strip", dR)):
        frac = cloudish[y0:y1 + 1][:, xx].mean()
        br = gray[y0:y1 + 1][:, xx].mean()
        print(f"  {name} x={xx.min()}..{xx.max()}: cloudish fraction={frac:.2f}, mean brightness={br:.1f}")
    # brightness of left vs right strips per row within extent +-20 more
    Lb = gray[:, max(0, x0 - 20):max(0, x0 - 3)].mean(1)
    Rb = gray[:, x0 + 4:min(W, x0 + 21)].mean(1)
    rows = np.arange(max(0, y0 - 25), min(H, y1 + 25))
    lo = np.percentile(Lb[rows], 40)
    hi = np.percentile(Lb[rows], 60)
    lower_half = Lb[rows] <= lo
    upper_half = Lb[rows] >= hi
    for nm, m in (("dimmer rows (bottom 40% of left-strip brightness)", lower_half),
                  ("brighter rows (top 60-100%)", upper_half)):
        d = (Rb - Lb)[rows][m]
        print(f"  {nm}: n={m.sum()}, median left={np.median(Lb[rows][m]):.0f}, "
              f"median right={np.median(Rb[rows][m]):.0f}, median step={np.median(d):+.1f}")


def diag_full(a, tag, region=None, thmin=0.5, emin=2.6, covmin=0.25):
    gray, sky = masks(a)
    H, W, _ = a.shape
    e = np.abs(np.diff(gray, axis=1))
    ok = sky[:, :-1] & sky[:, 1:]
    e[~ok] = 0.0
    Hh, Ww = e.shape
    if region:
        x0r, x1r, y0r, y1r = region
    else:
        x0r, x1r, y0r, y1r = 0, Ww, 0, Hh
    sub = e[y0r:y1r]
    ok_sub = ok[y0r:y1r]
    h_sub = sub.shape[0]
    yy = np.arange(h_sub)[:, None]
    cc = np.arange(x0r, x1r)[None, :]
    cand = []
    for th in np.arange(-84, 84.01, 1.5):
        t = np.tan(np.radians(th))
        xi = np.clip(np.round(cc - x0r + t * yy).astype(np.int32), 0, Ww - 1)
        vals = sub[np.arange(h_sub)[:, None], xi]
        skies = ok_sub[np.arange(h_sub)[:, None], xi]
        skn = skies.sum(0)
        good = skn >= (20 if region else 60)
        m = np.where(good, vals.mean(0), 0)
        fr = np.where(good, (vals > 3).sum(0) / np.maximum(skn, 1), 0)
        c = int(np.argmax(m))
        cand.append((m[c], int(cc[0, c]), th, fr[c], int(skn[c])))
    cand.sort(reverse=True)
    kept = []
    for sc, c, th, fr, skn in cand:
        if fr < covmin or sc < emin:
            continue
        if any(abs(c - k[1]) < 40 and abs(th - k[2]) < 8 for k in kept):
            continue
        kept.append((sc, c, th, fr, skn))
        if len(kept) >= 5:
            break
    print(f"\n[{tag}] diagonal scan{' region' + str(region) if region else ' full-sky'} "
          f"(|edge|>{emin}, cov>{covmin:.0%}):")
    for sc, c, th, fr, skn in kept:
        xb = int(round(c + np.tan(np.radians(th)) * (y1r - 1 - y0r)))
        print(f"  th={th:+5.1f}deg from vertical  start=({c},{y0r}) end=({xb},{y1r - 1})  "
              f"{c/W*100:5.1f}%W->{xb/W*100:5.1f}%W  mean_edge={sc:.2f} cov={fr*100:.0f}% skyrows={skn}")
    if not kept:
        print("  none")


im1, a1 = load(P1)
im2, a2 = load(P2)
cloud_cut_test(a1, 152, 51, 121, "IMG1_cubemap")
cloud_cut_test(a2, 127, 69, 147, "IMG2_panoramic")

diag_full(a1, "IMG1_cubemap", region=(0, 130, 0, 130))
diag_full(a2, "IMG2_panoramic", region=(0, 130, 0, 130))
diag_full(a1, "IMG1_cubemap")
diag_full(a2, "IMG2_panoramic")

# 6x zoom crops of filament areas
for tag, im, box in (("IMG1_cubemap", im1, (10, 35, 110, 100)),
                     ("IMG2_panoramic", im2, (10, 55, 110, 130))):
    crop = im.crop(box)
    crop = crop.resize((crop.width * 6, crop.height * 6), Image.NEAREST)
    crop.save(f"{OUT}\\{tag}_filament6x.png")
    print(f"saved {tag}_filament6x.png box={box}")
