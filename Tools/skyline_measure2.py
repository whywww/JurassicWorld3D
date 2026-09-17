# -*- coding: utf-8 -*-
"""Refined sky-only line artifact measurement for two Unity screenshots."""
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


def sky_mask(a):
    r, g, b = a[..., 0], a[..., 1], a[..., 2]
    gray = a.mean(2)
    return gray, (gray > 145) & ~((g > r + 12) & (g > b + 12))


def boundary_stats(gray, sky, x, H, W):
    L = gray[:, x - 3:x].mean(1)
    R = gray[:, x + 1:x + 4].mean(1)
    valid = sky[:, x - 3:x + 4].all(1)
    step = (R - L)[valid]
    n = len(step)
    if n < 15:
        return None
    med = np.median(step)
    strong = step[np.abs(step) > 1.5]
    sc = max((strong > 0).mean(), (strong < 0).mean()) * 100 if len(strong) else 0
    return dict(n=n, med=med, medabs=np.median(np.abs(step)),
                meanabs=np.mean(np.abs(step)), p90=np.percentile(np.abs(step), 90),
                signcons=sc, valid=valid, raw=step)


def line_stats(gray, sky, x, H, W):
    C = gray[:, x - 1:x + 2].mean(1)
    SH = (gray[:, max(0, x - 5):max(0, x - 2)].mean(1) + gray[:, x + 2:x + 5].mean(1)) / 2
    valid = sky[:, max(0, x - 5):x + 5].all(1)
    val = (C - SH)[valid]
    if len(val) < 15:
        return None
    return dict(n=len(val), med=np.median(val), p90=np.percentile(np.abs(val), 90),
                meanabs=np.mean(np.abs(val)))


def analyze(path, tag, results):
    im, a = load(path)
    H, W, _ = a.shape
    gray, sky = sky_mask(a)
    r, g, b = a[..., 0], a[..., 1], a[..., 2]
    print(f"\n########## {tag}: {W}x{H} ##########")
    skyfrac = sky.mean(0)
    open_sky_rows = np.where(sky.mean(1) > 0.5)[0]
    band_y0, band_y1 = int(open_sky_rows.min()), int(open_sky_rows.max())
    loose = np.where(skyfrac > 0.15)[0]
    print(f"open-sky row band (row sky-frac>0.5): {band_y0}..{band_y1} ({band_y1/H*100:.1f}%H); "
          f"loose band to {loose.max()} ({loose.max()/H*100:.1f}%H)")

    rows = []
    for x in range(6, W - 6):
        st = boundary_stats(gray, sky, x, H, W)
        if st is None:
            continue
        ln = line_stats(gray, sky, x, H, W)
        score = abs(st["med"]) * (st["signcons"] / 100.0)
        rows.append((score, x, st, ln))
    rows.sort(reverse=True, key=lambda t: t[0])
    print("top sky-only vertical boundary candidates (step model, sorted by |median| x signconsistency):")
    for sc, x, st, ln in rows[:8]:
        lns = f"line(med)={ln['med']:+.1f} p90={ln['p90']:.1f}" if ln else "line(n/a)"
        print(f"  x={x} ({x/W*100:.2f}%W)  step: med={st['med']:+.2f} medabs={st['medabs']:.2f} "
              f"meanabs={st['meanabs']:.2f} p90={st['p90']:.2f} signcons={st['signcons']:.0f}% n={st['n']}  {lns}")
    print("top candidates by line-spike model (|median center-vs-shoulder|):")
    lrows = sorted([t for t in rows if t[3]], key=lambda t: abs(t[3]["med"]), reverse=True)[:5]
    for sc, x, st, ln in lrows:
        print(f"  x={x} ({x/W*100:.2f}%W)  line: med={ln['med']:+.2f} p90={ln['p90']:.2f} meanabs={ln['meanabs']:.2f}  "
              f"boundary-step med={st['med']:+.2f} signcons={st['signcons']:.0f}%")

    # best sky boundary = max |med step|
    best = max(rows, key=lambda t: abs(t[2]["med"]))
    sc, x0, st, ln = best
    print(f"\n==> BEST sky vertical boundary: x={x0} ({x0/W*100:.2f}%W), median step={st['med']:+.2f}")
    colmean_sky = np.array([gray[sky[:, x], x].mean() if sky[:, x].any() else np.nan for x in range(W)])
    prof = colmean_sky[max(0, x0 - 10):x0 + 11]
    print(f"  sky-only column-mean profile around x0-10..x0+10:")
    print("    " + " ".join(f"{v:6.1f}" for v in prof))
    valid = st["valid"]
    ysv = np.where(valid)[0]
    step_v = st["raw"]
    thr = max(2.0, 0.5 * st["p90"])
    sig = np.abs(step_v) > thr
    if sig.any():
        ys = ysv[sig]
        span = (ys.max() - ys.min() + 1) / H * 100
        sgn = np.sign(step_v[sig])
        cons = max((sgn > 0).mean(), (sgn < 0).mean()) * 100
        print(f"  vertical extent of anomaly (|step|>{thr:.1f}): y={ys.min()}..{ys.max()} "
              f"({ys.min()/H*100:.1f}%..{ys.max()/H*100:.1f}%H, span={span:.1f}%H), rows={sig.sum()}, "
              f"sign consistency={cons:.0f}%, max|step|={np.max(np.abs(step_v)):.1f}, median|step| in extent={np.median(np.abs(step_v[sig])):.1f}")
    else:
        print("  no rows exceed significance threshold")
    # per-channel in extent
    for chname, arr in (("R", a[..., 0]), ("G", a[..., 1]), ("B", a[..., 2])):
        L = arr[:, x0 - 3:x0].mean(1)
        Rc = arr[:, x0 + 1:x0 + 4].mean(1)
        d = (Rc - L)[valid]
        print(f"    {chname}: median step={np.median(d):+.2f}, max|step|={np.max(np.abs(d)):.1f}")

    # cloud-cut: ambient brightness along the line vs step
    xl, xr = max(0, x0 - 40), min(W, x0 + 41)
    amb = np.array([gray[y, xl:xr][sky[y, xl:xr]].mean() if sky[y, xl:xr].any() else np.nan for y in range(H)])
    amb = med_filter(np.nan_to_num(amb, nan=np.nanmean(amb)), 7)
    a_v, s_v, y_v = amb[ysv], step_v, ysv
    med_amb = np.median(a_v)
    cloud = a_v > med_amb
    corr = np.corrcoef(a_v, np.abs(s_v))[0, 1]
    print(f"  cloud-cut check: ambient median={med_amb:.0f}; corr(ambient, |step|)={corr:+.2f}")
    for nm, m in (("brighter half (cloud-side)", cloud), ("darker half (clear-sky side)", ~cloud)):
        if m.sum():
            print(f"    {nm}: n={m.sum()}, median|step|={np.median(np.abs(s_v[m])):.2f}, "
                  f"median signed={np.median(s_v[m]):+.2f}, max|step|={np.max(np.abs(s_v[m])):.1f}")
    if sig.any():
        print("  row-by-row (y, ambient, step) inside extent:")
        for y, av, sv in zip(ys, amb[ys], step_v[sig]):
            bar = "#" * int(min(30, abs(sv) / 4))
            print(f"    y={y:3d} ({y/H*100:4.1f}%)  amb={av:5.0f}  step={sv:+7.1f} {bar}")

    # diagonal scan on sky-sky edges, excluding vertical line
    e = np.abs(np.diff(gray, axis=1))
    pair_ok = sky[:, :-1] & sky[:, 1:]
    e[~pair_ok] = 0.0
    lo, hi = max(0, x0 - 12), min(e.shape[1], x0 + 14)
    e2 = e.copy()
    e2[:, lo:hi] = 0.0
    Hh, Ww = e2.shape
    yy = np.arange(Hh)[:, None]
    cc = np.arange(Ww)[None, :]
    cand = []
    for th in np.arange(-40, 40.01, 1.0):
        t = np.tan(np.radians(th))
        xi = np.clip(np.round(cc + t * yy).astype(np.int32), 0, Ww - 1)
        vals = e2[np.arange(Hh)[:, None], xi]
        skies = pair_ok[np.arange(Hh)[:, None], xi]
        sk_n = skies.sum(0)
        ok_c = sk_n >= 30
        m = np.where(ok_c, vals.mean(0), 0)
        frac = np.where(ok_c, (vals > 4).sum(0) / np.maximum(sk_n, 1), 0)
        c = int(np.argmax(m))
        cand.append((m[c], c, th, frac[c], int(sk_n[c])))
    cand.sort(reverse=True)
    kept = []
    for scv, c, th, fr, skn in cand:
        if fr < 0.35:
            continue
        if any(abs(c - k[1]) < 50 and abs(th - k[2]) < 8 for k in kept):
            continue
        kept.append((scv, c, th, fr, skn))
        if len(kept) >= 4:
            break
    print(f"  diagonal scan on sky-sky edges (rows 0..{Hh-1}, excluding x0+/-12):")
    for scv, c, th, fr, skn in kept:
        xtop, ytop = c, 0
        xbot, ybot = int(round(c + np.tan(np.radians(th)) * (Hh - 1))), Hh - 1
        print(f"    angle={th:+.1f}deg from vertical (sign + = leans right going down): "
              f"({xtop},{ytop})->({xbot},{ybot}) i.e. {xtop/W*100:.1f}%W->{xbot/W*100:.1f}%W, "
              f"mean edge={scv:.2f}, coverage={fr*100:.0f}%, sky-rows={skn}")
    if not kept:
        print("    none above coverage threshold")

    results[tag] = dict(W=W, H=H, x0=x0)
    # crops and annotated images
    arr = np.asarray(im).copy()
    arr[:, max(0, x0 - 1):x0 + 2] = [255, 0, 0]
    imc = Image.fromarray(arr)
    imc.thumbnail((800, 800))
    imc.save(os.path.join(OUT, f"{tag}_marked2.png"))
    for nm, box, scale in (
        ("line", (max(0, x0 - 45), 0, min(W, x0 + 45), min(H, ysv.max() + 15 if len(ysv) else H)), 4),
        ("topleft", (0, 0, min(W, 100), min(H, 90)), 4),
    ):
        crop = im.crop(box)
        crop = crop.resize((crop.width * scale, crop.height * scale), Image.NEAREST)
        crop.save(os.path.join(OUT, f"{tag}_{nm}.png"))
    # grid layout comparison
    gy, gx = 8, 6
    r_, g_, b_ = a[..., 0], a[..., 1], a[..., 2]
    cloudish = sky & (b_ - r_ < 35) & (gray > 160)
    darkgreen = (g_ > r_ + 8) & (g_ > b_ + 8) & (gray < 150)
    print("  layout grid (8 rows x 6 cols): cloud-sky % / dark-green %")
    for i in range(gy):
        y0_, y1_ = i * H // gy, (i + 1) * H // gy
        row_c, row_g = [], []
        for j in range(gx):
            x0_, x1_ = j * W // gx, (j + 1) * W // gx
            cell = slice(y0_, y1_), slice(x0_, x1_)
            row_c.append(f"{cloudish[cell].mean()*100:4.0f}")
            row_g.append(f"{darkgreen[cell].mean()*100:4.0f}")
        print("    clouds: " + " ".join(row_c) + "   canopy: " + " ".join(row_g))
    return results


def main():
    results = {}
    im1, a1 = load(P1)
    im2, a2 = load(P2)
    analyze(P1, "IMG1_cubemap", results)
    analyze(P2, "IMG2_panoramic", results)
    print("\n########## CROSS SUMMARY ##########")
    for t, d in results.items():
        print(f"  {t}: {d['W']}x{d['H']}, best sky vertical boundary at x={d['x0']} ({d['x0']/d['W']*100:.2f}%W)")


if __name__ == "__main__":
    main()
