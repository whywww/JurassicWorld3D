# -*- coding: utf-8 -*-
"""Measure vertical/diagonal line artifacts in two Unity sky screenshots."""
import os
import numpy as np
from PIL import Image
from numpy.lib.stride_tricks import sliding_window_view

P1 = r"C:\Users\qqq\AppData\Roaming\QoderCN\SharedClientCache\cache\images\task-ff5\2iz7oqdj-4a796cc6.png"
P2 = r"C:\Users\qqq\AppData\Roaming\QoderCN\SharedClientCache\cache\images\task-ff5\eatlif50-17375436.png"
OUT = r"d:\OneDrive\Documents\work\Unity\JurassicWorld\Temp\skyline"
os.makedirs(OUT, exist_ok=True)


def med_filter(a, win):
    pad = np.pad(a, (win // 2, win // 2), mode="reflect")
    return np.median(sliding_window_view(pad, win), axis=1)


def load(path):
    im = Image.open(path).convert("RGB")
    return im, np.asarray(im).astype(np.float32)


def sky_region(gray, g, r, b):
    sky = (gray > 150) & ~((g > r + 12) & (g > b + 12))
    skyfrac = sky.mean(0)
    rows = np.where(skyfrac > 0.15)[0]
    return sky, int(rows.min()), int(rows.max())


def characterize(a, gray, x, y0, y1, W, H, tag):
    r, g, b = a[..., 0], a[..., 1], a[..., 2]
    ys = np.arange(y0, y1 + 1)

    def model(arr):
        left = arr[:, max(0, x - 9):x - 2].mean(1)[ys]
        right = arr[:, x + 3:min(W, x + 10)].mean(1)[ys]
        center = arr[:, max(0, x - 1):x + 3].mean(1)[ys]
        neigh = (arr[:, max(0, x - 7):x - 2].mean(1)[ys] + arr[:, x + 3:min(W, x + 9)].mean(1)[ys]) / 2.0
        return right - left, center - neigh

    step_g, line_g = model(gray)
    # pick dominant model by mean |.|
    s_step, s_line = np.mean(np.abs(step_g)), np.mean(np.abs(line_g))
    print(f"  [{tag}] boundary x={x} ({x/W*100:.2f}%W): mean|step|={s_step:.2f} mean|line|={s_line:.2f}")
    for chname, arr in (("R", r), ("G", g), ("B", b)):
        st, ln = model(arr)
        print(f"    ch {chname}: step mean={st.mean():+.2f} (max abs {np.max(np.abs(st)):.2f}), "
              f"line mean={ln.mean():+.2f} (max abs {np.max(np.abs(ln)):.2f})")
    if s_step >= s_line:
        val, mname = step_g, "STEP (left/right tonal step)"
    else:
        val, mname = line_g, "LINE (thin bright/dark line)"
    print(f"    dominant model: {mname}, signed mean={val.mean():+.2f}")
    thr = max(2.0, 0.4 * np.max(np.abs(val)))
    rows = ys[np.abs(val) > thr]
    if len(rows):
        hspan = (rows.max() - rows.min() + 1) / H * 100
        print(f"    significant rows: y={rows.min()}..{rows.max()} ({(rows.min())/H*100:.1f}%..{(rows.max())/H*100:.1f}% of H, span {hspan:.1f}%), "
              f"rows={len(rows)}, maxabs={np.max(np.abs(val)):.2f}")
    return mname.startswith("STEP"), val, ys


def cloud_cut(gray, x, y0, y1, W, H, val, ys, is_step):
    xl, xr = max(0, x - 45), min(W, x + 46)
    amb = gray[:, xl:xr].mean(1)
    amb_s = np.convolve(amb, np.ones(5) / 5, mode="same")
    a = amb_s[ys]
    cloud = a > np.percentile(a, 65)
    clear = a < np.percentile(a, 35)
    print(f"    ambient brightness: clear rows(<35pct, {np.percentile(a,35):.0f}) vs cloud rows(>65pct, {np.percentile(a,65):.0f})")
    for name, m in (("clear-sky rows", clear), ("cloud/haze rows", cloud)):
        v = val[m]
        if len(v):
            signcons = max((v > 0).mean(), (v < 0).mean()) * 100
            print(f"      {name}: n={m.sum()}, mean|d|={np.mean(np.abs(v)):.2f}, signed mean={v.mean():+.2f}, "
                  f"sign consistency={signcons:.0f}%, maxabs={np.max(np.abs(v)):.2f}")


def diag_scan(gray, y0, y1, W, xline, tag):
    edge = np.abs(np.diff(gray, axis=1))
    sub = edge[y0:y1 + 1].copy()
    if xline is not None:
        lo, hi = max(0, xline - 12), min(sub.shape[1], xline + 14)
        sub[:, lo:hi] = 0.0
    Hh, Ww = sub.shape
    ys = np.arange(Hh)[:, None]
    cols = np.arange(Ww)[None, :]
    cand = []
    for th in np.arange(-45, 45.01, 1.5):
        t = np.tan(np.radians(th))
        xi = np.clip(np.round(cols + t * ys).astype(np.int32), 0, Ww - 1)
        vals = sub[np.arange(Hh)[:, None], xi]
        m = vals.mean(0)
        frac = (vals > 3).mean(0)
        c = int(np.argmax(m))
        cand.append((m[c], c, th, frac[c]))
    cand.sort(reverse=True)
    kept = []
    for sc, c, th, fr in cand:
        if fr < 0.25:
            continue
        if any(abs(c - k[1]) < 60 and abs(th - k[2]) < 6 for k in kept):
            continue
        kept.append((sc, c, th, fr))
        if len(kept) >= 4:
            break
    print(f"  diagonal scan (line anchored at row {y0}): {len(kept)} candidates kept")
    for sc, c, th, fr in kept:
        ytop = y0
        ybot = y1
        xtop = c
        xbot = int(round(c + np.tan(np.radians(th)) * (ybot - ytop)))
        print(f"    angle={th:+.1f}deg from vertical -> from ({xtop},{ytop}) to ({xbot},{ybot}); "
              f"({xtop/W*100:.1f}%W -> {xbot/W*100:.1f}%W), mean edge={sc:.2f}, coverage(edge>3)={fr*100:.0f}%")


def composition(a, gray, g, r, b, H, W, tag):
    haze = (r - b > 12) & (gray > 90) & (gray < 190) & (g < r + 30)
    rf = med_filter(haze.mean(1), 31)
    hrow = int(np.argmax(rf))
    print(f"  horizon haze band: row {hrow} ({hrow/H*100:.1f}%H), max frac={rf[hrow]:.2f}")
    darkgreen = (g > r + 8) & (g > b + 8) & (gray < 150)
    run = 12
    C = np.zeros((H + 1, W))
    C[1:] = np.cumsum(darkgreen, 0)
    ok = (C[run:] - C[:-run]) == run
    has = ok.any(0)
    first = np.argmax(ok, 0).astype(float)
    top = np.where(has, first, np.nan)
    med = np.nanmedian(top)
    print(f"  tree-canopy top (dark-green run>={run}px): median row {med:.0f} ({med/H*100:.1f}%H), "
          f"valid cols={int(has.sum())}/{W}")
    for name, sl in (("left 25%", slice(0, W // 4)), ("center 50%", slice(W // 4, 3 * W // 4)), ("right 25%", slice(3 * W // 4, W))):
        v = top[sl]
        print(f"    {name}: median top={np.nanmedian(v):.0f} ({(np.nanmedian(v))/H*100:.1f}%H), "
              f"canopy coverage={darkgreen[:, sl].mean()*100:.0f}%")
    return top


def resample(profile, n):
    idx = np.arange(n) * (len(profile) - 1) / (n - 1)
    lo = np.floor(idx).astype(int)
    hi = np.minimum(lo + 1, len(profile) - 1)
    w = idx - lo
    v = profile
    interp = v[lo] * (1 - w) + v[hi] * w
    return interp


def main():
    im1, a1 = load(P1)
    im2, a2 = load(P2)
    profiles = {}
    horizons = {}
    results = {}
    for tag, im, a in (("IMG1(pre-fix cubemap)", im1, a1), ("IMG2(post-fix panoramic)", im2, a2)):
        H, W, _ = a.shape
        print(f"\n===== {tag}: {W} x {H}, aspect {W/H:.3f} =====")
        gray = a.mean(2)
        r, g, b = a[..., 0], a[..., 1], a[..., 2]
        sky, y0, y1 = sky_region(gray, g, r, b)
        print(f"  sky-dominant rows: {y0}..{y1} ({y0/H*100:.1f}%..{y1/H*100:.1f}%H), "
              f"mean sky frac in band={sky[y0:y1+1].mean():.2f}")
        colmean = gray[y0:y1 + 1].mean(0)
        D = np.abs(np.diff(colmean))
        base = med_filter(D, 81)
        score = D - base
        order = np.argsort(score)[::-1][:8]
        print("  top vertical-boundary spikes (boundary between col x and x+1):")
        for idx in order:
            print(f"    x={idx} ({idx/W*100:.2f}%W) D={D[idx]:.2f} base={base[idx]:.2f} score={score[idx]:.2f}")
        # characterize the strongest two separated spikes
        used = []
        for idx in order:
            if any(abs(int(idx) - u) < 20 for u in used):
                continue
            used.append(int(idx))
            if len(used) > 2:
                break
        dom = None
        for idx in used:
            is_step, val, ys = characterize(a, gray, int(idx), y0, y1, W, H, tag)
            if dom is None:
                dom = (int(idx), is_step, val, ys)
        if dom:
            print("  cloud-cut check at dominant boundary:")
            cloud_cut(gray, dom[0], y0, y1, W, H, dom[2], dom[3], dom[1])
            diag_scan(gray, y0, y1, W, dom[0], tag)
        top = composition(a, gray, g, r, b, H, W, tag)
        profiles[tag] = top
        horizons[tag] = y1
        results[tag] = (W, H)
    # cross-compare
    tags = list(profiles.keys())
    t1, t2 = tags
    n = 400
    p1 = resample(np.nan_to_num(profiles[t1], nan=np.nanmedian(profiles[t1])), n)
    p2 = resample(np.nan_to_num(profiles[t2], nan=np.nanmedian(profiles[t2])), n)
    cc = np.corrcoef(p1, p2)[0, 1]
    print(f"\n===== CROSS COMPARISON =====")
    print(f"  resolutions: {t1}={results[t1]}, {t2}={results[t2]}")
    print(f"  tree-top profile correlation (resampled 400 bins): r={cc:.3f}")
    print(f"  sky bottom row (visible horizon gap): {t1}={horizons[t1]} ({horizons[t1]/results[t1][1]*100:.1f}%), "
          f"{t2}={horizons[t2]} ({horizons[t2]/results[t2][1]*100:.1f}%)")

    # save annotated + crops
    for tag, im, path in ((t1, im1, P1), (t2, im2, P2)):
        W, H = im.size
        a = np.asarray(im).astype(np.float32)
        gray = a.mean(2)
        r, g, b = a[..., 0], a[..., 1], a[..., 2]
        sky, y0, y1 = sky_region(gray, g, r, b)
        colmean = gray[y0:y1 + 1].mean(0)
        D = np.abs(np.diff(colmean))
        base = med_filter(D, 81)
        x0 = int(np.argmax(D - base))
        arr = np.asarray(im).copy()
        arr[:, max(0, x0 - 1):x0 + 2] = [255, 0, 0]
        marked = Image.fromarray(arr)
        marked.thumbnail((800, 800))
        marked.save(os.path.join(OUT, f"{tag.split('(')[0]}_marked.png"))
        box = (max(0, x0 - 90), 0, min(W, x0 + 90), y1 + 20)
        crop = im.crop(box)
        crop = crop.resize((crop.width * 3, crop.height * 3), Image.NEAREST)
        crop.save(os.path.join(OUT, f"{tag.split('(')[0]}_zoom.png"))
        print(f"  saved crops for {tag}: x0={x0} ({x0/W*100:.2f}%)")


if __name__ == "__main__":
    main()
