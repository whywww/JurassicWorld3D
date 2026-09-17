# -*- coding: utf-8 -*-
"""Follow-up: extra column checks, relaxed diagonal scan, thin-line profile dump."""
import numpy as np
from PIL import Image

P1 = r"C:\Users\qqq\AppData\Roaming\QoderCN\SharedClientCache\cache\images\task-ff5\2iz7oqdj-4a796cc6.png"
P2 = r"C:\Users\qqq\AppData\Roaming\QoderCN\SharedClientCache\cache\images\task-ff5\eatlif50-17375436.png"


def load(path):
    return np.asarray(Image.open(path).convert("RGB")).astype(np.float32)


def sky_only_stats(a, x):
    r, g, b = a[..., 0], a[..., 1], a[..., 2]
    gray = a.mean(2)
    sky = (gray > 145) & ~((g > r + 12) & (g > b + 12))
    L = gray[:, x - 3:x].mean(1)
    R = gray[:, x + 1:x + 4].mean(1)
    valid = sky[:, x - 3:x + 4].all(1)
    step = (R - L)[valid]
    if len(step) < 15:
        return None
    strong = step[np.abs(step) > 1.5]
    sc = max((strong > 0).mean(), (strong < 0).mean()) * 100
    ys = np.where(valid)[0]
    sig = np.abs(step) > 3.0
    ext = (ys[sig].min(), ys[sig].max()) if sig.any() else None
    return dict(n=len(step), med=np.median(step), medabs=np.median(np.abs(step)),
                meanabs=np.mean(np.abs(step)), sc=sc, ext=ext)


for tag, path in (("IMG1", P1), ("IMG2", P2)):
    a = load(path)
    H, W, _ = a.shape
    print(f"\n== {tag} ({W}x{H}) specific columns (sky-only) ==")
    cols = [54, 60, 61, 108, 110, 112, 148, 150, 151, 152, 153, 154, 155, 156, 158, 160] if tag == "IMG1" else \
           [43, 110, 112, 114, 120, 123, 124, 125, 126, 127, 128, 129, 130, 131, 133, 135]
    for x in cols:
        if x >= W - 4:
            continue
        s = sky_only_stats(a, x)
        if s:
            ext = f"|step|>3 rows: {s['ext'][0]}..{s['ext'][1]}" if s["ext"] else "none"
            print(f"  x={x:3d} ({x/W*100:5.2f}%W) med={s['med']:+6.2f} medabs={s['medabs']:5.2f} "
                  f"meanabs={s['meanabs']:5.2f} signcons={s['sc']:.0f}% n={s['n']}  {ext}")
    # relaxed diagonal scan on sky-only edges
    r, g, b = a[..., 0], a[..., 1], a[..., 2]
    gray = a.mean(2)
    sky = (gray > 145) & ~((g > r + 12) & (g > b + 12))
    e = np.abs(np.diff(gray, axis=1))
    pair_ok = sky[:, :-1] & sky[:, 1:]
    e[~pair_ok] = 0.0
    # mask the main vertical seam
    if tag == "IMG1":
        lo, hi = 140, 168
    else:
        lo, hi = 115, 143
    e[:, lo:hi] = 0.0
    Hh, Ww = e.shape
    yy = np.arange(Hh)[:, None]
    cc = np.arange(Ww)[None, :]
    cand = []
    for th in np.arange(-40, 40.01, 1.0):
        t = np.tan(np.radians(th))
        xi = np.clip(np.round(cc + t * yy).astype(np.int32), 0, Ww - 1)
        vals = e[np.arange(Hh)[:, None], xi]
        skies = pair_ok[np.arange(Hh)[:, None], xi]
        sk_n = skies.sum(0)
        ok_c = sk_n >= 25
        m = np.where(ok_c, vals.mean(0), 0)
        frac = np.where(ok_c, (vals > 4).sum(0) / np.maximum(sk_n, 1), 0)
        c = int(np.argmax(m))
        cand.append((m[c], c, th, frac[c], int(sk_n[c])))
    cand.sort(reverse=True)
    kept = []
    for scv, c, th, fr, skn in cand:
        if fr < 0.15:
            continue
        if any(abs(c - k[1]) < 45 and abs(th - k[2]) < 6 for k in kept):
            continue
        kept.append((scv, c, th, fr, skn))
        if len(kept) >= 6:
            break
    print(f"  relaxed diagonal scan (coverage>=15%): {len(kept)} candidates")
    for scv, c, th, fr, skn in kept:
        xbot = int(round(c + np.tan(np.radians(th)) * (Hh - 1)))
        print(f"    th={th:+5.1f}deg  ({c},0)->({xbot},{Hh-1})  {c/W*100:5.1f}%W->{xbot/W*100:5.1f}%W  "
              f"mean_edge={scv:.2f} cov={fr*100:.0f}% skyrows={skn}")
